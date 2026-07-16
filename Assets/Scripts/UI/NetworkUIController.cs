using FishNet;
using FishNet.Transporting;
using UnityEngine;

/// <summary>
/// The reactive bridge between Network SyncVars and the local UI.
/// Responsibilities:
/// - Monitors player SyncVars for changes.
/// - Translates complex network data into simple, decoupled signals via NetworkUIEvents.
///
/// Match-level state (countdown, match state, lead player) is published onto the
/// NetworkUIEvents bus by the owning managers themselves (MatchFlowManager /
/// DeathmatchManager), so this bridge no longer reaches into those singletons.
/// </summary>
public sealed class NetworkUIController : MonoBehaviour
{
    private bool isSubscribed = false;

    private void Update()
    {
        // Wait until the managers and the local player are ready
        if (!isSubscribed && NetworkPlayer.LocalInstance != null && DeathmatchManager.Instance != null)
        {
            InitializeSubscriptions();
        }
    }

    private void InitializeSubscriptions()
    {
        isSubscribed = true;

        InstanceFinder.ClientManager.OnClientConnectionState += OnClientConnectionStateChanged;

        DeathmatchManager.Instance.OnGameModeEnded += OnGameModeEnded;

        NetworkPlayer.LocalInstance.ControlledPawn.OnChange += OnLocalPawnChanged;
        NetworkPlayer.LocalInstance.RespawnTimeEnd.OnChange += OnRespawnTimeEndChanged;

        // Manually trigger the first update to sync UI with current state
        OnLocalPawnChanged(null, NetworkPlayer.LocalInstance.ControlledPawn.Value, false);
    }

    private void OnDestroy()
    {
        if (!isSubscribed) return;

        var clientManager = InstanceFinder.ClientManager;
        if (clientManager != null)
            clientManager.OnClientConnectionState -= OnClientConnectionStateChanged;

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
        }
    }

    private void OnLocalHealthChanged(int prev, int next, bool asServer)
    {
        NetworkUIEvents.OnLocalHealthChanged?.Invoke(next);
        if(next < prev) NetworkUIEvents.OnLocalDamageTaken?.Invoke(prev - next);
        if(next <= 0) NetworkUIEvents.OnLocalDeath?.Invoke();
    }
}