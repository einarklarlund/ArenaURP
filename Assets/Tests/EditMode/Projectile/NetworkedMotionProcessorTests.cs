using Arena.NetworkedMotion;
using NUnit.Framework;
using UnityEngine;

namespace Arena.Tests.NetworkedMotion
{
    /// <summary>
    /// EditMode tests for the pure closed-form motion core (Linear/Arc/Laser). Engine-free: just structs.
    /// </summary>
    public class NetworkedMotionProcessorTests
    {
        private const float Tol = 1e-4f;

        /// <summary>A state on its first segment whose elapsed time equals <paramref name="elapsed"/>.</summary>
        private static NetworkedMotionState Segment(Vector3 pos, Vector3 forward, float speed, float elapsed)
        {
            var s = NetworkedMotionState.Spawn(id: 1, pos, forward, speed);
            s.Age = elapsed; // OriginAge is 0, so SegmentElapsed == elapsed
            return s;
        }

        [Test]
        public void Linear_IncludesAccelerationTerm()
        {
            var c = new MotionConfig(MotionKind.Linear, acceleration: 4f, gravity: 0f, lifetime: 5f, maxRange: 0f);
            var s = Segment(Vector3.zero, Vector3.forward, 10f, 2f);
            // pos = v*t + 0.5*a*t^2 = 20 + 8 = 28 ; vel = v + a*t = 18
            Assert.AreEqual(28f, NetworkedMotionProcessor.PositionAt(in s, in c).z, Tol);
            Assert.AreEqual(18f, NetworkedMotionProcessor.VelocityAt(in s, in c).z, Tol);
        }

        [Test]
        public void Arc_AppliesGravityOverTime()
        {
            var c = new MotionConfig(MotionKind.Arc, acceleration: 0f, gravity: -10f, lifetime: 5f, maxRange: 0f);
            var s = Segment(Vector3.zero, Vector3.forward, 10f, 2f);
            Vector3 p = NetworkedMotionProcessor.PositionAt(in s, in c);
            Assert.AreEqual(20f, p.z, Tol);              // forward cruise
            Assert.AreEqual(0.5f * -10f * 4f, p.y, Tol); // 0.5*g*t^2 = -20
            Assert.AreEqual(-20f, NetworkedMotionProcessor.VelocityAt(in s, in c).y, Tol); // g*t
        }

        [Test]
        public void Laser_IsStationary()
        {
            var c = new MotionConfig(MotionKind.Laser, 0f, 0f, 5f, 100f);
            var s = Segment(new Vector3(1, 2, 3), Vector3.forward, 0f, 3f);
            Assert.AreEqual(new Vector3(1, 2, 3), NetworkedMotionProcessor.PositionAt(in s, in c));
            Assert.AreEqual(Vector3.zero, NetworkedMotionProcessor.VelocityAt(in s, in c));
        }

        [Test]
        public void PositionAt_ExplicitElapsed_Matches()
        {
            var c = new MotionConfig(MotionKind.Linear, 0f, 0f, 5f, 0f);
            var s = NetworkedMotionState.Spawn(1, Vector3.zero, Vector3.forward, 5f);
            Assert.AreEqual(15f, NetworkedMotionProcessor.PositionAt(in s, in c, elapsed: 3f).z, Tol);
        }
    }
}
