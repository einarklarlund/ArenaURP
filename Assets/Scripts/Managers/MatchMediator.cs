using FishNet.Object;
using UnityEngine;
using System.Linq;
using FishNet;
using FishNet.Component.Spawning;

public sealed class MatchMediator : NetworkBehaviour
{
    [SerializeField] private BotSpawner botSpawner;
    [SerializeField] private NetworkPlayerManager networkPlayerManager;
    [SerializeField] private PawnManager pawnManager;
    [SerializeField] private MatchFlowManager matchFlowManager;
    [SerializeField] private DeathmatchManager deathmatchManager;

    public override void OnStartServer()
    {
        base.OnStartServer();

        // Register existing players (i.e. if server is a host instead of headless)
        foreach (NetworkObject nob in InstanceFinder.NetworkManager.ServerManager.Objects.Spawned.Values)
        {
            if (nob.TryGetComponent(out NetworkPlayer p))
                networkPlayerManager.ServerRegisterPlayer(p);
        }

        // Subscribe to highest-level network events
        var playerSpawner = InstanceFinder.NetworkManager.GetComponent<PlayerSpawner>();
        playerSpawner.OnSpawned += ServerHandlePlayerSpawn;

        botSpawner.OnSpawned += ServerHandlePlayerSpawn;

        // Subscribe to high-level match lifecycle events
        matchFlowManager.State.OnChange += ServerHandleMatchStateChanged;
        deathmatchManager.OnGameModeEnded += ServerHandleGameModeEnded;

        // Subscribe to pawn lifecycle events
        pawnManager.OnPawnKilled += ServerHandlePawnKilled;
    }

    /// <summary>
    /// Handles PlayerSpawner OnSpawned events.
    /// Registers the player with other services.
    /// Registers event handlers player event handlers.
    /// </summary>
    [Server]
    private void ServerHandlePlayerSpawn(NetworkObject playerObject)
    {
        var networkPlayer = playerObject.GetComponent<NetworkPlayer>();
        networkPlayerManager.ServerRegisterPlayer(networkPlayer);

        networkPlayer.IsReady.OnChange += ServerHandleIsReadyChanged;
        networkPlayer.OnDespawn += ServerHandlePlayerDespawn;

        if (matchFlowManager.State.Value == MatchState.During)
            pawnManager.SpawnPawnForPlayer(networkPlayer);
    }

    /// <summary>
    /// Handles NetworkPlayer OnDespawn events.
    /// Unregisters the player with other services.
    /// Unregisters player event handlers.
    /// </summary>
    [Server]
    private void ServerHandlePlayerDespawn(NetworkPlayer networkPlayer)
    {
        networkPlayerManager.ServerUnregisterPlayer(networkPlayer);
        pawnManager.UnregisterPawnForPlayer(networkPlayer);

        networkPlayer.IsReady.OnChange -= ServerHandleIsReadyChanged;
        networkPlayer.OnDespawn -= ServerHandlePlayerDespawn;
    }

    [Server]
    private void ServerHandleMatchStateChanged(MatchState prev, MatchState next, bool asServer)
    {
        switch (next)
        {
            case MatchState.During:
                if (prev == next) return;
                ServerHandleMatchStarted();
                break;
            case MatchState.Postgame:
                matchFlowManager.EnterPregame();
                break;
        }
    }

    [Server]
    private void ServerHandleIsReadyChanged(bool prev, bool next, bool asServer)
    {
        if (prev == next) return;
        if (matchFlowManager.State.Value != MatchState.Pregame) return;

        // Only human players participate in the ready check.
        bool everyoneReady = networkPlayerManager.HumanPlayers.Count() > 0
            && networkPlayerManager.HumanPlayers.All(p => p.IsReady.Value);

        if (everyoneReady)
        {
            matchFlowManager.EnterPregameCountdown();
        }
    }

    [Server]
    private void ServerHandleMatchStarted()
    {
        deathmatchManager.BeginGame();

        foreach (var player in networkPlayerManager.ActivePlayers)
        {
            pawnManager.SpawnPawnForPlayer(player);
        }
    }

    [Server]
    private void ServerHandleGameModeEnded(NetworkPlayer winner)
    {
        foreach (var player in networkPlayerManager.ActivePlayers)
        {
            deathmatchManager.ResetScore(player);
        }
        pawnManager.ClearPawns();
        matchFlowManager.EnterPostgameCountdown();
    }

    [Server]
    private void ServerHandlePawnKilled(Pawn pawn, DamageInfo damageInfo)
    {
        if (matchFlowManager.State.Value != MatchState.During) return;

        var killedPlayer = pawn.ControllingPlayer.Value;
        var killer = damageInfo.Attacker;

        deathmatchManager.ServerRecordKill(killer, killedPlayer);

        pawnManager.ServerStartRespawnTimerFor
        (
            killedPlayer,
            deathmatchManager.RespawnDelay
        );
    }
}
