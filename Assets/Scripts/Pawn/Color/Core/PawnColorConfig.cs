using UnityEngine;

/// <summary>
/// Immutable configuration for the pawn color flash processor.
/// Typically populated from serialized fields on <c>PawnColor</c>.
/// </summary>
[System.Serializable]
public struct PawnColorConfig
{
    [Tooltip("Color flashed on the pawn when a hit (or other transient event) occurs.")]
    public Color HitColor;

    [Tooltip("Seconds the hit flash lerps back from HitColor to the pawn's default color.")]
    public float HitDurationSeconds;

    public static PawnColorConfig Default => new()
    {
        HitColor = Color.red,
        HitDurationSeconds = 0.3f,
    };
}
