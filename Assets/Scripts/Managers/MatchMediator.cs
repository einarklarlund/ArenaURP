using FishNet.Object;
using UnityEngine;
using System.Collections;
using System.Linq;

public class MatchMediator : NetworkBehaviour
{
    [SerializeField] protected NetworkPlayerManager networkPlayerManager;
    [SerializeField] protected PawnManager pawnManager;
    [SerializeField] protected MatchFlowManager matchFlowManager;
    [SerializeField] protected DeathmatchManager deathmatchManager;

    [Tooltip("The shared passive-kind catalogue, spawned once per match. Its children are the passive KINDs every " +
             "player's loadout references. Leave null to run without passives.")]
    [SerializeField] protected PassiveRegistry passiveRegistryPrefab;

    public override void OnStartServer()
    {
        base.OnStartServer();

        // Spawn the one shared passive registry for this match before any grant path can reference its kinds.
        // Idempotent: a scene may carry more than one mediator (e.g. a Debug variant), and only one registry is
        // wanted — the server runs OnStartServer callbacks sequentially, so the first spawn sets Instance and the
        // rest no-op here.
        if (passiveRegistryPrefab != null && PassiveRegistry.Instance == null)
        {
            PassiveRegistry registry = Instantiate(passiveRegistryPrefab);
            ServerManager.Spawn(registry.NetworkObject);
        }

        // Subscribe to and kick-off NetworkPlayer registration
        networkPlayerManager.OnPlayerRegistered += ServerHandlePlayerRegistered;
        networkPlayerManager.Initialize();

        // Subscribe to high-level match lifecycle events
        matchFlowManager.State.OnChange += ServerHandleMatchStateChanged;
        deathmatchManager.OnGameModeEnded += ServerHandleGameModeEnded;

        // Subscribe to pawn lifecycle events
        pawnManager.OnPawnKilled += ServerHandlePawnKilled;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();

        if (networkPlayerManager != null)
            networkPlayerManager.OnPlayerRegistered -= ServerHandlePlayerRegistered;

        if (matchFlowManager != null)
            matchFlowManager.State.OnChange -= ServerHandleMatchStateChanged;

        if (deathmatchManager != null)
            deathmatchManager.OnGameModeEnded -= ServerHandleGameModeEnded;

        if (pawnManager != null)
            pawnManager.OnPawnKilled -= ServerHandlePawnKilled;
    }

    // --- client-side manager wiring -----------------------------------------
    // Managers never subscribe to each other; the mediator is the one place they are
    // wired. On the client it watches the match state so the DeathmatchManager can
    // re-broadcast the current leader once the postgame view is up (the lead SyncVar
    // last changed mid-match, before that view existed to hear it).

    public override void OnStartClient()
    {
        base.OnStartClient();
        matchFlowManager.State.OnChange += ClientHandleMatchStateChanged;
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        if (matchFlowManager != null)
            matchFlowManager.State.OnChange -= ClientHandleMatchStateChanged;
    }

    private void ClientHandleMatchStateChanged(MatchState prev, MatchState next, bool asServer)
    {
        if (asServer) return;
        if (next == MatchState.PostgameCountdown)
            StartCoroutine(ReseedLeadPlayerNextFrame());
    }

    // Defer one frame so the PostgameCountdownView (shown this frame in response to the
    // same state change) has already subscribed to OnLeadPlayerChanged before we re-emit.
    private IEnumerator ReseedLeadPlayerNextFrame()
    {
        yield return null;
        if (deathmatchManager != null)
            deathmatchManager.PublishLeadToBus();
    }

    /// <summary>
    /// Registers event handlers.
    /// Spawns a pawn if the game is already started.
    /// </summary>
    [Server]
    private void ServerHandlePlayerRegistered(NetworkPlayer networkPlayer)
    {
        networkPlayer.IsReady.OnChange += ServerHandleIsReadyChanged;
        // Clean up the player's pawn when they leave/disconnect. Without this subscription
        // ServerHandlePlayerDespawn never ran, so a departing player's pawn was orphaned.
        networkPlayer.OnDespawn += ServerHandlePlayerDespawn;

        if (matchFlowManager.State.Value == MatchState.During)
            pawnManager.SpawnPawnForPlayer(networkPlayer);
    }

    /// <summary>
    /// Unregisters the player with other services.
    /// Unregisters player event handlers.
    /// </summary>
    // Invoked from NetworkPlayer.OnDespawn, which also fires while the server is shutting
    // down (every player object deinitializes at once). In that path FishNet is already
    // despawning everything, so the [Server] guard correctly no-ops here; Logging.Off keeps
    // that expected teardown case from spamming "server is not active" warnings.
    [Server(Logging = FishNet.Managing.Logging.LoggingType.Off)]
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
