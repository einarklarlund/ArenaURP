using FishNet.Object;
using UnityEngine;

/// <summary>
/// Thin NetworkBehaviour that wires TemperatureProcessor into Unity.
/// Runs on the controlling machine only (same guard as PawnMovement).
/// Fires LocalUIEvents.OnTemperatureChanged each frame so the local HUD
/// can display the current value without going through the network stack.
/// </summary>
public sealed class PawnTemperature : NetworkBehaviour
{
    // ------------------------------------------------------------------
    // Inspector references
    // ------------------------------------------------------------------
    [SerializeField] private PawnMovement movement;
    [SerializeField] private PawnWeapon weapon;
    [SerializeField] private PawnInput input;

    [Header("Temperature Config")]
    [SerializeField] private TemperatureConfig config = TemperatureConfig.Default;

    // ------------------------------------------------------------------
    // Runtime state
    // ------------------------------------------------------------------
    private TemperatureState state = TemperatureState.Default;

    // ------------------------------------------------------------------
    // Frame update
    // ------------------------------------------------------------------

    private void Update()
    {
        if (!input.IsActive) return;

        var tempInput = new TemperatureInput()
        {
            IsDraining = input.Data.Fire
        };

        // Cache the previous modifiers before processing so we can swap them out.
        var previousMovementModifier = state.MovementModifier;
        var previousWeaponModifier = state.WeaponModifier;

        state = TemperatureProcessor.Process(state, in config, in tempInput, Time.deltaTime);

        // Notify the local HUD.
        if (IsOwner)
            LocalUIEvents.OnTemperatureChanged?.Invoke(state.Temperature);

        // Replace the previous temperature modifiers with the ones derived from the new state.
        movement.Modifiers.Remove(previousMovementModifier);
        movement.Modifiers.Add(state.MovementModifier);

        weapon.Modifiers.Remove(previousWeaponModifier);
        weapon.Modifiers.Add(state.WeaponModifier);
    }
}
