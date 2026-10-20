$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sdkVersion = & dotnet --version
if ($LASTEXITCODE -ne 0) { throw 'A .NET SDK is required.' }
$sdkMajor = [int]($sdkVersion.Split('.')[0])
if ($sdkMajor -lt 8) { throw '.NET SDK 8 or newer is required.' }

# Retain isolated build outputs for inspection. No Unity, network services or user
# preferences are used by these checks.
$fixtures = Join-Path $repoRoot ('build\aichat-action-checks-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtures -Force | Out-Null
$sources = @(
    'Assets\_Script\LLM\AIChat\Skills\Skill.cs'
    'Assets\_Script\LLM\AIChat\Skills\SkillAction.cs'
    'Assets\_Script\LLM\AIChat\Skills\SkillActionParser.cs'
    'scripts\AIChatActionChecks.cs'
)
$compileItems = foreach ($source in $sources) {
    $escapedPath = [Security.SecurityElement]::Escape((Join-Path $repoRoot $source))
    '    <Compile Include="' + $escapedPath + '" />'
}
$project = Join-Path $fixtures 'AIChatActionChecks.csproj'
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net$sdkMajor.0</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
$($compileItems -join "`n")
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $project -Encoding utf8
& dotnet run --project $project --configuration Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'AI Chat action checks failed.' }
Write-Output "Build outputs retained at $fixtures"
