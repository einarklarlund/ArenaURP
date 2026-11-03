using System;
using System.Collections;
using System.Collections.Generic;
using Arena.LevelGen;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Gives every peer the same generated level without sending it. The server generates from a seed and config, builds,
/// and publishes a <see cref="LevelTicket"/>; each client generates from the ticket itself and checks the result's
/// hash. On a match it builds and reports ready; on a mismatch it asks for the description, which the server sends in
/// chunks of at most <see cref="ChunkSize"/> bytes. A host builds once, on its server side. Players spawn only once their
/// client has reported ready for the current level (<see cref="IsReady"/>).
/// Generation runs a stage per frame on the main thread (WebGL has no other), so the loading screen can show
/// which stage is running: <see cref="OnLoadProgress"/>, and <see cref="LocalUIEvents.OnLevelLoadStarted"/> /
/// <see cref="LocalUIEvents.OnLevelLoadFinished"/> round each load on this peer.
/// </summary>
public sealed class LevelSync : NetworkBehaviour
{
    public static LevelSync Instance { get; private set; }

    public const int ChunkSize = 16 * 1024;

    [Tooltip("Generate levels at all. Off in scenes with a hand-built arena.")]
    [SerializeField] private bool generateLevels = true;
    [SerializeField] private LevelArtRegistry registry;
    [Tooltip("When a seed gives no valid level, the server tries the next one, this many times.")]
    [SerializeField] private int seedsToTry = 8;
    [SerializeField] private bool buildDecals = true;

    [Header("Debug")]
    [Tooltip("The host's client generates and checks the level as a remote client would, instead of using the server's.")]
    [SerializeField] private bool verifyOnHost;
    [Tooltip("The client treats every hash as a mismatch and takes the description from the server.")]
    [SerializeField] private bool forceDescriptionFallback;

    /// <summary>The ticket, as <see cref="LevelTicket.ToBytes"/>. Written only by the server.</summary>
    private readonly SyncVar<byte[]> ticket = new();

    /// <summary>The level this peer built, or null.</summary>
    public LevelDescription Level { get; private set; }
    public LevelTicket Ticket { get; private set; }
    /// <summary>Raised on every peer when a level is built: the description, and the ticket it was built for.</summary>
    public event Action<LevelDescription, LevelTicket> OnLevelBuilt;
    /// <summary>Server only: a new level was issued, so every client is building it and none is ready yet.</summary>
    public event Action OnServerLevelIssued;

    /// <summary>Server only: no valid level came from the seeds tried; the current level stays. Says why.</summary>
    public event Action<string> OnServerIssueFailed;
    /// <summary>Raised on this peer as a load moves on: the stage about to run, or the fallback's chunks.</summary>
    public event Action<LevelLoadStatus> OnLoadProgress;

    /// <summary>The built level's rooms: which room a position is in, its neighbours, its chest sites and pickup lines.
    /// Null until a level is built.</summary>
    public RoomGraph Rooms { get; private set; }
    /// <summary>This peer is generating, downloading or building a level.</summary>
    public bool Loading => _serverLoading || _clientLoading;
    public LevelLoadStatus LoadStatus { get; private set; }

    public bool GeneratesLevels => generateLevels;
    /// <summary>Server: levels are generated here but none is built yet, so there is nowhere to spawn.</summary>
    public bool LevelPending => generateLevels && Level == null;

    private GameObject _root;
    private bool _serverLoading;
    private bool _clientLoading;
    private Coroutine _serverRoutine;
    private Coroutine _clientRoutine;

    // Server only.
    private int _match;
    private LevelGenSettings _settings;
    private int _preset;
    private int _theme;
    private byte[] _serialized;
    private readonly HashSet<NetworkConnection> _ready = new();

    // Client only: the fallback's chunks as they arrive.
    private byte[][] _chunks;
    private int _chunksMatch = -1;

    private void Awake()
    {
        if (Instance != null && Instance != this) Debug.LogWarning("There is a duplicate LevelSync in the scene.", this);
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SetLoading(false, false);
        Teardown();
    }

    // ═════════════════════════════════════════════════════════════════════
    //  SERVER
    // ═════════════════════════════════════════════════════════════════════

    public override void OnStartServer()
    {
        base.OnStartServer();
        if (!generateLevels) return;

        var options = HostOptionsManager.Instance != null ? HostOptionsManager.Instance.Options : new HostOptionsData();
        _preset = options.LevelPreset;
        _theme = options.LevelTheme;
        ulong seed = options.RandomLevelSeed ? RandomSeed() : options.LevelSeed;
        _settings = PresetSettings(_preset);
        if (!string.IsNullOrWhiteSpace(options.LevelShareCode))
        {
            if (ShareCode.TryDecode(options.LevelShareCode, out ulong codeSeed, out var codeSettings, out string error))
            {
                _settings = codeSettings;
                seed = codeSeed;
            }
            else Debug.LogWarning($"[LevelSync] {error} Using the preset.");
        }
        ServerIssue(seed);
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        _ready.Clear();
        if (_serverRoutine != null) StopCoroutine(_serverRoutine);
        _serverRoutine = null;
        SetLoading(false, _clientLoading);
        Teardown();
    }

    /// <summary>A fresh random seed with the same config and art: every match after the first gets one. True when
    /// generation started; false while another is still running.</summary>
    [Server]
    public bool ServerNextLevel()
    {
        // In a scene that doesn't generate on start, the first call picks up the preset.
        if (_match == 0 && !generateLevels) _settings = PresetSettings(_preset);
        return ServerIssue(RandomSeed());
    }

    /// <summary>The level a share code describes: its seed and config, with the current art. False, with why, for a
    /// code that isn't one or while another level is generating. A code whose level fails to generate is reported
    /// later, through <see cref="OnServerIssueFailed"/>, and the current level and config stay.</summary>
    [Server]
    public bool ServerLoad(string code, out string error)
    {
        if (!ShareCode.TryDecode(code, out ulong seed, out var settings, out error)) return false;
        if (ServerStartIssue(seed, settings)) return true;
        error = "A level is already generating.";
        return false;
    }

    /// <summary>Starts generating from <paramref name="seed"/> (or the next seeds, if it gives no valid level); once one
    /// is found it is built and its ticket published, and readiness starts over. False while another is running.</summary>
    [Server]
    public bool ServerIssue(ulong seed) => ServerStartIssue(seed, _settings);

    [Server]
    private bool ServerStartIssue(ulong seed, LevelGenSettings settings)
    {
        if (_serverRoutine != null) return false;
        _serverRoutine = StartCoroutine(ServerGenerate(seed, settings));
        return true;
    }

    private IEnumerator ServerGenerate(ulong seed, LevelGenSettings settings)
    {
        SetLoading(true, _clientLoading);
        GenerationResult result = null;
        ulong used = seed;
        for (int i = 0; i < Mathf.Max(1, seedsToTry); i++)
        {
            used = seed + (ulong)i;
            var run = LevelGenerator.Start(settings, used);
            while (run.MoveNext())
            {
                Report(LevelLoadStatus.Generating(run.Current));
                yield return null;
            }
            result = run.Result;
            if (result.Success) break;
            Debug.LogWarning($"[LevelSync] Seed {used} gave no valid level: {result.Reason}");
        }
        if (result == null || !result.Success)
        {
            string reason = $"No valid level from seeds {seed} to {used}. Keeping the current level.";
            Debug.LogError($"[LevelSync] {reason}");
            _serverRoutine = null;
            SetLoading(false, _clientLoading);
            OnServerIssueFailed?.Invoke(reason);
            yield break;
        }
        if (used != seed) Debug.Log($"[LevelSync] Seed {seed} gave no valid level; using seed {used}.");

        Report(LevelLoadStatus.Building);
        yield return null;
        _settings = settings;
        _match++;
        _ready.Clear();
        _serialized = result.Level.Serialize();
        var issued = new LevelTicket
        {
            GeneratorVersion = LevelGenerator.GeneratorVersion, Seed = used, Config = _settings.ToBytes(),
            Preset = _preset, Theme = _theme, Hash = StableHash.Fnv1a64(_serialized), Match = _match,
        };
        Build(result.Level, issued);
        ticket.Value = issued.ToBytes();
        _serverRoutine = null;
        SetLoading(false, _clientLoading);
        OnServerLevelIssued?.Invoke();
        Debug.Log($"[LevelSync] Level {_match}: seed {used}, hash {issued.Hash:X16}, " +
            $"share code {ShareCode.Encode(issued.GeneratorVersion, used, _settings)}");
    }

    /// <summary>Whether a client has built the current level. Always true before any level is issued.</summary>
    public bool IsReady(NetworkConnection connection) => _match == 0 || connection == null || _ready.Contains(connection);

    [ServerRpc(RequireOwnership = false)]
    private void ServerReportReady(int match, NetworkConnection connection = null)
    {
        if (match == _match) _ready.Add(connection);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ServerRequestDescription(int match, NetworkConnection connection = null)
    {
        if (match != _match || _serialized == null) return;
        int count = (_serialized.Length + ChunkSize - 1) / ChunkSize;
        for (int i = 0; i < count; i++)
        {
            int length = Math.Min(ChunkSize, _serialized.Length - i * ChunkSize);
            var chunk = new byte[length];
            Buffer.BlockCopy(_serialized, i * ChunkSize, chunk, 0, length);
            TargetDescriptionChunk(connection, match, i, count, chunk);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  CLIENT
    // ═════════════════════════════════════════════════════════════════════

    public override void OnStartClient()
    {
        base.OnStartClient();
        ticket.OnChange += ClientOnTicketChanged;
        if (ticket.Value != null) ClientReceive(ticket.Value);
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        ticket.OnChange -= ClientOnTicketChanged;
        if (_clientRoutine != null) StopCoroutine(_clientRoutine);
        _clientRoutine = null;
        _chunksMatch = -1;
        SetLoading(_serverLoading, false);
        if (!IsServerStarted) Teardown();
    }

    private void ClientOnTicketChanged(byte[] prev, byte[] next, bool asServer)
    {
        if (asServer || next == null) return;
        ClientReceive(next);
    }

    private void ClientReceive(byte[] bytes)
    {
        var received = LevelTicket.FromBytes(bytes);
        if (received.GeneratorVersion != LevelGenerator.GeneratorVersion)
        {
            Debug.LogError($"[LevelSync] The host runs level generator version {received.GeneratorVersion}; this build has " +
                $"version {LevelGenerator.GeneratorVersion}. Update the game to join.");
            ClientManager.StopConnection();
            return;
        }

        // A host's server side already built this level.
        if (IsServerStarted && !verifyOnHost)
        {
            ServerReportReady(received.Match);
            return;
        }

        if (_clientRoutine != null) StopCoroutine(_clientRoutine);
        _chunksMatch = -1;
        _clientRoutine = StartCoroutine(ClientGenerate(received));
    }

    private IEnumerator ClientGenerate(LevelTicket received)
    {
        SetLoading(_serverLoading, true);
        var run = LevelGenerator.Start(received.Settings, received.Seed);
        while (run.MoveNext())
        {
            Report(LevelLoadStatus.Generating(run.Current));
            yield return null;
        }
        var result = run.Result;
        bool matches = result.Success && result.Level.ComputeHash() == received.Hash && !forceDescriptionFallback;
        if (matches)
        {
            if (!IsServerStarted)
            {
                Report(LevelLoadStatus.Building);
                yield return null;
                Build(result.Level, received);
            }
            ServerReportReady(received.Match);
            _clientRoutine = null;
            SetLoading(_serverLoading, false);
            yield break;
        }

        Debug.LogWarning($"[LevelSync] Level {received.Match} (seed {received.Seed}) didn't match the host's on " +
            $"{Application.platform}: {(result.Success ? $"hash {result.Level.ComputeHash():X16}, host's {received.Hash:X16}" : result.Reason)}. " +
            "Asking the host for the description.");
        _chunks = null;
        _chunksMatch = received.Match;
        _clientRoutine = null;
        // Still loading: the chunks finish it.
        Report(LevelLoadStatus.Downloading(0, 0));
        ServerRequestDescription(received.Match);
    }

    [TargetRpc]
    private void TargetDescriptionChunk(NetworkConnection connection, int match, int index, int count, byte[] chunk)
    {
        if (match != _chunksMatch) return;
        _chunks ??= new byte[count][];
        _chunks[index] = chunk;
        int arrived = 0;
        foreach (var c in _chunks)
            if (c != null) arrived++;
        Report(LevelLoadStatus.Downloading(arrived, count));
        if (arrived < count) return;

        int length = 0;
        foreach (var c in _chunks) length += c.Length;
        var bytes = new byte[length];
        int offset = 0;
        foreach (var c in _chunks)
        {
            Buffer.BlockCopy(c, 0, bytes, offset, c.Length);
            offset += c.Length;
        }
        _chunks = null;

        var current = LevelTicket.FromBytes(ticket.Value);
        if (current.Match != match) return;
        if (StableHash.Fnv1a64(bytes) != current.Hash)
            Debug.LogError($"[LevelSync] The description for level {match} arrived damaged; building it anyway.");
        _chunksMatch = -1;
        if (!IsServerStarted) Build(LevelDescription.Deserialize(bytes), current);
        ServerReportReady(match);
        SetLoading(_serverLoading, false);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  BOTH
    // ═════════════════════════════════════════════════════════════════════

    private void Build(LevelDescription level, LevelTicket built)
    {
        Teardown();
        var theme = registry != null ? registry.Theme(built.Theme) : null;
        var preset = registry != null ? registry.Preset(built.Preset) : null;
        _root = LevelBuilder.Build(level, null, "Generated Level", theme, preset != null ? preset.props : null, buildDecals);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_root, gameObject.scene);
        Level = level;
        Ticket = built;
        Rooms = new RoomGraph(level);
        OnLevelBuilt?.Invoke(level, built);
    }

    private void Teardown()
    {
        if (_root != null) LevelBuilder.Teardown(_root);
        _root = null;
        Level = null;
        Rooms = null;
    }

    private void Report(LevelLoadStatus status)
    {
        LoadStatus = status;
        OnLoadProgress?.Invoke(status);
    }

    /// <summary>Sets whether the server side and the client side are loading, and tells the UI when this peer as a
    /// whole starts or stops (a host verifying on its client loads on both sides at once).</summary>
    private void SetLoading(bool server, bool client)
    {
        bool was = Loading;
        _serverLoading = server;
        _clientLoading = client;
        if (Loading == was) return;
        if (Loading) LocalUIEvents.OnLevelLoadStarted?.Invoke();
        else LocalUIEvents.OnLevelLoadFinished?.Invoke();
    }

    private LevelGenSettings PresetSettings(int index)
    {
        var preset = registry != null ? registry.Preset(index) : null;
        if (preset == null) Debug.LogWarning($"[LevelSync] No level preset {index} in the registry; using the defaults.");
        return preset != null ? preset.ToSettings() : LevelGenSettings.Default.Sanitized();
    }

    private static ulong RandomSeed()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        return BitConverter.ToUInt64(bytes, 0);
    }
}

/// <summary>Where a level load on this peer is: generating (a stage, in an attempt), downloading the host's
/// description, or building.</summary>
public readonly struct LevelLoadStatus
{
    public enum Phase { Generating, Downloading, Building }

    public readonly Phase Step;
    /// <summary>Generating: the stage about to run.</summary>
    public readonly string Stage;
    public readonly int Attempt;
    public readonly int Attempts;
    /// <summary>Downloading: chunks arrived and expected (0 of 0 until the first arrives).</summary>
    public readonly int Chunks;
    public readonly int ChunkCount;

    private LevelLoadStatus(Phase step, string stage, int attempt, int attempts, int chunks, int chunkCount)
    {
        Step = step;
        Stage = stage;
        Attempt = attempt;
        Attempts = attempts;
        Chunks = chunks;
        ChunkCount = chunkCount;
    }

    public static LevelLoadStatus Generating(GenerationProgress p) =>
        new(Phase.Generating, p.Stage, p.Attempt, p.Attempts, 0, 0);

    public static LevelLoadStatus Downloading(int chunks, int count) => new(Phase.Downloading, null, 0, 0, chunks, count);

    public static LevelLoadStatus Building => new(Phase.Building, null, 0, 0, 0, 0);
}
