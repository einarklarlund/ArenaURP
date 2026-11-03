using System.Collections.Generic;
using Arena.LevelGen;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Puts experience capsules near every player during a match. When a player's pawn first appears (in a match, or
/// on a new level), they get a few capsules, each in a different room next to theirs. When one of their capsules
/// breaks, another appears in a different room next to the one they're in now. Capsules have no timer. Capsules
/// take chest sites, shared with <see cref="ChestDirector"/>. Server only. See chest-director-design.md.
/// A player's upgrades push extra capsules into their <see cref="PlayerRecord"/>; the player resets it when its
/// upgrades clear.
/// </summary>
public sealed class ExperienceCapsuleDirector : NetworkBehaviour
{
    private sealed class Capsule
    {
        public ExperienceCapsule Instance;
        public int Room;
    }

    private sealed class PlayerCapsules
    {
        public readonly List<Capsule> Live = new();
        /// <summary>Rooms of capsules that broke and aren't replaced yet; a replacement avoids them if it can.</summary>
        public readonly List<int> Broken = new();
        /// <summary>Capsules still owed: the player's count less their live capsules, recounted every tick.</summary>
        public int Owed;
        /// <summary>When an owed capsule may try again after finding no site, on the network clock.</summary>
        public double NextTryTime;
    }

    /// <summary>What a player's upgrades add to the director's base count.</summary>
    public sealed class PlayerRecord
    {
        public int ExtraCapsules;
    }

    /// <summary>The server's director, for upgrades that change a player's capsule count. Null on clients.</summary>
    public static ExperienceCapsuleDirector Instance { get; private set; }

    [SerializeField] private ExperienceCapsule experienceCapsule;
    [Tooltip("Capsules each player has before upgrades, each in a different room next to theirs. A broken one is replaced at once; capsules have no timer.")]
    [SerializeField] private int capsulesPerPlayer = 2;
    [Tooltip("Seconds an owed capsule with no free site waits before it tries again, picked at random in [x, y].")]
    [SerializeField] private Vector2 retryDelay = new(1f, 4f);

    // ── Server-only state ────────────────────────────────────────────────

    private readonly Dictionary<NetworkPlayer, PlayerCapsules> players = new();
    private readonly Dictionary<NetworkPlayer, PlayerRecord> records = new();
    private readonly List<ExperienceCapsule> capsules = new();
    private readonly List<NetworkPlayer> departed = new();
    private readonly List<int> avoid = new();
    private readonly System.Random random = new();
    private float lift;
    private bool supplying;

    private double Now => TimeManager.TicksToTime();

    // ═════════════════════════════════════════════════════════════════════
    //  SERVER: start-up and shutdown
    // ═════════════════════════════════════════════════════════════════════

    public override void OnStartServer()
    {
        base.OnStartServer();

        Instance = this;
        lift = Lift(experienceCapsule);

        if (LevelSync.Instance != null)
            LevelSync.Instance.OnServerLevelIssued += ServerEndSupply;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();

        if (LevelSync.Instance != null)
            LevelSync.Instance.OnServerLevelIssued -= ServerEndSupply;

        players.Clear();
        records.Clear();
        capsules.Clear();
        supplying = false;
        if (Instance == this)
            Instance = null;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  SERVER: API
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The record <paramref name="player"/>'s upgrades write to. Made on first use, so a pick before the player's first
    /// capsule still counts. A raise spawns the new capsules at once; a cut lets the surplus stand until broken. Lost when
    /// the player leaves or the match ends; a new level keeps it.
    /// </summary>
    [Server]
    public PlayerRecord RecordFor(NetworkPlayer player)
    {
        if (!records.TryGetValue(player, out PlayerRecord record))
        {
            record = new PlayerRecord();
            records.Add(player, record);
        }
        return record;
    }

    /// <summary>The player's upgrades cleared: back to the base count. Surplus capsules stand until broken.</summary>
    [Server]
    public void ServerResetRecord(NetworkPlayer player) => records.Remove(player);

    // ═════════════════════════════════════════════════════════════════════
    //  SERVER: supply
    // ═════════════════════════════════════════════════════════════════════

    private void Update()
    {
        if (!IsServerInitialized) return;

        MatchManager match = MatchManager.Instance;
        if (match == null || match.State != MatchState.During)
        {
            if (supplying)
            {
                ServerEndSupply();
                records.Clear();
            }
            return;
        }

        supplying = true;
        ServerDropDeparted(match);

        foreach (NetworkPlayer player in match.ActivePlayers)
        {
            if (player != null)
                ServerTickPlayer(player);
        }
    }

    /// <summary>A player who left keeps no state; their capsules stand until broken or the match ends.</summary>
    [Server]
    private void ServerDropDeparted(MatchManager match)
    {
        departed.Clear();
        foreach (NetworkPlayer player in players.Keys)
        {
            if (player == null || !match.ActivePlayers.Contains(player))
                departed.Add(player);
        }
        foreach (NetworkPlayer player in departed)
        {
            players.Remove(player);
            records.Remove(player);
        }
    }

    [Server]
    private void ServerTickPlayer(NetworkPlayer player)
    {
        // Nothing happens for a dead player: their first wave and replacements wait for their pawn.
        Pawn pawn = player.ControlledPawn.Value;
        if (pawn == null || !pawn.IsSpawned) return;

        if (!players.TryGetValue(player, out PlayerCapsules state))
        {
            // First pawn this match, or on this level.
            state = new PlayerCapsules();
            players.Add(player, state);
        }

        for (int i = state.Live.Count - 1; i >= 0; i--)
        {
            Capsule capsule = state.Live[i];
            if (capsule.Instance != null && capsule.Instance.IsSpawned) continue;

            // Broken.
            state.Live.RemoveAt(i);
            state.Broken.Add(capsule.Room);
            state.NextTryTime = 0;
        }

        // Upgrades change the count at any time. Over it, nothing is owed and the surplus stands until broken.
        state.Owed = Mathf.Max(0, CountFor(player) - state.Live.Count);

        if (state.Owed > 0 && Now >= state.NextTryTime)
            ServerSpawnOwed(pawn, state);
    }

    [Server]
    private void ServerSpawnOwed(Pawn pawn, PlayerCapsules state)
    {
        RoomGraph rooms = LevelSync.Instance != null ? LevelSync.Instance.Rooms : null;
        int playerRoom = rooms != null ? rooms.RoomAt(pawn.transform.position) : -1;

        while (state.Owed > 0)
        {
            // Never two of a player's capsules in one room. An adjacent room comes first, then one that isn't where a
            // capsule just broke: with too few neighbours, the broken capsule's room is used again before a room two
            // steps out.
            avoid.Clear();
            foreach (Capsule live in state.Live)
                avoid.Add(live.Room);
            int liveRooms = avoid.Count;
            avoid.AddRange(state.Broken);

            if (!SitePicker.TryPick(rooms, playerRoom, avoid, SiteClaims.IsFree, random, out ChestSite site, twoStepsOut: false))
            {
                avoid.RemoveRange(liveRooms, avoid.Count - liveRooms);
                if (!SitePicker.TryPick(rooms, playerRoom, avoid, SiteClaims.IsFree, random, out site))
                {
                    state.NextTryTime = Now + Random.Range(retryDelay.x, retryDelay.y);
                    return;
                }
            }

            ExperienceCapsule capsule = Instantiate(experienceCapsule, site.Position + Vector3.up * lift,
                Quaternion.Euler(0f, site.YawDegrees, 0f));
            Spawn(capsule.gameObject);
            SiteClaims.Claim(site, capsule.NetworkObject);

            state.Live.Add(new Capsule { Instance = capsule, Room = site.Room });
            state.Owed--;
            capsules.Add(capsule);
        }

        state.Broken.Clear();
    }

    /// <summary>The match is over, or a new level replaced this one: every capsule goes, and each player's first wave comes again.</summary>
    [Server]
    private void ServerEndSupply()
    {
        foreach (ExperienceCapsule capsule in capsules)
        {
            if (capsule != null && capsule.IsSpawned)
                capsule.Despawn();
        }

        capsules.Clear();
        players.Clear();
        supplying = false;
    }

    private int CountFor(NetworkPlayer player)
    {
        records.TryGetValue(player, out PlayerRecord record);
        return capsulesPerPlayer + (record?.ExtraCapsules ?? 0);
    }

    /// <summary>How far above the ground the capsule's origin goes so its bottom rests on it.</summary>
    private static float Lift(ExperienceCapsule prefab)
    {
        CapsuleCollider body = prefab.GetComponentInChildren<CapsuleCollider>(true);
        if (body == null) return 1f;
        return (body.height * 0.5f - body.center.y) * body.transform.lossyScale.y;
    }
}
