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
    // ------------------------------------------------------------------
    // Unity / Inspector references
    // ------------------------------------------------------------------
    [SerializeField] private CharacterController controller;
    [SerializeField] private Pawn pawn;
    [SerializeField] private PawnInput input;

    [Header("Movement Config")]
    [SerializeField] private MovementConfig config = MovementConfig.Default;

    // ------------------------------------------------------------------
    // Runtime state
    // ------------------------------------------------------------------
    private MovementState _state = MovementState.Default;
    private readonly List<MovementModifier> _modifiers = new();
    private MovementModifier[] _modifiersCache = System.Array.Empty<MovementModifier>();
    private bool _modifiersDirty = true;

    // ------------------------------------------------------------------
    // Public API for modifier system
    // ------------------------------------------------------------------

    /// <summary>Add a movement modifier (e.g. speed boost, slow field).</summary>
    public void AddModifier(MovementModifier modifier)
    {
        _modifiers.Add(modifier);
        _modifiersDirty = true;
    }

    /// <summary>Remove the first matching modifier.</summary>
    public bool RemoveModifier(MovementModifier modifier)
    {
        bool removed = _modifiers.Remove(modifier);
        if (removed) _modifiersDirty = true;
        return removed;
    }

    /// <summary>Remove all active modifiers.</summary>
    public void ClearModifiers()
    {
        _modifiers.Clear();
        _modifiersDirty = true;
    }

    /// <summary>Current movement state. Useful for debug inspectors or network sync.</summary>
    public MovementState State => _state;

    /// <summary>Current movement config. Can be read for UI or debug purposes.</summary>
    public MovementConfig Config => config;

    // ------------------------------------------------------------------
    // FishNet lifecycle
    // ------------------------------------------------------------------

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (!input.IsActive) return;

        pawn.OnDamageTaken += OnDamageTaken;
    }

    private void OnDestroy()
    {
        if (!input.IsActive) return;

        pawn.OnDamageTaken -= OnDamageTaken;
    }

    // ------------------------------------------------------------------
    // Event handlers
    // ------------------------------------------------------------------

    private void OnDamageTaken(DamageInfo damageInfo)
    {
        if (!input.IsActive) return;

        // Queue knockback on the state - the processor will consume it next frame.
        _state.HasPendingHit = true;
        _state.PendingHit = new HitKnockbackInfo { Direction = damageInfo.Direction };
    }

    // ------------------------------------------------------------------
    // Frame update - the only place we touch Unity APIs
    // ------------------------------------------------------------------

    private void Update()
    {
        if (!input.IsActive) return;

        // Rebuild modifier cache only when the list has changed (avoids per-frame allocation)
        if (_modifiersDirty)
        {
            _modifiersCache = _modifiers.ToArray();
            _modifiersDirty = false;
        }

        var movementInput = new MovementInput
        {
            Move = input.Data.Move,
            JumpPressed = input.Data.Jump,
            IsGrounded = controller.isGrounded,
            WorldOrientation = transform.rotation,
        };

        // Run the pure processor - the only Unity side-effect is the CharacterController.Move below
        _state = MovementProcessor.Process(
            _state,
            in config,
            _modifiersCache,
            in movementInput,
            Time.deltaTime);

        controller.Move(_state.Velocity * Time.deltaTime);
    }
}
