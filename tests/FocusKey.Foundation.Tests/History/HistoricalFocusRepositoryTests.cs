using FocusKey.Foundation.Data;
using FocusKey.Foundation.History;
using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Tests.History;

public sealed class HistoricalFocusRepositoryTests
{
    [Fact]
    public async Task ImportAsync_InsertsNewRecords_CalculatesCorrectDurations()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteHistoricalFocusRepository(connections);

        var entries = new List<HistoricalFocusEntry>
        {
            new(new DateOnly(2026, 9, 4), "", 5.98),
            new(new DateOnly(2026, 9, 5), "Writing", 4.5),
        };

        var result = await repo.ImportAsync(entries, 2, 0);

        Assert.True(result.Success);
        Assert.Equal(2, result.RowsFound);
        Assert.Equal(2, result.NewRecords);
        Assert.Equal(0, result.UpdatedRecords);
        Assert.Equal(0, result.DuplicateRecords);
        Assert.Equal(0, result.InvalidRows);
        Assert.Equal(10.48, result.TotalImportedHours);

        var all = await repo.GetAllAsync();
        Assert.Equal(2, all.Count);

        Assert.Equal(new DateOnly(2026, 9, 4), all[0].Date);
        Assert.Equal("", all[0].Project);
        Assert.Equal(5.98, all[0].SourceHours);
        Assert.Equal(TimeSpan.FromSeconds((long)Math.Round(5.98 * 3600)), all[0].Duration);

        Assert.Equal(new DateOnly(2026, 9, 5), all[1].Date);
        Assert.Equal("Writing", all[1].Project);
        Assert.Equal(4.5, all[1].SourceHours);
        Assert.Equal(TimeSpan.FromSeconds((long)Math.Round(4.5 * 3600)), all[1].Duration);
    }

    [Fact]
    public async Task ImportAsync_ExactMinutesFormat_NormalizesToExactSeconds()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteHistoricalFocusRepository(connections);

        string csv =
            "date\tproject\tminutes\r\n" +
            "20260905\t\"\"\t271\r\n" +
            "20260906\t\"\"\t690\r\n" +
            "20260907\t\"\"\t507\r\n" +
            "20260908\t\"\"\t300\r\n" +
            "20260909\t\"\"\t503\r\n" +
            "20260910\t\"\"\t664\r\n" +
            "20260911\t\"\"\t217\r\n";

        var parseResult = WebsiteHistoryCsv.Parse(csv);
        Assert.True(parseResult.Success);

        var result = await repo.ImportAsync(parseResult.ValidEntries, parseResult.TotalRowsFound, parseResult.InvalidRows);

        Assert.True(result.Success);
        Assert.Equal(7, result.RowsFound);
        Assert.Equal(7, result.NewRecords);
        Assert.Equal(0, result.DuplicateRecords);

        var all = await repo.GetAllAsync();
        Assert.Equal(7, all.Count);

        // 2026-09-06: 690 minutes = 41400 seconds
        var sep6 = all.First(r => r.Date == new DateOnly(2026, 9, 6));
        Assert.Equal(41400, (long)sep6.Duration.TotalSeconds);
        Assert.Equal(TimeSpan.FromSeconds(41400), sep6.Duration);

        // 2026-09-08: 300 minutes = 18000 seconds = 5.0 hours
        var sep8 = all.First(r => r.Date == new DateOnly(2026, 9, 8));
        Assert.Equal(18000, (long)sep8.Duration.TotalSeconds);
    }

    [Fact]
    public async Task ImportAsync_Idempotent_SubsequentIdenticalImportDoesNotDuplicate()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteHistoricalFocusRepository(connections);

        var entries = new List<HistoricalFocusEntry>
        {
            new(new DateOnly(2026, 9, 1), "", 6.0),
            new(new DateOnly(2026, 9, 2), "", 7.5),
        };

        // First import
        var first = await repo.ImportAsync(entries, 2, 0);
        Assert.True(first.Success);
        Assert.Equal(2, first.NewRecords);
        Assert.Equal(0, first.DuplicateRecords);

        // Repeat identical import
        var second = await repo.ImportAsync(entries, 2, 0);
        Assert.True(second.Success);
        Assert.Equal(0, second.NewRecords);
        Assert.Equal(0, second.UpdatedRecords);
        Assert.Equal(2, second.DuplicateRecords);

        var all = await repo.GetAllAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal(6.0, all[0].SourceHours);
        Assert.Equal(7.5, all[1].SourceHours);
    }

    [Fact]
    public async Task ImportAsync_EquivalentHoursThenMinutes_DoesNotDuplicate()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteHistoricalFocusRepository(connections);

        // 1. Import Hours: 11.5 hours on 2026-09-06 = 41400 seconds
        var hoursEntries = new List<HistoricalFocusEntry>
        {
            HistoricalFocusEntry.FromHours(new DateOnly(2026, 9, 6), "", 11.5)
        };
        var first = await repo.ImportAsync(hoursEntries, 1, 0);
        Assert.Equal(1, first.NewRecords);

        // 2. Import Minutes: 690 minutes on 2026-09-06 = 41400 seconds
        var minutesEntries = new List<HistoricalFocusEntry>
        {
            HistoricalFocusEntry.FromMinutes(new DateOnly(2026, 9, 6), "", 690)
        };
        var second = await repo.ImportAsync(minutesEntries, 1, 0);

        Assert.True(second.Success);
        Assert.Equal(0, second.NewRecords);
        Assert.Equal(0, second.UpdatedRecords);
        Assert.Equal(1, second.DuplicateRecords);

        var all = await repo.GetAllAsync();
        Assert.Single(all);
        Assert.Equal(TimeSpan.FromSeconds(41400), all[0].Duration);
    }

    [Fact]
    public async Task ImportAsync_EquivalentMinutesThenHours_DoesNotDuplicate()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteHistoricalFocusRepository(connections);

        // 1. Import Minutes: 300 minutes on 2026-09-08 = 18000 seconds
        var minutesEntries = new List<HistoricalFocusEntry>
        {
            HistoricalFocusEntry.FromMinutes(new DateOnly(2026, 9, 8), "", 300)
        };
        var first = await repo.ImportAsync(minutesEntries, 1, 0);
        Assert.Equal(1, first.NewRecords);

        // 2. Import Hours: 5.0 hours on 2026-09-08 = 18000 seconds
        var hoursEntries = new List<HistoricalFocusEntry>
        {
            HistoricalFocusEntry.FromHours(new DateOnly(2026, 9, 8), "", 5.0)
        };
        var second = await repo.ImportAsync(hoursEntries, 1, 0);

        Assert.True(second.Success);
        Assert.Equal(0, second.NewRecords);
        Assert.Equal(0, second.UpdatedRecords);
        Assert.Equal(1, second.DuplicateRecords);

        var all = await repo.GetAllAsync();
        Assert.Single(all);
        Assert.Equal(TimeSpan.FromSeconds(18000), all[0].Duration);
    }

    [Fact]
    public async Task ImportAsync_UpdatedHours_UpdatesExistingRecord()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteHistoricalFocusRepository(connections);

        var initialEntries = new List<HistoricalFocusEntry>
        {
            new(new DateOnly(2026, 9, 1), "Dev", 5.0),
        };
        await repo.ImportAsync(initialEntries, 1, 0);

        // Re-import with corrected hours
        var updatedEntries = new List<HistoricalFocusEntry>
        {
            new(new DateOnly(2026, 9, 1), "Dev", 8.25),
        };
        var updateResult = await repo.ImportAsync(updatedEntries, 1, 0);

        Assert.True(updateResult.Success);
        Assert.Equal(0, updateResult.NewRecords);
        Assert.Equal(1, updateResult.UpdatedRecords);
        Assert.Equal(0, updateResult.DuplicateRecords);

        var record = await repo.GetAsync(new DateOnly(2026, 9, 1), "Dev");
        Assert.NotNull(record);
        Assert.Equal(8.25, record.SourceHours);
        Assert.Equal(TimeSpan.FromSeconds((long)Math.Round(8.25 * 3600)), record.Duration);
    }

    [Fact]
    public async Task ImportAsync_OverlappingImportFiles_DoNotInflateDuration()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteHistoricalFocusRepository(connections);

        // File 1: Sep 4 to Sep 10 (Hours)
        string file1 =
            "date\tproject\thours\r\n" +
            "20260904\t\"\"\t5.98\r\n" +
            "20260905\t\"\"\t4.51\r\n" +
            "20260906\t\"\"\t11.49\r\n" +
            "20260907\t\"\"\t8.45\r\n" +
            "20260908\t\"\"\t5\r\n" +
            "20260909\t\"\"\t8.37\r\n" +
            "20260910\t\"\"\t11.06\r\n";

        // File 2: Sep 5 to Sep 11 (Minutes)
        string file2 =
            "date\tproject\tminutes\r\n" +
            "20260905\t\"\"\t271\r\n" +
            "20260906\t\"\"\t690\r\n" +
            "20260907\t\"\"\t507\r\n" +
            "20260908\t\"\"\t300\r\n" +
            "20260909\t\"\"\t503\r\n" +
            "20260910\t\"\"\t664\r\n" +
            "20260911\t\"\"\t217\r\n";

        var parsed1 = WebsiteHistoryCsv.Parse(file1);
        await repo.ImportAsync(parsed1.ValidEntries, parsed1.TotalRowsFound, parsed1.InvalidRows);

        var parsed2 = WebsiteHistoryCsv.Parse(file2);
        await repo.ImportAsync(parsed2.ValidEntries, parsed2.TotalRowsFound, parsed2.InvalidRows);

        var all = await repo.GetAllAsync();

        // Exactly 8 calendar days in union (Sep 4 through Sep 11)
        Assert.Equal(8, all.Count);
        Assert.Equal(new DateOnly(2026, 9, 4), all[0].Date);
        Assert.Equal(new DateOnly(2026, 9, 11), all[7].Date);

        // Sep 8 was 5 hours in file 1, and 300 minutes in file 2 -> exactly identical (18000s), not doubled
        var sep8 = all.First(r => r.Date == new DateOnly(2026, 9, 8));
        Assert.Equal(TimeSpan.FromSeconds(18000), sep8.Duration);
    }

    [Fact]
    public async Task GetBetweenAsync_FiltersDateRangeCorrectly()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteHistoricalFocusRepository(connections);

        var entries = new List<HistoricalFocusEntry>
        {
            new(new DateOnly(2026, 8, 31), "", 2.0),
            new(new DateOnly(2026, 9, 1), "", 4.0),
            new(new DateOnly(2026, 9, 2), "", 6.0),
            new(new DateOnly(2026, 9, 3), "", 8.0),
        };
        await repo.ImportAsync(entries, 4, 0);

        // Query range [2026-09-01, 2026-09-03) — half-open interval
        var inRange = await repo.GetBetweenAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3));

        Assert.Equal(2, inRange.Count);
        Assert.Equal(new DateOnly(2026, 9, 1), inRange[0].Date);
        Assert.Equal(new DateOnly(2026, 9, 2), inRange[1].Date);
    }

    [Fact]
    public async Task ImportWebsiteCsvAsync_ConflictingDuplicateRows_CommitsNothing()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteHistoricalFocusRepository(connections);
        var sessionRepo = new SqliteSessionRepository(connections);
        var service = new HistoricalFocusService(repo, sessionRepo);

        // Pre-populate with 1 legitimate record
        var existing = new List<HistoricalFocusEntry>
        {
            HistoricalFocusEntry.FromMinutes(new DateOnly(2026, 9, 1), "", 120)
        };
        await repo.ImportAsync(existing, 1, 0);

        // Ambiguous CSV with conflicting durations for 20260906 (690 vs 700)
        string conflictingCsv =
            "date\tproject\tminutes\r\n" +
            "20260905\t\"\"\t271\r\n" +
            "20260906\t\"\"\t690\r\n" +
            "20260906\t\"\"\t700\r\n" +
            "20260907\t\"\"\t507\r\n";

        var result = await service.ImportWebsiteCsvAsync(conflictingCsv);

        // 1. Must report failure
        Assert.False(result.Success);
        Assert.Contains("Conflicting duplicate rows", result.ErrorMessage);
        Assert.Contains("20260906", result.ErrorMessage);

        // 2. Transactional safety: database must remain untouched (only original record survives)
        var all = await repo.GetAllAsync();
        Assert.Single(all);
        Assert.Equal(new DateOnly(2026, 9, 1), all[0].Date);
        Assert.Equal(TimeSpan.FromMinutes(120), all[0].Duration);
    }

    [Fact]
    public async Task ImportAsync_ConflictingEntriesInBatch_RollsBackAndCommitsNothing()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteHistoricalFocusRepository(connections);

        // Batch with conflicting durations for same date/project
        var entries = new List<HistoricalFocusEntry>
        {
            HistoricalFocusEntry.FromMinutes(new DateOnly(2026, 9, 6), "", 690),
            HistoricalFocusEntry.FromMinutes(new DateOnly(2026, 9, 6), "", 700),
        };

        var result = await repo.ImportAsync(entries, 2, 0);

        Assert.False(result.Success);
        Assert.Contains("Conflicting duplicate entries in import batch", result.ErrorMessage);

        var all = await repo.GetAllAsync();
        Assert.Empty(all);
    }

    [Fact]
    public async Task ImportWebsiteCsvAsync_IdenticalDuplicateInSingleFile_ReportsDuplicateAndDoesNotMultiplyDuration()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteHistoricalFocusRepository(connections);
        var sessionRepo = new SqliteSessionRepository(connections);
        var service = new HistoricalFocusService(repo, sessionRepo);

        string csv =
            "date\tproject\tminutes\r\n" +
            "20260906\t\"\"\t690\r\n" +
            "20260906\t\"\"\t690\r\n";

        var result = await service.ImportWebsiteCsvAsync(csv);

        Assert.True(result.Success);
        Assert.Equal(2, result.RowsFound);
        Assert.Equal(1, result.NewRecords);
        Assert.Equal(1, result.DuplicateRecords);
        Assert.Equal(0, result.UpdatedRecords);
        Assert.Equal(0, result.InvalidRows);

        var all = await repo.GetAllAsync();
        Assert.Single(all);
        Assert.Equal(new DateOnly(2026, 9, 6), all[0].Date);
        // Duration must remain 690 minutes (41400s), NOT 1380 minutes
        Assert.Equal(TimeSpan.FromMinutes(690), all[0].Duration);
        Assert.Equal(41400, all[0].Duration.TotalSeconds);
    }
}
