using Arena.NetworkedMotion;
using NUnit.Framework;
using UnityEngine;

namespace Arena.Tests.NetworkedMotion
{
    /// <summary>
    /// EditMode tests for the pure closed-form motion strategies (Linear/Arc/Laser). Engine-free: just
    /// structs. Each strategy now carries its own tuning and is evaluated directly (no MotionKind/selector).
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
            var m = new LinearMotion(acceleration: 4f);
            var s = Segment(Vector3.zero, Vector3.forward, 10f, 2f);
            // pos = v*t + 0.5*a*t^2 = 20 + 8 = 28 ; vel = v + a*t = 18
            Assert.AreEqual(28f, NetworkedMotionProcessor.PositionAt(m, in s).z, Tol);
            Assert.AreEqual(18f, NetworkedMotionProcessor.VelocityAt(m, in s).z, Tol);
        }

        [Test]
        public void Arc_AppliesGravityOverTime()
        {
            var m = new ArcMotion(gravity: -10f);
            var s = Segment(Vector3.zero, Vector3.forward, 10f, 2f);
            Vector3 p = NetworkedMotionProcessor.PositionAt(m, in s);
            Assert.AreEqual(20f, p.z, Tol);              // forward cruise
            Assert.AreEqual(0.5f * -10f * 4f, p.y, Tol); // 0.5*g*t^2 = -20
            Assert.AreEqual(-20f, NetworkedMotionProcessor.VelocityAt(m, in s).y, Tol); // g*t
        }

        [Test]
        public void Laser_IsStationary_AndSweepsForwardOverRange()
        {
            var m = new LaserMotion(maxRange: 100f);
            var s = Segment(new Vector3(1, 2, 3), Vector3.forward, 0f, 3f);
            Assert.AreEqual(new Vector3(1, 2, 3), NetworkedMotionProcessor.PositionAt(m, in s));
            Assert.AreEqual(Vector3.zero, NetworkedMotionProcessor.VelocityAt(m, in s));
            // The old `if (Movement == Laser)` cast branch now lives here: cast forward from its position.
            MotionSweep sweep = m.CollisionSweep(in s, new Vector3(1, 2, 3), new Vector3(1, 2, 3));
            Assert.AreEqual(new Vector3(1, 2, 3), sweep.From);
            Assert.AreEqual(new Vector3(1, 2, 103), sweep.To);
        }

        [Test]
        public void MovingStrategy_CollisionSweep_IsTravelledPath()
        {
            var m = new LinearMotion();
            var s = Segment(Vector3.zero, Vector3.forward, 5f, 1f);
            MotionSweep sweep = m.CollisionSweep(in s, new Vector3(0, 0, 5), new Vector3(0, 0, 10));
            Assert.AreEqual(new Vector3(0, 0, 5), sweep.From);
            Assert.AreEqual(new Vector3(0, 0, 10), sweep.To);
        }

        [Test]
        public void PositionAt_ExplicitElapsed_Matches()
        {
            var m = new LinearMotion();
            var s = NetworkedMotionState.Spawn(1, Vector3.zero, Vector3.forward, 5f);
            Assert.AreEqual(15f, NetworkedMotionProcessor.PositionAt(m, in s, elapsed: 3f).z, Tol);
        }
    }
}
