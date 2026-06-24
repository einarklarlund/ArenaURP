using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Cross-processor integration test for the Temperature -> Weapon modifier handshake.
///
/// Asserts on the CONTRACT: that the WeaponModifier surfaced by TemperatureProcessor.Process
/// is consumed by WeaponProcessor.Process via WeaponInput.Modifiers and observably slows the
/// effective fire rate. If a field is renamed on one side, or WeaponProcessor stops folding
/// FireRateMultiplier into its cooldown, the slow-fire effect disappears silently.
///
/// Observable assertion: count the number of shots fired over a fixed simulated duration
/// with vs. without the temperature modifier. Slow-fire => fewer shots.
/// </summary>
[TestFixture]
public class TemperatureWeaponIntegrationTests
{
    private WeaponConfig _weaponConfig;
    private TemperatureConfig _temperatureConfig;

    [SetUp]
    public void SetUp()
    {
        _weaponConfig = WeaponConfig.Default; // FireRate = 0.1s, single-shot, automatic
        _temperatureConfig = TemperatureConfig.Default;
    }

    /// <summary>
    /// Run WeaponProcessor with a held-fire input for the given simulated duration and
    /// return the total number of shots fired across all ticks.
    /// </summary>
    private int CountShotsFired(List<WeaponModifier> modifiers, float totalSeconds = 2f, float dt = 1f / 60f)
    {
        var state = WeaponState.Default;
        var input = new WeaponInput
        {
            FireHeld = true,
            FirePressed = true,
            HasAmmo = true,
            Modifiers = modifiers ?? new List<WeaponModifier>(),
        };

        int totalShots = 0;
        int frames = UnityEngine.Mathf.RoundToInt(totalSeconds / dt);
        for (int i = 0; i < frames; i++)
        {
            state = WeaponProcessor.Process(state, in _weaponConfig, in input, dt, out int shotsThisFrame);
            totalShots += shotsThisFrame;
        }

        return totalShots;
    }

    [Test]
    public void TemperatureAtZero_ProducesSlowFireRateModifier_ThatWeaponProcessorConsumes()
    {
        // Arrange: drive temperature to zero and let the processor compute the WeaponModifier.
        var tempState = TemperatureState.Default;
        tempState.Temperature = 0f;
        var tempInput = new TemperatureInput { IsDraining = true };

        tempState = TemperatureProcessor.Process(tempState, in _temperatureConfig, in tempInput, 1f / 60f);

        // Sanity: the surfaced modifier should slow fire rate (multiplier > 1 = longer cooldown).
        Assert.Greater(tempState.WeaponModifier.FireRateMultiplier, 1f,
            "At zero temperature the surfaced FireRateMultiplier should be greater than 1 (slower fire).");

        // Act: feed surfaced modifier into WeaponInput.Modifiers exactly as PawnTemperature
        // and PawnWeapon do at runtime.
        int shotsWithSlowFire = CountShotsFired(
            new List<WeaponModifier> { tempState.WeaponModifier });

        int shotsBaseline = CountShotsFired(modifiers: null);

        // Assert: observable behaviour - over the same simulated duration the modified
        // weapon must fire measurably fewer shots. Sanity check the baseline is non-trivial
        // so a regression to "zero shots fired" doesn't accidentally pass the comparison.
        Assert.Greater(shotsBaseline, 5,
            "Baseline should fire enough shots over 2s for the comparison to be meaningful.");
        Assert.Less(shotsWithSlowFire, shotsBaseline,
            "WeaponProcessor should consume the slow-fire modifier surfaced by " +
            "TemperatureProcessor and fire fewer shots over the same duration.");
    }
}
