param([Parameter(Mandatory)][string]$GameRoot)
$ErrorActionPreference = 'Stop'
dotnet build "$PSScriptRoot/../managed/harness/SeaPower.Harness.csproj" -c Release "-p:GameManaged=$GameRoot/Sea Power_Data/Managed" --nologo
if ($LASTEXITCODE) {
    throw 'Harness build failed'
}
$stage = [IO.Path]::GetFullPath("$PSScriptRoot/../out/harness/0.1.0")
if (Test-Path -LiteralPath $stage) {
    Move-Item -LiteralPath $stage -Destination ($stage + '.previous-' + [Guid]::NewGuid().ToString('N'))
}
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item "$PSScriptRoot/../managed/harness/bin/Release/netstandard2.1/SeaPower.Harness.dll" $stage
Copy-Item -Recurse "$PSScriptRoot/../managed/harness/scenarios" $stage
Get-ChildItem -LiteralPath $stage -File -Recurse | ForEach-Object {
    [ordered]@{
        file = [IO.Path]::GetRelativePath($stage, $_.FullName).Replace('\', '/')
        sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash
    }
} | ConvertTo-Json | Set-Content "$stage/manifest.json"
Write-Output $stage
