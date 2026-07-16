#if UNITY_EDITOR
using System.Linq;
using System.Reflection;
using UnityEditor;
using TMPro;
using UnityEngine;

/// <summary>
/// Editor-only typed seam for the match-layer PlayMode integration tests.
///
/// The <c>Arena.PlayTests</c> asmdef cannot reference Assembly-CSharp, where the match
/// managers (<see cref="MatchFlowManager"/>, <see cref="DeathmatchManager"/>,
/// <see cref="PawnManager"/>, <see cref="NetworkPlayerManager"/>), <see cref="Pawn"/>,
/// <see cref="NetworkPlayer"/>, and <see cref="DamageInfo"/> live. Tests reach this class
/// by reflection (<c>Type.GetType("MatchTestRig, Assembly-CSharp")</c>) and call its static
/// methods, exactly like <c>ProjectileSpawnTestRig</c> / <c>ProjectileDamageTestRig</c>.
///
/// All methods assume the single-host fixture: the managers exist once in the loaded test
/// scene (instantiated from Assets/Prefabs/Managers/MatchMediator.prefab), so we resolve
/// them with <c>FindAnyObjectByType</c>. Keep this thin — it is glue, not logic.
/// </summary>
public static class MatchTestRig
{
    // ---- manager resolution -------------------------------------------------

    public static MatchFlowManager Flow() => Object.FindAnyObjectByType<MatchFlowManager>();
    public static DeathmatchManager Deathmatch() => Object.FindAnyObjectByType<DeathmatchManager>();
    public static PawnManager Pawns() => Object.FindAnyObjectByType<PawnManager>();
    public static NetworkPlayerManager Players() => Object.FindAnyObjectByType<NetworkPlayerManager>();
    public static BotSpawner Spawner() => Object.FindAnyObjectByType<BotSpawner>();

    /// <summary>True once the MatchMediator prefab's four managers are all present.</summary>
    public static bool ManagersPresent()
        => Flow() != null && Deathmatch() != null && Pawns() != null && Players() != null;

    // ---- match state --------------------------------------------------------

    public static string MatchState() => Flow().State.Value.ToString();

    /// <summary>Drive the match into a named MatchState (e.g. "During"). Goes through the
    /// SyncVar so the MatchMediator's State.OnChange wiring reacts exactly as in-game.</summary>
    public static void SetMatchState(string state)
    {
        var value = (MatchState)System.Enum.Parse(typeof(MatchState), state);
        Flow().State.Value = value;
    }

    public static void StartMatch() => Flow().StartDebugMatch();
    public static void EnterPregameCountdown() => Flow().EnterPregameCountdown();
    public static int CountdownTime() => Flow().CountdownTime.Value;

    public static void BeginGame() => Deathmatch().BeginGame();

    /// <summary>Set the score-to-win threshold on DeathmatchManager's private timerConfig
    /// so a win can be reached in a handful of scripted kills.</summary>
    public static void SetKillsToWin(float kills) => MutateTimerConfig(cfg =>
    {
        cfg.KillsToWin = kills;
        return cfg;
    });

    /// <summary>Set the match duration on the private timerConfig (seconds).</summary>
    public static void SetMatchDuration(float seconds) => MutateTimerConfig(cfg =>
    {
        cfg.MatchDurationSeconds = seconds;
        return cfg;
    });

    public static void SetRespawnDelay(float seconds) => Deathmatch().RespawnDelay = seconds;

    static void MutateTimerConfig(System.Func<MatchTimerConfig, MatchTimerConfig> edit)
    {
        var dm = Deathmatch();
        var field = typeof(DeathmatchManager).GetField(
            "timerConfig", BindingFlags.NonPublic | BindingFlags.Instance);
        var cfg = (MatchTimerConfig)field.GetValue(dm);
        field.SetValue(dm, edit(cfg));
    }

    // ---- players ------------------------------------------------------------

    /// <summary>Spawn + register one bot NetworkPlayer through the real BotSpawner.SpawnBot
    /// path (which synchronously fires OnSpawned -> NetworkPlayerManager.ServerRegisterPlayer,
    /// adding it to ActivePlayers). Returns the freshly registered player.</summary>
    public static NetworkPlayer SpawnBotPlayer()
    {
        var players = Players();
        var spawner = Spawner();
        if (players == null) throw new System.Exception("MatchTestRig: NetworkPlayerManager not found");
        if (spawner == null) throw new System.Exception("MatchTestRig: BotSpawner not found");

        int before = players.ActivePlayers.Count;
        spawner.SpawnBot();
        int after = players.ActivePlayers.Count;

        if (after <= before)
        {
            int subs = OnSpawnedSubscriberCount(spawner);
            bool botPrefabSet = typeof(BotSpawner)
                .GetField("botPlayerPrefab", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(spawner) != null;
            var conn = FishNet.InstanceFinder.ClientManager != null
                ? FishNet.InstanceFinder.ClientManager.Connection : null;
            throw new System.Exception(
                $"MatchTestRig.SpawnBotPlayer: SpawnBot did not grow ActivePlayers. " +
                $"OnSpawnedSubscribers={subs}, before={before}, after={after}, " +
                $"botPlayerPrefabSet={botPrefabSet}, clientConn={(conn != null ? conn.ClientId.ToString() : "null")}, " +
                $"serverStarted={FishNet.InstanceFinder.IsServerStarted}");
        }

        // ServerRegisterPlayer ran synchronously inside SpawnBot; the new player is last.
        return players.ActivePlayers[after - 1];
    }

    static int OnSpawnedSubscriberCount(BotSpawner spawner)
    {
        var field = typeof(BotSpawner).GetField("OnSpawned", BindingFlags.NonPublic | BindingFlags.Instance);
        var del = field?.GetValue(spawner) as System.Delegate;
        return del?.GetInvocationList().Length ?? 0;
    }

    const string TestPlayerPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestPlayer.prefab";

    /// <summary>
    /// Robustly spawn + register one NetworkPlayer, bypassing the BotSpawner.OnSpawned ->
    /// NetworkPlayerManager auto-subscription (which races with scene-object setup in the test
    /// fixture). Spawns a TestPlayer NetworkObject server-side, then calls the manager's own
    /// ServerRegisterPlayer so the player lands in ActivePlayers and OnPlayerRegistered fires —
    /// exactly the state ServerRecordKill / ServerHandleGameModeEnded operate on. Use this for
    /// match-logic tests; use SpawnBotPlayer when the BotSpawner path itself is under test.
    /// </summary>
    public static NetworkPlayer RegisterPlayer(bool isBot)
    {
        var nm = FishNet.InstanceFinder.NetworkManager;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestPlayerPrefabPath);
        if (prefab == null) throw new System.Exception($"MatchTestRig: missing {TestPlayerPrefabPath}");

        var go = Object.Instantiate(prefab);
        var np = go.GetComponent<NetworkPlayer>();
        np.IsBot = isBot;
        nm.ServerManager.Spawn(go, nm.ClientManager.Connection);

        typeof(NetworkPlayerManager)
            .GetMethod("ServerRegisterPlayer", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(Players(), new object[] { np });
        return np;
    }

    /// <summary>Swap PawnManager.botPawnPrefab at runtime (e.g. to the real Bot Pawn for the soak).
    /// Call before spawning bots.</summary>
    public static void UseBotPawnPrefab(string assetPath)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (go == null) throw new System.Exception($"MatchTestRig: missing {assetPath}");
        var pawn = go.GetComponent<Pawn>();
        if (pawn == null) throw new System.Exception($"MatchTestRig: {assetPath} has no Pawn component");
        typeof(PawnManager).GetField("botPawnPrefab", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(Pawns(), pawn);
    }

    public static int ActivePlayerCount() => Players().ActivePlayers.Count;
    public static int ActivePawnCount() => Pawns().ActivePawns.Count;

    /// <summary>Server-despawn a player's NetworkObject — the same OnStopServer -> OnDespawn cascade a
    /// real disconnect triggers. Lets a single-host test exercise the leave/cleanup path.</summary>
    public static void DespawnPlayer(NetworkPlayer player)
        => FishNet.InstanceFinder.ServerManager.Despawn(player.NetworkObject);

    public static bool PlayerIsRegistered(NetworkPlayer player)
        => Players().ActivePlayers.Contains(player);

    // ---- GameObject-addressed helpers (usable from the two-peer fixture, which has no managers
    //      and two NetworkManagers — so these never call FindAnyObjectByType) ----

    public static int HealthOfGo(GameObject pawnGo) => pawnGo.GetComponent<Pawn>().Health.Value;

    public static void ServerDamageGo(GameObject pawnGo, int amount)
        => pawnGo.GetComponent<Pawn>().TakeDamage(new DamageInfo
        {
            Amount = amount,
            Attacker = null,
            Type = DamageType.Bullet,
            HitPoint = pawnGo.transform.position,
            Direction = Vector3.forward,
        });

    /// <summary>Sum of all living pawns' distance from the world origin — a cheap proxy a soak test
    /// samples over time to prove the bots are actually being driven (BotBrain + NavMeshAgent moving
    /// real CharacterControllers), not standing inert.</summary>
    public static float TotalPawnTravelProxy()
    {
        float sum = 0f;
        foreach (var p in Pawns().ActivePawns)
            if (p != null) sum += p.transform.position.magnitude;
        return sum;
    }

    public static int Score(NetworkPlayer player) => player.Score.Value;
    public static bool IsBot(NetworkPlayer player) => player.IsBot;
    public static double RespawnTimeEnd(NetworkPlayer player) => player.RespawnTimeEnd.Value;
    public static Pawn ControlledPawn(NetworkPlayer player) => player.ControlledPawn.Value;
    public static bool IsReady(NetworkPlayer player) => player.IsReady.Value;
    public static void SetReady(NetworkPlayer player, bool ready) => player.IsReady.Value = ready;

    // ---- kills / damage -----------------------------------------------------

    public static void RecordKill(NetworkPlayer killer, NetworkPlayer killed)
        => Deathmatch().ServerRecordKill(killer, killed);

    public static int Health(Pawn pawn) => pawn.Health.Value;

    /// <summary>Deal exactly-lethal server damage to a pawn, attributed to <paramref name="attacker"/>.
    /// Drives the real Pawn.TakeDamage -> OnDeath -> PawnManager.OnPawnKilled -> MatchMediator chain.</summary>
    public static void KillPawn(Pawn pawn, NetworkPlayer attacker)
        => DamagePawn(pawn, pawn.Health.Value, attacker);

    public static void DamagePawn(Pawn pawn, int amount, NetworkPlayer attacker)
    {
        pawn.TakeDamage(new DamageInfo
        {
            Amount = amount,
            Attacker = attacker,
            Type = DamageType.Bullet,
            HitPoint = pawn.transform.position,
            Direction = Vector3.forward,
        });
    }

    // ---- event capture ------------------------------------------------------
    // Soak/lifecycle tests assert on events the managers fire. We subscribe once and latch
    // the results so a test can poll them after driving the simulation.

    static bool _wired;
    static int _gameModeEndedCount;
    static NetworkPlayer _lastWinner;
    static int _pawnKilledCount;

    /// <summary>(Re)subscribe to match events and clear the latched counters. Call at the
    /// start of a test, after the managers exist.</summary>
    public static void BeginCapture()
    {
        var dm = Deathmatch();
        var pm = Pawns();
        if (!_wired)
        {
            dm.OnGameModeEnded += OnGameModeEnded;
            pm.OnPawnKilled += OnPawnKilled;
            _wired = true;
        }
        _gameModeEndedCount = 0;
        _lastWinner = null;
        _pawnKilledCount = 0;
    }

    /// <summary>Unsubscribe. Call in teardown so latched handlers don't leak across tests
    /// (the managers are re-created each test, but the static delegate would dangle).</summary>
    public static void EndCapture()
    {
        var dm = Object.FindAnyObjectByType<DeathmatchManager>();
        var pm = Object.FindAnyObjectByType<PawnManager>();
        if (dm != null) dm.OnGameModeEnded -= OnGameModeEnded;
        if (pm != null) pm.OnPawnKilled -= OnPawnKilled;
        _wired = false;
    }

    static void OnGameModeEnded(NetworkPlayer winner)
    {
        _gameModeEndedCount++;
        _lastWinner = winner;
    }

    static void OnPawnKilled(Pawn pawn, DamageInfo info) => _pawnKilledCount++;

    public static int GameModeEndedCount() => _gameModeEndedCount;
    public static NetworkPlayer LastWinner() => _lastWinner;
    public static int PawnKilledCount() => _pawnKilledCount;

    // ---- soak invariants ----------------------------------------------------

    /// <summary>True if any living pawn has a negative Health (an invariant a soak test polls).</summary>
    public static bool AnyPawnHealthNegative()
        => Pawns().ActivePawns.Any(p => p != null && p.Health.Value < 0);

    // ---- UI bus decoupling (W3) ---------------------------------------------
    // Helpers for the PlayMode test proving the countdown/postgame views update from the
    // NetworkUIEvents bus rather than reading a manager singleton. The view type lives in
    // Assembly-CSharp (unreferenceable from the test asmdef), so tests build and read it here.

    public static void SetCountdownTime(int seconds) => Flow().CountdownTime.Value = seconds;

    public static void SetLeadPlayer(NetworkPlayer player)
    {
        // Install `player` as the stored leader through the REAL server path. ServerRecordKill
        // sets leadPlayer once the killer's score passes the high score (0 before BeginGame), so
        // one recorded kill makes leadPlayer.Value == player. This is how the game actually sets
        // the leader, and it mirrors the passing countdown test's direct SyncVar seed (the client
        // OnChange fires synchronously on the host, to no subscriber yet). It replaces reflecting
        // into the FishNet-woven private SyncVar field, which returned a null FieldInfo and NRE'd.
        Deathmatch().ServerRecordKill(player, player);
    }

    public static void SetUsername(NetworkPlayer player, string name) => player.ServerSetUsername(name);
    public static string Username(NetworkPlayer player) => player.Username.Value;

    public static void FireCountdownEvent(int seconds) => NetworkUIEvents.OnCountdownChanged?.Invoke(seconds);
    public static void FireLeadPlayerEvent(NetworkPlayer player) => NetworkUIEvents.OnLeadPlayerChanged?.Invoke(player);
    public static void ResetNetworkUIEvents() => NetworkUIEvents.Reset();

    /// <summary>Build a PostgameCountdownView with wired TMP text targets. The GameObject is
    /// returned INACTIVE so the caller controls when OnEnable (bus subscription) runs, via
    /// SetViewActive — this lets a test enable the view and then prove the bus, not the
    /// enable, is what fills the text.</summary>
    public static GameObject CreatePostgameCountdownView()
    {
        var root = new GameObject("TestPostgameCountdownView");
        root.SetActive(false);

        var winnerGo = new GameObject("Winner");
        winnerGo.transform.SetParent(root.transform);
        var winner = winnerGo.AddComponent<TextMeshProUGUI>();

        var countdownGo = new GameObject("Countdown");
        countdownGo.transform.SetParent(root.transform);
        var countdown = countdownGo.AddComponent<TextMeshProUGUI>();

        var view = root.AddComponent<PostgameCountdownView>();
        SetViewField(view, "winnerUsernameText", winner);
        SetViewField(view, "countdownText", countdown);
        return root;
    }

    public static void SetViewActive(GameObject viewRoot, bool active) => viewRoot.SetActive(active);
    public static string ViewCountdownText(GameObject viewRoot) => ReadTmpText(viewRoot, "countdownText");
    public static string ViewWinnerText(GameObject viewRoot) => ReadTmpText(viewRoot, "winnerUsernameText");
    public static void DestroyGameObject(GameObject go) => Object.Destroy(go);

    static void SetViewField(Component view, string field, object value)
        => view.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(view, value);

    static string ReadTmpText(GameObject viewRoot, string field)
    {
        var view = viewRoot.GetComponent<PostgameCountdownView>();
        var tmp = (TMP_Text)view.GetType()
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
        return tmp.text;
    }
}
#endif
