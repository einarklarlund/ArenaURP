using System;
using FishNet.Object;
using UnityEngine;

public class BotSpawner : NetworkBehaviour
{
    public event Action<NetworkObject> OnSpawned;
    
    [SerializeField] private NetworkPlayer botPlayerPrefab;

    private void Start()
    {
        SignalManager.OnRoomCreated += HandleRoomCreated;
    }

    /// <summary>
    /// Spawns bot players and registers them.
    /// Reads the desired bot count from HostOptionsManager.
    /// </summary>
    private void HandleRoomCreated(string roomID)
    {
        int numBots = HostOptionsManager.Instance != null
            ? HostOptionsManager.Instance.Options.BotCount
            : 0;

        // Spawn bots
        for (int i = 0; i < numBots; i++)
        {
            SpawnBot();
        }
    }

    [Server]
    private void SpawnBot()
    {
        var bot = Instantiate(botPlayerPrefab);
        bot.IsBot = true;
        Spawn(bot);
        OnSpawned?.Invoke(bot);
    }
}