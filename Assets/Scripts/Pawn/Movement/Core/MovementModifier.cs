/// <summary>
/// A multiplicative modifier that can be stacked to alter movement behaviour.
/// All fields default to 1.0 (no effect). Modifiers are combined multiplicatively.
/// </summary>
[System.Serializable]
public struct MovementModifier
{
    /// <summary>Multiplier applied to horizontal max speed.</summary>
    public float SpeedMultiplier;

    /// <summary>Multiplier applied to jump force.</summary>
    public float JumpForceMultiplier;

    /// <summary>Multiplier applied to gravity.</summary>
    public float GravityMultiplier;

    /// <summary>A modifier that has no effect (identity).</summary>
    public static MovementModifier Identity => new()
    {
        SpeedMultiplier = 1f,
        JumpForceMultiplier = 1f,
        GravityMultiplier = 1f,
    };
}
