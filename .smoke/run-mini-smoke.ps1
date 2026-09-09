param([string] $DataRootName = "p10-$([guid]::NewGuid().ToString('N'))")
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
    $env:FOCUSKEY_DATA_ROOT = $data
    $process = Start-Process $exe -WorkingDirectory $root -WindowStyle Hidden -PassThru
    Wait-Log 'Shell ready:'
    Probe @('mini', [string]$process.Id)
    Wait-Log 'Mini Timer shown: No active session'
    $first = Probe @('mini-probe', [string]$process.Id)
    Probe @('mini-hide', [string]$process.Id)
    $offset = (Get-Content -LiteralPath $log -Raw).Length
    Probe @('mini', [string]$process.Id)
    Wait-Log 'Mini Timer shown:' $offset
    $second = Probe @('mini-probe', [string]$process.Id)
    if ($first -ne $second) { throw 'Mini Timer window was not reused.' }
    foreach ($appearance in @('Light','Dark','System')) {
        Probe @('set-appearance', $data, $appearance)
        Probe @('set-colors', $data, '#FFFFFF', '#000000')
        $offset = (Get-Content -LiteralPath $log -Raw).Length
        Probe @('mini', [string]$process.Id)
        Wait-Log "appearance $appearance; Work #FFFFFF, Break #000000." $offset
    }
    foreach ($type in @('Work','Break')) {
        $offset = (Get-Content -LiteralPath $log -Raw).Length
        Probe @('start-short', [string]$process.Id, $data, $type.ToLowerInvariant(), '3')
        Probe @('reevaluate', [string]$process.Id)
        Wait-Log "Mini Timer refreshed: $type" $offset
        Wait-Log 'Mini Timer refreshed: No active session' $offset
        Probe @('mini-probe', [string]$process.Id)
    }
    if (([regex]::Matches((Get-Content -LiteralPath $log -Raw), 'Mini Timer created\.')).Count -ne 1) { throw 'More than one Mini Timer created.' }
    Probe @('hotkey', [string]$process.Id)
    Wait-Log 'Quick overlay created'
    Exit-App
    $offset = (Get-Content -LiteralPath $log -Raw).Length
    $process.Dispose()
    $process = Start-Process $exe -WorkingDirectory $root -WindowStyle Hidden -PassThru
    Wait-Log 'Shell ready:' $offset
    if ((Get-Content -LiteralPath $log -Raw).Substring($offset).Contains('Mini Timer created')) { throw 'Mini Timer should be off at startup.' }
    Probe @('mini', [string]$process.Id)
    Wait-Log 'Mini Timer shown: No active session; appearance System; Work #FFFFFF, Break #000000.' $offset
    Exit-App
    if ((Get-Content -LiteralPath $log -Raw) -match '\[ERROR\]') { throw 'Native error logged.' }
    Probe @('hotkey-free')
    Write-Output "PASS: Mini Timer native visibility/reuse, Work/Break completion, themes/colors, restart and cleanup. Data: $data"
} finally {
    if ($process) { if (-not $process.HasExited) { Exit-App }; $process.Dispose() }
    $env:FOCUSKEY_DATA_ROOT = $previous
}
