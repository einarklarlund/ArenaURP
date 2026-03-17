using UnityEngine;

/// <summary>
/// Immutable configuration for pawn movement.
/// Typically populated from serialized fields on a MonoBehaviour.
/// </summary>
[System.Serializable]
public struct MovementConfig
{
    [Header("Ground Movement")]
    [Tooltip("Base horizontal max speed when grounded.")]
    public float MaxSpeedXZ;

    [Tooltip("Seconds to accelerate from 0 to max speed.")]
    public float SpeedUpTime;

    [Tooltip("Seconds to decelerate from max speed to 0.")]
    public float SlowDownTime;

    [Header("Aerial Movement")]
    [Tooltip("Extra horizontal max speed added when airborne (for acceleration target only).")]
    public float AirSpeedBonus;

    [Tooltip("Divisor applied to walk acceleration when airborne. Higher = less air control.")]
    public float AirControlDivisor;

    [Header("Jumping")]
    [Tooltip("Peak height of the jump in world units.")]
    public float JumpHeight;

    [Tooltip("Duration in seconds of the mario-style variable jump (hold to go higher).")]
    public float MarioJumpTime;

    [Tooltip("Window in seconds after pressing jump that a buffered jump will still fire on landing.")]
    public float JumpBufferWindow;

    [Header("On Hit Effects")]
    [Tooltip("Speed applied as knockback when hit.")]
    public float SpeedOnHit;

    [Tooltip("Minimum launch angle in degrees from horizontal when hit.")]
    public float MinimumAngleOnHit;

    [Header("Physics")]
    [Tooltip("Gravitational acceleration (negative value, e.g. -9.81).")]
    public float Gravity;

    /// <summary>
    /// Returns a config matching the original PawnMovementHandler default serialized values.
    /// </summary>
    public static MovementConfig Default => new()
    {
        MaxSpeedXZ = 12.5f,
        SpeedUpTime = 0.075f,
        SlowDownTime = 0.075f,
        AirSpeedBonus = 5f,
        AirControlDivisor = 12f,
        JumpHeight = 1.5f,
        MarioJumpTime = 0.2f,
        JumpBufferWindow = 0.3f,
        SpeedOnHit = 7f,
        MinimumAngleOnHit = 35f,
        Gravity = -20f,
    };
}
