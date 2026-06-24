/// <summary>
/// Complete snapshot of a match countdown simulation.
/// Pure data - no behaviour, no Unity dependencies.
/// </summary>
[System.Serializable]
public struct MatchCountdownState
{
    /// <summary>Whether the countdown is currently ticking.</summary>
    public bool IsActive;

    /// <summary>
    /// Remaining time in fractional seconds.
    /// When the countdown begins this is the configured duration; the processor decrements
    /// by deltaTime each tick and clamps at zero.
    /// </summary>
    public float RemainingSeconds;

    /// <summary>
    /// The remaining time rounded up to whole seconds (matches the original "tick once per
    /// second" coroutine behaviour and is what the SyncVar projection should display).
    /// While IsActive, valid range is [1, DurationSeconds]; when the countdown finishes
    /// this is 0.
    /// </summary>
    public int DisplaySeconds;

    /// <summary>
    /// True for exactly the single tick on which the countdown finished (RemainingSeconds
    /// crossed from >0 to 0 and IsActive transitioned from true to false). The Pawn/Manager
    /// glue uses this as the edge signal to perform the SyncVar match-state transition.
    /// </summary>
    public bool TransitionFired;

    /// <summary>An inactive, zeroed countdown.</summary>
    public static MatchCountdownState Default => new()
    {
        IsActive = false,
        RemainingSeconds = 0f,
        DisplaySeconds = 0,
        TransitionFired = false,
    };

    /// <summary>
    /// Helper that returns a state representing a freshly-started countdown of the given duration.
    /// </summary>
    public static MatchCountdownState Begin(int durationSeconds) => new()
    {
        IsActive = true,
        RemainingSeconds = durationSeconds,
        DisplaySeconds = durationSeconds,
        TransitionFired = false,
    };
}
