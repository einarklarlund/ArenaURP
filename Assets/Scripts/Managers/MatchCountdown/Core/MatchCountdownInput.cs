/// <summary>
/// Per-tick inputs consumed by MatchCountdownProcessor.
/// The countdown has no external runtime input (other than dt) - this struct exists
/// for convention parity with the other processors in the codebase and gives us a
/// stable extension point.
/// </summary>
[System.Serializable]
public struct MatchCountdownInput
{
    /// <summary>
    /// A no-op input. The processor is dt-driven only; this is the canonical input value
    /// to pass each tick.
    /// </summary>
    public static MatchCountdownInput Default => new();
}
