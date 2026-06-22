/// <summary>
/// Complete snapshot of a pawn's temperature simulation state.
/// Pure data - no behaviour, no Unity dependencies.
///
/// Temperature is stored as a float internally to preserve per-frame drain/regen
/// precision. Display it as an integer by casting: (int)state.Temperature.
/// </summary>
[System.Serializable]
public struct TemperatureState
{
    /// <summary>
    /// Current temperature. Clamped to [0, TemperatureConfig.MaxTemperature] by the processor.
    /// </summary>
    public float Temperature;

    /// <summary>
    /// The movement modifier derived from the current temperature.
    /// Computed by the processor each tick.
    /// </summary>
    public MovementModifier MovementModifier;

    /// <summary>
    /// The weapon modifier derived from the current temperature.
    /// Computed by the processor each tick.
    /// </summary>
    public WeaponModifier WeaponModifier;

    /// <summary>A fully-charged pawn at default max temperature.</summary>
    public static TemperatureState Default => new()
    {
        Temperature = 100f,
        MovementModifier = MovementModifier.Identity,
        WeaponModifier = WeaponModifier.Identity,
    };
}
