# Synthetic media and pure logic only. Does not capture the desktop or audio devices.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& (Join-Path $PSScriptRoot 'BuildScreenCapture.ps1') -Check
& dotnet run --project (Join-Path $repo 'tools\ScreenCapture\Checks\Overlay\OverlayChecks.csproj')
if ($LASTEXITCODE -ne 0) { throw 'Screen recorder synthetic overlay checks failed.' }
& dotnet run --project (Join-Path $repo 'tools\ScreenCapture\Checks\Checks.csproj') -- $repo
if ($LASTEXITCODE -ne 0) { throw 'Screen recorder checks failed.' }
