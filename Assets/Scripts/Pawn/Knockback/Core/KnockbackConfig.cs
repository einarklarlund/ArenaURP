using UnityEngine;

/// <summary>
/// Configuration for knockback impulses applied when a pawn takes damage.
/// </summary>
[System.Serializable]
public struct KnockbackConfig
{
    [Tooltip("Speed applied as knockback when hit.")]
    public float SpeedOnHit;

    [Tooltip("Minimum launch angle in degrees from horizontal when hit.")]
    public float MinimumAngleOnHit;

    public static KnockbackConfig Default => new()
    {
        SpeedOnHit = 7f,
        MinimumAngleOnHit = 35f,
    };
}
