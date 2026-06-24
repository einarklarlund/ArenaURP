using UnityEngine;

/// <summary>
/// Immutable configuration for the match timer / win-condition processor.
/// Typically populated from serialized fields on DeathmatchManager.
/// </summary>
[System.Serializable]
public struct MatchTimerConfig
{
    [Tooltip("Total length of a match in seconds. Timer ends the match when ElapsedSeconds >= this value.")]
    public float MatchDurationSeconds;

    [Tooltip("Score threshold; match ends when the leading player reaches this many kills. Set to <= 0 to disable.")]
    public float KillsToWin;

    public static MatchTimerConfig Default => new()
    {
        MatchDurationSeconds = 600f,
        KillsToWin = 5f,
    };
}
