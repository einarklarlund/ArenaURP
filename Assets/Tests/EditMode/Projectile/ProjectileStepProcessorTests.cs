using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Arena.Projectile;

namespace Arena.Tests.Projectile
{
    /// <summary>
    /// EditMode tests for the functional-core ProjectileStepProcessor. Effects are returned in a
    /// caller-supplied list; there is no world interface or fake. Assertions are on returned state
    /// plus the emitted effect list.
    /// </summary>
    [TestFixture]
    public class ProjectileStepProcessorTests
    {
        private const float Tol = 1e-4f;

        private readonly List<ProjectileType> _types = new();

        private ProjectileType MakeType(
            MovementKind movement = MovementKind.Linear,
            float speed = 10f,
            float acceleration = 0f,
            float lifetime = 5f)
        {
            var t = ScriptableObject.CreateInstance<ProjectileType>();
            t.Movement = movement;
            t.Speed = speed;
            t.Acceleration = acceleration;
            t.Lifetime = lifetime;
            t.OnSurface = new List<IHitReaction>();
            t.OnEnemy = new List<IHitReaction>();
            t.OnProjectile = new List<IHitReaction>();
            _types.Add(t);
            return t;
        }

        private static ProjectileState NewState(int ownerId = 1, int id = 1, int health = 3) =>
            ProjectileState.Spawn(ownerId, id, Vector3.zero, Vector3.forward, speed: 10f, health);

        [TearDown]
        public void TearDown()
        {
            foreach (var t in _types)
                if (t != null)
                    Object.DestroyImmediate(t);
            _types.Clear();
        }

        // ------------------------------------------------------------------
        // ResolveWorldHit
        // ------------------------------------------------------------------

        [Test]
        public void ResolveWorldHit_OwnerHit_NoEffectsNoReactionsNoSnap()
        {
            var type = MakeType();
            type.OnEnemy.Add(new DealDamage { Amount = 10, DespawnAfter = true });
            var s = NewState(ownerId: 5);
            Vector3 originBefore = s.OriginPos;
            var effects = new List<WorldEffect>();

            var hit = new WorldHit { Point = new Vector3(9f, 9f, 9f), Normal = Vector3.up, PlayerId = 5 };
            ProjectileState result = ProjectileStepProcessor.ResolveWorldHit(s, type, in hit, effects);

            Assert.AreEqual(0, effects.Count, "owner hit emits nothing");
            Assert.IsTrue(result.Alive, "owner hit does not kill");
            Assert.AreEqual(originBefore, result.OriginPos, "owner hit does not snap the segment");
        }

        [Test]
        public void ResolveWorldHit_EnemyHit_RunsOnEnemyAndSnapsToImpact()
        {
            var type = MakeType();
            type.OnEnemy.Add(new DealDamage { Amount = 15, DespawnAfter = true });
            type.OnSurface.Add(new SpawnImpactVfx()); // present but must NOT run for an enemy hit
            var s = NewState(ownerId: 1);
            var effects = new List<WorldEffect>();

            var point = new Vector3(2f, 0f, 4f);
            var hit = new WorldHit { Point = point, Normal = Vector3.up, PlayerId = 2 };
            ProjectileState result = ProjectileStepProcessor.ResolveWorldHit(s, type, in hit, effects);

            Assert.AreEqual(1, effects.Count);
            Assert.AreEqual(EffectKind.Damage, effects[0].Kind);
            Assert.AreEqual(2, effects[0].TargetPlayerId);
            Assert.AreEqual(1, effects[0].AttackerPlayerId);
            Assert.AreEqual(15, effects[0].Amount);
            Assert.IsFalse(result.Alive, "DealDamage with DespawnAfter kills");
            Assert.AreEqual(point, result.OriginPos, "segment snapped to the impact point");
        }

        [Test]
        public void ResolveWorldHit_SurfaceHit_RunsOnSurfaceAndSnaps()
        {
            var type = MakeType();
            type.OnSurface.Add(new SpawnImpactVfx());
            type.OnEnemy.Add(new DealDamage()); // present but must NOT run for a surface hit
            var s = NewState(ownerId: 1);
            var effects = new List<WorldEffect>();

            var point = new Vector3(1f, 1f, 1f);
            var hit = new WorldHit { Point = point, Normal = Vector3.up, PlayerId = -1 };
            ProjectileState result = ProjectileStepProcessor.ResolveWorldHit(s, type, in hit, effects);

            Assert.AreEqual(1, effects.Count);
            Assert.AreEqual(EffectKind.Vfx, effects[0].Kind);
            Assert.AreEqual(point, result.OriginPos, "segment snapped to the impact point");
        }

        // ------------------------------------------------------------------
        // ResolveProjectileHits
        // ------------------------------------------------------------------

        [Test]
        public void ResolveProjectileHits_SameOwnerOverlap_Skipped()
        {
            var type = MakeType();
            type.OnProjectile.Add(new LoseHealth { Amount = 1 });
            var self = NewState(ownerId: 10, id: 1, health: 3);
            var other = NewState(ownerId: 10, id: 2, health: 3); // same owner
            var overlaps = new[] { other };
            var effects = new List<WorldEffect>();

            ProjectileState result =
                ProjectileStepProcessor.ResolveProjectileHits(self, type, overlaps, effects);

            Assert.AreEqual(0, effects.Count);
            Assert.AreEqual(3, result.Health, "same-owner overlap does not react");
        }

        [Test]
        public void ResolveProjectileHits_DifferentOwner_RunsOnProjectileAndRecordsGuard()
        {
            var type = MakeType();
            type.OnProjectile.Add(new LoseHealth { Amount = 1 });
            var self = NewState(ownerId: 10, id: 1, health: 3);
            var other = NewState(ownerId: 20, id: 2, health: 3);
            var overlaps = new[] { other };
            var effects = new List<WorldEffect>();

            ProjectileState result =
                ProjectileStepProcessor.ResolveProjectileHits(self, type, overlaps, effects);

            Assert.AreEqual(2, result.Health, "OnProjectile (LoseHealth) ran across a different owner");
            Assert.AreEqual(2, result.LastHitProjectileId, "one-shot guard records the handled projectile");
        }

        [Test]
        public void ResolveProjectileHits_GuardSuppressesReFireWhileStillOverlapping()
        {
            var type = MakeType();
            type.OnProjectile.Add(new LoseHealth { Amount = 1 });
            var self = NewState(ownerId: 10, id: 1, health: 3);
            self.LastHitProjectileId = 2; // already handled this pair last tick
            var other = NewState(ownerId: 20, id: 2, health: 3);
            var overlaps = new[] { other };
            var effects = new List<WorldEffect>();

            ProjectileState result =
                ProjectileStepProcessor.ResolveProjectileHits(self, type, overlaps, effects);

            Assert.AreEqual(0, effects.Count);
            Assert.AreEqual(3, result.Health, "already-recorded overlap must not re-fire");
            Assert.AreEqual(2, result.LastHitProjectileId, "guard stays set while still overlapping");
        }

        [Test]
        public void ResolveProjectileHits_NewIdFiresEvenWhileGuardSet()
        {
            var type = MakeType();
            type.OnProjectile.Add(new LoseHealth { Amount = 1 });
            var self = NewState(ownerId: 10, id: 1, health: 3);
            self.LastHitProjectileId = 2; // guarding a previous projectile
            var newOther = NewState(ownerId: 20, id: 3, health: 3); // a NEW projectile
            var overlaps = new[] { newOther };
            var effects = new List<WorldEffect>();

            ProjectileState result =
                ProjectileStepProcessor.ResolveProjectileHits(self, type, overlaps, effects);

            Assert.AreEqual(2, result.Health, "a new id fires");
            Assert.AreEqual(3, result.LastHitProjectileId, "guard advances to the newly handled id");
        }

        // ------------------------------------------------------------------
        // Determinism
        // ------------------------------------------------------------------

        [Test]
        public void Pipeline_IsDeterministic()
        {
            ProjectileState Run(out List<WorldEffect> effects)
            {
                var type = MakeType(speed: 20f, acceleration: 5f, lifetime: 10f);
                type.OnEnemy.Add(new DealDamage { Amount = 7, DespawnAfter = false });
                var s = NewState(ownerId: 1, id: 1, health: 3);
                s.Age = 0.1f; // the shell derives Age from the spawn tick; here we set it directly
                effects = new List<WorldEffect>();
                var hit = new WorldHit { Point = new Vector3(3f, 0f, 5f), Normal = Vector3.up, PlayerId = 2 };
                return ProjectileStepProcessor.ResolveWorldHit(s, type, in hit, effects);
            }

            ProjectileState a = Run(out var effectsA);
            ProjectileState b = Run(out var effectsB);

            Assert.AreEqual(a.Age, b.Age, Tol);
            Assert.AreEqual(a.Health, b.Health);
            Assert.AreEqual(a.Alive, b.Alive);
            Assert.AreEqual(a.OriginPos, b.OriginPos);
            Assert.AreEqual(a.OriginForward, b.OriginForward);
            Assert.AreEqual(a.OriginSpeed, b.OriginSpeed, Tol);
            Assert.AreEqual(a.OriginAge, b.OriginAge, Tol);
            Assert.AreEqual(a.LastHitProjectileId, b.LastHitProjectileId);

            Assert.AreEqual(effectsA.Count, effectsB.Count);
            for (int i = 0; i < effectsA.Count; i++)
            {
                Assert.AreEqual(effectsA[i].Kind, effectsB[i].Kind);
                Assert.AreEqual(effectsA[i].TargetPlayerId, effectsB[i].TargetPlayerId);
                Assert.AreEqual(effectsA[i].AttackerPlayerId, effectsB[i].AttackerPlayerId);
                Assert.AreEqual(effectsA[i].Amount, effectsB[i].Amount);
            }
        }
    }
}
