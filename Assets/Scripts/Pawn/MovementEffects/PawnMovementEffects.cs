using FishNet.Object;
using UnityEngine;

/// <summary>
/// Thin NetworkBehaviour that derives weapon modifiers from movement state
/// and pushes them to the pawn's weapon each frame.
/// </summary>
public sealed class PawnMovementEffects : NetworkBehaviour
{
    // Configuration
    [SerializeField] private PawnMovement movement;
    [SerializeField] private PawnWeapon pawnWeapon;

    [Header("Movement Effects Config")]
    [SerializeField] private MovementEffectsConfig config = MovementEffectsConfig.Default;

    // State
    private WeaponModifier activeModifier = WeaponModifier.Identity;

    private void Update()
    {
        var movementState = movement.State;
        var movementConfig = movement.Config;

        pawnWeapon.Modifiers.Remove(activeModifier);
        // Both multipliers lerp from their configured zero-speed value toward 1.0 (identity) at max speed.
        // Clamp01 prevents overshoot past identity above max speed.
        float t = Mathf.Clamp01(movementState.Velocity.magnitude / movementConfig.MaxSpeedXZ);
        activeModifier = new WeaponModifier
        {
            FireRateMultiplier = Mathf.Lerp(config.FireRateMultiplier, 1f, t),
            SpreadMultiplier = Mathf.Lerp(config.SpreadAngleMultiplier, 1f, t),
        };
        pawnWeapon.Modifiers.Add(activeModifier);
    }
}
