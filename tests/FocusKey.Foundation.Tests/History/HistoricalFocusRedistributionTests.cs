using FocusKey.Foundation.Data;
using FocusKey.Foundation.History;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Tests.Sessions;
using FocusKey.HistoricalRepair;
using Microsoft.Data.Sqlite;

namespace FocusKey.Foundation.Tests.History;

public sealed class HistoricalFocusRedistributionTests
{
    private static readonly DateTimeOffset ImportedAt = new(2026, 9, 25, 17, 0, 0, TimeSpan.Zero);

    private static HistoricalFocusRecord Row(long id, DateOnly date, long seconds, string project = "") =>
        new(id, date, project, TimeSpan.FromSeconds(seconds), seconds / 3600.0, ImportedAt);

    // Rebuilds the buggy import shape: a month total spread across every calendar day with
    // base = total/days and the first (total % days) days carrying one extra second.
    private static List<HistoricalFocusRecord> DistributeMonth(int year, int month, string project, long totalSeconds, ref long nextId)
    {
        int days = DateTime.DaysInMonth(year, month);
        long baseShare = totalSeconds / days;
        long remainder = totalSeconds % days;
        var rows = new List<HistoricalFocusRecord>();
        for (int day = 1; day <= days; day++)
        {
            long seconds = baseShare + (day - 1 < remainder ? 1 : 0);
            rows.Add(Row(nextId++, new DateOnly(year, month, day), seconds, project));
        }
        return rows;
    }

    private static List<HistoricalFocusRecord> ApplyInMemory(IReadOnlyList<HistoricalFocusRecord> rows, RedistributionPlan plan)
    {
        var deleted = new HashSet<long>(plan.DeleteRowIds);
        var upsertKeys = plan.Upserts.Select(u => (u.Date, u.Project)).ToHashSet();
        long nextId = rows.Count == 0 ? 1 : rows.Max(r => r.Id) + 1;
        var result = rows.Where(r => !deleted.Contains(r.Id) && !upsertKeys.Contains((r.Date, r.Project))).ToList();
        result.AddRange(plan.Upserts.Select(u => Row(nextId++, u.Date, u.DurationSeconds, u.Project)));
        return result;
    }

    private static async Task ImportAsync(SqliteHistoricalFocusRepository repo, IReadOnlyList<HistoricalFocusRecord> rows) =>
        await repo.ImportAsync(rows.Select(r => new HistoricalFocusEntry(r.Date, r.Project, r.Duration, r.SourceHours)).ToList(), rows.Count, 0);

    // ---- Pure policy tests ------------------------------------------------

    [Fact]
    public void RecordsWithNoFutureDatesProduceAnEmptyPlan()
    {
        var rows = new List<HistoricalFocusRecord> { Row(1, new DateOnly(2026, 9, 1), 3600), Row(2, new DateOnly(2026, 9, 25), 3600) };
        Assert.True(HistoricalFocusRedistribution.Plan(rows, new DateOnly(2026, 9, 25)).IsEmpty);
    }

    [Fact]
    public void MidMonthFutureFoldsIntoSameMonthDaysBeforeTodayPreservingTotal()
    {
        long id = 1;
        var rows = DistributeMonth(2026, 9, "", 300_000, ref id);
        long total = rows.Sum(r => (long)r.Duration.TotalSeconds);
        var today = new DateOnly(2026, 9, 25);

        var plan = HistoricalFocusRedistribution.Plan(rows, today);

        Assert.Empty(plan.SkippedMonths);
        Assert.Equal(5, plan.DeleteRowIds.Count);   // Sep 26–30 removed
        Assert.Equal(24, plan.Upserts.Count);       // Sep 1–24 receive the reclaimed time
        Assert.All(plan.Upserts, u => Assert.True(u.Date < today));      // strictly before today
        Assert.DoesNotContain(plan.Upserts, u => u.Date == today);       // never today

        var corrected = ApplyInMemory(rows, plan);
        Assert.Equal(total, corrected.Sum(r => (long)r.Duration.TotalSeconds));
        Assert.DoesNotContain(corrected, r => r.Date > today);
    }

    [Fact]
    public void PlanIsDeterministicForIdenticalInput()
    {
        long a = 1, b = 1;
        var today = new DateOnly(2026, 9, 25);
        var pa = HistoricalFocusRedistribution.Plan(DistributeMonth(2026, 9, "", 123_457, ref a), today);
        var pb = HistoricalFocusRedistribution.Plan(DistributeMonth(2026, 9, "", 123_457, ref b), today);
        Assert.Equal(pa.DeleteRowIds, pb.DeleteRowIds);
        Assert.Equal(pa.Upserts, pb.Upserts);
    }

    [Fact]
    public void DayOneOfMonthIsSkippedForManualReviewWithNoMutation()
    {
        long id = 1;
        var rows = DistributeMonth(2026, 9, "", 300_000, ref id);
        var plan = HistoricalFocusRedistribution.Plan(rows, new DateOnly(2026, 9, 1));

        Assert.False(plan.HasChanges);      // nothing is deleted or written
        Assert.Empty(plan.DeleteRowIds);
        Assert.Empty(plan.Upserts);
        var skip = Assert.Single(plan.SkippedMonths);
        Assert.Equal((2026, 9), (skip.Year, skip.Month));
        Assert.Equal(29, skip.RowCount);    // Sep 2–30 left untouched, reported for manual review
    }

    [Fact]
    public void FullyFutureMonthIsSkippedWithNoCrossMonthRedistribution()
    {
        long id = 1;
        var rows = new List<HistoricalFocusRecord> { Row(id++, new DateOnly(2026, 8, 10), 3600) }; // unrelated past history
        rows.AddRange(DistributeMonth(2026, 10, "", 300_000, ref id));                             // fully-future (today = Sep 25)

        var plan = HistoricalFocusRedistribution.Plan(rows, new DateOnly(2026, 9, 25));

        Assert.False(plan.HasChanges);
        Assert.Empty(plan.Upserts);         // never redistributes into August or any other month
        var skip = Assert.Single(plan.SkippedMonths);
        Assert.Equal((2026, 10), (skip.Year, skip.Month));
    }

    // ---- Repository / apply integration tests -----------------------------

    [Fact]
    public async Task ApplyMidMonthRemovesFutureRowsPreservesTotalAndIsIdempotent()
    {
        using var temp = new TempDirectory();
        var factory = new SqliteConnectionFactory(Path.Combine(temp.Path, "focus_key.db"));
        new DatabaseBootstrapper(factory).Initialize();
        var repo = new SqliteHistoricalFocusRepository(factory);

        long id = 1;
        await ImportAsync(repo, DistributeMonth(2026, 9, "", 300_000, ref id));
        var today = new DateOnly(2026, 9, 25);
        long before = (await repo.GetAllAsync()).Sum(r => (long)r.Duration.TotalSeconds);

        var report = await HistoricalRepairService.RepairAsync(factory, today, apply: true);
        Assert.Equal(5, report.FutureRowsFound);
        Assert.Equal(5, report.Outcome!.RowsDeleted);
        Assert.Equal(24, report.Outcome!.RowsUpserted);
        Assert.Equal(0, report.Outcome!.MonthsSkipped);

        var after = await repo.GetAllAsync();
        Assert.DoesNotContain(after, r => r.Date > today);
        Assert.Equal(before, after.Sum(r => (long)r.Duration.TotalSeconds));   // monthly & overall total preserved
        Assert.All(after, r => Assert.True(r.Duration > TimeSpan.Zero));        // storage CHECK invariant holds
        Assert.Equal(10_000, (long)after.Single(r => r.Date == today).Duration.TotalSeconds); // today untouched

        var second = await HistoricalRepairService.RepairAsync(factory, today, apply: true);
        Assert.Equal(0, second.FutureRowsFound);
        Assert.Null(second.Outcome);   // no changes → apply is a no-op
        Assert.Equal(before, (await repo.GetAllAsync()).Sum(r => (long)r.Duration.TotalSeconds));
    }

    [Fact]
    public async Task RepairPreservesSourceHoursOnExistingRowsAndSynthesizesForNewRows()
    {
        using var temp = new TempDirectory();
        var factory = new SqliteConnectionFactory(Path.Combine(temp.Path, "focus_key.db"));
        new DatabaseBootstrapper(factory).Initialize();
        var repo = new SqliteHistoricalFocusRepository(factory);

        long id = 1;
        await ImportAsync(repo, DistributeMonth(2026, 9, "", 300_000, ref id));
        double originalSep1Hours = (await repo.GetAsync(new DateOnly(2026, 9, 1), ""))!.SourceHours;

        await HistoricalRepairService.RepairAsync(factory, new DateOnly(2026, 9, 25), apply: true);

        var sep1 = await repo.GetAsync(new DateOnly(2026, 9, 1), "");
        Assert.NotNull(sep1);
        Assert.True(sep1!.Duration.TotalSeconds > 10_000);          // received redistributed time
        Assert.Equal(originalSep1Hours, sep1.SourceHours);          // original provenance preserved, not overwritten
    }

    [Fact]
    public async Task RepairInsertsNewTargetRowsWhenTheyDoNotExist()
    {
        using var temp = new TempDirectory();
        var factory = new SqliteConnectionFactory(Path.Combine(temp.Path, "focus_key.db"));
        new DatabaseBootstrapper(factory).Initialize();
        var repo = new SqliteHistoricalFocusRepository(factory);

        // Only future rows exist (Sep 26–30); no earlier September rows → targets must be INSERTed.
        var futureOnly = new List<HistoricalFocusEntry>();
        for (int day = 26; day <= 30; day++)
            futureOnly.Add(new HistoricalFocusEntry(new DateOnly(2026, 9, day), "", TimeSpan.FromSeconds(10_000), 10_000 / 3600.0));
        await repo.ImportAsync(futureOnly, futureOnly.Count, 0);

        var today = new DateOnly(2026, 9, 25);
        var report = await HistoricalRepairService.RepairAsync(factory, today, apply: true);
        Assert.Equal(5, report.Outcome!.RowsDeleted);
        Assert.Equal(24, report.Outcome!.RowsUpserted);

        var after = await repo.GetAllAsync();
        Assert.Equal(24, after.Count);                              // Sep 1–24 all newly inserted
        Assert.DoesNotContain(after, r => r.Date > today);
        Assert.Equal(50_000, after.Sum(r => (long)r.Duration.TotalSeconds));
        var newRow = after.Single(r => r.Date == new DateOnly(2026, 9, 1));
        Assert.Equal(newRow.Duration.TotalSeconds / 3600.0, newRow.SourceHours, 6); // synthesized provenance
    }

    [Fact]
    public async Task RepairIsolatesProjectsAndPreservesEachProjectTotal()
    {
        using var temp = new TempDirectory();
        var factory = new SqliteConnectionFactory(Path.Combine(temp.Path, "focus_key.db"));
        new DatabaseBootstrapper(factory).Initialize();
        var repo = new SqliteHistoricalFocusRepository(factory);

        long id = 1;
        var rows = new List<HistoricalFocusRecord>();
        rows.AddRange(DistributeMonth(2026, 9, "", 300_000, ref id));
        rows.AddRange(DistributeMonth(2026, 9, "Writing", 60_000, ref id));
        await ImportAsync(repo, rows);

        var today = new DateOnly(2026, 9, 25);
        await HistoricalRepairService.RepairAsync(factory, today, apply: true);

        var after = await repo.GetAllAsync();
        Assert.DoesNotContain(after, r => r.Date > today);
        Assert.Equal(300_000, after.Where(r => r.Project == "").Sum(r => (long)r.Duration.TotalSeconds));
        Assert.Equal(60_000, after.Where(r => r.Project == "Writing").Sum(r => (long)r.Duration.TotalSeconds));
    }

    [Fact]
    public async Task ApplyRollsBackCompletelyOnFailure()
    {
        using var temp = new TempDirectory();
        var factory = new SqliteConnectionFactory(Path.Combine(temp.Path, "focus_key.db"));
        new DatabaseBootstrapper(factory).Initialize();
        var repo = new SqliteHistoricalFocusRepository(factory);
        long id = 1;
        await ImportAsync(repo, DistributeMonth(2026, 9, "", 300_000, ref id));

        long sep1Id = (await repo.GetAsync(new DateOnly(2026, 9, 1), ""))!.Id;
        // Deletes a real row, then writes a zero-duration upsert that violates CHECK(duration_seconds > 0) mid-transaction.
        var badPlan = new RedistributionPlan([sep1Id], [new HistoricalFocusUpsert(new DateOnly(2026, 9, 2), "", 0)], []);

        await Assert.ThrowsAnyAsync<SqliteException>(() => HistoricalRepairService.ApplyPlanAsync(factory, badPlan));

        var after = await repo.GetAllAsync();
        Assert.Equal(30, after.Count);                                    // delete was rolled back
        Assert.Equal(300_000, after.Sum(r => (long)r.Duration.TotalSeconds));
        Assert.NotNull(await repo.GetAsync(new DateOnly(2026, 9, 1), "")); // Sep 1 restored
    }

    [Fact]
    public async Task ApplyHonorsCancellationWithoutPartialWrites()
    {
        using var temp = new TempDirectory();
        var factory = new SqliteConnectionFactory(Path.Combine(temp.Path, "focus_key.db"));
        new DatabaseBootstrapper(factory).Initialize();
        var repo = new SqliteHistoricalFocusRepository(factory);
        long id = 1;
        await ImportAsync(repo, DistributeMonth(2026, 9, "", 300_000, ref id));
        var today = new DateOnly(2026, 9, 25);
        var plan = HistoricalFocusRedistribution.Plan(await repo.GetAllAsync(), today);
        Assert.True(plan.HasChanges);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HistoricalRepairService.ApplyPlanAsync(factory, plan, cts.Token));

        var after = await repo.GetAllAsync();
        Assert.Equal(30, after.Count);
        Assert.Equal(5, after.Count(r => r.Date > today));   // future rows untouched — no partial write
        Assert.Equal(300_000, after.Sum(r => (long)r.Duration.TotalSeconds));
    }

    [Fact]
    public async Task DryRunReportsFutureRowsButWritesNothing()
    {
        using var temp = new TempDirectory();
        var factory = new SqliteConnectionFactory(Path.Combine(temp.Path, "focus_key.db"));
        new DatabaseBootstrapper(factory).Initialize();
        var repo = new SqliteHistoricalFocusRepository(factory);
        long id = 1;
        await ImportAsync(repo, DistributeMonth(2026, 9, "", 300_000, ref id));
        var today = new DateOnly(2026, 9, 25);

        var report = await HistoricalRepairService.RepairAsync(factory, today, apply: false);
        Assert.False(report.Applied);
        Assert.Null(report.Outcome);
        Assert.Equal(5, report.FutureRowsFound);
        Assert.Equal(24, report.ProposedTargetDays);

        var after = await repo.GetAllAsync();
        Assert.Equal(30, after.Count);                       // unchanged
        Assert.Equal(5, after.Count(r => r.Date > today));   // future rows still present
        Assert.Equal(300_000, after.Sum(r => (long)r.Duration.TotalSeconds));
    }

    [Fact]
    public async Task RepairDoesNotTouchNativeSessions()
    {
        using var temp = new TempDirectory();
        var factory = new SqliteConnectionFactory(Path.Combine(temp.Path, "focus_key.db"));
        new DatabaseBootstrapper(factory).Initialize();
        var history = new SqliteHistoricalFocusRepository(factory);
        var sessions = new SqliteSessionRepository(factory);

        var session = TestSessions.Finished(
            SessionStatus.Completed,
            startedAt: new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.Zero),
            type: SessionType.Work,
            plannedDuration: TimeSpan.FromMinutes(25));
        await sessions.AddAsync(session);

        long id = 1;
        await ImportAsync(history, DistributeMonth(2026, 9, "", 300_000, ref id));
        await HistoricalRepairService.RepairAsync(factory, new DateOnly(2026, 9, 25), apply: true);

        var afterSessions = await sessions.GetStartedBetweenAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
        Assert.Single(afterSessions);
        Assert.Equal(session.Id, afterSessions[0].Id);
    }
}
