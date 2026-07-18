using UnityEngine;

/// <summary>
/// The knockback a source DEALS on hit — authored on the dealing entity's <see cref="KnockbackController"/> (the
/// projectile prefab), not on the thing being hit. The controller's dealer half folds it with its outgoing
/// modifiers into an impulse.
/// </summary>
[System.Serializable]
public struct KnockbackConfig
{
    [Tooltip("Speed applied as knockback when this hits something.")]
    public float SpeedOnHit;

    [Tooltip("Minimum launch angle in degrees from horizontal when this hits something.")]
    public float MinimumAngleOnHit;

    public static KnockbackConfig Default => new()
    {
        SpeedOnHit = 7f,
        MinimumAngleOnHit = 35f,
    };
}
