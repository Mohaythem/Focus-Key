param([string] $DataRootName = "p94-$([guid]::NewGuid().ToString('N'))")
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$data = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $DataRootName))
if (-not $data.StartsWith($PSScriptRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $data)) { throw 'Expected a fresh isolated smoke root.' }
if (Get-Process FocusKey -ErrorAction SilentlyContinue) { throw 'Exit the existing FocusKey instance first.' }
$probe = Join-Path $PSScriptRoot 'ShellProbe\bin\Debug\net10.0-windows10.0.19041.0\ShellProbe.exe'
$exe = Join-Path $root 'src\FocusKey.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\FocusKey.exe'
$log = Join-Path $data 'logs\focus_key.log'
$previous = $env:FOCUSKEY_DATA_ROOT
$process = $null
function Probe([string[]] $Arguments) { & $probe @Arguments; if ($LASTEXITCODE -ne 0) { throw 'ShellProbe failed.' } }
function Wait-Log([string] $Text, [int] $Offset = 0) {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        if ((Test-Path -LiteralPath $log) -and (Get-Content -LiteralPath $log -Raw).Substring($Offset).Contains($Text)) { return }
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
    Wait-Log 'Session colors applied: Work #183739, Break #434763; foregrounds #FFFFFF, #FFFFFF.'
    Probe @('hotkey', [string]$process.Id)
    Wait-Log 'Quick overlay created with appearance System; Work #183739, Break #434763.'
    foreach ($appearance in @('light','dark','system')) {
        Probe @('set-appearance', $data, $appearance)
        foreach ($pair in @(@('#FFFFFF','#000000'), @('#183739','#FFFF00'))) {
            Probe @('set-colors', $data, $pair[0], $pair[1])
            $offset = (Get-Content -LiteralPath $log -Raw).Length
            Probe @('open', [string]$process.Id)
            Wait-Log "Session colors applied: Work $($pair[0]), Break $($pair[1]);" $offset
            Probe @('hotkey', [string]$process.Id)
        }
    }
    Exit-App
    $offset = (Get-Content -LiteralPath $log -Raw).Length
    $process.Dispose()
    $process = Start-Process $exe -WorkingDirectory $root -WindowStyle Hidden -PassThru
    Wait-Log 'Shell ready:' $offset
    Wait-Log 'Session colors applied: Work #183739, Break #FFFF00; foregrounds #FFFFFF, #000000.' $offset
    Probe @('hotkey', [string]$process.Id)
    Wait-Log 'Quick overlay created with appearance System; Work #183739, Break #FFFF00.' $offset
    Exit-App
    if ((Get-Content -LiteralPath $log -Raw) -match '\[ERROR\]') { throw 'Native error logged.' }
    Write-Output "PASS: native color application callbacks, runtime refresh, appearance matrix, overlay creation/reuse and restart. Data: $data. Logs do not verify rendered pixels."
} finally {
    if ($process) { if (-not $process.HasExited) { Exit-App }; $process.Dispose() }
    $env:FOCUSKEY_DATA_ROOT = $previous
}
