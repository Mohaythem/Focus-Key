using FocusKey.Foundation.Data;
using FocusKey.Foundation.History;
using Microsoft.Data.Sqlite;

namespace FocusKey.HistoricalRepair;

/// <summary>What a dry run (or an apply) found and, when applied, did. Contains no secrets — safe to print.</summary>
public sealed record RepairReport(
    string DatabasePath,
    int FutureRowsFound,
    long FutureSeconds,
    IReadOnlyList<string> AffectedProjects,
    IReadOnlyList<(int Year, int Month)> AffectedMonths,
    int ProposedTargetDays,
    IReadOnlyList<SkippedMonth> SkippedMonths,
    bool Applied,
    RedistributionOutcome? Outcome);

/// <summary>
/// Storage side of the one-time historical-date repair. Reads through the normal repository, computes the
/// plan with <see cref="HistoricalFocusRedistribution"/>, and (only when asked) applies it inside a single
/// all-or-nothing transaction. Never touches native session records.
/// </summary>
public static class HistoricalRepairService
{
    /// <summary>Inspects the database and, when <paramref name="apply"/> is true, applies the correction.</summary>
    public static async Task<RepairReport> RepairAsync(
        SqliteConnectionFactory connections,
        DateOnly today,
        bool apply,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connections);

        var repository = new SqliteHistoricalFocusRepository(connections);
        IReadOnlyList<HistoricalFocusRecord> all = await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        var future = all.Where(r => r.Date > today).ToArray();
        RedistributionPlan plan = HistoricalFocusRedistribution.Plan(all, today);

        var affectedProjects = future.Select(r => r.Project).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
        var affectedMonths = future.Select(r => (r.Date.Year, r.Date.Month)).Distinct()
            .OrderBy(m => m.Year).ThenBy(m => m.Month).ToList();
        long futureSeconds = future.Sum(r => (long)r.Duration.TotalSeconds);
        int proposedTargetDays = plan.Upserts.Select(u => u.Date).Distinct().Count();

        RedistributionOutcome? outcome = null;
        if (apply && plan.HasChanges)
        {
            outcome = await ApplyPlanAsync(connections, plan, cancellationToken).ConfigureAwait(false);
        }

        return new RepairReport(
            connections.DatabaseFile, future.Length, futureSeconds, affectedProjects,
            affectedMonths, proposedTargetDays, plan.SkippedMonths, apply, outcome);
    }

    /// <summary>
    /// Applies a pre-computed plan in one transaction. Rolls back completely on any failure or cancellation,
    /// so a month is never partially repaired. Existing rows keep their original <c>source_hours</c>
    /// provenance; only newly created target rows carry maintenance-synthesized <c>source_hours</c>.
    /// </summary>
    public static async Task<RedistributionOutcome> ApplyPlanAsync(
        SqliteConnectionFactory connections,
        RedistributionPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(plan);

        int futureFound = plan.DeleteRowIds.Count + plan.SkippedMonths.Sum(m => m.RowCount);
        if (!plan.HasChanges)
        {
            return new RedistributionOutcome(futureFound, 0, 0, plan.SkippedMonths.Count);
        }

        await using SqliteConnection connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        string nowUtc = UtcTimestamp.Format(DateTimeOffset.UtcNow);
        try
        {
            foreach (long id in plan.DeleteRowIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var delete = connection.CreateCommand();
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM historical_focus WHERE id = $id;";
                delete.Parameters.AddWithValue("$id", id);
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (HistoricalFocusUpsert upsert in plan.Upserts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                // On an existing target day, only the duration/timestamp change — the original imported
                // source_hours provenance is deliberately preserved (not overwritten). A brand-new target
                // row has no prior provenance, so it is stamped with the synthesized hours.
                command.CommandText =
                    """
                    INSERT INTO historical_focus (date, project, duration_seconds, source_hours, imported_at_utc)
                    VALUES ($date, $project, $duration, $hours, $now)
                    ON CONFLICT (date, project) DO UPDATE SET
                        duration_seconds = excluded.duration_seconds,
                        imported_at_utc  = excluded.imported_at_utc;
                    """;
                command.Parameters.AddWithValue("$date", upsert.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$project", upsert.Project);
                command.Parameters.AddWithValue("$duration", upsert.DurationSeconds);
                command.Parameters.AddWithValue("$hours", upsert.DurationSeconds / 3600.0);
                command.Parameters.AddWithValue("$now", nowUtc);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { /* best-effort rollback */ }
            throw;
        }

        return new RedistributionOutcome(futureFound, plan.DeleteRowIds.Count, plan.Upserts.Count, plan.SkippedMonths.Count);
    }
}
