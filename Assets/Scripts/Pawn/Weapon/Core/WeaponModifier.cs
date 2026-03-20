/// <summary>
/// A multiplicative modifier that can be stacked to alter weapon behaviour.
/// All fields default to 1.0 (no effect). Modifiers are combined multiplicatively.
/// </summary>
[System.Serializable]
public struct WeaponModifier
{
    /// <summary>Multiplier on fire rate cooldown. Less than 1 = faster fire rate.</summary>
    public float FireRateMultiplier;

    /// <summary>Multiplier on spread angle. Less than 1 = tighter spread.</summary>
    public float SpreadMultiplier;

    /// <summary>A modifier that has no effect (identity).</summary>
    public static WeaponModifier Identity => new()
    {
        FireRateMultiplier = 1f,
        SpreadMultiplier = 1f,
    };
}
