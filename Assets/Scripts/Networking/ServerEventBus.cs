using System;
using FishNet.Object;

public static class ServerEventBus
{
    public static event Action<KillEventData> OnPawnKilled;
    public static event Action<PlayerRegisteredData> OnPlayerRegistered;
    public static event Action<PlayerUnregisteredData> OnPlayerUnregistered;
    public static event Action<MatchBeganData> OnMatchBegan;

    [Server]
    public static void PublishPawnKilled(KillEventData data)
    {
        OnPawnKilled?.Invoke(data);
    }

    [Server]
    public static void PublishPlayerRegistered(PlayerRegisteredData data)
    {
        OnPlayerRegistered?.Invoke(data);
    }

    [Server]
    public static void PublishPlayerUnregistered(PlayerUnregisteredData data)
    {
        OnPlayerUnregistered?.Invoke(data);
    }

    [Server]
    public static void PublishMatchBegan(MatchBeganData data)
    {
        OnMatchBegan?.Invoke(data);
    }
}