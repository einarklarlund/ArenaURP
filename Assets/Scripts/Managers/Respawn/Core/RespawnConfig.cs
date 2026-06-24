using UnityEngine;

/// <summary>
/// Immutable configuration for the respawn timer processor.
/// Typically populated from serialized fields on PawnManager (or read from DeathmatchManager).
/// </summary>
[System.Serializable]
public struct RespawnConfig
{
    [Tooltip("Seconds to wait between a pawn's death and its respawn.")]
    public float DelaySeconds;

    public static RespawnConfig Default => new()
    {
        DelaySeconds = 3f,
    };
}
