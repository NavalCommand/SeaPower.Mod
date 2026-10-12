<#
.SYNOPSIS
Select a local test mission and run it with the staged harness and runtime.
.DESCRIPTION
Temporarily deploys both mods through Start-Live.ps1 and enables the existing
loader. Keeps the game open until you quit it, then restores loader configuration
and removes the temporary plugins. Keep this PowerShell window open until cleanup.
Packages must already be built. The game installation defaults to the configured
managed build context; -GameRoot overrides it. No request.json editing is needed.
.EXAMPLE
pwsh -File ./scripts/Start-TestMission.ps1
.EXAMPLE
./scripts/Start-TestMission.ps1 -ScenarioName NearContact -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$GameRoot,
    [string]$ScenarioName,
    [string]$Package,
    [string]$HarnessPackage,
    [ValidateRange(1, 300)][int]$TimeoutSeconds = 180
)
$ErrorActionPreference = 'Stop'
# The process environment APIs used by Start-Live require PowerShell 7/.NET.
# Hand off before deployment so the ordinary Windows PowerShell console works.
if ($PSVersionTable.PSVersion.Major -lt 7) {
    $powerShellCommand = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    $powerShellCandidates = @(
        $(if ($powerShellCommand) { $powerShellCommand.Source }),
        (Join-Path $env:ProgramFiles 'PowerShell/7/pwsh.exe'),
        (Join-Path $env:LOCALAPPDATA 'Microsoft/WinGet/Links/pwsh.exe'),
        (Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe')
    )
    $powerShellExecutable = $powerShellCandidates |
        Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
        Select-Object -First 1
    if (!$powerShellExecutable) {
        throw 'PowerShell 7 is required by Start-Live.ps1. Install it, then run this script again or use pwsh -File Start-TestMission.ps1.'
    }
    $forwardedArguments = @('-NoProfile', '-File', $PSCommandPath)
    foreach ($parameterName in $PSBoundParameters.Keys) {
        $parameterValue = $PSBoundParameters[$parameterName]
        if ($parameterValue -is [Management.Automation.SwitchParameter]) {
            if ($parameterValue.IsPresent) {
                $forwardedArguments += "-$parameterName"
            }
            else {
                $forwardedArguments += ('-{0}:$false' -f $parameterName)
            }
        }
        else {
            $forwardedArguments += "-$parameterName"
            $forwardedArguments += [string]$parameterValue
        }
    }
    Write-Host "Using PowerShell 7: $powerShellExecutable"
    & $powerShellExecutable @forwardedArguments
    if ($LASTEXITCODE) {
        throw "PowerShell 7 launcher failed with exit code $LASTEXITCODE"
    }
    return
}
$moduleRoot = Split-Path $PSScriptRoot
if (!$Package) {
    $Package = Join-Path $moduleRoot 'o/package/0.1.0'
}
if (!$HarnessPackage) {
    $HarnessPackage = Join-Path $moduleRoot 'o/harness/0.1.0'
}
$scenarioDirectory = Join-Path $moduleRoot 'managed/harness/scenarios'
$scenarios = @(Get-ChildItem -LiteralPath $scenarioDirectory -File -Filter '*.ini' | Sort-Object Name)
if (!$scenarios.Count) {
    throw "No test missions found in $scenarioDirectory"
}

$scenario = $null
if ($ScenarioName) {
    $matchesByName = @($scenarios | Where-Object { $_.Name -ieq $ScenarioName -or $_.BaseName -ieq $ScenarioName })
    if ($matchesByName.Count -ne 1) {
        throw "Unknown scenario '$ScenarioName'. Available: $($scenarios.Name -join ', ')"
    }
    $scenario = $matchesByName[0]
}
else {
    Write-Host 'Select a Sea Power test mission:'
    for ($index = 0; $index -lt $scenarios.Count; ++$index) {
        Write-Host ('  {0}. {1}' -f ($index + 1), $scenarios[$index].BaseName)
    }
    while (!$scenario) {
        $selection = Read-Host 'Mission number (Q to cancel)'
        if ($selection -ieq 'q') {
            return
        }
        $number = 0
        if ([int]::TryParse($selection, [ref]$number) -and $number -ge 1 -and $number -le $scenarios.Count) {
            $scenario = $scenarios[$number - 1]
        }
        else {
            Write-Host "Enter a number from 1 to $($scenarios.Count), or Q."
        }
    }
}

# Use the mission's actual player side; arbitrary side swapping is unverified.
$isMissionSection = $false
$playerSide = $null
foreach ($line in Get-Content -LiteralPath $scenario.FullName) {
    if ($line -match '^\s*\[([^]]+)\]') {
        $isMissionSection = $Matches[1] -eq 'Mission'
        continue
    }
    if ($isMissionSection -and $line -match '^\s*PlayerTaskforce\s*=\s*([^;]+)') {
        $playerSide = $Matches[1].Trim()
    }
}
if (!$playerSide) {
    throw "Scenario has no Mission.PlayerTaskforce: $($scenario.FullName)"
}

if (!$GameRoot) {
    $contextPath = Join-Path $moduleRoot 'o/managed/Build.Context.props'
    if (Test-Path -LiteralPath $contextPath) {
        [xml]$context = Get-Content -LiteralPath $contextPath -Raw
        $gameManaged = [string]$context.Project.PropertyGroup.GameManaged
        if ($gameManaged) {
            $GameRoot = [IO.Path]::GetFullPath((Join-Path $gameManaged '../..'))
        }
    }
    if (!$GameRoot) {
        $GameRoot = Read-Host 'Sea Power installation directory (contains Sea Power.exe)'
    }
}
if (!$GameRoot -or !(Test-Path -LiteralPath (Join-Path $GameRoot 'Sea Power.exe') -PathType Leaf)) {
    throw 'Specify -GameRoot pointing to the installed Sea Power directory.'
}
$GameRoot = (Resolve-Path -LiteralPath $GameRoot).Path

foreach ($stage in @($Package, $HarnessPackage)) {
    if (!(Test-Path -LiteralPath (Join-Path $stage 'manifest.json') -PathType Leaf)) {
        throw "Package missing at $stage. Build NavalCommand.SeaPower.Mod.PackageRuntime and NavalCommand.SeaPower.Mod.PackageHarness first."
    }
}
$runName = '{0}-{1}-{2}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), $scenario.BaseName, [Guid]::NewGuid().ToString('N')
$outputDirectory = Join-Path $moduleRoot "o/live/$runName"
Write-Host "Scenario: $($scenario.FullName)"
Write-Host "Player side: $playerSide"
Write-Host "Game: $GameRoot"
Write-Host "Session logs: $outputDirectory"

if (!$PSCmdlet.ShouldProcess($GameRoot, 'Temporarily load runtime and harness, launch the selected mission, and restore deployment on game exit')) {
    return
}
$launchOptions = @{
    OutputDirectory = $outputDirectory
    GameRoot = $GameRoot
    Mode = 'Combined'
    Package = $Package
    HarnessPackage = $HarnessPackage
    Scenario = $scenario.FullName
    Side = $playerSide
    TimeoutSeconds = $TimeoutSeconds
    WaitForGameExit = $true
    ApproveLoaderChanges = $true
}
& (Join-Path $PSScriptRoot 'Start-Live.ps1') @launchOptions
