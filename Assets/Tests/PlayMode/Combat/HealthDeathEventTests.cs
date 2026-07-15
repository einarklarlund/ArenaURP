using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Regression test for fix C: on a HOST (listen-server), one lethal hit must raise <c>OnDeath</c>
    /// EXACTLY once. Before the fix, <c>Health.TakeDamage</c> raised it twice on a host — once on the
    /// client-predictive branch (which fired because <c>IsClientInitialized</c> is true on a host) and once
    /// on the authoritative server branch — so self-persisting subscribers (e.g. the death VFX burst) ran
    /// twice. The predictive branch is now gated on <c>!IsServerInitialized</c>, so on a host only the
    /// authoritative branch fires.
    ///
    /// This test COUNTS 2 against pre-fix main (the ideal repro) and 1 after.
    /// </summary>
    public sealed class HealthDeathEventTests : CombatPlayModeTestFixture
    {
        const int ProjectileDamage = 3;   // DevilDaggers "Thing 1" deals 3 per hit — one hit is lethal at 3 HP.

        [UnityTest]
        public IEnumerator A_lethal_hit_raises_OnDeath_exactly_once_on_the_host()
        {
            // Arrange: a fragile target one hit from death, and a shooter facing it.
            var pair = SpawnFacingPair(8f, health: ProjectileDamage);
            var shooter = pair[0];
            var target = pair[1];

            yield return new WaitForSeconds(0.3f); // let both settle onto the floor before aiming

            BeginCapture(target);
            Assert.AreEqual(ProjectileDamage, Health(target), "precondition: target one hit from death");

            // Act: fire until the target is dead.
            AimAt(shooter, Position(target));
            SetFiring(shooter, true);
            yield return WaitUntil(() => Health(target) <= 0, timeoutSeconds: 3f);
            SetFiring(shooter, false);

            // Positive controls: the hit actually landed and was lethal (so the count below is meaningful, not
            // vacuously right because nothing happened).
            Assert.LessOrEqual(Health(target), 0, "the hit should have been lethal");
            Assert.GreaterOrEqual(DamageEventCount(target), 1, "the damage path should have run");

            // The fix: exactly one death event on the host, not two.
            Assert.AreEqual(1, DeathEventCount(target),
                "OnDeath must fire exactly once on a host — the client-predictive branch is gated on !IsServerInitialized");

            // Let a few more shots hit the corpse: TakeDamage early-outs once dead, so no further death events.
            SetFiring(shooter, true);
            yield return WaitFrames(10);
            SetFiring(shooter, false);
            Assert.AreEqual(1, DeathEventCount(target), "hits on an already-dead target must not re-raise OnDeath");
        }
    }
}
