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

    // Player-specific events
    public static Action<bool> OnLocalReadyStatusChanged;
    public static Action<Pawn> OnLocalPawnSpawned;
    public static Action<int> OnLocalHealthChanged;
    public static Action<int> OnLocalDamageTaken;
    public static Action<int> OnLocalHeal;
    public static Action OnLocalDeath;
    public static Action<double> OnRespawnTimerStarted;
    public static Action<AmmoType, int> OnLocalAmmoChanged;
    public static Action<int> OnLocalExperienceChanged;
    /// <summary>The chest or free weapon the local player could interact with right now; null when nothing is in reach.</summary>
    public static Action<UnityEngine.Component> OnLocalInteractTargetChanged;
    /// <summary>The local player's weapon in hand and the stowed one; either may be null.</summary>
    public static Action<Weapon, Weapon> OnLocalWeaponsChanged;
    /// <summary>The local player's upgrade offer, picks pending or re-roll charges changed. Read them off NetworkPlayer.LocalInstance.</summary>
    public static Action OnLocalUpgradeOfferChanged;

    public static void Reset()
    {
        OnClientConnectionChanged = null;
        OnCurrentRoomChanged = null;
        OnCountdownChanged = null;
        OnMatchStateChanged = null;
        OnLeadPlayerChanged = null;
        OnLocalReadyStatusChanged = null;
        OnLocalPawnSpawned = null;
        OnLocalHealthChanged = null;
        OnLocalDamageTaken = null;
        OnLocalHeal = null;
        OnLocalDeath = null;
        OnRespawnTimerStarted = null;
        OnLocalAmmoChanged = null;
        OnLocalExperienceChanged = null;
        OnLocalInteractTargetChanged = null;
        OnLocalWeaponsChanged = null;
        OnLocalUpgradeOfferChanged = null;
    }
}
