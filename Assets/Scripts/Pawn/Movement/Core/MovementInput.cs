using UnityEngine;

/// <summary>
/// All per-frame inputs consumed by MovementProcessor.
/// Bundles together player controls, physics state, and orientation
/// so the processor has a single clean parameter.
/// </summary>
[System.Serializable]
public struct MovementInput
{
    /// <summary>Player stick / WASD input in local space, magnitude clamped to 1.</summary>
    public Vector2 Move;

    /// <summary>Whether the jump button is held this frame.</summary>
    public bool JumpPressed;

    /// <summary>Whether the CharacterController reports being grounded.</summary>
    public bool IsGrounded;

    /// <summary>Yaw rotation of the character, used to transform local move input to world space.</summary>
    public Quaternion WorldOrientation;

    /// <summary>An input snapshot representing a stationary, grounded character facing forward.</summary>
    public static MovementInput Default => new()
    {
        Move = Vector2.zero,
        JumpPressed = false,
        IsGrounded = true,
        WorldOrientation = Quaternion.identity,
    };
}
