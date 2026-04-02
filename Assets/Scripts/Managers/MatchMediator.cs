using FishNet.Object;
using UnityEngine;
using System.Linq;

public class MatchMediator : NetworkBehaviour
{
    [SerializeField] protected NetworkPlayerManager networkPlayerManager;
    [SerializeField] protected PawnManager pawnManager;
    [SerializeField] protected MatchFlowManager matchFlowManager;
    [SerializeField] protected DeathmatchManager deathmatchManager;

    public override void OnStartServer()
    {
        base.OnStartServer();

        // Subscribe to and kick-off NetworkPlayer registration
        networkPlayerManager.OnPlayerRegistered += ServerHandlePlayerRegistered;
        networkPlayerManager.Initialize();

        // Subscribe to high-level match lifecycle events
        matchFlowManager.State.OnChange += ServerHandleMatchStateChanged;
        deathmatchManager.OnGameModeEnded += ServerHandleGameModeEnded;

        // Subscribe to pawn lifecycle events
        pawnManager.OnPawnKilled += ServerHandlePawnKilled;
    }

    /// <summary>
    /// Registers event handlers.
    /// Spawns a pawn if the game is already started.
    /// </summary>
    [Server]
    private void ServerHandlePlayerRegistered(NetworkPlayer networkPlayer)
    {
        networkPlayer.IsReady.OnChange += ServerHandleIsReadyChanged;

        if (matchFlowManager.State.Value == MatchState.During)
            pawnManager.SpawnPawnForPlayer(networkPlayer);
    }

    /// <summary>
    /// Unregisters the player with other services.
    /// Unregisters player event handlers.
    /// </summary>
    [Server]
    private void ServerHandlePlayerDespawn(NetworkPlayer networkPlayer)
    {
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
            foreach (var player in networkPlayerManager.HumanPlayers)
                player.IsReady.Value = false;
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
        var killedPlayer = pawn.ControllingPlayer.Value;
        var killer = damageInfo.Attacker;

        deathmatchManager.ServerRecordKill(killer, killedPlayer);

        if (matchFlowManager.State.Value != MatchState.During) return;
        pawnManager.ServerStartRespawnTimerFor
        (
            killedPlayer,
            deathmatchManager.RespawnDelay
        );
    }
}
