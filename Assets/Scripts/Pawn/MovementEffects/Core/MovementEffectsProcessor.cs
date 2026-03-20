using UnityEngine;

/// <summary>
/// Pure, stateless processor that derives weapon modifiers from movement state.
/// Has zero Unity lifecycle dependencies - all data flows in and out via structs.
/// </summary>
public static class MovementEffectsProcessor
{
    /// <summary>
    /// Derives the WeaponModifier that movement should currently impose on the pawn's weapon.
    /// Both multipliers interpolate linearly from their configured values at zero speed
    /// toward 1.0 (identity) at max speed.
    /// </summary>
    public static WeaponModifier Process(
        in MovementState state,
        in MovementConfig movementConfig,
        in MovementEffectsConfig config)
    {
        float t = Mathf.Clamp01(state.Velocity.magnitude / movementConfig.MaxSpeedXZ);
        float fireRateMultiplier = Mathf.Lerp(config.FireRateMultiplier, 1f, t);
        float spreadMultiplier = Mathf.Lerp(config.SpreadAngleMultiplier, 1f, t);

        return new WeaponModifier
        {
            FireRateMultiplier = fireRateMultiplier,
            SpreadMultiplier = spreadMultiplier,
        };
    }
}
