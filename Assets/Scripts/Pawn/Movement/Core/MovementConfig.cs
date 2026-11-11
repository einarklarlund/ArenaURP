using UnityEngine;

/// <summary>
/// Tuning for pawn movement, a port of Half-Life 2's player movement (Source SDK 2013 gamemovement.cpp).
/// Distances and speeds are in Source units (HU) like the cvars they come from, and are converted to metres with
/// <see cref="MetersPerUnit"/>; unitless rates (accelerate, friction) are used as they are.
/// </summary>
[System.Serializable]
public struct MovementConfig
{
    [Header("Scale and timing")]
    [Tooltip("Metres per Source unit. 2/72 makes HL2's 72-unit-tall player as tall as the 2 m CharacterController.")]
    public float MetersPerUnit;

    [Tooltip("Seconds per movement tick. HL2 runs at 0.015 (66.7 ticks per second).")]
    public float TickInterval;

    [Header("Speeds (units/s)")]
    [Tooltip("hl2_normspeed: running speed.")]
    public float NormalSpeed;

    [Tooltip("hl2_sprintspeed: speed while sprinting.")]
    public float SprintSpeed;

    [Tooltip("cl_forwardspeed / cl_sidespeed: what full stick or a key asks for, before it is capped at the max speed. " +
             "Above the max speed, keys always ask for exactly the max speed, and diagonals are no faster.")]
    public float InputSpeed;

    [Tooltip("sv_maxvelocity: no velocity component can go past this.")]
    public float MaxVelocity;

    [Header("Ground")]
    [Tooltip("sv_accelerate.")]
    public float Accelerate;

    [Tooltip("sv_friction.")]
    public float Friction;

    [Tooltip("sv_stopspeed: below this speed, friction slows at a fixed rate instead of a proportional one.")]
    public float StopSpeed;

    [Tooltip("sv_stepsize: the height the pawn walks up and down without leaving the ground.")]
    public float StepSize;

    [Tooltip("Steepest ground the pawn stands on, as the minimum up component of its normal (0.7 is about 45.6 degrees).")]
    public float MinGroundNormalY;

    [Header("Air")]
    [Tooltip("sv_airaccelerate.")]
    public float AirAccelerate;

    [Tooltip("GetAirSpeedCap(): air acceleration only pushes speed along the wish direction up to this. " +
             "What makes air-strafing work.")]
    public float AirSpeedCap;

    [Tooltip("sv_gravity.")]
    public float Gravity;

    [Header("Jumping")]
    [Tooltip("Upward speed a jump adds (HL2: 160, about 21 units high at gravity 600).")]
    public float JumpSpeed;

    [Tooltip("Seconds a jump press counts for: pressed in the air this long before landing, the pawn jumps on landing " +
             "even if the button is still held. Not in HL2, where a press only counts on a tick on the ground: 0 is HL2 " +
             "exactly. A buffered jump on the first tick on the ground skips friction, as a perfectly timed one does.")]
    public float JumpBufferTime;

    [Tooltip("Rising faster than this keeps the pawn off the ground (NON_JUMP_VELOCITY).")]
    public float NonJumpVelocity;

    [Tooltip("HL2 single player's forward speed bonus on jumping (the one behind accelerated back-hopping). " +
             "Multiplayer HL2 doesn't have it.")]
    public bool JumpForwardBoost;

    [Header("Hull")]
    [Tooltip("Hull width (VEC_HULL_MAX.x - VEC_HULL_MIN.x). The CharacterController's diameter.")]
    public float HullWidth;

    [Header("Crouching")]
    [Tooltip("Hull height standing (VEC_HULL_MAX.z).")]
    public float StandHeight;

    [Tooltip("Hull height crouched (VEC_DUCK_HULL_MAX.z).")]
    public float CrouchHeight;

    [Tooltip("Eye height standing (VEC_VIEW.z).")]
    public float StandEyeHeight;

    [Tooltip("Eye height crouched (VEC_DUCK_VIEW.z).")]
    public float CrouchEyeHeight;

    [Tooltip("TIME_TO_DUCK: seconds to crouch on the ground. In the air, crouching is instant and tucks the legs up.")]
    public float TimeToDuck;

    [Tooltip("TIME_TO_UNDUCK: seconds to stand up on the ground.")]
    public float TimeToUnduck;

    [Tooltip("Speed scale while crouched on the ground.")]
    public float CrouchSpeedScale;

    [Header("Sprint (HL2 suit power)")]
    [Tooltip("Suit power used per second of sprinting while moving. 0 makes sprint unlimited.")]
    public float SprintDrainPerSecond;

    [Tooltip("Suit power each jump uses; a jump needs at least this much. Jumping also restarts the recharge delay. " +
             "0 makes jumping free. Not in HL2.")]
    public float JumpEnergyCost;

    [Tooltip("Suit power regained per second while not sprinting.")]
    public float SuitChargePerSecond;

    [Tooltip("Seconds after sprinting stops before suit power recharges.")]
    public float SuitRechargeDelay;

    [Tooltip("Suit power needed to start sprinting. Full power is 100.")]
    public float SprintMinPower;

    [Header("Ground probe")]
    public LayerMask GroundMask;

    public const float MaxSuitPower = 100f;

    /// <summary>Half-Life 2's values (Source SDK 2013 cvar defaults and hl2_player.cpp).</summary>
    public static MovementConfig Default => new()
    {
        MetersPerUnit = 2f / 72f,
        TickInterval = 0.015f,
        NormalSpeed = 190f,
        SprintSpeed = 320f,
        InputSpeed = 450f,
        MaxVelocity = 3500f,
        Accelerate = 10f,
        Friction = 4f,
        StopSpeed = 100f,
        StepSize = 18f,
        MinGroundNormalY = 0.7f,
        AirAccelerate = 10f,
        AirSpeedCap = 30f,
        Gravity = 600f,
        JumpSpeed = 160f,
        JumpBufferTime = 0.1f,
        NonJumpVelocity = 140f,
        JumpForwardBoost = false,
        HullWidth = 32f,
        StandHeight = 72f,
        CrouchHeight = 36f,
        StandEyeHeight = 64f,
        CrouchEyeHeight = 28f,
        TimeToDuck = 0.4f,
        TimeToUnduck = 0.2f,
        CrouchSpeedScale = 1f / 3f,
        // Faster than HL2's 12.5: a full bar lasts 4 s of sprinting, or 8 jumps
        SprintDrainPerSecond = 25f,
        JumpEnergyCost = 12.5f,
        SuitChargePerSecond = 12.5f,
        SuitRechargeDelay = 0.5f,
        SprintMinPower = 10f,
    };
}
