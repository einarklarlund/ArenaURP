using UnityEngine;

/// <summary>
/// Stateless processor that advances movement simulation by one step.
/// Pure apart from the ground probe against the physics scene.
/// </summary>
public static class MovementProcessor
{
    // Shrinks the ground probe so it starts clear of the ground it rests on; a cast ignores what it starts inside.
    private const float ProbeShrink = 0.05f;
    // How far above resting height the ground still holds the character, and is snapped down to. Deeper drops
    // are ledges to walk off.
    private const float MaxSnapDistance = 0.15f;

    /// <summary>
    /// Advance the movement simulation by one tick.
    /// </summary>
    /// <param name="controllerState">The CharacterController as it stands this tick.</param>
    /// <param name="input">Player controls for this tick.</param>
    /// <param name="config">Movement tuning parameters.</param>
    /// <param name="jump">Jump state carried between ticks; updated in place.</param>
    /// <param name="impulse">Sum of external impulses to apply this tick (added after velocity scaling).</param>
    /// <param name="speedMultiplier">Scales max speed on the ground and in the air (e.g. 1.125 for +12.5%).
    /// Acceleration scales with it, so reaching full speed takes as long as before.</param>
    /// <param name="deltaTime">Time step for this tick.</param>
    /// <param name="snapVelocity">Extra velocity that pulls the character down onto the ground this tick. It
    /// isn't momentum: move by it too, and pass it back as <see cref="MovementState.SnapVelocity"/>.</param>
    /// <returns>The velocity to move the CharacterController at this tick.</returns>
    public static Vector3 Process(
        in MovementState controllerState,
        in MovementInputData input,
        in MovementConfig config,
        ref JumpState jump,
        Vector3 impulse,
        float speedMultiplier,
        float deltaTime,
        out Vector3 snapVelocity)
    {
        MovementState state = controllerState;
        // Last tick's snap moved the character, but isn't momentum
        state.Velocity -= state.SnapVelocity;
        // Nor is what the CharacterController adds on its own, popping up a step or out of a seam; it can only
        // take vertical speed away (landing, ceilings)
        state.Velocity.y = Mathf.Min(state.Velocity.y, Mathf.Max(state.RequestedVelocity.y, 0f));

        // --- Combine modifiers ---
        float walkSpeedMult = speedMultiplier;
        float walkAccelMult = 1f;
        float jumpMult = 1f;
        float gravMult = 1f;
        float preImpulseVelMult = 1f;
        float effectiveMaxSpeed = config.MaxSpeedXZ * walkSpeedMult;
        float effectiveAirMaxSpeed = (config.MaxSpeedXZ + config.AirSpeedBonus) * walkSpeedMult;

        // A jump or knockback lasts until the character starts falling
        if (state.Velocity.y <= 0f)
        {
            jump.IsLaunched = false;
        }

        // --- Ground ---
        bool isGrounded = TryGetGround(state, config, jump.IsLaunched, out Vector3 groundNormal);

        // --- Velocity along the ground (the XZ plane in the air) ---
        float activeMaxSpeedXZ = isGrounded ? effectiveMaxSpeed : effectiveAirMaxSpeed;
        Vector3 planarVelocity = CalculatePlanarVelocity(
            state, config, input, isGrounded, groundNormal, deltaTime, activeMaxSpeedXZ, effectiveMaxSpeed, walkAccelMult);

        // --- Vertical velocity ---
        float velocityY = CalculateYVelocity(
            ref jump, state, config, input, isGrounded,
            deltaTime, jumpMult, gravMult);

        // Jumping: leave the slope plane, or running downhill would cancel the jump
        if (isGrounded && velocityY > 0f)
        {
            planarVelocity = Flatten(planarVelocity);
        }

        // --- Combine ---
        Vector3 finalVelocity = planarVelocity + Vector3.up * velocityY;

        // --- Pre-impulse velocity scaling ---
        finalVelocity *= preImpulseVelMult;

        // --- Apply external impulses ---
        finalVelocity += impulse;

        if ((isGrounded && velocityY > 0f) || impulse.y > 0f)
        {
            jump.IsLaunched = true;
        }

        // --- Stay on the ground ---
        snapVelocity = isGrounded && !jump.IsLaunched
            ? SnapToGround(finalVelocity, state, config, deltaTime)
            : Vector3.zero;

        Debug.DrawRay(state.Position, finalVelocity * 0.1f, Color.white);

        return finalVelocity;
    }

    /// <summary>
    /// Calculates this frame's velocity along the plane with normal <paramref name="groundNormal"/>:
    /// the ground's slope when grounded, the XZ plane in the air.
    /// </summary>
    private static Vector3 CalculatePlanarVelocity(
        in MovementState state,
        in MovementConfig config,
        in MovementInputData input,
        bool isGrounded,
        Vector3 groundNormal,
        float deltaTime,
        float activeMaxSpeedXZ,
        float effectiveMaxSpeed,
        float accelMult)
    {
        Vector3 previousVelocity = Vector3.ProjectOnPlane(state.Velocity, groundNormal);

        Vector3 inputDirection = new(input.Move.x, 0f, input.Move.y);
        inputDirection = Vector3.ClampMagnitude(inputDirection, 1f);

        // Drag (grounded, no input)
        if (inputDirection == Vector3.zero && isGrounded)
        {
            float dragAcceleration = activeMaxSpeedXZ / config.SlowDownTime;
            float deltaSpeedFromDrag = dragAcceleration * deltaTime;

            if (previousVelocity.magnitude - deltaSpeedFromDrag <= deltaSpeedFromDrag)
            {
                return Vector3.zero;
            }

            return Vector3.ClampMagnitude(
                previousVelocity,
                previousVelocity.magnitude - deltaSpeedFromDrag);
        }

        // No drag (airborne, no input)
        if (inputDirection == Vector3.zero && !isGrounded)
        {
            return previousVelocity;
        }

        // Acceleration (input present)
        // Tilted onto the slope at full length, so walking up or down it is as fast as on flat ground
        Vector3 inputDirectionWorld = state.Rotation * inputDirection;
        inputDirectionWorld = Vector3.ProjectOnPlane(inputDirectionWorld, groundNormal).normalized * inputDirectionWorld.magnitude;
        Vector3 inputVelocity = inputDirectionWorld * activeMaxSpeedXZ;
        float walkAcceleration = activeMaxSpeedXZ / config.SpeedUpTime * accelMult;

        if (!isGrounded)
        {
            walkAcceleration /= config.AirControlDivisor;
        }

        float deltaSpeedFromInput = walkAcceleration * deltaTime;
        Vector3 deltaVelocity = Vector3.ClampMagnitude(
            inputVelocity - previousVelocity, deltaSpeedFromInput);

        // Final clamp uses base max speed (not air-boosted), matching original behaviour.
        // Airborne, speed already above that (e.g. from knockback impulses) isn't cut off: it
        // can only be steered or slowed by input, and carries until landing, where the grounded
        // clamp takes over.
        float clampSpeed = isGrounded
            ? effectiveMaxSpeed
            : Mathf.Max(effectiveMaxSpeed, previousVelocity.magnitude);
        return Vector3.ClampMagnitude(previousVelocity + deltaVelocity, clampSpeed);
    }

    /// <summary>
    /// Calculates vertical (Y) velocity for this frame.
    /// </summary>
    private static float CalculateYVelocity(
        ref JumpState jump,
        in MovementState state,
        in MovementConfig config,
        in MovementInputData input,
        bool isGrounded,
        float deltaTime,
        float jumpMult,
        float gravMult)
    {
        // Track jump press timing
        if (input.Jump)
        {
            jump.TimeSinceJumpPressed = 0f;
        }
        else
        {
            jump.TimeSinceJumpPressed += deltaTime;
            // Interrupt mario jump on release
            jump.MarioJumpInterrupted = true;
        }

        if (!isGrounded)
        {
            return GetAerialYVelocity(ref jump, state, config, deltaTime, jumpMult, gravMult);
        }
        else
        {
            return GetGroundedYVelocity(ref jump, config, deltaTime, jumpMult);
        }
    }

    private static float GetAerialYVelocity(
        ref JumpState jump,
        in MovementState state,
        in MovementConfig config,
        float deltaTime,
        float jumpMult,
        float gravMult)
    {
        float yVelocity = state.Velocity.y;

        jump.TimeSinceJumpBegan += deltaTime;

        // Continue mario jump if not interrupted and within time window and y velocity is not greater than jump y velocity
        var jumpYVelocity = GetJumpYVelocity(config, jumpMult);
        if (!jump.MarioJumpInterrupted && jump.TimeSinceJumpBegan <= config.MarioJumpTime && yVelocity <= jumpYVelocity)
        {
            yVelocity = GetJumpYVelocity(config, jumpMult);
        }

        // Apply gravity
        yVelocity += config.Gravity * gravMult * deltaTime;
        return yVelocity;
    }

    private static float GetGroundedYVelocity(
        ref JumpState jump,
        in MovementConfig config,
        float deltaTime,
        float jumpMult)
    {
        jump.TimeSinceJumpBegan += deltaTime;

        bool isJumpBuffered = jump.TimeSinceJumpPressed <= config.JumpBufferWindow;

        if (isJumpBuffered)
        {
            jump.MarioJumpInterrupted = false;
            jump.TimeSinceJumpBegan = 0f;
            // Consume the buffered press so it doesn't fire again
            jump.TimeSinceJumpPressed = JumpState.JumpTimeNotSet;
            return GetJumpYVelocity(config, jumpMult);
        }

        return 0f;
    }

    /// <summary>
    /// Rotates a velocity onto the XZ plane, keeping its speed.
    /// </summary>
    private static Vector3 Flatten(Vector3 velocity)
    {
        return Vector3.ProjectOnPlane(velocity, Vector3.up).normalized * velocity.magnitude;
    }

    /// <summary>
    /// Jump velocity derived from desired peak height: v = sqrt(-1 * h * g).
    /// </summary>
    public static float GetJumpYVelocity(in MovementConfig config, float jumpMult = 1f)
    {
        return Mathf.Sqrt(-1f * config.JumpHeight * config.Gravity) * jumpMult;
    }

    /// <summary>
    /// Whether the capsule rests on walkable ground, and that ground's normal (straight up when it doesn't).
    /// Sphere-casts the bottom of the capsule down rather than trusting CharacterController.isGrounded, which
    /// only sees ground the capsule hit while moving down, so never while walking up a slope. The cast's normal
    /// is the contact's: it tilts gradually as the capsule rolls over an edge, so walking follows the edge
    /// instead of leaving it.
    /// </summary>
    private static bool TryGetGround(in MovementState state, in MovementConfig config, bool isLaunched, out Vector3 normal)
    {
        normal = Vector3.up;

        // Rising from a jump or knockback: the probe can still reach the ground for a few frames
        if (isLaunched)
        {
            return false;
        }

        // At rest the capsule floats SkinWidth off the ground, measured along the ground's normal.
        float restGap = state.SkinWidth + ProbeShrink;
        float maxSlopeCos = Mathf.Cos(state.SlopeLimit * Mathf.Deg2Rad);
        Vector3 origin = state.Position + Vector3.up * state.Radius;

        bool hasHit = Physics.SphereCast(
            origin,
            state.Radius - ProbeShrink,
            Vector3.down,
            out RaycastHit hit,
            restGap / maxSlopeCos + MaxSnapDistance,
            config.GroundMask,
            QueryTriggerInteraction.Ignore
        );

        float slopeCos = hasHit ? Vector3.Dot(hit.normal, Vector3.up) : 0f;
        bool isGrounded = hasHit
            && slopeCos >= maxSlopeCos
            && hit.distance <= restGap / slopeCos + MaxSnapDistance;

        Debug.DrawRay(origin, Vector3.down * (state.Radius + restGap / maxSlopeCos + MaxSnapDistance),
            isGrounded ? Color.cyan : Color.magenta);

        if (isGrounded)
        {
            normal = hit.normal;
            Debug.DrawRay(hit.point, hit.normal * 0.5f, Color.yellow);
        }

        return isGrounded;
    }

    /// <summary>
    /// The velocity that lowers a grounded move so it ends resting on the ground instead of hovering over it.
    /// Walking along the ground's tilt overshoots wherever the ground bends down, as over the top of a ramp, and a wall
    /// that blocks a move up a slope lifts the capsule off it.
    /// </summary>
    private static Vector3 SnapToGround(Vector3 velocity, in MovementState state, in MovementConfig config, float deltaTime)
    {
        float restGap = state.SkinWidth + ProbeShrink;
        float maxSlopeCos = Mathf.Cos(state.SlopeLimit * Mathf.Deg2Rad);
        Vector3 endOrigin = state.Position + Vector3.up * state.Radius + velocity * deltaTime;

        if (
            !Physics.SphereCast(
                endOrigin,
                state.Radius - ProbeShrink,
                Vector3.down,
                out RaycastHit hit,
                restGap / maxSlopeCos + MaxSnapDistance,
                config.GroundMask,
                QueryTriggerInteraction.Ignore
            )
        )
        {
            return Vector3.zero;
        }

        float slopeCos = Vector3.Dot(hit.normal, Vector3.up);
        if (slopeCos < maxSlopeCos)
        {
            return Vector3.zero;
        }

        float hover = hit.distance - restGap / slopeCos;
        if (hover <= 0f || hover > MaxSnapDistance)
        {
            return Vector3.zero;
        }

        return Vector3.down * (hover / deltaTime);
    }

}
