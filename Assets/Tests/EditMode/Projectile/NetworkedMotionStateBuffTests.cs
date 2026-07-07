using Arena.NetworkedMotion;
using NUnit.Framework;
using UnityEngine;

namespace Arena.Tests.NetworkedMotion
{
    /// <summary>
    /// EditMode tests for <see cref="NetworkedMotionState.WithOriginSpeed"/> — the pure helper behind the
    /// on-spawn speed buff's idempotent setter (NetworkedMotion.SetSpeedMultiplier). Pins the two properties the
    /// buff design relies on: it changes only OriginSpeed, and recomputing OriginSpeed at age 0 is lossless
    /// (position stays the spawn point regardless of speed).
    /// </summary>
    public class NetworkedMotionStateBuffTests
    {
        private const float Tol = 1e-4f;

        [Test]
        public void WithOriginSpeed_SetsSpeed_LeavesEverythingElse()
        {
            var s = NetworkedMotionState.Spawn(id: 7, new Vector3(1f, 2f, 3f), Vector3.right, speed: 50f);
            s.Age = 1.25f; // whole-life age is untouched by the helper

            var buffed = s.WithOriginSpeed(75f);

            Assert.AreEqual(75f, buffed.OriginSpeed, Tol);
            // Baseline × 1.5 arithmetic the setter performs: 50 × 1.5 == 75.
            Assert.AreEqual(50f * 1.5f, buffed.OriginSpeed, Tol);
            Assert.AreEqual(s.Id, buffed.Id);
            Assert.AreEqual(s.OriginPos, buffed.OriginPos);
            Assert.AreEqual(s.OriginForward, buffed.OriginForward);
            Assert.AreEqual(s.OriginAge, buffed.OriginAge, Tol);
            Assert.AreEqual(s.Age, buffed.Age, Tol);
        }

        [Test]
        public void WithOriginSpeed_AtAgeZero_IsPositionLossless()
        {
            var pos = new Vector3(4f, -1f, 2f);
            var s = NetworkedMotionState.Spawn(id: 1, pos, Vector3.forward, speed: 50f);
            // Age is 0 throughout the init phase, where the buff recomputes OriginSpeed in place.
            Assert.AreEqual(0f, s.Age, Tol);

            var m = new LinearMotion();
            Vector3 before = NetworkedMotionProcessor.PositionAt(m, in s);

            var buffed = s.WithOriginSpeed(999f); // any speed
            Vector3 after = NetworkedMotionProcessor.PositionAt(m, in buffed);

            Assert.AreEqual(before, after);        // position at age 0 is the spawn point regardless of speed
            Assert.AreEqual(pos, after);
        }

        [Test]
        public void WithOriginSpeed_ScalesTravelAfterInit()
        {
            var s = NetworkedMotionState.Spawn(id: 1, Vector3.zero, Vector3.forward, speed: 10f);
            var buffed = s.WithOriginSpeed(20f);
            buffed.Age = 2f; // advance after the buff, as a real projectile does

            var m = new LinearMotion();
            // Linear: pos.z == speed × t == 20 × 2 == 40 (double the un-buffed 20).
            Assert.AreEqual(40f, NetworkedMotionProcessor.PositionAt(m, in buffed).z, Tol);
        }
    }
}
