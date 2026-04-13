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

    [Header("Ground Detection")]
    [SerializeField] private float groundRayDistance = 1.5f;
    [SerializeField] private LayerMask groundMask = ~0;

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

        // Snap velocity to follow slope ahead
        Vector3 velocity = state.Velocity;
        if (controller.isGrounded && velocity.y <= 0f)
        {
            velocity = GetSlopeSnappedVelocity(velocity);
        }

        // Debug.DrawLine(transform.position, transform.position + state.Velocity, Color.red, 10f);
        // Debug.DrawLine(transform.position, transform.position + velocity, Color.green, 10f);
        controller.Move(velocity * Time.deltaTime);

        // controller.Move(state.Velocity * Time.deltaTime);

        PendingImpulses.Clear();
    }

    private Vector3 GetSlopeSnappedVelocity(Vector3 velocity)
    {
        // Cast down from a point ahead in the velocity direction
        Vector3 aheadPoint = transform.position + velocity.normalized * 0.3f;
        
        // Debug.DrawRay(
        //     transform.position,
        //     velocity.normalized * 0.3f,
        //     Color.blue,
        //     10f
        // );

        float castOriginHeight = groundRayDistance / 2;

        if (
            !Physics.Raycast(
                aheadPoint + Vector3.up * castOriginHeight,
                Vector3.down,
                out RaycastHit hit,
                groundRayDistance,
                groundMask
            )
        )
        {
            // Debug.DrawRay(
            //     aheadPoint + Vector3.up * castOriginHeight,
            //     Vector3.down * (castOriginHeight + groundRayDistance),
            //     Color.green,
            //     10f
            // );
            return velocity;
        }
        else
        {
            
            // Debug.DrawRay(
            //     aheadPoint + Vector3.up * castOriginHeight,
            //     Vector3.down * (castOriginHeight + groundRayDistance),
            //     Color.red,
            //     10f
            // );
        }

        // Direction from current position to the ground point ahead
        Vector3 toHit = hit.point - transform.position;
        float slopeAngle = Vector3.Angle(velocity.normalized, toHit.normalized);

        // Only snap if the slope is within the controller's slope limit
        if (slopeAngle > controller.slopeLimit)
        {
            // Debug.DrawRay(
            //     transform.position,
            //     velocity,
            //     Color.black,
            //     10f
            // );
            // return velocity;
        }
        // Debug.DrawRay(
        //     transform.position,
        //     toHit.normalized * velocity.magnitude,
        //     Color.white,
        //     10f
        // );

        return toHit.normalized * velocity.magnitude;
    }
}
