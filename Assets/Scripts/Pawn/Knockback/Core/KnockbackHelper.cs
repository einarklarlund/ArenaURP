using UnityEngine;

/// <summary>
/// Pure, stateless helper that computes knockback impulse vectors.
/// Has zero Unity lifecycle dependencies.
/// </summary>
public static class KnockbackHelper
{
    /// <summary>
    /// Computes a knockback impulse vector from a hit direction and config.
    /// Enforces a minimum launch angle from horizontal.
    /// </summary>
    /// <param name="hitDirection">Direction the hit came from (will be normalized).</param>
    /// <param name="config">Knockback tuning parameters.</param>
    /// <returns>An impulse vector to add to the pawn's velocity.</returns>
    public static Vector3 ComputeImpulse(Vector3 hitDirection, in KnockbackConfig config)
    {
        Vector3 dir = hitDirection.normalized;

        float horizontalMag = Vector3.ProjectOnPlane(dir, Vector3.up).magnitude;
        float minHeight = horizontalMag * Mathf.Tan(config.MinimumAngleOnHit * Mathf.Deg2Rad);
        dir.y = Mathf.Max(dir.y, minHeight);
        dir = dir.normalized;

        return config.SpeedOnHit * dir;
    }
}
