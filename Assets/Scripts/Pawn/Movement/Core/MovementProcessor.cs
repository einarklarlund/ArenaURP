using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Advances pawn movement by one fixed tick: Half-Life 2's player movement (Source SDK 2013
/// CGameMovement::PlayerMove and FullWalkMove, with HL2's sprint and crouch), on an <see cref="IMovementBody"/>.
/// The order of steps matters and follows Source's: for example, a jump leaves the ground before friction runs, so
/// jumping again on landing loses no speed to friction.
/// </summary>
public static class MovementProcessor
{
    // Main thread only
    private static readonly List<Vector3> Hits = new();
    private static readonly List<Vector3> Planes = new();

    /// <param name="state">Carried between ticks; updated in place.</param>
    /// <param name="body">The hull to move.</param>
    /// <param name="input">Controls for this tick.</param>
    /// <param name="config">Tuning, in Source units.</param>
    /// <param name="yaw">Which way the pawn faces.</param>
    /// <param name="speedMultiplier">Scales max speed (e.g. 1.125 for +12.5%). Acceleration is in proportion to the
    /// wish speed, so reaching full speed takes as long as before.</param>
    /// <param name="impulse">Velocity change (m/s) from outside since the last tick, such as knockback.</param>
    /// <param name="dt">Tick length; <see cref="MovementConfig.TickInterval"/> for HL2's feel.</param>
    public static void Tick(
        ref MovementState state,
        IMovementBody body,
        in MovementInputData input,
        in MovementConfig config,
        Quaternion yaw,
        float speedMultiplier,
        Vector3 impulse,
        float dt)
    {
        float u = config.MetersPerUnit;

        // Game code changed the velocity: see where that leaves us, as PlayerMove does when game code moved the player.
        // Knocked upward faster than NonJumpVelocity, the pawn leaves the ground.
        if (impulse != Vector3.zero)
        {
            state.Velocity += impulse;
            CategorizePosition(ref state, body, config);
        }

        UpdateSprint(ref state, input, config, dt);

        float maxSpeed = (state.IsSprinting ? config.SprintSpeed : config.NormalSpeed) * u * speedMultiplier;
        Vector2 move = SourceMovement.InputMove(input.Move, config.InputSpeed * u * speedMultiplier, maxSpeed);

        Duck(ref state, body, input.Crouch, config, dt);
        // HandleDuckingSpeedCrop
        if (state.IsDucked && state.IsGrounded)
            move *= config.CrouchSpeedScale;

        // --- FullWalkMove ---
        StartGravity(ref state, config, dt);

        // A fresh press starts the buffer; a buffered press stands in for a fresh one until it runs out
        if (input.JumpPressed || (input.Jump && !state.JumpWasHeld))
            state.JumpBuffer = config.JumpBufferTime;

        if (input.Jump || state.JumpBuffer > 0f)
            CheckJumpButton(ref state, config, yaw, move.y, maxSpeed, dt);

        if (state.IsGrounded)
        {
            state.Velocity.y = 0f;
            state.Velocity = SourceMovement.ApplyFriction(state.Velocity, config.Friction, config.StopSpeed * u, 0.1f * u, dt);
        }

        state.Velocity = SourceMovement.ClampVelocity(state.Velocity, config.MaxVelocity * u);

        if (state.IsGrounded)
            WalkMove(ref state, body, config, yaw, move, maxSpeed, dt);
        else
            AirMove(ref state, body, config, yaw, move, maxSpeed, dt);

        CategorizePosition(ref state, body, config);

        state.Velocity = SourceMovement.ClampVelocity(state.Velocity, config.MaxVelocity * u);

        FinishGravity(ref state, config, dt);

        if (state.IsGrounded)
            state.Velocity.y = 0f;

        // FinishMove: this tick's buttons are next tick's old buttons
        state.JumpWasHeld = input.Jump;
        state.JumpBuffer = Mathf.Max(0f, state.JumpBuffer - dt);

        float eyeUnits = Mathf.Lerp(config.StandEyeHeight, config.CrouchEyeHeight, SourceMovement.SimpleSpline(state.DuckFraction));
        state.EyeHeight = eyeUnits * u;
    }

    // Gravity is applied in two halves around the move, which integrates a constant fall exactly.
    private static void StartGravity(ref MovementState state, in MovementConfig config, float dt)
    {
        state.Velocity.y -= config.Gravity * config.MetersPerUnit * 0.5f * dt;
    }

    private static void FinishGravity(ref MovementState state, in MovementConfig config, float dt)
    {
        state.Velocity.y -= config.Gravity * config.MetersPerUnit * 0.5f * dt;
    }

    private static void CheckJumpButton(ref MovementState state, in MovementConfig config, Quaternion yaw, float forwardMove,
        float maxSpeed, float dt)
    {
        if (!state.IsGrounded)
            return;

        // Don't pogo stick: holding jump doesn't jump again, unless the press that is held is still buffered
        if (state.JumpWasHeld && state.JumpBuffer <= 0f)
            return;

        // Can't jump while standing up from a crouch
        if (state.IsDucked && state.DuckFraction < 1f)
            return;

        // Jumping costs suit power, and blocks its recharge as sprinting does
        if (config.JumpEnergyCost > 0f)
        {
            if (state.SuitPower < config.JumpEnergyCost)
                return;
            state.SuitPower -= config.JumpEnergyCost;
            state.SuitRechargeWait = config.SuitRechargeDelay;
        }

        state.IsGrounded = false;
        state.GroundNormal = Vector3.up;
        state.JumpBuffer = 0f;

        // Crouched or on the way down (m_bDucking || FL_DUCKING): the jump sets the upward speed instead of adding to it
        float jumpSpeed = config.JumpSpeed * config.MetersPerUnit;
        if (state.IsDucked || state.DuckFraction > 0f)
            state.Velocity.y = jumpSpeed;
        else
            state.Velocity.y += jumpSpeed;

        if (config.JumpForwardBoost)
            state.Velocity = SourceMovement.JumpForwardBoost(state.Velocity, yaw, forwardMove, maxSpeed, state.IsSprinting || state.IsDucked);

        // The jump tick gets this extra half of gravity in Source too
        FinishGravity(ref state, config, dt);
    }

    private static void WalkMove(ref MovementState state, IMovementBody body, in MovementConfig config, Quaternion yaw,
        Vector2 move, float maxSpeed, float dt)
    {
        float u = config.MetersPerUnit;
        SourceMovement.SplitWish(SourceMovement.WishVelocity(yaw, move), maxSpeed, out Vector3 wishDir, out float wishSpeed);

        state.Velocity.y = 0f;
        state.Velocity = SourceMovement.Accelerate(state.Velocity, wishDir, wishSpeed, config.Accelerate, dt);
        state.Velocity.y = 0f;

        if (state.Velocity.magnitude < 1f * u)
        {
            state.Velocity = Vector3.zero;
            return;
        }

        // Velocity on the ground is flat. Moving along the ground's tilt by its full flat length walks slopes at full
        // speed, as Source's step-up move does.
        Vector3 normal = state.GroundNormal;
        Vector3 displacement = state.Velocity * dt;
        displacement.y = -(normal.x * displacement.x + normal.z * displacement.z) / normal.y;

        Hits.Clear();
        Vector3 moved = body.Move(displacement, Hits);

        // The CharacterController climbs steps by itself; only what cut the flat move short slows the pawn
        Vector2 wanted = new(displacement.x, displacement.z);
        Vector2 got = new(moved.x, moved.z);
        if ((wanted - got).magnitude > 1e-4f + wanted.magnitude * 0.01f)
        {
            CollectPlanes(Hits, config.MinGroundNormalY);
            state.Velocity = SourceMovement.ClipToPlanes(state.Velocity, Planes);
        }

        body.StayOnGround(config.StepSize * u, config.MinGroundNormalY);
    }

    private static void AirMove(ref MovementState state, IMovementBody body, in MovementConfig config, Quaternion yaw,
        Vector2 move, float maxSpeed, float dt)
    {
        SourceMovement.SplitWish(SourceMovement.WishVelocity(yaw, move), maxSpeed, out Vector3 wishDir, out float wishSpeed);
        state.Velocity = SourceMovement.AirAccelerate(state.Velocity, wishDir, wishSpeed, config.AirAccelerate,
            config.AirSpeedCap * config.MetersPerUnit, dt);

        Hits.Clear();
        body.Move(state.Velocity * dt, Hits);

        if (Hits.Count > 0)
        {
            CollectPlanes(Hits, float.PositiveInfinity);
            state.Velocity = SourceMovement.ClipToPlanes(state.Velocity, Planes);
        }
    }

    /// <summary>Distinct hit normals flatter than <paramref name="maxNormalY"/> (ground is excluded on the ground,
    /// where velocity is flat anyway) into <see cref="Planes"/>.</summary>
    private static void CollectPlanes(List<Vector3> hits, float maxNormalY)
    {
        Planes.Clear();
        foreach (Vector3 normal in hits)
        {
            if (normal.y >= maxNormalY)
                continue;

            bool duplicate = false;
            foreach (Vector3 plane in Planes)
            {
                if (Vector3.Dot(plane, normal) > 0.99f)
                {
                    duplicate = true;
                    break;
                }
            }

            if (!duplicate)
                Planes.Add(normal);
        }
    }

    /// <summary>CategorizePosition(): on the ground if standable ground is within 2 units below, unless rising faster
    /// than <see cref="MovementConfig.NonJumpVelocity"/>.</summary>
    private static void CategorizePosition(ref MovementState state, IMovementBody body, in MovementConfig config)
    {
        float u = config.MetersPerUnit;
        if (state.Velocity.y > config.NonJumpVelocity * u)
        {
            state.IsGrounded = false;
            state.GroundNormal = Vector3.up;
            return;
        }

        state.IsGrounded = body.ProbeGround(2f * u, config.MinGroundNormalY, out state.GroundNormal);
    }

    /// <summary>
    /// HL2's crouch. On the ground the view lowers over <see cref="MovementConfig.TimeToDuck"/> and the hull shrinks
    /// at the end; in the air it shrinks at once with the top kept in place, tucking the legs up, which is what makes
    /// crouch-jumping reach higher. Standing up needs room for the full hull.
    /// </summary>
    private static void Duck(ref MovementState state, IMovementBody body, bool crouch, in MovementConfig config, float dt)
    {
        float u = config.MetersPerUnit;

        if (crouch)
        {
            state.DuckFraction = Mathf.Min(1f, state.DuckFraction + dt / config.TimeToDuck);

            if (!state.IsDucked && (!state.IsGrounded || state.DuckFraction >= 1f))
            {
                if (body.TrySetHeight(config.CrouchHeight * u, keepTop: !state.IsGrounded))
                {
                    state.IsDucked = true;
                    state.DuckFraction = 1f;
                    if (!state.IsGrounded)
                        CategorizePosition(ref state, body, config);
                }
            }

            return;
        }

        if (!state.IsDucked)
        {
            // Let go before the hull shrank
            state.DuckFraction = Mathf.Max(0f, state.DuckFraction - dt / config.TimeToUnduck);
            return;
        }

        float standHeight = config.StandHeight * u;

        if (!state.IsGrounded)
        {
            if (body.TrySetHeight(standHeight, keepTop: true))
            {
                state.IsDucked = false;
                state.DuckFraction = 0f;
                CategorizePosition(ref state, body, config);
            }

            return;
        }

        if (!body.HasRoomForHeight(standHeight, keepTop: false))
            return;

        state.DuckFraction = Mathf.Max(0f, state.DuckFraction - dt / config.TimeToUnduck);
        if (state.DuckFraction <= 0f && body.TrySetHeight(standHeight, keepTop: false))
            state.IsDucked = false;
    }

    /// <summary>
    /// HL2's sprint: starts on a press with enough suit power, stops on release, crouching, or running out. Sprinting
    /// while moving drains suit power; it recharges after a short wait once sprinting stops.
    /// </summary>
    private static void UpdateSprint(ref MovementState state, in MovementInputData input, in MovementConfig config, float dt)
    {
        bool pressed = input.Sprint && !state.SprintWasHeld;
        state.SprintWasHeld = input.Sprint;
        bool unlimited = config.SprintDrainPerSecond <= 0f;

        if (state.IsSprinting && (!input.Sprint || state.IsDucked || (!unlimited && state.SuitPower <= 0f)))
        {
            state.IsSprinting = false;
            state.SuitRechargeWait = config.SuitRechargeDelay;
        }
        else if (!state.IsSprinting && pressed && !state.IsDucked && (unlimited || state.SuitPower >= config.SprintMinPower))
        {
            state.IsSprinting = true;
        }

        if (state.IsSprinting)
        {
            Vector3 flat = state.Velocity;
            flat.y = 0f;
            if (!unlimited && flat.sqrMagnitude > config.MetersPerUnit * config.MetersPerUnit)
                state.SuitPower = Mathf.Max(0f, state.SuitPower - config.SprintDrainPerSecond * dt);
            return;
        }

        if (state.SuitRechargeWait > 0f)
        {
            state.SuitRechargeWait -= dt;
            return;
        }

        state.SuitPower = Mathf.Min(MovementConfig.MaxSuitPower, state.SuitPower + config.SuitChargePerSecond * dt);
    }
}
