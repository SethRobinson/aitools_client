# Adversarial ZIP path fixtures only. No player, desktop capture or input is started.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture = Join-Path $repo ('build\smoke-guard-check-' + [Guid]::NewGuid().ToString('N'))
for ($cursor = $fixture; $cursor; $cursor = [IO.Path]::GetDirectoryName($cursor)) {
    if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked fixture path.' }
}
New-Item -ItemType Directory -Path $fixture | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$cases = @(
    @{ Name = 'traversal'; Entries = @('aitools_client/../escaped.txt') },
    @{ Name = 'wrong-root'; Entries = @('other/file.txt') },
    @{ Name = 'duplicate'; Entries = @('aitools_client/a.txt', 'aitools_client/A.txt') },
    @{ Name = 'ads'; Entries = @('aitools_client/a.txt:stream') },
    @{ Name = 'windows-alias'; Entries = @('aitools_client/.. /escaped.txt') },
    @{ Name = 'device'; Entries = @('aitools_client/NUL.txt') },
    @{ Name = 'symlink'; Entries = @('aitools_client/link') }
)
foreach ($case in $cases) {
    $path = Join-Path $fixture ($case.Name + '.zip')
    $zip = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in $case.Entries) {
            $entry = $zip.CreateEntry($name)
            if ($case.Name -eq 'symlink') { $entry.ExternalAttributes = -1610612736 }
            $writer = [IO.StreamWriter]::new($entry.Open())
            try { $writer.Write('synthetic fixture') } finally { $writer.Dispose() }
        }
    } finally { $zip.Dispose() }
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'SmokeReleaseZip.ps1') -ArchivePath $path 2>&1
    if ($LASTEXITCODE -eq 0 -or ($output -join "`n") -notmatch 'Unsafe|duplicate') { throw "ZIP guard failed for $($case.Name): $output" }
}
Write-Output "PASS: $($cases.Count) release smoke ZIP guards. Fixtures: $fixture"
