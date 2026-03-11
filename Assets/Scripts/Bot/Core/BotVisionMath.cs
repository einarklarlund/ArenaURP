using UnityEngine;

/// <summary>
/// Pure geometry helpers for bot vision calculations.
/// Extracted from PawnSensor so these can be unit-tested
/// in EditMode without any scene or FishNet runtime setup.
/// </summary>
public static class BotVisionMath
{
    /// <summary>
    /// Small tolerance added to the FOV half-angle comparison to absorb
    /// floating-point error from Quaternion/Vector3 math. Without this,
    /// targets sitting exactly on the FOV boundary can flicker in and out
    /// as sub-degree rounding nudges the computed angle across the threshold.
    /// </summary>
    public const float AngleEpsilon = 0.01f;

    /// <summary>
    /// Returns true if <paramref name="targetPos"/> falls within a forward-facing
    /// cone of <paramref name="fovDegrees"/> total angle and <paramref name="maxRange"/> radius
    /// as seen from <paramref name="observerPos"/> looking along <paramref name="observerForward"/>.
    /// </summary>
    public static bool IsInFieldOfView(
        Vector3 observerPos,
        Vector3 observerForward,
        Vector3 targetPos,
        float fovDegrees,
        float maxRange)
    {
        Vector3 toTarget = targetPos - observerPos;
        if (toTarget.sqrMagnitude > maxRange * maxRange)
            return false;

        float angle = Vector3.Angle(observerForward, toTarget);
        return angle <= fovDegrees * 0.5f + AngleEpsilon;
    }
}
