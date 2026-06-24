using System.Collections.Generic;

/// <summary>
/// Pure, stateless processor that advances all in-flight respawn timers by one step.
///
/// Each tick the processor:
///   1. Clears the <see cref="RespawnState.ReadyThisTick"/> one-shot edge list.
///   2. Decrements <c>RemainingSeconds</c> on every existing pending entry by <paramref name="deltaTime"/>
///      and removes any that reached zero, copying their keys into <c>ReadyThisTick</c>.
///   3. Enqueues fresh <see cref="RespawnState.Pending"/> entries for every key in
///      <see cref="RespawnInput.NewlyKilled"/>, using the full configured delay (the player
///      who died this tick gets a full countdown, not <c>Delay - dt</c>).
///
/// Side-effect free: mutates the supplied state lists (instead of allocating new ones each tick)
/// and returns the updated state. Callers should treat <c>ReadyThisTick</c> as consumed within
/// the same tick - it is cleared on the very next <c>Process</c> call.
/// </summary>
public static class RespawnProcessor
{
    /// <summary>
    /// Advance every pending respawn timer by one tick.
    /// </summary>
    /// <param name="state">Current respawn state. Lists must be non-null (see <see cref="RespawnState.Default"/>).</param>
    /// <param name="config">Respawn tuning parameters.</param>
    /// <param name="input">Per-tick input (newly-killed players).</param>
    /// <param name="deltaTime">Time step for this tick, in seconds. Negative values are clamped to 0.</param>
    /// <returns>The new respawn state after this tick.</returns>
    public static RespawnState Process(
        RespawnState state,
        in RespawnConfig config,
        in RespawnInput input,
        float deltaTime)
    {
        // Lazily allocate the lists if a caller built the struct without using Default.
        state.PendingRespawns ??= new List<RespawnState.Pending>();
        state.ReadyThisTick ??= new List<object>();

        // One-shot edge signal: always cleared at the start of a new tick.
        state.ReadyThisTick.Clear();

        float dt = deltaTime > 0f ? deltaTime : 0f;

        // 1. Advance and harvest existing pending timers. Iterate in reverse so removals
        //    don't shift indices we still need.
        for (int i = state.PendingRespawns.Count - 1; i >= 0; i--)
        {
            var pending = state.PendingRespawns[i];
            pending.RemainingSeconds -= dt;

            if (pending.RemainingSeconds <= 0f)
            {
                state.ReadyThisTick.Add(pending.Key);
                state.PendingRespawns.RemoveAt(i);
            }
            else
            {
                state.PendingRespawns[i] = pending;
            }
        }

        // 2. Enqueue any newly-killed players with a full countdown. Adding *after* the
        //    decrement step ensures a player who died this tick gets the full configured
        //    delay, not Delay - dt.
        if (input.NewlyKilled != null)
        {
            for (int i = 0; i < input.NewlyKilled.Count; i++)
            {
                var key = input.NewlyKilled[i];
                if (key == null) continue;

                state.PendingRespawns.Add(new RespawnState.Pending
                {
                    Key = key,
                    RemainingSeconds = config.DelaySeconds,
                });
            }
        }

        return state;
    }
}
