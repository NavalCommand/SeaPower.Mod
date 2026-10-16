param(
    [Parameter(Mandatory)][ValidateSet('Runtime', 'Harness')][string]$Kind,
    [Parameter(Mandatory)][string]$Configuration,
    [string]$NativeHost,
    [string]$ApiSource,
    [string]$VcpkgInstalled,
    [string]$Triplet
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath("$PSScriptRoot/..")
$outputRoot = Join-Path $repository 'o'
$projectName = if ($Kind -eq 'Runtime') { 'SeaPower.Mod' } else { 'SeaPower.Harness' }
$recordDirectory = Join-Path $outputRoot "managed/$projectName/bin/$Configuration"
$targetPath = (Get-Content -LiteralPath "$recordDirectory/TargetPath.txt" -Raw).Trim()
if (!(Test-Path -LiteralPath $targetPath -PathType Leaf)) {
    throw "Evaluated MSBuild TargetPath is unavailable: $targetPath"
}
$category = if ($Kind -eq 'Runtime') { 'package' } else { 'harness' }
$stage = [IO.Path]::GetFullPath((Join-Path $outputRoot "$category/0.1.0"))
if (!$stage.StartsWith($outputRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Stage escapes the repository output directory'
}
# Keep a recoverable old stage; each new stage starts empty.
if (Test-Path -LiteralPath $stage) {
    Move-Item -LiteralPath $stage -Destination ($stage + '.previous-' + [Guid]::NewGuid().ToString('N'))
}
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item -LiteralPath $targetPath -Destination $stage
if ($Kind -eq 'Runtime') {
    $copyPaths = @(Get-Content -LiteralPath "$recordDirectory/CopyLocalPaths.txt")
    foreach ($assembly in @('SeaPower.Client.dll', 'Gateway.Api.dll', 'Google.Protobuf.dll')) {
        $matches = @($copyPaths | Where-Object { [IO.Path]::GetFileName($_) -eq $assembly } | Select-Object -Unique)
        if ($matches.Count -ne 1 -or !(Test-Path -LiteralPath $matches[0] -PathType Leaf)) {
            throw "Expected exactly one evaluated MSBuild copy-local output for $assembly"
        }
        Copy-Item -LiteralPath $matches[0] -Destination $stage
    }
    if (!(Test-Path -LiteralPath $NativeHost -PathType Leaf)) {
        throw 'Built native host is unavailable'
    }
    Copy-Item -LiteralPath $NativeHost -Destination $stage
    Copy-Item -LiteralPath "$repository/config.json" -Destination $stage
    Copy-Item -LiteralPath "$repository/platform-roles.ini" -Destination $stage
    Copy-Item -LiteralPath "$repository/alternative-display-names.en.ini" -Destination $stage
    Copy-Item -LiteralPath "$repository/alternative-display-names.ru.ini" -Destination $stage
    $licenses = Join-Path $stage 'licenses'
    New-Item -ItemType Directory -Path $licenses | Out-Null
    Get-ChildItem "$VcpkgInstalled/$Triplet/share/*/copyright" | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $licenses ($_.Directory.Name + '.txt'))
    }
    $licenseRecord = Join-Path $ApiSource "o/managed/Gateway.Api/bin/$Configuration/ProtobufLicense.txt"
    $protobufLicense = (Get-Content -LiteralPath $licenseRecord -Raw).Trim()
    if (Test-Path -LiteralPath $protobufLicense -PathType Leaf) {
        Copy-Item -LiteralPath $protobufLicense -Destination "$licenses/Google.Protobuf.txt"
    }
    elseif (!(Test-Path -LiteralPath "$licenses/protobuf.txt" -PathType Leaf)) {
        throw 'Protobuf redistribution license is unavailable'
    }
    $allowedFiles = @('SeaPower.Mod.dll', 'SeaPower.Client.dll', 'Gateway.Api.dll', 'Google.Protobuf.dll', 'SeaPower.Host.dll', 'config.json', 'platform-roles.ini', 'alternative-display-names.en.ini', 'alternative-display-names.ru.ini')
    foreach ($file in Get-ChildItem -LiteralPath $stage -File -Recurse) {
        $relativePath = [IO.Path]::GetRelativePath($stage, $file.FullName).Replace('\', '/')
        if ($relativePath -notin $allowedFiles -and $relativePath -notmatch '^licenses/[^/]+\.txt$') {
            throw "Unexpected production distribution file: $relativePath"
        }
    }
}
else {
    Copy-Item -LiteralPath "$repository/managed/harness/scenarios" -Destination $stage -Recurse
}
Get-ChildItem -LiteralPath $stage -File -Recurse | ForEach-Object {
    [ordered]@{
        file = [IO.Path]::GetRelativePath($stage, $_.FullName).Replace('\', '/')
        sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash
    }
} | ConvertTo-Json | Set-Content -LiteralPath "$stage/manifest.json"
Write-Output $stage
