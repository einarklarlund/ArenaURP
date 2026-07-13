using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// The hitstun-RECEIVING system of an entity: accepts a stun duration a <see cref="HitstunDealer"/> produced and
/// publishes the resulting stun window. While stunned, <see cref="PawnWeapon"/> refuses to START a new fire event
/// (an already-committed burst plays out). Sits on the entity ROOT alongside <see cref="Health"/>, so the
/// collision site resolves it with the same <c>GetComponentInParent</c> walk that resolves <see cref="IDamageable"/>.
///
/// A pure receiver: how long a hit stuns is the DEALER's config. This end owns only its resistance
/// <see cref="Modifiers"/> and the replication of the window.
///
/// The window is a SyncVar EXPIRY TICK, not a countdown. Firing is simulated by different peers depending on the
/// pawn — the owning client for a human (<c>HumanPawnInput.IsActive => IsOwner</c>), the server for a bot — so a
/// countdown would have to be ticked in two places. Publishing one server-authoritative tick lets each peer
/// answer <see cref="IsStunned"/> against its own <c>TimeManager.Tick</c> and agree. Without this the owning
/// client would keep predicting shots the server rejects, and the player would watch bullets evaporate.
/// </summary>
public sealed class HitstunReceiver : NetworkBehaviour
{
    /// <summary>
    /// Standing resistance modifiers folded into every incoming stun (contributors own their own entries; an
    /// empty list is the identity). A multiplier of 0 makes this entity stun-immune.
    /// </summary>
    public readonly List<HitstunModifier> Modifiers = new();

    /// <summary>
    /// The tick this entity's current stun expires on; 0 when never stunned. Server-written, read by every peer
    /// (the owning client needs it to gate its own predicted firing).
    /// </summary>
    public readonly SyncVar<uint> StunnedUntilTick = new(0u, new SyncTypeSettings(WritePermission.ServerOnly));

    /// <summary>Whether firing is currently suppressed. Evaluated locally against this peer's own tick.</summary>
    public bool IsStunned =>
        TimeManager != null && HitstunProcessor.IsStunned(TimeManager.Tick, StunnedUntilTick.Value);

    public override void OnStartServer()
    {
        base.OnStartServer();
        StunnedUntilTick.Value = 0u;   // a respawned / pooled pawn never inherits a previous life's stun
    }

    /// <summary>
    /// Accept a stun duration already resolved by the dealer. Folds this receiver's resistance modifiers, converts
    /// the result to an expiry tick, and EXTENDS the current window (a weak late hit never cuts a strong one short).
    /// </summary>
    [Server]
    public void ApplyHitstun(float seconds)
    {
        float resolved = HitstunProcessor.ResolveIncoming(seconds, Modifiers);
        if (resolved <= 0f)
            return;   // fully absorbed by resistance

        uint end = HitstunProcessor.EndTick(TimeManager.Tick, resolved, (float)TimeManager.TickDelta);
        StunnedUntilTick.Value = HitstunProcessor.ExtendTo(StunnedUntilTick.Value, end);
    }
}
