using System.Collections.Generic;
using FishNet;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;

/// <summary>
/// The reactive bridge between Network SyncVars and the local UI.
/// Responsibilities:
/// - Monitors manager and player SyncVars for changes.
/// - Translates complex network data into simple, decoupled signals via NetworkUIEvents.
/// </summary>
public sealed class NetworkUIController : MonoBehaviour
{
    private bool isSubscribed = false;

    private void Update()
    {
        // Wait until managers and the local player are ready
        if (!isSubscribed && NetworkPlayer.LocalInstance != null && MatchFlowManager.Instance != null && DeathmatchManager.Instance != null)
        {
            InitializeSubscriptions();
        }
    }

    private void InitializeSubscriptions()
    {
        isSubscribed = true;

        InstanceFinder.ClientManager.OnClientConnectionState += OnClientConnectionStateChanged;

        MatchFlowManager.Instance.CountdownTime.OnChange += OnCountdownChanged;
        MatchFlowManager.Instance.State.OnChange += OnMatchStateChanged;
        
        DeathmatchManager.Instance.OnGameModeEnded += OnGameModeEnded;
        
        NetworkPlayer.LocalInstance.ControlledPawn.OnChange += OnLocalPawnChanged;
        NetworkPlayer.LocalInstance.RespawnTimeEnd.OnChange += OnRespawnTimeEndChanged;

        // Manually trigger the first update to sync UI with current state
        OnCountdownChanged(0, MatchFlowManager.Instance.CountdownTime.Value, false);
        OnMatchStateChanged(MatchState.Pregame, MatchFlowManager.Instance.State.Value, false);
        OnLocalPawnChanged(null, NetworkPlayer.LocalInstance.ControlledPawn.Value, false);
    }

    private void OnDestroy()
    {
        if (!isSubscribed) return;

        var clientManager = InstanceFinder.ClientManager;
        if (clientManager != null)
            clientManager.OnClientConnectionState -= OnClientConnectionStateChanged;

        if (MatchFlowManager.Instance != null)
        {
            MatchFlowManager.Instance.CountdownTime.OnChange -= OnCountdownChanged;
            MatchFlowManager.Instance.State.OnChange -= OnMatchStateChanged;
        }

        if (DeathmatchManager.Instance != null)
        {
            DeathmatchManager.Instance.OnGameModeEnded -= OnGameModeEnded;
        }

        if (NetworkPlayer.LocalInstance != null)
        {
            NetworkPlayer.LocalInstance.ControlledPawn.OnChange -= OnLocalPawnChanged;
            NetworkPlayer.LocalInstance.RespawnTimeEnd.OnChange -= OnRespawnTimeEndChanged;
        }

        if (NetworkPlayer.LocalInstance != null && NetworkPlayer.LocalInstance.ControlledPawn.Value != null)
        {
            OnLocalPawnChanged(NetworkPlayer.LocalInstance.ControlledPawn.Value, null, false);
        }
    }

    // --- Named Event Handlers ---

    private void OnClientConnectionStateChanged(ClientConnectionStateArgs args)
    {
        bool started = args.ConnectionState == LocalConnectionState.Started;
        NetworkUIEvents.OnClientConnectionChanged?.Invoke(started);
    }

    private void OnCountdownChanged(int prev, int next, bool asServer)
    {
        if (asServer) return;
        NetworkUIEvents.OnCountdownChanged?.Invoke(next);
    }

    private void OnMatchStateChanged(MatchState prev, MatchState next, bool asServer)
    {
        if (asServer) return;
        NetworkUIEvents.OnMatchStateChanged?.Invoke(next);
    }

    private void OnGameModeEnded(NetworkPlayer winner)
    {
        NetworkUIEvents.OnGameModeEnded?.Invoke(winner);
    }

    private void OnRespawnTimeEndChanged(double prev, double next, bool asServer)
    {
        if (asServer) return;
        if (next > 0) NetworkUIEvents.OnRespawnTimerStarted?.Invoke(next);
    }

    private void OnLocalPawnChanged(Pawn prev, Pawn next, bool asServer)
    {
        if (asServer) return;

        // Unsubscribe from the old pawn's health, inventory, ammo
        if (prev != null)
        {
            prev.Health.OnChange -= OnLocalHealthChanged;
            prev.Inventory.Slots.OnChange -= OnSlotsChanged;
            prev.Ammo.AmmoPools.OnChange -= OnAmmoPoolChanged;
        }

        if (next != null)
        {
             // Call the OnLocalPawnSpawned event first so that views can listen to updates
             // that are called in this block (ie. OnLocalHealthChanged, OnSlotsChanged).
            NetworkUIEvents.OnLocalPawnSpawned?.Invoke(next);

            // Subscribe to the new pawn's health
            next.Health.OnChange += OnLocalHealthChanged;
            // Immediate update for the UI
            OnLocalHealthChanged(0, next.Health.Value, false);

            // Subscribe to the new pawn's inventory
            next.Inventory.Slots.OnChange += OnSlotsChanged;
            OnSlotsChanged(SyncListOperation.Set, 0, default, next.Inventory.Slots[0], false);
            OnSlotsChanged(SyncListOperation.Set, 1, default, next.Inventory.Slots[1], false);

            // Subscribe to the new pawn's ammo
            next.Ammo.AmmoPools.OnChange += OnAmmoPoolChanged;
            OnAmmoPoolChanged(SyncDictionaryOperation.Set, AmmoType.Bullet, next.Ammo.AmmoPools[AmmoType.Bullet], false);
        }
    }

    private void OnLocalHealthChanged(int prev, int next, bool asServer)
    {
        if (asServer) return;
        NetworkUIEvents.OnLocalHealthChanged?.Invoke(next);
        if(next < prev) NetworkUIEvents.OnLocalDamageTaken?.Invoke(prev - next);
        if(next <= 0) NetworkUIEvents.OnLocalDeath?.Invoke();
    }

    private void OnSlotsChanged(SyncListOperation op, int index, WeaponSlot oldItem, WeaponSlot newItem, bool asServer)
    {
        switch(op)
        {
            case SyncListOperation.Set:
                NetworkUIEvents.OnWeaponInventoryChanged?.Invoke(index, newItem);
                break;
        }
    }

    private void OnAmmoPoolChanged(SyncDictionaryOperation op, AmmoType ammo, int numAmmo, bool asServer)
    {
        switch (op)
        {
            case SyncDictionaryOperation.Set:
                NetworkUIEvents.OnAmmoPoolChanged?.Invoke(ammo, numAmmo);
                break;
        }
    }
}