using System;
using System.Collections.Generic;
using System.Linq;
using FishNet;
using FishNet.Component.Spawning;
using FishNet.Object;
using FishNet.Object.Synchronizing;

/// <summary>
/// Manages lifecycle and bookkeeping of NetworkPlayer objects.
/// </summary>
public class NetworkPlayerManager : NetworkBehaviour
{
    public event Action<NetworkPlayer> OnPlayerRegistered;
    public event Action<NetworkPlayer> OnPlayerUnregistered;
    public readonly SyncList<NetworkPlayer> ActivePlayers = new();
    public IEnumerable<NetworkPlayer> HumanPlayers => ActivePlayers.Where(p => !p.IsBot);
    public IEnumerable<NetworkPlayer> BotPlayers => ActivePlayers.Where(p => p.IsBot);

    public void Initialize()
    {
        // Register existing players
        BotSpawner botSpawner = null;
        foreach (NetworkObject nob in InstanceFinder.NetworkManager.ServerManager.Objects.Spawned.Values)
        {
            if (nob.TryGetComponent(out NetworkPlayer p))
                ServerRegisterPlayer(p);
            if (nob.TryGetComponent(out BotSpawner spawner))
                botSpawner = spawner;
        }

        // PlayerSpawner is the human-registration path in real builds. It's optional, though:
        // PlayMode test harnesses run on a NetworkManager with no PlayerSpawner and register
        // players directly (see MatchTestRig), so only subscribe when one is present.
        var playerSpawner = InstanceFinder.NetworkManager.GetComponent<PlayerSpawner>();
        if (playerSpawner != null)
            playerSpawner.OnSpawned += ServerHandlePlayerSpawn;

        if (botSpawner == null)
            botSpawner = FindAnyObjectByType<BotSpawner>();

        botSpawner.OnSpawned += ServerHandlePlayerSpawn;
    }

    /// <summary>
    /// Adds player to public registry.
    /// </summary>
    [Server]
    private void ServerHandlePlayerSpawn(NetworkObject playerObject)
    {
        var networkPlayer = playerObject.GetComponent<NetworkPlayer>();
        ServerRegisterPlayer(networkPlayer);
    }

    [Server]
    private void ServerRegisterPlayer(NetworkPlayer player)
    {
        ActivePlayers.Add(player);
        player.OnDespawn += ServerUnregisterPlayer;
        OnPlayerRegistered?.Invoke(player);
    }

    /// <summary>
    /// Removes player from public registry.
    /// </summary>
    // Invoked from NetworkPlayer.OnDespawn, which also fires while the server is shutting
    // down (every player object deinitializes at once). The [Server] guard correctly no-ops
    // in that path; Logging.Off keeps that expected teardown case from spamming
    // "server is not active" warnings.
    [Server(Logging = FishNet.Managing.Logging.LoggingType.Off)]
    private void ServerUnregisterPlayer(NetworkPlayer player)
    {
        ActivePlayers.Remove(player);
        OnPlayerUnregistered?.Invoke(player);
    }
}
