param(
    [Parameter(Mandatory)][string]$GameRoot,
    [Parameter()][string]$OutputPath = (Join-Path $PSScriptRoot '../platform-roles.ini')
)

$ErrorActionPreference = 'Stop'
$streamingAssets = Join-Path ([IO.Path]::GetFullPath($GameRoot)) 'Sea Power_Data/StreamingAssets'
$originalContent = Join-Path $streamingAssets 'original'
$englishContent = Join-Path $originalContent 'language_en'
if (!(Test-Path -LiteralPath $originalContent -PathType Container) -or !(Test-Path -LiteralPath $englishContent -PathType Container)) {
    throw "Sea Power encyclopedia content was not found under $streamingAssets"
}

$nativeRoleMap = @{
    None = @()
    Deco = @('Decoy')
    SeaMine = @('MineWarfare')
    SmallCivilian = @()
    Merchant = @()
    Passenger = @('Transport')
    Transport = @('Transport')
    Landing = @('Transport')
    Minesweep = @('MineWarfare')
    RAS = @('Transport')
    Spy = @('Reconnaissance', 'Surveillance')
    FAC = @('SurfaceWarfare')
    ASuW = @('SurfaceWarfare')
    ASuW_LR = @('SurfaceWarfare', 'Strike')
    ASuW_VLR = @('SurfaceWarfare', 'Strike')
    ASuW_Gun = @('SurfaceWarfare')
    NGFS = @('LandAttack')
    AAW = @('AirDefence')
    ASW = @('AntiSubmarineWarfare')
    Carrier = @()
    CVS = @()
    CVL = @()
    SS = @()
    SSN = @('AntiSubmarineWarfare')
    SSG = @('SurfaceWarfare')
    SSGN = @('SurfaceWarfare', 'LandAttack')
    SSB = @('Strike')
    SSBN = @('Strike')
    Airliner = @('Transport')
    Fighter = @('AirSuperiority')
    Bomber = @('Strike')
    HeavyBomber = @('Strike')
    Recon = @('Reconnaissance', 'Surveillance')
    AEW = @('AirborneEarlyWarning', 'Surveillance')
    EW = @('ElectronicWarfare')
    MPA = @('MaritimePatrol', 'AntiSubmarineWarfare')
    ESM = @('Surveillance')
    SEAD = @('LandAttack', 'ElectronicWarfare')
    Attack = @('Strike', 'LandAttack')
    Targeting = @('Surveillance')
    SAR = @('Transport')
    Airfield = @()
    Port = @()
    Gun = @('SurfaceWarfare')
    SAM = @('AirDefence')
    ASM = @('SurfaceWarfare')
    Radar = @('Surveillance')
    Target = @()
    LifeRaft = @('Transport')
}

$encyclopediaFolders = @{
    'aircraft_names.ini' = 'aircraft'
    'vessel_names.ini' = 'vessels'
    'land_units_names.ini' = 'land_units'
    'biologic_names.ini' = 'biologic'
}
$encyclopediaTypes = @{}
foreach ($fileName in $encyclopediaFolders.Keys) {
    $filePath = Join-Path $englishContent $fileName
    if (!(Test-Path -LiteralPath $filePath -PathType Leaf)) {
        continue
    }
    $folder = $encyclopediaFolders[$fileName]
    $entryName = ''
    foreach ($line in Get-Content -LiteralPath $filePath) {
        $cleanLine = ($line -replace '\s*(;|#|//).*$', '').Trim()
        if ($cleanLine -match '^\[([^\]]+)\]$') {
            $entryName = $Matches[1].Trim().ToLowerInvariant()
        }
        elseif ($entryName -and $cleanLine -match '^Type\s*=\s*(.*?)\s*$') {
            $encyclopediaTypes[($folder + '/' + $entryName)] = $Matches[1].Trim()
        }
    }
}

$mappings = [Collections.Generic.SortedDictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
$typeFiles = Get-ChildItem -LiteralPath $originalContent -Recurse -File -Filter '*.ini' |
    Where-Object { $_.Directory.Name -in @('aircraft', 'vessels', 'land_units', 'biologic') }
foreach ($file in $typeFiles) {
    $nativeRoles = [Collections.Generic.List[string]]::new()
    foreach ($line in Get-Content -LiteralPath $file.FullName) {
        $cleanLine = ($line -replace '\s*(;|#|//).*$', '').Trim()
        if ($cleanLine -match '^Role\s*=\s*(.*?)\s*$') {
            foreach ($nativeRole in $Matches[1].Split(',')) {
                $roleName = $nativeRole.Trim()
                if ($roleName) {
                    if (!$nativeRoleMap.ContainsKey($roleName)) {
                        throw "Unmapped native AI.Role '$roleName' in $($file.FullName)"
                    }
                    $nativeRoles.Add($roleName)
                }
            }
        }
    }
    if ($nativeRoles.Count -eq 0) {
        continue
    }

    $roles = [Collections.Generic.List[string]]::new()
    foreach ($nativeRole in $nativeRoles) {
        foreach ($role in $nativeRoleMap[$nativeRole]) {
            if (!$roles.Contains($role)) {
                $roles.Add($role)
            }
        }
    }
    $relativeFolder = [IO.Path]::GetRelativePath($originalContent, $file.DirectoryName).Replace('\', '/')
    $typeId = ($relativeFolder + '/' + $file.BaseName).ToLowerInvariant()
    if ($relativeFolder -eq 'biologic' -and $encyclopediaTypes[$typeId] -match 'Whale') {
        # AI.Role is a simulation concern; an encyclopedia-listed animal is not a military platform.
        $roles.Clear()
    }
    $entry = if ($roles.Count -eq 0) { '' } else { $roles -join ',' }
    if ($encyclopediaTypes.ContainsKey($typeId)) {
        $entry += ' ; Encyclopedia type: ' + $encyclopediaTypes[$typeId]
    }
    $mappings[$typeId] = $entry
}

if ($mappings.Count -eq 0) {
    throw 'No native platform-role mappings were generated.'
}

$output = [Collections.Generic.List[string]]::new()
$output.Add('; Sea Power 8.3 default platform-role overrides.')
$output.Add('; Keys use the active Unit.TypeId form: <content-folder>/<INI-name>.')
$output.Add('; Values are NavalCommand PlatformRole names; an empty value clears native roles.')
$output.Add('; Entries were built from native AI.Role definitions and annotated with language_en encyclopedia Type labels when available.')
$output.Add('; Edit or extend this file to override one type. Unlisted mod types use the built-in native-role mapping.')
$output.Add('[PlatformRoles]')
foreach ($entry in $mappings.GetEnumerator()) {
    $output.Add($entry.Key + '=' + $entry.Value)
}

$absoluteOutputPath = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $absoluteOutputPath) -Force | Out-Null
[IO.File]::WriteAllLines($absoluteOutputPath, $output, [Text.UTF8Encoding]::new($false))
Write-Output "Generated $($mappings.Count) role mappings at $absoluteOutputPath"
