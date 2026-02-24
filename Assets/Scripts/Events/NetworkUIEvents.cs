using System;

public static class NetworkUIEvents
{
    // Network connection events
    public static Action<bool> OnClientConnectionChanged;

    // High-level match events
    public static Action<int> OnCountdownChanged;
    public static Action<MatchState> OnMatchStateChanged;
    public static Action<NetworkPlayer> OnGameModeEnded;
    
    // Player-specific events
    public static Action<bool> OnLocalReadyStatusChanged;
    public static Action<Pawn> OnLocalPawnSpawned;
    public static Action<int> OnLocalHealthChanged;
    public static Action<int> OnLocalDamageTaken;
    public static Action OnLocalDeath;
    public static Action<double> OnRespawnTimerStarted;
    public static Action<int, WeaponSlot> OnWeaponInventoryChanged;
    public static Action<AmmoType, int> OnAmmoPoolChanged;
}