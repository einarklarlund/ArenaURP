using Arena.NetworkedMotion;
using NUnit.Framework;
using UnityEngine;

namespace Arena.Tests.NetworkedMotion
{
    /// <summary>
    /// EditMode tests for the pure trajectory/direction helpers the Unity-coupled reactions delegate to
    /// (ricochet reflect, deflect redirect, spawn-fan distribution).
    /// </summary>
    public class MotionMathTests
    {
        private const float Tol = 1e-4f;

        [Test]
        public void Ricochet_ReflectsAcrossNormal_KeepsSpeed()
        {
            bool ok = RicochetMath.TryReflect(new Vector3(10f, 0f, 0f), new Vector3(-1f, 0f, 0f), out Vector3 dir, out float speed);
            Assert.IsTrue(ok);
            Assert.AreEqual(-1f, dir.x, Tol);
            Assert.AreEqual(10f, speed, Tol);
        }

        [Test]
        public void Ricochet_DegenerateReturnsFalse()
        {
            Assert.IsFalse(RicochetMath.TryReflect(Vector3.zero, Vector3.up, out _, out _));
        }

        [Test]
        public void Deflect_PrefersOtherForward_KeepsIncomingSpeed()
        {
            bool ok = DeflectMath.TryRedirect(otherForward: Vector3.forward, otherVelocity: Vector3.zero,
                incomingSpeed: 8f, fallbackSpeed: 1f, out Vector3 dir, out float speed);
            Assert.IsTrue(ok);
            Assert.AreEqual(Vector3.forward, dir);
            Assert.AreEqual(8f, speed, Tol);
        }

        [Test]
        public void Deflect_FallsBackToVelocityAndFallbackSpeed()
        {
            bool ok = DeflectMath.TryRedirect(Vector3.zero, new Vector3(1f, 0f, 0f),
                incomingSpeed: 0f, fallbackSpeed: 3f, out Vector3 dir, out float speed);
            Assert.IsTrue(ok);
            Assert.AreEqual(Vector3.right, dir);
            Assert.AreEqual(3f, speed, Tol);
        }

        [Test]
        public void SpawnFan_Single_ReturnsBaseDir()
        {
            Assert.AreEqual(Vector3.forward, SpawnFanMath.Direction(Vector3.forward, 0, 1, 360f, Vector3.up));
        }

        [Test]
        public void SpawnFan_FullRing_IsEvenlySpaced()
        {
            Vector3 d0 = SpawnFanMath.Direction(Vector3.forward, 0, 4, 360f, Vector3.up);
            Vector3 d1 = SpawnFanMath.Direction(Vector3.forward, 1, 4, 360f, Vector3.up);
            Assert.AreEqual(Vector3.forward, d0);
            Assert.AreEqual(90f, Vector3.Angle(d0, d1), 1e-2f);
        }
    }
}
