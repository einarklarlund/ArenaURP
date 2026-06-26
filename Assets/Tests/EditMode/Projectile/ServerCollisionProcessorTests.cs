using Arena.NetworkedMotion;
using Arena.ServerCollision;
using NUnit.Framework;
using UnityEngine;

namespace Arena.Tests.ServerCollision
{
    /// <summary>
    /// EditMode tests for the pure collision classifier: owner-safety, enemy/surface routing, the
    /// cross-projectile owner filter + one-shot-on-enter guard. Engine-free.
    /// </summary>
    public class ServerCollisionProcessorTests
    {
        private const int NoGuard = ServerCollisionProcessor.NoGuard;

        private static NetworkedMotionState Mover(int id) =>
            NetworkedMotionState.Spawn(id, Vector3.zero, Vector3.forward, 10f);

        // ---- world channel ----

        [Test]
        public void WorldHit_OnOwnHit_IsIgnored()
        {
            var self = Mover(1);
            var hit = new WorldHit { PlayerId = 5, Point = Vector3.forward, Normal = -Vector3.forward };
            Assert.IsFalse(ServerCollisionProcessor.TryClassifyWorldHit(in self, selfOwnerId: 5, Vector3.forward * 10f, in hit, out _));
        }

        [Test]
        public void WorldHit_PlayerIsEnemy_NonPlayerIsSurface()
        {
            var self = Mover(1);

            var enemy = new WorldHit { PlayerId = 7, Point = Vector3.one, Normal = Vector3.up };
            Assert.IsTrue(ServerCollisionProcessor.TryClassifyWorldHit(in self, 5, Vector3.forward * 10f, in enemy, out var ei));
            Assert.AreEqual(HitCategory.Enemy, ei.Category);
            Assert.AreEqual(7, ei.OtherPlayerId);

            var surface = new WorldHit { PlayerId = -1, Point = Vector3.one, Normal = Vector3.up };
            Assert.IsTrue(ServerCollisionProcessor.TryClassifyWorldHit(in self, 5, Vector3.forward * 10f, in surface, out var si));
            Assert.AreEqual(HitCategory.Surface, si.Category);
        }

        // ---- projectile channel ----

        [Test]
        public void ProjectileHit_SameOwner_Skipped()
        {
            var self = Mover(1);
            var other = Mover(2);
            Assert.IsFalse(ServerCollisionProcessor.TryClassifyProjectileHit(
                in self, selfOwnerId: 9, Vector3.zero, Vector3.forward * 10f, in other, otherOwnerId: 9, NoGuard, out _, out _));
        }

        [Test]
        public void ProjectileHit_Self_Skipped()
        {
            var self = Mover(1);
            var other = Mover(1); // same Id
            Assert.IsFalse(ServerCollisionProcessor.TryClassifyProjectileHit(
                in self, 1, Vector3.zero, Vector3.forward * 10f, in other, otherOwnerId: 2, NoGuard, out _, out _));
        }

        [Test]
        public void ProjectileHit_DifferentOwner_Classified()
        {
            var self = Mover(1);
            var other = Mover(2);
            Assert.IsTrue(ServerCollisionProcessor.TryClassifyProjectileHit(
                in self, 1, Vector3.zero, Vector3.forward * 10f, in other, otherOwnerId: 2, NoGuard, out var info, out _));
            Assert.AreEqual(HitCategory.Projectile, info.Category);
            Assert.AreEqual(2, info.OtherProjectileId);
            Assert.AreEqual(2, info.OtherOwnerId);
        }

        [Test]
        public void ProjectileHit_GuardedId_StaysSuppressed()
        {
            var self = Mover(1);
            var other = Mover(2);
            bool react = ServerCollisionProcessor.TryClassifyProjectileHit(
                in self, 1, Vector3.zero, Vector3.forward * 10f, in other, otherOwnerId: 2, guard: 2, out _, out bool stillGuarded);
            Assert.IsFalse(react);
            Assert.IsTrue(stillGuarded);
        }

        [Test]
        public void ResolveGuard_PrefersHandled_ElseKeepsWhileOverlapping_ElseClears()
        {
            Assert.AreEqual(2, ServerCollisionProcessor.ResolveGuard(currentGuard: 5, firstHandled: 2, stillGuarded: true));
            Assert.AreEqual(5, ServerCollisionProcessor.ResolveGuard(currentGuard: 5, firstHandled: NoGuard, stillGuarded: true));
            Assert.AreEqual(NoGuard, ServerCollisionProcessor.ResolveGuard(currentGuard: 5, firstHandled: NoGuard, stillGuarded: false));
        }
    }
}
