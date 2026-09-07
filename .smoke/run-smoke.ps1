# Focus Key - Phase 4 native shell smoke test.
param([string] $DataRootName = "p4-$([guid]::NewGuid().ToString('N'))")
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$smokeRoot = Join-Path $projectRoot '.smoke'
$exe = Join-Path $projectRoot 'src\FocusKey.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\FocusKey.exe'
$probeProject = Join-Path $smokeRoot 'ShellProbe\ShellProbe.csproj'
$probe = Join-Path $smokeRoot 'ShellProbe\bin\Debug\net10.0-windows10.0.19041.0\ShellProbe.exe'
$previousDataRoot = [Environment]::GetEnvironmentVariable('FOCUSKEY_DATA_ROOT', 'Process'); $process = $null
function Wait-Until([scriptblock]$Condition, [int]$TimeoutSeconds, [string]$Message) { $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds); do { if (& $Condition) { return }; Start-Sleep -Milliseconds 200 } while ([DateTime]::UtcNow -lt $deadline); throw $Message }
function Read-Log { if (Test-Path -LiteralPath $logFile) { [string](Get-Content -LiteralPath $logFile -Raw) } else { [string]'' } }
function Invoke-Probe([string[]]$Arguments) { & $probe @Arguments; if ($LASTEXITCODE -ne 0) { throw "ShellProbe $($Arguments -join ' ') failed with exit code $LASTEXITCODE." } }
function Assert-Database { if (-not (Test-Path -LiteralPath $databaseFile -PathType Leaf)) { throw "Database was not created: $databaseFile" }; $bytes = New-Object byte[] 64; $stream = [IO.File]::Open($databaseFile, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite); try { if ($stream.Read($bytes, 0, 64) -ne 64) { throw 'Database is truncated.' } } finally { $stream.Dispose() }; if ([Text.Encoding]::ASCII.GetString($bytes, 0, 15) -ne 'SQLite format 3') { throw 'Database is not SQLite.' }; $version = ([int64]$bytes[60] * 16777216) + ([int64]$bytes[61] * 65536) + ([int64]$bytes[62] * 256) + $bytes[63]; if ($version -ne 3) { throw "Database schema version is $version, expected 3." } }
try {
    if ([string]::IsNullOrWhiteSpace($DataRootName) -or [IO.Path]::IsPathRooted($DataRootName) -or $DataRootName -match '(^|[\\/])\.\.([\\/]|$)') { throw 'DataRootName must be a relative child of .smoke.' }
    $dataRoot = [IO.Path]::GetFullPath((Join-Path $smokeRoot $DataRootName)); $prefix = $smokeRoot.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if (-not $dataRoot.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $dataRoot)) { throw "Smoke data root must be fresh and contained: $dataRoot" }
    if (Get-Process -Name FocusKey -ErrorAction SilentlyContinue) { throw 'An existing FocusKey process is running; exit it before this smoke test.' }
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Executable not found: $exe" }
    dotnet build $probeProject --no-restore; if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $probe -PathType Leaf)) { throw 'ShellProbe build failed or executable is missing.' }
    & $probe hotkey-free; if ($LASTEXITCODE -ne 0) { throw 'Shift+F3 is already held by another application.' }
    $logFile = Join-Path $dataRoot 'logs\focus_key.log'; $databaseFile = Join-Path $dataRoot 'focus_key.db'; $env:FOCUSKEY_DATA_ROOT = $dataRoot
    $process = Start-Process -FilePath $exe -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
    Wait-Until { $process.Refresh(); -not $process.HasExited -and $process.MainWindowHandle -ne 0 } 30 'Focus Key did not create a ready window.'
    Wait-Until { (Read-Log) -match 'Foundation initialization complete\.' } 30 'Foundation did not initialize.'
    Wait-Until { (Read-Log) -match 'Shell ready: tray added; Shift \+ F3 registered\.' } 30 'Shell did not become ready.'
    if ((Read-Log) -notmatch 'Database ready at schema version 3\.' -or (Read-Log) -notmatch 'Session startup recovery: NoActiveSession\.') { throw 'First launch did not confirm schema and startup recovery.' }
    Invoke-Probe -Arguments @('probe', [string]$process.Id)
    # The on-disk header can lag committed schema changes while WAL is open.
    # Check it after graceful exit/checkpoint; live schema is verified by bootstrap logs.
    if ((Read-Log) -notmatch 'Applied database migration 1 \(schema_metadata\)\.' -or (Read-Log) -notmatch 'Applied database migration 2 \(sessions\)\.' -or (Read-Log) -notmatch 'Applied database migration 3 \(application_settings\)\.') { throw 'Fresh database did not apply all expected migrations.' }
    $beforeHide = Read-Log; if (-not $process.CloseMainWindow()) { throw 'Could not request window close.' }
    Wait-Until { (Read-Log).Length -gt $beforeHide.Length -and (Read-Log) -match 'Main window hidden; shell remains running\.' } 10 'Window close was not converted to hide.'
    $process.Refresh(); if ($process.HasExited) { throw 'Process exited when main window was closed.' }; Invoke-Probe -Arguments @('open', [string]$process.Id)
    Wait-Until { (Read-Log) -match 'Shell activation: ShowWindow\.' } 10 'ShowWindow activation was not logged.'
    $competingRoot = Join-Path $smokeRoot ("$DataRootName-competing"); if (Test-Path -LiteralPath $competingRoot) { throw "Competing root already exists: $competingRoot" }
    $psi = [Diagnostics.ProcessStartInfo]::new($exe); $psi.WorkingDirectory = $projectRoot; $psi.WindowStyle = 'Hidden'; $psi.UseShellExecute = $false; $psi.Environment['FOCUSKEY_DATA_ROOT'] = $competingRoot
    $competing = [Diagnostics.Process]::Start($psi); if (-not $competing.WaitForExit(30000)) { throw 'Competing process did not exit.' }; if ($competing.ExitCode -ne 0) { throw "Competing process exited with $($competing.ExitCode)." }; $competing.Dispose()
    if (Test-Path -LiteralPath $competingRoot) { throw 'Competing launch created a data root.' }; $process.Refresh(); if ($process.HasExited) { throw 'Owner exited after competing launch.' }
    Invoke-Probe -Arguments @('exit', [string]$process.Id); if (-not $process.WaitForExit(30000)) { throw 'Explicit shell exit did not complete.' }; if ($process.ExitCode -ne 0) { throw "Owner exited with $($process.ExitCode)." }
    $finalLog = Read-Log; if ($finalLog -notmatch 'Session shutdown: NoActiveSession\.' -or $finalLog -notmatch 'Shell stopped: tray removed; hotkey unregistered\.' -or $finalLog -notmatch 'Main window closed\. Focus Key shutting down\.') { throw 'Clean shell shutdown was not confirmed in the log.' }
    & $probe hotkey-free; if ($LASTEXITCODE -ne 0) { throw 'Shift+F3 remained registered after shutdown.' }; Assert-Database
    $secondStart = Read-Log
    $process.Dispose()
    $process = Start-Process -FilePath $exe -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
    Wait-Until { $process.Refresh(); -not $process.HasExited -and $process.MainWindowHandle -ne 0 } 30 'Second launch did not create a ready window.'
    Wait-Until { (Read-Log).Substring($secondStart.Length) -match 'Shell ready: tray added; Shift \+ F3 registered\.' } 30 'Second launch did not become ready.'
    Invoke-Probe -Arguments @('probe', [string]$process.Id); Invoke-Probe -Arguments @('exit', [string]$process.Id)
    if (-not $process.WaitForExit(30000)) { throw 'Second launch did not exit.' }; if ($process.ExitCode -ne 0) { throw "Second launch exited with $($process.ExitCode)." }
    $secondLog = (Read-Log).Substring($secondStart.Length)
    if ($secondLog -match 'Applied database migration') { throw 'Second launch applied a migration; initialization was not idempotent.' }
    if ($secondLog -notmatch 'Session shutdown: NoActiveSession\.' -or $secondLog -notmatch 'Shell stopped: tray removed; hotkey unregistered\.') { throw 'Second launch did not confirm clean shell shutdown.' }
    Invoke-Probe -Arguments @('hotkey-free'); Assert-Database
    Write-Output "PASS: native shell API smoke, two clean launches. Isolated data root: $dataRoot"
}
finally { if ($null -ne $process) { $process.Refresh(); if (-not $process.HasExited) { try { Invoke-Probe -Arguments @('exit', [string]$process.Id); $process.WaitForExit(10000) | Out-Null } catch { } }; $process.Dispose() }; if ($null -eq $previousDataRoot) { Remove-Item Env:FOCUSKEY_DATA_ROOT -ErrorAction SilentlyContinue } else { $env:FOCUSKEY_DATA_ROOT = $previousDataRoot } }
