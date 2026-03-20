using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Edit-mode unit tests for MovementEffectsProcessor.
/// These run instantly in the Unity Test Runner - no Play Mode, no networking, no GameObjects.
/// </summary>
[TestFixture]
public class MovementEffectsProcessorTests
{
    private MovementConfig _movementConfig;
    private MovementEffectsConfig _effectsConfig;

    [SetUp]
    public void SetUp()
    {
        _movementConfig = MovementConfig.Default;
        _effectsConfig = MovementEffectsConfig.Default;
    }

    // =====================================================================
    // Helper
    // =====================================================================

    private WeaponModifier ProcessAtSpeed(float speed)
    {
        var state = MovementState.Default;
        state.Velocity = new Vector3(0f, 0f, speed);
        return MovementEffectsProcessor.Process(in state, in _movementConfig, in _effectsConfig);
    }

    // =====================================================================
    // Tests
    // =====================================================================

    [Test]
    public void ZeroSpeed_ReturnsConfiguredMultipliers()
    {
        var result = ProcessAtSpeed(0f);

        Assert.AreEqual(_effectsConfig.FireRateMultiplier, result.FireRateMultiplier, 0.001f,
            "At zero speed, fire rate multiplier should equal the configured value.");
        Assert.AreEqual(_effectsConfig.SpreadAngleMultiplier, result.SpreadMultiplier, 0.001f,
            "At zero speed, spread multiplier should equal the configured value.");
    }

    [Test]
    public void MaxSpeed_ReturnsIdentity()
    {
        var result = ProcessAtSpeed(_movementConfig.MaxSpeedXZ);

        Assert.AreEqual(1f, result.FireRateMultiplier, 0.001f,
            "At max speed, fire rate multiplier should be 1 (identity).");
        Assert.AreEqual(1f, result.SpreadMultiplier, 0.001f,
            "At max speed, spread multiplier should be 1 (identity).");
    }

    [Test]
    public void HalfSpeed_ReturnsMidpointValues()
    {
        float halfSpeed = _movementConfig.MaxSpeedXZ * 0.5f;
        var result = ProcessAtSpeed(halfSpeed);

        float expectedFireRate = Mathf.Lerp(_effectsConfig.FireRateMultiplier, 1f, 0.5f);
        float expectedSpread = Mathf.Lerp(_effectsConfig.SpreadAngleMultiplier, 1f, 0.5f);

        Assert.AreEqual(expectedFireRate, result.FireRateMultiplier, 0.001f,
            "At half max speed, fire rate multiplier should be the midpoint.");
        Assert.AreEqual(expectedSpread, result.SpreadMultiplier, 0.001f,
            "At half max speed, spread multiplier should be the midpoint.");
    }

    [Test]
    public void AboveMaxSpeed_ClampsToIdentity()
    {
        var result = ProcessAtSpeed(_movementConfig.MaxSpeedXZ * 2f);

        Assert.AreEqual(1f, result.FireRateMultiplier, 0.001f,
            "Above max speed, fire rate multiplier should clamp to 1 (identity).");
        Assert.AreEqual(1f, result.SpreadMultiplier, 0.001f,
            "Above max speed, spread multiplier should clamp to 1 (identity).");
    }
}
