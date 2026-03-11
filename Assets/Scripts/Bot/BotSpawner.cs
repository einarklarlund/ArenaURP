using System;
using FishNet.Object;
using UnityEngine;

public class BotSpawner : NetworkBehaviour
{
    public event Action<NetworkObject> OnSpawned;
    
    [SerializeField] private int numBots = 0;
    [SerializeField] private NetworkPlayer botPlayerPrefab;

    public override void OnStartServer()
    {
        base.OnStartServer();
        SpawnBots();
    }

    /// <summary>
    /// Spawns bot players and registers them.
    /// </summary>
    [Server]
    private void SpawnBots()
    {
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