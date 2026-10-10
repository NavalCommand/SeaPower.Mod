[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$GameRoot,
    [ValidateSet('Production', 'Harness', 'Combined')][string]$Mode = 'Combined',
    [string]$Package = (Join-Path (Split-Path $PSScriptRoot) 'o/package/0.1.0'),
    [string]$HarnessPackage = (Join-Path (Split-Path $PSScriptRoot) 'o/harness/0.1.0'),
    [string]$Cli,
    [string]$Scenario = (Join-Path (Split-Path $PSScriptRoot) 'managed/harness/scenarios/MinimalCombat.ini'),
    [string]$Side = 'Taskforce1',
    [string]$RuntimeLogDirectory,
    [ValidateRange(1, 300)][int]$TimeoutSeconds = 180,
    [switch]$Smoke,
    [switch]$Blocked,
    [switch]$ReloadTest,
    [switch]$RuntimeDisabled,
    [switch]$NormalExit,
    [switch]$ApproveLoaderChanges
)
$ErrorActionPreference = 'Stop'
if (!$ApproveLoaderChanges) {
    throw 'Pass -ApproveLoaderChanges to explicitly authorize temporary loader/plugin changes and exact rollback.'
}
if (@(Get-Process -Name 'Sea Power' -ErrorAction SilentlyContinue).Count) {
    throw 'Existing game session; refusing replacement'
}
$useRuntime = $Mode -ne 'Harness'
$useHarness = $Mode -ne 'Production'
if (($Smoke -or $Blocked -or $ReloadTest) -and (!$useRuntime -or $RuntimeDisabled -or !$Cli)) {
    throw 'Command/reload checks require the explicitly enabled runtime and external CLI'
}
if ($ReloadTest -and !$useHarness) {
    throw 'Automated reload requires the separately enabled development harness'
}
function Assert-Package([string]$Directory) {
    $resolvedDirectory = (Resolve-Path -LiteralPath $Directory).Path
    $allowedPrefix = $resolvedDirectory.TrimEnd('\') + '\'
    foreach ($entry in Get-Content "$resolvedDirectory/manifest.json" -Raw | ConvertFrom-Json) {
        $file = [IO.Path]::GetFullPath((Join-Path $resolvedDirectory $entry.file))
        if (!$file.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
            (Get-FileHash -LiteralPath $file).Hash -ne $entry.sha256) {
            throw "Invalid package entry/checksum: $($entry.file)"
        }
    }
    return $resolvedDirectory
}
$runtimeStage = $null
$harnessStage = $null
if ($useRuntime) {
    $runtimeStage = Assert-Package $Package
}
if ($useHarness) {
    $harnessStage = Assert-Package $HarnessPackage
    & "$PSScriptRoot/Prepare-LaunchRequest.ps1" -Scenario $Scenario -Side $Side -OutputDirectory $OutputDirectory -GameRoot $GameRoot | Out-Null
}
else {
    if (Test-Path -LiteralPath $OutputDirectory) {
        throw 'Choose a new output directory'
    }
    $outputPath = [IO.Path]::GetFullPath($OutputDirectory)
    $gamePath = [IO.Path]::GetFullPath($GameRoot).TrimEnd('\')
    if ($outputPath -eq $gamePath -or $outputPath.StartsWith($gamePath + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Keep evidence outside the game installation'
    }
    $expectedHash = '0FE6AAF2A6D9982E7E2EB41FD6035CB97C149773202C90821D71C00A9FDEB01A'
    if ((Get-FileHash "$GameRoot/Sea Power_Data/Managed/Seapower-Scripts.dll").Hash -ne $expectedHash) {
        throw 'Game assembly changed; reassess bootstrap before running'
    }
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
}
$runDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$runtimePluginDirectory = Join-Path $GameRoot 'BepInEx/plugins/NavalCommand.Runtime'
$harnessPluginDirectory = Join-Path $GameRoot 'BepInEx/plugins/NavalCommand.Harness'
foreach ($directory in @($runtimePluginDirectory, $harnessPluginDirectory)) {
    if (Test-Path -LiteralPath $directory) {
        throw "An existing deployment must be left untouched: $directory"
    }
}
$doorstopPath = Join-Path $GameRoot 'doorstop_config.ini'
$originalDoorstop = [IO.File]::ReadAllBytes($doorstopPath)
$originalDoorstopHash = (Get-FileHash -LiteralPath $doorstopPath).Hash
$executable = (Resolve-Path -LiteralPath (Join-Path $GameRoot 'Sea Power.exe')).Path
$startInfo = [Diagnostics.ProcessStartInfo]::new($executable)
$startInfo.WorkingDirectory = $GameRoot
$startInfo.UseShellExecute = $false
# Isolate this child from inherited activation or experiment settings.
foreach ($key in @($startInfo.Environment.Keys)) {
    if ($key.StartsWith('NC_')) {
        $startInfo.Environment.Remove($key) | Out-Null
    }
}
$startInfo.Environment.Remove('DOORSTOP_DISABLE') | Out-Null
$gameProcess = $null
$installedDirectories = @()
$cleanupMode = 'not-started'
try {
    if ($useRuntime) {
        New-Item -ItemType Directory -Path $runtimePluginDirectory | Out-Null
        $installedDirectories += $runtimePluginDirectory
        Copy-Item "$runtimeStage/*" $runtimePluginDirectory -Recurse
        $startInfo.Environment['NC_MOD_ENABLED'] = $(if ($RuntimeDisabled) { '0' } else { '1' })
        $startInfo.Environment['NC_MOD_LOG_DIRECTORY'] = $(if ($RuntimeLogDirectory) { $RuntimeLogDirectory } else { $runDirectory })
    }
    if ($useHarness) {
        New-Item -ItemType Directory -Path $harnessPluginDirectory | Out-Null
        $installedDirectories += $harnessPluginDirectory
        Copy-Item "$harnessStage/SeaPower.Harness.dll" $harnessPluginDirectory
        $request = Get-Content "$runDirectory/request.json" -Raw | ConvertFrom-Json
        $startInfo.Environment['NC_HARNESS_ENABLED'] = '1'
        $startInfo.Environment['NC_HARNESS_RUN_DIRECTORY'] = $runDirectory
        $startInfo.Environment['NC_HARNESS_SCENARIO'] = $request.scenario
        $startInfo.Environment['NC_HARNESS_SIDE'] = $request.side
        $startInfo.Environment['NC_HARNESS_TOKEN'] = $request.token
        if ($ReloadTest) {
            $startInfo.Environment['NC_HARNESS_RELOAD'] = '1'
        }
    }
    $enabledDoorstop = [regex]::Replace([IO.File]::ReadAllText($doorstopPath), '(?m)^enabled[ \t]*=[ \t]*false[ \t]*\r?$', 'enabled = true')
    [IO.File]::WriteAllText($doorstopPath, $enabledDoorstop)
    $startedUtc = [DateTime]::UtcNow
    $gameProcess = [Diagnostics.Process]::Start($startInfo)
    [ordered]@{ pid = $gameProcess.Id; mode = $Mode; runtimeEnabled = ($useRuntime -and !$RuntimeDisabled); harnessEnabled = $useHarness; executable = $executable } |
        ConvertTo-Json | Set-Content "$runDirectory/launch.json"
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $ready = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($gameProcess.HasExited) {
            throw "Owned game exited before readiness: $($gameProcess.ExitCode)"
        }
        if ($useRuntime -and (Test-Path "$GameRoot/BepInEx/LogOutput.log") -and
            (Get-Item "$GameRoot/BepInEx/LogOutput.log").LastWriteTimeUtc -ge $startedUtc -and
            (Select-String "$GameRoot/BepInEx/LogOutput.log" -Pattern 'Runtime bootstrap failed:' -Quiet)) {
            throw 'Production bootstrap failed; see archived loader.log'
        }
        foreach ($errorFile in @('evidence-error.json', 'gateway-error.txt')) {
            if (Test-Path "$runDirectory/$errorFile") {
                throw (Get-Content "$runDirectory/$errorFile" -Raw)
            }
        }
        $ready = !$useHarness
        if ($useHarness -and (Test-Path "$runDirectory/ready.json")) {
            $receipt = Get-Content "$runDirectory/ready.json" -Raw | ConvertFrom-Json
            $ready = $receipt.ready -eq $true -and $receipt.token -eq $request.token -and $receipt.pid -eq $gameProcess.Id
        }
        if ($ready -and $useRuntime -and !$RuntimeDisabled) {
            $ready = Test-Path "$runDirectory/gateway-ready.json"
        }
        if ($ready -and (!$RuntimeDisabled -or $useHarness)) {
            break
        }
        if ($RuntimeDisabled -and !$useHarness -and (Test-Path "$GameRoot/BepInEx/LogOutput.log") -and
            (Get-Item "$GameRoot/BepInEx/LogOutput.log").LastWriteTimeUtc -ge $startedUtc -and
            (Select-String "$GameRoot/BepInEx/LogOutput.log" -Pattern 'Production runtime disabled' -Quiet)) {
            $ready = $true
            break
        }
        Start-Sleep -Milliseconds 200
    }
    if (!$ready) {
        throw "Readiness timeout after $TimeoutSeconds seconds"
    }
    if ($Cli -and $useRuntime -and !$RuntimeDisabled) {
        $config = Get-Content "$runtimeStage/config.json" -Raw | ConvertFrom-Json
        if ($Mode -eq 'Production') {
            # Production never selects a mission. Wait for a player-selected playable view.
            $viewAvailable = $false
            while ([DateTime]::UtcNow -lt $deadline -and !$gameProcess.HasExited) {
                & $Cli --endpoint $config.endpoint *> "$runDirectory/readiness-view.log"
                if (!$LASTEXITCODE) {
                    $viewAvailable = $true
                    break
                }
                Start-Sleep -Milliseconds 500
            }
            if (!$viewAvailable) {
                throw 'No player-selected playable view before timeout'
            }
        }
        $cliArguments = @('--endpoint', $config.endpoint)
        if ($Smoke) { $cliArguments += '--smoke' }
        if ($Blocked) { $cliArguments += '--blocked' }
        if ($ReloadTest) { $cliArguments += '--reload' }
        & $Cli @cliArguments *> "$runDirectory/cli.log"
        $cliExitCode = $LASTEXITCODE
        if ($cliExitCode) {
            throw "CLI failed: $cliExitCode"
        }
    }
    [ordered]@{ ready = $ready; mode = $Mode; cliExitCode = $cliExitCode; optedIntoMutation = [bool]($Smoke -or $Blocked); finishedUtc = [DateTime]::UtcNow.ToString('o') } |
        ConvertTo-Json | Set-Content "$runDirectory/result.json"
    if ($NormalExit) {
        # WM_CLOSE asks Unity to close normally; forced fallback is recorded separately.
        $gameProcess.Refresh()
        if ($gameProcess.MainWindowHandle -eq [IntPtr]::Zero -or !$gameProcess.CloseMainWindow() -or !$gameProcess.WaitForExit(15000)) {
            throw 'Normal exit did not complete within 15 seconds'
        }
        $cleanupMode = 'normal-window-close'
    }
}
finally {
    if ($gameProcess -and !$gameProcess.HasExited) {
        # The process object was created by this invocation, never recovered from a stale PID receipt.
        Stop-Process -Id $gameProcess.Id -Force
        $gameProcess.WaitForExit()
        $cleanupMode = 'forced-owned-process-cleanup'
    }
    [IO.File]::WriteAllBytes($doorstopPath, $originalDoorstop)
    try {
        if (Test-Path "$GameRoot/BepInEx/LogOutput.log") {
            Copy-Item "$GameRoot/BepInEx/LogOutput.log" "$runDirectory/loader.log"
        }
    }
    catch {
        Write-Warning "Could not archive loader log: $_"
    }
    $restored = (Get-FileHash -LiteralPath $doorstopPath).Hash -eq $originalDoorstopHash
    foreach ($directory in $installedDirectories) {
        $resolvedDirectory = [IO.Path]::GetFullPath($directory)
        $pluginRoot = [IO.Path]::GetFullPath((Join-Path $GameRoot 'BepInEx/plugins')).TrimEnd('\') + '\'
        if (!$resolvedDirectory.StartsWith($pluginRoot, [StringComparison]::OrdinalIgnoreCase) -or
            (Split-Path $resolvedDirectory -Leaf) -notin @('NavalCommand.Runtime', 'NavalCommand.Harness')) {
            throw 'Refusing cleanup outside owned plugin directories'
        }
        Remove-Item -LiteralPath $resolvedDirectory -Recurse -Force
    }
    [ordered]@{ restoredDoorstop = $restored; restoredSha256 = (Get-FileHash $doorstopPath).Hash; cleanup = $cleanupMode; gameExitCode = $(if ($gameProcess -and $gameProcess.HasExited) { $gameProcess.ExitCode } else { $null }); ownedPid = $(if ($gameProcess) { $gameProcess.Id } else { 0 }) } |
        ConvertTo-Json | Set-Content "$runDirectory/cleanup.json"
    if (!$restored) {
        throw 'Loader configuration rollback failed'
    }
}
