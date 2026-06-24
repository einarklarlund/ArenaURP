/// <summary>
/// Per-tick input to <see cref="MatchTimerProcessor"/>.
/// Built each frame from the live scoreboard.
/// </summary>
[System.Serializable]
public struct MatchTimerInput
{
    /// <summary>The highest score across all active players this tick.</summary>
    public float LeadingScore;

    public static MatchTimerInput Default => new()
    {
        LeadingScore = 0f,
    };
}
