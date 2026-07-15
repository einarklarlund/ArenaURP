using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Tests for fix F: PawnDamageVfx now spawns its hit/death bursts off the REPLICATED CurrentHealth SyncVar
    /// (OnChange) instead of the in-process damage events, so the bursts reach every peer — the projectile-hit
    /// path resolves server-only, so the C# events never crossed the wire and remote clients saw nothing.
    ///
    /// The cross-peer DELIVERY is the same OnChange-replication mechanism PawnColor already uses (covered by the
    /// color-flash tests); a single-host fixture can't isolate "a remote client saw it" because server- and
    /// client-side VFX are indistinguishable in one process. What this host fixture DOES pin is the GATING, which
    /// the port can easily get wrong: a ServerOnly SyncVar's OnChange fires only asServer==true on a listen-server
    /// host, so gating on asServer spawns ZERO bursts on the host (this actually happened — the test caught it).
    /// These assert one burst per hit / exactly one per death, with a positive control that the burst fires at all.
    /// </summary>
    public sealed class DamageVfxTests : CombatPlayModeTestFixture
    {
        [UnityTest]
        public IEnumerator Each_hit_spawns_exactly_one_hit_burst()
        {
            var pair = SpawnFacingPair(8f, health: 50); // high HP so the target survives the burst we fire
            var shooter = pair[0];
            var target = pair[1];
            yield return new WaitForSeconds(0.3f);

            BeginCapture(target);
            Assert.AreEqual(0, ActiveHitVfx(target), "precondition: no hit VFX before firing");

            AimAt(shooter, Position(target));
            SetFiring(shooter, true);
            yield return WaitUntilHealthBelow(target, 50, timeoutSeconds: 3f);
            SetFiring(shooter, false);
            yield return WaitFrames(2); // let the client-side OnChange callback spawn its burst

            // Positive control: at least one hit landed and at least one burst played (not vacuously right).
            int hits = DamageEventCount(target);
            Assert.GreaterOrEqual(hits, 1, "the target should have been hit");
            Assert.GreaterOrEqual(ActiveHitVfx(target), 1, "a hit should spawn a hit burst");

            // The fix: exactly one burst PER hit. A broken/absent asServer gate double-spawns on the host.
            // (VFX lifetime is 10s, far longer than this test, so none have returned to the pool yet.)
            Assert.AreEqual(hits, ActiveHitVfx(target),
                "one hit burst per hit — the asServer gate must not double-spawn on a host");
        }

        [UnityTest]
        public IEnumerator A_lethal_hit_spawns_exactly_one_death_burst()
        {
            var pair = SpawnFacingPair(8f, health: 3); // one DevilDaggers hit (3 dmg) is lethal
            var shooter = pair[0];
            var target = pair[1];
            yield return new WaitForSeconds(0.3f);

            Assert.AreEqual(0, ActiveDeathVfx(target), "precondition: no death VFX before firing");

            AimAt(shooter, Position(target));
            SetFiring(shooter, true);
            yield return WaitUntil(() => Health(target) <= 0, timeoutSeconds: 3f);
            SetFiring(shooter, false);
            yield return WaitFrames(2);

            Assert.LessOrEqual(Health(target), 0, "positive control: the hit was lethal");
            Assert.AreEqual(1, ActiveDeathVfx(target),
                "a death spawns exactly one death burst — the asServer gate must not double-spawn on a host");
        }
    }
}
