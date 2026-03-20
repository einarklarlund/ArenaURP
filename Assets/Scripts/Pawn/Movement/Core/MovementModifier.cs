/// <summary>
/// A multiplicative modifier that can be stacked to alter movement behaviour.
/// All fields default to 1.0 (no effect). Modifiers are combined multiplicatively.
/// </summary>
[System.Serializable]
public struct MovementModifier
{
    /// <summary>Multiplier applied to horizontal max walk speed.</summary>
    public float WalkSpeedMultiplier;

    /// <summary>Multiplier applied to horizontal walk acceleration.</summary>
    public float WalkAccelerationMultiplier;

    /// <summary>Multiplier applied to jump force.</summary>
    public float JumpForceMultiplier;

    /// <summary>Multiplier applied to gravity.</summary>
    public float GravityMultiplier;

    /// <summary>Multiplier applied to overall velocity before external impulses are added.</summary>
    public float PreImpulseVelocityMultiplier;

    /// <summary>A modifier that has no effect (identity).</summary>
    public static MovementModifier Identity => new()
    {
        WalkSpeedMultiplier = 1f,
        WalkAccelerationMultiplier = 1f,
        JumpForceMultiplier = 1f,
        GravityMultiplier = 1f,
        PreImpulseVelocityMultiplier = 1f,
    };
}
