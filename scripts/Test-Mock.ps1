param([Parameter(Mandatory)][string]$HostExecutable, [Parameter(Mandatory)][string]$Cli, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$readinessTimeoutSeconds = 15
$readinessPollMilliseconds = 100
New-Item -ItemType Directory $OutputDirectory -Force | Out-Null
$hostStartParameters = @{
    FilePath               = $HostExecutable
    ArgumentList           = '127.0.0.1:50052'
    WindowStyle            = 'Hidden'
    PassThru               = $true
    RedirectStandardOutput = "$OutputDirectory/host.log"
    RedirectStandardError  = "$OutputDirectory/host-error.log"
}
$hostProcess = Start-Process @hostStartParameters
try {
    $deadline = [DateTime]::UtcNow.AddSeconds($readinessTimeoutSeconds)
    while (!(Test-Path "$OutputDirectory/host.log") -or !(Select-String -Path "$OutputDirectory/host.log" -Pattern MOCK_READY -Quiet)) {
        if ($hostProcess.HasExited) {
            throw 'Mock host exited'
        }
        if ([DateTime]::UtcNow -gt $deadline) {
            throw 'Mock readiness timeout'
        }
        Start-Sleep -Milliseconds $readinessPollMilliseconds
    }
    & $Cli --endpoint 127.0.0.1:50052 --smoke *> "$OutputDirectory/cli.log"
    if ($LASTEXITCODE) {
        throw "Mock CLI failed: $LASTEXITCODE"
    }
    Get-Content "$OutputDirectory/cli.log"

}
finally {
    if (!$hostProcess.HasExited) {
        Stop-Process -Id $hostProcess.Id -Force
    }
}
