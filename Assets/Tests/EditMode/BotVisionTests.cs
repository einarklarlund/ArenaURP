using NUnit.Framework;
using UnityEngine;

namespace Arena.Tests
{
    /// <summary>
    /// EditMode unit tests for BotVisionMath.IsInFieldOfView.
    ///
    /// Coverage: range boundary, FOV angle boundary (inside / outside / exactly on edge),
    /// epsilon tolerance, behind-observer, and degenerate zero-distance cases.
    /// </summary>
    public sealed class BotVisionTests
    {
        // Convenience: observer always at origin looking down +Z.
        private static readonly Vector3 Origin = Vector3.zero;
        private static readonly Vector3 Forward = Vector3.forward;

        // Range

        [Test]
        public void Target_WithinRange_DirectlyAhead_IsVisible()
        {
            Assert.IsTrue(BotVisionMath.IsInFieldOfView(
                Origin, Forward,
                new Vector3(0f, 0f, 5f),
                fovDegrees: 90f, maxRange: 20f));
        }

        [Test]
        public void Target_BeyondRange_IsNotVisible()
        {
            Assert.IsFalse(BotVisionMath.IsInFieldOfView(
                Origin, Forward,
                new Vector3(0f, 0f, 25f),
                fovDegrees: 90f, maxRange: 20f));
        }

        [Test]
        public void Target_AtExactlyMaxRange_IsVisible()
        {
            Assert.IsTrue(BotVisionMath.IsInFieldOfView(
                Origin, Forward,
                new Vector3(0f, 0f, 20f),
                fovDegrees: 90f, maxRange: 20f));
        }

        // FOV angle

        [Test]
        public void Target_InsideFovHalfAngle_IsVisible()
        {
            // 44 degrees < 45 degree half-angle of a 90 degree FOV.
            Vector3 targetPos = Quaternion.Euler(0f, 44f, 0f) * Vector3.forward * 10f;
            Assert.IsTrue(BotVisionMath.IsInFieldOfView(
                Origin, Forward,
                targetPos,
                fovDegrees: 90f, maxRange: 20f));
        }

        [Test]
        public void Target_AtExactFovHalfAngle_IsVisible()
        {
            // Exactly 45 degrees. Floating-point error in Quaternion.Euler * Vector3
            // can push the computed angle slightly above the mathematical boundary.
            // The epsilon in IsInFieldOfView absorbs this.
            Vector3 targetPos = Quaternion.Euler(0f, 45f, 0f) * Vector3.forward * 10f;
            Assert.IsTrue(BotVisionMath.IsInFieldOfView(
                Origin, Forward,
                targetPos,
                fovDegrees: 90f, maxRange: 20f));
        }

        [Test]
        public void Target_OutsideFovHalfAngle_IsNotVisible()
        {
            // 46 degrees > 45 degree half-angle.
            Vector3 targetPos = Quaternion.Euler(0f, 46f, 0f) * Vector3.forward * 10f;
            Assert.IsFalse(BotVisionMath.IsInFieldOfView(
                Origin, Forward,
                targetPos,
                fovDegrees: 90f, maxRange: 20f));
        }

        // Epsilon boundary

        [Test]
        public void Target_WithinEpsilonBeyondHalfAngle_IsStillVisible()
        {
            // A target whose true angle is exactly half-angle + (epsilon / 2) should
            // still pass thanks to the tolerance. This verifies the epsilon is working.
            float halfAngle = 45f;
            float justInsideEpsilon = halfAngle + BotVisionMath.AngleEpsilon * 0.5f;
            Vector3 targetPos = Quaternion.Euler(0f, justInsideEpsilon, 0f) * Vector3.forward * 10f;
            Assert.IsTrue(BotVisionMath.IsInFieldOfView(
                Origin, Forward,
                targetPos,
                fovDegrees: 90f, maxRange: 20f));
        }

        [Test]
        public void Target_WellBeyondEpsilon_IsNotVisible()
        {
            // A target 1 degree beyond the half-angle is well outside the epsilon
            // tolerance and should be rejected. This verifies the epsilon doesn't
            // meaningfully widen the FOV.
            float halfAngle = 45f;
            float wellBeyond = halfAngle + 1f;
            Vector3 targetPos = Quaternion.Euler(0f, wellBeyond, 0f) * Vector3.forward * 10f;
            Assert.IsFalse(BotVisionMath.IsInFieldOfView(
                Origin, Forward,
                targetPos,
                fovDegrees: 90f, maxRange: 20f));
        }

        // Behind observer

        [Test]
        public void Target_DirectlyBehindObserver_IsNotVisible()
        {
            Assert.IsFalse(BotVisionMath.IsInFieldOfView(
                Origin, Forward,
                new Vector3(0f, 0f, -5f),
                fovDegrees: 90f, maxRange: 20f));
        }

        [Test]
        public void Target_DirectlyBehind_WideAngleFov_StillNotVisible()
        {
            // A 350 degree FOV has a half-angle of 175 degrees; 180 degrees > 175.
            Assert.IsFalse(BotVisionMath.IsInFieldOfView(
                Origin, Forward,
                new Vector3(0f, 0f, -5f),
                fovDegrees: 350f, maxRange: 20f));
        }

        [Test]
        public void Target_DirectlyBehind_FullCircleFov_IsVisible()
        {
            // A 360 degree FOV has half-angle 180 degrees; everything is within view.
            Assert.IsTrue(BotVisionMath.IsInFieldOfView(
                Origin, Forward,
                new Vector3(0f, 0f, -5f),
                fovDegrees: 360f, maxRange: 20f));
        }

        // Degenerate

        [Test]
        public void Target_AtSamePositionAsObserver_IsVisible()
        {
            Assert.IsTrue(BotVisionMath.IsInFieldOfView(
                Origin, Forward,
                Origin,
                fovDegrees: 90f, maxRange: 20f));
        }

        // Observer direction

        [Test]
        public void ObserverFacingRight_TargetToTheRight_IsVisible()
        {
            Assert.IsTrue(BotVisionMath.IsInFieldOfView(
                Origin, Vector3.right,
                new Vector3(5f, 0f, 0f),
                fovDegrees: 90f, maxRange: 20f));
        }

        [Test]
        public void ObserverFacingRight_TargetAhead_IsNotVisible()
        {
            // Observer facing +X; target at +Z is 90 degrees off-axis.
            Assert.IsFalse(BotVisionMath.IsInFieldOfView(
                Origin, Vector3.right,
                new Vector3(0f, 0f, 5f),
                fovDegrees: 90f, maxRange: 20f));
        }
    }
}
