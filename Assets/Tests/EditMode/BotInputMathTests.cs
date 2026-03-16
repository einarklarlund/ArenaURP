using NUnit.Framework;
using UnityEngine;

namespace Arena.Tests
{
    /// <summary>
    /// EditMode unit tests for BotInputMath static helpers.
    /// All tests use explicit transform values (position, rotation, euler angles)
    /// instead of actual Transform components, so no scene or MonoBehaviour is needed.
    /// </summary>
    public sealed class BotInputMathTests
    {
        private const float Tolerance = 0.01f;

        // ComputeMoveInput

        [Test]
        public void MoveInput_ZeroDirection_ReturnsZero()
        {
            var result = BotInputMath.ComputeMoveInput(Vector3.zero, Quaternion.identity);
            Assert.AreEqual(Vector2.zero, result);
        }

        [Test]
        public void MoveInput_WorldForward_PawnFacingForward_ReturnsPosY()
        {
            // Pawn faces +Z, intent is +Z => local forward => (0, 1)
            var result = BotInputMath.ComputeMoveInput(Vector3.forward, Quaternion.identity);
            Assert.AreEqual(0f, result.x, Tolerance);
            Assert.AreEqual(1f, result.y, Tolerance);
        }

        [Test]
        public void MoveInput_WorldRight_PawnFacingForward_ReturnsPosX()
        {
            // Pawn faces +Z, intent is +X => local right => (1, 0)
            var result = BotInputMath.ComputeMoveInput(Vector3.right, Quaternion.identity);
            Assert.AreEqual(1f, result.x, Tolerance);
            Assert.AreEqual(0f, result.y, Tolerance);
        }

        [Test]
        public void MoveInput_WorldBackward_PawnFacingForward_ReturnsNegY()
        {
            var result = BotInputMath.ComputeMoveInput(Vector3.back, Quaternion.identity);
            Assert.AreEqual(0f, result.x, Tolerance);
            Assert.AreEqual(-1f, result.y, Tolerance);
        }

        [Test]
        public void MoveInput_WorldForward_PawnRotated90Right_ReturnsLocalLeft()
        {
            // Pawn faces +X (rotated 90 around Y). World +Z is now pawn's left (-X local).
            Quaternion pawnRotation = Quaternion.Euler(0f, 90f, 0f);
            var result = BotInputMath.ComputeMoveInput(Vector3.forward, pawnRotation);
            Assert.AreEqual(-1f, result.x, Tolerance);
            Assert.AreEqual(0f, result.y, Tolerance);
        }

        [Test]
        public void MoveInput_WorldRight_PawnRotated90Right_ReturnsLocalForward()
        {
            // Pawn faces +X. World +X is pawn's forward.
            Quaternion pawnRotation = Quaternion.Euler(0f, 90f, 0f);
            var result = BotInputMath.ComputeMoveInput(Vector3.right, pawnRotation);
            Assert.AreEqual(0f, result.x, Tolerance);
            Assert.AreEqual(1f, result.y, Tolerance);
        }

        [Test]
        public void MoveInput_DiagonalDirection_ClampedToMagnitudeOne()
        {
            // A large diagonal input should be clamped to magnitude 1.
            var result = BotInputMath.ComputeMoveInput(new Vector3(10f, 0f, 10f), Quaternion.identity);
            Assert.LessOrEqual(result.magnitude, 1f + Tolerance);
        }

        // ComputeLookInput

        [Test]
        public void LookInput_TargetDirectlyAhead_YawDeltaNearZero()
        {
            var result = BotInputMath.ComputeLookInput(
                pawnPosition: Vector3.zero,
                pawnEulerY: 0f,
                currentPitch: 0f,
                targetPosition: new Vector3(0f, 0f, 10f),
                hasLookTarget: true,
                maxTurnSpeed: 360f,
                maxPitch: 60f,
                deltaTime: 0.016f);

            Assert.AreEqual(0f, result.LookDelta.x, Tolerance);
        }

        [Test]
        public void LookInput_TargetToTheRight_PositiveYawDelta()
        {
            // Target at +X from a pawn facing +Z. Yaw should turn right (positive).
            var result = BotInputMath.ComputeLookInput(
                pawnPosition: Vector3.zero,
                pawnEulerY: 0f,
                currentPitch: 0f,
                targetPosition: new Vector3(10f, 0f, 0f),
                hasLookTarget: true,
                maxTurnSpeed: 360f,
                maxPitch: 60f,
                deltaTime: 0.016f);

            Assert.Greater(result.LookDelta.x, 0f);
        }

        [Test]
        public void LookInput_TargetToTheLeft_NegativeYawDelta()
        {
            var result = BotInputMath.ComputeLookInput(
                pawnPosition: Vector3.zero,
                pawnEulerY: 0f,
                currentPitch: 0f,
                targetPosition: new Vector3(-10f, 0f, 0f),
                hasLookTarget: true,
                maxTurnSpeed: 360f,
                maxPitch: 60f,
                deltaTime: 0.016f);

            Assert.Less(result.LookDelta.x, 0f);
        }

        [Test]
        public void LookInput_TargetBehind_YawClampedByTurnSpeed()
        {
            // Target is directly behind (180 degrees). With a low turn speed and
            // small delta time, the yaw delta should be clamped well below 180.
            float maxTurnSpeed = 90f;
            float deltaTime = 0.1f; // max delta = 9 degrees
            var result = BotInputMath.ComputeLookInput(
                pawnPosition: Vector3.zero,
                pawnEulerY: 0f,
                currentPitch: 0f,
                targetPosition: new Vector3(0f, 0f, -10f),
                hasLookTarget: true,
                maxTurnSpeed: maxTurnSpeed,
                maxPitch: 60f,
                deltaTime: deltaTime);

            float maxDelta = maxTurnSpeed * deltaTime;
            Assert.LessOrEqual(Mathf.Abs(result.LookDelta.x), maxDelta + Tolerance);
        }

        [Test]
        public void LookInput_ElevatedTarget_ProducesNegativePitchDelta()
        {
            // Target above the pawn. Pitch convention: negative = look up.
            var result = BotInputMath.ComputeLookInput(
                pawnPosition: Vector3.zero,
                pawnEulerY: 0f,
                currentPitch: 0f,
                targetPosition: new Vector3(0f, 10f, 10f),
                hasLookTarget: true,
                maxTurnSpeed: 360f,
                maxPitch: 60f,
                deltaTime: 0.5f);

            Assert.Less(result.LookDelta.y, 0f);
        }

        [Test]
        public void LookInput_TargetBelow_ProducesPositivePitchDelta()
        {
            var result = BotInputMath.ComputeLookInput(
                pawnPosition: Vector3.zero,
                pawnEulerY: 0f,
                currentPitch: 0f,
                targetPosition: new Vector3(0f, -10f, 10f),
                hasLookTarget: true,
                maxTurnSpeed: 360f,
                maxPitch: 60f,
                deltaTime: 0.5f);

            Assert.Greater(result.LookDelta.y, 0f);
        }

        [Test]
        public void LookInput_PitchClampedToMaxPitch()
        {
            // Target steeply above. Even with a high turn speed, accumulated pitch
            // should not exceed maxPitch.
            float maxPitch = 30f;
            var result = BotInputMath.ComputeLookInput(
                pawnPosition: Vector3.zero,
                pawnEulerY: 0f,
                currentPitch: 0f,
                targetPosition: new Vector3(0f, 100f, 1f), // nearly straight up
                hasLookTarget: true,
                maxTurnSpeed: 9999f,
                maxPitch: maxPitch,
                deltaTime: 1f);

            Assert.LessOrEqual(Mathf.Abs(result.UpdatedPitch), maxPitch + Tolerance);
        }

        [Test]
        public void LookInput_NoLookTarget_PitchReturnsToZero()
        {
            // With no look target, desired pitch is 0. If current pitch is non-zero,
            // the delta should push it back toward zero.
            float startPitch = 20f;
            var result = BotInputMath.ComputeLookInput(
                pawnPosition: Vector3.zero,
                pawnEulerY: 0f,
                currentPitch: startPitch,
                targetPosition: new Vector3(0f, 0f, 10f), // doesn't matter, hasLookTarget is false
                hasLookTarget: false,
                maxTurnSpeed: 360f,
                maxPitch: 60f,
                deltaTime: 1f);

            // Pitch should have decreased (moved toward zero).
            Assert.Less(Mathf.Abs(result.UpdatedPitch), Mathf.Abs(startPitch));
        }

        [Test]
        public void LookInput_UpdatedPitch_AccumulatesAcrossFrames()
        {
            // Simulate two frames looking at an elevated target.
            // The second call should start from the first call's UpdatedPitch.
            Vector3 target = new Vector3(0f, 5f, 10f);
            float deltaTime = 0.1f;
            float maxTurnSpeed = 90f;

            var frame1 = BotInputMath.ComputeLookInput(
                Vector3.zero, 0f,
                currentPitch: 0f,
                target, true,
                maxTurnSpeed, 60f, deltaTime);

            var frame2 = BotInputMath.ComputeLookInput(
                Vector3.zero, 0f,
                currentPitch: frame1.UpdatedPitch,
                target, true,
                maxTurnSpeed, 60f, deltaTime);

            // After two frames, accumulated pitch should be further from zero
            // than after one frame (still converging toward the target).
            Assert.Less(Mathf.Abs(frame1.UpdatedPitch), Mathf.Abs(frame2.UpdatedPitch));
        }

        [Test]
        public void LookInput_PawnRotated_YawDeltaAccountsForCurrentHeading()
        {
            // Pawn faces +X (eulerY = 90). Target at +X should produce near-zero yaw.
            var result = BotInputMath.ComputeLookInput(
                pawnPosition: Vector3.zero,
                pawnEulerY: 90f,
                currentPitch: 0f,
                targetPosition: new Vector3(10f, 0f, 0f),
                hasLookTarget: true,
                maxTurnSpeed: 360f,
                maxPitch: 60f,
                deltaTime: 0.016f);

            Assert.AreEqual(0f, result.LookDelta.x, Tolerance);
        }
    }
}
