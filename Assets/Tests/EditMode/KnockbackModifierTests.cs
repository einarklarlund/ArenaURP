using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Edit-mode tests for the knockback modifier fold — the "modifiers on both ends" seam.
///
/// A knockback exchange folds twice: the DEALER scales the impulse it emits (ResolveImpulse), the RECEIVER scales
/// the impulse it accepts (ResolveIncoming). Both go through the same FoldMultiplier, so these tests pin the
/// contract both ends rely on: an absent contributor is the identity, and contributors stack multiplicatively.
/// </summary>
[TestFixture]
public class KnockbackModifierTests
{
    static KnockbackModifier Mult(float m) => new() { ImpulseMultiplier = m };

    [Test]
    public void FoldMultiplier_NullOrEmptyList_IsIdentity()
    {
        Assert.AreEqual(1f, KnockbackHelper.FoldMultiplier(null), 1e-5f,
            "A null modifier list must fold to the identity, not zero.");
        Assert.AreEqual(1f, KnockbackHelper.FoldMultiplier(new List<KnockbackModifier>()), 1e-5f,
            "An empty modifier list must fold to the identity — an absent contributor changes nothing.");
    }

    [Test]
    public void FoldMultiplier_IdentityModifier_ChangesNothing()
    {
        var mods = new List<KnockbackModifier> { KnockbackModifier.Identity };
        Assert.AreEqual(1f, KnockbackHelper.FoldMultiplier(mods), 1e-5f);
    }

    [Test]
    public void FoldMultiplier_StacksMultiplicatively()
    {
        var mods = new List<KnockbackModifier> { Mult(0.5f), Mult(2f), Mult(3f) };
        Assert.AreEqual(3f, KnockbackHelper.FoldMultiplier(mods), 1e-5f,
            "Modifiers stack multiplicatively: 0.5 * 2 * 3 == 3.");
    }

    [Test]
    public void ResolveImpulse_DealerModifiers_ScaleTheEmittedImpulse()
    {
        var config = KnockbackConfig.Default;
        Vector3 unbuffed = KnockbackHelper.ResolveImpulse(Vector3.forward, in config, null);
        Vector3 doubled = KnockbackHelper.ResolveImpulse(Vector3.forward, in config,
            new List<KnockbackModifier> { Mult(2f) });

        Assert.AreEqual(unbuffed.magnitude * 2f, doubled.magnitude, 1e-3f,
            "A dealer's outgoing modifier scales the impulse magnitude.");
        Assert.AreEqual(0f, Vector3.Angle(unbuffed, doubled), 1e-2f,
            "Scaling the impulse must not rotate it.");
    }

    [Test]
    public void ResolveImpulse_DoesNotMutateAuthoredConfig()
    {
        var config = KnockbackConfig.Default;
        float authoredSpeed = config.SpeedOnHit;

        KnockbackHelper.ResolveImpulse(Vector3.forward, in config, new List<KnockbackModifier> { Mult(5f) });

        Assert.AreEqual(authoredSpeed, config.SpeedOnHit, 1e-5f,
            "Folding a modifier must never write back into the authored baseline (pooling-unsafe).");
    }

    [Test]
    public void ResolveIncoming_ReceiverModifiers_ScaleTheAcceptedImpulse()
    {
        Vector3 dealt = new Vector3(0f, 4f, 6f);
        Vector3 halved = KnockbackHelper.ResolveIncoming(dealt, new List<KnockbackModifier> { Mult(0.5f) });

        Assert.AreEqual(dealt.magnitude * 0.5f, halved.magnitude, 1e-3f,
            "A receiver's resistance modifier scales the impulse it accepts.");
    }

    [Test]
    public void ResolveIncoming_ZeroMultiplier_AbsorbsTheHitEntirely()
    {
        Vector3 absorbed = KnockbackHelper.ResolveIncoming(new Vector3(0f, 4f, 6f),
            new List<KnockbackModifier> { Mult(0f) });

        Assert.AreEqual(0f, absorbed.magnitude, 1e-5f,
            "A 0x resistance modifier makes the receiver knockback-immune.");
    }

    [Test]
    public void BothEnds_Compose()
    {
        var config = KnockbackConfig.Default;
        var dealerMods = new List<KnockbackModifier> { Mult(2f) };
        var receiverMods = new List<KnockbackModifier> { Mult(0.25f) };

        Vector3 baseline = KnockbackHelper.ResolveImpulse(Vector3.forward, in config, null);
        Vector3 dealt = KnockbackHelper.ResolveImpulse(Vector3.forward, in config, dealerMods);
        Vector3 landed = KnockbackHelper.ResolveIncoming(dealt, receiverMods);

        Assert.AreEqual(baseline.magnitude * 0.5f, landed.magnitude, 1e-3f,
            "Dealer 2x then receiver 0.25x composes to 0.5x of the unmodified impulse.");
    }
}
