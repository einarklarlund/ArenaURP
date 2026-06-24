using System;

/// <summary>
/// Pure, stateless processor that advances a match countdown by one step.
/// Side-effect free: returns a new state. The single edge event ("countdown just finished")
/// is reported back to the caller via <see cref="MatchCountdownState.TransitionFired"/>.
/// </summary>
public static class MatchCountdownProcessor
{
    /// <summary>
    /// Advance the countdown simulation by one tick.
    /// </summary>
    /// <param name="state">Current countdown state.</param>
    /// <param name="config">Countdown tuning parameters (unused at runtime, kept for convention parity).</param>
    /// <param name="input">Per-tick input (currently no-op).</param>
    /// <param name="deltaTime">Time step for this tick, in seconds.</param>
    /// <returns>The new countdown state after this tick.</returns>
    public static MatchCountdownState Process(
        MatchCountdownState state,
        in MatchCountdownConfig config,
        in MatchCountdownInput input,
        float deltaTime)
    {
        // TransitionFired is an edge signal - consumed by the caller on the same tick
        // and cleared on every subsequent Process call.
        state.TransitionFired = false;

        // Idempotent when inactive.
        if (!state.IsActive)
        {
            return state;
        }

        state.RemainingSeconds -= deltaTime;

        if (state.RemainingSeconds <= 0f)
        {
            // Crossed zero this tick - fire the one-shot transition signal and stop ticking.
            state.RemainingSeconds = 0f;
            state.DisplaySeconds = 0;
            state.IsActive = false;
            state.TransitionFired = true;
            return state;
        }

        // While active, DisplaySeconds is the ceiling of RemainingSeconds.
        // This matches the original "yield 1s after setting CountdownTime.Value = i" loop.
        state.DisplaySeconds = (int)Math.Ceiling(state.RemainingSeconds);
        return state;
    }
}
