[CmdletBinding()]
param([switch] $Stage)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$work = Join-Path $repo 'build\capture-native'
$source = Join-Path $work 'source'
$hdr = Join-Path $repo 'tools\ScreenCapture\Hdr'
$commit = '646383234187efd3854e6b1069612b80c3ab9981' # upstream v7.0.1
$archiveHash = '6BB971BAA236DB6374E218B0220074963A19FABA5B5AF6D5FF60FE0D22C4B581'
function Assert-NoLinks([string] $Path) {
    for ($cursor = [IO.Path]::GetFullPath($Path); $cursor; $cursor = [IO.Path]::GetDirectoryName($cursor)) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Refusing linked HDR build path: $cursor" }
    }
}
Assert-NoLinks $work
Assert-NoLinks $hdr
New-Item -ItemType Directory -Path $work -Force | Out-Null
$archive = Join-Path $work 'ScreenRecorderLib-7.0.1.zip'
Assert-NoLinks $archive
if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri "https://codeload.github.com/sskodje/ScreenRecorderLib/zip/$commit" -OutFile $archive }
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $archiveHash) { throw 'Upstream archive hash mismatch.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($entry in $zip.Entries) {
        $prefix = "ScreenRecorderLib-$commit/"
        if (-not $entry.FullName.StartsWith($prefix, [StringComparison]::Ordinal)) { throw 'Unexpected archive root.' }
        $relative = $entry.FullName.Substring($prefix.Length)
        if ($relative -notmatch '^(ScreenRecorderLib|ScreenRecorderLibNative)/' -or $relative.EndsWith('/')) { continue }
        if ($relative -match '(^|/)\.\.(/|$)|:|\\') { throw 'Unsafe archive entry.' }
        $target = [IO.Path]::GetFullPath((Join-Path $source $relative))
        if (-not $target.StartsWith($source + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive entry escaped source directory.' }
        Assert-NoLinks $target
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
        if (Test-Path -LiteralPath $target) {
            $stream = $entry.Open()
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $entryHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
            finally { $stream.Dispose(); $sha.Dispose() }
            if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $entryHash) { continue }
        }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
    }
} finally { $zip.Dispose() }
$patch = Join-Path $hdr 'ScreenRecorderLib.patch'
Assert-NoLinks $patch
& git -C $source apply --check $patch
if ($LASTEXITCODE -ne 0) { throw 'HDR patch no longer matches pinned source.' }
& git -C $source apply $patch
if ($LASTEXITCODE -ne 0) { throw 'Applying HDR patch failed.' }
$toneMapperTarget = Join-Path $source 'ScreenRecorderLibNative\HdrToneMapper.h'
Assert-NoLinks (Join-Path $hdr 'HdrToneMapper.h')
Assert-NoLinks $toneMapperTarget
Copy-Item -LiteralPath (Join-Path $hdr 'HdrToneMapper.h') -Destination $toneMapperTarget -Force
# Pin the native toolset to the shipped VC 14.44 runtime. Do not build against a
# newer CRT accidentally when a newer Visual Studio is installed.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -all -products '*' -property installationPath | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'VC\Tools\MSVC\14.44.35207\bin\Hostx64\x64\cl.exe') } | Select-Object -First 1
if (-not $vs) { throw 'Install Visual Studio C++ tools, including v143 14.44, C++/CLI and Windows SDK 10.0.26100.0.' }
$msbuild = Join-Path $vs 'MSBuild\Current\Bin\MSBuild.exe'
$output = Join-Path $work 'bin'
Assert-NoLinks $output
& $msbuild (Join-Path $source 'ScreenRecorderLib\ScreenRecorderLib.vcxproj') /m /nologo /v:minimal /p:Configuration=Release /p:Platform=x64 /p:VCToolsVersion=14.44.35207 /p:WindowsTargetPlatformVersion=10.0.26100.0 "/p:OutDir=$output\"
if ($LASTEXITCODE -ne 0) { throw 'HDR ScreenRecorderLib build failed.' }
& $msbuild (Join-Path $hdr 'Checks\HdrChecks.vcxproj') /nologo /v:minimal /p:Configuration=Release /p:Platform=x64 /p:VCToolsVersion=14.44.35207 "/p:OutDir=$output\"
if ($LASTEXITCODE -ne 0) { throw 'HDR GPU checks compilation failed.' }
& (Join-Path $output 'HdrChecks.exe')
if ($LASTEXITCODE -ne 0) { throw 'Synthetic HDR GPU checks failed.' }
if ($Stage) {
    $runtime = Join-Path $repo 'utils\ScreenCapture'
    $destination = Join-Path $runtime 'ScreenRecorderLib.dll'
    Assert-NoLinks $destination
    Copy-Item -LiteralPath (Join-Path $output 'ScreenRecorderLib.dll') -Destination $destination -Force
    $manifestPath = Join-Path $runtime 'dependencies.json'
    Assert-NoLinks $manifestPath
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    ($manifest.files | Where-Object name -eq 'ScreenRecorderLib.dll').sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
    $manifest | Add-Member -Force -NotePropertyName screenRecorderLibPatch -NotePropertyValue ([ordered]@{
        name = 'AITools HDR 1'; commit = $commit; archiveSha256 = $archiveHash
        toolset = '14.44.35207'; windowsSdk = '10.0.26100.0'
        patchSha256 = (Get-FileHash -LiteralPath $patch -Algorithm SHA256).Hash
        toneMapperSha256 = (Get-FileHash -LiteralPath (Join-Path $hdr 'HdrToneMapper.h') -Algorithm SHA256).Hash
    })
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
}
Write-Output "Built patched ScreenRecorderLib 7.0.1 in $output"
