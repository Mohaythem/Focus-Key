using FocusKey.Foundation.Data;
using FocusKey.Foundation.History;

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
}
