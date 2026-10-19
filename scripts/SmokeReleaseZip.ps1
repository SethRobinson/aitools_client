[CmdletBinding()]
param([string] $ArchivePath, [switch] $CheckMissingRecorderDll)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not (Test-Path -LiteralPath (Join-Path $repo 'ProjectSettings\ProjectVersion.txt'))) { throw 'Run this script from the AI Tools checkout.' }
if (-not $PSBoundParameters.ContainsKey('ArchivePath')) { $ArchivePath = Join-Path $repo 'SethsAIToolsWindows.zip' }
if ([string]::IsNullOrWhiteSpace($ArchivePath) -or -not [IO.Path]::IsPathRooted($ArchivePath)) { throw 'ArchivePath must be an absolute ZIP filename.' }
function Assert-NoLinks([string] $Path) {
    for ($cursor = [IO.Path]::GetFullPath($Path); $cursor; $cursor = [IO.Path]::GetDirectoryName($cursor)) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing linked smoke-check path: $cursor"
        }
    }
}
Assert-NoLinks $ArchivePath
$destination = Join-Path $repo ('build\package-smoke-' + [Guid]::NewGuid().ToString('N'))
Assert-NoLinks $destination
if (Test-Path -LiteralPath $destination) { throw 'Smoke directory must be fresh.' }
New-Item -ItemType Directory -Path $destination | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
$seen = @{}
$files = 0
try {
    # Preflight every path before writing any archive content. No traversal, ADS,
    # duplicate entries, or symlinks, even when checking a separately supplied ZIP.
    foreach ($entry in $archive.Entries) {
        $name = $entry.FullName.Replace('\', '/')
        if (-not $name.StartsWith('aitools_client/', [StringComparison]::Ordinal) -or
            $name -match '(^|/)\.{1,2}(/|$)|:|//|[\x00-\x1f]' -or
            (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000 -or
            ($entry.ExternalAttributes -band 0x400) -ne 0 -or $seen.ContainsKey($name)) { throw "Unsafe or duplicate ZIP entry: $name" }
        foreach ($part in $name.TrimEnd('/').Split('/')) {
            if ($part -ne $part.TrimEnd(' ', '.') -or $part -match '^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])($|\.)') { throw "Unsafe Windows ZIP component: $part" }
        }
        $seen[$name] = $true
        $target = [IO.Path]::GetFullPath((Join-Path $destination $name))
        if (-not $target.StartsWith($destination + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "ZIP entry escapes smoke directory: $name" }
    }
    foreach ($entry in $archive.Entries) {
        $target = Join-Path $destination $entry.FullName.Replace('/', '\')
        Assert-NoLinks $target
        if (-not $entry.Name) { New-Item -ItemType Directory -Path $target -Force | Out-Null; continue }
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $false)
        $files++
    }
} finally { $archive.Dispose() }
$app = Join-Path $destination 'aitools_client'
$executable = Join-Path $app 'aitools_client.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'ZIP has no Unity player.' }
Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;
public static class PackageSmokeNative {
    [DllImport("kernel32.dll")]
    public static extern uint SetErrorMode(uint mode);
}
'@
function Invoke-SmokePlayer([string] $LogFile) {
    $start = [Diagnostics.ProcessStartInfo]::new($executable, "-batchmode -nographics -package-smoke-check -logFile $LogFile")
    $start.WorkingDirectory = $app
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    # Do not accidentally satisfy dependencies from developer tools or the checkout.
    $start.EnvironmentVariables['PATH'] = (Join-Path $env:SystemRoot 'System32') + ';' + $env:SystemRoot
    foreach ($variable in @('MONO_PATH', 'MONO_GAC_PREFIX', 'DOTNET_ADDITIONAL_DEPS', 'DOTNET_SHARED_STORE', 'DOTNET_STARTUP_HOOKS')) { $start.EnvironmentVariables.Remove($variable) }
    # Suppress loader/crash dialogs even if a missing player DLL prevents managed
    # startup. The child inherits this mode; restore the harness mode immediately.
    $previousMode = [PackageSmokeNative]::SetErrorMode(0x8003)
    try { $process = [Diagnostics.Process]::Start($start) }
    finally { [void][PackageSmokeNative]::SetErrorMode($previousMode) }
    try {
        if (-not $process.WaitForExit(180000)) { $process.Kill(); $process.WaitForExit(); throw "Package smoke timed out. Evidence: $destination" }
        return $process.ExitCode
    } finally { $process.Dispose() }
}
$exitCode = Invoke-SmokePlayer 'package-smoke-player.log'
$reportPath = Join-Path $app 'package-smoke\report.json'
if (-not (Test-Path -LiteralPath $reportPath)) { throw "Player exited $exitCode without a smoke report. Check $app\package-smoke-player.log" }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
foreach ($check in $report.checks) { Write-Output "$($check.name): $(if ($check.passed) { 'PASS' } else { 'FAIL' }) - $($check.detail)" }
if ($exitCode -ne 0 -or $report.state -ne 'passed' -or @($report.checks | Where-Object { -not $_.passed }).Count -ne 0 -or $report.checks.Count -lt 7) {
    throw "Package smoke failed (exit $exitCode). Report: $reportPath"
}
foreach ($unexpected in @('config.txt', 'config_llm.txt', 'config_preferences.txt', 'tempCache', 'log.txt')) {
    if (Test-Path -LiteralPath (Join-Path $app $unexpected)) { throw "Smoke mode entered normal application startup: $unexpected" }
}
if ($CheckMissingRecorderDll) {
    # Only the new disposable extraction is modified. Preserve the healthy report
    # and DLL, and prove that a missing native recorder dependency fails the player.
    $dll = Join-Path $app 'utils\ScreenCapture\ScreenRecorderLib.dll'
    $parked = Join-Path $app 'utils\ScreenCapture\ScreenRecorderLib.dll.smoke-disabled'
    Assert-NoLinks $dll
    Assert-NoLinks $parked
    $healthy = Join-Path $app 'package-smoke\report-healthy.json'
    [IO.File]::Copy($reportPath, $healthy, $false)
    [IO.File]::Move($dll, $parked)
    try {
        $badExit = Invoke-SmokePlayer 'package-smoke-missing-dll.log'
        $badReport = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        $missing = @($badReport.checks | Where-Object { $_.name -eq 'Recorder native dependencies' -and -not $_.passed })
        if ($badExit -eq 0 -or $badReport.state -ne 'failed' -or $missing.Count -ne 1) { throw 'Smoke check did not detect the missing recorder DLL.' }
        [IO.File]::Copy($reportPath, (Join-Path $app 'package-smoke\report-missing-dll.json'), $false)
        Write-Output 'Missing recorder DLL failure detection: PASS'
    }
    finally {
        [IO.File]::Move($parked, $dll)
        [IO.File]::Copy($healthy, $reportPath, $true)
    }
}
$stream = [IO.File]::OpenRead($ArchivePath)
$hash = [Security.Cryptography.SHA256]::Create()
try { $archiveHash = [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '') }
finally { $stream.Dispose(); $hash.Dispose() }
$summary = [ordered]@{ state = 'passed'; archive = $ArchivePath; sha256 = $archiveHash; extractedFiles = $files; exitCode = $exitCode; missingRecorderDllDetected = $CheckMissingRecorderDll.IsPresent; report = $reportPath }
$summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'summary.json') -Encoding UTF8
Write-Output "PASS: fresh-ZIP command-line smoke check ($files extracted files). Evidence: $destination"
