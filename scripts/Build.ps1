param([string]$Protoc,[Parameter(Mandatory)][string]$GameRoot,[string]$Preset='windows-msvc-release',[string]$CMake='cmake',[string]$BuildCMakeSource)
$ErrorActionPreference='Stop'
& "$PSScriptRoot/Build-Native.ps1" -Preset $Preset -CMake $CMake -BuildCMakeSource $BuildCMakeSource
if($LASTEXITCODE){throw 'Native build failed'}
$paths=Get-Content "$PSScriptRoot/../o/$Preset/managed-paths.json" -Raw|ConvertFrom-Json
$api=$paths.api;$client=$paths.client
if(!$Protoc){$Protoc=$paths.protoc}
dotnet build "$PSScriptRoot/../managed/SeaPower.Mod.csproj" -c Release "-p:GatewayApiSource=$api" "-p:SeaPowerClientSource=$client" "-p:Protoc=$Protoc" "-p:GameManaged=$GameRoot/Sea Power_Data/Managed"
if($LASTEXITCODE){throw 'Mod build failed'}
dotnet build "$PSScriptRoot/../app/harness/LaunchCandidate.csproj" -c Release "-p:GameManaged=$GameRoot/Sea Power_Data/Managed"
if($LASTEXITCODE){throw 'Harness build failed'}
$package=[IO.Path]::GetFullPath("$PSScriptRoot/../out/package/0.1.0")
New-Item -ItemType Directory -Path $package -Force | Out-Null
$managed=[IO.Path]::GetFullPath("$PSScriptRoot/../managed/bin/Release/netstandard2.1")
# Explicit own/permitted redistribution allowlist, never game references.
foreach($name in @('SeaPower.Mod.dll','SeaPower.Client.dll','Gateway.Api.dll','Google.Protobuf.dll')){
 $path=Join-Path $managed $name
 if(!(Test-Path $path)){
  if($name -eq 'Google.Protobuf.dll'){$path=Join-Path $env:USERPROFILE '.nuget/packages/google.protobuf/3.33.4/lib/netstandard2.0/Google.Protobuf.dll'}
 }
 Copy-Item -LiteralPath $path -Destination $package
}
foreach($name in @('System.Memory','System.Buffers','System.Runtime.CompilerServices.Unsafe')){
 # Unity supplies platform shims (e.g. System.Memory 4.0.99.0). A separate
 # NuGet Span assembly can break the game's System.Text.Json type identity.
 $duplicate=Join-Path $package "$name.dll"
 if(Test-Path -LiteralPath $duplicate){Remove-Item -LiteralPath $duplicate}
}
Copy-Item "$PSScriptRoot/../o/$Preset/src/SeaPower.Host.dll" $package
Copy-Item "$PSScriptRoot/../app/harness/bin/Release/netstandard2.1/LaunchCandidate.dll" "$package/Task004.LaunchCandidate.dll"
Copy-Item "$PSScriptRoot/../config.json" $package
$licenses=Join-Path $package 'licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
Get-ChildItem "$($paths.vcpkg)/$($paths.triplet)/share/*/copyright" | ForEach-Object {Copy-Item $_.FullName (Join-Path $licenses ($_.Directory.Name+'.txt'))}
$protobufLicense=Join-Path $env:USERPROFILE '.nuget/packages/google.protobuf/3.33.4/LICENSE'
if(Test-Path $protobufLicense){Copy-Item $protobufLicense "$licenses/Google.Protobuf.txt"}
Get-ChildItem $package -File | Where-Object Name -ne 'manifest.json' | ForEach-Object { @{file=$_.Name;sha256=(Get-FileHash $_.FullName).Hash} } | ConvertTo-Json | Set-Content "$package/manifest.json"
Write-Output $package
