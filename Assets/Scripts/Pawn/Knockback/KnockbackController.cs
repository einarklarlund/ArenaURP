using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// The knockback controller of an entity (ADR 0005): the ONE home of the knockback datum — config, folds, and
/// delivery — owning both halves of the exchange. A controller OWNS STATE (the authored <see cref="config"/>
/// baseline plus the two runtime modifier registries) and exposes an API to modify it
/// (<see cref="ApplyKnockback"/>) — the state-owning half of the dispatch model, replacing the
/// KnockbackDealer/KnockbackReceiver pair (and the KnockbackHelper core: the arithmetic lives here now).
///
/// DEALER half (a layer-2 hit responder): on a Player-channel <see cref="HitMessage"/> it resolves the outgoing
/// impulse — the hit direction clamped to the authored minimum launch angle, scaled by
/// <see cref="config"/>.SpeedOnHit × <see cref="OutgoingModifiers"/> — and delivers it with a direct call into
/// the victim's own controller (the same GetComponentInParent walk to the entity root every receiver uses).
/// Carrying this component on a projectile IS what makes its hits shove (presence, ADR 0002).
///
/// RECEIVER half (a layer-3 operation): <see cref="ApplyKnockback"/> folds <see cref="IncomingModifiers"/>
/// (resistance; 0 = immune) and routes the result to whichever peer actually simulates this entity's movement.
/// Pawn movement is CLIENT-authoritative for humans (the owning client consumes <c>PendingImpulses</c>) and
/// server-simulated for bots, so: a valid Owner gets the impulse via TargetRpc (the sanctioned delivery leg);
/// otherwise it is applied in place. Both ends terminate in <c>movement.PendingImpulses.Add</c> — a data
/// contribution, never a behaviour call. <see cref="movement"/> is null on dealer-only entities (bullets), where
/// delivery simply early-outs.
///
/// The authored config is a BASELINE and is never mutated; registries clear on <see cref="ResetState"/> (pooled
/// reuse), the baseline never does.
/// </summary>
public sealed class KnockbackController : NetworkBehaviour, IHitMessageHandler
{
    [SerializeField] private KnockbackConfig config = KnockbackConfig.Default;

    [Tooltip("The movement system the receiver half pushes impulses into. Null on dealer-only entities (bullets).")]
    [SerializeField] private PawnMovement movement;

    /// <summary>Outgoing modifiers folded into every impulse this entity deals (the future damage-buff seam).
    /// Empty list = the identity.</summary>
    public readonly List<KnockbackModifier> OutgoingModifiers = new();

    /// <summary>Standing resistance modifiers folded into every incoming impulse (contributors own their own
    /// entries; an empty list is the identity). A multiplier of 0 makes this entity knockback-immune.</summary>
    public readonly List<KnockbackModifier> IncomingModifiers = new();

    // Dealer half responds on the Player channel only — nothing varies per kind, so no serialized knob.
    public HitChannels EnabledHitChannels => HitChannels.Player;

    /// <summary>Dealer half: resolve the outgoing impulse and deliver it into the victim's controller (a layer-2
    /// direct call; the victim resolves root-ward from the struck collider).</summary>
    public void OnHit(HitMessage hit)
    {
        var receiver = hit.Victim != null ? hit.Victim.GetComponentInParent<KnockbackController>() : null;
        if (receiver != null)
            receiver.ApplyKnockback(ResolveOutgoing(hit.Direction));
    }

    /// <summary>
    /// The outgoing impulse this entity deals along <paramref name="hitDirection"/>: the direction lifted to at
    /// least the authored minimum launch angle from horizontal (so a flat shot still pops the victim up), scaled
    /// by the authored speed and the outgoing modifier fold.
    /// </summary>
    private Vector3 ResolveOutgoing(Vector3 hitDirection)
    {
        Vector3 dir = hitDirection.normalized;
        float horizontalMag = Vector3.ProjectOnPlane(dir, Vector3.up).magnitude;
        float minHeight = horizontalMag * Mathf.Tan(config.MinimumAngleOnHit * Mathf.Deg2Rad);
        dir.y = Mathf.Max(dir.y, minHeight);
        return config.SpeedOnHit * dir.normalized * FoldMultiplier(OutgoingModifiers);
    }

    /// <summary>
    /// Receiver half: accept an impulse already resolved by the dealer. Folds this entity's resistance modifiers,
    /// then routes the result to whichever peer actually simulates this entity's movement. Hit operations run
    /// server-only, so this always begins on the server.
    /// </summary>
    [Server]
    public void ApplyKnockback(Vector3 impulse)
    {
        Vector3 resolved = impulse * FoldMultiplier(IncomingModifiers);
        if (resolved.sqrMagnitude < 1e-8f)
            return;   // fully absorbed by resistance; nothing to apply

        // Owner.IsValid distinguishes a human pawn (owned by a client, which simulates its own movement) from a
        // bot pawn (no owning connection, simulated here). On a host the target RPC executes locally, so exactly
        // one of these two paths adds the impulse — never both.
        if (Owner.IsValid)
            TargetApplyKnockback(Owner, resolved);
        else
            AddImpulse(resolved);
    }

    /// <summary>Run the impulse on the owning client, the only peer whose <c>PawnMovement.Update</c> consumes it.</summary>
    [TargetRpc]
    private void TargetApplyKnockback(NetworkConnection _, Vector3 impulse) => AddImpulse(impulse);

    private void AddImpulse(Vector3 impulse)
    {
        if (movement != null)
            movement.PendingImpulses.Add(impulse);
    }

    /// <summary>Fold a modifier list into one multiplier. An empty list is the identity, so an absent
    /// contributor changes nothing.</summary>
    private static float FoldMultiplier(List<KnockbackModifier> modifiers)
    {
        float multiplier = 1f;
        for (int i = 0; i < modifiers.Count; i++)
            multiplier *= modifiers[i].ImpulseMultiplier;
        return multiplier;
    }

    public override void ResetState(bool asServer)
    {
        base.ResetState(asServer);
        OutgoingModifiers.Clear();
        IncomingModifiers.Clear();
        enabled = true;   // disabled-is-silent is the strip hook; pooled reuse starts audible
    }
}
