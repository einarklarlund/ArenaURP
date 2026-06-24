/// <summary>
/// Mutable snapshot of the match timer simulation.
/// Pure data, no Unity dependencies. Captures everything needed to resume a match in progress.
/// </summary>
[System.Serializable]
public struct MatchTimerState
{
    /// <summary>Whether the timer is actively counting. False before BeginGame and after the match ends.</summary>
    public bool IsRunning;

    /// <summary>Seconds elapsed since the match began.</summary>
    public float ElapsedSeconds;

    /// <summary>Highest score observed so far this match.</summary>
    public float HighScore;

    /// <summary>True once the win-condition has fired (timer expired or kill-threshold reached).</summary>
    public bool IsOver;

    /// <summary>
    /// Reason the match ended. <see cref="MatchEndReason.None"/> while IsOver is false.
    /// </summary>
    public MatchEndReason EndReason;

    public static MatchTimerState Default => new()
    {
        IsRunning = false,
        ElapsedSeconds = 0f,
        HighScore = 0f,
        IsOver = false,
        EndReason = MatchEndReason.None,
    };

    /// <summary>
    /// Returns a fresh "match has just begun" state.
    /// </summary>
    public static MatchTimerState Begin() => new()
    {
        IsRunning = true,
        ElapsedSeconds = 0f,
        HighScore = 0f,
        IsOver = false,
        EndReason = MatchEndReason.None,
    };
}

/// <summary>
/// Why the match ended.
/// </summary>
public enum MatchEndReason
{
    None = 0,
    TimerExpired = 1,
    KillsReached = 2,
}
