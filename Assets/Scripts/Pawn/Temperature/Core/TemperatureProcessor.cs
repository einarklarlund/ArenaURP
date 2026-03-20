using UnityEngine;

/// <summary>
/// Pure, stateless processor that advances the temperature simulation by one step.
/// Has zero Unity lifecycle dependencies - all state flows in and out via structs.
/// </summary>
public static class TemperatureProcessor
{
    /// <summary>
    /// Advance the temperature simulation by one tick.
    /// </summary>
    /// <param name="state">Current temperature state.</param>
    /// <param name="config">Temperature tuning parameters.</param>
    /// <param name="input">Per-frame inputs: whether primary fire is held.</param>
    /// <param name="deltaTime">Time step for this tick.</param>
    /// <returns>The new temperature state after this tick.</returns>
    public static TemperatureState Process(
        TemperatureState state,
        in TemperatureConfig config,
        in TemperatureInput input,
        float deltaTime)
    {
        if (input.IsDraining)
        {
            state.Temperature -= config.DrainRatePerSecond * deltaTime;
        }
        else
        {
            state.Temperature += config.RegenRatePerSecond * deltaTime;
        }

        state.Temperature = Mathf.Clamp(state.Temperature, 0f, config.MaxTemperature);
        state.MovementModifier = GetMovementModifier(in state, in config);
        return state;
    }

    /// <summary>
    /// Derives the MovementModifier that temperature should currently impose on the pawn.
    /// </summary>
    private static MovementModifier GetMovementModifier(
        in TemperatureState state,
        in TemperatureConfig config)
    {
        float t = Mathf.Clamp01(state.Temperature / config.MaxTemperature);
        float accelerationMultiplier = Mathf.Lerp(config.MinAccelerationMultiplier, 1f, t);
        float jumpForceMultiplier = Mathf.Lerp(config.MinJumpForceMultiplier, 1f, t);
        float walkSpeedMultiplier = Mathf.Lerp(config.MinSpeedMultiplier, 1f, t);

        return new MovementModifier
        {
            WalkSpeedMultiplier = walkSpeedMultiplier,
            WalkAccelerationMultiplier = accelerationMultiplier,
            JumpForceMultiplier = jumpForceMultiplier,
            GravityMultiplier = 1f,
            PreImpulseVelocityMultiplier = 1f,
        };
    }
}
