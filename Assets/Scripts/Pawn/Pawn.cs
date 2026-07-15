using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// The central "Hub" or "Core" for the player's physical representation in the world.
/// Responsibilities:
/// - Acts as a gateway to access sibling components (Input, Inventory, Ammo, etc).
/// - Stores synced networked state such as the Controlling Player reference.
/// - Invokes pawn-related game events on NetworkPlayer based on synced network state.
///
/// HEALTH LIVES ON THE SIBLING <see cref="global::Health"/> COMPONENT — the one health system shared with
/// projectiles and props — NOT on the Pawn. The Pawn is a thin FACADE over it: it forwards
/// <see cref="Health"/> / <see cref="MaxHealth"/> to that component's SyncVars and re-raises its
/// <see cref="OnDamageTaken"/> / <see cref="OnDeath"/> events, so the pawn's existing consumers (knockback, VFX,
/// colour, HUD, PawnManager, bots, test rigs) keep reading <c>pawn.Health</c> / <c>pawn.OnDeath</c> unchanged.
/// The sibling <see cref="global::Health"/> is the sole <see cref="IDamageable"/>; hit operations resolve it
/// directly. <see cref="TakeDamage"/> here is a convenience forwarder for the HurtBox wiring and the rigs.
/// </summary>
[RequireComponent(typeof(Health))]
public class Pawn : NetworkBehaviour
{
    [Header("Synced State")]
    public readonly SyncVar<NetworkPlayer> ControllingPlayer = new();

    private Health _health;

    /// <summary>The pawn's current HP — forwarded from the sibling <see cref="global::Health"/> component's
    /// <c>CurrentHealth</c> SyncVar (subscribe to its <c>OnChange</c> / read its <c>Value</c> as before).</summary>
    public SyncVar<int> Health => _health.CurrentHealth;

    /// <summary>The pawn's max HP — forwarded from the sibling <see cref="global::Health"/> component.</summary>
    public SyncVar<int> MaxHealth => _health.MaxHealth;

    /// <summary>Re-raised from the sibling health's predictive all-peer damage event.</summary>
    public event Action<DamageInfo> OnDamageTaken;

    /// <summary>Re-raised from the sibling health's death event (authoritative + client-predictive).</summary>
    public event Action<DamageInfo> OnDeath;

    private void Awake()
    {
        _health = GetComponent<Health>();
        if (_health == null)
        {
            // [RequireComponent] should keep the sibling attached; if it is stripped anyway, fail loudly ONCE and
            // disable rather than silently forwarding a null facade into per-frame NREs across every reader.
            Debug.LogError($"{nameof(Pawn)} requires a sibling Health component.", this);
            enabled = false;
            return;
        }
        _health.OnDamageTaken += HandleDamageTaken;
        _health.OnDeath += HandleDeath;
    }

    private void OnDestroy()
    {
        if (_health != null)
        {
            _health.OnDamageTaken -= HandleDamageTaken;
            _health.OnDeath -= HandleDeath;
        }
    }

    private void HandleDamageTaken(DamageInfo info) => OnDamageTaken?.Invoke(info);
    private void HandleDeath(DamageInfo info) => OnDeath?.Invoke(info);

    public override void OnDespawnServer(NetworkConnection connection)
    {
        base.OnDespawnServer(connection);

        // Guard: ControllingPlayer may not be set if the pawn is despawned before initialization.
        if (ControllingPlayer.Value != null)
            ControllingPlayer.Value.ControlledPawn.Value = null;
    }

    /// <summary>Convenience forwarder so the HurtBox UnityEvent and test rigs can keep calling
    /// <c>pawn.TakeDamage</c>; the sibling <see cref="global::Health"/> is the actual damage receiver.</summary>
    public void TakeDamage(DamageInfo damageInfo) => _health.TakeDamage(damageInfo);

    public void Heal(int amount) => _health.ServerHeal(amount);
}
