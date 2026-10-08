[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Reset', 'Package')]
    [string] $Phase
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Derive the root from this file, never the caller's working directory or environment.
if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) { throw 'Missing cleanup script directory.' }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($repoRoot -eq [IO.Path]::GetPathRoot($repoRoot) -or
    -not (Test-Path -LiteralPath (Join-Path $repoRoot 'ProjectSettings\ProjectVersion.txt') -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $repoRoot 'Assets\_Script\GameLogic.cs') -PathType Leaf)) {
    throw 'Refusing cleanup outside the AI Tools project.'
}

function Assert-NoLinks([string] $Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Refusing cleanup through a link or junction: $cursor"
            }
        }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
}

function Assert-SafeTree([string] $Path) {
    Assert-NoLinks $Path
    if (-not (Test-Path -LiteralPath $Path)) { return }
    # Walk one level at a time so no enumeration follows a junction before checking it.
    foreach ($entry in Get-ChildItem -LiteralPath $Path -Force) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Refusing cleanup of a tree containing a link or junction: $($entry.FullName)"
        }
        if ($entry.PSIsContainer) { Assert-SafeTree $entry.FullName }
    }
}

# Preflight the complete tree before removing anything, including wildcard matches.
Assert-SafeTree (Join-Path $repoRoot 'build\win')
if ($Phase -eq 'Reset') {
    if (Test-Path -LiteralPath (Join-Path $repoRoot 'build\win')) {
        Remove-Item -LiteralPath (Join-Path $repoRoot 'build\win') -Recurse -Force
    }
    New-Item -ItemType Directory -Path (Join-Path $repoRoot 'build\win') -Force | Out-Null
    return
}

# Only these fixed directories and filename patterns can enter the wildcard sweep.
$rules = @(
    @('Adventure', 'test*.txt', $false),
    @('AIGuide', 'TEST*.txt', $false),
    @('ComfyUI', 'test*.json', $false),
    @('ComfyUI', '*_cached_api*.json', $true),
    @('ComfyUI\workflow', 'test*.json', $false),
    @('Presets', 'TEST*.txt', $false),
    @('aichat', 'test_*', $true),
    @('aichat\skills', 'local_*.md', $false)
)
$buildPrefix = (Join-Path $repoRoot 'build\win') + [IO.Path]::DirectorySeparatorChar
foreach ($rule in $rules) {
    $directory = Join-Path (Join-Path $repoRoot 'build\win') $rule[0]
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $directory -File -Force -Filter $rule[1] -Recurse:$rule[2]) {
        $fullPath = [IO.Path]::GetFullPath($file.FullName)
        if (-not $fullPath.StartsWith($buildPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Wildcard cleanup escaped build\win.'
        }
        Assert-NoLinks $fullPath
        # A single enumerated file, checked against the fixed build root, never a shell wildcard.
        [IO.File]::Delete((Join-Path $repoRoot ('build\win\' + $fullPath.Substring($buildPrefix.Length))))
    }
}

foreach ($leaf in @('config.txt', 'config_llm.txt', 'config_preferences.txt', 'utils\RTClip.zip')) {
    if (Test-Path -LiteralPath (Join-Path $repoRoot ('build\win\' + $leaf))) {
        [IO.File]::Delete((Join-Path $repoRoot ('build\win\' + $leaf)))
    }
}
if (Test-Path -LiteralPath (Join-Path $repoRoot 'build\win\ComfyUI\Unused')) {
    Remove-Item -LiteralPath (Join-Path $repoRoot 'build\win\ComfyUI\Unused') -Recurse -Force
}
if (Test-Path -LiteralPath (Join-Path $repoRoot "build\win\Seth's AI Tools_BurstDebugInformation_DoNotShip")) {
    Remove-Item -LiteralPath (Join-Path $repoRoot "build\win\Seth's AI Tools_BurstDebugInformation_DoNotShip") -Recurse -Force
}
if (Test-Path -LiteralPath (Join-Path $repoRoot "build\win\Seth's AI Tools_BackUpThisFolder_ButDontShipItWithYourGame")) {
    Remove-Item -LiteralPath (Join-Path $repoRoot "build\win\Seth's AI Tools_BackUpThisFolder_ButDontShipItWithYourGame") -Recurse -Force
}
if (Test-Path -LiteralPath (Join-Path $repoRoot 'build\win\aitools_client_BackUpThisFolder_ButDontShipItWithYourGame')) {
    Remove-Item -LiteralPath (Join-Path $repoRoot 'build\win\aitools_client_BackUpThisFolder_ButDontShipItWithYourGame') -Recurse -Force
}
if (Test-Path -LiteralPath (Join-Path $repoRoot 'build\win\aitools_client_BurstDebugInformation_DoNotShip')) {
    Remove-Item -LiteralPath (Join-Path $repoRoot 'build\win\aitools_client_BurstDebugInformation_DoNotShip') -Recurse -Force
}
if (Test-Path -LiteralPath (Join-Path $repoRoot 'build\win\autosave')) {
    Remove-Item -LiteralPath (Join-Path $repoRoot 'build\win\autosave') -Recurse -Force
}
if (Test-Path -LiteralPath (Join-Path $repoRoot 'build\win\tempCache')) {
    Remove-Item -LiteralPath (Join-Path $repoRoot 'build\win\tempCache') -Recurse -Force
}
