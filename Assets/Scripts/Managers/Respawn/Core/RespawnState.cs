using System.Collections.Generic;

/// <summary>
/// Mutable snapshot of all in-flight respawn timers.
/// Pure data, no Unity / FishNet dependencies.
///
/// Each pending entry tracks a single player (referenced by an opaque <see cref="object"/> key)
/// counting down toward their respawn. Multiple players can be in respawn concurrently;
/// the processor advances all of them in lock-step each tick.
///
/// The key type is intentionally <see cref="object"/> so this assembly stays free of any
/// NetworkPlayer / FishNet reference. The PawnManager glue is responsible for mapping
/// NetworkPlayer &lt;-&gt; key (reference equality is sufficient).
/// </summary>
[System.Serializable]
public struct RespawnState
{
    /// <summary>One pending respawn: a player key plus their remaining countdown in seconds.</summary>
    public struct Pending
    {
        public object Key;
        public float RemainingSeconds;
    }

    /// <summary>
    /// All players currently waiting to respawn. Order is insertion order; the processor
    /// does not reorder this list.
    /// </summary>
    public List<Pending> PendingRespawns;

    /// <summary>
    /// Player keys whose timer just hit zero on the most recent <c>Process</c> call.
    /// One-shot edge signal: cleared at the start of every <c>Process</c> call. The
    /// PawnManager glue reads this immediately after Process to spawn pawns.
    /// </summary>
    public List<object> ReadyThisTick;

    /// <summary>
    /// Returns an empty, ready-to-use state with both lists allocated.
    /// </summary>
    public static RespawnState Default => new()
    {
        PendingRespawns = new List<Pending>(),
        ReadyThisTick = new List<object>(),
    };
}
