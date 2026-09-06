# Native Phase 6 check. Production defaults are untouched; the probe starts short sessions
# through the real engine, then sends the same re-evaluation message as a system clock change.
param([string] $DataRootName = "p6-$([guid]::NewGuid().ToString('N'))")
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$smokeRoot = Join-Path $projectRoot '.smoke'
$dataRoot = [IO.Path]::GetFullPath((Join-Path $smokeRoot $DataRootName))
if (-not $dataRoot.StartsWith($smokeRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $dataRoot)) { throw 'Use a fresh data root strictly under .smoke.' }
if (Get-Process FocusKey -ErrorAction SilentlyContinue) { throw 'Exit the existing Focus Key process before this smoke test.' }
$exe = Join-Path $projectRoot 'src\FocusKey.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\FocusKey.exe'
$probe = Join-Path $smokeRoot 'ShellProbe\bin\Debug\net10.0-windows10.0.19041.0\ShellProbe.exe'
$logFile = Join-Path $dataRoot 'logs\focus_key.log'
$previousRoot = $env:FOCUSKEY_DATA_ROOT
$process = $null
function Read-Log { if (Test-Path -LiteralPath $logFile) { return [string](Get-Content -LiteralPath $logFile -Raw) }; return '' }
function Wait-Until([scriptblock]$Check, [string]$Message) {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do { if (& $Check) { return }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $deadline)
    throw $Message
}
function Invoke-Probe([string[]]$Arguments) {
    $result = & $probe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Probe failed: $Arguments" }
    return $result
}
try {
    dotnet build (Join-Path $smokeRoot 'ShellProbe\ShellProbe.csproj') --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Probe build failed.' }
    $env:FOCUSKEY_DATA_ROOT = $dataRoot
    $process = Start-Process -FilePath $exe -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
    Wait-Until { (Read-Log) -match 'Shell ready:' } 'App did not initialize.'
    $process.Refresh()
    if (-not $process.CloseMainWindow()) { throw 'Could not close the main window.' }
    Wait-Until { (Read-Log) -match 'Main window hidden;' } 'Main window did not hide.'
    foreach ($type in @('work', 'break')) {
        Invoke-Probe @('start-short', [string]$process.Id, $dataRoot, $type, '2')
        Wait-Until { (Read-Log) -match "Completion notification submitted: .* $type;" } "$type completion did not submit a notification."
        Invoke-Probe @('reevaluate', [string]$process.Id)
        Invoke-Probe @('resume', [string]$process.Id)
    }
    # A future session must be Interrupted by explicit Exit, without completion feedback.
    Invoke-Probe @('start-short', [string]$process.Id, $dataRoot, 'work', '120')
    Invoke-Probe @('exit', [string]$process.Id)
    if (-not $process.WaitForExit(30000) -or $process.ExitCode -ne 0) { throw 'Clean exit failed.' }
    $log = Read-Log
    if ([regex]::Matches($log, 'Completion notification submitted:').Count -ne 2) { throw 'Expected exactly two notification submissions.' }
    if ($log -notmatch 'Session shutdown: Interrupted\.' -or $log -notmatch 'Shell stopped:') { throw 'Shutdown was not clean.' }
    $rows = @(Invoke-Probe @('inspect', $dataRoot))
    if ($rows.Count -ne 3 -or @($rows | Where-Object { $_ -match ' Completed ' }).Count -ne 2 -or @($rows | Where-Object { $_ -match ' Interrupted ' }).Count -ne 1) { throw "Unexpected stored sessions: $rows" }
    foreach ($row in $rows | Where-Object { $_ -match ' Completed ' }) {
        if ($row -notmatch 'plannedEnd=(\S+) ended=(\S+)' -or $Matches[1] -ne $Matches[2]) { throw "Incorrect completion timestamp: $row" }
    }
    Invoke-Probe @('hotkey-free')
    Write-Output "PASS: hidden-app Work/Break completion, no duplicate UX on repeated signals, Interrupted shutdown silent. Data root: $dataRoot"
} finally {
    if ($process) {
        $process.Refresh()
        if (-not $process.HasExited) { & $probe exit $process.Id; [void]$process.WaitForExit(30000) }
        $process.Dispose()
    }
    $env:FOCUSKEY_DATA_ROOT = $previousRoot
}
