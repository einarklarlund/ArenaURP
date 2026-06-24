using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Arena.Projectile;

namespace Arena.Tests.Projectile
{
    /// <summary>
    /// EditMode tests for the pure movement strategies (Linear/Arc/Laser).
    /// Strategies are stateless and engine-free; all trajectory state lives in ProjectileState.
    /// </summary>
    [TestFixture]
    public class ProjectileMovementTests
    {
        private const float Tol = 1e-4f;

        private readonly List<ProjectileType> _types = new();

        private ProjectileType MakeType(
            MovementKind movement = MovementKind.Linear,
            float speed = 50f,
            float acceleration = 0f,
            float gravity = -9.81f,
            float lifetime = 5f)
        {
            var t = ScriptableObject.CreateInstance<ProjectileType>();
            t.Movement = movement;
            t.Speed = speed;
            t.Acceleration = acceleration;
            t.Gravity = gravity;
            t.Lifetime = lifetime;
            _types.Add(t);
            return t;
        }

        /// <summary>A state on a single segment whose elapsed time equals <paramref name="elapsed"/>.</summary>
        private static ProjectileState SegmentState(Vector3 pos, Vector3 forward, float speed, float elapsed)
        {
            var s = ProjectileState.Spawn(ownerId: 1, id: 1, pos, forward, speed, health: 1);
            s.OriginAge = 0f;
            s.Age = elapsed; // SegmentElapsed = Age - OriginAge = elapsed
            return s;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var t in _types)
                if (t != null)
                    Object.DestroyImmediate(t);
            _types.Clear();
        }

        // ------------------------------------------------------------------
        // Linear
        // ------------------------------------------------------------------

        [Test]
        public void Linear_PositionAt_IncludesAccelerationTerm()
        {
            var type = MakeType(MovementKind.Linear, speed: 10f, acceleration: 4f);
            var s = SegmentState(new Vector3(1f, 2f, 3f), Vector3.forward, speed: 10f, elapsed: 2f);

            // pos = origin + fwd * (speed*e + 0.5*acc*e^2) = (1,2,3) + (0,0,1)*(20 + 8) = (1,2,31)
            Vector3 pos = new LinearMovement().PositionAt(in s, in type, s.SegmentElapsed);

            Assert.AreEqual(1f, pos.x, Tol);
            Assert.AreEqual(2f, pos.y, Tol);
            Assert.AreEqual(31f, pos.z, Tol);
        }

        [Test]
        public void Linear_VelocityAt_IncludesAccelerationTerm()
        {
            var type = MakeType(MovementKind.Linear, speed: 10f, acceleration: 4f);
            var s = SegmentState(Vector3.zero, Vector3.forward, speed: 10f, elapsed: 2f);

            // vel = fwd * (speed + acc*e) = (0,0,1)*(10 + 8) = (0,0,18)
            Vector3 vel = new LinearMovement().VelocityAt(in s, in type, s.SegmentElapsed);

            Assert.AreEqual(0f, vel.x, Tol);
            Assert.AreEqual(0f, vel.y, Tol);
            Assert.AreEqual(18f, vel.z, Tol);
        }

        [Test]
        public void Linear_ProcessorHelpers_MatchStrategy()
        {
            var type = MakeType(MovementKind.Linear, speed: 10f, acceleration: 4f);
            var s = SegmentState(new Vector3(1f, 2f, 3f), Vector3.forward, speed: 10f, elapsed: 2f);

            Vector3 pos = ProjectileStepProcessor.PositionAt(in s, type);
            Vector3 vel = ProjectileStepProcessor.VelocityAt(in s, type);

            Assert.AreEqual(31f, pos.z, Tol);
            Assert.AreEqual(18f, vel.z, Tol);
        }

        // ------------------------------------------------------------------
        // Arc
        // ------------------------------------------------------------------

        [Test]
        public void Arc_PositionAt_AddsGravityOverTime()
        {
            var type = MakeType(MovementKind.Arc, speed: 10f, gravity: -9.81f);
            var s = SegmentState(Vector3.zero, Vector3.forward, speed: 10f, elapsed: 2f);

            // pos = origin + fwd*(speed*e) + 0.5*g*e^2*up = (0,0,20) + (0, 0.5*-9.81*4, 0)
            Vector3 pos = new ArcMovement().PositionAt(in s, in type, s.SegmentElapsed);

            Assert.AreEqual(0f, pos.x, Tol);
            Assert.AreEqual(0.5f * -9.81f * 4f, pos.y, Tol); // -19.62
            Assert.AreEqual(20f, pos.z, Tol);
        }

        [Test]
        public void Arc_VelocityAt_AddsGravityOverTime()
        {
            var type = MakeType(MovementKind.Arc, speed: 10f, gravity: -9.81f);
            var s = SegmentState(Vector3.zero, Vector3.forward, speed: 10f, elapsed: 2f);

            // vel = fwd*speed + g*e*up = (0,0,10) + (0, -9.81*2, 0)
            Vector3 vel = new ArcMovement().VelocityAt(in s, in type, s.SegmentElapsed);

            Assert.AreEqual(0f, vel.x, Tol);
            Assert.AreEqual(-9.81f * 2f, vel.y, Tol); // -19.62
            Assert.AreEqual(10f, vel.z, Tol);
        }

        // ------------------------------------------------------------------
        // Laser
        // ------------------------------------------------------------------

        [Test]
        public void Laser_PositionAt_IsOriginAndVelocityIsZero()
        {
            var type = MakeType(MovementKind.Laser, speed: 0f);
            var origin = new Vector3(4f, 5f, 6f);
            var s = SegmentState(origin, Vector3.forward, speed: 0f, elapsed: 3f);

            Vector3 pos = new LaserMovement().PositionAt(in s, in type, s.SegmentElapsed);
            Vector3 vel = new LaserMovement().VelocityAt(in s, in type, s.SegmentElapsed);

            Assert.AreEqual(origin.x, pos.x, Tol);
            Assert.AreEqual(origin.y, pos.y, Tol);
            Assert.AreEqual(origin.z, pos.z, Tol);
            Assert.AreEqual(0f, vel.magnitude, Tol);
        }
    }
}
