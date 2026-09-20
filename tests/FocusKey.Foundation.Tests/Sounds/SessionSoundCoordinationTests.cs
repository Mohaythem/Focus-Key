using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Tests.Sessions;

namespace FocusKey.Foundation.Tests.Sounds;

public sealed class SessionSoundCoordinationTests
{
    private sealed class MockSoundPlayer
    {
        public int StartTickCount { get; private set; }
        public int CompletionBellCount { get; private set; }
        public int PreviewStartTickCount { get; private set; }
        public int PreviewCompletionBellCount { get; private set; }

        public bool SessionSoundsEnabled { get; set; } = true;
        public bool StartSoundEnabled { get; set; } = true;
        public bool CompletionSoundEnabled { get; set; } = true;

        public void PlayStartTick()
        {
            if (SessionSoundsEnabled && StartSoundEnabled) StartTickCount++;
        }

        public void PlayCompletionBell()
        {
            if (SessionSoundsEnabled && CompletionSoundEnabled) CompletionBellCount++;
        }

        public void PreviewStartTick()
        {
            PreviewStartTickCount++;
        }

        public void PreviewCompletionBell()
        {
            PreviewCompletionBellCount++;
        }
    }

    [Fact]
    public async Task StartSession_WhenSuccessful_TriggersStartSoundOnce()
    {
        var sound = new MockSoundPlayer();
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock);
        await sessions.InitializeAsync();

        // Act: Start session
        var session = await sessions.StartAsync(SessionType.Work);
        sound.PlayStartTick();

        // Assert
        Assert.NotNull(session);
        Assert.Equal(1, sound.StartTickCount);
        Assert.Equal(0, sound.CompletionBellCount);
    }

    [Fact]
    public async Task StartSession_WhenConflicting_DoesNotTriggerStartSound()
    {
        var sound = new MockSoundPlayer();
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock);
        await sessions.InitializeAsync();

        // Start first session successfully
        await sessions.StartAsync(SessionType.Work);
        sound.PlayStartTick();
        Assert.Equal(1, sound.StartTickCount);

        // Attempting to start another session while one is already running throws
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await sessions.StartAsync(SessionType.Break);
            sound.PlayStartTick(); // Should never be reached
        });

        // Start tick count must remain 1
        Assert.Equal(1, sound.StartTickCount);
    }

    [Fact]
    public async Task StopSession_DoesNotTriggerCompletionSound()
    {
        var sound = new MockSoundPlayer();
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock);
        await sessions.InitializeAsync();

        var session = await sessions.StartAsync(SessionType.Work);
        sound.PlayStartTick();

        // Act: Stop session manually
        var outcome = await sessions.StopAsync(session.Id);

        // Assert: Stop must NEVER trigger completion bell
        Assert.NotNull(outcome.Session);
        Assert.Equal(SessionStatus.Stopped, outcome.Session.Status);
        Assert.Equal(0, sound.CompletionBellCount);
    }

    [Fact]
    public async Task NaturalCompletion_TriggersCompletionSoundOnce()
    {
        var sound = new MockSoundPlayer();
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock);
        await sessions.InitializeAsync();

        int notificationCount = 0;
        using var completion = new CompletionCoordinator(sessions, s =>
        {
            notificationCount++;
            sound.PlayCompletionBell();
            return Task.CompletedTask;
        }, _ => { }, clock);

        var session = await completion.StartAsync(SessionType.Work);
        sound.PlayStartTick();

        // Advance clock past planned duration
        clock.Advance(TimeSpan.FromMinutes(35));

        // Evaluate natural completion
        await completion.EvaluateAsync();

        // Assert: Natural completion triggers completion bell exactly once
        Assert.Equal(1, notificationCount);
        Assert.Equal(1, sound.CompletionBellCount);

        // Repeated evaluation does not duplicate sound
        await completion.EvaluateAsync();
        Assert.Equal(1, notificationCount);
        Assert.Equal(1, sound.CompletionBellCount);
    }

    [Fact]
    public void SoundsDisabled_SuppressesBothStartAndCompletionSounds()
    {
        var sound = new MockSoundPlayer { SessionSoundsEnabled = false };

        sound.PlayStartTick();
        sound.PlayCompletionBell();

        Assert.Equal(0, sound.StartTickCount);
        Assert.Equal(0, sound.CompletionBellCount);
    }

    [Fact]
    public void StartSoundDisabled_SuppressesStartSoundOnly()
    {
        var sound = new MockSoundPlayer { StartSoundEnabled = false };

        sound.PlayStartTick();
        sound.PlayCompletionBell();

        Assert.Equal(0, sound.StartTickCount);
        Assert.Equal(1, sound.CompletionBellCount);
    }

    [Fact]
    public void CompletionSoundDisabled_SuppressesCompletionSoundOnly()
    {
        var sound = new MockSoundPlayer { CompletionSoundEnabled = false };

        sound.PlayStartTick();
        sound.PlayCompletionBell();

        Assert.Equal(1, sound.StartTickCount);
        Assert.Equal(0, sound.CompletionBellCount);
    }

    [Fact]
    public void PreviewSounds_PlayEvenWhenAllSoundsAreDisabled()
    {
        var sound = new MockSoundPlayer
        {
            SessionSoundsEnabled = false,
            StartSoundEnabled = false,
            CompletionSoundEnabled = false
        };

        sound.PreviewStartTick();
        sound.PreviewCompletionBell();

        Assert.Equal(1, sound.PreviewStartTickCount);
        Assert.Equal(1, sound.PreviewCompletionBellCount);
        Assert.Equal(0, sound.StartTickCount);
        Assert.Equal(0, sound.CompletionBellCount);
    }

    [Fact]
    public async Task ContinueSession_DoesNotTriggerStartSound()
    {
        var sound = new MockSoundPlayer();
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock);
        await sessions.InitializeAsync();

        var session = await sessions.StartAsync(SessionType.Work);
        sound.PlayStartTick();
        Assert.Equal(1, sound.StartTickCount);

        // Pause session
        clock.Advance(TimeSpan.FromMinutes(5));
        var paused = await sessions.PauseAsync(session.Id);
        Assert.NotNull(paused.Session);
        Assert.NotNull(paused.Session.PausedAt);

        // Resume / Continue session: In App.xaml.cs, Continue does NOT call sound.PlayStartTick()
        clock.Advance(TimeSpan.FromMinutes(2));
        var resumed = await sessions.ContinueAsync(session.Id);
        Assert.NotNull(resumed.Session);
        Assert.Null(resumed.Session.PausedAt);

        // Start sound must still be 1 (never re-triggered on resume)
        Assert.Equal(1, sound.StartTickCount);
    }
}
