param([Parameter(Mandatory)][string]$VcpkgRoot,[Parameter(Mandatory)][string]$Installed)
$ErrorActionPreference='Stop'
$revision=& git -C $VcpkgRoot rev-parse HEAD
if($revision -ne '3ddaad9be959816602453ecb05533f8732464ef4'){throw 'Use the manifest-pinned vcpkg checkout'}
& "$VcpkgRoot/vcpkg.exe" install --triplet x64-windows-static-md --host-triplet x64-windows-static-md "--x-manifest-root=$PSScriptRoot/.." "--x-install-root=$Installed" --binarysource=clear
if($LASTEXITCODE){throw 'Source dependency build failed'}
# After provisioning, use windows-x64-sourcebuilt to avoid a second installer run.
