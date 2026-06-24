using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Edit-mode unit tests for <see cref="PawnColorProcessor"/>.
/// No GameObjects, no FishNet, no networking - pure struct in, struct out.
/// </summary>
[TestFixture]
public class PawnColorProcessorTests
{
    private PawnColorConfig _config;
    private static readonly Color White = Color.white;

    [SetUp]
    public void SetUp()
    {
        _config = new PawnColorConfig
        {
            HitColor = Color.red,
            HitDurationSeconds = 0.3f,
        };
    }

    private static void AssertColorApprox(Color expected, Color actual, float tol = 0.001f, string msg = "")
    {
        Assert.AreEqual(expected.r, actual.r, tol, $"r {msg}");
        Assert.AreEqual(expected.g, actual.g, tol, $"g {msg}");
        Assert.AreEqual(expected.b, actual.b, tol, $"b {msg}");
        Assert.AreEqual(expected.a, actual.a, tol, $"a {msg}");
    }

    // =====================================================================
    // Trigger behavior
    // =====================================================================

    [Test]
    public void Process_HitTrigger_StartsTargetTimelineAtZero()
    {
        var state = PawnColorState.Default;
        var input = PawnColorInput.Hit(_config.HitColor, _config.HitDurationSeconds);

        state = PawnColorProcessor.Process(state, in _config, in input, 1f / 60f);

        Assert.IsTrue(state.IsActive);
        Assert.AreEqual(0f, state.TargetElapsedSeconds, 0.0001f,
            "Newly triggered timeline starts at t=0 — dt is NOT applied on the trigger tick.");
        Assert.AreEqual(_config.HitDurationSeconds, state.TargetDurationSeconds, 0.0001f);
        AssertColorApprox(_config.HitColor, state.TargetColor);
    }

    [Test]
    public void Resolve_AtTriggerInstant_ReturnsHitColor()
    {
        var state = PawnColorState.Default;
        var input = PawnColorInput.Hit(_config.HitColor, _config.HitDurationSeconds);

        state = PawnColorProcessor.Process(state, in _config, in input, 1f / 60f);
        Color resolved = PawnColorProcessor.Resolve(in state, White);

        AssertColorApprox(_config.HitColor, resolved, 0.001f,
            "At t=0 the resolved color should equal the flash color, not the default.");
    }

    // =====================================================================
    // Lerp back to default
    // =====================================================================

    [Test]
    public void Resolve_HalfwayThroughDuration_IsHalfwayBetweenHitAndDefault()
    {
        var state = PawnColorState.Default;
        var trigger = PawnColorInput.Hit(_config.HitColor, _config.HitDurationSeconds);

        // Trigger tick (no time advance).
        state = PawnColorProcessor.Process(state, in _config, in trigger, 0f);
        // Advance to halfway.
        var idle = PawnColorInput.Default;
        state = PawnColorProcessor.Process(state, in _config, in idle, _config.HitDurationSeconds * 0.5f);

        Color resolved = PawnColorProcessor.Resolve(in state, White);
        Color expected = Color.Lerp(_config.HitColor, White, 0.5f);
        AssertColorApprox(expected, resolved);
    }

    [Test]
    public void Resolve_AfterDurationExpires_ReturnsDefault()
    {
        var state = PawnColorState.Default;
        var trigger = PawnColorInput.Hit(_config.HitColor, _config.HitDurationSeconds);
        state = PawnColorProcessor.Process(state, in _config, in trigger, 0f);

        var idle = PawnColorInput.Default;
        state = PawnColorProcessor.Process(state, in _config, in idle, _config.HitDurationSeconds + 0.1f);

        Assert.IsFalse(state.IsActive,
            "Once both timelines expire IsActive should be false so callers can skip Resolve.");
        Color resolved = PawnColorProcessor.Resolve(in state, White);
        AssertColorApprox(White, resolved);
    }

    // =====================================================================
    // Repeated triggers
    // =====================================================================

    [Test]
    public void Process_SecondHitMidTimeline_ResetsElapsedToZero()
    {
        var state = PawnColorState.Default;
        var trigger = PawnColorInput.Hit(_config.HitColor, _config.HitDurationSeconds);
        state = PawnColorProcessor.Process(state, in _config, in trigger, 0f);

        // Advance partway.
        var idle = PawnColorInput.Default;
        state = PawnColorProcessor.Process(state, in _config, in idle, _config.HitDurationSeconds * 0.7f);
        Assert.Greater(state.TargetElapsedSeconds, 0f);

        // Second hit lands mid-flash — should reset the timeline.
        state = PawnColorProcessor.Process(state, in _config, in trigger, 1f / 60f);

        Assert.AreEqual(0f, state.TargetElapsedSeconds, 0.0001f,
            "A second hit must restart the flash from t=0 (matches the original SetColor stamping Time.time).");
        Assert.AreEqual(_config.HitDurationSeconds, state.TargetDurationSeconds, 0.0001f);
        Assert.IsTrue(state.IsActive);
    }

    [Test]
    public void Process_SecondHitWithLongerDuration_ExtendsTimeline()
    {
        var state = PawnColorState.Default;
        var shortTrigger = PawnColorInput.Hit(_config.HitColor, _config.HitDurationSeconds);
        state = PawnColorProcessor.Process(state, in _config, in shortTrigger, 0f);

        // Restart with a longer duration.
        var longTrigger = PawnColorInput.Hit(Color.blue, _config.HitDurationSeconds * 3f);
        state = PawnColorProcessor.Process(state, in _config, in longTrigger, 1f / 60f);

        Assert.AreEqual(_config.HitDurationSeconds * 3f, state.TargetDurationSeconds, 0.0001f);
        Assert.AreEqual(0f, state.TargetElapsedSeconds, 0.0001f);
        AssertColorApprox(Color.blue, state.TargetColor);
    }

    // =====================================================================
    // Idempotency after expiry
    // =====================================================================

    [Test]
    public void Process_IsIdempotent_AfterTimelineExpires()
    {
        var state = PawnColorState.Default;
        var trigger = PawnColorInput.Hit(_config.HitColor, _config.HitDurationSeconds);
        state = PawnColorProcessor.Process(state, in _config, in trigger, 0f);

        var idle = PawnColorInput.Default;
        state = PawnColorProcessor.Process(state, in _config, in idle, _config.HitDurationSeconds + 1f);
        Assert.IsFalse(state.IsActive);

        var snapshot = state;
        for (int i = 0; i < 100; i++)
        {
            state = PawnColorProcessor.Process(state, in _config, in idle, 1f / 60f);
        }

        Assert.AreEqual(snapshot.TargetElapsedSeconds, state.TargetElapsedSeconds, 0.0001f);
        Assert.AreEqual(snapshot.TargetDurationSeconds, state.TargetDurationSeconds, 0.0001f);
        Assert.AreEqual(snapshot.IsActive, state.IsActive);
        AssertColorApprox(PawnColorProcessor.Resolve(in snapshot, White),
            PawnColorProcessor.Resolve(in state, White));
    }

    [Test]
    public void Process_DefaultState_NeverBecomesActive_WithoutTrigger()
    {
        var state = PawnColorState.Default;
        var idle = PawnColorInput.Default;

        for (int i = 0; i < 60; i++)
        {
            state = PawnColorProcessor.Process(state, in _config, in idle, 1f / 60f);
        }

        Assert.IsFalse(state.IsActive,
            "With no trigger input the default state must stay inactive forever.");
    }

    // =====================================================================
    // dt edge cases
    // =====================================================================

    [Test]
    public void Process_ZeroDeltaTime_DoesNotProgressTimeline()
    {
        var state = PawnColorState.Default;
        var trigger = PawnColorInput.Hit(_config.HitColor, _config.HitDurationSeconds);
        state = PawnColorProcessor.Process(state, in _config, in trigger, 0f);
        float before = state.TargetElapsedSeconds;

        var idle = PawnColorInput.Default;
        state = PawnColorProcessor.Process(state, in _config, in idle, 0f);

        Assert.AreEqual(before, state.TargetElapsedSeconds, 0.0001f);
    }

    [Test]
    public void Process_NegativeDeltaTime_IsClampedToZero()
    {
        var state = PawnColorState.Default;
        var trigger = PawnColorInput.Hit(_config.HitColor, _config.HitDurationSeconds);
        state = PawnColorProcessor.Process(state, in _config, in trigger, 0f);

        var idle = PawnColorInput.Default;
        state = PawnColorProcessor.Process(state, in _config, in idle, 0.1f);
        float afterPositive = state.TargetElapsedSeconds;

        state = PawnColorProcessor.Process(state, in _config, in idle, -100f);
        Assert.AreEqual(afterPositive, state.TargetElapsedSeconds, 0.0001f,
            "Negative dt must not rewind the timeline.");
    }

    // =====================================================================
    // Base + target layering
    // =====================================================================

    [Test]
    public void Process_TargetAndBaseTimelinesAreIndependent()
    {
        var state = PawnColorState.Default;
        var input = new PawnColorInput
        {
            TriggerTarget = true,
            TargetColor = Color.red,
            TargetDurationSeconds = 0.2f,
            TriggerBase = true,
            BaseColor = Color.green,
            BaseDurationSeconds = 1.0f,
        };

        state = PawnColorProcessor.Process(state, in _config, in input, 0f);
        Assert.IsTrue(state.IsActive);
        Assert.AreEqual(0.2f, state.TargetDurationSeconds, 0.0001f);
        Assert.AreEqual(1.0f, state.BaseDurationSeconds, 0.0001f);

        // Advance past the target timeline but not the base timeline.
        var idle = PawnColorInput.Default;
        state = PawnColorProcessor.Process(state, in _config, in idle, 0.25f);

        Assert.IsTrue(state.IsActive, "Base timeline should still be running.");
        Assert.GreaterOrEqual(state.TargetElapsedSeconds, state.TargetDurationSeconds,
            "Target timeline should have expired.");
        Assert.Less(state.BaseElapsedSeconds, state.BaseDurationSeconds,
            "Base timeline should still be active.");
    }
}
