using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Arena.Ability;
using Arena.Projectile;

namespace Arena.Tests.Ability
{
    /// <summary>
    /// EditMode unit tests for ability effects. Build an <see cref="AbilityContext"/> + empty buffer,
    /// call Collect, and assert on the emitted shared <see cref="WorldEffect"/> values — the same shape
    /// the projectile reaction tests assert on, since the vocabulary is shared.
    /// </summary>
    [TestFixture]
    public class AbilityEffectTests
    {
        private const float Tol = 1e-3f;
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

        private static AbilityContext Ctx(Vector3 aim) => new()
        {
            CasterPlayerId = 7,
            Origin = new Vector3(1f, 2f, 3f),
            AimDirection = aim,
            AimPoint = Vector3.zero,
        };

        [Test]
        public void SpawnProjectiles_EmitsOneSpawnPerProjectile()
        {
            var effect = new SpawnProjectilesEffect { Type = MakeType(), Count = 8, SpreadDegrees = 360f };
            var buffer = new List<WorldEffect>();

            effect.Collect(Ctx(Vector3.forward), buffer);

            Assert.AreEqual(8, buffer.Count);
            foreach (var e in buffer)
            {
                Assert.AreEqual(EffectKind.Spawn, e.Kind);
                Assert.AreEqual(1, e.Count);
                Assert.AreEqual(7, e.AttackerPlayerId);     // caster id flows through as spawn owner
                Assert.AreEqual(new Vector3(1f, 2f, 3f), e.Point);
                Assert.AreSame(effect.Type, e.Type);
            }
        }

        [Test]
        public void SpawnProjectiles_FullRing_DirectionsAreEvenlySpaced()
        {
            var effect = new SpawnProjectilesEffect { Type = MakeType(), Count = 4, SpreadDegrees = 360f };
            var buffer = new List<WorldEffect>();

            effect.Collect(Ctx(Vector3.forward), buffer);

            // 4 projectiles over 360° -> forward, right, back, left (90° apart, around +Y).
            Assert.AreEqual(4, buffer.Count);
            Assert.That(Vector3.Angle(buffer[0].Dir, Vector3.forward), Is.LessThan(0.01f));
            Assert.That(Vector3.Angle(buffer[1].Dir, Vector3.right), Is.LessThan(0.01f));
            Assert.That(Vector3.Angle(buffer[2].Dir, Vector3.back), Is.LessThan(0.01f));
            Assert.That(Vector3.Angle(buffer[3].Dir, Vector3.left), Is.LessThan(0.01f));
            foreach (var e in buffer)
                Assert.That(e.Dir.magnitude, Is.EqualTo(1f).Within(Tol)); // unit directions
        }

        [Test]
        public void SpawnProjectiles_Arc_IsCentredOnAim()
        {
            var effect = new SpawnProjectilesEffect { Type = MakeType(), Count = 3, SpreadDegrees = 90f };
            var buffer = new List<WorldEffect>();

            effect.Collect(Ctx(Vector3.forward), buffer);

            // 3 over a 90° arc centred on forward -> -45°, 0°, +45° around +Y. Middle one is forward.
            Assert.AreEqual(3, buffer.Count);
            Assert.That(Vector3.Angle(buffer[1].Dir, Vector3.forward), Is.LessThan(0.01f));
            Assert.That(Vector3.Angle(buffer[0].Dir, Vector3.forward), Is.EqualTo(45f).Within(0.01f));
            Assert.That(Vector3.Angle(buffer[2].Dir, Vector3.forward), Is.EqualTo(45f).Within(0.01f));
        }

        [Test]
        public void SpawnProjectiles_NullTypeOrZeroCount_EmitsNothing()
        {
            var buffer = new List<WorldEffect>();
            new SpawnProjectilesEffect { Type = null, Count = 8 }.Collect(Ctx(Vector3.forward), buffer);
            new SpawnProjectilesEffect { Type = MakeType(), Count = 0 }.Collect(Ctx(Vector3.forward), buffer);
            Assert.AreEqual(0, buffer.Count);
        }
    }
}
