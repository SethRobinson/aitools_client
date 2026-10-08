[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Stage', 'Verify', 'VerifyArchive')]
    [string] $Phase
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'Assets\_Script\GameLogic.cs')) -or
    -not (Test-Path -LiteralPath (Join-Path $repoRoot 'ProjectSettings\ProjectVersion.txt'))) {
    throw 'Release packaging must run from the AI Tools project.'
}
$buildRoot = Join-Path $repoRoot 'build\win'

function Assert-NoLinks([string] $Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and
            ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing a linked release path: $cursor"
        }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
}

function Get-SafeFiles([string] $Directory) {
    Assert-NoLinks $Directory
    foreach ($entry in Get-ChildItem -LiteralPath $Directory -Force) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Refusing a linked release entry: $($entry.FullName)"
        }
        if ($entry.PSIsContainer) { Get-SafeFiles $entry.FullName } else { $entry }
    }
}

function Get-ReleaseHash([string] $Path) {
    # The signing environment can change PowerShell's module discovery. Use .NET
    # directly so hashing does not depend on the Get-FileHash script module.
    $stream = [IO.File]::OpenRead($Path)
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $hash.Dispose() }
}

# These presets lost their workflows when the obsolete SDXL/WAN graphs were retired.
# Keep the source history without offering broken menu entries in the release.
$retiredPresets = @(
    'Presets/(Photo Edit) Everyone is at Disneyland.txt',
    'Presets/Image To Image Inpaint (SDXL).txt',
    'Presets/Image To Image Inpaint Mask Subject (SDXL).txt',
    'Presets/Image to Video (VLLM, LLM, Wan) 20s 4p.txt'
)
$runtimeRoots = @('utils', 'web', 'Adventure', 'AIGuide', 'ComfyUI', 'Presets', 'aichat', 'Media')
$rootFiles = @('config_cam.txt', 'model_data.json', 'README.md', 'LICENSE.md')
$tracked = @(& git -C $repoRoot -c core.quotepath=false ls-files)
if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate tracked release inputs.' }
$runtimeFiles = @($tracked | Where-Object {
    $path = $_
    $root = ($path -split '/')[0]
    (($root -in $runtimeRoots) -or ($path -in $rootFiles) -or
        ($path -match '^cli/[^/]+\.(py|bat)$') -or
        ($path -in @('cli/README.md', 'cli/requirements.txt', 'cli/config.example.txt'))) -and
    $path -notmatch '(^|/)(test[^/]*|local_[^/]*|Unused|__pycache__|\.[^/]+)(/|$)' -and
    $path -notmatch '_cached_api|\.meta$' -and
    $path -ne 'utils/RTClip.zip' -and $path -notin $retiredPresets
})
foreach ($required in @('utils/RTClip.exe', 'utils/RTClip.dll', 'utils/RTClip.runtimeconfig.json',
    'utils/ffmpeg/bin/ffmpeg.exe', 'utils/ffmpeg/bin/ffprobe.exe', 'utils/ffmpeg/NOTICE.txt',
    'utils/ffmpeg/licenses/LICENSE', 'utils/yt-dlp/yt-dlp.exe', 'utils/yt-dlp/LICENSE',
    'cli/aitools_cli.py', 'cli/aitools_cli.bat', 'cli/config.example.txt', 'cli/requirements.txt',
    'ComfyUI/text_to_img_qwen21.json', 'ComfyUI/img_to_img_qwen21_edit.json',
    'Presets/Prompt To Image (Qwen Image 2.1).txt', 'Presets/Image To Image (Qwen Image 2.1).txt',
    'aichat/main_prompt.txt') + $rootFiles) {
    if ($required -notin $runtimeFiles) { throw "Missing required tracked release input: $required" }
}

# Validate all inputs before copying anything. Never stage ignored/private local files.
foreach ($relative in $runtimeFiles) {
    $source = Join-Path $repoRoot $relative
    Assert-NoLinks $source
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing release input: $relative" }
    if ($relative -match '^(Presets|Adventure|AIGuide)/.*\.txt$') {
        foreach ($line in Get-Content -LiteralPath $source) {
            if ($line -match '^\s*([^#\s|]+\.json)(?:\s|$)') {
                $workflow = 'ComfyUI/' + $Matches[1]
                if ($workflow -notin $runtimeFiles) { throw "Missing workflow $workflow required by $relative" }
            }
        }
    }
    if ($relative -match '\.json$') {
        $null = Get-Content -LiteralPath $source -Raw | ConvertFrom-Json
    }
}

Assert-NoLinks $buildRoot
if ($Phase -eq 'Stage') {
    New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
    $null = @(Get-SafeFiles $buildRoot) # Refuse links before any writes.
    foreach ($relative in $runtimeFiles) {
        $destination = Join-Path $buildRoot $relative
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $repoRoot $relative) -Destination $destination -Force
    }
    New-Item -ItemType Directory -Path (Join-Path $buildRoot 'output') -Force | Out-Null
    Write-Output "Staged $($runtimeFiles.Count) tracked runtime files. Excluded private, cached and retired inputs."
    return
}

$files = @(Get-SafeFiles $buildRoot)
foreach ($file in $files) {
    $relative = $file.FullName.Substring($buildRoot.Length + 1).Replace('\', '/')
    if ($relative -match '(^|/)(test[^/]*|local_[^/]*|agents[^/]*|\.git|__pycache__|venv|Unused|autosave|tempCache|[^/]*DoNotShip[^/]*|[^/]*DontShip[^/]*)(/|$)' -or
        $relative -match '(^|/)(config(?:_llm|_preferences)?\.txt|log\.txt|.*_cached_api.*|.*\.pdb|.*\.meta|RTClip\.zip)$' -or
        $relative -in $retiredPresets) { throw "Private or development artifact in release: $relative" }
    $root = ($relative -split '/')[0]
    if (($root -in ($runtimeRoots + @('cli'))) -and $relative -notin $runtimeFiles) {
        throw "Unexpected runtime file in release: $relative"
    }
}
foreach ($relative in $runtimeFiles) {
    $destination = Join-Path $buildRoot $relative
    if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) { throw "Missing packaged runtime file: $relative" }
    # Signing changes these two files after staging; verify their signatures separately.
    if ($relative -in @('utils/RTClip.exe', 'utils/RTClip.dll')) { continue }
    if ((Get-ReleaseHash (Join-Path $repoRoot $relative)) -ne
        (Get-ReleaseHash $destination)) { throw "Packaged file differs from source: $relative" }
}
foreach ($relative in @('aitools_client.exe', 'UnityPlayer.dll', 'aitools_client_Data/globalgamemanagers',
    'aitools_client_Data/Managed/Assembly-CSharp.dll', 'MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $buildRoot $relative) -PathType Leaf)) {
        throw "Missing Unity player component: $relative"
    }
}
# Use this PowerShell installation's module, even when build/signing setup
# supplies a PSModulePath containing incompatible PowerShell 7 modules.
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1') -ErrorAction Stop
foreach ($relative in @('aitools_client.exe', 'utils/RTClip.exe', 'utils/RTClip.dll')) {
    $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $buildRoot $relative)
    if ($signature.Status -ne 'Valid') { throw "Invalid release signature: $relative ($($signature.Status))" }
}

if ($Phase -eq 'VerifyArchive') {
    $archivePath = Join-Path $repoRoot 'SethsAIToolsWindows.zip'
    Assert-NoLinks $archivePath
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $entries = @($archive.Entries | Where-Object { $_.Name })
        if ($entries.Count -ne $files.Count) { throw 'Archive and staging file counts differ.' }
        $seen = @{}
        foreach ($entry in $entries) {
            $name = $entry.FullName.Replace('\', '/')
            if (-not $name.StartsWith('aitools_client/') -or $name -match '(^|/)\.\.(/|$)' -or $seen.ContainsKey($name)) {
                throw "Invalid or duplicate archive path: $name"
            }
            $seen[$name] = $true
            $relative = $name.Substring('aitools_client/'.Length)
            $disk = Join-Path $buildRoot $relative
            if (-not (Test-Path -LiteralPath $disk -PathType Leaf)) { throw "Unexpected archive entry: $name" }
            $stream = $entry.Open()
            $hash = [Security.Cryptography.SHA256]::Create()
            try { $actual = [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '') }
            finally { $stream.Dispose(); $hash.Dispose() }
            if ($actual -ne (Get-ReleaseHash $disk)) { throw "Archive content mismatch: $name" }
        }
    } finally { $archive.Dispose() }
}
Write-Output "PASS: $Phase checked $($files.Count) package files, $($runtimeFiles.Count) runtime inputs, workflow dependencies and three signatures."
