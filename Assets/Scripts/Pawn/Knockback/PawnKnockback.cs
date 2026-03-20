using FishNet.Object;
using UnityEngine;

/// <summary>
/// Thin NetworkBehaviour that computes knockback impulses from damage events
/// and pushes them to PawnMovement's impulse API.
/// </summary>
public sealed class PawnKnockback : NetworkBehaviour
{
    // Configuration
    [SerializeField] private Pawn pawn;
    [SerializeField] private PawnMovement movement;
    [SerializeField] private PawnInput input;

    [Header("Knockback Config")]
    [SerializeField] private KnockbackConfig config = KnockbackConfig.Default;

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (!IsOwner) return;

        pawn.OnDamageTaken += OnDamageTaken;
    }

    private void OnDestroy()
    {
        if (!input.IsActive) return;

        pawn.OnDamageTaken -= OnDamageTaken;
    }

    private void OnDamageTaken(DamageInfo damageInfo)
    {
        Vector3 impulse = KnockbackHelper.ComputeImpulse(damageInfo.Direction, in config);
        movement.PendingImpulses.Add(impulse);
    }
}
