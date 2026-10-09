param([Parameter(Mandatory)][string]$HostExecutable,[Parameter(Mandatory)][string]$Cli,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory $OutputDirectory -Force | Out-Null
$hostProcess=Start-Process -FilePath $HostExecutable -ArgumentList '127.0.0.1:50052' -WindowStyle Hidden -PassThru -RedirectStandardOutput "$OutputDirectory/host.log" -RedirectStandardError "$OutputDirectory/host-error.log"
try{
 $deadline=[DateTime]::UtcNow.AddSeconds(15)
 while(!(Test-Path "$OutputDirectory/host.log")-or !(Select-String -Path "$OutputDirectory/host.log" -Pattern MOCK_READY -Quiet)){if($hostProcess.HasExited){throw 'Mock host exited'};if([DateTime]::UtcNow -gt $deadline){throw 'Mock readiness timeout'};Start-Sleep -Milliseconds 100}
 & $Cli --endpoint 127.0.0.1:50052 --smoke *> "$OutputDirectory/cli.log"
 if($LASTEXITCODE){throw "Mock CLI failed: $LASTEXITCODE"}
 Get-Content "$OutputDirectory/cli.log"
}finally{if(!$hostProcess.HasExited){Stop-Process -Id $hostProcess.Id -Force}}

