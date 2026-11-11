using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Checks the HL2 movement port against numbers worked out by hand from Source's formulas. Runs in Source units
/// (MetersPerUnit = 1) on a flat, endless floor, so it needs no physics: `dotnet test Tools/LevelGenTests` runs it.
/// </summary>
public class SourceMovementTests
{
    private const float Dt = 0.015f;

    private static MovementConfig Config()
    {
        MovementConfig config = MovementConfig.Default;
        config.MetersPerUnit = 1f;
        // Free jumps, so the hopping tests can jump as often as they like
        config.JumpEnergyCost = 0f;
        return config;
    }

    /// <summary>An endless floor at y = 0, in Source units.</summary>
    private sealed class FlatFloor : IMovementBody
    {
        public Vector3 Feet;
        public float Height = 72f;

        public Vector3 Move(Vector3 displacement, List<Vector3> hitNormals)
        {
            Vector3 before = Feet;
            Feet += displacement;
            if (Feet.y < 0f)
            {
                Feet.y = 0f;
                hitNormals.Add(Vector3.up);
            }
            return Feet - before;
        }

        public bool ProbeGround(float distance, float minNormalY, out Vector3 normal)
        {
            normal = Vector3.up;
            return Feet.y <= distance;
        }

        public void StayOnGround(float maxDrop, float minNormalY)
        {
            if (Feet.y <= maxDrop)
                Feet.y = 0f;
        }

        public bool HasRoomForHeight(float height, bool keepTop) => !keepTop || Feet.y >= height - Height;

        public bool TrySetHeight(float height, bool keepTop)
        {
            if (!HasRoomForHeight(height, keepTop))
                return false;
            if (keepTop)
                Feet.y -= height - Height;
            Height = height;
            return true;
        }
    }

    private static MovementState Grounded(Vector3 velocity)
    {
        MovementState state = MovementState.Initial(Config());
        state.IsGrounded = true;
        state.Velocity = velocity;
        return state;
    }

    private static void Tick(ref MovementState state, FlatFloor body, MovementInputData input, MovementConfig? config = null)
    {
        MovementProcessor.Tick(ref state, body, input, config ?? Config(), Quaternion.identity, 1f, Vector3.zero, Dt);
    }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    [Test]
    public void Friction_AboveStopSpeed_ScalesWithSpeed()
    {
        // drop = 190 * 4 * 0.015 = 11.4
        Vector3 v = SourceMovement.ApplyFriction(new Vector3(190f, 0f, 0f), 4f, 100f, 0.1f, Dt);
        Assert.AreEqual(178.6f, v.x, 1e-3f);
    }

    [Test]
    public void Friction_BelowStopSpeed_UsesStopSpeed()
    {
        // drop = 100 * 4 * 0.015 = 6
        Vector3 v = SourceMovement.ApplyFriction(new Vector3(50f, 0f, 0f), 4f, 100f, 0.1f, Dt);
        Assert.AreEqual(44f, v.x, 1e-3f);
    }

    [Test]
    public void Accelerate_AddsAccelTimesWishSpeedPerTick()
    {
        // 10 * 0.015 * 190 = 28.5
        Vector3 v = SourceMovement.Accelerate(Vector3.zero, Vector3.forward, 190f, 10f, Dt);
        Assert.AreEqual(28.5f, v.z, 1e-3f);

        Vector3 nearTop = SourceMovement.Accelerate(new Vector3(0f, 0f, 180f), Vector3.forward, 190f, 10f, Dt);
        Assert.AreEqual(190f, nearTop.z, 1e-3f);
    }

    [Test]
    public void AirAccelerate_SidewaysGainsSpeed_ForwardDoesNot()
    {
        Vector3 fast = new(0f, 0f, 300f);

        // Nothing along +x yet, so up to the cap of 30, in steps of 10 * 190 * 0.015 = 28.5
        Vector3 strafed = SourceMovement.AirAccelerate(fast, Vector3.right, 190f, 10f, 30f, Dt);
        Assert.AreEqual(28.5f, strafed.x, 1e-3f);
        Assert.Greater(strafed.magnitude, fast.magnitude);

        Vector3 forward = SourceMovement.AirAccelerate(fast, Vector3.forward, 190f, 10f, 30f, Dt);
        Assert.AreEqual(fast, forward);
    }

    [Test]
    public void InputMove_DiagonalIsNoFaster()
    {
        Vector2 move = SourceMovement.InputMove(new Vector2(0.7071f, 0.7071f), 450f, 190f);
        Assert.AreEqual(190f, move.magnitude, 1e-2f);
    }

    [Test]
    public void ClipToPlanes_SlidesAlongWall_StopsInCorner()
    {
        Vector3 v = new(100f, 0f, 100f);
        Vector3 slid = SourceMovement.ClipToPlanes(v, new List<Vector3> { Vector3.left });
        Assert.AreEqual(0f, slid.x, 1e-4f);
        Assert.AreEqual(100f, slid.z, 1e-4f);

        // Two walls facing each other's way: the crease runs vertically, so nothing flat survives
        Vector3 cornered = SourceMovement.ClipToPlanes(v, new List<Vector3> { Vector3.left, Vector3.back });
        Assert.AreEqual(Vector3.zero, cornered);
    }

    [Test]
    public void Run_ReachesNormalSpeedAndHoldsIt()
    {
        FlatFloor body = new();
        MovementState state = Grounded(Vector3.zero);
        MovementInputData forward = new() { Move = new Vector2(0f, 1f) };

        for (int i = 0; i < 100; i++)
            Tick(ref state, body, forward);

        // Friction takes 190 * 4 * dt each tick and acceleration gives exactly that back
        Assert.AreEqual(190f, Flat(state.Velocity), 1e-2f);
        Assert.IsTrue(state.IsGrounded);
    }

    [Test]
    public void Stop_From190_Takes27Ticks()
    {
        FlatFloor body = new();
        MovementState state = Grounded(new Vector3(0f, 0f, 190f));

        int ticks = 0;
        while (state.Velocity != Vector3.zero && ticks < 1000)
        {
            Tick(ref state, body, default);
            ticks++;
        }

        Assert.AreEqual(27, ticks);
    }

    [Test]
    public void Jump_PeaksAbout20Units()
    {
        FlatFloor body = new();
        MovementState state = Grounded(Vector3.zero);

        Tick(ref state, body, new MovementInputData { Jump = true });
        float peak = body.Feet.y;
        for (int i = 0; i < 200; i++)
        {
            Tick(ref state, body, default);
            peak = Mathf.Max(peak, body.Feet.y);
        }

        // Source's half-step gravity, with an extra half step on the jump tick
        Assert.AreEqual(20.145f, peak, 0.01f);
        Assert.IsTrue(state.IsGrounded);
    }

    [Test]
    public void Jump_OnLanding_LosesNoSpeedToFriction()
    {
        FlatFloor body = new();
        MovementState state = Grounded(new Vector3(0f, 0f, 300f));

        Tick(ref state, body, new MovementInputData { Jump = true });

        Assert.IsFalse(state.IsGrounded);
        Assert.AreEqual(300f, Flat(state.Velocity), 1e-3f);
    }

    [Test]
    public void Jump_HeldThroughLanding_DoesNotJumpAgain()
    {
        FlatFloor body = new();
        MovementState state = Grounded(Vector3.zero);
        MovementInputData held = new() { Jump = true };

        for (int i = 0; i < 200; i++)
            Tick(ref state, body, held);

        // Ground within 2 units counts as landed, as in Source, and standing still doesn't snap down to it
        Assert.IsTrue(state.IsGrounded);
        Assert.Less(body.Feet.y, 2f);
    }

    /// <summary>Jumps from standing, presses jump <paramref name="ticksEarly"/> ticks before the tick it lands on and
    /// holds it. Returns whether it jumped again.</summary>
    private static bool JumpsAgainAfterEarlyPress(float bufferTime, int ticksEarly)
    {
        MovementConfig config = Config();
        config.JumpBufferTime = bufferTime;
        FlatFloor body = new();
        MovementState state = Grounded(Vector3.zero);

        // Find the landing tick first
        FlatFloor probeBody = new();
        MovementState probe = state;
        Tick(ref probe, probeBody, new MovementInputData { Jump = true }, config);
        int landing = 1;
        while (!probe.IsGrounded)
        {
            Tick(ref probe, probeBody, default, config);
            landing++;
        }

        Tick(ref state, body, new MovementInputData { Jump = true }, config);
        for (int i = 1; i < landing + 3; i++)
        {
            bool held = i >= landing - ticksEarly;
            Tick(ref state, body, new MovementInputData { Jump = held }, config);
            if (i >= landing && !state.IsGrounded)
                return true;
        }

        return false;
    }

    [Test]
    public void JumpBuffer_PressJustBeforeLanding_JumpsOnLanding()
    {
        // The press is (ticksEarly + 1) ticks before the first tick on the ground
        Assert.IsTrue(JumpsAgainAfterEarlyPress(0.1f, 3));
    }

    [Test]
    public void JumpBuffer_PressTooEarly_DoesNotJump()
    {
        Assert.IsFalse(JumpsAgainAfterEarlyPress(0.1f, 8));
    }

    [Test]
    public void JumpBuffer_Zero_IsHL2_HeldPressDoesNotJump()
    {
        Assert.IsFalse(JumpsAgainAfterEarlyPress(0f, 1));
    }

    [Test]
    public void BunnyHop_BufferedJumps_KeepEverySpeed()
    {
        FlatFloor body = new();
        MovementState state = Grounded(new Vector3(0f, 0f, 300f));

        int jumps = 0;
        bool wasGrounded = true;
        for (int i = 0; i < 400; i++)
        {
            // Mash jump, as on a scroll wheel: pressed every other tick
            Tick(ref state, body, new MovementInputData { Jump = i % 2 == 0 });
            if (wasGrounded && !state.IsGrounded)
                jumps++;
            wasGrounded = state.IsGrounded;
        }

        Assert.Greater(jumps, 5);
        // Every jump came on the first tick on the ground, so friction never ran
        Assert.AreEqual(300f, Flat(state.Velocity), 1e-2f);
    }

    [Test]
    public void BunnyHop_PressesFasterThanTicks_StillBuffer()
    {
        FlatFloor body = new();
        MovementState state = Grounded(new Vector3(0f, 0f, 300f));
        // Pressed and released within every tick, so jump never looks released between ticks
        MovementInputData mash = new() { Jump = true, JumpPressed = true };

        for (int i = 0; i < 400; i++)
            Tick(ref state, body, mash);

        Assert.AreEqual(300f, Flat(state.Velocity), 1e-2f);
    }

    [Test]
    public void AirStrafe_WithoutTurning_SidewaysSpeedStopsAtTheCap()
    {
        FlatFloor body = new() { Feet = new Vector3(0f, 1e5f, 0f) };
        MovementState state = MovementState.Initial(Config());
        state.Velocity = new Vector3(0f, 0f, 300f);
        MovementInputData strafeRight = new() { Move = new Vector2(1f, 0f) };

        for (int i = 0; i < 20; i++)
            Tick(ref state, body, strafeRight);

        Assert.AreEqual(30f, state.Velocity.x, 1e-3f);
        Assert.AreEqual(300f, state.Velocity.z, 1e-3f);
    }

    [Test]
    public void AirStrafe_TurningWithTheStrafe_GainsSpeed()
    {
        FlatFloor body = new() { Feet = new Vector3(0f, 1e5f, 0f) };
        MovementState state = MovementState.Initial(Config());
        state.Velocity = new Vector3(0f, 0f, 300f);
        MovementInputData strafeRight = new() { Move = new Vector2(1f, 0f) };

        // Turning right at 4 degrees a tick, about 267 degrees a second, for 0.9 s
        float yaw = 0f;
        for (int i = 0; i < 60; i++)
        {
            yaw += 4f;
            MovementProcessor.Tick(ref state, body, strafeRight, Config(), Quaternion.Euler(0f, yaw, 0f), 1f, Vector3.zero, Dt);
        }

        Assert.Greater(Flat(state.Velocity), 350f, $"{Flat(state.Velocity)}");
        // The velocity turned with the view, through 240 degrees
        Vector3 facing = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 heading = new Vector3(state.Velocity.x, 0f, state.Velocity.z).normalized;
        Assert.Greater(Vector3.Dot(heading, facing), 0.7f);
    }

    [Test]
    public void JumpForwardBoost_BackwardsWithNoKeys_Accelerates()
    {
        MovementConfig config = Config();
        config.JumpForwardBoost = true;
        FlatFloor body = new();
        // Facing +z, moving backwards at 400
        MovementState state = Grounded(new Vector3(0f, 0f, -400f));

        Tick(ref state, body, new MovementInputData { Jump = true }, config);

        // Capped at 190 * 1.5 = 285, so the boost is 285 - 400 = -115 along the view: 115 faster backwards
        Assert.AreEqual(-515f, state.Velocity.z, 1e-2f);
    }

    [Test]
    public void Crouch_OnGround_ShrinksAfterTimeToDuck_AndCrawlsAtAThird()
    {
        FlatFloor body = new();
        MovementState state = Grounded(Vector3.zero);
        MovementInputData crouchForward = new() { Crouch = true, Move = new Vector2(0f, 1f) };

        for (int i = 0; i < 26; i++)
            Tick(ref state, body, crouchForward);
        Assert.IsFalse(state.IsDucked, "0.39 s in");

        for (int i = 0; i < 200; i++)
            Tick(ref state, body, crouchForward);

        Assert.IsTrue(state.IsDucked);
        Assert.AreEqual(36f, body.Height);
        Assert.AreEqual(28f, state.EyeHeight, 1e-3f);
        Assert.AreEqual(190f / 3f, Flat(state.Velocity), 1e-2f);
    }

    [Test]
    public void Crouch_InAir_TucksLegsUp()
    {
        FlatFloor body = new() { Feet = new Vector3(0f, 50f, 0f) };
        MovementState state = MovementState.Initial(Config());

        Tick(ref state, body, new MovementInputData { Crouch = true });

        Assert.IsTrue(state.IsDucked);
        // The top of the hull stays put, so the feet rise by 72 - 36, less one tick of falling
        Assert.AreEqual(86f, body.Feet.y, 0.2f);
    }

    [Test]
    public void Sprint_RunsAt320_AndDrains25PerSecond()
    {
        FlatFloor body = new();
        MovementState state = Grounded(Vector3.zero);
        MovementInputData sprint = new() { Sprint = true, Move = new Vector2(0f, 1f) };

        // One second; the first tick starts from standing, so it drains nothing
        for (int i = 0; i < 67; i++)
            Tick(ref state, body, sprint);

        Assert.IsTrue(state.IsSprinting);
        Assert.AreEqual(320f, Flat(state.Velocity), 1e-2f);
        Assert.AreEqual(100f - 25f * 66 * Dt, state.SuitPower, 1e-2f);
    }
}
