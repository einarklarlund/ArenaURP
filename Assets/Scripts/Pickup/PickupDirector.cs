using System.Collections.Generic;
using Arena.LevelGen;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Keeps the level stocked with small health and ammo pickups during a match: a few in one room, and one line along
/// one of the level's pickup-line sites. When either's pickups are all taken, it comes back after a delay somewhere
/// picked afresh. The server picks where pickups go and when a group refills; every peer then makes its own local
/// copies, and each
/// copy decides by itself who claims it (see <see cref="Pickup"/>). A refill replaces the group's copies on every
/// peer, so any disagreement between peers ends there.
/// </summary>
public sealed class PickupDirector : NetworkBehaviour
{
    /// <summary>The room's pickups or the line's: <see cref="RoomGroup"/> or <see cref="LineGroup"/>.</summary>
    private sealed class Group
    {
        public readonly List<Pickup> Pickups = new();
        /// <summary>Server only: when an empty group refills, on the network clock. Unset while it has pickups.</summary>
        public double RefillTime = double.MaxValue;
    }

    private const int RoomGroup = 0;
    private const int LineGroup = 1;

    [SerializeField] private PickupDirectorSettings settings;

    // Every peer's local copies, by group.
    private readonly Dictionary<int, Group> groups = new();

    // ── Server-only state ────────────────────────────────────────────────

    private RoomGraph stockedRooms;
    private bool supplying;

    private double Now => TimeManager.TicksToTime();

    // ═════════════════════════════════════════════════════════════════════
    //  SERVER: start-up and shutdown
    // ═════════════════════════════════════════════════════════════════════

    public override void OnStopServer()
    {
        base.OnStopServer();
        ClearAll();
        stockedRooms = null;
        supplying = false;
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        ClearAll();
    }

    /// <summary>A late joiner gets what's still lying around, as the server sees it.</summary>
    public override void OnSpawnServer(NetworkConnection connection)
    {
        base.OnSpawnServer(connection);
        if (connection.IsLocalClient) return;

        foreach (KeyValuePair<int, Group> entry in groups)
        {
            List<Vector3> positions = new();
            List<byte> kinds = new();
            foreach (Pickup pickup in entry.Value.Pickups)
            {
                if (pickup == null || pickup.Claimed) continue;
                positions.Add(pickup.transform.position);
                kinds.Add((byte)pickup.kind);
            }

            if (positions.Count > 0)
                TargetFillGroup(connection, entry.Key, positions.ToArray(), kinds.ToArray());
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  SERVER: supply
    // ═════════════════════════════════════════════════════════════════════

    private void Update()
    {
        if (!IsServerInitialized) return;

        MatchManager match = MatchManager.Instance;
        RoomGraph rooms = LevelSync.Instance != null ? LevelSync.Instance.Rooms : null;
        if (match == null || match.State != MatchState.During || rooms == null)
        {
            if (supplying)
                ServerEnd();
            return;
        }

        // The match started, or a new level arrived mid-match: start over on it.
        if (rooms != stockedRooms)
            ServerStock(rooms);

        foreach (KeyValuePair<int, Group> entry in groups)
            ServerTickGroup(rooms, entry.Key, entry.Value);
    }

    [Server]
    private void ServerStock(RoomGraph rooms)
    {
        ServerEnd();
        supplying = true;
        stockedRooms = rooms;

        ServerFillRoom(rooms);
        ServerFillLine(rooms);
    }

    [Server]
    private void ServerTickGroup(RoomGraph rooms, int id, Group group)
    {
        if (group.RefillTime == double.MaxValue)
        {
            foreach (Pickup pickup in group.Pickups)
            {
                if (pickup != null) return;
            }

            // All taken.
            group.RefillTime = Now + settings.refillDelay;
            return;
        }

        if (Now < group.RefillTime) return;

        if (id == RoomGroup)
            ServerFillRoom(rooms);
        else
            ServerFillLine(rooms);
    }

    /// <summary>The match is over: every pickup goes.</summary>
    [Server]
    private void ServerEnd()
    {
        ClearAll();
        RpcClearAll();
        stockedRooms = null;
        supplying = false;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  SERVER: placement
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pickups near a random room's chest sites, offset roughly towards the room's middle (the way a site faces).
    /// </summary>
    [Server]
    private void ServerFillRoom(RoomGraph rooms)
    {
        List<int> stocked = new();
        for (int room = 0; room < rooms.RoomCount; room++)
        {
            if (rooms.ChestSitesIn(room).Count > 0)
                stocked.Add(room);
        }

        if (stocked.Count == 0)
        {
            ServerRetryLater(RoomGroup);
            return;
        }

        List<ChestSite> shuffled = new(rooms.ChestSitesIn(stocked[Random.Range(0, stocked.Count)]));
        Shuffle(shuffled);

        Vector3[] positions = new Vector3[settings.pickupsPerRoom];
        for (int i = 0; i < positions.Length; i++)
        {
            // Fewer sites than pickups: go round again, with a different offset each time.
            ChestSite site = shuffled[i % shuffled.Count];
            float yaw = site.YawDegrees + Random.Range(-60f, 60f);
            Vector3 offset = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * settings.siteOffset;
            positions[i] = Grounded(site.Position, site.Position + offset);
        }

        ServerFill(RoomGroup, positions, MixedKinds(positions.Length));
    }

    /// <summary>One pickup per point of a random line site.</summary>
    [Server]
    private void ServerFillLine(RoomGraph rooms)
    {
        IReadOnlyList<PickupLine> lines = rooms.Level.PickupLines;
        if (lines.Count == 0)
        {
            ServerRetryLater(LineGroup);
            return;
        }

        List<Vector3> points = lines[Random.Range(0, lines.Count)].Points;
        Vector3[] positions = new Vector3[points.Count];
        for (int i = 0; i < positions.Length; i++)
            positions[i] = points[i] + Vector3.up * settings.hoverHeight;

        ServerFill(LineGroup, positions, SameKinds(positions.Length));
    }

    /// <summary>No site for the group: try again a refill later.</summary>
    [Server]
    private void ServerRetryLater(int id)
    {
        GetGroup(id).RefillTime = Now + settings.refillDelay;
    }

    /// <summary>Makes the group on this peer and every client.</summary>
    [Server]
    private void ServerFill(int id, Vector3[] positions, byte[] kinds)
    {
        Fill(id, positions, kinds);
        GetGroup(id).RefillTime = double.MaxValue;

        RpcFillGroup(id, positions, kinds);
    }

    /// <summary>A room's kinds: at least one health and one ammo, the rest a coin flip, in no particular order.</summary>
    private static byte[] MixedKinds(int count)
    {
        byte[] kinds = new byte[count];
        for (int i = 0; i < count; i++)
        {
            Pickup.Kind kind = i switch
            {
                0 => Pickup.Kind.Health,
                1 => Pickup.Kind.Ammo,
                _ => Random.value < 0.5f ? Pickup.Kind.Health : Pickup.Kind.Ammo,
            };
            kinds[i] = (byte)kind;
        }
        Shuffle(kinds);
        return kinds;
    }

    /// <summary>A line's kinds: all health or all ammo, a coin flip.</summary>
    private static byte[] SameKinds(int count)
    {
        byte[] kinds = new byte[count];
        byte kind = (byte)(Random.value < 0.5f ? Pickup.Kind.Health : Pickup.Kind.Ammo);
        for (int i = 0; i < count; i++)
            kinds[i] = kind;
        return kinds;
    }

    /// <summary>
    /// <paramref name="target"/> dropped onto the ground under it, when this peer has the level's colliders,
    /// then lifted by the hover height. Without a hit it keeps <paramref name="surface"/>'s height.
    /// </summary>
    private Vector3 Grounded(Vector3 surface, Vector3 target)
    {
        Vector3 from = new(target.x, surface.y + 2f, target.z);
        Vector3 ground = Physics.Raycast(from, Vector3.down, out RaycastHit hit, 4f, settings.groundLayers, QueryTriggerInteraction.Ignore)
            ? hit.point
            : new Vector3(target.x, surface.y, target.z);
        return ground + Vector3.up * settings.hoverHeight;
    }

    private static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  CLIENT
    // ═════════════════════════════════════════════════════════════════════

    // The server has already made its own copies, which a host's client shares.

    [ObserversRpc]
    private void RpcFillGroup(int id, Vector3[] positions, byte[] kinds)
    {
        if (IsServerStarted) return;
        Fill(id, positions, kinds);
    }

    [TargetRpc]
    private void TargetFillGroup(NetworkConnection connection, int id, Vector3[] positions, byte[] kinds)
    {
        if (IsServerStarted) return;
        Fill(id, positions, kinds);
    }

    [ObserversRpc]
    private void RpcClearAll()
    {
        if (IsServerStarted) return;
        ClearAll();
    }

    // ═════════════════════════════════════════════════════════════════════
    //  EVERY PEER: local copies
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Replaces group <paramref name="id"/>'s copies on this peer.</summary>
    private void Fill(int id, Vector3[] positions, byte[] kinds)
    {
        Group group = GetGroup(id);
        Clear(group);

        for (int i = 0; i < positions.Length; i++)
        {
            Pickup prefab = (Pickup.Kind)kinds[i] == Pickup.Kind.Ammo ? settings.ammoPickup : settings.healthPickup;
            Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            group.Pickups.Add(Instantiate(prefab, positions[i], rotation));
        }
    }

    private Group GetGroup(int id)
    {
        if (!groups.TryGetValue(id, out Group group))
        {
            group = new Group();
            groups.Add(id, group);
        }

        return group;
    }

    private void ClearAll()
    {
        foreach (Group group in groups.Values)
            Clear(group);
        groups.Clear();
    }

    private static void Clear(Group group)
    {
        foreach (Pickup pickup in group.Pickups)
        {
            if (pickup != null)
                Destroy(pickup.gameObject);
        }

        group.Pickups.Clear();
    }
}
