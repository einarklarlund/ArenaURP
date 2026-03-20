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
        activeModifier = MovementEffectsProcessor.Process(in movementState, in movementConfig, in config);
        pawnWeapon.Modifiers.Add(activeModifier);
    }
}
