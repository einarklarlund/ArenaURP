using NUnit.Framework;

/// <summary>
/// Edit-mode unit tests for <see cref="MatchTimerProcessor"/>.
/// No GameObjects, no FishNet, no networking - pure struct in, struct out.
/// </summary>
[TestFixture]
public class MatchTimerProcessorTests
{
    private MatchTimerConfig _config;

    [SetUp]
    public void SetUp()
    {
        _config = new MatchTimerConfig
        {
            MatchDurationSeconds = 10f,
            KillsToWin = 5f,
        };
    }

    // =====================================================================
    // Time progression
    // =====================================================================

    [Test]
    public void Process_AdvancesElapsedSecondsByDeltaTime()
    {
        var state = MatchTimerState.Begin();
        var input = MatchTimerInput.Default;

        state = MatchTimerProcessor.Process(state, in _config, in input, 0.5f);

        Assert.AreEqual(0.5f, state.ElapsedSeconds, 0.0001f);
        Assert.IsFalse(state.IsOver);
        Assert.IsTrue(state.IsRunning);
    }

    [Test]
    public void Process_AccumulatesElapsedSecondsAcrossMultipleTicks()
    {
        var state = MatchTimerState.Begin();
        var input = MatchTimerInput.Default;

        for (int i = 0; i < 10; i++)
        {
            state = MatchTimerProcessor.Process(state, in _config, in input, 0.25f);
        }

        Assert.AreEqual(2.5f, state.ElapsedSeconds, 0.0001f);
        Assert.IsFalse(state.IsOver);
    }

    // =====================================================================
    // Timer expiry
    // =====================================================================

    [Test]
    public void Process_FlipsIsOverTrue_WhenTimerExpires()
    {
        var state = MatchTimerState.Begin();
        var input = MatchTimerInput.Default;

        // Advance one second short of the match duration; should still be running.
        state = MatchTimerProcessor.Process(state, in _config, in input, _config.MatchDurationSeconds - 1f);
        Assert.IsFalse(state.IsOver, "Should not be over before duration elapsed.");
        Assert.IsTrue(state.IsRunning);

        // Cross the threshold.
        state = MatchTimerProcessor.Process(state, in _config, in input, 1.5f);

        Assert.IsTrue(state.IsOver, "Match should end when ElapsedSeconds >= MatchDurationSeconds.");
        Assert.IsFalse(state.IsRunning, "IsRunning should be cleared after match ends.");
        Assert.AreEqual(MatchEndReason.TimerExpired, state.EndReason);
    }

    [Test]
    public void Process_ClampsElapsedSecondsToDuration_OnTimerExpiry()
    {
        var state = MatchTimerState.Begin();
        var input = MatchTimerInput.Default;

        // Big delta that blows past the duration.
        state = MatchTimerProcessor.Process(state, in _config, in input, _config.MatchDurationSeconds * 5f);

        Assert.IsTrue(state.IsOver);
        Assert.AreEqual(_config.MatchDurationSeconds, state.ElapsedSeconds, 0.0001f,
            "ElapsedSeconds should be clamped to MatchDurationSeconds when the timer expires.");
    }

    // =====================================================================
    // Kill threshold
    // =====================================================================

    [Test]
    public void Process_FlipsIsOverTrue_WhenKillsThresholdReached()
    {
        var state = MatchTimerState.Begin();
        var input = new MatchTimerInput { LeadingScore = _config.KillsToWin };

        state = MatchTimerProcessor.Process(state, in _config, in input, 1f / 60f);

        Assert.IsTrue(state.IsOver);
        Assert.AreEqual(MatchEndReason.KillsReached, state.EndReason);
        Assert.AreEqual(_config.KillsToWin, state.HighScore);
    }

    [Test]
    public void Process_DoesNotEnd_WhenLeadingScoreBelowThreshold()
    {
        var state = MatchTimerState.Begin();
        var input = new MatchTimerInput { LeadingScore = _config.KillsToWin - 1f };

        state = MatchTimerProcessor.Process(state, in _config, in input, 1f / 60f);

        Assert.IsFalse(state.IsOver);
        Assert.IsTrue(state.IsRunning);
    }

    [Test]
    public void Process_HighScoreOnlyIncreases()
    {
        var state = MatchTimerState.Begin();

        var hiInput = new MatchTimerInput { LeadingScore = 3f };
        state = MatchTimerProcessor.Process(state, in _config, in hiInput, 1f / 60f);
        Assert.AreEqual(3f, state.HighScore);

        // Player gets dethroned and the new leader has a lower score (e.g. someone wiped).
        // HighScore must not regress.
        var loInput = new MatchTimerInput { LeadingScore = 1f };
        state = MatchTimerProcessor.Process(state, in _config, in loInput, 1f / 60f);
        Assert.AreEqual(3f, state.HighScore, "HighScore must be monotonic.");
    }

    [Test]
    public void Process_KillsToWinZero_DisablesKillCondition()
    {
        var cfg = new MatchTimerConfig
        {
            MatchDurationSeconds = 10f,
            KillsToWin = 0f, // disabled
        };

        var state = MatchTimerState.Begin();
        var input = new MatchTimerInput { LeadingScore = 99999f };

        state = MatchTimerProcessor.Process(state, in cfg, in input, 1f / 60f);

        Assert.IsFalse(state.IsOver,
            "KillsToWin <= 0 should disable the kill-threshold win condition.");
    }

    // =====================================================================
    // Idempotency after end
    // =====================================================================

    [Test]
    public void Process_IsIdempotent_AfterIsOver()
    {
        var state = MatchTimerState.Begin();
        var input = MatchTimerInput.Default;

        // Force a timer expiry.
        state = MatchTimerProcessor.Process(state, in _config, in input, _config.MatchDurationSeconds + 1f);
        Assert.IsTrue(state.IsOver);

        var snapshot = state;

        // Subsequent ticks must not mutate state.
        for (int i = 0; i < 100; i++)
        {
            state = MatchTimerProcessor.Process(state, in _config, in input, 1f / 60f);
        }

        Assert.AreEqual(snapshot.IsOver, state.IsOver);
        Assert.AreEqual(snapshot.IsRunning, state.IsRunning);
        Assert.AreEqual(snapshot.ElapsedSeconds, state.ElapsedSeconds, 0.0001f);
        Assert.AreEqual(snapshot.HighScore, state.HighScore);
        Assert.AreEqual(snapshot.EndReason, state.EndReason);
    }

    [Test]
    public void Process_DoesNothing_WhenIsRunningFalse()
    {
        var state = MatchTimerState.Default; // IsRunning = false
        var input = MatchTimerInput.Default;

        var result = MatchTimerProcessor.Process(state, in _config, in input, 1f);

        Assert.AreEqual(0f, result.ElapsedSeconds);
        Assert.IsFalse(result.IsOver);
        Assert.IsFalse(result.IsRunning);
    }

    // =====================================================================
    // Edge cases: dt
    // =====================================================================

    [Test]
    public void Process_ZeroDeltaTime_DoesNotAdvanceTime()
    {
        var state = MatchTimerState.Begin();
        var input = MatchTimerInput.Default;

        state = MatchTimerProcessor.Process(state, in _config, in input, 0f);

        Assert.AreEqual(0f, state.ElapsedSeconds);
        Assert.IsFalse(state.IsOver);
        Assert.IsTrue(state.IsRunning);
    }

    [Test]
    public void Process_NegativeDeltaTime_IsClampedToZero()
    {
        var state = MatchTimerState.Begin();
        state.ElapsedSeconds = 5f;
        var input = MatchTimerInput.Default;

        state = MatchTimerProcessor.Process(state, in _config, in input, -100f);

        Assert.AreEqual(5f, state.ElapsedSeconds,
            "Negative dt should be clamped so time never goes backwards.");
    }

    [Test]
    public void Process_VeryLargeDeltaTime_EndsMatchOnTimer()
    {
        var state = MatchTimerState.Begin();
        var input = MatchTimerInput.Default;

        state = MatchTimerProcessor.Process(state, in _config, in input, 1e9f);

        Assert.IsTrue(state.IsOver);
        Assert.AreEqual(MatchEndReason.TimerExpired, state.EndReason);
        Assert.AreEqual(_config.MatchDurationSeconds, state.ElapsedSeconds, 0.0001f);
    }

    // =====================================================================
    // Priority: kill threshold over timer when both trigger on the same tick
    // =====================================================================

    [Test]
    public void Process_KillThreshold_TakesPriorityOverTimer_OnSameTick()
    {
        var state = MatchTimerState.Begin();
        var input = new MatchTimerInput { LeadingScore = _config.KillsToWin };

        // Delta large enough that the timer would also fire this tick.
        state = MatchTimerProcessor.Process(state, in _config, in input, _config.MatchDurationSeconds * 2f);

        Assert.IsTrue(state.IsOver);
        Assert.AreEqual(MatchEndReason.KillsReached, state.EndReason,
            "Kill threshold should be checked before timer expiry.");
    }
}
