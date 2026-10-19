[CmdletBinding()]
param([switch] $Check)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repo 'tools\ScreenCapture\ScreenCapture.csproj'
$runtime = Join-Path $repo 'utils\ScreenCapture'
function Get-RuntimeHash([string] $Path) {
    # The signing/build environment can replace PowerShell's module search path.
    # Get-FileHash is a module function; use .NET so this also works in that shell.
    $stream = [IO.File]::OpenRead($Path)
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $hash.Dispose() }
}
function Assert-NoLinks([string] $Path) {
    for ($cursor = [IO.Path]::GetFullPath($Path); $cursor; $cursor = [IO.Path]::GetDirectoryName($cursor)) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing a linked helper path: $cursor"
        }
    }
}
Assert-NoLinks $project
Assert-NoLinks $runtime
& dotnet restore $project --locked-mode --nologo
if ($LASTEXITCODE -ne 0) { throw 'Screen recorder dependency restore failed.' }
& dotnet build $project --no-restore -c Release --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Screen recorder build failed.' }
$output = Join-Path $repo 'tools\ScreenCapture\bin\x64\Release\net48'
Assert-NoLinks $output
New-Item -ItemType Directory -Path $runtime -Force | Out-Null
foreach ($file in @('AIToolsCapture.exe', 'AIToolsCapture.exe.config', 'ScreenRecorderLib.dll', 'ScreenRecorderLib.LICENSE.txt')) {
    $sourceFile = Join-Path $output $file
    $runtimeFile = Join-Path $runtime $file
    Assert-NoLinks $sourceFile
    Assert-NoLinks $runtimeFile
    # Unchanged native dependencies may still be loaded by a running helper.
    if ((Test-Path -LiteralPath $runtimeFile) -and
        (Get-RuntimeHash $sourceFile) -eq (Get-RuntimeHash $runtimeFile)) { continue }
    Copy-Item -LiteralPath $sourceFile -Destination $runtimeFile -Force
}
# App-local Microsoft runtimes are pinned, original vendor files. Never harvest
# machine DLLs or download replacements implicitly during a release build.
$dependencies = Get-Content -LiteralPath (Join-Path $runtime 'dependencies.json') -Raw | ConvertFrom-Json
foreach ($hdrInput in @(
    @{ Name = 'ScreenRecorderLib.patch'; Hash = $dependencies.screenRecorderLibPatch.patchSha256 },
    @{ Name = 'HdrToneMapper.h'; Hash = $dependencies.screenRecorderLibPatch.toneMapperSha256 }
)) {
    $path = Join-Path $repo ('tools\ScreenCapture\Hdr\' + $hdrInput.Name)
    Assert-NoLinks $path
    if ((Get-RuntimeHash $path) -ne $hdrInput.Hash) { throw 'HDR source changed: rebuild with scripts/BuildScreenCaptureHdr.ps1 -Stage before packaging.' }
}
foreach ($file in $dependencies.files) {
    if ($file.name -notmatch '^[A-Za-z0-9_.-]+$') { throw 'Invalid dependency manifest filename.' }
    $path = Join-Path $runtime $file.name
    Assert-NoLinks $path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-RuntimeHash $path) -ne $file.sha256) {
        throw "Screen recorder dependency is missing or changed: $($file.name)"
    }
}
if ($Check) {
    # --check only loads the library. It never displays UI, registers a shortcut,
    # reads desktop pixels, or opens an audio device.
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $runtime 'AIToolsCapture.exe'), '--check')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    try {
        if ($process.ExitCode -ne 0) { throw "Screen recorder dependency check failed: $stdout $stderr" }
        Write-Output $stdout.Trim()
    } finally { $process.Dispose() }
}
Write-Output 'Built and staged the Windows screen recorder.'
