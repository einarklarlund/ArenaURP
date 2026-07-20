using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Atomicity pin (ADR 0005): <c>HitMessage.Dispatch</c> spends the source's durability FIRST, then runs every
    /// handler unconditionally — so a single hit that DESPAWNS the bullet (durability-1) still delivers damage AND
    /// hitstun AND knockback. If durability were spent after the handler loop, or a handler gated on the projectile
    /// being alive, a dead bullet would drop some of the three; this asserts the conjunction on the very hits that
    /// kill the projectile. Each effect has its own positive control in the sibling combat fixtures (same setup);
    /// the point HERE is that all three co-occur while the bullets that carried them are despawning on contact.
    /// </summary>
    public sealed class HitAtomicityTests : CombatPlayModeTestFixture
    {
        // Chunky so the victim survives the burst and we observe every effect while it is still alive.
        const int StartHealth = 400;

        [UnityTest]
        public IEnumerator A_hit_lands_damage_stun_and_knockback_while_the_bullet_despawns()
        {
            var pair = SpawnFacingPair(8f, health: StartHealth);
            var shooter = pair[0];
            var target = pair[1];
            yield return new WaitForSeconds(0.3f);   // let both settle onto the floor

            Assert.IsFalse(IsStunned(target), "precondition: an unhit pawn is not stunned");
            float startZ = Position(target).z;

            AimAt(shooter, Position(target));
            SetFiring(shooter, true);

            // Damage is the earliest-observable effect; then give the impulse a frame or two to become displacement.
            yield return WaitUntilHealthBelow(target, StartHealth, timeoutSeconds: 3f);
            yield return WaitUntil(
                () => IsStunned(target) && Position(target).z > startZ + 0.1f, timeoutSeconds: 2f);
            SetFiring(shooter, false);

            // All three effects of the hit exchange landed on the target...
            Assert.Less(Health(target), StartHealth, "damage handler ran");
            Assert.IsTrue(IsStunned(target), "hitstun handler ran on the same hit");
            Assert.Greater(Position(target).z, startZ + 0.1f, "knockback handler ran on the same hit (+Z)");

            // ...and the durability-1 bullets that carried them are gone. Durability was spent, yet every handler
            // still fired — the hit sequence is atomic, not gated on the projectile surviving.
            yield return WaitUntil(() => CountSpawnedWith("NetworkedMotion") == 0, timeoutSeconds: 4f);
            Assert.AreEqual(0, CountSpawnedWith("NetworkedMotion"),
                "the bullets despawned on their hits — handlers ran atomically on projectiles their own durability killed");
        }
    }
}
