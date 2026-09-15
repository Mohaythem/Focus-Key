using System.Globalization;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Tests.Sessions;
using FocusKey.Foundation.Today;

namespace FocusKey.Foundation.Tests.Today;

public sealed class TodayLauncherTests
{
    [Fact]
    public void TodaySnapshotDefaultsToDefaultDurationsWhenOmitted()
    {
        var snapshot = new TodaySnapshot(
            new DateOnly(2026, 9, 15),
            TimeZoneInfo.Utc,
            TestSessions.Anchor,
            TestSessions.Anchor.AddDays(1),
            [],
            null);

        Assert.Equal(SessionDurations.Default, snapshot.Durations);
        Assert.Equal(TimeSpan.FromMinutes(30), snapshot.Durations.Work);
        Assert.Equal(TimeSpan.FromMinutes(10), snapshot.Durations.Break);
    }

    [Fact]
    public async Task TodayServicePopulatesConfiguredDurationsFromProvider()
    {
        using var store = new SessionStore();
        var customDurations = new SessionDurations(TimeSpan.FromMinutes(45), TimeSpan.FromMinutes(15));
        var durationProvider = new FixedDurationProvider(customDurations);
        var service = new TodayService(store.Repository, durationProvider, new ManualTimeProvider(TestSessions.Anchor));

        var snapshot = await service.ReadAsync();

        Assert.Equal(customDurations, snapshot.Durations);
        Assert.Equal(TimeSpan.FromMinutes(45), snapshot.Durations.Work);
        Assert.Equal(TimeSpan.FromMinutes(15), snapshot.Durations.Break);
    }

    [Theory]
    [InlineData(30, 0, "30 min")]
    [InlineData(10, 0, "10 min")]
    [InlineData(45, 0, "45 min")]
    [InlineData(15, 0, "15 min")]
    [InlineData(60, 0, "1 hr")]
    [InlineData(120, 0, "2 hr")]
    [InlineData(90, 0, "1h 30m")]
    [InlineData(25, 30, "25m 30s")]
    [InlineData(0, 45, "45 sec")]
    [InlineData(0, 0, "0 min")]
    public void FormatLauncherDurationFormatsCorrectly(int minutes, int seconds, string expected)
    {
        var duration = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        string formatted = TodayFormatting.FormatLauncherDuration(duration);
        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void FormatLauncherDurationPreservesWesternDigitsUnderNonEnglishLocales()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUICulture = CultureInfo.CurrentUICulture;
        try
        {
            var arabicCulture = new CultureInfo("ar-SA");
            CultureInfo.CurrentCulture = arabicCulture;
            CultureInfo.CurrentUICulture = arabicCulture;

            string work = TodayFormatting.FormatLauncherDuration(TimeSpan.FromMinutes(30));
            string brk = TodayFormatting.FormatLauncherDuration(TimeSpan.FromMinutes(10));

            Assert.Equal("30 min", work);
            Assert.Equal("10 min", brk);
            Assert.DoesNotContain("٣", work, StringComparison.Ordinal);
            Assert.DoesNotContain("٠", work, StringComparison.Ordinal);
            Assert.All(work, c => Assert.True(c <= 127));
            Assert.All(brk, c => Assert.True(c <= 127));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
        }
    }

    [Fact]
    public async Task StartAsyncInvokesStartDelegateWithCorrectTypeAndRefreshes()
    {
        int reads = 0;
        SessionType? startedType = null;
        var runningWork = TestSessions.Running(type: SessionType.Work);

        using var controller = new TodayController(
            _ => Task.FromResult(++reads == 1 ? EmptySnapshot() : EmptySnapshot(runningWork)),
            (type, _) => { startedType = type; return Task.FromResult(runningWork); },
            (_, _) => throw new InvalidOperationException(),
            _ => { });

        await controller.OpenAsync();
        Assert.Equal(1, reads);
        Assert.Null(controller.Snapshot!.Running);

        await controller.StartAsync(SessionType.Work);

        Assert.Equal(SessionType.Work, startedType);
        Assert.Equal(2, reads);
        Assert.NotNull(controller.Snapshot!.Running);
        Assert.Equal(SessionType.Work, controller.Snapshot.Running!.Type);
        Assert.False(controller.IsStarting);
    }

    [Fact]
    public async Task StartAsyncForBreakInvokesStartDelegateWithBreak()
    {
        int reads = 0;
        SessionType? startedType = null;
        var runningBreak = TestSessions.Running(type: SessionType.Break);

        using var controller = new TodayController(
            _ => Task.FromResult(++reads == 1 ? EmptySnapshot() : EmptySnapshot(runningBreak)),
            (type, _) => { startedType = type; return Task.FromResult(runningBreak); },
            (_, _) => throw new InvalidOperationException(),
            _ => { });

        await controller.OpenAsync();
        await controller.StartAsync(SessionType.Break);

        Assert.Equal(SessionType.Break, startedType);
        Assert.NotNull(controller.Snapshot!.Running);
        Assert.Equal(SessionType.Break, controller.Snapshot.Running!.Type);
    }

    [Fact]
    public async Task StartAsyncIsBlockedWhenSessionAlreadyRunningInSnapshot()
    {
        int startCalls = 0;
        var running = TestSessions.Running();

        using var controller = new TodayController(
            _ => Task.FromResult(EmptySnapshot(running)),
            (type, _) => { startCalls++; return Task.FromResult(running); },
            (_, _) => throw new InvalidOperationException(),
            _ => { });

        await controller.OpenAsync();
        Assert.NotNull(controller.Snapshot!.Running);

        await controller.StartAsync(SessionType.Work);
        Assert.Equal(0, startCalls);
    }

    [Fact]
    public async Task StartAsyncHandlesActiveSessionAlreadyExistsGracefully()
    {
        int reads = 0;
        var alreadyRunning = TestSessions.Running();

        using var controller = new TodayController(
            _ => Task.FromResult(++reads == 1 ? EmptySnapshot() : EmptySnapshot(alreadyRunning)),
            (type, _) => Task.FromException<SessionRecord>(new ActiveSessionAlreadyExistsException(alreadyRunning.Id)),
            (_, _) => throw new InvalidOperationException(),
            _ => { });

        await controller.OpenAsync();
        await controller.StartAsync(SessionType.Work);

        Assert.Null(controller.Error);
        Assert.Equal(2, reads);
        Assert.NotNull(controller.Snapshot!.Running);
    }

    [Fact]
    public async Task StartAsyncFailureSetsActionableErrorAndReportsException()
    {
        var errors = new List<Exception>();
        var failure = new InvalidOperationException("Failed to start session engine");

        using var controller = new TodayController(
            _ => Task.FromResult(EmptySnapshot()),
            (type, _) => Task.FromException<SessionRecord>(failure),
            (_, _) => throw new InvalidOperationException(),
            errors.Add);

        await controller.OpenAsync();
        await controller.StartAsync(SessionType.Work);

        Assert.NotNull(controller.Error);
        Assert.Contains("Could not start", controller.Error);
        Assert.Single(errors);
        Assert.Same(failure, errors[0]);
    }

    private static TodaySnapshot EmptySnapshot(SessionRecord? running = null, SessionDurations? durations = null) =>
        new(new DateOnly(2026, 9, 15), TimeZoneInfo.Utc, TestSessions.Anchor,
            new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero), [], running, durations);

    private sealed class FixedDurationProvider(SessionDurations durations) : ISessionDurationProvider
    {
        public Task<SessionDurations> GetDurationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(durations);
    }
}
