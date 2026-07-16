using System;

public static class NetworkUIEvents
{
    // Network connection events
    public static Action<bool> OnClientConnectionChanged;
    public static Action<string> OnCurrentRoomChanged;

    // High-level match events
    public static Action<int> OnCountdownChanged;
    public static Action<MatchState> OnMatchStateChanged;
    public static Action<NetworkPlayer> OnLeadPlayerChanged;
    public static Action<NetworkPlayer> OnGameModeEnded;

    // Player-specific events
    public static Action<bool> OnLocalReadyStatusChanged;
    public static Action<Pawn> OnLocalPawnSpawned;
    public static Action<int> OnLocalHealthChanged;
    public static Action<int> OnLocalDamageTaken;
    public static Action OnLocalDeath;
    public static Action<double> OnRespawnTimerStarted;

    public static void Reset()
    {
        OnClientConnectionChanged = null;
        OnCurrentRoomChanged = null;
        OnCountdownChanged = null;
        OnMatchStateChanged = null;
        OnLeadPlayerChanged = null;
        OnGameModeEnded = null;
        OnLocalReadyStatusChanged = null;
        OnLocalPawnSpawned = null;
        OnLocalHealthChanged = null;
        OnLocalDamageTaken = null;
        OnLocalDeath = null;
        OnRespawnTimerStarted = null;
    }
}
