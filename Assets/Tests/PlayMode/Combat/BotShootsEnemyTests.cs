using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// First combat integration test: one scripted combatant shoots its weapon at an enemy combatant,
    /// and we assert the FULL set of effects a hit produces on the victim ??? discovered by reading the
    /// code + the Pawn Root prefab configuration:
    ///
    /// - DAMAGE: the DevilDaggers projectile (WeaponData "Devil Daggers Thing 1") deals 3 per hit; the
    ///   victim's Health drops and Pawn.OnDamageTaken fires (server-authoritative, always on).
    /// - COLOR: PawnColor flashes the body to its HitColor (red, 0.3s) on any Health change. No ownership
    ///   gate ??? it fires on the host.
    /// - KNOCKBACK: PawnKnockback turns the hit into an impulse along the shot direction (+Z here) and
    ///   pushes the non-self-moving victim back. NOTE: Pawn Root ships PawnKnockback.SpeedOnHit = 0 (a
    ///   no-op!), so this test deliberately enables knockback on the victim to exercise the real path
    ///   (KnockbackHelper -> PawnMovement.PendingImpulses -> CharacterController). Knockback also only
    ///   subscribes on the OWNER client, which is why CombatTestRig spawns pawns owned by the host.
    ///
    /// Driven through CombatTestRig via the typed CombatPlayModeTestFixture API.
    /// </summary>
    public sealed class BotShootsEnemyTests : CombatPlayModeTestFixture
    {
        const int StartHealth = 21;
        const int ProjectileDamage = 3;   // ProjectileType "DevilDaggers".Damage
        const float KnockbackSpeed = 12f; // Pawn Root ships 0; we enable it on the victim.

        [UnityTest]
        public IEnumerator Shooting_an_enemy_deals_damage_knocks_it_back_and_flashes_its_color()
        {
            // Arrange: two combatants 8 units apart on Z, facing each other.
            var pair = SpawnFacingPair(8f, health: StartHealth);
            var shooter = pair[0];
            var target = pair[1];

            // Let both pawns drop onto the floor and settle before aiming.
            yield return new WaitForSeconds(0.3f);

            // Knockback ships disabled (SpeedOnHit=0); enable it on the victim to make it observable.
            SetKnockbackSpeed(target, KnockbackSpeed);

            BeginCapture(target);
            Vector3 startPos = Position(target);
            Assert.AreEqual(StartHealth, Health(target), "precondition: target at full health");

            // Act: aim at the target and hold fire (auto weapon).
            AimAt(shooter, Position(target));
            SetFiring(shooter, true);

            // Wait for the first hit to land, then stop firing.
            yield return WaitUntilHealthBelow(target, StartHealth, timeoutSeconds: 3f);
            SetFiring(shooter, false);

            // --- DAMAGE ---
            Assert.Less(Health(target), StartHealth, "target should lose health from being shot");
            Assert.GreaterOrEqual(DamageEventCount(target), 1, "OnDamageTaken should have fired on the victim");
            Assert.AreEqual(ProjectileDamage, LastDamageAmount(target),
                "each DevilDaggers hit deals the projectile's configured damage");

            // --- COLOR FLASH --- give PawnColor.Update one tick to apply the flash, then sample. The
            // flash lasts 0.3s, so a couple of frames in it is still strongly red.
            yield return WaitFrames(2);
            Color c = BodyColor(target);
            Assert.Greater(c.r, 0.5f, "hit flash should drive the body toward red");
            Assert.Less(c.g, 0.5f, "red flash => low green");
            Assert.Less(c.b, 0.5f, "red flash => low blue");

            // --- KNOCKBACK --- the shot direction is +Z, and the victim does not move itself, so any
            // +Z displacement is knockback. Poll a couple of frames for the impulse to integrate.
            yield return WaitUntil(() => Position(target).z > startPos.z + 0.1f, timeoutSeconds: 2f);
            Assert.Greater(Position(target).z, startPos.z + 0.1f,
                "knockback should push the victim away from the shooter (+Z)");
        }
    }
}
