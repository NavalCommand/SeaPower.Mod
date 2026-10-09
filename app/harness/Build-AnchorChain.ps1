[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$WorkDirectory,
    [Parameter(Mandatory)][string]$GameRoot
)
$ErrorActionPreference='Stop'
# Source build only: does not install/activate anything. Approved BepInEx references required.
$pin='28d225be917533fcc25e19cf8f25ac44010f8abd'
$sourceHash='5DC88976D56D7A47ADD211DFA9C0436148FAA71B712FF4532AD9BE13101BFD0C'
$work=[IO.Path]::GetFullPath($WorkDirectory)
if(Test-Path -LiteralPath $work){throw 'Choose a fresh source-build directory.'}
New-Item -ItemType Directory -Path $work | Out-Null
$main=Join-Path $work 'Main.cs'
Invoke-WebRequest -Uri "https://raw.githubusercontent.com/SeaPower-Modders/AnchorChain/$pin/AnchorChain/Main.cs" -OutFile $main
if((Get-FileHash -LiteralPath $main -Algorithm SHA256).Hash -ne $sourceHash){throw 'Pinned source hash mismatch.'}
$managed=Join-Path $GameRoot 'Sea Power_Data/Managed'
$loader=Join-Path $GameRoot 'BepInEx'
# Paths are XML data, never interpolated shell commands.
$m=[Security.SecurityElement]::Escape($managed)
$l=[Security.SecurityElement]::Escape($loader)
$project=@"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><ImplicitUsings>enable</ImplicitUsings><LangVersion>12</LangVersion><AssemblyName>AnchorChain</AssemblyName><Version>1.1.0</Version></PropertyGroup><ItemGroup>
<Reference Include="BepInEx"><HintPath>$l/core/BepInEx.dll</HintPath><Private>false</Private></Reference>
<Reference Include="AnchorChain.Preloader"><HintPath>$l/plugins/AnchorChain.Preloader.dll</HintPath><Private>false</Private></Reference>
<Reference Include="Seapower-Scripts"><HintPath>$m/Seapower-Scripts.dll</HintPath><Private>false</Private></Reference>
<Reference Include="Noesis.NoesisGUI"><HintPath>$m/Noesis.NoesisGUI.dll</HintPath><Private>false</Private></Reference>
<Reference Include="UnityEngine"><HintPath>$m/UnityEngine.dll</HintPath><Private>false</Private></Reference>
<Reference Include="$m/UnityEngine.*Module.dll"><Private>false</Private></Reference>
</ItemGroup></Project>
"@
$projectPath=Join-Path $work 'AnchorChain.csproj'
Set-Content -LiteralPath $projectPath $project
dotnet build $projectPath -c Release --nologo
if($LASTEXITCODE -ne 0){throw 'Anchor Chain source build failed.'}
Write-Output (Join-Path $work 'bin/Release/netstandard2.1/AnchorChain.dll')
