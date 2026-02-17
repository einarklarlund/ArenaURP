using System;
using FishNet;
using FishNet.Connection;
using FishNet.Managing.Timing;
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
    [Header("References")] // Components on the pawn that other game components
    public PawnInput Input;
    public PawnInventory Inventory;
    public PawnAmmo Ammo;
    
    [Header("Synced State")]
    public readonly SyncVar<NetworkPlayer> ControllingPlayer = new();
    public readonly SyncVar<int> Health = new(40, new SyncTypeSettings(WritePermission.ServerOnly));
    public readonly SyncVar<int> MaxHealth = new(40, new SyncTypeSettings(WritePermission.ServerOnly));

    public event Action<DamageInfo> OnDamageTaken;
    public event Action<DamageInfo> OnDeath;

    public void OnServerStart()
    {
        Health.Value = MaxHealth.Value;
    }

    public override void OnDespawnServer(NetworkConnection connection)
    {
        base.OnDespawnServer(connection);
        ControllingPlayer.Value.ControlledPawn.Value = null;
    }

    [Server]
    public void Heal(int amount)
    {
        
    }
    
    [Server]
    public void ServerTakeDamage(DamageInfo damageInfo)
    {
        if (Health.Value <= 0) return;

        Health.Value -= damageInfo.Amount;
        
        OnDamageTaken?.Invoke(damageInfo);

        ObserversTakeDamage(damageInfo);

        if (Health.Value <= 0)
        {
            OnDeath?.Invoke(damageInfo);
            Despawn();
        }
    }

    [ObserversRpc]
    public void ObserversTakeDamage(DamageInfo damageInfo)
    {
        if (IsServerInitialized) return;

        OnDamageTaken?.Invoke(damageInfo);
    }
}