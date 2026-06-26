using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Arena.Projectile;

namespace Arena.Tests.Projectile
{
    /// <summary>
    /// EditMode unit tests for individual IHitReaction implementations. Each builds a HitInfo and an
    /// empty effect list, applies the reaction, and asserts on the mutated state + emitted effects.
    /// </summary>
    [TestFixture]
    public class ProjectileReactionTests
    {
        private const float Tol = 1e-4f;

        private readonly List<ProjectileType> _types = new();

        private ProjectileType MakeType()
        {
            var t = ScriptableObject.CreateInstance<ProjectileType>();
            _types.Add(t);
            return t;
        }

        private static ProjectileState NewSelf(int ownerId = 1, int id = 1, int health = 3) =>
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
        // Ricochet
        // ------------------------------------------------------------------

        [Test]
        public void Ricochet_ReflectsVelocityAcrossNormal()
        {
            var self = NewSelf();
            var effects = new List<WorldEffect>();
            // Incoming (1,-1,0) off a floor normal (0,1,0) reflects to (1,1,0).
            var hit = new HitInfo
            {
                Category = HitCategory.Surface,
                Point = new Vector3(5f, 0f, 5f),
                Normal = Vector3.up,
                IncomingVelocity = new Vector3(1f, -1f, 0f),
                Direction = new Vector3(1f, -1f, 0f).normalized,
            };

            new Ricochet().Apply(in hit, ref self, effects);

            Vector3 expected = new Vector3(1f, 1f, 0f).normalized;
            Assert.AreEqual(expected.x, self.OriginForward.x, Tol);
            Assert.AreEqual(expected.y, self.OriginForward.y, Tol);
            Assert.AreEqual(expected.z, self.OriginForward.z, Tol);
            Assert.AreEqual(hit.Point, self.OriginPos, "ricochet re-segments at the impact point");
            Assert.AreEqual(0, effects.Count);
        }

        // ------------------------------------------------------------------
        // DeflectProjectile
        // ------------------------------------------------------------------

        [Test]
        public void DeflectProjectile_RedirectsAlongOtherForwardPreservingSpeed()
        {
            var self = NewSelf();
            var effects = new List<WorldEffect>();
            var hit = new HitInfo
            {
                Category = HitCategory.Projectile,
                Point = new Vector3(2f, 2f, 2f),
                IncomingVelocity = new Vector3(0f, 0f, 5f), // speed 5
                OtherForward = Vector3.right,
            };

            new DeflectProjectile().Apply(in hit, ref self, effects);

            Assert.AreEqual(1f, self.OriginForward.x, Tol);
            Assert.AreEqual(0f, self.OriginForward.y, Tol);
            Assert.AreEqual(0f, self.OriginForward.z, Tol);
            Assert.AreEqual(5f, self.OriginSpeed, Tol, "incoming speed preserved");
            Assert.AreEqual(hit.Point, self.OriginPos);
        }

        // ------------------------------------------------------------------
        // LoseHealth
        // ------------------------------------------------------------------

        [Test]
        public void LoseHealth_DecrementsButStaysAliveAboveZero()
        {
            var self = NewSelf(health: 2);
            var effects = new List<WorldEffect>();

            new LoseHealth { Amount = 1 }.Apply(default(HitInfo), ref self, effects);

            Assert.AreEqual(1, self.Health);
            Assert.IsTrue(self.Alive);
        }

        [Test]
        public void LoseHealth_DrainsToZero_IsDead()
        {
            // LoseHealth only subtracts; death is intrinsic to Health (ProjectileState.Alive => Health > 0),
            // so draining the last point of health makes the projectile not-alive with no separate flag.
            var self = NewSelf(health: 1);
            var effects = new List<WorldEffect>();

            new LoseHealth { Amount = 1 }.Apply(default(HitInfo), ref self, effects);

            Assert.AreEqual(0, self.Health, "LoseHealth subtracts its amount");
            Assert.IsFalse(self.Alive, "at zero health the projectile is dead (Alive is derived from Health)");
        }

        // ------------------------------------------------------------------
        // DealDamage
        // ------------------------------------------------------------------

        [Test]
        public void DealDamage_EnemyHit_EmitsDamageWithAttackerOwnerAndKills()
        {
            var self = NewSelf(ownerId: 7);
            var effects = new List<WorldEffect>();
            var hit = new HitInfo
            {
                Category = HitCategory.Enemy,
                Point = new Vector3(1f, 0f, 1f),
                Direction = Vector3.forward,
                OtherPlayerId = 9,
            };

            new DealDamage { Amount = 12, DespawnAfter = true }.Apply(in hit, ref self, effects);

            Assert.AreEqual(1, effects.Count);
            Assert.AreEqual(EffectKind.Damage, effects[0].Kind);
            Assert.AreEqual(9, effects[0].TargetPlayerId);
            Assert.AreEqual(7, effects[0].AttackerPlayerId, "attacker is the firing owner");
            Assert.AreEqual(12, effects[0].Amount);
            Assert.IsFalse(self.Alive, "DespawnAfter kills the projectile");
        }

        [Test]
        public void DealDamage_DespawnAfterFalse_KeepsProjectileAlive()
        {
            var self = NewSelf(ownerId: 7);
            var effects = new List<WorldEffect>();
            var hit = new HitInfo { Category = HitCategory.Enemy, OtherPlayerId = 9 };

            new DealDamage { Amount = 5, DespawnAfter = false }.Apply(in hit, ref self, effects);

            Assert.AreEqual(1, effects.Count);
            Assert.IsTrue(self.Alive);
        }

        [Test]
        public void DealDamage_IgnoresOwnerHit()
        {
            var self = NewSelf(ownerId: 7);
            var effects = new List<WorldEffect>();
            var hit = new HitInfo { Category = HitCategory.Enemy, OtherPlayerId = 7 }; // own player id

            new DealDamage { Amount = 5 }.Apply(in hit, ref self, effects);

            Assert.AreEqual(0, effects.Count);
            Assert.IsTrue(self.Alive);
        }

        [Test]
        public void DealDamage_IgnoresNonPlayerHit()
        {
            var self = NewSelf(ownerId: 7);
            var effects = new List<WorldEffect>();
            var hit = new HitInfo { Category = HitCategory.Surface, OtherPlayerId = -1 };

            new DealDamage { Amount = 5 }.Apply(in hit, ref self, effects);

            Assert.AreEqual(0, effects.Count);
        }

        // ------------------------------------------------------------------
        // SpawnProjectiles
        // ------------------------------------------------------------------

        [Test]
        public void SpawnProjectiles_EmitsSpawnEffectWithTypeCountAndOwner()
        {
            var childType = MakeType();
            var self = NewSelf(ownerId: 3);
            var effects = new List<WorldEffect>();
            var hit = new HitInfo { Point = new Vector3(1f, 2f, 3f) };

            new SpawnProjectiles { Type = childType, Count = 4 }.Apply(in hit, ref self, effects);

            Assert.AreEqual(1, effects.Count, "one Spawn effect carries the count");
            Assert.AreEqual(EffectKind.Spawn, effects[0].Kind);
            Assert.AreSame(childType, effects[0].Type);
            Assert.AreEqual(4, effects[0].Count);
            Assert.AreEqual(3, effects[0].AttackerPlayerId, "spawn owner is the firing owner");
            Assert.AreEqual(hit.Point, effects[0].Point);
        }

        [Test]
        public void SpawnProjectiles_NullTypeEmitsNothing()
        {
            var self = NewSelf();
            var effects = new List<WorldEffect>();

            new SpawnProjectiles { Type = null, Count = 4 }.Apply(default(HitInfo), ref self, effects);

            Assert.AreEqual(0, effects.Count);
        }

        // ------------------------------------------------------------------
        // SpawnImpactVfx
        // ------------------------------------------------------------------

        [Test]
        public void SpawnImpactVfx_EmitsVfxEffect()
        {
            var vfxType = MakeType();
            var self = NewSelf();
            var effects = new List<WorldEffect>();
            var hit = new HitInfo { Point = new Vector3(4f, 0f, 4f), Normal = Vector3.up };

            new SpawnImpactVfx { Type = vfxType }.Apply(in hit, ref self, effects);

            Assert.AreEqual(1, effects.Count);
            Assert.AreEqual(EffectKind.Vfx, effects[0].Kind);
            Assert.AreSame(vfxType, effects[0].Type);
            Assert.AreEqual(hit.Point, effects[0].Point);
            Assert.AreEqual(hit.Normal, effects[0].Normal);
        }

        // ------------------------------------------------------------------
        // ApplyDebuff
        // ------------------------------------------------------------------

        [Test]
        public void ApplyDebuff_PlayerHit_EmitsDebuffEffect()
        {
            var self = NewSelf();
            var effects = new List<WorldEffect>();
            var hit = new HitInfo { Category = HitCategory.Enemy, OtherPlayerId = 8 };

            new ApplyDebuff { Debuff = DebuffType.Burn }.Apply(in hit, ref self, effects);

            Assert.AreEqual(1, effects.Count);
            Assert.AreEqual(EffectKind.Debuff, effects[0].Kind);
            Assert.AreEqual(8, effects[0].TargetPlayerId);
            Assert.AreEqual(DebuffType.Burn, effects[0].Debuff);
        }

        [Test]
        public void ApplyDebuff_NonPlayerHit_EmitsNothing()
        {
            var self = NewSelf();
            var effects = new List<WorldEffect>();
            var hit = new HitInfo { Category = HitCategory.Surface, OtherPlayerId = -1 };

            new ApplyDebuff { Debuff = DebuffType.Slow }.Apply(in hit, ref self, effects);

            Assert.AreEqual(0, effects.Count);
        }
    }
}
