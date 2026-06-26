using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Arena.Projectile;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Arena.Tests.Projectile
{
    /// <summary>
    /// Verifies two things the user asked for:
    ///
    ///   1. The SHIPPED "Primary Bullet" ProjectileType (the asset Bullet Root uses) routes each hit
    ///      category to the CORRECT IHitReaction list — a surface hit runs OnSurface, an enemy hit runs
    ///      OnEnemy, a bullet-vs-bullet hit runs OnProjectile — and the wrong list is never consulted.
    ///      These load the real asset, so a mis-authoring (empty list, wrong reaction, a damage value
    ///      that silently drops to a no-op) turns this red. This is the integration between
    ///      IHitReaction + the asset + ProjectileStepProcessor.
    ///
    ///   2. The system-wide "lose all health -> despawn" invariant: ANY reaction that drains a
    ///      projectile's Health to zero kills it (Alive == false), even one that never touches Alive
    ///      itself. The shell (PawnProjectile.ServerTick) despawns on !Alive.
    /// </summary>
    [TestFixture]
    public class BulletReactionRoutingTests
    {
        const string BulletAssetPath = "Assets/Prefabs/Projectiles/Primary Bullet.asset";

        readonly List<ProjectileType> _synthetic = new();

        [TearDown]
        public void TearDown()
        {
            // Only destroy types WE created; never the loaded shared asset.
            foreach (var t in _synthetic)
                if (t != null) UnityEngine.Object.DestroyImmediate(t);
            _synthetic.Clear();
        }

        ProjectileType LoadBullet()
        {
#if UNITY_EDITOR
            var t = AssetDatabase.LoadAssetAtPath<ProjectileType>(BulletAssetPath);
            Assert.IsNotNull(t, $"missing shipped bullet type at {BulletAssetPath}");
            return t;
#else
            Assert.Ignore("real-asset routing tests require the editor (AssetDatabase)");
            return null;
#endif
        }

        ProjectileType MakeType(int maxHealth, List<IHitReaction> onSurface = null,
            List<IHitReaction> onEnemy = null, List<IHitReaction> onProjectile = null)
        {
            var t = ScriptableObject.CreateInstance<ProjectileType>();
            t.MaxHealth = maxHealth;
            t.Speed = 10f;
            t.OnSurface = onSurface ?? new List<IHitReaction>();
            t.OnEnemy = onEnemy ?? new List<IHitReaction>();
            t.OnProjectile = onProjectile ?? new List<IHitReaction>();
            _synthetic.Add(t);
            return t;
        }

        static ProjectileState Fresh(ProjectileType type, int ownerId = 1, int id = 1) =>
            ProjectileState.Spawn(ownerId, id, Vector3.zero, Vector3.forward, type.Speed, type.MaxHealth);

        static int DamageCount(List<WorldEffect> e) => e.FindAll(x => x.Kind == EffectKind.Damage).Count;

        // ------------------------------------------------------------------
        // Shipped Primary Bullet: correct list per hit category.
        // ------------------------------------------------------------------

        [Test]
        public void PrimaryBullet_SurfaceHit_RunsOnSurface_DiesWithoutDamage()
        {
            var type = LoadBullet();
            var effects = new List<WorldEffect>();
            var hit = new WorldHit { Point = new Vector3(0, 0, 5), Normal = Vector3.back, PlayerId = -1 };

            var result = ProjectileStepProcessor.ResolveWorldHit(Fresh(type), type, in hit, effects);

            Assert.IsFalse(result.Alive,
                "a surface hit must run OnSurface (LoseHealth) and despawn the bullet at zero health");
            Assert.AreEqual(0, DamageCount(effects),
                "a surface hit must NOT emit damage — that would mean OnEnemy was consulted for a surface");
        }

        [Test]
        public void PrimaryBullet_EnemyHit_RunsOnEnemy_DealsDamageAndDies()
        {
            var type = LoadBullet();
            var effects = new List<WorldEffect>();
            const int owner = 1, victim = 2;
            var hit = new WorldHit { Point = new Vector3(0, 0, 5), Normal = Vector3.back, PlayerId = victim };

            var result = ProjectileStepProcessor.ResolveWorldHit(Fresh(type, owner), type, in hit, effects);

            Assert.AreEqual(1, DamageCount(effects),
                "an enemy hit must run OnEnemy (DealDamage) and emit exactly one damage effect");
            var dmg = effects.Find(e => e.Kind == EffectKind.Damage);
            Assert.AreEqual(victim, dmg.TargetPlayerId, "damage targets the player hit");
            Assert.AreEqual(owner, dmg.AttackerPlayerId, "damage is attributed to the firer");
            Assert.Greater(dmg.Amount, 0, "the authored DealDamage must carry a positive amount");
            Assert.IsFalse(result.Alive, "the shipped bullet despawns on an enemy hit (DespawnAfter)");
        }

        [Test]
        public void PrimaryBullet_OwnerHit_IsIgnored()
        {
            var type = LoadBullet();
            var effects = new List<WorldEffect>();
            const int owner = 7;
            var hit = new WorldHit { Point = new Vector3(0, 0, 5), Normal = Vector3.back, PlayerId = owner };

            var result = ProjectileStepProcessor.ResolveWorldHit(Fresh(type, owner), type, in hit, effects);

            Assert.IsTrue(result.Alive, "owner-safety: a bullet must pass through its own firer");
            Assert.AreEqual(0, DamageCount(effects), "no self-damage");
        }

        [Test]
        public void PrimaryBullet_ProjectileHit_RunsOnProjectile_AndInteracts()
        {
            var type = LoadBullet();
            var effects = new List<WorldEffect>();
            var self = Fresh(type, ownerId: 1, id: 1);
            var other = ProjectileState.Spawn(99, 2, new Vector3(0, 0, 0.1f), Vector3.back, type.Speed, type.MaxHealth);

            var result = ProjectileStepProcessor.ResolveProjectileHits(
                self, type, new ReadOnlySpan<ProjectileState>(new[] { other }), effects);

            Assert.IsFalse(result.Alive,
                "a bullet-vs-bullet hit must run OnProjectile and actually interact (the bullet dies). " +
                "If this is alive, OnProjectile is empty or authored with a reaction that no-ops on the " +
                "projectile channel (e.g. DealDamage, whose OtherPlayerId is always -1 there).");
        }

        // ------------------------------------------------------------------
        // System-wide health invariant.
        // ------------------------------------------------------------------

        /// <summary>A reaction that drains health but deliberately never sets Alive — proves the
        /// processor, not the reaction, owns death-at-zero.</summary>
        sealed class DrainHealth : IHitReaction
        {
            public int Amount;
            public void Apply(in HitInfo hit, ref ProjectileState self, List<WorldEffect> effects)
                => self.Health -= Amount; // intentionally does NOT touch self.Alive
        }

        [Test]
        public void HealthDepletedByAnyReaction_KillsProjectile_EvenIfReactionNeverSetsAlive()
        {
            var type = MakeType(maxHealth: 4,
                onSurface: new List<IHitReaction> { new DrainHealth { Amount = 2 } });
            var surface = new WorldHit { Point = new Vector3(0, 0, 5), Normal = Vector3.back, PlayerId = -1 };
            var effects = new List<WorldEffect>();

            var s = Fresh(type);
            s = ProjectileStepProcessor.ResolveWorldHit(s, type, in surface, effects);
            Assert.IsTrue(s.Alive, "after one drain (4 -> 2) the projectile is still alive");
            Assert.AreEqual(2, s.Health);

            s = ProjectileStepProcessor.ResolveWorldHit(s, type, in surface, effects);
            Assert.IsFalse(s.Alive,
                "once Health hits 0 the processor must mark the projectile dead, even though DrainHealth " +
                "never set Alive itself — this is the 'lose all health -> despawn' invariant");
        }

        [Test]
        public void NonHealthReaction_DoesNotKill_WhileHealthRemains()
        {
            // A ricochet changes direction but doesn't touch health: the invariant must NOT over-kill.
            var type = MakeType(maxHealth: 1, onSurface: new List<IHitReaction> { new Ricochet() });
            var surface = new WorldHit { Point = new Vector3(0, 0, 5), Normal = Vector3.back, PlayerId = -1 };
            var effects = new List<WorldEffect>();

            var s = ProjectileStepProcessor.ResolveWorldHit(Fresh(type), type, in surface, effects);

            Assert.IsTrue(s.Alive, "a reaction that leaves health > 0 must keep the projectile alive");
            Assert.AreEqual(1, s.Health, "ricochet must not change health");
        }
    }
}
