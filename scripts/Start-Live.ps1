[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$GameRoot,
    [string]$Package = (Join-Path (Split-Path $PSScriptRoot) 'out/package/0.1.0'),
    [Parameter(Mandatory)][string]$Cli,
    [string]$Scenario = (Join-Path (Split-Path $PSScriptRoot) 'app/harness/MinimalCombat.ini'),
    [switch]$Smoke, [switch]$Blocked, [switch]$ReloadTest, [switch]$KeepRunning)
$ErrorActionPreference = 'Stop'
$minimumEndpointPort = 1024
$maximumEndpointPort = 65535
$gatewayReadinessTimeoutSeconds = 30
$pollIntervalMilliseconds = 100
$ownedProcessExitTimeoutSeconds = 10
$maximumRestoreAttempts = 50
if ($KeepRunning) {
    throw 'This safety wrapper always closes its owned test process; use manual deployment for extended debugging.'
}
if (@(Get-Process -Name 'Sea Power' -ErrorAction SilentlyContinue).Count) {
    throw 'Existing game session; refusing replacement'
}
$packagePath = (Resolve-Path $Package).Path
$config = Get-Content "$packagePath/config.json" -Raw | ConvertFrom-Json
if ($config.endpoint -notmatch '^127\.0\.0\.1:([0-9]+)$') {
    throw 'Loopback required'
}
$port = [int]$Matches[1]
if ($port -lt $minimumEndpointPort -or $port -gt $maximumEndpointPort) {
    throw 'Invalid port'
}
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $port)
try {
    $listener.Start()
}
finally {
    $listener.Stop()
}
foreach ($manifestEntry in Get-Content "$packagePath/manifest.json" -Raw | ConvertFrom-Json) {
    if ((Get-FileHash "$packagePath/$($manifestEntry.file)").Hash -ne $manifestEntry.sha256) {
        throw 'Package checksum mismatch'
    }
}
$pluginPath = Join-Path $GameRoot 'BepInEx/plugins/LaunchCandidate.dll'
$previousPluginBytes = [IO.File]::ReadAllBytes($pluginPath)
$pluginBackupPath = Join-Path $env:TEMP ('naval-task004-plugin-backup-' + [Guid]::NewGuid().ToString('N') + '.dll')
[IO.File]::WriteAllBytes($pluginBackupPath, $previousPluginBytes)
$previousEnvironment = @{}
foreach ($environmentVariableName in @('NC_TASK004_ENABLED', 'NC_TASK004_MOD_ASSEMBLY', 'NC_TASK004_NATIVE_DLL', 'NC_TASK004_ENDPOINT', 'NC_TASK004_RELOAD_TEST')) {
    $previousEnvironment[$environmentVariableName] = [Environment]::GetEnvironmentVariable($environmentVariableName, 'Process')
}
$ownedPid = 0
try {
    Copy-Item "$packagePath/Task004.LaunchCandidate.dll" $pluginPath
    $env:NC_TASK004_ENABLED = '1'
    $env:NC_TASK004_MOD_ASSEMBLY = "$packagePath/SeaPower.Mod.dll"
    $env:NC_TASK004_NATIVE_DLL = "$packagePath/SeaPower.Host.dll"
    $env:NC_TASK004_ENDPOINT = $config.endpoint
    if ($ReloadTest) {
        $env:NC_TASK004_RELOAD_TEST = '1'
    }
    else {
        $env:NC_TASK004_RELOAD_TEST = $null
    }
    $validationParameters = @{
        Scenario        = $Scenario
        Side            = 'Taskforce1'
        OutputDirectory = $OutputDirectory
        GameRoot        = $GameRoot
    }
    & "$PSScriptRoot/../app/harness/Start-Validation.ps1" @validationParameters | Set-Content "$OutputDirectory/launch-output.json"
    $launch = Get-Content "$OutputDirectory/launch.json" -Raw | ConvertFrom-Json
    $ownedPid = $launch.pid
    $gatewayReadyPath = Join-Path $OutputDirectory 'gateway-ready.json'
    $deadline = [DateTime]::UtcNow.AddSeconds($gatewayReadinessTimeoutSeconds)
    while (!(Test-Path $gatewayReadyPath)) {
        if (Test-Path "$OutputDirectory/gateway-error.txt") {
            throw (Get-Content "$OutputDirectory/gateway-error.txt" -Raw)
        }
        if ([DateTime]::UtcNow -gt $deadline) {
            throw 'In-process gateway readiness timeout'
        }
        Start-Sleep -Milliseconds $pollIntervalMilliseconds

    }
    $cliArguments = @('--endpoint', $config.endpoint)
    if ($Smoke) {
        $cliArguments += '--smoke'
    }
    if ($Blocked) {
        $cliArguments += '--blocked'
    }
    if ($ReloadTest) {
        $cliArguments += '--reload'
    }
    & $Cli @cliArguments *> "$OutputDirectory/cli.log"
    $cliExitCode = $LASTEXITCODE
    @{
        cliExitCode       =$cliExitCode
        optedIntoMutation =[bool]($Smoke -or $Blocked)
        package           =(Get-FileHash "$packagePath/SeaPower.Host.dll").Hash
        finishedUtc       =[DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json | Set-Content "$OutputDirectory/smoke-result.json"
    if ($cliExitCode) {
        throw "CLI failed: $cliExitCode"
    }
    Get-Content "$OutputDirectory/cli.log"

}
finally {
    if (!$ownedPid -and (Test-Path "$OutputDirectory/launch.json")) {
        $ownedPid = (Get-Content "$OutputDirectory/launch.json" -Raw | ConvertFrom-Json).pid
    }
    if ($ownedPid) {
        $ownedProcess = Get-Process -Id $ownedPid -ErrorAction SilentlyContinue
        if ($ownedProcess -and $ownedProcess.Path -eq (Join-Path $GameRoot 'Sea Power.exe')) {
            Stop-Process -Id $ownedPid -Force
            Wait-Process -Id $ownedPid -Timeout $ownedProcessExitTimeoutSeconds -ErrorAction SilentlyContinue
        }
    }
    $restored = $false
    for ($attempt = 0; $attempt -lt $maximumRestoreAttempts -and !$restored; $attempt++) {
        try {
            [IO.File]::WriteAllBytes($pluginPath, $previousPluginBytes)
            $restored = $true
        }
        catch {
            Start-Sleep -Milliseconds $pollIntervalMilliseconds
        }
    }
    if (!$restored) {
        throw "Plugin remains locked. Exact backup retained at $pluginBackupPath"
    }
    if (Test-Path $OutputDirectory) {
        @{
            restoredSha256 =(Get-FileHash $pluginPath).Hash
            backup         =$pluginBackupPath
            ownedPid       =$ownedPid
        } | ConvertTo-Json | Set-Content "$OutputDirectory/cleanup.json"
    }
    foreach ($environmentVariableName in $previousEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($environmentVariableName, $previousEnvironment[$environmentVariableName], 'Process')
    }

}
