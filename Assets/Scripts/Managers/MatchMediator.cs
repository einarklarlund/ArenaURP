using FishNet.Object;
using UnityEngine;
using System.Linq;
using FishNet;
using FishNet.Component.Spawning;
using FishNet.Object.Synchronizing;

public sealed class MatchMediator : NetworkBehaviour
{
    private readonly SyncList<NetworkPlayer> players = new();

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
            {
                ServerRegisterPlayer(p);
            }
        }

        // Subscribe to highest-level network events
        var playerSpawner = InstanceFinder.NetworkManager.GetComponent<PlayerSpawner>();
        playerSpawner.OnSpawned += ServerOnPlayerSpawned;

        // Subscribe to high-level match lifecycle events
        matchFlowManager.State.OnChange += ServerHandleMatchStateChanged;
        deathmatchManager.OnGameModeEnded += ServerHandleGameModeEnded;

        // Subscribe to pawn lifecycle events
        pawnManager.OnPawnKilled += ServerHandlePawnKilled;
    }

    [Server]
    private void ServerOnPlayerSpawned(NetworkObject playerObject)
    {
        ServerRegisterPlayer(playerObject.GetComponent<NetworkPlayer>());
    }

    [Server]
    private void ServerRegisterPlayer(NetworkPlayer player)
    {
        players.Add(player);
        player.IsReady.OnChange += ServerHandleIsReadyChanged;
        player.OnDespawn += ServerUnregisterPlayer;
        
        if (matchFlowManager.State.Value == MatchState.During)
            pawnManager.SpawnPawnForPlayer(player);
    }

    [Server]
    private void ServerUnregisterPlayer(NetworkPlayer player)
    {
        players.Remove(player);
        // NetworkObject ownership should get rid of the pawn prefab
    }

    [Server]
    private void ServerHandleMatchStateChanged(MatchState prev, MatchState next, bool asServer)
    {
        switch (next)
        {
            case MatchState.During:
                // this handler is getting called twice for some reason, with prev == next on the second call
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
        // player.IsReady.OnChange gets called twice when being set to true for some reason.
        // the second call has params prev = true, next = true
        if (prev == next) return;

        if (matchFlowManager.State.Value != MatchState.Pregame) return;

        bool everyoneReady = players.Count > 0 && players.All(p => p.IsReady.Value);

        if (everyoneReady)
        {
            matchFlowManager.EnterPregameCountdown(); // does not immediately begin the match
            foreach(var player in players)
            {
                player.IsReady.Value = false;
            }
        }
    }

    [Server]
    private void ServerHandleMatchStarted()
    {
        deathmatchManager.BeginGame();
        foreach (var player in players)
        {
            pawnManager.SpawnPawnForPlayer(player);
        }
    }

    [Server]
    private void ServerHandleGameModeEnded(NetworkPlayer winner)
    {
        foreach (var player in players)
        {
            deathmatchManager.ResetScore(player);
        }
        pawnManager.ClearPawns();
        matchFlowManager.EnterPostgameCountdown(); // does not immediately end the match
    }

    [Server]
    private void ServerHandlePawnKilled(Pawn pawn, DamageInfo damageInfo)
    {
        if (matchFlowManager.State.Value != MatchState.During) return;

        var killedPlayer = pawn.ControllingPlayer.Value;
        var killer = players.Find(p => p.Owner == damageInfo.Attacker);
        deathmatchManager.ServerRecordKill(killer, killedPlayer);

        deathmatchManager.CheckGameEnd(players.ToList());

        if (deathmatchManager.IsRunning)
            pawnManager.ServerStartRespawnTimerFor(pawn.ControllingPlayer.Value, deathmatchManager.RespawnDelay);
    }
}