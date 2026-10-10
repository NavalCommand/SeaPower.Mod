param(
    [string]$Preset = 'windows-msvc-release',
    [string]$Configuration = 'Release',
    [string]$CMake = 'cmake'
)
$ErrorActionPreference = 'Stop'
& $CMake --build "$PSScriptRoot/../o/$Preset" --config $Configuration --target package-harness
if ($LASTEXITCODE) {
    throw 'package-harness failed; configure the selected preset before building.'
}
