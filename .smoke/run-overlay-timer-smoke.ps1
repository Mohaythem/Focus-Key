param([string] $DataRootName = "p101-$([guid]::NewGuid().ToString('N'))")
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$data = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $DataRootName))
if (-not $data.StartsWith($PSScriptRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $data)) { throw 'Expected fresh isolated root.' }
if (Get-Process FocusKey -ErrorAction SilentlyContinue) { throw 'Exit existing FocusKey first.' }
$probe = Join-Path $PSScriptRoot 'ShellProbe\bin\Debug\net10.0-windows10.0.19041.0\ShellProbe.exe'
$exe = Join-Path $root 'src\FocusKey.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\FocusKey.exe'
$log = Join-Path $data 'logs\focus_key.log'
$previous = $env:FOCUSKEY_DATA_ROOT
$process = $null
function Probe([string[]] $Arguments) { & $probe @Arguments; if ($LASTEXITCODE -ne 0) { throw "Probe failed: $Arguments" } }
function Wait-Log([string] $Text, [int] $Offset = 0) {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        if (Test-Path -LiteralPath $log) {
            $snapshot = [string](Get-Content -LiteralPath $log -Raw)
            if ($snapshot.Length -ge $Offset -and $snapshot.Substring($Offset).Contains($Text)) { return }
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Missing native log: $Text"
}
function Exit-App {
    Probe @('exit', [string]$process.Id)
    if (-not $process.WaitForExit(20000) -or $process.ExitCode -ne 0) { throw 'Native exit failed.' }
}
try {
    Probe @('seed-reports', $data)
    Probe @('set-durations', $data, '8', '5')
    $env:FOCUSKEY_DATA_ROOT = $data
    $process = Start-Process $exe -WorkingDirectory $root -WindowStyle Hidden -PassThru
    Wait-Log 'Shell ready:'
    Probe @('hotkey', [string]$process.Id)
    Wait-Log 'Quick overlay state: selection; ready=True'
    $first = Probe @('overlay-probe', [string]$process.Id)
    foreach ($type in @('Work','Break')) {
        $offset = (Get-Content -LiteralPath $log -Raw).Length
        Probe @('start-configured', [string]$process.Id, $data, $type.ToLowerInvariant())
        Wait-Log "Quick overlay state: $type timer" $offset
        Probe @('overlay-hide', [string]$process.Id)
        Probe @('hotkey', [string]$process.Id)
        Probe @('hotkey', [string]$process.Id)
        Wait-Log "Quick overlay state: $type timer" $offset
        $active = Probe @('overlay-probe', [string]$process.Id)
        if ($active -ne $first) { throw 'Overlay did not reuse the same native surface.' }
        foreach ($appearance in @('Light','Dark','System')) {
            Probe @('set-appearance', $data, $appearance)
            Probe @('set-colors', $data, '#FFFFFF', '#FFFF00')
            Probe @('hotkey', [string]$process.Id)
        }
        Wait-Log 'Quick overlay state: selection; ready=True' $offset
        Probe @('overlay-probe', [string]$process.Id)
    }
    Probe @('mini', [string]$process.Id)
    Wait-Log 'Mini Timer shown: No active session'
    Exit-App
    $offset = (Get-Content -LiteralPath $log -Raw).Length
    $process.Dispose()
    $process = Start-Process $exe -WorkingDirectory $root -WindowStyle Hidden -PassThru
    Wait-Log 'Shell ready:' $offset
    Probe @('hotkey', [string]$process.Id)
    Wait-Log 'Quick overlay state: selection; ready=True' $offset
    Exit-App
    if ((Get-Content -LiteralPath $log -Raw) -match '\[ERROR\]') { throw 'Native error logged.' }
    Probe @('hotkey-free')
    Write-Output "PASS: unified overlay native reuse, active Work/Break reopen, completion to selection, themes/colors, custom durations, restart and cleanup. Data: $data"
} finally {
    if ($process) { if (-not $process.HasExited) { Exit-App }; $process.Dispose() }
    $env:FOCUSKEY_DATA_ROOT = $previous
}