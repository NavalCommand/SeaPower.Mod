param([Parameter(Mandatory)][string]$HostExecutable, [Parameter(Mandatory)][string]$Cli, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$readinessTimeoutSeconds = 15
$readinessPollMilliseconds = 100
New-Item -ItemType Directory $OutputDirectory -Force | Out-Null
# Each scenario starts a fresh provider so native-style route protection is not bypassed.
foreach ($scenario in @(
    @{ Switch = '--smoke'; Prefix = '' },
    @{ Switch = '--movement-smoke'; Prefix = 'routes-' }
)) {
    $hostLog = Join-Path $OutputDirectory ($scenario.Prefix + 'host.log')
    $hostErrorLog = Join-Path $OutputDirectory ($scenario.Prefix + 'host-error.log')
    $cliLog = Join-Path $OutputDirectory ($scenario.Prefix + 'cli.log')
    $hostStartParameters = @{
        FilePath               = $HostExecutable
        ArgumentList           = '127.0.0.1:50052'
        WindowStyle            = 'Hidden'
        PassThru               = $true
        RedirectStandardOutput = $hostLog
        RedirectStandardError  = $hostErrorLog
    }
    $hostProcess = Start-Process @hostStartParameters
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds($readinessTimeoutSeconds)
        while (!(Test-Path $hostLog) -or !(Select-String -Path $hostLog -Pattern MOCK_READY -Quiet)) {
            if ($hostProcess.HasExited) {
                throw 'Mock host exited'
            }
            if ([DateTime]::UtcNow -gt $deadline) {
                throw 'Mock readiness timeout'
            }
            Start-Sleep -Milliseconds $readinessPollMilliseconds
        }
        & $Cli --endpoint 127.0.0.1:50052 $scenario.Switch *> $cliLog
        if ($LASTEXITCODE) {
            throw "Mock CLI $($scenario.Switch) failed: $LASTEXITCODE"
        }
        Get-Content $cliLog
    }
    finally {
        if (!$hostProcess.HasExited) {
            Stop-Process -Id $hostProcess.Id -Force
            $hostProcess.WaitForExit()
        }
    }
}
