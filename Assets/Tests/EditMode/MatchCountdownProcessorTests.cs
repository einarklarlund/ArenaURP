using NUnit.Framework;

/// <summary>
/// Edit-mode unit tests for <see cref="MatchCountdownProcessor"/>.
/// No GameObjects, no FishNet, no networking - pure struct in, struct out.
/// </summary>
[TestFixture]
public class MatchCountdownProcessorTests
{
    private MatchCountdownConfig _config;

    [SetUp]
    public void SetUp()
    {
        _config = new MatchCountdownConfig { DurationSeconds = 3 };
    }

    // =====================================================================
    // Decrement
    // =====================================================================

    [Test]
    public void Process_DecrementsRemainingSecondsByDeltaTime()
    {
        var state = MatchCountdownState.Begin(_config.DurationSeconds);
        var input = MatchCountdownInput.Default;

        state = MatchCountdownProcessor.Process(state, in _config, in input, 0.5f);

        Assert.AreEqual(_config.DurationSeconds - 0.5f, state.RemainingSeconds, 0.0001f);
        Assert.IsTrue(state.IsActive);
        Assert.IsFalse(state.TransitionFired);
    }

    [Test]
    public void Process_AccumulatesAcrossMultipleTicks()
    {
        var state = MatchCountdownState.Begin(_config.DurationSeconds);
        var input = MatchCountdownInput.Default;

        // 10 * 0.1s = 1s consumed.
        for (int i = 0; i < 10; i++)
        {
            state = MatchCountdownProcessor.Process(state, in _config, in input, 0.1f);
        }

        Assert.AreEqual(_config.DurationSeconds - 1f, state.RemainingSeconds, 0.0001f);
        Assert.IsTrue(state.IsActive);
        Assert.IsFalse(state.TransitionFired);
    }

    // =====================================================================
    // DisplaySeconds projection (matches the original "tick once per second" coroutine)
    // =====================================================================

    [Test]
    public void Process_DisplaySeconds_MatchesCeilingOfRemaining_WhileActive()
    {
        var state = MatchCountdownState.Begin(_config.DurationSeconds); // Display=3
        var input = MatchCountdownInput.Default;

        Assert.AreEqual(3, state.DisplaySeconds);

        // 0.5s in - 2.5 remain, ceil=3
        state = MatchCountdownProcessor.Process(state, in _config, in input, 0.5f);
        Assert.AreEqual(3, state.DisplaySeconds);

        // 1.5s in total - 1.5 remain, ceil=2
        state = MatchCountdownProcessor.Process(state, in _config, in input, 1f);
        Assert.AreEqual(2, state.DisplaySeconds);

        // 2.5s in total - 0.5 remain, ceil=1
        state = MatchCountdownProcessor.Process(state, in _config, in input, 1f);
        Assert.AreEqual(1, state.DisplaySeconds);
    }

    // =====================================================================
    // Clamping
    // =====================================================================

    [Test]
    public void Process_ClampsRemainingSecondsAtZero_DoesNotGoNegative()
    {
        var state = MatchCountdownState.Begin(_config.DurationSeconds);
        var input = MatchCountdownInput.Default;

        // Big delta that would otherwise drive RemainingSeconds well below zero.
        state = MatchCountdownProcessor.Process(state, in _config, in input, _config.DurationSeconds * 100f);

        Assert.AreEqual(0f, state.RemainingSeconds,
            "RemainingSeconds should clamp at zero and never go negative.");
        Assert.AreEqual(0, state.DisplaySeconds);
        Assert.IsFalse(state.IsActive);
    }

    // =====================================================================
    // Transition signal (edge event, fires exactly once)
    // =====================================================================

    [Test]
    public void Process_FiresTransitionExactlyOnce_WhenCrossingZero()
    {
        var state = MatchCountdownState.Begin(_config.DurationSeconds);
        var input = MatchCountdownInput.Default;

        // Walk the countdown down to (but not across) zero.
        state = MatchCountdownProcessor.Process(state, in _config, in input, _config.DurationSeconds - 0.5f);
        Assert.IsFalse(state.TransitionFired, "Transition must not fire while countdown is still active.");
        Assert.IsTrue(state.IsActive);

        // Cross zero this tick - transition must fire.
        state = MatchCountdownProcessor.Process(state, in _config, in input, 0.5f);
        Assert.IsTrue(state.TransitionFired,
            "TransitionFired must be set on the exact tick the countdown reaches zero.");
        Assert.IsFalse(state.IsActive);

        // Subsequent ticks must clear the edge signal and never re-raise it.
        for (int i = 0; i < 50; i++)
        {
            state = MatchCountdownProcessor.Process(state, in _config, in input, 1f / 60f);
            Assert.IsFalse(state.TransitionFired,
                "TransitionFired is a one-shot edge signal - it must clear on the next tick.");
        }
    }

    [Test]
    public void Process_TransitionFires_OnSingleTickThatBlowsPastZero()
    {
        var state = MatchCountdownState.Begin(_config.DurationSeconds);
        var input = MatchCountdownInput.Default;

        state = MatchCountdownProcessor.Process(state, in _config, in input, _config.DurationSeconds + 999f);

        Assert.IsTrue(state.TransitionFired);
        Assert.IsFalse(state.IsActive);
        Assert.AreEqual(0f, state.RemainingSeconds);
        Assert.AreEqual(0, state.DisplaySeconds);
    }

    // =====================================================================
    // Idempotency after IsActive=false
    // =====================================================================

    [Test]
    public void Process_IsIdempotent_AfterIsActiveFalse()
    {
        var state = MatchCountdownState.Default; // IsActive = false
        var input = MatchCountdownInput.Default;

        var snapshot = state;

        for (int i = 0; i < 100; i++)
        {
            state = MatchCountdownProcessor.Process(state, in _config, in input, 1f / 60f);
        }

        Assert.AreEqual(snapshot.IsActive, state.IsActive);
        Assert.AreEqual(snapshot.RemainingSeconds, state.RemainingSeconds);
        Assert.AreEqual(snapshot.DisplaySeconds, state.DisplaySeconds);
        Assert.IsFalse(state.TransitionFired,
            "An inactive countdown must never raise a transition signal.");
    }

    [Test]
    public void Process_IsIdempotent_AfterCountdownFinishes()
    {
        var state = MatchCountdownState.Begin(_config.DurationSeconds);
        var input = MatchCountdownInput.Default;

        // Drive the countdown to completion.
        state = MatchCountdownProcessor.Process(state, in _config, in input, _config.DurationSeconds);
        Assert.IsTrue(state.TransitionFired);
        Assert.IsFalse(state.IsActive);

        // Step one tick forward to clear the edge signal, then snapshot.
        state = MatchCountdownProcessor.Process(state, in _config, in input, 1f / 60f);
        var snapshot = state;

        // Continue ticking - no mutation allowed.
        for (int i = 0; i < 100; i++)
        {
            state = MatchCountdownProcessor.Process(state, in _config, in input, 1f / 60f);
        }

        Assert.AreEqual(snapshot.IsActive, state.IsActive);
        Assert.AreEqual(snapshot.RemainingSeconds, state.RemainingSeconds);
        Assert.AreEqual(snapshot.DisplaySeconds, state.DisplaySeconds);
        Assert.AreEqual(snapshot.TransitionFired, state.TransitionFired);
    }
}
