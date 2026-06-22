using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// The central "Hub" or "Core" for the player's physical representation in the world.
/// Responsibilities:
/// - Acts as a gateway to access sibling components (Input, Inventory, Ammo, etc).
/// - Stores synced networked state such as Health and the Controlling Player reference.
/// - Invokes pawn-related game events on NetworkPlayer based on synced network state.
/// </summary>
public class Pawn : NetworkBehaviour, IDamageable
{
    [Header("Synced State")]
    public readonly SyncVar<NetworkPlayer> ControllingPlayer = new();
    public readonly SyncVar<int> Health = new(21, new SyncTypeSettings(WritePermission.ServerOnly));
    public readonly SyncVar<int> MaxHealth = new(21, new SyncTypeSettings(WritePermission.ServerOnly));

    public event Action<DamageInfo> OnDamageTaken;
    public event Action<DamageInfo> OnDeath;

    public void OnServerStart()
    {
        Health.Value = MaxHealth.Value;
    }

    public override void OnDespawnServer(NetworkConnection connection)
    {
        base.OnDespawnServer(connection);

        // Guard: ControllingPlayer may not be set if the pawn is despawned before initialization.
        if (ControllingPlayer.Value != null)
            ControllingPlayer.Value.ControlledPawn.Value = null;
    }

    public void Heal(int amount)
    {
        
    }

    public void TakeDamage(DamageInfo damageInfo)
    {        
        if (Health.Value <= 0) return;

        // We want clients to predictively use with TakeDamage before even waiting for
        // Health.Value to be updated, ie. predicting a valid hit and playing effects.
        OnDamageTaken?.Invoke(damageInfo);

        // This here will cause trouble if OnDeath is used for game UI since the
        // game UI will have to account for rollbacks.
        if (IsClientInitialized && Health.Value - damageInfo.Amount <= 0)
            OnDeath.Invoke(damageInfo);

        if (!IsServerInitialized)
            return;

        Health.Value -= damageInfo.Amount;

        if (Health.Value <= 0)
        {
            OnDeath?.Invoke(damageInfo);
        }
    }
}
