$ErrorActionPreference = 'Stop'
$status = Invoke-RestMethod 'http://127.0.0.1:8772/status' -TimeoutSec 10
$expectedRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$status.playing -or !$status.driverReady -or $status.compileFailed -or
    ![string]::Equals([IO.Path]::GetFullPath($status.projectPath), $expectedRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Start a compiled Play session with the automation bridge in this checkout first.'
}
$result = Invoke-RestMethod 'http://127.0.0.1:8772/aichat_settings_validation' -Method Post -TimeoutSec 30
if (!$result.ok) { throw $result.error }
$result.checks | ForEach-Object { Write-Output "PASS: $_" }
Write-Output "Fixtures retained at $($result.fixtures)"
