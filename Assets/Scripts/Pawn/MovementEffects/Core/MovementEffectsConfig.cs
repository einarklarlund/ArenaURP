using UnityEngine;

/// <summary>
/// Configuration for how movement state affects other pawn systems (e.g. weapons).
/// </summary>
[System.Serializable]
public struct MovementEffectsConfig
{
    [Header("Weapon Effects")]
    [Tooltip("Multiplier on fire rate cooldown at zero speed. Lerps toward 1 at max speed.")]
    public float FireRateMultiplier;

    [Tooltip("Multiplier on spread angle at zero speed. Lerps toward 1 at max speed.")]
    public float SpreadAngleMultiplier;

    public static MovementEffectsConfig Default => new()
    {
        FireRateMultiplier = 0.5f,
        SpreadAngleMultiplier = 1.5f,
    };
}
