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
        var tempInput = TemperatureInput.Default;

        // Cache the previous modifier before processing so we can swap it out.
        var previousModifier = state.MovementModifier;

        state = TemperatureProcessor.Process(state, in config, in tempInput, Time.deltaTime);

        // Notify the local HUD.
        if (IsOwner)
            LocalUIEvents.OnTemperatureChanged?.Invoke(state.Temperature);

        // Replace the previous temperature modifier with the one derived from the new state.
        movement.Modifiers.Remove(previousModifier);
        movement.Modifiers.Add(state.MovementModifier);
    }
}
