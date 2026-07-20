using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// PlayMode coverage for the knockback + hitstun exchanges, end to end through the real networked path that no
    /// EditMode test can reach (the layered model, ADR 0005):
    ///
    ///   HitMessage.Dispatch  (the atomic hit sequence; every IHitMessageHandler on the source, channel-filtered)
    ///     -> KnockbackController / HitstunController DEALER half (a layer-2 handler folds its OUTgoing modifiers)
    ///     -> the victim's KnockbackController / HitstunController RECEIVER half (ApplyKnockback / ApplyHitstun folds resistance)
    ///     -> PawnMovement.PendingImpulses / a ServerOnly SyncVar expiry tick   (the observable effect)
    ///
    /// The Bullet prefab ships a KnockbackController(SpeedOnHit=7) and a HitstunController(Seconds=0.2); carrying
    /// the controller IS what makes a hit shove/stun (presence, ADR 0002), so a plain hit exercises all of it. Both
    /// magnitudes are properties of the BULLET now, not of the victim — nothing here configures the pawn except
    /// when a test is deliberately exercising the receiving-end modifier fold.
    ///
    /// EVERY absence-assertion here (not stunned / did not move) is paired with a POSITIVE CONTROL proving the
    /// mechanism fires when unsuppressed. Without one, these tests pass against a Bullet prefab carrying neither
    /// controller — which is exactly what happened once. See the integration-testing skill, gate 5.
    ///
    /// CombatTestRig spawns pawns server-side with no owning connection, so ScriptedPawnInput.IsActive
    /// (IsServerInitialized) makes the host simulate them and the victim's KnockbackController takes its no-owner branch.
    /// </summary>
    public sealed class HitstunKnockbackTests : CombatPlayModeTestFixture
    {
        // Chunky enough that a victim survives sustained fire across a multi-phase test.
        const int StartHealth = 400;

        /// <summary>Bullet ships 0.2s of hitstun; a 10x receiver modifier stretches it to ~2s so a test has room
        /// to observe the victim unable to fire before it lifts.</summary>
        const float LongStunResistance = 10f;

        // ---- hitstun ---------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Being_shot_puts_the_victim_in_hitstun_which_then_expires()
        {
            var pair = SpawnFacingPair(8f, health: StartHealth);
            var shooter = pair[0];
            var target = pair[1];
            yield return new WaitForSeconds(0.3f);   // let both settle onto the floor

            Assert.IsFalse(IsStunned(target), "precondition: an unhit pawn is not stunned");

            AimAt(shooter, Position(target));
            SetFiring(shooter, true);

            yield return WaitUntil(() => IsStunned(target), timeoutSeconds: 3f);
            Assert.IsTrue(IsStunned(target), "a bullet carrying a HitstunController should stun the pawn it hits");
            Assert.Less(Health(target), StartHealth, "sanity: the stun came from an actual hit");

            SetFiring(shooter, false);

            // The window is an expiry TICK, so it lifts on its own with nobody ticking a countdown.
            yield return WaitUntil(() => !IsStunned(target), timeoutSeconds: 3f);
            Assert.IsFalse(IsStunned(target), "hitstun should expire without anyone clearing it");
        }

        [UnityTest]
        public IEnumerator A_stunned_pawn_cannot_fire_but_fires_again_once_the_stun_lifts()
        {
            var pair = SpawnFacingPair(8f, health: StartHealth);
            var shooter = pair[0];
            var target = pair[1];
            yield return new WaitForSeconds(0.3f);

            // Stretch the victim's stun so there is a comfortable window to observe it not firing.
            SetHitstunResistance(target, LongStunResistance);

            AimAt(shooter, Position(target));
            SetFiring(shooter, true);
            yield return WaitUntil(() => IsStunned(target), timeoutSeconds: 3f);
            SetFiring(shooter, false);

            // Let every bullet already in flight die (they despawn on hit / on the floor) so the projectile count
            // below is attributable solely to the target's own weapon.
            yield return WaitUntil(() => CountSpawnedWith("NetworkedMotion") == 0, timeoutSeconds: 6f);
            Assert.AreEqual(0, CountSpawnedWith("NetworkedMotion"), "precondition: no bullets in flight");
            Assert.IsTrue(IsStunned(target), "precondition: target still inside its stretched stun window");

            // Act: the stunned victim tries to shoot back.
            AimAt(target, Position(shooter));
            SetFiring(target, true);
            yield return WaitFrames(10);

            Assert.AreEqual(0, CountSpawnedWith("NetworkedMotion"),
                "a pawn in hitstun must not start a fire event, so it spawns no projectiles");

            // POSITIVE CONTROL: the very same held trigger fires the moment the window lapses. Without this, the
            // assertion above would pass just as well against a pawn whose weapon is simply broken.
            yield return WaitUntil(() => !IsStunned(target), timeoutSeconds: 5f);
            yield return WaitUntil(() => CountSpawnedWith("NetworkedMotion") > 0, timeoutSeconds: 2f);

            Assert.Greater(CountSpawnedWith("NetworkedMotion"), 0,
                "once hitstun expires the pawn fires again with no further input");
            SetFiring(target, false);
        }

        [UnityTest]
        public IEnumerator Hitstun_resistance_of_zero_makes_the_victim_immune()
        {
            var pair = SpawnFacingPair(8f, health: StartHealth);
            var shooter = pair[0];
            var target = pair[1];
            yield return new WaitForSeconds(0.3f);

            AimAt(shooter, Position(target));

            // POSITIVE CONTROL, same pawn: with no resistance the bullet DOES stun it. This is what proves the
            // Bullet prefab actually carries a HitstunDealer before we assert an absence below.
            SetFiring(shooter, true);
            yield return WaitUntil(() => IsStunned(target), timeoutSeconds: 3f);
            Assert.IsTrue(IsStunned(target), "control: an unresisted pawn IS stunned by a hit");
            SetFiring(shooter, false);
            yield return WaitUntil(() => !IsStunned(target), timeoutSeconds: 3f);

            // Now the receiving-end fold: 0x absorbs the stun entirely, without touching the bullet's authored value.
            SetHitstunResistance(target, 0f);
            int hpBefore = Health(target);

            SetFiring(shooter, true);
            yield return WaitUntilHealthBelow(target, hpBefore, timeoutSeconds: 3f);
            SetFiring(shooter, false);

            Assert.Less(Health(target), hpBefore, "sanity: the target really was hit again");
            Assert.IsFalse(IsStunned(target),
                "a 0x hitstun-resistance modifier on the receiver absorbs the stun the bullet dealt");
        }

        // ---- knockback -------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Being_shot_knocks_the_victim_away_from_the_shooter()
        {
            var pair = SpawnFacingPair(8f, health: StartHealth);
            var shooter = pair[0];
            var target = pair[1];
            yield return new WaitForSeconds(0.3f);

            float startZ = Position(target).z;
            AimAt(shooter, Position(target));
            SetFiring(shooter, true);
            yield return WaitUntilHealthBelow(target, StartHealth, timeoutSeconds: 3f);

            // The shot travels +Z and the victim never moves itself, so any +Z displacement is knockback.
            yield return WaitUntil(() => Position(target).z > startZ + 0.1f, timeoutSeconds: 2f);
            SetFiring(shooter, false);

            Assert.Greater(Position(target).z, startZ + 0.1f,
                "a bullet carrying a KnockbackController should shove the victim away from the shooter (+Z)");
        }

        // ---- composability (presence-dispatch; ADR 0002) -----------------------------------------

        /// <summary>
        /// The exchange switch IS the controller's presence: disable the HitstunController (an IHitMessageHandler,
        /// silent when disabled — the strip hook) on every bullet in flight and the hitstun exchange falls silent
        /// while the SAME hits' damage exchange still lands. Damage landing is the in-test positive control (the
        /// hits really resolved); the stun-happens control is the hitstun tests above, which run the identical
        /// setup unstripped.
        /// </summary>
        [UnityTest]
        public IEnumerator Stripping_the_hitstun_controller_component_silences_only_that_exchange()
        {
            var pair = SpawnFacingPair(8f, health: StartHealth);
            var shooter = pair[0];
            var target = pair[1];
            yield return new WaitForSeconds(0.3f);

            AimAt(shooter, Position(target));
            SetFiring(shooter, true);

            // Disable every frame so no bullet can land with its HitstunController audible; watch for any stun the whole time.
            bool everStunned = false;
            float t = 0f;
            while (Health(target) >= StartHealth && t < 4f)
            {
                StripHitstunDealersFromLiveProjectiles();
                everStunned |= IsStunned(target);
                t += Time.deltaTime;
                yield return null;
            }
            SetFiring(shooter, false);
            everStunned |= IsStunned(target);

            Assert.Less(Health(target), StartHealth,
                "sanity + positive control: the dealer-stripped bullets still hit and their damage exchange landed");
            Assert.IsFalse(everStunned,
                "with the HitstunController component disabled the hitstun exchange must not fire — component " +
                "presence is the switch, and the other exchanges on the same hit are unaffected");
        }

        [UnityTest]
        public IEnumerator Knockback_resistance_of_zero_makes_the_victim_immovable()
        {
            var pair = SpawnFacingPair(8f, health: StartHealth);
            var shooter = pair[0];
            var target = pair[1];
            yield return new WaitForSeconds(0.3f);

            // Paired with Being_shot_knocks_the_victim_away_from_the_shooter above, which is the positive control
            // for this fixture: it proves the identical setup DOES displace an unresisted victim.
            SetKnockbackResistance(target, 0f);

            float startZ = Position(target).z;
            AimAt(shooter, Position(target));
            SetFiring(shooter, true);
            yield return WaitUntilHealthBelow(target, StartHealth, timeoutSeconds: 3f);
            yield return WaitFrames(10);   // give any impulse time to integrate into a displacement
            SetFiring(shooter, false);

            Assert.Less(Health(target), StartHealth, "sanity: the target really was hit");
            Assert.Less(Mathf.Abs(Position(target).z - startZ), 0.05f,
                "a 0x knockback-resistance modifier on the receiver absorbs the impulse, so the victim does not move");
        }
    }
}
