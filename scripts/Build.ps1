param([string]$Protoc, [Parameter(Mandatory)][string]$GameRoot, [string]$Preset = 'windows-msvc-release', [string]$CMake = 'cmake', [string]$BuildCMakeSource)
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot/Build-Native.ps1" -Preset $Preset -CMake $CMake -BuildCMakeSource $BuildCMakeSource
if ($LASTEXITCODE) {
    throw 'Native build failed'
}
$managedBuildPaths = Get-Content "$PSScriptRoot/../o/$Preset/managed-paths.json" -Raw | ConvertFrom-Json
$canonicalApiSource = $managedBuildPaths.api
$providerSource = $managedBuildPaths.client
if (!$Protoc) {
    $Protoc = $managedBuildPaths.protoc
}
$modBuildArguments = @(
    "$PSScriptRoot/../managed/runtime/SeaPower.Mod.csproj"
    '-c'
    'Release'
    "-p:GatewayApiSource=$canonicalApiSource"
    "-p:SeaPowerClientSource=$providerSource"
    "-p:Protoc=$Protoc"
    "-p:GameManaged=$GameRoot/Sea Power_Data/Managed"
)
dotnet build @modBuildArguments
if ($LASTEXITCODE) {
    throw 'Mod build failed'
}
$packageDirectory = [IO.Path]::GetFullPath("$PSScriptRoot/../out/package/0.1.0")
# A fresh stage prevents stale development artifacts entering a runtime distribution.
if (Test-Path -LiteralPath $packageDirectory) {
    $packageParent = [IO.Path]::GetFullPath("$PSScriptRoot/../out/package").TrimEnd('\')
    if (![IO.Path]::GetFullPath($packageDirectory).StartsWith($packageParent + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Package path escapes its staging root'
    }
    Move-Item -LiteralPath $packageDirectory -Destination ($packageDirectory + '.previous-' + [Guid]::NewGuid().ToString('N'))
}
New-Item -ItemType Directory -Path $packageDirectory | Out-Null
$managedOutputDirectory = [IO.Path]::GetFullPath("$PSScriptRoot/../managed/runtime/bin/Release/netstandard2.1")
# Explicit own/permitted redistribution allowlist, never game references.
foreach ($assemblyName in @('SeaPower.Mod.dll', 'SeaPower.Client.dll', 'Gateway.Api.dll', 'Google.Protobuf.dll')) {
    $assemblyPath = Join-Path $managedOutputDirectory $assemblyName
    if (!(Test-Path $assemblyPath)) {
        if ($assemblyName -eq 'Google.Protobuf.dll') {
            $assemblyPath = Join-Path $env:USERPROFILE '.nuget/packages/google.protobuf/3.33.4/lib/netstandard2.0/Google.Protobuf.dll'
        }

    }
    Copy-Item -LiteralPath $assemblyPath -Destination $packageDirectory

}
foreach ($assemblyName in @('System.Memory', 'System.Buffers', 'System.Runtime.CompilerServices.Unsafe')) {
    # Unity supplies platform shims (e.g. System.Memory 4.0.99.0). A separate
    # NuGet Span assembly can break the game's System.Text.Json type identity.
    $platformShimPath = Join-Path $packageDirectory "$assemblyName.dll"
    if (Test-Path -LiteralPath $platformShimPath) {
        Remove-Item -LiteralPath $platformShimPath
    }

}
Copy-Item "$PSScriptRoot/../o/$Preset/src/SeaPower.Host.dll" $packageDirectory
Copy-Item "$PSScriptRoot/../config.json" $packageDirectory
$licenses = Join-Path $packageDirectory 'licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
Get-ChildItem "$($managedBuildPaths.vcpkg)/$($managedBuildPaths.triplet)/share/*/copyright" | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $licenses ($_.Directory.Name + '.txt'))
}
$protobufLicense = Join-Path $env:USERPROFILE '.nuget/packages/google.protobuf/3.33.4/LICENSE'
if (Test-Path $protobufLicense) {
    Copy-Item $protobufLicense "$licenses/Google.Protobuf.txt"
}
Get-ChildItem $packageDirectory -File -Recurse | Where-Object Name -NE 'manifest.json' | ForEach-Object {
    @{
        file   =[IO.Path]::GetRelativePath($packageDirectory, $_.FullName).Replace('\', '/')
        sha256 =(Get-FileHash $_.FullName).Hash
    }
} | ConvertTo-Json | Set-Content "$packageDirectory/manifest.json"
Write-Output $packageDirectory
