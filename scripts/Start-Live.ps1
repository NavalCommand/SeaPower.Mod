[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory,
 [Parameter(Mandatory)][string]$GameRoot,
 [string]$Package=(Join-Path (Split-Path $PSScriptRoot) 'out/package/0.1.0'),
 [Parameter(Mandatory)][string]$Cli,
 [string]$Scenario=(Join-Path (Split-Path $PSScriptRoot) 'app/harness/MinimalCombat.ini'),
 [switch]$Smoke,[switch]$Blocked,[switch]$ReloadTest,[switch]$KeepRunning)
$ErrorActionPreference='Stop'
if($KeepRunning){throw 'This safety wrapper always closes its owned test process; use manual deployment for extended debugging.'}
if(@(Get-Process -Name 'Sea Power' -ErrorAction SilentlyContinue).Count){throw 'Existing game session; refusing replacement'}
$packagePath=(Resolve-Path $Package).Path
$config=Get-Content "$packagePath/config.json" -Raw | ConvertFrom-Json
if($config.endpoint -notmatch '^127\.0\.0\.1:([0-9]+)$'){throw 'Loopback required'}
$port=[int]$Matches[1];if($port -lt 1024 -or $port -gt 65535){throw 'Invalid port'}
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$port)
try{$listener.Start()}finally{$listener.Stop()}
foreach($item in Get-Content "$packagePath/manifest.json" -Raw | ConvertFrom-Json){if((Get-FileHash "$packagePath/$($item.file)").Hash -ne $item.sha256){throw 'Package checksum mismatch'}}
$plugin=Join-Path $GameRoot 'BepInEx/plugins/LaunchCandidate.dll'
$before=[IO.File]::ReadAllBytes($plugin)
$backup=Join-Path $env:TEMP ('naval-task004-plugin-backup-'+[Guid]::NewGuid().ToString('N')+'.dll')
[IO.File]::WriteAllBytes($backup,$before)
$environment=@{}
foreach($name in @('NC_TASK004_ENABLED','NC_TASK004_MOD_ASSEMBLY','NC_TASK004_NATIVE_DLL','NC_TASK004_ENDPOINT','NC_TASK004_RELOAD_TEST')){$environment[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
$ownedPid=0
try{
 Copy-Item "$packagePath/Task004.LaunchCandidate.dll" $plugin
 $env:NC_TASK004_ENABLED='1';$env:NC_TASK004_MOD_ASSEMBLY="$packagePath/SeaPower.Mod.dll";$env:NC_TASK004_NATIVE_DLL="$packagePath/SeaPower.Host.dll";$env:NC_TASK004_ENDPOINT=$config.endpoint
 if($ReloadTest){$env:NC_TASK004_RELOAD_TEST='1'}else{$env:NC_TASK004_RELOAD_TEST=$null}
 & "$PSScriptRoot/../app/harness/Start-Validation.ps1" -Scenario $Scenario -Side Taskforce1 -OutputDirectory $OutputDirectory -GameRoot $GameRoot | Set-Content "$OutputDirectory/launch-output.json"
 $launch=Get-Content "$OutputDirectory/launch.json" -Raw | ConvertFrom-Json;$ownedPid=$launch.pid
 $ready=Join-Path $OutputDirectory 'gateway-ready.json'
 $deadline=[DateTime]::UtcNow.AddSeconds(30)
 while(!(Test-Path $ready)){
  if(Test-Path "$OutputDirectory/gateway-error.txt"){throw (Get-Content "$OutputDirectory/gateway-error.txt" -Raw)}
  if([DateTime]::UtcNow -gt $deadline){throw 'In-process gateway readiness timeout'}
  Start-Sleep -Milliseconds 100
 }
 $argsList=@('--endpoint',$config.endpoint)
 if($Smoke){$argsList+='--smoke'}
 if($Blocked){$argsList+='--blocked'}
 if($ReloadTest){$argsList+='--reload'}
 & $Cli @argsList *> "$OutputDirectory/cli.log"
 $code=$LASTEXITCODE
 @{cliExitCode=$code;optedIntoMutation=[bool]($Smoke-or $Blocked);package=(Get-FileHash "$packagePath/SeaPower.Host.dll").Hash;finishedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content "$OutputDirectory/smoke-result.json"
 if($code){throw "CLI failed: $code"}
 Get-Content "$OutputDirectory/cli.log"
}finally{
 if(!$ownedPid -and (Test-Path "$OutputDirectory/launch.json")){$ownedPid=(Get-Content "$OutputDirectory/launch.json" -Raw | ConvertFrom-Json).pid}
 if($ownedPid){$p=Get-Process -Id $ownedPid -ErrorAction SilentlyContinue;if($p -and $p.Path -eq (Join-Path $GameRoot 'Sea Power.exe')){Stop-Process -Id $ownedPid -Force;Wait-Process -Id $ownedPid -Timeout 10 -ErrorAction SilentlyContinue}}
 $restored=$false
 for($attempt=0;$attempt -lt 50 -and !$restored;$attempt++){try{[IO.File]::WriteAllBytes($plugin,$before);$restored=$true}catch{Start-Sleep -Milliseconds 100}}
 if(!$restored){throw "Plugin remains locked. Exact backup retained at $backup"}
 if(Test-Path $OutputDirectory){@{restoredSha256=(Get-FileHash $plugin).Hash;backup=$backup;ownedPid=$ownedPid} | ConvertTo-Json | Set-Content "$OutputDirectory/cleanup.json"}
 foreach($name in $environment.Keys){[Environment]::SetEnvironmentVariable($name,$environment[$name],'Process')}
}
