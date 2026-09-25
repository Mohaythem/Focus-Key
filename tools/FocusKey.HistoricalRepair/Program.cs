using System.Globalization;
using FocusKey.Foundation;
using FocusKey.Foundation.Data;
using FocusKey.HistoricalRepair;
using Microsoft.Data.Sqlite;

// One-time maintenance tool. Default mode is a read-only DRY RUN. Mutation requires an explicit --apply
// together with an explicit --database or --data-root target; the tool never guesses a database.

bool apply = false;
string? database = null;
string? dataRoot = null;
string? todayArg = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--apply": apply = true; break;
        case "--database" or "-d": database = Next(args, ref i); break;
        case "--data-root": dataRoot = Next(args, ref i); break;
        case "--today": todayArg = Next(args, ref i); break;
        case "--help" or "-h": Usage(); return 0;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            Usage();
            return 2;
    }
}

if (database is not null && dataRoot is not null)
{
    Console.Error.WriteLine("Specify only one of --database or --data-root.");
    return 2;
}

string? path =
    database is not null ? Path.GetFullPath(database) :
    dataRoot is not null ? AppPaths.ForRoot(dataRoot).DatabaseFile :
    null;

if (path is null)
{
    Console.Error.WriteLine("A target is required: pass --database <file> or --data-root <folder>.");
    Usage();
    return 2;
}

DateOnly today;
if (todayArg is null)
{
    today = DateOnly.FromDateTime(DateTime.Now);
}
else if (!DateOnly.TryParseExact(todayArg, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out today))
{
    Console.Error.WriteLine($"--today must be yyyy-MM-dd, got '{todayArg}'.");
    return 2;
}

if (!File.Exists(path))
{
    Console.Error.WriteLine($"Database not found (the tool never creates one): {path}");
    return 2;
}

if (!HasHistoricalFocusTable(path))
{
    Console.WriteLine($"Database: {path}");
    Console.WriteLine("No historical_focus table present — nothing to repair.");
    return 0;
}

var connections = new SqliteConnectionFactory(path);
RepairReport report = await HistoricalRepairService.RepairAsync(connections, today, apply);

Console.WriteLine($"Database:            {report.DatabasePath}");
Console.WriteLine($"Today:               {today:yyyy-MM-dd}");
Console.WriteLine($"Future-dated rows:   {report.FutureRowsFound}");
Console.WriteLine($"Future focus time:   {report.FutureSeconds / 3600.0:F2} h ({report.FutureSeconds} s)");
Console.WriteLine($"Affected projects:   {(report.AffectedProjects.Count == 0 ? "(none)" : string.Join(", ", report.AffectedProjects.Select(p => p.Length == 0 ? "(default)" : p)))}");
Console.WriteLine($"Affected months:     {(report.AffectedMonths.Count == 0 ? "(none)" : string.Join(", ", report.AffectedMonths.Select(m => $"{m.Year:0000}-{m.Month:00}")))}");
Console.WriteLine($"Proposed target days:{report.ProposedTargetDays}");

if (report.SkippedMonths.Count > 0)
{
    Console.WriteLine("Skipped (NeedsManualReview — no in-month day before today; left untouched):");
    foreach (var m in report.SkippedMonths)
        Console.WriteLine($"  {m.Year:0000}-{m.Month:00}: {m.RowCount} row(s), {m.TotalSeconds / 3600.0:F2} h, projects [{string.Join(", ", m.Projects.Select(p => p.Length == 0 ? "(default)" : p))}]");
}

if (report.FutureRowsFound == 0)
{
    Console.WriteLine("\nNothing to do: no future-dated historical rows.");
    return 0;
}

if (!apply)
{
    Console.WriteLine("\nDRY RUN — no changes were written. Re-run with --apply to apply the correction above.");
    return 0;
}

RedistributionOutcome? outcome = report.Outcome;
Console.WriteLine("\nAPPLIED:");
Console.WriteLine($"  rows deleted:  {outcome?.RowsDeleted ?? 0}");
Console.WriteLine($"  rows upserted: {outcome?.RowsUpserted ?? 0}");
Console.WriteLine($"  months skipped:{outcome?.MonthsSkipped ?? report.SkippedMonths.Count}");
return 0;

static string? Next(string[] args, ref int i)
{
    if (i + 1 >= args.Length) return null;
    return args[++i];
}

static bool HasHistoricalFocusTable(string path)
{
    var csb = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly };
    using var connection = new SqliteConnection(csb.ConnectionString);
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='historical_focus';";
    return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
}

static void Usage()
{
    Console.WriteLine(
        """
        FocusKey.HistoricalRepair — one-time correction for future-dated historical_focus rows.

        Usage:
          FocusKey.HistoricalRepair (--database <file> | --data-root <folder>) [--today yyyy-MM-dd] [--apply]

        Default is a read-only DRY RUN (zero database writes). Pass --apply to write the correction.
        A target (--database or --data-root) is always required; the tool never guesses a database.
        """);
}
