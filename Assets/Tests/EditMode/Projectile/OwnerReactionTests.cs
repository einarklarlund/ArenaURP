using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Arena.Projectile;

namespace Arena.Tests.Projectile
{
    /// <summary>
    /// EditMode tests for owner-contributed reactions: the projectile shell looks up its owner's extra
    /// reactions server-side and passes them into the pure processor as data (no payload). These pin that
    /// the processor runs them, respects owner-safety, and stacks them on top of the type's reactions.
    /// </summary>
    [TestFixture]
    public class OwnerReactionTests
    {
        private readonly List<ProjectileType> _types = new();

        private ProjectileType MakeType()
        {
            var t = ScriptableObject.CreateInstance<ProjectileType>();
            _types.Add(t);
            return t;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var t in _types)
                if (t != null)
                    Object.DestroyImmediate(t);
            _types.Clear();
        }

        private static ProjectileState Self() =>
            ProjectileState.Spawn(ownerId: 1, id: 2, Vector3.zero, Vector3.forward, speed: 10f, health: 3);

        private static WorldHit EnemyHit(int playerId) => new()
        {
            Point = Vector3.forward, Normal = Vector3.back, Distance = 1f, PlayerId = playerId,
        };

        [Test]
        public void OwnerReaction_RunsOnEnemyHit_EvenWhenTypeHasNoReactions()
        {
            var type = MakeType(); // OnEnemy empty
            var owner = new List<IHitReaction> { new DealDamage { Amount = 5, DespawnAfter = false } };
            var effects = new List<WorldEffect>();

            ProjectileStepProcessor.ResolveWorldHit(Self(), type, EnemyHit(99), effects, new OwnerReactions(owner, null, null));

            Assert.AreEqual(1, effects.Count);
            Assert.AreEqual(EffectKind.Damage, effects[0].Kind);
            Assert.AreEqual(99, effects[0].TargetPlayerId);
            Assert.AreEqual(5, effects[0].Amount);
            Assert.AreEqual(1, effects[0].AttackerPlayerId, "owner reaction damage is attributed to the projectile owner");
        }

        [Test]
        public void OwnerReaction_StacksOnTopOfTypeReactions()
        {
            var type = MakeType();
            type.OnEnemy.Add(new DealDamage { Amount = 3, DespawnAfter = false });
            var owner = new List<IHitReaction> { new DealDamage { Amount = 5, DespawnAfter = false } };
            var effects = new List<WorldEffect>();

            ProjectileStepProcessor.ResolveWorldHit(Self(), type, EnemyHit(99), effects, new OwnerReactions(owner, null, null));

            Assert.AreEqual(2, effects.Count, "both the type's and the owner's reaction should fire");
            Assert.AreEqual(3, effects[0].Amount);
            Assert.AreEqual(5, effects[1].Amount);
        }

        [Test]
        public void OwnerReaction_StillRuns_WhenTheTypeReactionDespawnedTheProjectile()
        {
            // The bullet's own DealDamage despawns it on hit, but the hit happened, so the owner's on-hit
            // bonus must still land (both damage effects emitted for the single hit).
            var type = MakeType();
            type.OnEnemy.Add(new DealDamage { Amount = 3, DespawnAfter = true });
            var owner = new List<IHitReaction> { new DealDamage { Amount = 5, DespawnAfter = false } };
            var effects = new List<WorldEffect>();

            ProjectileStepProcessor.ResolveWorldHit(Self(), type, EnemyHit(99), effects, new OwnerReactions(owner, null, null));

            Assert.AreEqual(2, effects.Count, "owner on-hit effect lands even though the type reaction despawned the bullet");
            Assert.AreEqual(3, effects[0].Amount);
            Assert.AreEqual(5, effects[1].Amount);
        }

        [Test]
        public void OwnerReaction_NotRun_WhenTheHitIsTheOwnerThemself()
        {
            var type = MakeType();
            var owner = new List<IHitReaction> { new DealDamage { Amount = 5 } };
            var effects = new List<WorldEffect>();

            // Hitting the owner (PlayerId == OwnerId) returns early — owner-safety, no reactions at all.
            ProjectileStepProcessor.ResolveWorldHit(Self(), type, EnemyHit(1), effects, new OwnerReactions(owner, null, null));

            Assert.AreEqual(0, effects.Count);
        }

        [Test]
        public void NullOwnerReactions_IsHarmless()
        {
            var type = MakeType();
            type.OnEnemy.Add(new DealDamage { Amount = 3, DespawnAfter = false });
            var effects = new List<WorldEffect>();

            ProjectileStepProcessor.ResolveWorldHit(Self(), type, EnemyHit(99), effects);

            Assert.AreEqual(1, effects.Count, "passing no owner reactions behaves exactly as before");
        }
    }
}
