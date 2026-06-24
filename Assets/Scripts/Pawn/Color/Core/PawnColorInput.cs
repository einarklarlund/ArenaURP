using UnityEngine;

/// <summary>
/// Per-tick input to <see cref="PawnColorProcessor"/>.
/// Carries any new flash events that occurred since the last Process call.
///
/// To trigger a flash, set the corresponding Trigger flag and fill in the
/// color/duration fields. When the flag is false the matching fields are
/// ignored. This mirrors the imperative <c>SetColor</c> / <c>SetBaseColor</c>
/// calls in the original PawnColor.
/// </summary>
[System.Serializable]
public struct PawnColorInput
{
    /// <summary>If true, (re)start the target timeline with <see cref="TargetColor"/> / <see cref="TargetDurationSeconds"/>.</summary>
    public bool TriggerTarget;

    /// <summary>Color to flash to at t=0 of the target timeline.</summary>
    public Color TargetColor;

    /// <summary>Duration the target timeline should lerp back to default. Use the config's HitDurationSeconds for hits.</summary>
    public float TargetDurationSeconds;

    /// <summary>If true, (re)start the base timeline with <see cref="BaseColor"/> / <see cref="BaseDurationSeconds"/>.</summary>
    public bool TriggerBase;

    /// <summary>Color to tint to at t=0 of the base timeline.</summary>
    public Color BaseColor;

    /// <summary>Duration the base timeline should lerp back to default.</summary>
    public float BaseDurationSeconds;

    public static PawnColorInput Default => new()
    {
        TriggerTarget = false,
        TargetColor = Color.white,
        TargetDurationSeconds = 0f,
        TriggerBase = false,
        BaseColor = Color.white,
        BaseDurationSeconds = 0f,
    };

    /// <summary>Convenience: build an input that triggers a hit flash using the supplied color/duration.</summary>
    public static PawnColorInput Hit(Color color, float durationSeconds) => new()
    {
        TriggerTarget = true,
        TargetColor = color,
        TargetDurationSeconds = durationSeconds,
        TriggerBase = false,
        BaseColor = Color.white,
        BaseDurationSeconds = 0f,
    };
}
