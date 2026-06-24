using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Cross-processor integration tests for the Temperature -> Movement modifier handshake.
///
/// Asserts on the CONTRACT: that the MovementModifier surfaced by TemperatureProcessor.Process
/// is the same shape MovementProcessor.Process consumes via MovementInput.Modifiers.
/// If someone renames a field on one side but not the other, or stops reading a field on the
/// consumer side, the slow effect silently disappears in-game. These tests catch that.
///
/// Each test asserts observable behaviour (final horizontal velocity over N ticks) and uses
/// a baseline comparison rather than pinning to specific multiplier values.
/// </summary>
[TestFixture]
public class TemperatureMovementIntegrationTests
{
    private MovementConfig _movementConfig;
    private TemperatureConfig _temperatureConfig;

    [SetUp]
    public void SetUp()
    {
        _movementConfig = MovementConfig.Default;
        _temperatureConfig = TemperatureConfig.Default;
    }

    /// <summary>
    /// Runs MovementProcessor for N frames of constant forward input on the ground
    /// with the given modifier list, returning the final horizontal velocity magnitude.
    /// </summary>
    private float SimulateForwardHorizontalSpeed(List<MovementModifier> modifiers, int frames = 120)
    {
        var state = MovementState.Default;
        var input = new MovementInput
        {
            Move = Vector2.up,
            JumpPressed = false,
            IsGrounded = true,
            WorldOrientation = Quaternion.identity,
            Modifiers = modifiers ?? new List<MovementModifier>(),
            Impulses = null,
        };

        const float dt = 1f / 60f;
        for (int i = 0; i < frames; i++)
        {
            state = MovementProcessor.Process(state, in _movementConfig, in input, dt);
        }

        return Vector3.ProjectOnPlane(state.Velocity, Vector3.up).magnitude;
    }

    [Test]
    public void TemperatureAtZero_ProducesSlowModifier_ThatMovementProcessorConsumes()
    {
        // Arrange: build the TemperatureState a real pawn would have when its
        // temperature has bottomed out. Run one tick of the temperature processor
        // (with IsDraining = false so regen doesn't yank us back to neutral in one
        // tick) so the surfaced MovementModifier is computed by the same code path
        // that runs in-game.
        var tempState = TemperatureState.Default;
        tempState.Temperature = 0f;
        var tempInput = new TemperatureInput { IsDraining = true };

        tempState = TemperatureProcessor.Process(tempState, in _temperatureConfig, in tempInput, 1f / 60f);

        // Sanity: temperature stayed clamped at 0 and a non-identity modifier was surfaced.
        Assert.AreEqual(0f, tempState.Temperature, 0.001f,
            "Temperature should remain clamped at 0 after draining.");
        Assert.Less(tempState.MovementModifier.WalkSpeedMultiplier, 1f,
            "At zero temperature the surfaced WalkSpeedMultiplier should be below 1.");

        // Act: feed the surfaced modifier straight into MovementInput.Modifiers,
        // exactly as PawnTemperature/PawnMovement do at runtime.
        float speedWithSlow = SimulateForwardHorizontalSpeed(
            new List<MovementModifier> { tempState.MovementModifier });

        float speedBaseline = SimulateForwardHorizontalSpeed(modifiers: null);

        // Assert: observable behaviour - moving with the slow modifier should travel
        // measurably less per second than with no modifier. Loose tolerance so tuning
        // constants can drift without breaking the contract.
        Assert.Less(speedWithSlow, speedBaseline * 0.95f,
            "MovementProcessor should consume the slow MovementModifier surfaced by " +
            "TemperatureProcessor and produce noticeably reduced horizontal speed.");
    }

    [Test]
    public void TemperatureAtMax_ProducesIdentityModifier_MovementMatchesBaseline()
    {
        // Arrange: a fully-charged pawn. The contract here is the inverse of the slow
        // case: when temperature is at max, the surfaced modifier must NOT silently
        // reduce movement. This guards against a regression that applies a phantom
        // multiplier whenever the temperature system is wired in.
        var tempState = TemperatureState.Default; // Temperature = 100 (max)
        var tempInput = TemperatureInput.Default;  // not draining

        tempState = TemperatureProcessor.Process(tempState, in _temperatureConfig, in tempInput, 1f / 60f);

        Assert.AreEqual(_temperatureConfig.MaxTemperature, tempState.Temperature, 0.001f,
            "Temperature should remain clamped at max when not draining.");

        // Act
        float speedWithMaxTempModifier = SimulateForwardHorizontalSpeed(
            new List<MovementModifier> { tempState.MovementModifier });

        float speedBaseline = SimulateForwardHorizontalSpeed(modifiers: null);

        // Assert: at max temperature the surfaced modifier must be effectively
        // identity-equivalent for movement output. Loose tolerance (1%) absorbs any
        // tiny float drift while still catching a real phantom slow.
        Assert.AreEqual(speedBaseline, speedWithMaxTempModifier, speedBaseline * 0.01f,
            "At max temperature the surfaced MovementModifier should not change movement output.");
    }
}
