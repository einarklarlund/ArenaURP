using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Order-independence pin (ADR 0005): "authored order can never change a hit's outcome." Durability is spent
    /// BEFORE the handler loop in <c>HitMessage.Dispatch</c> and every handler runs unconditionally, so permuting
    /// the source's <c>IHitMessageHandler</c> component order must not drop any effect. This rotates that order on
    /// every live projectile each frame — so no hit lands in the authored order — and asserts the SAME hit still
    /// delivers damage AND hitstun AND knockback. The positive control (these fire under the authored order) is the
    /// sibling HitstunKnockbackTests / BotShootsEnemyTests, which run the identical setup unscrambled.
    /// </summary>
    public sealed class HitDispatchOrderTests : CombatPlayModeTestFixture
    {
        const int StartHealth = 400;

        [UnityTest]
        public IEnumerator Scrambling_the_hit_handler_order_drops_no_effect()
        {
            var pair = SpawnFacingPair(8f, health: StartHealth);
            var shooter = pair[0];
            var target = pair[1];
            yield return new WaitForSeconds(0.3f);

            float startZ = Position(target).z;
            AimAt(shooter, Position(target));
            SetFiring(shooter, true);

            // Permute every live bullet's handler order each frame, until all three effects have landed (or we time
            // out). Because bullets fly ~8 units before hitting, many frames of scrambling precede the first hit.
            int everScrambled = 0;
            float t = 0f;
            while (!(Health(target) < StartHealth && IsStunned(target) && Position(target).z > startZ + 0.1f)
                   && t < 4f)
            {
                everScrambled += ScrambleHitHandlerOrderOnLiveProjectiles();
                t += Time.deltaTime;
                yield return null;
            }
            SetFiring(shooter, false);

            // The rig counts only projectiles whose handler TYPE SEQUENCE actually differed after the move, so a
            // non-zero count is proof the delivery order really was permuted — not merely that a move was attempted.
            Assert.Greater(everScrambled, 0,
                "sanity: at least one bullet's handler order demonstrably changed before it hit (else the pin proves nothing)");
            Assert.Less(Health(target), StartHealth, "damage still landed under a non-authored handler order");
            Assert.IsTrue(IsStunned(target), "hitstun still landed under a non-authored handler order");
            Assert.Greater(Position(target).z, startZ + 0.1f, "knockback still landed under a non-authored handler order (+Z)");
        }
    }
}
