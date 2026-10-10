[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$WorkDirectory,
    [Parameter(Mandatory)][string]$GameRoot
)
$ErrorActionPreference = 'Stop'
# Source build only: does not install/activate anything. Approved BepInEx references required.
$anchorChainRevision = '28d225be917533fcc25e19cf8f25ac44010f8abd'
$expectedSourceHash = '5DC88976D56D7A47ADD211DFA9C0436148FAA71B712FF4532AD9BE13101BFD0C'
$workDirectory = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $workDirectory) {
    throw 'Choose a fresh source-build directory.'
}
New-Item -ItemType Directory -Path $workDirectory | Out-Null
$anchorChainSourcePath = Join-Path $workDirectory 'Main.cs'
Invoke-WebRequest -Uri "https://raw.githubusercontent.com/SeaPower-Modders/AnchorChain/$anchorChainRevision/AnchorChain/Main.cs" -OutFile $anchorChainSourcePath
if ((Get-FileHash -LiteralPath $anchorChainSourcePath -Algorithm SHA256).Hash -ne $expectedSourceHash) {
    throw 'Pinned source hash mismatch.'
}
$gameManagedDirectory = Join-Path $GameRoot 'Sea Power_Data/Managed'
$loaderDirectory = Join-Path $GameRoot 'BepInEx'
# Paths are XML data, never interpolated shell commands.
$gameReferencesXml = [Security.SecurityElement]::Escape($gameManagedDirectory)
$loaderReferencesXml = [Security.SecurityElement]::Escape($loaderDirectory)
$projectXml = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <AssemblyName>AnchorChain</AssemblyName>
    <Version>1.1.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="BepInEx">
      <HintPath>$loaderReferencesXml/core/BepInEx.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="AnchorChain.Preloader">
      <HintPath>$loaderReferencesXml/plugins/AnchorChain.Preloader.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Seapower-Scripts">
      <HintPath>$gameReferencesXml/Seapower-Scripts.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Noesis.NoesisGUI">
      <HintPath>$gameReferencesXml/Noesis.NoesisGUI.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine">
      <HintPath>$gameReferencesXml/UnityEngine.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="$gameReferencesXml/UnityEngine.*Module.dll">
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
"@
$projectPath = Join-Path $workDirectory 'AnchorChain.csproj'
Set-Content -LiteralPath $projectPath $projectXml
dotnet build $projectPath -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    throw 'Anchor Chain source build failed.'
}
Write-Output (Join-Path $workDirectory 'bin/Release/netstandard2.1/AnchorChain.dll')
