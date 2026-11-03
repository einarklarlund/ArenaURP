using System;
using System.Collections.Generic;
using System.Linq;
using FishNet.Component.Spawning;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

public enum MatchState
{
    Pregame,            // Waiting for every human player to ready up.
    PregameCountdown,   // Everyone is ready; counting down to the start.
    During,             // Match in progress.
    PostgameCountdown,  // Match over; winner on screen, counting down to the next Pregame.
    Postgame,           // Never entered: PostgameCountdown returns straight to Pregame.
                        // Kept so existing references still compile.
}

/// <summary>
/// Runs the game from the server: who is playing, when a match starts and ends, when and
/// where pawns spawn, and who is winning.
/// </summary>
public sealed class MatchManager : NetworkBehaviour
{
    // ── Public API ───────────────────────────────────────────────────────

    public static MatchManager Instance;
    /// <summary>Every player currently in the game, humans and bots. Written by the server.</summary>
    public readonly SyncList<NetworkPlayer> ActivePlayers = new();
    public MatchState State => matchState.Value;
    /// <summary>Seconds since the match started; 0 outside a match. Server only.</summary>
    public double ServerMatchTime => State == MatchState.During ? Now - (matchEndTime - matchDuration) : 0;

    // ── Settings ─────────────────────────────────────────────────────────

    [Header("Pawns")]
    [SerializeField] private Pawn humanPawnPrefab;
    [SerializeField] private Pawn botPawnPrefab;
    [SerializeField] private Transform[] spawnPoints;

    [Header("Match")]
    [SerializeField] private int beginMatchCountdown = 3;
    [SerializeField] private int endMatchCountdown = 20;
    [SerializeField] private float matchDuration = 600f;
    [Tooltip("The match ends when a player reaches this score. 0 or less disables the score limit.")]
    [SerializeField] private int killsToWin = 5;

    [Header("Debug")]
    [SerializeField] private bool skipToMatch = false;

    // ── Replicated state ─────────────────────────────────────────────────
    // Written only by the server. Clients forward every change to NetworkUIEvents (see CLIENT).

    private readonly SyncVar<NetworkPlayer> leadPlayer = new();
    private readonly SyncVar<int> countdownSeconds = new();
    private readonly SyncVar<MatchState> matchState = new(MatchState.Pregame);

    // ── Server-only state ────────────────────────────────────────────────

    private PlayerSpawner playerSpawner;

    // Living pawns and the death handler subscribed on each.
    private readonly Dictionary<Pawn, Action<DamageInfo>> livingPawns = new();
    private int nextSpawnIndex;

    // Players ready to spawn whose level isn't built yet, and when to try them again.
    private readonly Dictionary<NetworkPlayer, double> waitingForLevel = new();

    /// <summary>Generated spawn points are on the ground; a pawn starts this far above it.</summary>
    private const float SpawnLift = 0.05f;
    /// <summary>How often a human whose level isn't built yet is tried again.</summary>
    private const double LevelReadyRetry = 0.5;

    // Absolute times on the network clock (see Now).
    private double countdownEndTime;
    private double matchEndTime;

    private IEnumerable<NetworkPlayer> HumanPlayers => ActivePlayers.Where(p => !p.IsBot);

    private double Now => TimeManager.TicksToTime();

    // ═════════════════════════════════════════════════════════════════════
    //  CLIENT: forward replicated state to NetworkUIEvents
    // ═════════════════════════════════════════════════════════════════════
    public override void OnStartNetwork()
    {
        base.OnStartNetwork();

        if (Instance == null)
            Instance = this;
        else
            Debug.LogWarning("There is a duplicate MatchManager in the scene!");
    }

    // Leaving a match destroys the online scene, and the next match's MatchManager must not find this one here.
    public override void OnStopNetwork()
    {
        base.OnStopNetwork();

        if (Instance == this)
            Instance = null;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  SERVER: start-up and shutdown
    // ═════════════════════════════════════════════════════════════════════

    public override void OnStartServer()
    {
        base.OnStartServer();

        // Players that spawned before this manager did...
        foreach (NetworkObject nob in ServerManager.Objects.Spawned.Values)
        {
            if (nob.TryGetComponent(out NetworkPlayer player))
                ServerOnPlayerJoined(player);
        }

        // ...and everyone the PlayerSpawner spawns from now on.
        playerSpawner = NetworkManager.GetComponent<PlayerSpawner>();
        if (playerSpawner != null)
            playerSpawner.OnSpawned += ServerOnPlayerObjectSpawned;

        if (LevelSync.Instance != null)
            LevelSync.Instance.OnServerLevelIssued += ServerOnLevelIssued;

        if (skipToMatch)
            ServerStartMatch();
    }

    public override void OnStopServer()
    {
        base.OnStopServer();

        if (playerSpawner != null)
            playerSpawner.OnSpawned -= ServerOnPlayerObjectSpawned;

        if (LevelSync.Instance != null)
            LevelSync.Instance.OnServerLevelIssued -= ServerOnLevelIssued;

        foreach (NetworkPlayer player in ActivePlayers)
        {
            if (player == null) continue;
            player.IsReady.OnChange -= ServerOnPlayerReadyChanged;
            player.OnDespawn -= ServerOnPlayerLeft;
        }

        foreach (KeyValuePair<Pawn, Action<DamageInfo>> entry in livingPawns)
        {
            if (entry.Key != null)
                entry.Key.OnDeath -= entry.Value;
        }
        livingPawns.Clear();
    }

    // ═════════════════════════════════════════════════════════════════════
    //  SERVER: Game Flow
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>The clock: advances whichever timer the current state is waiting on.</summary>
    private void Update()
    {
        if (!IsServerStarted || !IsServerInitialized) return;

        switch (matchState.Value)
        {
            case MatchState.PregameCountdown:
                if (ServerTickCountdown())
                    ServerStartMatch();
                break;

            case MatchState.During:
                if (ServerTickMatchTimer())
                    ServerEndMatch();
                else
                    ServerTickWaitingForLevel();
                break;

            case MatchState.PostgameCountdown:
                if (ServerTickCountdown())
                    ServerReturnToPregame();
                break;
        }
    }

    /// <summary>Everyone is ready: count down to the start of the match.</summary>
    [Server]
    private void ServerEnterPregameCountdown()
    {
        ServerSetMatchState(MatchState.PregameCountdown);
        ServerBeginCountdown(beginMatchCountdown);

        foreach (NetworkPlayer player in HumanPlayers)
            player.IsReady.Value = false;
    }

    /// <summary>The pregame countdown reached zero: the match begins.</summary>
    [Server]
    private void ServerStartMatch()
    {
        ServerSetMatchState(MatchState.During);
        leadPlayer.Value = null;
        matchEndTime = Now + matchDuration;

        // Everyone picks their first upgrades, then spawns (see NetworkPlayer.ServerBeginRespawn).
        foreach (NetworkPlayer player in ActivePlayers)
        {
            player.ServerResetProgression();
            player.ServerBeginRespawn();
        }
    }

    /// <summary>The time limit or score limit was reached: the match is over.</summary>
    [Server]
    private void ServerEndMatch()
    {
        foreach (NetworkPlayer player in ActivePlayers)
        {
            player.Score.Value = 0;
            player.ServerResetProgression(); // also cancels any pending respawn
        }
        waitingForLevel.Clear();

        ServerDespawnAllPawns();

        // leadPlayer is deliberately left alone: it is the winner the postgame screen shows.
        ServerSetMatchState(MatchState.PostgameCountdown);
        ServerBeginCountdown(endMatchCountdown);

        // Every match gets a new level, generated during the postgame countdown.
        if (LevelSync.Instance != null && LevelSync.Instance.GeneratesLevels)
            LevelSync.Instance.ServerNextLevel();
    }

    /// <summary>The postgame countdown reached zero: back to waiting for players to ready up.</summary>
    [Server]
    private void ServerReturnToPregame()
    {
        ServerSetMatchState(MatchState.Pregame);
    }

    /// <summary>
    /// A new level mid-match (regenerated or loaded from the debug view): everyone leaves the old one and respawns on
    /// the new one, humans once their client has built it.
    /// </summary>
    [Server]
    private void ServerOnLevelIssued()
    {
        if (matchState.Value != MatchState.During) return;

        // Not a death: everyone keeps their upgrades. Anyone still picking spawns when they're done.
        ServerDespawnAllPawns();
        foreach (NetworkPlayer player in ActivePlayers)
        {
            if (!player.IsPickingBeforeSpawn)
                ServerSpawn(player);
        }
    }

    /// <summary>The only way to change matchState: sets it and raises NetworkUIEvents.OnMatchStateChanged.</summary>
    [Server]
    private void ServerSetMatchState(MatchState state)
    {
        matchState.Value = state;
        NetworkUIEvents.OnMatchStateChanged?.Invoke(state);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  SERVER: NetworkPlayers
    // ═════════════════════════════════════════════════════════════════════

    [Server]
    private void ServerOnPlayerObjectSpawned(NetworkObject playerObject)
    {
        if (playerObject.TryGetComponent(out NetworkPlayer player))
            ServerOnPlayerJoined(player);
    }

    /// <summary>Any time: a player (human or bot) entered the game.</summary>
    [Server]
    private void ServerOnPlayerJoined(NetworkPlayer player)
    {
        if (ActivePlayers.Contains(player)) return;

        ActivePlayers.Add(player);
        player.IsReady.OnChange += ServerOnPlayerReadyChanged;
        player.OnDespawn += ServerOnPlayerLeft;

        // Joining mid-match: pick, then drop in.
        if (matchState.Value == MatchState.During)
        {
            player.ServerResetProgression();
            player.ServerBeginRespawn();
        }
    }

    /// <summary>Pregame: a player toggled ready. Once every human is ready, start counting down.</summary>
    private void ServerOnPlayerReadyChanged(bool prev, bool next, bool asServer)
    {
        // On a host this fires once for the server side and once for the client side.
        if (!asServer) return;
        if (matchState.Value != MatchState.Pregame) return;

        // Bots don't take part in the ready check.
        bool everyoneReady = HumanPlayers.Any() && HumanPlayers.All(p => p.IsReady.Value);
        if (everyoneReady)
            ServerEnterPregameCountdown();
    }

    /// <summary>Any time: a player left the game.</summary>
    private void ServerOnPlayerLeft(NetworkPlayer player)
    {
        player.IsReady.OnChange -= ServerOnPlayerReadyChanged;
        player.OnDespawn -= ServerOnPlayerLeft;

        // Every player also despawns while the server itself shuts down. FishNet is tearing
        // everything down at that point, so there is nothing left to manage.
        if (!IsServerStarted) return;

        ActivePlayers.Remove(player);
        waitingForLevel.Remove(player);
        ServerStopTrackingPawn(player.ControlledPawn.Value);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  SERVER: Pawns
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>During: a pawn died. Its player handles its own respawn (NetworkPlayer.ServerOnPawnDied).</summary>
    [Server]
    private void ServerOnPawnKilled(Pawn pawn, DamageInfo damageInfo)
    {
        ServerStopTrackingPawn(pawn);

        NetworkPlayer victim = pawn.ControllingPlayer.Value;
        NetworkPlayer killer = damageInfo.Attacker;
        ServerScoreKill(killer, victim);
    }

    /// <summary>A player is ready to play: spawns their pawn now, or once their level is built.</summary>
    [Server]
    public void ServerSpawn(NetworkPlayer player)
    {
        if (matchState.Value != MatchState.During) return;

        // Everyone waits while the level is still being generated (a player can join, or skipToMatch start the
        // match, before LevelSync has made one), and a human until their client has built it.
        LevelSync level = LevelSync.Instance;
        if (level != null && (level.LevelPending || (!player.IsBot && !level.IsReady(player.Owner))))
        {
            waitingForLevel[player] = Now + LevelReadyRetry;
            return;
        }

        waitingForLevel.Remove(player);
        ServerSpawnPawn(player);
    }

    [Server]
    private void ServerSpawnPawn(NetworkPlayer player)
    {

        if (!TryNextSpawnPoint(out Vector3 position, out Quaternion rotation))
        {
            Debug.LogError("No spawn points: none assigned to MatchManager, and no generated level.", this);
            return;
        }

        Pawn prefab;
        if (player.IsBot)
            prefab = botPawnPrefab;
        else if (player.CharacterPrefab.Value != null)
            prefab = player.CharacterPrefab.Value;
        else
            prefab = humanPawnPrefab;

        Pawn pawn = Instantiate(prefab, position, rotation);
        Spawn(pawn, player.Owner);

        pawn.ControllingPlayer.Value = player;
        player.ControlledPawn.Value = pawn;

        // Pawn.OnDeath doesn't say which pawn died, so each pawn gets its own handler.
        // It is stored so it can be unsubscribed later.
        Action<DamageInfo> deathHandler = damageInfo => ServerOnPawnKilled(pawn, damageInfo);
        pawn.OnDeath += deathHandler;
        livingPawns[pawn] = deathHandler;
    }

    /// <summary>
    /// The next spawn point, round-robin: the generated level's when one is built (they come spread out, each far from
    /// the ones before it), else the assigned transforms.
    /// </summary>
    [Server]
    private bool TryNextSpawnPoint(out Vector3 position, out Quaternion rotation)
    {
        var generated = LevelSync.Instance != null ? LevelSync.Instance.Level?.SpawnPoints : null;
        if (generated != null && generated.Count > 0)
        {
            var point = generated[nextSpawnIndex % generated.Count];
            nextSpawnIndex = (nextSpawnIndex + 1) % generated.Count;
            position = point.Position + Vector3.up * SpawnLift;
            rotation = Quaternion.Euler(0f, point.YawDegrees, 0f);
            return true;
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            position = default;
            rotation = default;
            return false;
        }
        Transform spawnPoint = spawnPoints[nextSpawnIndex % spawnPoints.Length];
        nextSpawnIndex = (nextSpawnIndex + 1) % spawnPoints.Length;
        position = spawnPoint.position;
        rotation = spawnPoint.rotation;
        return true;
    }

    /// <summary>Stops listening for a pawn's death. Does not despawn it.</summary>
    [Server]
    private void ServerStopTrackingPawn(Pawn pawn)
    {
        if (pawn == null) return;
        if (!livingPawns.TryGetValue(pawn, out Action<DamageInfo> deathHandler)) return;

        pawn.OnDeath -= deathHandler;
        livingPawns.Remove(pawn);
    }

    [Server]
    private void ServerDespawnAllPawns()
    {
        foreach (KeyValuePair<Pawn, Action<DamageInfo>> entry in livingPawns)
        {
            Pawn pawn = entry.Key;
            if (pawn == null) continue;

            pawn.OnDeath -= entry.Value;
            Despawn(pawn);
        }
        livingPawns.Clear();
    }

    // ═════════════════════════════════════════════════════════════════════
    //  SERVER: Waiting for the level
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Tries again to spawn everyone whose level wasn't built.</summary>
    [Server]
    private void ServerTickWaitingForLevel()
    {
        if (waitingForLevel.Count == 0) return;

        double now = Now;
        foreach (NetworkPlayer player in waitingForLevel.Keys.ToList())
        {
            if (now >= waitingForLevel[player])
                ServerSpawn(player);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  SERVER: Scoring
    // ═════════════════════════════════════════════════════════════════════

    [Server]
    private void ServerScoreKill(NetworkPlayer killer, NetworkPlayer victim)
    {
        if (killer == null)
        {
            // Nobody to credit: the victim loses a point instead.
            if (victim != null)
                victim.Score.Value -= 1;
            return;
        }

        killer.Score.Value++;

        // The match timer reads the leader's score every tick, so a score-limit win is
        // picked up on the next Update.
        NetworkPlayer leader = leadPlayer.Value;
        if (leader == null || killer.Score.Value > leader.Score.Value)
            leadPlayer.Value = killer;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  SERVER: Countdowns
    // ═════════════════════════════════════════════════════════════════════

    [Server]
    private void ServerBeginCountdown(int seconds)
    {
        countdownEndTime = Now + seconds;
        countdownSeconds.Value = seconds;
    }

    /// <returns>True once the countdown reaches zero.</returns>
    [Server]
    private bool ServerTickCountdown()
    {
        double remaining = countdownEndTime - Now;

        // The UI shows whole seconds, so only publish when that number changes.
        int display = Math.Max(0, (int)Math.Ceiling(remaining));
        if (display != countdownSeconds.Value)
            countdownSeconds.Value = display;

        return remaining <= 0;
    }

    // ── Match timer (time limit and score limit) ─────────────────────────

    /// <returns>True once the time limit or the score limit is reached.</returns>
    [Server]
    private bool ServerTickMatchTimer()
    {
        NetworkPlayer leader = leadPlayer.Value;
        bool scoreLimitReached = killsToWin > 0 && leader != null && leader.Score.Value >= killsToWin;
        bool timeLimitReached = Now >= matchEndTime;

        return scoreLimitReached || timeLimitReached;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  CLIENT: forward replicated state to NetworkUIEvents
    // ═════════════════════════════════════════════════════════════════════

    public override void OnStartClient()
    {
        base.OnStartClient();

        matchState.OnChange += ClientOnMatchStateChanged;
        countdownSeconds.OnChange += ClientOnCountdownChanged;
        leadPlayer.OnChange += ClientOnLeadPlayerChanged;

        // Seed UI that is already listening (e.g. when joining mid-match).
        NetworkUIEvents.OnMatchStateChanged?.Invoke(matchState.Value);
        NetworkUIEvents.OnCountdownChanged?.Invoke(countdownSeconds.Value);
        NetworkUIEvents.OnLeadPlayerChanged?.Invoke(leadPlayer.Value);
    }

    public override void OnStopClient()
    {
        base.OnStopClient();

        matchState.OnChange -= ClientOnMatchStateChanged;
        countdownSeconds.OnChange -= ClientOnCountdownChanged;
        leadPlayer.OnChange -= ClientOnLeadPlayerChanged;
    }

    private void ClientOnMatchStateChanged(MatchState prev, MatchState next, bool asServer)
    {
        if (asServer) return;

        NetworkUIEvents.OnMatchStateChanged?.Invoke(next);

        // A view shown in response to the new state subscribes to the bus during the call
        // above, so it missed the values below when they last changed. Re-send them.
        NetworkUIEvents.OnCountdownChanged?.Invoke(countdownSeconds.Value);
        if (next == MatchState.PostgameCountdown)
            NetworkUIEvents.OnLeadPlayerChanged?.Invoke(leadPlayer.Value); // the winner
    }

    private void ClientOnCountdownChanged(int prev, int next, bool asServer)
    {
        if (asServer) return;
        NetworkUIEvents.OnCountdownChanged?.Invoke(next);
    }

    private void ClientOnLeadPlayerChanged(NetworkPlayer prev, NetworkPlayer next, bool asServer)
    {
        if (asServer) return;
        NetworkUIEvents.OnLeadPlayerChanged?.Invoke(next);
    }
}