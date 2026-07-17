using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// The hitstun controller of an entity (ADR 0005): the ONE home of the hitstun datum — config, folds, window
/// arithmetic, and the fire gate — owning both halves of the exchange. A controller OWNS STATE (the authored
/// <see cref="config"/> baseline, the two runtime modifier registries, and the replicated
/// <see cref="StunnedUntilTick"/> window) and exposes an API to modify it (<see cref="ApplyHitstun"/>) — the
/// state-owning half of the dispatch model, replacing the HitstunDealer/HitstunReceiver pair (and the
/// HitstunProcessor core: the arithmetic lives here now).
///
/// DEALER half (a layer-2 hit responder): on a Player-channel <see cref="HitMessage"/> it resolves the outgoing
/// duration (authored baseline × <see cref="OutgoingModifiers"/>) and delivers it with a direct call into the
/// victim's own controller. Carrying this component on a projectile IS what makes its hits stagger (presence,
/// ADR 0002).
///
/// RECEIVER half (a layer-3 operation): <see cref="ApplyHitstun"/> folds <see cref="IncomingModifiers"/>
/// (resistance; 0 = immune), converts the result to an expiry tick — rounding UP, so any positive stun costs at
/// least one full tick and can never round away — and EXTENDS the published window (a weak late hit never cuts a
/// strong one short).
///
/// The window is a SyncVar EXPIRY TICK, not a countdown, because firing is simulated by different peers depending
/// on the pawn (the owning client for a human, the server for a bot). This controller WATCHES the window on every
/// peer — on stun start it sets the sibling <see cref="PawnWeapon"/>'s <c>CanFire</c> false and subscribes to the
/// tick; at expiry it sets it back true and unsubscribes — so the weapon consumes plain data it owns and never
/// reaches into hitstun state. <see cref="weapon"/> is null on dealer-only entities (bullets), where the watch
/// simply has nothing to flip. The authored config is a BASELINE and is never mutated; registries clear on
/// <see cref="ResetState"/>, the baseline never does.
/// </summary>
public sealed class HitstunController : NetworkBehaviour, IHitMessageHandler
{
    [SerializeField] private HitstunConfig config = HitstunConfig.Default;

    [Tooltip("The weapon whose CanFire gate the stun window drives. Null on dealer-only entities (bullets).")]
    [SerializeField] private PawnWeapon weapon;

    /// <summary>Outgoing modifiers folded into every stun this entity deals. Empty list = the identity.</summary>
    public readonly List<HitstunModifier> OutgoingModifiers = new();

    /// <summary>Standing resistance modifiers folded into every incoming stun (contributors own their own
    /// entries; an empty list is the identity). A multiplier of 0 makes this entity stun-immune.</summary>
    public readonly List<HitstunModifier> IncomingModifiers = new();

    /// <summary>
    /// The tick this entity's current stun expires on; 0 when never stunned. Server-written, read by every peer
    /// (the owning client needs it to gate its own predicted firing).
    /// </summary>
    public readonly SyncVar<uint> StunnedUntilTick = new(0u, new SyncTypeSettings(WritePermission.ServerOnly));

    /// <summary>Whether the stun window is currently open. Evaluated locally against this peer's own tick.</summary>
    public bool IsStunned => TimeManager != null && TimeManager.Tick < StunnedUntilTick.Value;

    // Dealer half responds on the Player channel only — nothing varies per kind, so no serialized knob.
    public HitChannels EnabledHitChannels => HitChannels.Player;

    // Whether this peer's tick-watch is live (subscribed to TimeManager.OnTick awaiting the window's expiry).
    private bool _watching;

    public override void OnStartServer()
    {
        base.OnStartServer();
        // Conditional write: a respawned / pooled pawn never inherits a previous life's stun, while the dormant
        // half on a pooled bullet (never stunned) costs no SyncVar dirtying at all.
        if (StunnedUntilTick.Value != 0u)
            StunnedUntilTick.Value = 0u;
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        StunnedUntilTick.OnChange += OnWindowChanged;
        UpdateWatch();   // a late joiner (or pooled reuse) may start inside an already-open window
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        StunnedUntilTick.OnChange -= OnWindowChanged;
        StopWatch();
        SetCanFire(true);   // never leave a recycled weapon gated by a dead watch
    }

    /// <summary>Dealer half: resolve the outgoing stun (authored seconds × outgoing fold, floored at zero) and
    /// deliver it into the victim's controller (a layer-2 direct call; the victim resolves root-ward from the
    /// struck collider).</summary>
    public void OnHit(HitMessage hit)
    {
        var receiver = hit.Victim != null ? hit.Victim.GetComponentInParent<HitstunController>() : null;
        if (receiver != null)
            receiver.ApplyHitstun(Mathf.Max(0f, config.Seconds * FoldMultiplier(OutgoingModifiers)));
    }

    /// <summary>
    /// Receiver half: accept a stun duration already resolved by the dealer. Folds this entity's resistance
    /// modifiers, converts the result to an expiry tick (rounding UP — a positive stun never rounds away), and
    /// EXTENDS the current window (never shortens it).
    /// </summary>
    [Server]
    public void ApplyHitstun(float seconds)
    {
        float resolved = Mathf.Max(0f, seconds * FoldMultiplier(IncomingModifiers));
        float tickDelta = (float)TimeManager.TickDelta;
        if (resolved <= 0f || tickDelta <= 0f)
            return;   // fully absorbed by resistance

        uint end = TimeManager.Tick + (uint)Mathf.Max(1, Mathf.CeilToInt(resolved / tickDelta));
        if (end > StunnedUntilTick.Value)
            StunnedUntilTick.Value = end;
    }

    // ---- the per-peer window watch (drives PawnWeapon.CanFire as plain data) ----

    private void OnWindowChanged(uint previous, uint next, bool asServer) => UpdateWatch();

    /// <summary>Reconcile CanFire and the tick subscription with the current window. Idempotent — on a host the
    /// SyncVar OnChange fires in both contexts and this converges to the same state.</summary>
    private void UpdateWatch()
    {
        bool stunned = IsStunned;
        SetCanFire(!stunned);
        if (stunned)
            StartWatch();
        else
            StopWatch();
    }

    private void WatchTick()
    {
        if (IsStunned)
            return;
        SetCanFire(true);
        StopWatch();
    }

    private void StartWatch()
    {
        if (_watching || TimeManager == null)
            return;
        _watching = true;
        TimeManager.OnTick += WatchTick;
    }

    private void StopWatch()
    {
        if (!_watching)
            return;
        _watching = false;
        if (TimeManager != null)
            TimeManager.OnTick -= WatchTick;
    }

    private void SetCanFire(bool value)
    {
        if (weapon != null)
            weapon.SetCanFire(value);
    }

    /// <summary>Fold a modifier list into one multiplier. An empty list is the identity, so an absent
    /// contributor changes nothing.</summary>
    private static float FoldMultiplier(List<HitstunModifier> modifiers)
    {
        float multiplier = 1f;
        for (int i = 0; i < modifiers.Count; i++)
            multiplier *= modifiers[i].DurationMultiplier;
        return multiplier;
    }

    public override void ResetState(bool asServer)
    {
        base.ResetState(asServer);
        OutgoingModifiers.Clear();
        IncomingModifiers.Clear();
        StopWatch();
        SetCanFire(true);
        enabled = true;   // disabled-is-silent is the strip hook; pooled reuse starts audible
    }
}
