[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Scenario,
    [Parameter(Mandatory)][string]$Side,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$GameRoot='C:\Games\Sea_Power_8_3',
    [ValidateRange(1,300)][int]$TimeoutSeconds=180,
    [switch]$RunProbes,
    [switch]$ObserveOnly,
    [switch]$DeliberateInterference,
    [switch]$LifecycleTest,
    [switch]$AirOps,
    [switch]$CloseAfterReady
)
$ErrorActionPreference='Stop'
$executable=Join-Path $GameRoot 'Sea Power.exe'
if (@(Get-Process -Name 'Sea Power' -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'A Sea Power session exists; close the intended experiment before starting another.'
}
if (!(Test-Path -LiteralPath (Join-Path $GameRoot 'BepInEx/plugins/LaunchCandidate.dll'))) {
    throw 'Approved Task002 plugin is not installed.'
}
& (Join-Path $PSScriptRoot 'Prepare-LaunchRequest.ps1') -Scenario $Scenario -Side $Side -OutputDirectory $OutputDirectory -GameRoot $GameRoot | Out-Null
$directory=[IO.Path]::GetFullPath($OutputDirectory)
$request=Get-Content -LiteralPath (Join-Path $directory 'request.json') -Raw | ConvertFrom-Json
$startInfo=[Diagnostics.ProcessStartInfo]::new($executable)
$startInfo.WorkingDirectory=$GameRoot
$startInfo.UseShellExecute=$false
$startInfo.Environment['NC_TASK002_ENABLED']='1'
$startInfo.Environment['NC_TASK002_RUN_DIR']=$directory
$startInfo.Environment['NC_TASK002_SCENARIO']=$request.scenario
$startInfo.Environment['NC_TASK002_SIDE']=$request.side
$startInfo.Environment['NC_TASK002_TOKEN']=$request.token
if($RunProbes){$startInfo.Environment['NC_TASK002_PROBES']='1'}else{$startInfo.Environment.Remove('NC_TASK002_PROBES') | Out-Null}
if($ObserveOnly){$startInfo.Environment['NC_TASK002_OBSERVE_ONLY']='1'}else{$startInfo.Environment.Remove('NC_TASK002_OBSERVE_ONLY') | Out-Null}
if($DeliberateInterference){$startInfo.Environment['NC_TASK002_INTERFERE']='1'}else{$startInfo.Environment.Remove('NC_TASK002_INTERFERE') | Out-Null}
if($LifecycleTest){$startInfo.Environment['NC_TASK002_LIFECYCLE']='1'}else{$startInfo.Environment.Remove('NC_TASK002_LIFECYCLE') | Out-Null}
$startInfo.Environment.Remove('DOORSTOP_DISABLE') | Out-Null
if($AirOps){$startInfo.Environment['NC_TASK003_AIROPS']='1'}else{$startInfo.Environment.Remove('NC_TASK003_AIROPS') | Out-Null}
if($AirOps -and $RunProbes){throw 'AirOps and Task002 scripted diagnostics must run separately.'}
$doorstop=Join-Path $GameRoot 'doorstop_config.ini'
$previousDoorstopBytes=[IO.File]::ReadAllBytes($doorstop)
$previousDoorstop=Get-Content -LiteralPath $doorstop -Raw
$enabledDoorstop=[regex]::Replace($previousDoorstop,'(?m)^enabled[ \t]*=[ \t]*false[ \t]*\r?$','enabled = true')
[IO.File]::WriteAllText($doorstop,$enabledDoorstop)
try {
$clock=[Diagnostics.Stopwatch]::StartNew()
$process=[Diagnostics.Process]::Start($startInfo)
$launch=@{pid=$process.Id;scenario=$request.scenario;side=$request.side;token=$request.token;startedUtc=[DateTime]::UtcNow.ToString('o');assemblySha256=$request.assemblySha256}
$launch | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'launch.json')
while($clock.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
    if($process.HasExited) { throw "Game exited before readiness: $($process.ExitCode)" }
    $readyPath=Join-Path $directory 'ready.json'
    if(Test-Path -LiteralPath (Join-Path $directory 'evidence-error.json')) { throw 'Evidence writer reported an error; refusing readiness.' }
    if(Test-Path -LiteralPath $readyPath) {
        try { $ready=Get-Content -LiteralPath $readyPath -Raw | ConvertFrom-Json } catch { Start-Sleep -Milliseconds 100; continue }
        if($ready.ready -eq $true -and $ready.token -eq $request.token -and $ready.pid -eq $process.Id) {
            $launch.ready=$true
            $launch.secondsToReady=$clock.Elapsed.TotalSeconds
            $launch | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'result.json')
            if($CloseAfterReady){Stop-Process -Id $process.Id -Force; Wait-Process -Id $process.Id -Timeout 10 -ErrorAction SilentlyContinue}
            $launch | ConvertTo-Json
            return
        }
    }
    Start-Sleep -Milliseconds 200
}
throw "Readiness timeout after $TimeoutSeconds seconds. Experiment PID $($process.Id) remains for diagnosis."
} finally { [IO.File]::WriteAllBytes($doorstop,$previousDoorstopBytes) }
