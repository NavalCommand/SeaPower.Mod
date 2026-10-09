[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Scenario,
    [Parameter(Mandatory)][string]$Side,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$GameRoot,
    [ValidateRange(1, 300)][int]$TimeoutSeconds = 180,
    [switch]$RunProbes,
    [switch]$ObserveOnly,
    [switch]$DeliberateInterference,
    [switch]$LifecycleTest,
    [switch]$AirOps,
    [switch]$CloseAfterReady
)
$ErrorActionPreference = 'Stop'
$readinessPollMilliseconds = 200
$partialReceiptRetryMilliseconds = 100
$ownedProcessExitTimeoutSeconds = 10
$executable = Join-Path $GameRoot 'Sea Power.exe'
if (@(Get-Process -Name 'Sea Power' -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'A Sea Power session exists; close the intended experiment before starting another.'

}
if (!(Test-Path -LiteralPath (Join-Path $GameRoot 'BepInEx/plugins/LaunchCandidate.dll'))) {
    throw 'Approved Task002 plugin is not installed.'

}
& (Join-Path $PSScriptRoot 'Prepare-LaunchRequest.ps1') -Scenario $Scenario -Side $Side -OutputDirectory $OutputDirectory -GameRoot $GameRoot | Out-Null
$runDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$launchRequest = Get-Content -LiteralPath (Join-Path $runDirectory 'request.json') -Raw | ConvertFrom-Json
$startInfo = [Diagnostics.ProcessStartInfo]::new($executable)
$startInfo.WorkingDirectory = $GameRoot
$startInfo.UseShellExecute = $false
$startInfo.Environment['NC_TASK002_ENABLED'] = '1'
$startInfo.Environment['NC_TASK002_RUN_DIR'] = $runDirectory
$startInfo.Environment['NC_TASK002_SCENARIO'] = $launchRequest.scenario
$startInfo.Environment['NC_TASK002_SIDE'] = $launchRequest.side
$startInfo.Environment['NC_TASK002_TOKEN'] = $launchRequest.token
if ($RunProbes) {
    $startInfo.Environment['NC_TASK002_PROBES'] = '1'
}
else {
    $startInfo.Environment.Remove('NC_TASK002_PROBES') | Out-Null
}
if ($ObserveOnly) {
    $startInfo.Environment['NC_TASK002_OBSERVE_ONLY'] = '1'
}
else {
    $startInfo.Environment.Remove('NC_TASK002_OBSERVE_ONLY') | Out-Null
}
if ($DeliberateInterference) {
    $startInfo.Environment['NC_TASK002_INTERFERE'] = '1'
}
else {
    $startInfo.Environment.Remove('NC_TASK002_INTERFERE') | Out-Null
}
if ($LifecycleTest) {
    $startInfo.Environment['NC_TASK002_LIFECYCLE'] = '1'
}
else {
    $startInfo.Environment.Remove('NC_TASK002_LIFECYCLE') | Out-Null
}
$startInfo.Environment.Remove('DOORSTOP_DISABLE') | Out-Null
if ($AirOps) {
    $startInfo.Environment['NC_TASK003_AIROPS'] = '1'
}
else {
    $startInfo.Environment.Remove('NC_TASK003_AIROPS') | Out-Null
}
if ($AirOps -and $RunProbes) {
    throw 'AirOps and Task002 scripted diagnostics must run separately.'
}
$doorstopConfigPath = Join-Path $GameRoot 'doorstop_config.ini'
$previousDoorstopBytes = [IO.File]::ReadAllBytes($doorstopConfigPath)
$previousDoorstop = Get-Content -LiteralPath $doorstopConfigPath -Raw
$enabledDoorstop = [regex]::Replace($previousDoorstop, '(?m)^enabled[ \t]*=[ \t]*false[ \t]*\r?$', 'enabled = true')
[IO.File]::WriteAllText($doorstopConfigPath, $enabledDoorstop)
try {
    $launchStopwatch = [Diagnostics.Stopwatch]::StartNew()
    $gameProcess = [Diagnostics.Process]::Start($startInfo)
    $launchReceipt = @{
        pid            =$gameProcess.Id
        scenario       =$launchRequest.scenario
        side           =$launchRequest.side
        token          =$launchRequest.token
        startedUtc     =[DateTime]::UtcNow.ToString('o')
        assemblySha256 =$launchRequest.assemblySha256
    }
    $launchReceipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runDirectory 'launch.json')
    while ($launchStopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        if ($gameProcess.HasExited) {
            throw "Game exited before readiness: $($gameProcess.ExitCode)"
        }
        $readyPath = Join-Path $runDirectory 'ready.json'
        if (Test-Path -LiteralPath (Join-Path $runDirectory 'evidence-error.json')) {
            throw 'Evidence writer reported an error; refusing readiness.'
        }
        if (Test-Path -LiteralPath $readyPath) {
            try {
                $readinessReceipt = Get-Content -LiteralPath $readyPath -Raw | ConvertFrom-Json
            }
            catch {
                Start-Sleep -Milliseconds $partialReceiptRetryMilliseconds
                continue
            }
            if ($readinessReceipt.ready -eq $true -and $readinessReceipt.token -eq $launchRequest.token -and $readinessReceipt.pid -eq $gameProcess.Id) {
                $launchReceipt.ready = $true
                $launchReceipt.secondsToReady = $launchStopwatch.Elapsed.TotalSeconds
                $launchReceipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runDirectory 'result.json')
                if ($CloseAfterReady) {
                    Stop-Process -Id $gameProcess.Id -Force
                    Wait-Process -Id $gameProcess.Id -Timeout $ownedProcessExitTimeoutSeconds -ErrorAction SilentlyContinue
                }
                $launchReceipt | ConvertTo-Json
                return

            }

        }
        Start-Sleep -Milliseconds $readinessPollMilliseconds

    }
    throw "Readiness timeout after $TimeoutSeconds seconds. Experiment PID $($gameProcess.Id) remains for diagnosis."

}
finally {
    [IO.File]::WriteAllBytes($doorstopConfigPath, $previousDoorstopBytes)
}
