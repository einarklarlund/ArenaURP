/// <summary>
/// All per-frame inputs consumed by TemperatureProcessor.
/// </summary>
[System.Serializable]
public struct TemperatureInput
{
    /// <summary>Whether the primary fire button is held this frame.</summary>
    public bool IsDraining;

    /// <summary>An input snapshot representing a pawn that is not firing.</summary>
    public static TemperatureInput Default => new()
    {
        IsDraining = false,
    };
}
