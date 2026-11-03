using System.Collections.Generic;
using Arena.LevelGen;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Keeps every player stocked with supply and weapon chests during a match. Each player has a few
/// slots per chest kind; an empty slot refills after a short delay, on a free chest site drawn by
/// <see cref="SiteScorer"/>: near its player and away from the leader, or, for the leader, away from everyone.
/// Server only. See chest-director-design.md.
/// A player's upgrades push extra slots into their <see cref="PlayerRecord"/>; the player resets it when its
/// upgrades clear.
/// </summary>
public sealed class ChestDirector : NetworkBehaviour
{
    private enum ChestKind { Supply, Weapon }

    private sealed class Slot
    {
        public ChestKind Kind;
        /// <summary>The slot's current chest. Compared with <c>is null</c> to tell "never had one" from "it went away".</summary>
        public ChestTimer Chest;
        /// <summary>When an empty slot may refill, on the network clock.</summary>
        public double NextSpawnTime;
        /// <summary>A slot's first chest starts partway through its life, so a wave of them doesn't expire together.</summary>
        public bool Started;
    }

    /// <summary>What a player's upgrades add to the director's base counts.</summary>
    public sealed class PlayerRecord
    {
        public int ExtraSupplyChests;
        public int ExtraWeaponChests;
    }

    /// <summary>The server's director, for upgrades that change a player's chest counts. Null on clients.</summary>
    public static ChestDirector Instance { get; private set; }

    [Tooltip("Must have its origin at a bottom corner of the chest, with the body along local +X, +Y and +Z.")]
    [SerializeField] private ChestTimer supplyChest;
    [Tooltip("Must have its origin at a bottom corner of the chest, with the body along local +X, +Y and +Z.")]
    [SerializeField] private ChestTimer weaponChest;
    [Tooltip("Supply chests each player has, before upgrades.")]
    [SerializeField] private int supplyChestsPerPlayer = 2;
    [Tooltip("Weapon chests each player has, before upgrades.")]
    [SerializeField] private int weaponChestsPerPlayer = 2;
    [Tooltip("Seconds a chest lasts before the server despawns it.")]
    [SerializeField] private float lifetime = 20f;
    [Tooltip("Seconds an empty slot waits before it refills, picked at random in [x, y]. A slot with no free site also waits this long.")]
    [SerializeField] private Vector2 respawnDelay = new(1f, 4f);
    [SerializeField] private SiteWeights siteWeights = new();

    // ── Server-only state ────────────────────────────────────────────────

    private readonly Dictionary<NetworkPlayer, List<Slot>> slots = new();
    private readonly Dictionary<NetworkPlayer, PlayerRecord> records = new();
    // Every chest this spawned, including those whose player has left: they all go when the match ends.
    private readonly List<ChestTimer> chests = new();
    private readonly List<NetworkPlayer> departed = new();
    private readonly List<NetworkPlayer> standings = new();
    private readonly System.Random random = new();
    private Bounds supplyFootprint;
    private Bounds weaponFootprint;
    private bool supplying;

    private double Now => TimeManager.TicksToTime();

    // ═════════════════════════════════════════════════════════════════════
    //  SERVER: start-up and shutdown
    // ═════════════════════════════════════════════════════════════════════

    public override void OnStartServer()
    {
        base.OnStartServer();

        Instance = this;
        SiteClaims.Clear();
        supplyFootprint = SitePicker.Footprint(supplyChest);
        weaponFootprint = SitePicker.Footprint(weaponChest);

        if (LevelSync.Instance != null)
            LevelSync.Instance.OnServerLevelIssued += ServerEndSupply;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();

        if (LevelSync.Instance != null)
            LevelSync.Instance.OnServerLevelIssued -= ServerEndSupply;

        // FishNet despawns the chests itself as the server stops.
        slots.Clear();
        records.Clear();
        chests.Clear();
        SiteClaims.Clear();
        supplying = false;
        if (Instance == this)
            Instance = null;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  SERVER: API
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The record <paramref name="player"/>'s upgrades write to. Made on first use, so a pick before the player's first
    /// chest still counts. Lost when the player leaves or the match ends; a new level keeps it.
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

    /// <summary>The player's upgrades cleared. Slots over the base count go; their chests stay until opened or expired.</summary>
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
        ServerSyncPlayers(match);

        foreach (KeyValuePair<NetworkPlayer, List<Slot>> entry in slots)
        {
            // Upgrades change the counts at any time; this is a no-op when they haven't.
            ServerResizeSlots(entry.Key, entry.Value);
            foreach (Slot slot in entry.Value)
                ServerTickSlot(entry.Key, slot);
        }
    }

    /// <summary>Gives each new player their slots, and drops the slots of players who left. Their chests stay.</summary>
    [Server]
    private void ServerSyncPlayers(MatchManager match)
    {
        foreach (NetworkPlayer player in match.ActivePlayers)
        {
            if (player == null || slots.ContainsKey(player)) continue;

            List<Slot> playerSlots = new();
            ServerResizeSlots(player, playerSlots);
            slots.Add(player, playerSlots);
        }

        departed.Clear();
        foreach (NetworkPlayer player in slots.Keys)
        {
            if (player == null || !match.ActivePlayers.Contains(player))
                departed.Add(player);
        }
        foreach (NetworkPlayer player in departed)
        {
            slots.Remove(player);
            records.Remove(player);
        }
    }

    /// <summary>Adds or removes slots until the player has their count of each kind. A removed slot's chest stays.</summary>
    [Server]
    private void ServerResizeSlots(NetworkPlayer player, List<Slot> playerSlots)
    {
        records.TryGetValue(player, out PlayerRecord record);
        ServerResizeSlots(playerSlots, ChestKind.Supply, supplyChestsPerPlayer + (record?.ExtraSupplyChests ?? 0));
        ServerResizeSlots(playerSlots, ChestKind.Weapon, weaponChestsPerPlayer + (record?.ExtraWeaponChests ?? 0));
    }

    private void ServerResizeSlots(List<Slot> playerSlots, ChestKind kind, int count)
    {
        int have = playerSlots.FindAll(slot => slot.Kind == kind).Count;
        for (; have < count; have++)
            playerSlots.Add(new Slot { Kind = kind, NextSpawnTime = Now });
        for (; have > count; have--)
            playerSlots.RemoveAt(playerSlots.FindLastIndex(slot => slot.Kind == kind));
    }

    [Server]
    private void ServerTickSlot(NetworkPlayer player, Slot slot)
    {
        if (slot.Chest is not null)
        {
            if (slot.Chest != null && slot.Chest.IsSpawned)
                return;

            // Opened or expired.
            slot.Chest = null;
            slot.NextSpawnTime = Now + RespawnDelay();
        }

        if (Now < slot.NextSpawnTime) return;

        // A dead player's empty slots wait for them to respawn.
        Pawn pawn = player.ControlledPawn.Value;
        if (pawn == null || !pawn.IsSpawned) return;

        if (!ServerTrySpawn(player, pawn, slot))
            slot.NextSpawnTime = Now + RespawnDelay();
    }

    [Server]
    private bool ServerTrySpawn(NetworkPlayer player, Pawn pawn, Slot slot)
    {
        RoomGraph rooms = LevelSync.Instance != null ? LevelSync.Instance.Rooms : null;
        if (rooms == null)
            return false;

        SiteScorer scorer = MatchStandings.Scorer(MatchManager.Instance, rooms, siteWeights, standings);
        int contender = standings.IndexOf(player);
        if (contender < 0 || !SitePicker.TryPick(rooms, scorer.WeightsFor(contender), null, SiteClaims.IsFree, random, out ChestSite site))
            return false;

        bool supply = slot.Kind == ChestKind.Supply;
        SitePicker.CornerFor(site, supply ? supplyFootprint : weaponFootprint, out Vector3 position, out Quaternion rotation);

        ChestTimer chest = Instantiate(supply ? supplyChest : weaponChest, position, rotation);
        Spawn(chest.gameObject);
        SiteClaims.Claim(site, chest.NetworkObject);

        double age = slot.Started ? 0 : Random.Range(0f, lifetime);
        chest.ServerStartLife(player, Now - age, Now - age + lifetime);

        slot.Chest = chest;
        slot.Started = true;
        chests.Add(chest);
        return true;
    }

    /// <summary>
    /// The match is over, or a new level replaced this one: every chest goes, and every player starts with fresh
    /// slots (a staggered first wave again) on the next tick of a match.
    /// </summary>
    [Server]
    private void ServerEndSupply()
    {
        foreach (ChestTimer chest in chests)
        {
            if (chest != null && chest.IsSpawned)
                chest.Despawn();
        }

        chests.Clear();
        slots.Clear();
        supplying = false;
    }

    private float RespawnDelay() => Random.Range(respawnDelay.x, respawnDelay.y);
}
