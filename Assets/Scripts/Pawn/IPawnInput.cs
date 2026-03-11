using UnityEngine;

/// <summary>
/// Unified input data consumed by MovementInputHandler, CameraInputHandler,
/// and PawnWeaponHandler. A single source of truth for all pawn actions,
/// whether produced by a human player or a bot.
/// </summary>
public struct PawnInputData
{
    /// <summary>Horizontal (x) and vertical (y) movement axes, in local space, magnitude clamped to 1.</summary>
    public Vector2 Move;

    /// <summary>Whether the jump action is currently held.</summary>
    public bool Jump;

    /// <summary>
    /// Look/rotation delta this frame.
    /// x = horizontal (yaw) delta. y = vertical (pitch) delta.
    /// </summary>
    public Vector2 Look;

    /// <summary>Whether the fire action is currently held (true every frame the trigger is down).</summary>
    public bool Fire;
}

/// <summary>
/// Contract for any component that supplies PawnInputData to the pawn's handler
/// components.
/// </summary>
public interface IPawnInput
{
    PawnInputData Data { get; }

    /// <summary>
    /// True when this provider should drive the pawn's logic on the local machine.
    /// </summary>
    bool IsActive { get; }
}
