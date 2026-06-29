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

    public override void OnStartServer()
    {
        base.OnStartServer();

        // The server is authoritative for movement, including bots (which have no owning client).
        // Subscribing here is what makes a server-controlled pawn actually get knocked back, and it
        // keeps knockback in the authoritative simulation so it survives client reconciliation.
        pawn.OnDamageTaken += OnDamageTaken;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // A REMOTE owning client predicts its own knockback so it feels responsive under latency.
        // On a host the server instance above already subscribed (same object), so skip there to
        // avoid applying the impulse twice for the same damage event.
        if (IsOwner && !IsServerInitialized)
            pawn.OnDamageTaken += OnDamageTaken;
    }

    private void OnDestroy()
    {
        // Safe even if we never subscribed (removing an absent delegate is a no-op).
        if (pawn != null)
            pawn.OnDamageTaken -= OnDamageTaken;
    }

    private void OnDamageTaken(DamageInfo damageInfo)
    {
        Vector3 impulse = KnockbackHelper.ComputeImpulse(damageInfo.Direction, in config);
        movement.PendingImpulses.Add(impulse);
    }
}
