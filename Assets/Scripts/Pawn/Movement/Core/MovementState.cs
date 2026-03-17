using UnityEngine;

/// <summary>
/// Complete snapshot of a pawn's movement simulation state.
/// Pure data - no behaviour, no Unity dependencies beyond Vector3.
/// This struct captures everything needed to resume simulation from a given point,
/// making it ideal for FishNet Reconcile data and replay systems.
/// </summary>
[System.Serializable]
public struct MovementState
{
    /// <summary>Current velocity (full 3D - XZ for horizontal, Y for vertical).</summary>
    public Vector3 Velocity;

    /// <summary>Whether the character controller was grounded this frame.</summary>
    public bool IsGrounded;

    /// <summary>Seconds elapsed since the jump button was last pressed. Resets to 0 on press.</summary>
    public float TimeSinceJumpPressed;

    /// <summary>Seconds elapsed since the current jump began. Resets to 0 on jump initiation.</summary>
    public float TimeSinceJumpBegan;

    /// <summary>
    /// Whether the mario jump (hold-to-jump-higher) has been interrupted
    /// by releasing the jump button.
    /// </summary>
    public bool MarioJumpInterrupted;

    /// <summary>
    /// When true, the processor will apply PendingHit this frame and clear the flag.
    /// Set this from outside (e.g. a damage event) before calling Process().
    /// </summary>
    public bool HasPendingHit;

    /// <summary>
    /// Knockback to apply this frame. Only read when HasPendingHit is true.
    /// The processor clears HasPendingHit after consuming this.
    /// </summary>
    public HitKnockbackInfo PendingHit;

    /// <summary>
    /// Large sentinel value indicating "jump was never pressed" or
    /// "so long ago that the jump buffer has expired."
    /// </summary>
    public const float JumpTimeNotSet = 1000f;

    public static MovementState Default => new()
    {
        Velocity = Vector3.zero,
        IsGrounded = false,
        TimeSinceJumpPressed = JumpTimeNotSet,
        TimeSinceJumpBegan = JumpTimeNotSet,
        MarioJumpInterrupted = true,
        HasPendingHit = false,
        PendingHit = default,
    };
}

/// <summary>
/// Knockback direction data from a damage event.
/// Kept decoupled from DamageInfo so MovementProcessor has no dependency on the damage system.
/// </summary>
[System.Serializable]
public struct HitKnockbackInfo
{
    public Vector3 Direction;
}
