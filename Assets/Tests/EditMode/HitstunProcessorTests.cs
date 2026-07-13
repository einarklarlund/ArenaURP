using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Edit-mode tests for the pure hitstun core: the two-ended modifier fold, the seconds-to-expiry-tick conversion
/// that lets the server publish one number both peers agree on, and the extend-never-shorten stacking rule.
/// </summary>
[TestFixture]
public class HitstunProcessorTests
{
    const float TickDelta = 0.02f;   // 50Hz, FishNet's default

    static HitstunModifier Mult(float m) => new() { DurationMultiplier = m };

    // ---- fold ---------------------------------------------------------------------------------

    [Test]
    public void FoldMultiplier_NullOrEmptyList_IsIdentity()
    {
        Assert.AreEqual(1f, HitstunProcessor.FoldMultiplier(null), 1e-5f);
        Assert.AreEqual(1f, HitstunProcessor.FoldMultiplier(new List<HitstunModifier>()), 1e-5f);
    }

    [Test]
    public void FoldMultiplier_StacksMultiplicatively()
    {
        var mods = new List<HitstunModifier> { Mult(0.5f), Mult(4f) };
        Assert.AreEqual(2f, HitstunProcessor.FoldMultiplier(mods), 1e-5f);
    }

    [Test]
    public void ResolveDuration_DealerModifiers_ScaleOutgoingSeconds()
    {
        var config = new HitstunConfig { Seconds = 0.4f };

        Assert.AreEqual(0.4f, HitstunProcessor.ResolveDuration(in config, null), 1e-5f);
        Assert.AreEqual(0.8f, HitstunProcessor.ResolveDuration(in config, new List<HitstunModifier> { Mult(2f) }), 1e-5f);
    }

    [Test]
    public void ResolveDuration_DoesNotMutateAuthoredConfig()
    {
        var config = new HitstunConfig { Seconds = 0.4f };
        HitstunProcessor.ResolveDuration(in config, new List<HitstunModifier> { Mult(3f) });

        Assert.AreEqual(0.4f, config.Seconds, 1e-5f,
            "Folding a modifier must never write back into the authored baseline.");
    }

    [Test]
    public void ResolveIncoming_ReceiverModifiers_ScaleAcceptedSeconds()
    {
        Assert.AreEqual(0.1f, HitstunProcessor.ResolveIncoming(0.4f, new List<HitstunModifier> { Mult(0.25f) }), 1e-5f);
    }

    [Test]
    public void ResolveIncoming_ZeroMultiplier_MakesReceiverStunImmune()
    {
        Assert.AreEqual(0f, HitstunProcessor.ResolveIncoming(10f, new List<HitstunModifier> { Mult(0f) }), 1e-5f);
    }

    [Test]
    public void Resolve_NegativeResult_FloorsAtZero()
    {
        Assert.AreEqual(0f, HitstunProcessor.ResolveIncoming(0.4f, new List<HitstunModifier> { Mult(-2f) }), 1e-5f,
            "A negative multiplier must not produce a negative stun (which would read as 'stunned forever' upstream).");
    }

    // ---- seconds -> expiry tick ---------------------------------------------------------------

    [Test]
    public void EndTick_RoundsUp_SoAnyPositiveStunCostsAtLeastOneTick()
    {
        // 0.001s at a 0.02s tick is a fraction of a tick; it must NOT round away to "not stunned".
        uint end = HitstunProcessor.EndTick(100u, 0.001f, TickDelta);
        Assert.AreEqual(101u, end);
        Assert.IsTrue(HitstunProcessor.IsStunned(100u, end), "A sub-tick stun still stuns for one tick.");
    }

    [Test]
    public void EndTick_ConvertsSecondsToTicks()
    {
        Assert.AreEqual(110u, HitstunProcessor.EndTick(100u, 0.2f, TickDelta),
            "0.2s at a 0.02s tick is 10 ticks.");
    }

    [Test]
    public void EndTick_NonPositiveDuration_IsAlreadyExpired()
    {
        Assert.AreEqual(100u, HitstunProcessor.EndTick(100u, 0f, TickDelta));
        Assert.AreEqual(100u, HitstunProcessor.EndTick(100u, -1f, TickDelta));
        Assert.IsFalse(HitstunProcessor.IsStunned(100u, HitstunProcessor.EndTick(100u, 0f, TickDelta)));
    }

    [Test]
    public void EndTick_InvalidTickDelta_IsAlreadyExpired()
    {
        Assert.AreEqual(100u, HitstunProcessor.EndTick(100u, 0.5f, 0f),
            "A zero tick delta must not divide by zero or stun forever.");
    }

    // ---- stun window --------------------------------------------------------------------------

    [Test]
    public void IsStunned_TrueBeforeExpiry_FalseOnAndAfterIt()
    {
        Assert.IsTrue(HitstunProcessor.IsStunned(109u, 110u));
        Assert.IsFalse(HitstunProcessor.IsStunned(110u, 110u), "The expiry tick itself is no longer stunned.");
        Assert.IsFalse(HitstunProcessor.IsStunned(111u, 110u));
    }

    [Test]
    public void IsStunned_NeverStunned_IsFalse()
    {
        Assert.IsFalse(HitstunProcessor.IsStunned(0u, 0u), "A zero expiry (never stunned) must read as not stunned.");
    }

    // ---- stacking -----------------------------------------------------------------------------

    [Test]
    public void ExtendTo_LaterStunWins()
    {
        Assert.AreEqual(120u, HitstunProcessor.ExtendTo(110u, 120u), "A longer stun extends the window.");
    }

    [Test]
    public void ExtendTo_WeakLateHit_CannotShortenAnExistingStun()
    {
        Assert.AreEqual(120u, HitstunProcessor.ExtendTo(120u, 115u),
            "A weaker stun landing during a stronger one must not rescue the victim early.");
    }
}
