using UnityEngine;

/// <summary>
/// Pure math helpers for bot input computation.
/// </summary>
public static class BotInputMath
{
    /// <summary>
    /// Converts a world-space movement direction into a local-space Vector2
    /// (x = strafe, y = forward) relative to the pawn's orientation.
    /// Returns Vector2.zero if the direction is zero-length.
    /// </summary>
    /// <param name="worldDirection">Desired movement direction in world space.</param>
    /// <param name="pawnRotation">Current rotation of the pawn (typically transform.rotation).</param>
    public static Vector2 ComputeMoveInput(Vector3 worldDirection, Quaternion pawnRotation)
    {
        if (worldDirection == Vector3.zero) return Vector2.zero;

        // InverseTransformDirection without needing a Transform component.
        Vector3 local = Quaternion.Inverse(pawnRotation) * worldDirection.normalized;
        return Vector2.ClampMagnitude(new Vector2(local.x, local.z), 1f);
    }

    /// <summary>
    /// Computes the yaw and pitch deltas needed to aim from the pawn's current
    /// orientation toward a target position, clamped by a maximum turn rate.
    /// </summary>
    /// <param name="pawnPosition">World position of the pawn.</param>
    /// <param name="pawnEulerY">Current yaw of the pawn in degrees (transform.eulerAngles.y).</param>
    /// <param name="pawnForward">Current forward direction of the pawn (transform.forward).</param>
    /// <param name="currentPitch">Accumulated pitch from previous frames.</param>
    /// <param name="targetPosition">World position to aim toward.</param>
    /// <param name="hasLookTarget">Whether a look target was explicitly set.</param>
    /// <param name="maxTurnSpeed">Maximum degrees per second of rotation.</param>
    /// <param name="maxPitch">Maximum upward/downward pitch in degrees.</param>
    /// <param name="deltaTime">Frame delta time.</param>
    /// <returns>A LookResult containing the yaw/pitch deltas and the updated pitch accumulator.</returns>
    public static LookResult ComputeLookInput
    (
        Vector3 pawnPosition,
        float pawnEulerY,
        float currentPitch,
        Vector3 targetPosition,
        bool hasLookTarget,
        float maxTurnSpeed,
        float maxPitch,
        float deltaTime
    )
    {
        // Flatten target direction onto the horizontal plane for yaw calculation.
        Vector3 toTargetFlat = Vector3.ProjectOnPlane(targetPosition - pawnPosition, Vector3.up);

        float targetYaw = toTargetFlat != Vector3.zero
            ? Quaternion.LookRotation(toTargetFlat).eulerAngles.y
            : pawnEulerY;

        float yawDelta = Mathf.DeltaAngle(pawnEulerY, targetYaw);
        float maxDelta = maxTurnSpeed * deltaTime;
        float clampedYaw = Mathf.Clamp(yawDelta, -maxDelta, maxDelta);

        // Pitch: only computed when an explicit look target is set.
        float desiredPitch = 0f;
        if (hasLookTarget)
        {
            Vector3 toTarget3D = targetPosition - pawnPosition;
            desiredPitch = -Mathf.Atan2(toTarget3D.y, toTargetFlat.magnitude) * Mathf.Rad2Deg;
            desiredPitch = Mathf.Clamp(desiredPitch, -maxPitch, maxPitch);
        }

        float pitchDelta = desiredPitch - currentPitch;
        float clampedPitch = Mathf.Clamp(pitchDelta, -maxDelta, maxDelta);
        float newPitch = currentPitch + clampedPitch;

        return new LookResult
        {
            LookDelta = new Vector2(clampedYaw, clampedPitch),
            UpdatedPitch = newPitch,
        };
    }

    /// <summary>
    /// Return value for ComputeLookInput, bundling the frame's look delta
    /// with the updated pitch accumulator for the next frame.
    /// </summary>
    public struct LookResult
    {
        public Vector2 LookDelta;
        public float UpdatedPitch;
    }
}
