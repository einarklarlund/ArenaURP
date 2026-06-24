using UnityEngine;

/// <summary>
/// Mutable snapshot of the pawn color flash simulation.
/// Pure data, no Unity lifecycle dependencies. Two independent timelines
/// (target + base) layer over each renderer's default color, identical to
/// the original PawnColor.Update logic.
///
/// Each timeline starts with <see cref="TargetDurationSeconds"/> or
/// <see cref="BaseDurationSeconds"/> set to a negative value, which the
/// processor treats as "expired" so the default color renders unchanged
/// until something pushes a flash.
/// </summary>
[System.Serializable]
public struct PawnColorState
{
    // --- Target (hit-flash) timeline ---

    /// <summary>Seconds elapsed since the target timeline last (re)started.</summary>
    public float TargetElapsedSeconds;

    /// <summary>Total duration of the active target timeline. Negative means "no active flash".</summary>
    public float TargetDurationSeconds;

    /// <summary>Color the target timeline lerps from at t=0 back to the default at t=duration.</summary>
    public Color TargetColor;

    // --- Base (longer-lived tint) timeline ---

    /// <summary>Seconds elapsed since the base timeline last (re)started.</summary>
    public float BaseElapsedSeconds;

    /// <summary>Total duration of the active base timeline. Negative means "no active tint".</summary>
    public float BaseDurationSeconds;

    /// <summary>Color the base timeline lerps from at t=0 back to the default at t=duration.</summary>
    public Color BaseColor;

    /// <summary>
    /// True iff at least one timeline is still mid-lerp. When false, callers can skip the
    /// per-renderer Resolve pass and write defaults directly (matches the original early-out).
    /// </summary>
    public bool IsActive;

    /// <summary>
    /// Default starting state: both timelines expired, nothing to render.
    /// Matches the values <c>Start()</c> assigned in the original PawnColor.
    /// </summary>
    public static PawnColorState Default => new()
    {
        TargetElapsedSeconds = 0f,
        TargetDurationSeconds = -1f,
        TargetColor = Color.white,
        BaseElapsedSeconds = 0f,
        BaseDurationSeconds = -1f,
        BaseColor = Color.white,
        IsActive = false,
    };
}
