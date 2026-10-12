param(
    [string]$Preset = 'windows-msvc-release',
    [string]$Configuration = 'Release',
    [string]$CMake = 'cmake'
)
$ErrorActionPreference = 'Stop'
& $CMake --build "$PSScriptRoot/../o/$Preset" --config $Configuration --target NavalCommand.SeaPower.Mod.PackageRuntime
if ($LASTEXITCODE) {
    throw 'Runtime packaging failed; configure the selected preset before building.'
}
