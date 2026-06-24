/// <summary>
/// Immutable configuration for the match countdown processor.
/// Typically populated from serialized fields on MatchFlowManager.
/// </summary>
[System.Serializable]
public struct MatchCountdownConfig
{
    /// <summary>Total countdown duration, in seconds, when the countdown begins.</summary>
    public int DurationSeconds;

    /// <summary>
    /// Returns a config matching the original MatchFlowManager pregame default (3 seconds).
    /// </summary>
    public static MatchCountdownConfig Default => new()
    {
        DurationSeconds = 3,
    };
}
