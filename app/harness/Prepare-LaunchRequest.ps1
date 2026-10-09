[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Scenario,
    [Parameter(Mandatory)][string]$Side,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$GameRoot = 'C:\Games\Sea_Power_8_3'
)
$ErrorActionPreference = 'Stop'
# Offline preparation only. Does not install a loader, start a game, or alter settings.
$expected = '0FE6AAF2A6D9982E7E2EB41FD6035CB97C149773202C90821D71C00A9FDEB01A'
$assembly = Join-Path $GameRoot 'Sea Power_Data\Managed\Seapower-Scripts.dll'
if ((Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -ne $expected) {
    throw 'Game assembly changed; reassess hooks before running.'
}
$mission = (Resolve-Path -LiteralPath $Scenario).Path
if ([IO.Path]::GetExtension($mission) -ne '.ini') { throw 'A local .ini mission is required.' }
$inMission = $false
$playerSide = $null
foreach ($line in Get-Content -LiteralPath $mission) {
    if ($line -match '^\s*\[([^]]+)\]') { $inMission = $Matches[1] -eq 'Mission'; continue }
    if ($inMission -and $line -match '^\s*PlayerTaskforce\s*=\s*([^;]+)') {
        $playerSide = $Matches[1].Trim()
    }
}
if (!$playerSide -or $playerSide -cne $Side) {
    throw 'Side must equal the mission PlayerTaskforce; arbitrary side swapping is unverified.'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
$gamePath = [IO.Path]::GetFullPath($GameRoot).TrimEnd('\')
if ($output -eq $gamePath -or $output.StartsWith($gamePath + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Keep experimental outputs outside the game installation.'
}
if (Test-Path -LiteralPath $output) { throw 'Choose a new output directory; stale results are refused.' }
New-Item -ItemType Directory -Path $output | Out-Null
$request = [ordered]@{
    schema = 1; token = [Guid]::NewGuid().ToString(); assemblySha256 = $expected
    scenario = $mission; side = $Side; timeoutSeconds = 180
    status = 'prepared-only-no-loader-activated'; ready = $false
}
$request | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'request.json') -Encoding utf8
$request | ConvertTo-Json
