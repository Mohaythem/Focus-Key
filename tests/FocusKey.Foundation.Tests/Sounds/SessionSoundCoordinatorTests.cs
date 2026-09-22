using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Sounds;
using FocusKey.Foundation.Tests.Sessions;

namespace FocusKey.Foundation.Tests.Sounds;

public sealed class SessionSoundCoordinatorTests
{
    private sealed class MockSoundPlayer : ISoundPlayer
    {
        public int StartTickCount { get; private set; }
        public int SessionActionCount { get; private set; }
        public int PauseCount { get; private set; }
        public int ContinueCount { get; private set; }
        public int StopCount { get; private set; }
        public int NaturalCompletionBellCount { get; private set; }
        public int PreviewStartTickCount { get; private set; }
        public int PreviewCompletionBellCount { get; private set; }
        public int PreviewSessionActionCount { get; private set; }

        public void PlayStartTick() => StartTickCount++;
        public void PlaySessionAction() => SessionActionCount++;
        public void PlayPause() { PauseCount++; PlaySessionAction(); }
        public void PlayContinue() { ContinueCount++; PlaySessionAction(); }
        public void PlayStop() { StopCount++; PlaySessionAction(); }
        public void PlayCompletionBell() => PlayStop();
        public void PlayNaturalCompletionBell() => NaturalCompletionBellCount++;
        public void PreviewStartTick() => PreviewStartTickCount++;
        public void PreviewCompletionBell() => PreviewCompletionBellCount++;
        public void PreviewSessionAction() => PreviewSessionActionCount++;
    }

    [Fact]
    public void SessionStart_PlaysStartSoundOnce()
    {
        var player = new MockSoundPlayer();
        var coordinator = new SessionSoundCoordinator(player);
        var session = TestSessions.Running();

        coordinator.HandleSessionStarted(session);

        Assert.Equal(1, player.StartTickCount);
        Assert.Equal(0, player.SessionActionCount);
        Assert.Equal(0, player.NaturalCompletionBellCount);
    }

    [Fact]
    public void SessionPause_PlaysSessionActionSoundOnce()
    {
        var player = new MockSoundPlayer();
        var coordinator = new SessionSoundCoordinator(player);
        var session = TestSessions.Running();
        var outcome = SessionOutcome.Paused(session);

        coordinator.HandleSessionPaused(outcome);

        Assert.Equal(0, player.StartTickCount);
        Assert.Equal(1, player.PauseCount);
        Assert.Equal(1, player.SessionActionCount);
        Assert.Equal(0, player.NaturalCompletionBellCount);
    }

    [Fact]
    public void SessionPause_ConflictOutcome_DoesNotPlaySound()
    {
        var player = new MockSoundPlayer();
        var coordinator = new SessionSoundCoordinator(player);
        var session = TestSessions.Running();
        var outcome = SessionOutcome.Conflict(session);

        coordinator.HandleSessionPaused(outcome);

        Assert.Equal(0, player.StartTickCount);
        Assert.Equal(0, player.PauseCount);
        Assert.Equal(0, player.SessionActionCount);
        Assert.Equal(0, player.NaturalCompletionBellCount);
    }

    [Fact]
    public void SessionContinue_AfterPause_PlaysSessionActionSoundOnce()
    {
        var player = new MockSoundPlayer();
        var coordinator = new SessionSoundCoordinator(player);
        var session = TestSessions.Running();
        var outcome = SessionOutcome.Continued(session);

        coordinator.HandleSessionContinued(outcome);

        Assert.Equal(0, player.StartTickCount);
        Assert.Equal(1, player.ContinueCount);
        Assert.Equal(1, player.SessionActionCount);
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
        Assert.Equal(0, player.ContinueCount);
        Assert.Equal(0, player.SessionActionCount);
        Assert.Equal(0, player.NaturalCompletionBellCount);
    }

    [Fact]
    public void SessionStop_PlaysSessionActionSoundOnce()
    {
        var player = new MockSoundPlayer();
        var coordinator = new SessionSoundCoordinator(player);
        var session = TestSessions.Finished(SessionStatus.Stopped);
        var outcome = SessionOutcome.Stopped(session);

        coordinator.HandleSessionStopped(outcome);

        Assert.Equal(0, player.StartTickCount);
        Assert.Equal(1, player.StopCount);
        Assert.Equal(1, player.SessionActionCount);
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
        Assert.Equal(0, player.SessionActionCount);
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
        Assert.Equal(0, player.SessionActionCount);
        Assert.Equal(1, player.NaturalCompletionBellCount);
    }

    [Fact]
    public void SessionInterrupted_IsCompletelySilent()
    {
        var player = new MockSoundPlayer();
        var coordinator = new SessionSoundCoordinator(player);

        coordinator.HandleSessionInterrupted();

        Assert.Equal(0, player.StartTickCount);
        Assert.Equal(0, player.SessionActionCount);
        Assert.Equal(0, player.NaturalCompletionBellCount);
        Assert.Equal(0, player.PreviewStartTickCount);
        Assert.Equal(0, player.PreviewCompletionBellCount);
        Assert.Equal(0, player.PreviewSessionActionCount);
    }

    private sealed class GatedSoundPlayerHarness : ISoundPlayer
    {
        public bool MasterEnabled { get; set; } = true;
        public bool StartEnabled { get; set; } = true;
        public bool CompletionEnabled { get; set; } = true;

        public int StartTickPlayed { get; private set; }
        public int SessionActionPlayed { get; private set; }
        public int PausePlayed { get; private set; }
        public int ContinuePlayed { get; private set; }
        public int StopPlayed { get; private set; }
        public int NaturalCompletionPlayed { get; private set; }
        public int PreviewStartTickPlayed { get; private set; }
        public int PreviewCompletionBellPlayed { get; private set; }
        public int PreviewSessionActionPlayed { get; private set; }

        public void PlayStartTick()
        {
            if (!MasterEnabled || !StartEnabled) return;
            StartTickPlayed++;
        }

        public void PlaySessionAction()
        {
            if (!MasterEnabled || !StartEnabled) return;
            SessionActionPlayed++;
        }

        public void PlayPause()
        {
            if (!MasterEnabled || !StartEnabled) return;
            PausePlayed++;
            PlaySessionAction();
        }

        public void PlayContinue()
        {
            if (!MasterEnabled || !StartEnabled) return;
            ContinuePlayed++;
            PlaySessionAction();
        }

        public void PlayStop()
        {
            if (!MasterEnabled || !StartEnabled) return;
            StopPlayed++;
            PlaySessionAction();
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

        public void PreviewSessionAction()
        {
            PreviewSessionActionPlayed++;
        }
    }

    [Fact]
    public void Gating_MasterDisabled_MutesAutomaticPlayback()
    {
        var harness = new GatedSoundPlayerHarness { MasterEnabled = false, StartEnabled = true, CompletionEnabled = true };
        var coordinator = new SessionSoundCoordinator(harness);
        var session = TestSessions.Running();

        coordinator.HandleSessionStarted(session);
        coordinator.HandleSessionPaused(SessionOutcome.Paused(session));
        coordinator.HandleSessionContinued(SessionOutcome.Continued(session));
        coordinator.HandleSessionStopped(SessionOutcome.Stopped(session));
        coordinator.HandleSessionCompleted(session);

        Assert.Equal(0, harness.StartTickPlayed);
        Assert.Equal(0, harness.PausePlayed);
        Assert.Equal(0, harness.ContinuePlayed);
        Assert.Equal(0, harness.StopPlayed);
        Assert.Equal(0, harness.SessionActionPlayed);
        Assert.Equal(0, harness.NaturalCompletionPlayed);
    }

    [Fact]
    public void Gating_StartDisabled_MutesStartAndSessionActions()
    {
        var harness = new GatedSoundPlayerHarness { MasterEnabled = true, StartEnabled = false, CompletionEnabled = true };
        var coordinator = new SessionSoundCoordinator(harness);
        var session = TestSessions.Running();

        coordinator.HandleSessionStarted(session);
        coordinator.HandleSessionPaused(SessionOutcome.Paused(session));
        coordinator.HandleSessionContinued(SessionOutcome.Continued(session));
        coordinator.HandleSessionStopped(SessionOutcome.Stopped(session));
        coordinator.HandleSessionCompleted(session);

        Assert.Equal(0, harness.StartTickPlayed);
        Assert.Equal(0, harness.PausePlayed);
        Assert.Equal(0, harness.ContinuePlayed);
        Assert.Equal(0, harness.StopPlayed);
        Assert.Equal(0, harness.SessionActionPlayed);
        Assert.Equal(1, harness.NaturalCompletionPlayed);
    }

    [Fact]
    public void Gating_CompletionDisabled_MutesCompletionOnly()
    {
        var harness = new GatedSoundPlayerHarness { MasterEnabled = true, StartEnabled = true, CompletionEnabled = false };
        var coordinator = new SessionSoundCoordinator(harness);
        var session = TestSessions.Running();

        coordinator.HandleSessionStarted(session);
        coordinator.HandleSessionPaused(SessionOutcome.Paused(session));
        coordinator.HandleSessionContinued(SessionOutcome.Continued(session));
        coordinator.HandleSessionStopped(SessionOutcome.Stopped(session));
        coordinator.HandleSessionCompleted(session);

        Assert.Equal(1, harness.StartTickPlayed);
        Assert.Equal(1, harness.PausePlayed);
        Assert.Equal(1, harness.ContinuePlayed);
        Assert.Equal(1, harness.StopPlayed);
        Assert.Equal(3, harness.SessionActionPlayed); // Pause + Continue + Stop
        Assert.Equal(0, harness.NaturalCompletionPlayed);
    }

    [Fact]
    public void Preview_PlaysOnce_RegardlessOfMasterAndChildGates()
    {
        var harness = new GatedSoundPlayerHarness { MasterEnabled = false, StartEnabled = false, CompletionEnabled = false };

        harness.PreviewStartTick();
        harness.PreviewCompletionBell();
        harness.PreviewSessionAction();

        Assert.Equal(1, harness.PreviewStartTickPlayed);
        Assert.Equal(1, harness.PreviewCompletionBellPlayed);
        Assert.Equal(1, harness.PreviewSessionActionPlayed);
    }
}
