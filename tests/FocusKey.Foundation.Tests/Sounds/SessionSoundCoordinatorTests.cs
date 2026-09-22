using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Sounds;
using FocusKey.Foundation.Tests.Sessions;

namespace FocusKey.Foundation.Tests.Sounds;

public sealed class SessionSoundCoordinatorTests
{
    private sealed class MockSoundPlayer : ISoundPlayer
    {
        public int StartTickCount { get; private set; }
        public int StopCount { get; private set; }
        public int NaturalCompletionBellCount { get; private set; }
        public int PreviewStartTickCount { get; private set; }
        public int PreviewCompletionBellCount { get; private set; }

        public void PlayStartTick() => StartTickCount++;
        public void PlayStop() => StopCount++;
        public void PlayCompletionBell() => PlayStop();
        public void PlayNaturalCompletionBell() => NaturalCompletionBellCount++;
        public void PreviewStartTick() => PreviewStartTickCount++;
        public void PreviewCompletionBell() => PreviewCompletionBellCount++;
    }

    [Fact]
    public void SessionStart_PlaysStartSoundOnce()
    {
        var player = new MockSoundPlayer();
        var coordinator = new SessionSoundCoordinator(player);
        var session = TestSessions.Running();

        coordinator.HandleSessionStarted(session);

        Assert.Equal(1, player.StartTickCount);
        Assert.Equal(0, player.StopCount);
        Assert.Equal(0, player.NaturalCompletionBellCount);
    }

    [Fact]
    public void SessionContinue_AfterPause_PlaysStartSoundOnce()
    {
        var player = new MockSoundPlayer();
        var coordinator = new SessionSoundCoordinator(player);
        var session = TestSessions.Running();
        var outcome = SessionOutcome.Continued(session);

        coordinator.HandleSessionContinued(outcome);

        Assert.Equal(1, player.StartTickCount);
        Assert.Equal(0, player.StopCount);
        Assert.Equal(0, player.NaturalCompletionBellCount);
    }

    [Fact]
    public void SessionContinue_ConflictOutcome_DoesNotPlaySound()
    {
        var player = new MockSoundPlayer();
        var coordinator = new SessionSoundCoordinator(player);
        var session = TestSessions.Running();
        var outcome = SessionOutcome.Conflict(session);

        coordinator.HandleSessionContinued(outcome);

        Assert.Equal(0, player.StartTickCount);
        Assert.Equal(0, player.StopCount);
        Assert.Equal(0, player.NaturalCompletionBellCount);
    }

    [Fact]
    public void SessionStop_PlaysCompletionSoundOnce()
    {
        var player = new MockSoundPlayer();
        var coordinator = new SessionSoundCoordinator(player);
        var session = TestSessions.Finished(SessionStatus.Stopped);
        var outcome = SessionOutcome.Stopped(session);

        coordinator.HandleSessionStopped(outcome);

        Assert.Equal(0, player.StartTickCount);
        Assert.Equal(1, player.StopCount);
        Assert.Equal(0, player.NaturalCompletionBellCount);
    }

    [Fact]
    public void SessionStop_ConflictOutcome_DoesNotPlaySound()
    {
        var player = new MockSoundPlayer();
        var coordinator = new SessionSoundCoordinator(player);
        var session = TestSessions.Running();
        var outcome = SessionOutcome.Conflict(session);

        coordinator.HandleSessionStopped(outcome);

        Assert.Equal(0, player.StartTickCount);
        Assert.Equal(0, player.StopCount);
        Assert.Equal(0, player.NaturalCompletionBellCount);
    }

    [Fact]
    public void NaturalCompletion_PlaysCompletionSoundOnce()
    {
        var player = new MockSoundPlayer();
        var coordinator = new SessionSoundCoordinator(player);
        var session = TestSessions.Finished(SessionStatus.Completed);

        coordinator.HandleSessionCompleted(session);

        Assert.Equal(0, player.StartTickCount);
        Assert.Equal(0, player.StopCount);
        Assert.Equal(1, player.NaturalCompletionBellCount);
    }

    [Fact]
    public void SessionInterrupted_IsCompletelySilent()
    {
        var player = new MockSoundPlayer();
        var coordinator = new SessionSoundCoordinator(player);

        coordinator.HandleSessionInterrupted();

        Assert.Equal(0, player.StartTickCount);
        Assert.Equal(0, player.StopCount);
        Assert.Equal(0, player.NaturalCompletionBellCount);
        Assert.Equal(0, player.PreviewStartTickCount);
        Assert.Equal(0, player.PreviewCompletionBellCount);
    }

    private sealed class GatedSoundPlayerHarness : ISoundPlayer
    {
        public bool MasterEnabled { get; set; } = true;
        public bool StartEnabled { get; set; } = true;
        public bool CompletionEnabled { get; set; } = true;

        public int StartTickPlayed { get; private set; }
        public int StopPlayed { get; private set; }
        public int NaturalCompletionPlayed { get; private set; }
        public int PreviewStartTickPlayed { get; private set; }
        public int PreviewCompletionBellPlayed { get; private set; }

        public void PlayStartTick()
        {
            if (!MasterEnabled || !StartEnabled) return;
            StartTickPlayed++;
        }

        public void PlayStop()
        {
            if (!MasterEnabled || !CompletionEnabled) return;
            StopPlayed++;
        }

        public void PlayCompletionBell() => PlayStop();

        public void PlayNaturalCompletionBell()
        {
            if (!MasterEnabled || !CompletionEnabled) return;
            NaturalCompletionPlayed++;
        }

        public void PreviewStartTick()
        {
            PreviewStartTickPlayed++;
        }

        public void PreviewCompletionBell()
        {
            PreviewCompletionBellPlayed++;
        }
    }

    [Fact]
    public void Gating_MasterDisabled_MutesAutomaticPlayback()
    {
        var harness = new GatedSoundPlayerHarness { MasterEnabled = false, StartEnabled = true, CompletionEnabled = true };
        var coordinator = new SessionSoundCoordinator(harness);
        var session = TestSessions.Running();

        coordinator.HandleSessionStarted(session);
        coordinator.HandleSessionContinued(SessionOutcome.Continued(session));
        coordinator.HandleSessionStopped(SessionOutcome.Stopped(session));
        coordinator.HandleSessionCompleted(session);

        Assert.Equal(0, harness.StartTickPlayed);
        Assert.Equal(0, harness.StopPlayed);
        Assert.Equal(0, harness.NaturalCompletionPlayed);
    }

    [Fact]
    public void Gating_StartDisabled_MutesStartOnly()
    {
        var harness = new GatedSoundPlayerHarness { MasterEnabled = true, StartEnabled = false, CompletionEnabled = true };
        var coordinator = new SessionSoundCoordinator(harness);
        var session = TestSessions.Running();

        coordinator.HandleSessionStarted(session);
        coordinator.HandleSessionContinued(SessionOutcome.Continued(session));
        coordinator.HandleSessionStopped(SessionOutcome.Stopped(session));
        coordinator.HandleSessionCompleted(session);

        Assert.Equal(0, harness.StartTickPlayed);
        Assert.Equal(1, harness.StopPlayed);
        Assert.Equal(1, harness.NaturalCompletionPlayed);
    }

    [Fact]
    public void Gating_CompletionDisabled_MutesCompletionOnly()
    {
        var harness = new GatedSoundPlayerHarness { MasterEnabled = true, StartEnabled = true, CompletionEnabled = false };
        var coordinator = new SessionSoundCoordinator(harness);
        var session = TestSessions.Running();

        coordinator.HandleSessionStarted(session);
        coordinator.HandleSessionContinued(SessionOutcome.Continued(session));
        coordinator.HandleSessionStopped(SessionOutcome.Stopped(session));
        coordinator.HandleSessionCompleted(session);

        Assert.Equal(2, harness.StartTickPlayed); // 1 on Start + 1 on Continue
        Assert.Equal(0, harness.StopPlayed);
        Assert.Equal(0, harness.NaturalCompletionPlayed);
    }

    [Fact]
    public void Preview_PlaysOnce_RegardlessOfMasterAndChildGates()
    {
        var harness = new GatedSoundPlayerHarness { MasterEnabled = false, StartEnabled = false, CompletionEnabled = false };

        harness.PreviewStartTick();
        harness.PreviewCompletionBell();

        Assert.Equal(1, harness.PreviewStartTickPlayed);
        Assert.Equal(1, harness.PreviewCompletionBellPlayed);
    }
}
