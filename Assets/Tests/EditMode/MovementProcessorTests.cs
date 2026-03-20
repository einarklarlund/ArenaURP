using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Edit-mode unit tests for MovementProcessor.
/// These run instantly in the Unity Test Runner - no Play Mode, no networking, no GameObjects.
/// </summary>
[TestFixture]
public class MovementProcessorTests
{
    private MovementConfig _config;

    [SetUp]
    public void SetUp()
    {
        _config = MovementConfig.Default;
    }

    // =====================================================================
    // Helper
    // =====================================================================

    /// <summary>
    /// Run the processor for N frames with the given inputs.
    /// Returns the final state.
    /// </summary>
    private MovementState Simulate(
        MovementState state,
        Vector2 moveInput,
        bool jumpPressed,
        bool isGrounded,
        int frames,
        float dt = 1f / 60f,
        List<MovementModifier> modifiers = null,
        Quaternion? orientation = null,
        List<Vector3> impulsesOnLastFrame = null)
    {
        var input = new MovementInput
        {
            Move = moveInput,
            JumpPressed = jumpPressed,
            IsGrounded = isGrounded,
            WorldOrientation = orientation ?? Quaternion.identity,
            Modifiers = modifiers ?? new List<MovementModifier>(),
        };

        for (int i = 0; i < frames; i++)
        {
            if (i == frames - 1 && impulsesOnLastFrame != null)
            {
                input.Impulses = impulsesOnLastFrame;
            }
            else
            {
                input.Impulses = null;
            }

            state = MovementProcessor.Process(state, in _config, in input, dt);
        }

        return state;
    }

    // =====================================================================
    // Ground Movement
    // =====================================================================

    [Test]
    public void Stationary_OnGround_NoInput_VelocityRemainsZero()
    {
        var state = MovementState.Default;
        state = Simulate(state, Vector2.zero, false, isGrounded: true, frames: 60);

        Assert.AreEqual(Vector3.zero, state.Velocity);
    }

    [Test]
    public void ForwardInput_OnGround_AcceleratesForward()
    {
        var state = MovementState.Default;

        // One frame of forward input
        state = Simulate(state, Vector2.up, false, isGrounded: true, frames: 1);

        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(state.Velocity, Vector3.up);
        Assert.Greater(horizontalVelocity.magnitude, 0f,
            "Forward input should produce positive horizontal velocity.");
        Assert.Greater(state.Velocity.z, 0f,
            "Forward input (Vector2.up) maps to +Z with identity orientation.");
    }

    [Test]
    public void ForwardInput_OnGround_ReachesMaxSpeed()
    {
        var state = MovementState.Default;

        // Simulate enough frames to reach max speed (speedUpTime = 0.3s at 60fps = 18 frames, add margin)
        state = Simulate(state, Vector2.up, false, isGrounded: true, frames: 60);

        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(state.Velocity, Vector3.up);
        Assert.AreEqual(_config.MaxSpeedXZ, horizontalVelocity.magnitude, 0.1f,
            "Should reach max horizontal speed after sufficient acceleration time.");
    }

    [Test]
    public void ReleasingInput_OnGround_Decelerates()
    {
        var state = MovementState.Default;

        // Accelerate to speed
        state = Simulate(state, Vector2.up, false, isGrounded: true, frames: 60);
        float speedBefore = Vector3.ProjectOnPlane(state.Velocity, Vector3.up).magnitude;

        // Release input for a few frames
        state = Simulate(state, Vector2.zero, false, isGrounded: true, frames: 5);
        float speedAfter = Vector3.ProjectOnPlane(state.Velocity, Vector3.up).magnitude;

        Assert.Less(speedAfter, speedBefore, "Speed should decrease when input is released on ground.");
    }

    [Test]
    public void ReleasingInput_OnGround_EventuallyStops()
    {
        var state = MovementState.Default;

        // Accelerate to max speed
        state = Simulate(state, Vector2.up, false, isGrounded: true, frames: 60);

        // Release input for a long time (slowDownTime = 0.2s, so 60 frames at 60fps is plenty)
        state = Simulate(state, Vector2.zero, false, isGrounded: true, frames: 60);

        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(state.Velocity, Vector3.up);
        Assert.AreEqual(0f, horizontalVelocity.magnitude, 0.01f,
            "Pawn should come to a full stop after releasing input on ground.");
    }

    [Test]
    public void HorizontalSpeed_ClampedToMaxSpeedXZ()
    {
        var state = MovementState.Default;

        // Simulate for a very long time
        state = Simulate(state, Vector2.up, false, isGrounded: true, frames: 600);

        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(state.Velocity, Vector3.up);
        Assert.LessOrEqual(horizontalVelocity.magnitude, _config.MaxSpeedXZ + 0.01f,
            "Horizontal speed should never exceed MaxSpeedXZ.");
    }

    // =====================================================================
    // Air Control
    // =====================================================================

    [Test]
    public void AirControl_AccelerationIsReduced()
    {
        // Ground acceleration for 1 frame
        var groundState = MovementState.Default;
        groundState = Simulate(groundState, Vector2.up, false, isGrounded: true, frames: 1);
        float groundDelta = Vector3.ProjectOnPlane(groundState.Velocity, Vector3.up).magnitude;

        // Air acceleration for 1 frame
        var airState = MovementState.Default;
        airState = Simulate(airState, Vector2.up, false, isGrounded: false, frames: 1);
        float airDelta = Vector3.ProjectOnPlane(airState.Velocity, Vector3.up).magnitude;

        Assert.Less(airDelta, groundDelta,
            "Air acceleration should be significantly less than ground acceleration.");
    }

    [Test]
    public void Airborne_NoInput_DoesNotDecelerate()
    {
        var state = MovementState.Default;

        // Get some horizontal velocity on the ground
        state = Simulate(state, Vector2.up, false, isGrounded: true, frames: 30);
        float speedBefore = Vector3.ProjectOnPlane(state.Velocity, Vector3.up).magnitude;
        Assert.Greater(speedBefore, 0f);

        // Release input while airborne - should maintain horizontal speed (no drag in air)
        state = Simulate(state, Vector2.zero, false, isGrounded: false, frames: 10);
        float speedAfter = Vector3.ProjectOnPlane(state.Velocity, Vector3.up).magnitude;

        Assert.AreEqual(speedBefore, speedAfter, 0.01f,
            "Releasing input in the air should not apply drag.");
    }

    // =====================================================================
    // Jumping
    // =====================================================================

    [Test]
    public void Jump_OnGround_ProducesPositiveYVelocity()
    {
        var state = MovementState.Default;

        state = Simulate(state, Vector2.zero, jumpPressed: true, isGrounded: true, frames: 1);

        Assert.Greater(state.Velocity.y, 0f, "Jumping should produce upward velocity.");
    }

    [Test]
    public void Jump_YVelocity_MatchesExpectedFormula()
    {
        var state = MovementState.Default;

        state = Simulate(state, Vector2.zero, jumpPressed: true, isGrounded: true, frames: 1);

        float expected = Mathf.Sqrt(-1f * _config.JumpHeight * _config.Gravity);
        Assert.AreEqual(expected, state.Velocity.y, 0.01f,
            "Initial jump velocity should match sqrt(-jumpHeight * gravity).");
    }

    [Test]
    public void JumpBuffer_LateJump_StillFires()
    {
        var state = MovementState.Default;
        float dt = 1f / 60f;

        // Press jump while airborne
        state = Simulate(state, Vector2.zero, jumpPressed: true, isGrounded: false, frames: 1, dt);

        // Release jump but land within the buffer window
        // Buffer window is 0.2s. At 60fps, that's 12 frames. Land after 5 frames.
        state = Simulate(state, Vector2.zero, jumpPressed: false, isGrounded: false, frames: 5, dt);

        // Land - the buffered jump should fire
        state = Simulate(state, Vector2.zero, jumpPressed: false, isGrounded: true, frames: 1, dt);

        Assert.Greater(state.Velocity.y, 0f,
            "A jump pressed within the buffer window should fire on landing.");
    }

    [Test]
    public void JumpBuffer_Expired_DoesNotFire()
    {
        var state = MovementState.Default;
        float dt = 1f / 60f;

        // Press jump while airborne
        state = Simulate(state, Vector2.zero, jumpPressed: true, isGrounded: false, frames: 1, dt);

        // Wait longer than the buffer window (0.2s = 12 frames at 60fps, wait 20)
        state = Simulate(state, Vector2.zero, jumpPressed: false, isGrounded: false, frames: 20, dt);

        // Land - buffer should have expired
        state = Simulate(state, Vector2.zero, jumpPressed: false, isGrounded: true, frames: 2, dt);

        Assert.LessOrEqual(state.Velocity.y, 0f,
            "Jump buffer should expire after the buffer window.");
    }

    [Test]
    public void MarioJump_HoldingJump_MaintainsUpwardVelocity()
    {
        var state = MovementState.Default;
        float dt = 1f / 60f;

        // Jump and keep holding
        state = Simulate(state, Vector2.zero, jumpPressed: true, isGrounded: true, frames: 1, dt);
        float initialJumpVel = state.Velocity.y;

        // Continue holding jump while airborne for a few frames (within marioJumpTime)
        state = Simulate(state, Vector2.zero, jumpPressed: true, isGrounded: false, frames: 3, dt);

        // The velocity should still be close to the initial jump velocity because
        // the mario jump keeps re-applying it (minus some gravity)
        Assert.Greater(state.Velocity.y, initialJumpVel * 0.5f,
            "Holding jump should maintain upward velocity via mario jump.");
    }

    [Test]
    public void MarioJump_ReleasingEarly_ReducesHeight()
    {
        float dt = 1f / 60f;

        // Path A: Hold jump the whole time
        var stateHold = MovementState.Default;
        stateHold = Simulate(stateHold, Vector2.zero, jumpPressed: true, isGrounded: true, frames: 1, dt);
        stateHold = Simulate(stateHold, Vector2.zero, jumpPressed: true, isGrounded: false, frames: 20, dt);

        // Path B: Release jump immediately after takeoff
        var stateRelease = MovementState.Default;
        stateRelease = Simulate(stateRelease, Vector2.zero, jumpPressed: true, isGrounded: true, frames: 1, dt);
        stateRelease = Simulate(stateRelease, Vector2.zero, jumpPressed: false, isGrounded: false, frames: 20, dt);

        Assert.Greater(stateHold.Velocity.y, stateRelease.Velocity.y,
            "Releasing jump early should result in less upward velocity than holding it.");
    }

    // =====================================================================
    // Gravity
    // =====================================================================

    [Test]
    public void Airborne_Gravity_PullsDown()
    {
        var state = MovementState.Default;
        state.Velocity = new Vector3(0f, 5f, 0f);

        state = Simulate(state, Vector2.zero, false, isGrounded: false, frames: 1);

        Assert.Less(state.Velocity.y, 5f, "Gravity should reduce Y velocity when airborne.");
    }

    [Test]
    public void Grounded_NoJump_YVelocityIsZero()
    {
        var state = MovementState.Default;
        state.Velocity = new Vector3(5f, -2f, 0f); // some leftover downward velocity

        state = Simulate(state, Vector2.zero, false, isGrounded: true, frames: 1);

        Assert.AreEqual(0f, state.Velocity.y, 0.01f,
            "Grounded Y velocity should be 0 when not jumping.");
    }

    // =====================================================================
    // Modifiers
    // =====================================================================

    [Test]
    public void SpeedModifier_DoublesMaxSpeed()
    {
        var speedBoost = new MovementModifier
        {
            WalkSpeedMultiplier = 2f,
            WalkAccelerationMultiplier = 1f,
            JumpForceMultiplier = 1f,
            GravityMultiplier = 1f,
            PreImpulseVelocityMultiplier = 1f,
        };
        var mods = new[] { speedBoost }.ToList();

        var state = MovementState.Default;
        state = Simulate(state, Vector2.up, false, isGrounded: true, frames: 120, modifiers: mods);

        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(state.Velocity, Vector3.up);
        Assert.AreEqual(_config.MaxSpeedXZ * 2f, horizontalVelocity.magnitude, 0.2f,
            "A 2x speed modifier should double the effective max speed.");
    }

    [Test]
    public void SpeedModifier_SlowHalvesSpeed()
    {
        var slow = new MovementModifier
        {
            WalkSpeedMultiplier = 0.5f,
            WalkAccelerationMultiplier = 1f,
            JumpForceMultiplier = 1f,
            GravityMultiplier = 1f,
            PreImpulseVelocityMultiplier = 1f,
        };
        var mods = new[] { slow }.ToList();

        var state = MovementState.Default;
        state = Simulate(state, Vector2.up, false, isGrounded: true, frames: 120, modifiers: mods);

        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(state.Velocity, Vector3.up);
        Assert.AreEqual(_config.MaxSpeedXZ * 0.5f, horizontalVelocity.magnitude, 0.2f,
            "A 0.5x speed modifier should halve the effective max speed.");
    }

    [Test]
    public void JumpModifier_IncreasesJumpVelocity()
    {
        var jumpBoost = new MovementModifier
        {
            WalkSpeedMultiplier = 1f,
            WalkAccelerationMultiplier = 1f,
            JumpForceMultiplier = 1.5f,
            GravityMultiplier = 1f,
            PreImpulseVelocityMultiplier = 1f,
        };

        // Without modifier
        var stateNormal = MovementState.Default;
        stateNormal = Simulate(stateNormal, Vector2.zero, jumpPressed: true, isGrounded: true, frames: 1);

        // With modifier
        var stateBoosted = MovementState.Default;
        stateBoosted = Simulate(stateBoosted, Vector2.zero, jumpPressed: true, isGrounded: true, frames: 1,
            modifiers: new[] { jumpBoost }.ToList());

        Assert.Greater(stateBoosted.Velocity.y, stateNormal.Velocity.y,
            "Jump force modifier should increase initial jump velocity.");
    }

    [Test]
    public void GravityModifier_IncreasesDownwardPull()
    {
        var heavyGravity = new MovementModifier
        {
            WalkSpeedMultiplier = 1f,
            WalkAccelerationMultiplier = 1f,
            JumpForceMultiplier = 1f,
            GravityMultiplier = 2f,
            PreImpulseVelocityMultiplier = 1f,
        };

        // Without modifier
        var stateNormal = MovementState.Default;
        stateNormal.Velocity = new Vector3(0f, 10f, 0f);
        stateNormal = Simulate(stateNormal, Vector2.zero, false, isGrounded: false, frames: 10);

        // With modifier
        var stateHeavy = MovementState.Default;
        stateHeavy.Velocity = new Vector3(0f, 10f, 0f);
        stateHeavy = Simulate(stateHeavy, Vector2.zero, false, isGrounded: false, frames: 10,
            modifiers: new[] { heavyGravity }.ToList());

        Assert.Less(stateHeavy.Velocity.y, stateNormal.Velocity.y,
            "2x gravity modifier should pull down faster.");
    }

    [Test]
    public void MultipleModifiers_StackMultiplicatively()
    {
        var boost1 = new MovementModifier
        {
            WalkSpeedMultiplier = 2f,
            WalkAccelerationMultiplier = 1f,
            JumpForceMultiplier = 1f,
            GravityMultiplier = 1f,
            PreImpulseVelocityMultiplier = 1f,
        };
        var boost2 = new MovementModifier
        {
            WalkSpeedMultiplier = 1.5f,
            WalkAccelerationMultiplier = 1f,
            JumpForceMultiplier = 1f,
            GravityMultiplier = 1f,
            PreImpulseVelocityMultiplier = 1f,
        };
        var mods = new[] { boost1, boost2 }.ToList(); // 2.0 * 1.5 = 3.0x

        var state = MovementState.Default;
        state = Simulate(state, Vector2.up, false, isGrounded: true, frames: 120, modifiers: mods);

        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(state.Velocity, Vector3.up);
        Assert.AreEqual(_config.MaxSpeedXZ * 3f, horizontalVelocity.magnitude, 0.3f,
            "Multiple speed modifiers should stack multiplicatively (2x * 1.5x = 3x).");
    }

    [Test]
    public void IdentityModifier_HasNoEffect()
    {
        var identity = new[] { MovementModifier.Identity }.ToList();

        // With identity modifier
        var stateWithMod = MovementState.Default;
        stateWithMod = Simulate(stateWithMod, Vector2.up, false, isGrounded: true, frames: 30, modifiers: identity);

        // Without any modifier
        var stateNoMod = MovementState.Default;
        stateNoMod = Simulate(stateNoMod, Vector2.up, false, isGrounded: true, frames: 30);

        Assert.AreEqual(stateNoMod.Velocity, stateWithMod.Velocity,
            "An identity modifier should produce identical results to no modifier.");
    }

    // =====================================================================
    // Impulses
    // =====================================================================

    [Test]
    public void Impulse_SingleImpulse_AppliedAdditively()
    {
        var state = MovementState.Default;
        var impulses = new List<Vector3> { new(5f, 0f, 0f) };

        state = Simulate(state, Vector2.zero, false, isGrounded: true, frames: 1,
            impulsesOnLastFrame: impulses);

        Assert.AreEqual(5f, state.Velocity.x, 0.01f,
            "A single impulse should be added to velocity.");
    }

    [Test]
    public void Impulse_MultipleImpulses_SummedAndApplied()
    {
        var state = MovementState.Default;
        var impulses = new List<Vector3>
        {
            new(3f, 0f, 0f),
            new(0f, 2f, 0f),
        };

        state = Simulate(state, Vector2.zero, false, isGrounded: true, frames: 1,
            impulsesOnLastFrame: impulses);

        Assert.AreEqual(3f, state.Velocity.x, 0.01f,
            "Multiple impulses should be summed (X component).");
        Assert.AreEqual(2f, state.Velocity.y, 0.01f,
            "Multiple impulses should be summed (Y component).");
    }

    [Test]
    public void Impulse_EmptyList_NoEffect()
    {
        var state = MovementState.Default;
        var impulses = new List<Vector3>();

        state = Simulate(state, Vector2.zero, false, isGrounded: true, frames: 1,
            impulsesOnLastFrame: impulses);

        Assert.AreEqual(Vector3.zero, state.Velocity,
            "An empty impulse list should have no effect on velocity.");
    }

    [Test]
    public void Impulse_NullList_NoEffect()
    {
        var state = MovementState.Default;

        state = Simulate(state, Vector2.zero, false, isGrounded: true, frames: 1,
            impulsesOnLastFrame: null);

        Assert.AreEqual(Vector3.zero, state.Velocity,
            "A null impulse list should have no effect on velocity.");
    }

    [Test]
    public void Impulse_IsAdditiveToExistingVelocity()
    {
        var state = MovementState.Default;

        // Build up velocity
        state = Simulate(state, Vector2.up, false, isGrounded: true, frames: 30);
        float speedBefore = state.Velocity.magnitude;
        Assert.Greater(speedBefore, 0f);

        // Apply an impulse in a different direction
        var impulses = new List<Vector3> { new(0f, 5f, 0f) };
        state = Simulate(state, Vector2.zero, false, isGrounded: true, frames: 1,
            impulsesOnLastFrame: impulses);

        Assert.Greater(state.Velocity.magnitude, 5f,
            "Impulse should be additive to existing velocity.");
    }

    // =====================================================================
    // PreImpulseVelocityMultiplier
    // =====================================================================

    [Test]
    public void PreImpulseVelocityMultiplier_ScalesVelocityBeforeImpulses()
    {
        var halfVelocity = new MovementModifier
        {
            WalkSpeedMultiplier = 1f,
            WalkAccelerationMultiplier = 1f,
            JumpForceMultiplier = 1f,
            GravityMultiplier = 1f,
            PreImpulseVelocityMultiplier = 0.5f,
        };
        var mods = new[] { halfVelocity }.ToList();

        var impulses = new List<Vector3> { new(0f, 5f, 0f) };

        // With modifier: velocity is halved before impulse is added
        var stateWithMod = MovementState.Default;
        stateWithMod = Simulate(stateWithMod, Vector2.up, false, isGrounded: true, frames: 60, modifiers: mods);
        stateWithMod = Simulate(stateWithMod, Vector2.zero, false, isGrounded: true, frames: 1,
            modifiers: mods, impulsesOnLastFrame: impulses);

        // Without modifier: full velocity before impulse
        var stateNoMod = MovementState.Default;
        stateNoMod = Simulate(stateNoMod, Vector2.up, false, isGrounded: true, frames: 60);
        stateNoMod = Simulate(stateNoMod, Vector2.zero, false, isGrounded: true, frames: 1,
            impulsesOnLastFrame: impulses);

        Assert.Less(stateWithMod.Velocity.magnitude, stateNoMod.Velocity.magnitude,
            "PreImpulseVelocityMultiplier should scale velocity before impulses are added.");
    }

    [Test]
    public void PreImpulseVelocityMultiplier_DoesNotAffectImpulsesThemselves()
    {
        var halfVelocity = new MovementModifier
        {
            WalkSpeedMultiplier = 1f,
            WalkAccelerationMultiplier = 1f,
            JumpForceMultiplier = 1f,
            GravityMultiplier = 1f,
            PreImpulseVelocityMultiplier = 0.5f,
        };
        var mods = new[] { halfVelocity }.ToList();

        // From standstill, the pre-impulse velocity is zero, so the multiplier has no effect
        var impulses = new List<Vector3> { new(0f, 5f, 0f) };

        var stateWithMod = MovementState.Default;
        stateWithMod = Simulate(stateWithMod, Vector2.zero, false, isGrounded: true, frames: 1,
            modifiers: mods, impulsesOnLastFrame: impulses);

        var stateNoMod = MovementState.Default;
        stateNoMod = Simulate(stateNoMod, Vector2.zero, false, isGrounded: true, frames: 1,
            impulsesOnLastFrame: impulses);

        Assert.AreEqual(stateNoMod.Velocity.magnitude, stateWithMod.Velocity.magnitude, 0.01f,
            "PreImpulseVelocityMultiplier should not affect the impulse itself.");
    }

    // =====================================================================
    // Orientation
    // =====================================================================

    [Test]
    public void Orientation_RotatesInputToWorldSpace()
    {
        // Rotate 90 degrees around Y - forward input should now go along +X
        Quaternion rotated90 = Quaternion.Euler(0f, 90f, 0f);

        var state = MovementState.Default;
        state = Simulate(state, Vector2.up, false, isGrounded: true, frames: 30, orientation: rotated90);

        Assert.Greater(state.Velocity.x, 0.5f,
            "With a 90-degree yaw, forward input should produce +X world velocity.");
        Assert.AreEqual(0f, state.Velocity.z, 0.5f,
            "With a 90-degree yaw, forward input should produce near-zero Z velocity.");
    }

    // =====================================================================
    // Determinism (critical for netcode prediction / replay)
    // =====================================================================

    [Test]
    public void Determinism_SameInputs_ProduceSameOutput()
    {
        float dt = 1f / 60f;

        MovementState RunSequence()
        {
            var state = MovementState.Default;
            state = Simulate(state, Vector2.up, false, isGrounded: true, frames: 30, dt);
            state = Simulate(state, Vector2.right, true, isGrounded: true, frames: 1, dt);
            state = Simulate(state, Vector2.right, true, isGrounded: false, frames: 10, dt);
            state = Simulate(state, Vector2.right, false, isGrounded: false, frames: 20, dt);
            state = Simulate(state, Vector2.zero, false, isGrounded: true, frames: 10, dt);
            return state;
        }

        var result1 = RunSequence();
        var result2 = RunSequence();

        Assert.AreEqual(result1.Velocity, result2.Velocity,
            "Identical inputs must produce identical outputs for prediction to work.");
        Assert.AreEqual(result1.TimeSinceJumpPressed, result2.TimeSinceJumpPressed);
        Assert.AreEqual(result1.TimeSinceJumpBegan, result2.TimeSinceJumpBegan);
        Assert.AreEqual(result1.MarioJumpInterrupted, result2.MarioJumpInterrupted);
    }

    [Test]
    public void Determinism_StateSnapshot_CanResumeSimulation()
    {
        float dt = 1f / 60f;

        // Run a full sequence
        var state = MovementState.Default;
        state = Simulate(state, Vector2.up, false, isGrounded: true, frames: 30, dt);

        // Snapshot the state
        MovementState snapshot = state;

        // Continue from the original
        state = Simulate(state, Vector2.right, true, isGrounded: true, frames: 30, dt);

        // Resume from snapshot with the same inputs - must match exactly
        var resumed = Simulate(snapshot, Vector2.right, true, isGrounded: true, frames: 30, dt);

        Assert.AreEqual(state.Velocity, resumed.Velocity,
            "Resuming from a state snapshot with the same inputs must produce identical results.");
    }
}
