using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Thin NetworkBehaviour that wires the pure MovementProcessor into Unity and FishNet.
/// Owns the CharacterController, reads input, feeds data to the processor, and applies
/// the resulting velocity.
/// </summary>
public sealed class PawnMovement : NetworkBehaviour
{
    // Configuration
    [SerializeField] private CharacterController controller;
    [SerializeField] private PawnInput input;

    [Header("Movement Config")]
    [SerializeField] private MovementConfig config = MovementConfig.Default;

    // API
    public readonly List<MovementModifier> Modifiers = new();
    public readonly List<Vector3> PendingImpulses = new();
    public MovementState State => state;
    public MovementConfig Config => config;

    // State
    private MovementState state = MovementState.Default;

    private void Update()
    {
        if (!input.IsActive) return;

        // Calculate input
        var movementInput = new MovementInput
        {
            Move = input.Data.Move,
            JumpPressed = input.Data.Jump,
            IsGrounded = controller.isGrounded,
            WorldOrientation = transform.rotation,
            Modifiers = Modifiers,
            Impulses = PendingImpulses,
        };

        // Process state + input
        state = MovementProcessor.Process
        (
            state,
            in config,
            in movementInput,
            Time.deltaTime
        );

        // Apply state to other components
        controller.Move(state.Velocity * Time.deltaTime);

        PendingImpulses.Clear();
    }
}
