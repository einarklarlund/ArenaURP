using UnityEngine;

/// <summary>
/// Pure, stateless processor that advances movement simulation by one step.
/// </summary>
public static class MovementProcessor
{
    /// <summary>
    /// Advance the movement simulation by one tick.
    /// </summary>
    /// <param name="state">Current movement state.</param>
    /// <param name="config">Movement tuning parameters.</param>
    /// <param name="input">All inputs that will affect the movement state.</param>
    /// <param name="deltaTime">Time step for this tick.</param>
    /// <returns>The new movement state after this tick.</returns>
    public static MovementState Process(
        MovementState state,
        in MovementConfig config,
        in MovementInput input,
        float deltaTime)
    {
        state.IsGrounded = input.IsGrounded;

        // --- Combine modifiers ---
        float walkSpeedMult = 1f;
        float walkAccelMult = 1f;
        float jumpMult = 1f;
        float gravMult = 1f;
        float preImpulseVelMult = 1f;
        foreach (var mod in input.Modifiers)
        {
            walkSpeedMult *= mod.WalkSpeedMultiplier;
            walkAccelMult *= mod.WalkAccelerationMultiplier;
            jumpMult *= mod.JumpForceMultiplier;
            gravMult *= mod.GravityMultiplier;
            preImpulseVelMult *= mod.PreImpulseVelocityMultiplier;
        }

        float effectiveMaxSpeed = config.MaxSpeedXZ * walkSpeedMult;
        float effectiveAirMaxSpeed = (config.MaxSpeedXZ + config.AirSpeedBonus) * walkSpeedMult;

        // --- Horizontal velocity ---
        float activeMaxSpeedXZ = input.IsGrounded ? effectiveMaxSpeed : effectiveAirMaxSpeed;
        Vector3 velocityXZ = CalculateXZVelocity(
            state, config, input, deltaTime, activeMaxSpeedXZ, effectiveMaxSpeed, walkAccelMult);

        // --- Vertical velocity ---
        float velocityY = CalculateYVelocity(
            ref state, config, input.JumpPressed, input.IsGrounded,
            deltaTime, jumpMult, gravMult);

        // --- Combine ---
        Vector3 finalVelocity = new(velocityXZ.x, velocityY, velocityXZ.z);

        // --- Pre-impulse velocity scaling ---
        finalVelocity *= preImpulseVelMult;

        // --- Apply external impulses ---
        if (input.Impulses != null)
        {
            foreach (var impulse in input.Impulses)
                finalVelocity += impulse;
        }

        state.Velocity = finalVelocity;
        return state;
    }

    /// <summary>
    /// Calculates horizontal (XZ) velocity for this frame.
    /// </summary>
    private static Vector3 CalculateXZVelocity(
        in MovementState state,
        in MovementConfig config,
        in MovementInput input,
        float deltaTime,
        float activeMaxSpeedXZ,
        float effectiveMaxSpeed,
        float accelMult)
    {
        Vector3 previousVelocityXZ = Vector3.ProjectOnPlane(state.Velocity, Vector3.up);

        Vector3 inputDirection = new(input.Move.x, 0f, input.Move.y);
        inputDirection = Vector3.ClampMagnitude(inputDirection, 1f);

        // Drag (grounded, no input)
        if (inputDirection == Vector3.zero && input.IsGrounded)
        {
            float dragAcceleration = activeMaxSpeedXZ / config.SlowDownTime;
            float deltaSpeedFromDrag = dragAcceleration * deltaTime;

            if (previousVelocityXZ.magnitude - deltaSpeedFromDrag <= deltaSpeedFromDrag)
            {
                return Vector3.zero;
            }

            return Vector3.ClampMagnitude(
                previousVelocityXZ,
                previousVelocityXZ.magnitude - deltaSpeedFromDrag);
        }

        // No drag (airborne, no input)
        if (inputDirection == Vector3.zero && !input.IsGrounded)
        {
            return previousVelocityXZ;
        }

        // Acceleration (input present)
        Vector3 inputDirectionWorld = input.WorldOrientation * inputDirection;
        Vector3 inputVelocity = inputDirectionWorld * activeMaxSpeedXZ;
        float walkAcceleration = activeMaxSpeedXZ / config.SpeedUpTime * accelMult;

        if (!input.IsGrounded)
        {
            walkAcceleration /= config.AirControlDivisor;
        }

        float deltaSpeedFromInput = walkAcceleration * deltaTime;
        Vector3 deltaVelocity = Vector3.ClampMagnitude(
            inputVelocity - previousVelocityXZ, deltaSpeedFromInput);

        // Final clamp uses base max speed (not air-boosted), matching original behaviour.
        return Vector3.ClampMagnitude(previousVelocityXZ + deltaVelocity, effectiveMaxSpeed);
    }

    /// <summary>
    /// Calculates vertical (Y) velocity for this frame.
    /// </summary>
    private static float CalculateYVelocity(
        ref MovementState state,
        in MovementConfig config,
        bool jumpPressed,
        bool isGrounded,
        float deltaTime,
        float jumpMult,
        float gravMult)
    {
        // Track jump press timing
        if (jumpPressed)
        {
            state.TimeSinceJumpPressed = 0f;
        }
        else
        {
            state.TimeSinceJumpPressed += deltaTime;
            // Interrupt mario jump on release
            state.MarioJumpInterrupted = true;
        }

        if (!isGrounded)
        {
            return GetAerialYVelocity(ref state, config, deltaTime, jumpMult, gravMult);
        }
        else
        {
            return GetGroundedYVelocity(ref state, config, deltaTime, jumpMult);
        }
    }

    private static float GetAerialYVelocity(
        ref MovementState state,
        in MovementConfig config,
        float deltaTime,
        float jumpMult,
        float gravMult)
    {
        float yVelocity = state.Velocity.y;

        state.TimeSinceJumpBegan += deltaTime;

        // Continue mario jump if not interrupted and within time window
        if (!state.MarioJumpInterrupted && state.TimeSinceJumpBegan <= config.MarioJumpTime)
        {
            yVelocity = GetJumpYVelocity(config, jumpMult);
        }

        // Apply gravity
        yVelocity += config.Gravity * gravMult * deltaTime;
        return yVelocity;
    }

    private static float GetGroundedYVelocity(
        ref MovementState state,
        in MovementConfig config,
        float deltaTime,
        float jumpMult)
    {
        state.TimeSinceJumpBegan += deltaTime;

        bool isJumpBuffered = state.TimeSinceJumpPressed <= config.JumpBufferWindow;

        if (isJumpBuffered)
        {
            state.MarioJumpInterrupted = false;
            state.TimeSinceJumpBegan = 0f;
            // Consume the buffered press so it doesn't fire again
            state.TimeSinceJumpPressed = MovementState.JumpTimeNotSet;
            return GetJumpYVelocity(config, jumpMult);
        }

        return 0f;
    }

    /// <summary>
    /// Jump velocity derived from desired peak height: v = sqrt(-1 * h * g).
    /// </summary>
    public static float GetJumpYVelocity(in MovementConfig config, float jumpMult = 1f)
    {
        return Mathf.Sqrt(-1f * config.JumpHeight * config.Gravity) * jumpMult;
    }

}
