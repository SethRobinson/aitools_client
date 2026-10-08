# Offline staging regression checks. Disposable fixtures are retained under build/.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture = Join-Path $repo ('build\package-check-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $fixture 'scripts') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ReleasePackage.ps1') -Destination (Join-Path $fixture 'scripts\ReleasePackage.ps1')
$checks = 0
function Check([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}
function Write-Fixture([string] $Name, [string] $Content = 'fixture') {
    $path = Join-Path $fixture $Name
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($path)) -Force | Out-Null
    [IO.File]::WriteAllText($path, $Content)
}
function Run-Stage([bool] $Success) {
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture 'scripts\ReleasePackage.ps1') -Phase Stage 2>&1
    Check (($LASTEXITCODE -eq 0) -eq $Success) ($output -join "`n")
}
Write-Fixture 'Assets/_Script/GameLogic.cs'
Write-Fixture 'ProjectSettings/ProjectVersion.txt'
foreach ($path in @(& git -C $repo -c core.quotepath=false ls-files utils web Adventure AIGuide ComfyUI Presets aichat Media cli config_cam.txt model_data.json README.md LICENSE.md)) {
    if ($path -match '\.json$') { Write-Fixture $path '{}' } else { Write-Fixture $path }
}
foreach ($path in @('config.txt', 'config_llm.txt', 'config_preferences.txt', 'cli/config.txt',
    'aichat/skills/local_fixture.md', 'aichat/test_prompt.txt', 'ComfyUI/sample_cached_api_version.json',
    'Presets/test_fixture.txt', 'cli/venv/private.txt', 'cli/__pycache__/private.pyc')) {
    Write-Fixture $path 'PRIVATE FIXTURE'
}
& git -C $fixture init --quiet
if ($LASTEXITCODE -ne 0) { throw 'Fixture git init failed.' }
& git -C $fixture add --all
if ($LASTEXITCODE -ne 0) { throw 'Fixture git add failed.' }
# Files not in the index must not slip into the package, even under a runtime directory.
Write-Fixture 'aichat/skills/untracked-private.md' 'PRIVATE FIXTURE'
Run-Stage $true
foreach ($path in @('cli/aitools_cli.py', 'cli/config.example.txt', 'LICENSE.md', 'utils/ffmpeg/licenses/LICENSE',
    'utils/yt-dlp/LICENSE', 'config_cam.txt', 'Presets/Prompt To Image (Qwen Image 2.1).txt')) {
    Check (Test-Path -LiteralPath (Join-Path $fixture ('build/win/' + $path))) "Missing $path"
}
foreach ($path in @('config.txt', 'config_llm.txt', 'config_preferences.txt', 'cli/config.txt',
    'aichat/skills/local_fixture.md', 'aichat/skills/untracked-private.md', 'aichat/test_prompt.txt',
    'ComfyUI/sample_cached_api_version.json', 'Presets/test_fixture.txt', 'cli/venv/private.txt',
    'cli/__pycache__/private.pyc', 'utils/RTClip.zip', 'cli/codex_imagegen.sh', 'Media',
    'Presets/Image To Image Inpaint (SDXL).txt')) {
    Check (-not (Test-Path -LiteralPath (Join-Path $fixture ('build/win/' + $path)))) "Leaked $path"
}
Write-Fixture 'Presets/Prompt To Image (Qwen Image 2.1).txt' "COMMAND_START|joblist`nmissing.json @upload|image1|input1|`nCOMMAND_END"
Run-Stage $false
Write-Fixture 'Presets/Prompt To Image (Qwen Image 2.1).txt' "COMMAND_START|joblist`ntext_to_img_qwen21.json`nCOMMAND_END"
Run-Stage $true
Write-Fixture 'ComfyUI/text_to_img_qwen21.json' '{broken json'
Run-Stage $false
Write-Fixture 'ComfyUI/text_to_img_qwen21.json' '{}'
Write-Fixture 'build/win/aichat/skills/local_leak.md' 'PRIVATE FIXTURE'
$verification = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture 'scripts\ReleasePackage.ps1') -Phase Verify 2>&1
Check ($LASTEXITCODE -ne 0 -and ($verification -join "`n").Contains('Private or development artifact')) 'Verification accepted a private staged file.'
$outside = Join-Path $fixture 'outside'
New-Item -ItemType Directory -Path $outside | Out-Null
New-Item -ItemType Junction -Path (Join-Path $fixture 'build/win/linked') -Target $outside | Out-Null
Run-Stage $false
Check (@(Get-ChildItem -LiteralPath $outside).Count -eq 0) 'Staging wrote through a junction.'
Write-Output "PASS: $checks release staging checks. Fixtures retained at $fixture"
