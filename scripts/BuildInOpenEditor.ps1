# Builds through the loopback editor bridge. Does not click, capture or close the editor.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$status = Invoke-RestMethod -Uri 'http://127.0.0.1:8772/status' -TimeoutSec 5
if (-not $status.PSObject.Properties['editorProcessId'] -or -not $status.PSObject.Properties['projectPath']) {
    throw 'Refresh the editor scripts first; the build requires the current automation bridge.'
}
if (-not [string]::Equals([IO.Path]::GetFullPath($status.projectPath), $repo, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The editor bridge belongs to a different checkout. No output was cleaned.'
}
$editor = [Diagnostics.Process]::GetProcessById($status.editorProcessId)
if (-not $status.ok -or $status.playing -or $status.compiling -or $status.compileFailed -or $status.rebuilding) {
    throw 'The open editor must be stopped, compiled and idle before building a release.'
}
& (Join-Path $PSScriptRoot 'CleanBuildOutput.ps1') -Phase Reset
$requestId = [Guid]::NewGuid().ToString('N')
$response = Invoke-RestMethod -Uri 'http://127.0.0.1:8772/windows_release_build' -Method Post -Body $requestId -ContentType 'text/plain' -TimeoutSec 30
if (-not $response.ok) { throw "Editor refused release build: $($response.error)" }
$reportPath = Join-Path $repo 'build\release-build.json'
$watch = [Diagnostics.Stopwatch]::StartNew()
$lastState = ''
while ($watch.Elapsed.TotalMinutes -lt 30) {
    $report = $null
    # The editor replaces this small report at state changes; tolerate a partial read.
    try { if (Test-Path -LiteralPath $reportPath) { $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json } } catch { }
    if ($report -and $report.id -eq $requestId) {
        if ($lastState -ne $report.state) { Write-Output "Unity release: $($report.state)"; $lastState = $report.state }
        if ($report.state -eq 'failed') { throw "Unity release failed: $($report.error)" }
        if ($report.state -eq 'built') {
            if ($report.errors -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $repo 'build\win\aitools_client.exe'))) { throw 'Invalid release build result.' }
            Write-Output "Unity release built with $($report.warnings) warnings."
            exit 0
        }
    }
    if ($editor.HasExited) { throw 'The editor exited before completing the release build. Packaging stopped.' }
    Start-Sleep -Seconds 2
}
throw 'Timed out waiting for the open editor release build. Packaging stopped.'
