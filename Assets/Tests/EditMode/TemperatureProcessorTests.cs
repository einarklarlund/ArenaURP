using NUnit.Framework;

/// <summary>
/// Edit-mode tests for TemperatureProcessor.
/// All tests operate on plain structs - no GameObjects or Unity lifecycle required.
/// </summary>
[TestFixture]
public class TemperatureProcessorTests
{
    // ------------------------------------------------------------------
    // Shared helpers
    // ------------------------------------------------------------------

    private static TemperatureConfig MakeConfig(
        float max = 100f,
        float drain = 20f,
        float regen = 15f,
        float minSpeed = 0.3f,
        float minAccel = 0.3f) => new()
    {
        MaxTemperature = max,
        DrainRatePerSecond = drain,
        RegenRatePerSecond = regen,
        MinSpeedMultiplier = minSpeed,
        MinAccelerationMultiplier = minAccel,
    };

    private static TemperatureState AtTemp(float temperature) => new() { Temperature = temperature };

    private static readonly TemperatureInput Firing    = new() { IsDraining = true };
    private static readonly TemperatureInput NotFiring = new() { IsDraining = false };

    /// <summary>
    /// Processes a state at the given temperature with zero deltaTime,
    /// so the temperature is preserved but the MovementModifier is populated.
    /// </summary>
    private static TemperatureState ProcessAtTemp(float temperature, TemperatureConfig config)
    {
        return TemperatureProcessor.Process(AtTemp(temperature), in config, in NotFiring, deltaTime: 0f);
    }

    private const float Tolerance = 0.0001f;

    // ------------------------------------------------------------------
    // Process - drain behaviour
    // ------------------------------------------------------------------

    [Test]
    public void Process_DrainsTempWhenFiring()
    {
        var config = MakeConfig(drain: 20f);
        var result = TemperatureProcessor.Process(AtTemp(100f), in config, in Firing, deltaTime: 1f);
        Assert.AreEqual(80f, result.Temperature, Tolerance);
    }

    [Test]
    public void Process_DrainIsProportionalToDeltaTime()
    {
        var config = MakeConfig(drain: 20f);
        var result = TemperatureProcessor.Process(AtTemp(100f), in config, in Firing, deltaTime: 0.5f);
        Assert.AreEqual(90f, result.Temperature, Tolerance);
    }

    [Test]
    public void Process_ClampsToZeroWhenDrainExceedsTemperature()
    {
        var config = MakeConfig(drain: 20f);
        // 5 * 20 = 100 drain, starting at 10 -> would be -90, clamped to 0
        var result = TemperatureProcessor.Process(AtTemp(10f), in config, in Firing, deltaTime: 5f);
        Assert.AreEqual(0f, result.Temperature, Tolerance);
    }

    [Test]
    public void Process_TemperatureDoesNotGoBelowZero()
    {
        var config = MakeConfig(drain: 20f);
        var state = AtTemp(0f);
        var result = TemperatureProcessor.Process(state, in config, in Firing, deltaTime: 1f);
        Assert.AreEqual(0f, result.Temperature, Tolerance);
    }

    // ------------------------------------------------------------------
    // Process - regen behaviour
    // ------------------------------------------------------------------

    [Test]
    public void Process_RegensTempWhenNotFiring()
    {
        var config = MakeConfig(regen: 15f);
        var result = TemperatureProcessor.Process(AtTemp(0f), in config, in NotFiring, deltaTime: 1f);
        Assert.AreEqual(15f, result.Temperature, Tolerance);
    }

    [Test]
    public void Process_RegenIsProportionalToDeltaTime()
    {
        var config = MakeConfig(regen: 15f);
        var result = TemperatureProcessor.Process(AtTemp(0f), in config, in NotFiring, deltaTime: 0.5f);
        Assert.AreEqual(7.5f, result.Temperature, Tolerance);
    }

    [Test]
    public void Process_ClampsToMaxWhenRegenExceedsMaxTemperature()
    {
        var config = MakeConfig(max: 100f, regen: 15f);
        // 10 * 15 = 150 regen from 0 -> would be 150, clamped to 100
        var result = TemperatureProcessor.Process(AtTemp(0f), in config, in NotFiring, deltaTime: 10f);
        Assert.AreEqual(100f, result.Temperature, Tolerance);
    }

    [Test]
    public void Process_TemperatureDoesNotExceedMax()
    {
        var config = MakeConfig(max: 100f, regen: 15f);
        var state = AtTemp(100f);
        var result = TemperatureProcessor.Process(state, in config, in NotFiring, deltaTime: 1f);
        Assert.AreEqual(100f, result.Temperature, Tolerance);
    }

    // ------------------------------------------------------------------
    // Process - integration
    // ------------------------------------------------------------------

    [Test]
    public void Process_FullDrainThenFullRegen()
    {
        var config = MakeConfig(max: 100f, drain: 100f, regen: 100f);
        var state = AtTemp(100f);

        // Drain completely in 1 second
        state = TemperatureProcessor.Process(state, in config, in Firing, deltaTime: 1f);
        Assert.AreEqual(0f, state.Temperature, Tolerance, "Should reach 0 after full drain.");

        // Regen completely in 1 second
        state = TemperatureProcessor.Process(state, in config, in NotFiring, deltaTime: 1f);
        Assert.AreEqual(100f, state.Temperature, Tolerance, "Should return to 100 after full regen.");
    }

    [Test]
    public void Process_AccumulatesCorrectlyOverManySmallSteps()
    {
        var config = MakeConfig(drain: 20f);
        var state = AtTemp(100f);
        const int steps = 60;
        const float dt = 1f / steps; // simulate 1 second at 60fps

        for (int i = 0; i < steps; i++)
            state = TemperatureProcessor.Process(state, in config, in Firing, dt);

        // After 1 second at 20/s drain: 100 - 20 = 80
        Assert.AreEqual(80f, state.Temperature, 0.01f);
    }

    // ------------------------------------------------------------------
    // Process - MovementModifier (speed multiplier mapping)
    // ------------------------------------------------------------------

    [Test]
    public void Process_MovementModifier_ReturnsFullSpeedAtMaxTemperature()
    {
        var config = MakeConfig(max: 100f, minSpeed: 0.3f);
        var result = ProcessAtTemp(100f, config);
        Assert.AreEqual(1f, result.MovementModifier.WalkSpeedMultiplier, Tolerance);
    }

    [Test]
    public void Process_MovementModifier_ReturnsMinSpeedAtZeroTemperature()
    {
        var config = MakeConfig(max: 100f, minSpeed: 0.3f);
        var result = ProcessAtTemp(0f, config);
        Assert.AreEqual(0.3f, result.MovementModifier.WalkSpeedMultiplier, Tolerance);
    }

    [Test]
    public void Process_MovementModifier_InterpolatesLinearlyAtMidpoint()
    {
        var config = MakeConfig(max: 100f, minSpeed: 0.3f);
        // At 50% temperature: Lerp(0.3, 1.0, 0.5) = 0.65
        var result = ProcessAtTemp(50f, config);
        Assert.AreEqual(0.65f, result.MovementModifier.WalkSpeedMultiplier, Tolerance);
    }

    [Test]
    public void Process_MovementModifier_NonWalkMultipliersAreAlwaysOne()
    {
        var config = MakeConfig();
        var resultAtMax  = ProcessAtTemp(100f, config);
        var resultAtZero = ProcessAtTemp(0f,   config);

        Assert.AreEqual(1f, resultAtMax.MovementModifier.GravityMultiplier,                Tolerance);
        Assert.AreEqual(1f, resultAtMax.MovementModifier.PreImpulseVelocityMultiplier,   Tolerance);
        Assert.AreEqual(1f, resultAtZero.MovementModifier.GravityMultiplier,               Tolerance);
        Assert.AreEqual(1f, resultAtZero.MovementModifier.PreImpulseVelocityMultiplier,  Tolerance);
    }

    // ------------------------------------------------------------------
    // Process - MovementModifier (acceleration multiplier mapping)
    // ------------------------------------------------------------------

    [Test]
    public void Process_MovementModifier_ReturnsFullAccelerationAtMaxTemperature()
    {
        var config = MakeConfig(max: 100f, minAccel: 0.3f);
        var result = ProcessAtTemp(100f, config);
        Assert.AreEqual(1f, result.MovementModifier.WalkAccelerationMultiplier, Tolerance);
    }

    [Test]
    public void Process_MovementModifier_ReturnsMinAccelerationAtZeroTemperature()
    {
        var config = MakeConfig(max: 100f, minAccel: 0.3f);
        var result = ProcessAtTemp(0f, config);
        Assert.AreEqual(0.3f, result.MovementModifier.WalkAccelerationMultiplier, Tolerance);
    }

    [Test]
    public void Process_MovementModifier_AccelerationInterpolatesLinearlyAtMidpoint()
    {
        var config = MakeConfig(max: 100f, minAccel: 0.3f);
        // At 50% temperature: Lerp(0.3, 1.0, 0.5) = 0.65
        var result = ProcessAtTemp(50f, config);
        Assert.AreEqual(0.65f, result.MovementModifier.WalkAccelerationMultiplier, Tolerance);
    }

    [Test]
    public void Process_MovementModifier_SpeedAndAccelerationAreIndependent()
    {
        var config = MakeConfig(max: 100f, minSpeed: 1.0f, minAccel: 0.2f);
        // With minSpeed = 1.0, speed should be 1.0 at all temps; accel should still vary
        var result = ProcessAtTemp(0f, config);
        Assert.AreEqual(1.0f, result.MovementModifier.WalkSpeedMultiplier,        Tolerance);
        Assert.AreEqual(0.2f, result.MovementModifier.WalkAccelerationMultiplier, Tolerance);
    }

    [Test]
    public void Process_MovementModifier_ClampsTemperatureAboveMax()
    {
        var config = MakeConfig(max: 100f, minSpeed: 0.3f);
        // Temperature above max should still yield WalkSpeedMultiplier = 1
        var result = ProcessAtTemp(200f, config);
        Assert.AreEqual(1f, result.MovementModifier.WalkSpeedMultiplier, Tolerance);
    }

    [Test]
    public void Process_MovementModifier_ClampsTemperatureBelowZero()
    {
        var config = MakeConfig(max: 100f, minSpeed: 0.3f);
        // Negative temperature should yield MinWalkSpeedMultiplier
        var result = ProcessAtTemp(-50f, config);
        Assert.AreEqual(0.3f, result.MovementModifier.WalkSpeedMultiplier, Tolerance);
    }
}
