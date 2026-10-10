[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Scenario,
    [Parameter(Mandatory)][string]$Side,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$GameRoot,
    [ValidateRange(1, 300)][int]$TimeoutSeconds = 180,
    [switch]$ApproveLoaderChanges
)
# Compatibility entry point: readiness receipts retain token/PID; cleanup now always owns the run.
& "$PSScriptRoot/Start-Live.ps1" -Mode Harness -Scenario $Scenario -Side $Side -OutputDirectory $OutputDirectory -GameRoot $GameRoot -TimeoutSeconds $TimeoutSeconds -ApproveLoaderChanges:$ApproveLoaderChanges
