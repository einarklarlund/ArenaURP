using UnityEngine;

/// <summary>
/// Pure, stateless processor that advances the match timer / win-condition simulation by one step.
/// </summary>
public static class MatchTimerProcessor
{
    /// <summary>
    /// Advance the match timer by one tick.
    /// </summary>
    /// <param name="state">Current timer state.</param>
    /// <param name="config">Timer tuning parameters.</param>
    /// <param name="input">Per-tick scoreboard input.</param>
    /// <param name="deltaTime">Time step for this tick (clamped to >= 0).</param>
    /// <returns>The new timer state after this tick.</returns>
    public static MatchTimerState Process(
        MatchTimerState state,
        in MatchTimerConfig config,
        in MatchTimerInput input,
        float deltaTime)
    {
        // Idempotent: once the match is over or not running, no state changes.
        if (!state.IsRunning || state.IsOver)
        {
            return state;
        }

        float dt = Mathf.Max(0f, deltaTime);

        // Track high score from the leading player this tick.
        if (input.LeadingScore > state.HighScore)
        {
            state.HighScore = input.LeadingScore;
        }

        // Advance elapsed time.
        state.ElapsedSeconds += dt;

        // Kill threshold takes priority when reached on the same tick as the timer expires.
        if (config.KillsToWin > 0f && state.HighScore >= config.KillsToWin)
        {
            state.IsOver = true;
            state.IsRunning = false;
            state.EndReason = MatchEndReason.KillsReached;
            return state;
        }

        // Timer expiry.
        if (state.ElapsedSeconds >= config.MatchDurationSeconds)
        {
            state.IsOver = true;
            state.IsRunning = false;
            state.EndReason = MatchEndReason.TimerExpired;
            // Clamp so callers reading ElapsedSeconds get a clean value.
            state.ElapsedSeconds = config.MatchDurationSeconds;
        }

        return state;
    }
}
