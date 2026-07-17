using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// The knockback-RECEIVING system of an entity: accepts an impulse a <see cref="KnockbackDealer"/> produced and
/// feeds it into the entity's movement. Sits on the entity ROOT alongside <see cref="HealthController"/>, so the collision
/// site resolves it with the same <c>GetComponentInParent</c> walk that resolves <see cref="IDamageable"/>.
///
/// It is a PURE RECEIVER. It does not watch for damage, and it does not decide how hard a hit shoves — that config
/// lives on the DEALER (the projectile), which is what makes knockback per-weapon tunable and lets a projectile
/// shove without dealing any damage. Its only authority is over the receiving end: it folds its own resistance
/// <see cref="Modifiers"/> into the incoming impulse, and it knows WHICH PEER may actually apply it.
///
/// That last part is the subtle bit. Pawn movement is CLIENT-AUTHORITATIVE (the root NetworkTransform is
/// clientAuthoritative, and <c>PawnMovement.Update</c> early-outs unless <c>PawnInput.IsActive</c>), so:
///  - a HUMAN pawn simulates movement only on its OWNING CLIENT — an impulse added on the server would sit in
///    <c>PendingImpulses</c> forever, neither consumed nor cleared. So the impulse is forwarded there.
///  - a BOT pawn has no owning connection and simulates on the server, so the impulse is applied in place.
/// Hit operations run server-only, so <see cref="ApplyImpulse"/> always begins on the server.
/// </summary>
public sealed class KnockbackReceiver : NetworkBehaviour
{
    [Tooltip("The movement system this receiver pushes impulses into.")]
    [SerializeField] private PawnMovement movement;

    /// <summary>
    /// Standing resistance modifiers folded into every incoming impulse (contributors own their own entries; an
    /// empty list is the identity). A multiplier of 0 makes this entity knockback-immune.
    /// </summary>
    public readonly List<KnockbackModifier> Modifiers = new();

    private void Awake()
    {
        if (movement == null)
            Debug.LogError($"{nameof(KnockbackReceiver)} requires a {nameof(PawnMovement)} reference.", this);
    }

    /// <summary>
    /// Accept an impulse already resolved by the dealer. Folds this receiver's resistance modifiers, then routes
    /// the result to whichever peer actually simulates this entity's movement.
    /// </summary>
    [Server]
    public void ApplyImpulse(Vector3 impulse)
    {
        Vector3 resolved = KnockbackHelper.ResolveIncoming(impulse, Modifiers);
        if (resolved.sqrMagnitude < 1e-8f)
            return;   // fully absorbed by resistance; nothing to apply

        // Owner.IsValid distinguishes a human pawn (owned by a client, which simulates its own movement) from a
        // bot pawn (no owning connection, simulated here). On a host the target RPC executes locally, so exactly
        // one of these two paths adds the impulse — never both.
        if (Owner.IsValid)
            TargetApplyImpulse(Owner, resolved);
        else
            AddImpulse(resolved);
    }

    /// <summary>Run the impulse on the owning client, the only peer whose <c>PawnMovement.Update</c> consumes it.</summary>
    [TargetRpc]
    private void TargetApplyImpulse(NetworkConnection _, Vector3 impulse) => AddImpulse(impulse);

    private void AddImpulse(Vector3 impulse)
    {
        if (movement != null)
            movement.PendingImpulses.Add(impulse);
    }
}
