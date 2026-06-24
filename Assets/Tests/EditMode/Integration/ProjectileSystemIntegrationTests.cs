using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Arena.Projectile;

namespace Arena.Tests.Integration
{
    /// <summary>
    /// Cross-component integration tests for the projectile system. The existing tests in
    /// Tests/EditMode/Projectile exercise each piece in isolation (one reaction, one processor call,
    /// a fresh ProjectileType per case). These tests instead compose the pieces the way the imperative
    /// shell (PawnProjectile.ServerTick) drives them, to catch bugs that only live in the SEAMS:
    ///
    ///   * movement strategy -> processor -> reaction  (the velocity ArcMovement computes is what
    ///     Ricochet reflects, not the spawn velocity);
    ///   * reaction-list ORDER  (RunReactions short-circuits on death, so a spawn-on-death authored
    ///     after the killing reaction silently never fires);
    ///   * the world channel chaining into the projectile channel within one tick;
    ///   * the ProjectileType flyweight staying immutable across many bullets of the same kind.
    ///
    /// All assertions are on observable state + the emitted ProjectileEffect list — no engine, no
    /// FishNet, so they stay in EditMode. The handful of things these CANNOT reach (the real
    /// Physics.SphereCast, collider->playerId resolution, OnTick ordering, Despawn) are engine glue in
    /// ProjectileWorldProvider/PawnProjectile and belong to a PlayMode smoke, not here.
    /// </summary>
    [TestFixture]
    public class ProjectileSystemIntegrationTests
    {
        private const float Tol = 1e-4f;

        private readonly List<ProjectileType> _types = new();

        private ProjectileType MakeType(
            MovementKind movement = MovementKind.Linear,
            float speed = 10f,
            float gravity = -10f,
            int damage = 10)
        {
            var t = ScriptableObject.CreateInstance<ProjectileType>();
            t.Movement = movement;
            t.Speed = speed;
            t.Gravity = gravity;
            t.Damage = damage;
            t.OnSurface = new List<IHitReaction>();
            t.OnEnemy = new List<IHitReaction>();
            t.OnProjectile = new List<IHitReaction>();
            _types.Add(t);
            return t;
        }

        private static ProjectileState SpawnForward(int ownerId = 1, int id = 1, int health = 3,
            float speed = 10f) =>
            ProjectileState.Spawn(ownerId, id, Vector3.zero, Vector3.forward, speed, health);

        [TearDown]
        public void TearDown()
        {
            foreach (var t in _types)
                if (t != null)
                    Object.DestroyImmediate(t);
            _types.Clear();
        }

        // ------------------------------------------------------------------
        // Flyweight immutability: the shared ProjectileType must survive many hits unchanged.
        //
        // This is the seam the per-test "fresh type, single hit" unit tests cannot cover. Firing the
        // SAME type at a player repeatedly must deal the authored DealDamage EACH time, never
        // accumulating reactions on the shared list. Guards the spec invariant "ProjectileType is
        // shared/immutable at runtime" — and the regression where the processor used to append a
        // DealDamage to OnEnemy on every hit.
        // ------------------------------------------------------------------

        [Test]
        public void SameType_HitsEnemyRepeatedly_DamageStaysConstant_TypeNotMutated()
        {
            // Damage is authored data now (a DealDamage in OnEnemy), not an in-code default.
            var type = MakeType();
            type.OnEnemy.Add(new DealDamage { Amount = 10, DespawnAfter = true });
            int onEnemyCountBefore = type.OnEnemy.Count;

            var enemyHit = new WorldHit { Point = new Vector3(0f, 0f, 3f), Normal = Vector3.back, PlayerId = 2 };

            for (int shot = 1; shot <= 3; shot++)
            {
                var s = SpawnForward(ownerId: 1, id: shot);
                var effects = new List<ProjectileEffect>();

                ProjectileStepProcessor.ResolveWorldHit(s, type, in enemyHit, effects);

                int damageEffects = effects.FindAll(e => e.Kind == EffectKind.Damage).Count;
                Assert.AreEqual(1, damageEffects,
                    $"shot #{shot}: exactly one damage effect expected (got {damageEffects}). " +
                    "More than one means the shared type's OnEnemy list is accumulating DealDamage.");
                Assert.AreEqual(10, effects.Find(e => e.Kind == EffectKind.Damage).Amount,
                    $"shot #{shot}: damage amount must equal the authored DealDamage, not a multiple of it.");
            }

            Assert.AreEqual(onEnemyCountBefore, type.OnEnemy.Count,
                "ResolveWorldHit must not mutate the shared ProjectileType.OnEnemy list.");
        }

        [Test]
        public void ProjectileChannel_RepeatedOverlaps_DoNotMutateSharedType()
        {
            var type = MakeType();
            type.OnProjectile.Add(new LoseHealth { Amount = 1 });
            int onProjectileCountBefore = type.OnProjectile.Count;

            for (int tick = 0; tick < 3; tick++)
            {
                var self = SpawnForward(ownerId: 10, id: 1, health: 9);
                var other = SpawnForward(ownerId: 20, id: 100 + tick); // different owner, fresh id each tick
                var effects = new List<ProjectileEffect>();

                ProjectileStepProcessor.ResolveProjectileHits(self, type,
                    new System.ReadOnlySpan<ProjectileState>(new[] { other }), effects);
            }

            Assert.AreEqual(onProjectileCountBefore, type.OnProjectile.Count,
                "ResolveProjectileHits must not mutate the shared ProjectileType.OnProjectile list.");
        }

        // ------------------------------------------------------------------
        // Reaction-list ORDER of operations. RunReactions stops at the first reaction that kills the
        // projectile, so anything authored AFTER a fatal reaction never runs. A "spawn child bullets on
        // death" placed after the kill is a real, silent, player-visible mis-authoring. Pin the contract
        // in both orders.
        // ------------------------------------------------------------------

        [Test]
        public void SurfaceReactions_SpawnBeforeKill_SpawnsChildren()
        {
            var child = MakeType();
            var type = MakeType();
            type.OnSurface.Add(new SpawnProjectiles { Type = child, Count = 2 }); // spawn first...
            type.OnSurface.Add(new LoseHealth { Amount = 99 });                   // ...then die

            var s = SpawnForward();
            var effects = new List<ProjectileEffect>();
            var hit = new WorldHit { Point = new Vector3(0f, 0f, 5f), Normal = Vector3.back, PlayerId = -1 };

            ProjectileState result = ProjectileStepProcessor.ResolveWorldHit(s, type, in hit, effects);

            Assert.IsFalse(result.Alive, "LoseHealth(99) kills the projectile");
            Assert.AreEqual(1, effects.FindAll(e => e.Kind == EffectKind.Spawn).Count,
                "a spawn reaction ordered BEFORE the kill must still emit its Spawn effect");
            Assert.AreEqual(2, effects.Find(e => e.Kind == EffectKind.Spawn).Count);
        }

        [Test]
        public void SurfaceReactions_KillBeforeSpawn_ShortCircuits_NoChildren()
        {
            var child = MakeType();
            var type = MakeType();
            type.OnSurface.Add(new LoseHealth { Amount = 99 });                   // die first...
            type.OnSurface.Add(new SpawnProjectiles { Type = child, Count = 2 }); // ...spawn never reached

            var s = SpawnForward();
            var effects = new List<ProjectileEffect>();
            var hit = new WorldHit { Point = new Vector3(0f, 0f, 5f), Normal = Vector3.back, PlayerId = -1 };

            ProjectileState result = ProjectileStepProcessor.ResolveWorldHit(s, type, in hit, effects);

            Assert.IsFalse(result.Alive);
            Assert.AreEqual(0, effects.FindAll(e => e.Kind == EffectKind.Spawn).Count,
                "a reaction authored AFTER a fatal reaction must NOT run (RunReactions short-circuits on death). " +
                "This documents why spawn-on-death must precede the killing reaction.");
        }

        // ------------------------------------------------------------------
        // Kinematics -> reaction. The velocity a Ricochet reflects is the projectile's CURRENT velocity
        // (computed by the movement strategy at the impact tick), not its spawn velocity. For an arc
        // bullet past launch, gravity has bent the velocity downward, so the bounce must carry that bend.
        // This composes ArcMovement.VelocityAt -> ResolveWorldHit -> Ricochet.
        // ------------------------------------------------------------------

        [Test]
        public void ArcBullet_RicochetReflectsGravityBentVelocity_NotSpawnVelocity()
        {
            // Arc: forward +Z at speed 10, gravity -10. One second in, velocity = (0,0,10)+(0,-10,0) = (0,-10,10).
            var type = MakeType(movement: MovementKind.Arc, speed: 10f, gravity: -10f);
            type.OnSurface.Add(new Ricochet());

            var s = SpawnForward(speed: 10f);
            s.Age = 1f; // one second of fall (OriginAge is 0 from Spawn) so velocity has dropped

            // Wall facing back along -Z (normal = (0,0,-1)).
            var hit = new WorldHit { Point = new Vector3(0f, -5f, 10f), Normal = Vector3.back, PlayerId = -1 };
            var effects = new List<ProjectileEffect>();

            ProjectileState result = ProjectileStepProcessor.ResolveWorldHit(s, type, in hit, effects);

            // Reflect((0,-10,10), (0,0,-1)) = (0,-10,-10) -> normalized has a clearly DOWNWARD component.
            // Had the processor reflected the spawn velocity (0,0,10) instead, the bounce would be flat (y == 0).
            Assert.Less(result.OriginForward.y, -0.1f,
                "the ricochet must reflect the gravity-bent velocity (downward), proving VelocityAt fed the reaction");
            Assert.Less(result.OriginForward.z, -0.1f, "and reverse along Z off the wall");
            Assert.AreEqual(hit.Point, result.OriginPos, "ricochet re-segments at the impact point");
        }

        // ------------------------------------------------------------------
        // Two-channel order of operations within one tick. The shell resolves the WORLD channel first;
        // if that kills the projectile it must NOT then resolve the PROJECTILE channel. Compose the two
        // processor calls the way ServerTick chains them and assert the dead state makes the second a no-op.
        // ------------------------------------------------------------------

        [Test]
        public void WorldHitThatKills_SuppressesProjectileChannel_SameTick()
        {
            var type = MakeType();
            type.OnSurface.Add(new LoseHealth { Amount = 99 });           // world hit kills
            type.OnProjectile.Add(new LoseHealth { Amount = 1 });         // would fire if reached

            var s = SpawnForward(ownerId: 10, id: 1, health: 3);
            var effects = new List<ProjectileEffect>();

            // World channel: a surface hit that kills.
            var surface = new WorldHit { Point = new Vector3(0f, 0f, 5f), Normal = Vector3.back, PlayerId = -1 };
            s = ProjectileStepProcessor.ResolveWorldHit(s, type, in surface, effects);
            Assert.IsFalse(s.Alive, "precondition: the world hit killed the projectile");

            // Projectile channel, same tick: a real different-owner overlap is present...
            var other = SpawnForward(ownerId: 20, id: 2);
            int healthBefore = s.Health;
            s = ProjectileStepProcessor.ResolveProjectileHits(s, type,
                new System.ReadOnlySpan<ProjectileState>(new[] { other }), effects);

            // ...but a dead projectile must not resolve it.
            Assert.AreEqual(healthBefore, s.Health,
                "once the world channel kills the projectile, the projectile channel must be a no-op for that tick");
            Assert.AreEqual(0, effects.FindAll(e => e.Kind == EffectKind.Damage).Count,
                "no projectile-channel effects after death");
        }
    }
}
