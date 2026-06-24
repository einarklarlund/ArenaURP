using UnityEngine;

/// <summary>
/// Pure, stateless processor that advances the pawn color flash simulation
/// by one tick. Tracks two independent timelines (target + base) that each
/// lerp from a flash color back to a per-renderer default.
///
/// Per-renderer default colors are intentionally NOT part of the simulation;
/// callers fold them in via <see cref="Resolve"/> when applying the result
/// to materials.
/// </summary>
public static class PawnColorProcessor
{
    /// <summary>
    /// Advance the color simulation by one tick.
    /// </summary>
    /// <param name="state">Current color state.</param>
    /// <param name="config">Color tuning parameters (currently unused inside Process; reserved for future tuning hooks).</param>
    /// <param name="input">Per-tick triggers for new flashes.</param>
    /// <param name="deltaTime">Time step for this tick (clamped to >= 0).</param>
    /// <returns>The new color state after this tick.</returns>
    public static PawnColorState Process(
        PawnColorState state,
        in PawnColorConfig config,
        in PawnColorInput input,
        float deltaTime)
    {
        // config currently has no per-tick effect, but we keep the
        // signature consistent with other processors.
        _ = config;

        float dt = Mathf.Max(0f, deltaTime);

        // Handle triggers first so a flash that fires this tick starts at t=0,
        // not t=dt. Matches the original where Time.time is captured in SetColor
        // and Update reads (Time.time - changedTime) afterwards.
        if (input.TriggerTarget)
        {
            state.TargetElapsedSeconds = 0f;
            state.TargetDurationSeconds = input.TargetDurationSeconds;
            state.TargetColor = input.TargetColor;
        }
        else if (state.TargetElapsedSeconds < state.TargetDurationSeconds)
        {
            state.TargetElapsedSeconds += dt;
        }

        if (input.TriggerBase)
        {
            state.BaseElapsedSeconds = 0f;
            state.BaseDurationSeconds = input.BaseDurationSeconds;
            state.BaseColor = input.BaseColor;
        }
        else if (state.BaseElapsedSeconds < state.BaseDurationSeconds)
        {
            state.BaseElapsedSeconds += dt;
        }

        bool targetActive = state.TargetElapsedSeconds < state.TargetDurationSeconds;
        bool baseActive = state.BaseElapsedSeconds < state.BaseDurationSeconds;
        state.IsActive = targetActive || baseActive;

        return state;
    }

    /// <summary>
    /// Resolve the per-renderer output color by layering the two active
    /// timelines over the renderer's default color. Mirrors the lerp
    /// sequence inside the original PawnColor.Update loop.
    ///
    /// Order matters: base is applied first, then target overrides on top.
    /// </summary>
    /// <param name="state">Current state (as returned by <see cref="Process"/>).</param>
    /// <param name="defaultColor">The renderer's default color (albedo or emission).</param>
    /// <returns>The color to write to the material this frame.</returns>
    public static Color Resolve(in PawnColorState state, Color defaultColor)
    {
        Color color = defaultColor;

        if (state.BaseElapsedSeconds < state.BaseDurationSeconds && state.BaseDurationSeconds > 0f)
        {
            float t = state.BaseElapsedSeconds / state.BaseDurationSeconds;
            color = Color.Lerp(state.BaseColor, defaultColor, t);
        }

        if (state.TargetElapsedSeconds < state.TargetDurationSeconds && state.TargetDurationSeconds > 0f)
        {
            float t = state.TargetElapsedSeconds / state.TargetDurationSeconds;
            color = Color.Lerp(state.TargetColor, color, t);
        }

        return color;
    }
}
