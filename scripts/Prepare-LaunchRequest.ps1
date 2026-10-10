[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Scenario,
    [Parameter(Mandatory)][string]$Side,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$GameRoot
)
$ErrorActionPreference = 'Stop'
# Offline preparation only. Does not install a loader, start a game, or alter settings.
$expectedAssemblySha256 = '0FE6AAF2A6D9982E7E2EB41FD6035CB97C149773202C90821D71C00A9FDEB01A'
$gameAssemblyPath = Join-Path $GameRoot 'Sea Power_Data\Managed\Seapower-Scripts.dll'
if ((Get-FileHash -LiteralPath $gameAssemblyPath -Algorithm SHA256).Hash -ne $expectedAssemblySha256) {
    throw 'Game assembly changed; reassess hooks before running.'

}
$scenarioPath = (Resolve-Path -LiteralPath $Scenario).Path
if ([IO.Path]::GetExtension($scenarioPath) -ne '.ini') {
    throw 'A local .ini mission is required.'
}
$isMissionSection = $false
$nativePlayerSide = $null
foreach ($line in Get-Content -LiteralPath $scenarioPath) {
    if ($line -match '^\s*\[([^]]+)\]') {
        $isMissionSection = $Matches[1] -eq 'Mission'
        continue
    }
    if ($isMissionSection -and $line -match '^\s*PlayerTaskforce\s*=\s*([^;]+)') {
        $nativePlayerSide = $Matches[1].Trim()

    }

}
if (!$nativePlayerSide -or $nativePlayerSide -cne $Side) {
    throw 'Side must equal the mission PlayerTaskforce; arbitrary side swapping is unverified.'

}
$outputDirectoryPath = [IO.Path]::GetFullPath($OutputDirectory)
$gameDirectoryPath = [IO.Path]::GetFullPath($GameRoot).TrimEnd('\')
if ($outputDirectoryPath -eq $gameDirectoryPath -or $outputDirectoryPath.StartsWith($gameDirectoryPath + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Keep experimental outputs outside the game installation.'

}
if (Test-Path -LiteralPath $outputDirectoryPath) {
    throw 'Choose a new output directory; stale results are refused.'
}
New-Item -ItemType Directory -Path $outputDirectoryPath | Out-Null
$launchRequest = [ordered]@{
    schema         = 1
    token          = [Guid]::NewGuid().ToString()
    assemblySha256 = $expectedAssemblySha256
    scenario       = $scenarioPath
    side           = $Side
    timeoutSeconds = 180
    status         = 'prepared-only-no-loader-activated'
    ready          = $false

}
$launchRequest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputDirectoryPath 'request.json') -Encoding utf8
$launchRequest | ConvertTo-Json
