# Focus Key - native runtime smoke test
# Launches the unpackaged WinUI 3 build against an isolated data root, verifies
# initialization and schema, then closes the window and checks clean shutdown.

param(
    [string] $DataRootName = "p3-$([guid]::NewGuid().ToString('N'))"
)

$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$smokeRoot = Join-Path $projectRoot '.smoke'
$exe = Join-Path $projectRoot 'src\FocusKey.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\FocusKey.exe'

if ([string]::IsNullOrWhiteSpace($DataRootName) -or [IO.Path]::IsPathRooted($DataRootName) -or
    $DataRootName -match '(^|[\\/])\.\.([\\/]|$)') {
    throw "DataRootName must be a relative path contained by '$smokeRoot'."
}

$dataRoot = [IO.Path]::GetFullPath((Join-Path $smokeRoot $DataRootName))
$smokePrefix = $smokeRoot.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
if (-not $dataRoot.StartsWith($smokePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Resolved data root '$dataRoot' is outside '$smokeRoot'."
}

$previousDataRoot = [Environment]::GetEnvironmentVariable('FOCUSKEY_DATA_ROOT', 'Process')
$logFile = Join-Path $dataRoot 'logs\focus_key.log'
$databaseFile = Join-Path $dataRoot 'focus_key.db'
$expectedSchema = 2

function Show-Header($text) {
    Write-Output ''
    Write-Output "===== $text ====="
}

Show-Header 'PRE-CONDITIONS'
Write-Output "executable            : $exe"
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Executable not found: $exe" }
if (Test-Path -LiteralPath $dataRoot) { throw "Use a fresh smoke data root; '$dataRoot' already exists." }
Write-Output "smoke data root       : $dataRoot"
Write-Output "project root          : $projectRoot"

function Wait-Until([scriptblock] $Condition, [int] $TimeoutSeconds, [string] $FailureMessage) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    throw $FailureMessage
}

function Read-Log { if (Test-Path -LiteralPath $logFile) { [string](Get-Content -LiteralPath $logFile -Raw) } else { [string]'' } }

function Get-NewLog([string] $before) {
    $after = Read-Log
    if ($after.Length -lt $before.Length) { throw 'The runtime log was truncated during the smoke run.' }
    return $after.Substring($before.Length)
}

function Assert-Database {
    if (-not (Test-Path -LiteralPath $databaseFile -PathType Leaf)) { throw "Database was not created: $databaseFile" }
    $bytes = New-Object byte[] 64
    $stream = [IO.File]::OpenRead($databaseFile)
    try { if ($stream.Read($bytes, 0, 64) -ne 64) { throw "Database is truncated: $databaseFile" } }
    finally { $stream.Dispose() }
    $header = [Text.Encoding]::ASCII.GetString($bytes, 0, 15)
    if ($header -ne 'SQLite format 3') { throw "Database is not a SQLite file: $databaseFile" }
    $schemaVersion = ([int64]$bytes[60] * 16777216) + ([int64]$bytes[61] * 65536) + ([int64]$bytes[62] * 256) + $bytes[63]
    if ($schemaVersion -ne $expectedSchema) { throw "Database header reports schema version $schemaVersion instead of $expectedSchema." }
}

function Invoke-Run($label) {
    Show-Header "RUN ${label}: LAUNCH"
    $logBefore = Read-Log
    $process = Start-Process -FilePath $exe -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
    try {
        Wait-Until { $process.Refresh(); if ($process.HasExited) { throw "Run $label exited early with code $($process.ExitCode)." }; $process.MainWindowHandle -ne 0 } 30 "Run $label did not create a ready window within 30 seconds."
        Wait-Until { $process.Refresh(); if ($process.HasExited) { throw "Run $label exited during initialization with code $($process.ExitCode)." }; (Get-NewLog $logBefore) -match 'Foundation initialization complete\.' } 30 "Run $label did not complete foundation initialization within 30 seconds."
        $process.Refresh()
        if (-not $process.Responding -or $process.MainWindowTitle -ne 'Focus Key') { throw "Run $label window was not responsive or had unexpected title '$($process.MainWindowTitle)'." }

        Write-Output "process id            : $($process.Id)"
        Write-Output "responding            : $($process.Responding)"
        Write-Output "main window title     : '$($process.MainWindowTitle)'"

        Show-Header "RUN ${label}: SHUTDOWN"
        if (-not $process.CloseMainWindow()) { throw "Run $label could not send a close request." }
        if (-not $process.WaitForExit(20000)) { throw "Run $label did not exit within 20 seconds." }
        if ($process.ExitCode -ne 0) { throw "Run $label exited with code $($process.ExitCode)." }
        $logAfterClose = Get-NewLog $logBefore
        if ($logAfterClose -notmatch "Database ready at schema version $expectedSchema\." -or
            $logAfterClose -notmatch 'Placeholder window displayed\.' -or
            $logAfterClose -notmatch 'Session startup recovery: NoActiveSession\.' -or
            $logAfterClose -notmatch 'Session shutdown: NoActiveSession\.') {
            throw "Run $label did not confirm the current schema and placeholder window."
        }
        if ($logAfterClose -notmatch 'Main window closed\. Focus Key shutting down\.') { throw "Run $label did not report clean shutdown." }
        $script:RunLogAfter = $logAfterClose
        Write-Output "exit code             : $($process.ExitCode)"
    } finally {
        if (-not $process.HasExited) { $process.CloseMainWindow() | Out-Null; $process.WaitForExit(5000) }
        $process.Dispose()
    }
}

try {
    $env:FOCUSKEY_DATA_ROOT = $dataRoot
    Invoke-Run '1'
    Assert-Database
    if ($RunLogAfter -notmatch 'Applied database migration 1 \(schema_metadata\)\.' -or
        $RunLogAfter -notmatch 'Applied database migration 2 \(sessions\)\.') {
        throw 'Run 1 did not apply both expected schema migrations on the fresh data root.'
    }

    Invoke-Run '2'
    Assert-Database
    if ($RunLogAfter -match 'Applied database migration') {
        throw 'Run 2 applied a migration; initialization was not idempotent.'
    }
}
finally {
    if ($null -eq $previousDataRoot) { Remove-Item Env:FOCUSKEY_DATA_ROOT -ErrorAction SilentlyContinue }
    else { $env:FOCUSKEY_DATA_ROOT = $previousDataRoot }
}

Show-Header 'FINAL STATE'
Write-Output "database file exists  : $(Test-Path -LiteralPath $databaseFile)"
Write-Output "isolated data root    : $dataRoot"
