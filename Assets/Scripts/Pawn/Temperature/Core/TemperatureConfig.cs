using UnityEngine;

/// <summary>
/// Immutable configuration for pawn temperature simulation.
/// Typically populated from serialized fields on a MonoBehaviour.
/// </summary>
[System.Serializable]
public struct TemperatureConfig
{
    [Header("Range")]
    [Tooltip("Maximum temperature. Temperature is clamped to [0, MaxTemperature].")]
    public float MaxTemperature;

    [Header("Rates")]
    [Tooltip("Temperature lost per second while primary fire is held.")]
    public float DrainRatePerSecond;

    [Tooltip("Temperature gained per second while primary fire is not held.")]
    public float RegenRatePerSecond;

    [Header("Movement Effect")]
    [Tooltip(
        "Speed multiplier applied to the pawn when temperature reaches 0. " +
        "Interpolates linearly to 1.0 at max temperature.")]
    public float MinSpeedMultiplier;

    [Tooltip(
        "Acceleration multiplier applied to the pawn when temperature reaches 0. " +
        "Interpolates linearly to 1.0 at max temperature. " +
        "Independent of the speed multiplier.")]
    public float MinAccelerationMultiplier;

    [Tooltip(
        "Jump force multiplier applied to the pawn when temperature reaches 0. " +
        "Interpolates linearly to 1.0 at max temperature. " +
        "Independent of the speed multiplier.")]
    public float MinJumpForceMultiplier;

    public static TemperatureConfig Default => new()
    {
        MaxTemperature = 100f,
        DrainRatePerSecond = 20f,
        RegenRatePerSecond = 15f,
        MinSpeedMultiplier = 0.7f,
        MinAccelerationMultiplier = 0.3f,
        MinJumpForceMultiplier = 1f,
    };
}
