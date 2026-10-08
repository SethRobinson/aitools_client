#Requires -Version 7.0
# Offline checks against disposable fixtures only. Requires PowerShell 7 on Windows.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$verificationRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixtureRoot = Join-Path $verificationRepo ('build\delete-safety-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
Add-Type -Path (Join-Path $verificationRepo 'Assets\RT\RTSafeFileSystem.cs')
$script:checks = 0

function Assert-Check([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}
function Assert-Rejected([scriptblock] $Action, [string] $Message) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    Assert-Check $rejected $Message
}
function Write-Fixture([string] $Path) {
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($Path)) -Force | Out-Null
    [IO.File]::WriteAllText($Path, 'disposable safety fixture')
}
function New-BuildFixture([string] $Name) {
    $root = Join-Path $fixtureRoot $Name
    Write-Fixture (Join-Path $root 'ProjectSettings\ProjectVersion.txt')
    Write-Fixture (Join-Path $root 'Assets\_Script\GameLogic.cs')
    New-Item -ItemType Directory -Path (Join-Path $root 'scripts') | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CleanBuildOutput.ps1') -Destination (Join-Path $root 'scripts\CleanBuildOutput.ps1')
    return $root
}
function Run-BuildCleanup([string] $Root, [string] $Phase, [bool] $ShouldSucceed) {
    # Exercise the same Windows PowerShell executable used by BuildWin64.bat.
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root 'scripts\CleanBuildOutput.ps1') -Phase $Phase 2>&1
    Assert-Check (($LASTEXITCODE -eq 0) -eq $ShouldSucceed) ("Build cleanup result mismatch: " + ($output -join "`n"))
}
function Run-UploadValidation([hashtable] $Settings, [bool] $ShouldSucceed) {
    $start = [Diagnostics.ProcessStartInfo]::new('powershell.exe')
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($arg in @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'ValidateWebGLUpload.ps1'))) {
        $start.ArgumentList.Add($arg)
    }
    foreach ($name in $Settings.Keys) { $start.Environment[$name] = $Settings[$name] }
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    Assert-Check (($process.ExitCode -eq 0) -eq $ShouldSucceed) ("Upload validation mismatch: $stdout $stderr")
    $process.Dispose()
}

$elsewhere = Join-Path $fixtureRoot 'unrelated-working-directory'
Write-Fixture (Join-Path $elsewhere 'build\win\keep.txt')
Write-Fixture (Join-Path $elsewhere 'tempCache\keep.txt')
Push-Location $elsewhere
try {
    $reset = New-BuildFixture 'reset'
    Write-Fixture (Join-Path $reset 'build\win\nested\old.txt')
    Write-Fixture (Join-Path $reset 'build\other\keep.txt')
    Run-BuildCleanup $reset Reset $true
    Assert-Check (Test-Path -LiteralPath (Join-Path $reset 'build\win') -PathType Container) 'Reset did not recreate build\win.'
    Assert-Check (-not (Test-Path -LiteralPath (Join-Path $reset 'build\win\nested'))) 'Reset left old output.'
    Assert-Check (Test-Path -LiteralPath (Join-Path $reset 'build\other\keep.txt')) 'Reset touched a sibling.'
    Assert-Check (Test-Path -LiteralPath (Join-Path $elsewhere 'build\win\keep.txt')) 'Reset used the working directory.'
    Run-BuildCleanup $reset Package $true # Absent optional files/directories are harmless.

    $settings = @{ WEB_SUB_DIR = 'aitools'; APP_NAME = 'aitools_client'; _FTP_USER_ = 'uploader'; _FTP_SITE_ = 'example.com'; BUILDMODE = 'BETA' }
    Run-UploadValidation $settings $true
    foreach ($name in @('WEB_SUB_DIR', 'APP_NAME', '_FTP_USER_', '_FTP_SITE_', 'BUILDMODE')) {
        foreach ($bad in @('', ' ', '../outside', '*', 'bad"value', 'bad;value')) {
            $invalidSettings = $settings.Clone()
            $invalidSettings[$name] = $bad
            Run-UploadValidation $invalidSettings $false
        }
    }

    # A real cmd run with a failing fake Unity command must stop before copying/signing.
    $batchContainer = Join-Path $fixtureRoot 'batch-with-spaces'
    $batch = New-BuildFixture 'batch-with-spaces\project with spaces'
    foreach ($script in @('BuildWin64.bat', 'app_info_setup.bat')) {
        Copy-Item -LiteralPath (Join-Path $verificationRepo $script) -Destination (Join-Path $batch $script)
    }
    [IO.File]::WriteAllText((Join-Path $batchContainer 'base_setup.bat'), "@echo off`r`nset UNITY_EXE=cmd /c exit 9`r`nset RT_UTIL=unused`r`nset RT_PROJECTS=unused`r`nexit /b 0`r`n")
    [IO.File]::WriteAllText((Join-Path $batch 'UpdateBuildDirConfigFiles.bat'), "@echo off`r`necho failure>unexpected-copy.txt`r`nexit /b 0`r`n")
    Write-Fixture (Join-Path $batch 'build\win\old.txt')
    $start = [Diagnostics.ProcessStartInfo]::new('cmd.exe')
    $start.UseShellExecute = $false
    $start.WorkingDirectory = $elsewhere
    $start.Environment['NO_PAUSE'] = '1'
    $start.Arguments = '/d /c ""' + (Join-Path $batch 'BuildWin64.bat') + '""'
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    Assert-Check ($process.ExitCode -ne 0) 'Build ignored the Unity failure.'
    Assert-Check ($stdout.Contains('Building project...')) ("Batch did not reach the fake Unity command: $stdout $stderr")
    Assert-Check (-not (Test-Path -LiteralPath (Join-Path $batch 'unexpected-copy.txt'))) 'Build continued packaging after failure.'
    Assert-Check (Test-Path -LiteralPath (Join-Path $elsewhere 'build\win\keep.txt')) 'Batch cleanup used its caller directory.'
    $process.Dispose()

    $package = New-BuildFixture 'package'
    $removed = @('Adventure\testing.txt', 'AIGuide\TESTdraft.txt', 'ComfyUI\testing.json',
        'ComfyUI\workflow\testing.json', 'ComfyUI\example_cached_api_version.json', 'Presets\TESTdraft.txt', 'aichat\skills\local_fixture.md',
        'config.txt', 'config_llm.txt', 'config_preferences.txt', 'utils\RTClip.zip',
        'ComfyUI\Unused\old.txt', "Seth's AI Tools_BurstDebugInformation_DoNotShip\old.txt",
        "Seth's AI Tools_BackUpThisFolder_ButDontShipItWithYourGame\old.txt",
        'aitools_client_BackUpThisFolder_ButDontShipItWithYourGame\old.txt',
        'aitools_client_BurstDebugInformation_DoNotShip\old.txt',
        'autosave\old.txt', 'tempCache\old.txt')
    foreach ($file in $removed) { Write-Fixture (Join-Path $package ('build\win\' + $file)) }
    Write-Fixture (Join-Path $package 'build\win\Presets\keep.txt')
    Write-Fixture (Join-Path $package 'build\win\aichat\skills\keep.md')
    Run-BuildCleanup $package Package $true
    foreach ($file in $removed) {
        Assert-Check (-not (Test-Path -LiteralPath (Join-Path $package ('build\win\' + $file)))) "Package left $file"
    }
    Assert-Check (Test-Path -LiteralPath (Join-Path $package 'build\win\Presets\keep.txt')) 'Package removed a production preset.'
    Assert-Check (Test-Path -LiteralPath (Join-Path $package 'build\win\aichat\skills\keep.md')) 'Package removed a production skill.'

    $invalid = Join-Path $fixtureRoot 'not-a-project'
    New-Item -ItemType Directory -Path (Join-Path $invalid 'scripts') | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CleanBuildOutput.ps1') -Destination (Join-Path $invalid 'scripts\CleanBuildOutput.ps1')
    Write-Fixture (Join-Path $invalid 'build\win\keep.txt')
    Run-BuildCleanup $invalid Reset $false
    Assert-Check (Test-Path -LiteralPath (Join-Path $invalid 'build\win\keep.txt')) 'Invalid project was deleted.'

    $outside = Join-Path $fixtureRoot 'outside'
    Write-Fixture (Join-Path $outside 'keep.txt')
    $linked = New-BuildFixture 'linked-build'
    New-Item -ItemType Junction -Path (Join-Path $linked 'build') -Target $outside | Out-Null
    Run-BuildCleanup $linked Reset $false
    $nested = New-BuildFixture 'nested-link'
    Write-Fixture (Join-Path $nested 'build\win\keep.txt')
    New-Item -ItemType Junction -Path (Join-Path $nested 'build\win\linked') -Target $outside | Out-Null
    Run-BuildCleanup $nested Reset $false
    Run-BuildCleanup $nested Package $false
    Assert-Check (Test-Path -LiteralPath (Join-Path $nested 'build\win\keep.txt')) 'Link preflight deleted files before refusing.'

    foreach ($invalidRoot in @('', ' ', 'relative', '\root-relative', 'C:drive-relative', [IO.Path]::GetPathRoot($fixtureRoot), "$fixtureRoot\..", "$fixtureRoot\*")) {
        Assert-Rejected { [RTSafeFileSystem]::DeleteTempCache($invalidRoot) } "Accepted unsafe cache root: '$invalidRoot'"
    }
    $runtime = Join-Path $fixtureRoot 'runtime'
    Write-Fixture (Join-Path $runtime 'Assets\marker.txt')
    Write-Fixture (Join-Path $runtime 'tempCache\nested\old.txt')
    Write-Fixture (Join-Path $runtime 'keep.txt')
    Assert-Check ([RTSafeFileSystem]::GetAppRoot((Join-Path $runtime 'Assets')) -eq $runtime) 'Incorrect Unity app root.'
    [RTSafeFileSystem]::DeleteTempCache($runtime)
    [RTSafeFileSystem]::DeleteTempCache($runtime) # Missing cache is a no-op.
    Assert-Check (-not (Test-Path -LiteralPath (Join-Path $runtime 'tempCache'))) 'Runtime cache was not deleted.'
    Assert-Check (Test-Path -LiteralPath (Join-Path $runtime 'keep.txt')) 'Runtime cleanup escaped tempCache.'
    Assert-Check (Test-Path -LiteralPath (Join-Path $elsewhere 'tempCache\keep.txt')) 'Runtime cleanup used the working directory.'
    New-Item -ItemType Junction -Path (Join-Path $runtime 'tempCache') -Target $outside | Out-Null
    Assert-Rejected { [RTSafeFileSystem]::DeleteTempCache($runtime) } 'Accepted a cache junction.'
    $runtimeNested = Join-Path $fixtureRoot 'runtime-nested'
    Write-Fixture (Join-Path $runtimeNested 'tempCache\keep.txt')
    New-Item -ItemType Junction -Path (Join-Path $runtimeNested 'tempCache\linked') -Target $outside | Out-Null
    Assert-Rejected { [RTSafeFileSystem]::DeleteTempCache($runtimeNested) } 'Accepted a nested cache junction.'
    Assert-Check (Test-Path -LiteralPath (Join-Path $runtimeNested 'tempCache\keep.txt')) 'Cache preflight partially deleted the tree.'

    $downloads = Join-Path $fixtureRoot 'downloads'
    $downloadDir = Join-Path $downloads 'tempCache\aichat_web_videos'
    foreach ($name in @('ytdlp_ab12cd34.mp4', 'ytdlp_ab12cd34.f137.mp4.part', 'ytdlp_ab12cd345.mp4', 'ytdlp_deadbeef.mp4', 'keep.txt')) {
        Write-Fixture (Join-Path $downloadDir $name)
    }
    foreach ($stem in @('', ' ', '*', 'ytdlp_*', 'ytdlp_', '../ytdlp_ab12cd34', 'ytdlp_ab12cd34*')) {
        Assert-Rejected { [RTSafeFileSystem]::DeleteYtDlpPartials($downloads, $downloadDir, $stem) } "Accepted unsafe download stem: '$stem'"
    }
    Assert-Rejected { [RTSafeFileSystem]::DeleteYtDlpPartials($downloads, $outside, 'ytdlp_ab12cd34') } 'Accepted unrelated download directory.'
    [RTSafeFileSystem]::DeleteYtDlpPartials($downloads, $downloadDir, 'ytdlp_ab12cd34')
    Assert-Check (-not (Test-Path -LiteralPath (Join-Path $downloadDir 'ytdlp_ab12cd34.mp4'))) 'Download was not cleaned.'
    Assert-Check (-not (Test-Path -LiteralPath (Join-Path $downloadDir 'ytdlp_ab12cd34.f137.mp4.part'))) 'Partial fragment was not cleaned.'
    foreach ($name in @('ytdlp_ab12cd345.mp4', 'ytdlp_deadbeef.mp4', 'keep.txt')) {
        Assert-Check (Test-Path -LiteralPath (Join-Path $downloadDir $name)) 'Download cleanup exceeded the exact stem.'
    }
    Assert-Check (Test-Path -LiteralPath (Join-Path $outside 'keep.txt')) 'Junction target was touched.'
} finally { Pop-Location }

Write-Output "PASS: $script:checks cleanup safety checks. Fixtures retained at $fixtureRoot"
