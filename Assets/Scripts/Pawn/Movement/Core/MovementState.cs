using UnityEngine;

/// <summary>
/// Everything movement carries from one tick to the next. Velocity is in m/s.
/// </summary>
[System.Serializable]
public struct MovementState
{
    public Vector3 Velocity;

    public bool IsGrounded;

    /// <summary>Normal of the ground under the pawn; straight up when airborne.</summary>
    public Vector3 GroundNormal;

    /// <summary>Jump was held last tick. A jump needs a fresh press (Source's "don't pogo stick").</summary>
    public bool JumpWasHeld;

    /// <summary>Seconds left on a buffered jump press; see <see cref="MovementConfig.JumpBufferTime"/>.</summary>
    public float JumpBuffer;

    /// <summary>The hull is crouch-sized (Source's FL_DUCKING).</summary>
    public bool IsDucked;

    /// <summary>How far the view has gone from standing (0) to crouched (1), by time.</summary>
    public float DuckFraction;

    public bool IsSprinting;
    public bool SprintWasHeld;

    /// <summary>HL2 suit power, 0 to <see cref="MovementConfig.MaxSuitPower"/>.</summary>
    public float SuitPower;

    /// <summary>Seconds until suit power starts recharging.</summary>
    public float SuitRechargeWait;

    /// <summary>Eye height above the feet in metres, for the camera.</summary>
    public float EyeHeight;

    public static MovementState Initial(in MovementConfig config) => new()
    {
        GroundNormal = Vector3.up,
        SuitPower = MovementConfig.MaxSuitPower,
        EyeHeight = config.StandEyeHeight * config.MetersPerUnit,
    };
}
