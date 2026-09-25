using FocusKey.Foundation.History;

namespace FocusKey.HistoricalRepair;

/// <summary>One upsert the repair produces: the absolute corrected duration for a single (date, project).</summary>
public sealed record HistoricalFocusUpsert(DateOnly Date, string Project, long DurationSeconds);

/// <summary>A month whose future-dated rows cannot be safely corrected automatically and were left untouched.</summary>
public sealed record SkippedMonth(int Year, int Month, int RowCount, long TotalSeconds, IReadOnlyList<string> Projects);

/// <summary>The deterministic set of changes that corrects future-dated historical focus aggregates.</summary>
public sealed record RedistributionPlan(
    IReadOnlyList<long> DeleteRowIds,
    IReadOnlyList<HistoricalFocusUpsert> Upserts,
    IReadOnlyList<SkippedMonth> SkippedMonths)
{
    public bool HasChanges => DeleteRowIds.Count > 0 || Upserts.Count > 0;
    public bool IsEmpty => !HasChanges && SkippedMonths.Count == 0;
    public static readonly RedistributionPlan Empty = new([], [], []);
}

/// <summary>The result of applying the redistribution to storage.</summary>
public sealed record RedistributionOutcome(int FutureRowsFound, int RowsDeleted, int RowsUpserted, int MonthsSkipped);

/// <summary>
/// Pure, deterministic, idempotent correction for <c>historical_focus</c> aggregate rows whose calendar date
/// is later than the current day. Such rows exist only because an earlier bulk import spread a whole month's
/// total evenly across every calendar day while the month was still in progress, dating the tail days after
/// today. Authoritative policy:
/// <list type="number">
///   <item>Never redistribute onto today or any future date.</item>
///   <item>Never move data across calendar months — a row is only ever folded back into its own month.</item>
///   <item>Valid target dates for a month are exactly <c>monthStart &lt;= date &lt; today</c>.</item>
///   <item>If a month has no valid target date (e.g. today is the 1st of that month, or the whole month is in
///         the future), that month is <b>skipped</b> and reported for manual review — zero rows mutated.</item>
///   <item>For corrected months, the month total, project identity, determinism and idempotency are preserved.</item>
/// </list>
/// A record set with no future-dated rows produces an empty plan, so applying it more than once is safe.
/// </summary>
public static class HistoricalFocusRedistribution
{
    /// <summary>Computes the corrective plan. Pure: it reads <paramref name="records"/> and returns changes.</summary>
    public static RedistributionPlan Plan(IReadOnlyList<HistoricalFocusRecord> records, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(records);

        var future = records.Where(r => r.Date > today).ToArray();
        if (future.Length == 0) return RedistributionPlan.Empty;

        // Baseline absolute seconds for every existing (date, project); the working set folds reclaimed
        // seconds into these so multiple projects in one month compose correctly.
        var baseline = new Dictionary<(DateOnly Date, string Project), long>();
        foreach (var r in records)
            baseline[(r.Date, r.Project)] = (long)r.Duration.TotalSeconds;

        var working = new Dictionary<(DateOnly Date, string Project), long>();
        long Current((DateOnly Date, string Project) key) =>
            working.TryGetValue(key, out var w) ? w : baseline.TryGetValue(key, out var b) ? b : 0L;

        var deleteIds = new List<long>();
        var skipped = new List<SkippedMonth>();

        foreach (var monthGroup in future
            .GroupBy(r => (r.Date.Year, r.Date.Month))
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month))
        {
            var (year, month) = monthGroup.Key;
            var monthStart = new DateOnly(year, month, 1);
            var monthEnd = monthStart.AddMonths(1); // exclusive

            // Valid targets: strictly before today AND within this same month.
            var targets = DaysInRange(monthStart, MinDate(today, monthEnd));

            var monthRows = monthGroup.ToArray();
            if (targets.Count == 0)
            {
                // No safe in-month day before today (day-1 edge or fully-future month). Do not mutate;
                // never dump onto today and never cross months. Report the month for manual review.
                long secs = monthRows.Sum(r => (long)r.Duration.TotalSeconds);
                var projects = monthRows.Select(r => r.Project).Distinct()
                    .OrderBy(p => p, StringComparer.Ordinal).ToList();
                skipped.Add(new SkippedMonth(year, month, monthRows.Length, secs, projects));
                continue;
            }

            foreach (var projectGroup in monthRows.GroupBy(r => r.Project))
            {
                string project = projectGroup.Key;
                long reclaimed = 0;
                foreach (var r in projectGroup)
                {
                    reclaimed += (long)r.Duration.TotalSeconds;
                    deleteIds.Add(r.Id);
                }
                DistributeInto(working, Current, targets, project, reclaimed);
            }
        }

        deleteIds.Sort(); // deterministic delete order

        var upserts = working
            .OrderBy(kv => kv.Key.Date).ThenBy(kv => kv.Key.Project, StringComparer.Ordinal)
            .Select(kv => new HistoricalFocusUpsert(kv.Key.Date, kv.Key.Project, kv.Value))
            .ToList();

        return new RedistributionPlan(deleteIds, upserts, skipped);
    }

    private static void DistributeInto(
        Dictionary<(DateOnly Date, string Project), long> working,
        Func<(DateOnly Date, string Project), long> current,
        IReadOnlyList<DateOnly> targets,
        string project,
        long reclaimedSeconds)
    {
        int n = targets.Count;
        if (n == 0 || reclaimedSeconds <= 0) return;

        long baseShare = reclaimedSeconds / n;
        long remainder = reclaimedSeconds % n; // exact split, no rounding drift

        for (int i = 0; i < n; i++)
        {
            long add = baseShare + (i < remainder ? 1 : 0);
            if (add <= 0) continue;
            var key = (targets[i], project);
            working[key] = current(key) + add;
        }
    }

    private static List<DateOnly> DaysInRange(DateOnly startInclusive, DateOnly endExclusive)
    {
        var days = new List<DateOnly>();
        for (var d = startInclusive; d < endExclusive; d = d.AddDays(1))
            days.Add(d);
        return days;
    }

    private static DateOnly MinDate(DateOnly a, DateOnly b) => a < b ? a : b;
}
