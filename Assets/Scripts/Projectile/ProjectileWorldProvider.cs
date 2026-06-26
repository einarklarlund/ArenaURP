using System.Collections.Generic;
using Arena.ServerCollision;
using FishNet;
using FishNet.Managing.Timing;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Scene-level concrete "world service" for projectiles: the single surface through which the
/// server-only collision driver (<see cref="ServerCollision"/>) does its I/O — physics casts/overlaps,
/// spawning networked child projectiles, owner lookup, cosmetic VFX, and the live-projectile registry that
/// resolves an overlap collider back to its <see cref="NetworkedMotion"/>.
///
/// Unity-coupled <see cref="WorldEffect"/> commands also reach the world through this (Spawn/SpawnVfx/owner
/// resolution) so it stays the one place projectile-world mutation happens. Concrete by design (one real
/// implementation, no interface); soft singleton (scene-scoped, resettable). Integration tests subclass it.
/// </summary>
public sealed class ProjectileWorldProvider : MonoBehaviour
{
    [System.Serializable]
    public sealed class TypePrefab
    {
        public ProjectileType Type;
        [Tooltip("Networked bullet prefab spawned for this type (child Spawn).")]
        public NetworkObject Prefab;
        [Tooltip("Optional non-networked cosmetic impact VFX prefab.")]
        public GameObject ImpactVfx;
    }

    [Header("Layer masks")]
    [SerializeField] private LayerMask worldMask;       // Stage | Players
    [SerializeField] private LayerMask projectileMask;  // Bullet-To-Bullet

    [Header("Wiring")]
    [SerializeField] private NetworkPlayerManager playerManager;
    [SerializeField] private List<TypePrefab> catalog = new();

    public static ProjectileWorldProvider Instance { get; private set; }

    private readonly Dictionary<int, NetworkedMotion> _registry = new();
    private readonly Collider[] _cols = new Collider[32];

    private void OnEnable() => Instance = this;
    private void OnDisable() { if (Instance == this) Instance = null; }

    private NetworkPlayerManager Players => playerManager != null
        ? playerManager
        : (playerManager = FindAnyObjectByType<NetworkPlayerManager>());

    // ---- registry ----
    public void Register(int id, NetworkedMotion m) => _registry[id] = m;
    public void Unregister(int id) => _registry.Remove(id);
    public bool TryGet(int id, out NetworkedMotion m) => _registry.TryGetValue(id, out m);

    // ---- queries ----

    /// <summary>
    /// Sweep from-&gt;to against the world broadphase. On a hit, fills <paramref name="hit"/> (engine-free
    /// data the pure classifier consumes) AND outs the actual <paramref name="hitObject"/> so the shell can
    /// hand it to effects as the target GameObject.
    /// </summary>
    public bool CastWorld(Vector3 from, Vector3 to, float radius, out WorldHit hit, out GameObject hitObject)
    {
        hit = default;
        hitObject = null;
        Vector3 delta = to - from;
        float dist = delta.magnitude;
        if (dist < 0.0001f)
            return false;

        if (!Physics.SphereCast(from, radius, delta / dist, out RaycastHit rh, dist, worldMask, QueryTriggerInteraction.Ignore))
            return false;

        hitObject = rh.collider.gameObject;
        var pawn = rh.collider.GetComponentInParent<Pawn>();
        int playerId = pawn != null && pawn.ControllingPlayer.Value != null ? pawn.ControllingPlayer.Value.ObjectId : -1;
        hit = new WorldHit { Point = rh.point, Normal = rh.normal, Distance = rh.distance, PlayerId = playerId };
        return true;
    }

    /// <summary>Overlap the projectile broadphase; write each found projectile's NetworkedMotion (excluding excludeId) into buffer; return count.</summary>
    public int OverlapProjectiles(Vector3 center, float radius, NetworkedMotion[] buffer, int excludeId)
    {
        int found = Physics.OverlapSphereNonAlloc(center, radius, _cols, projectileMask, QueryTriggerInteraction.Collide);
        int written = 0;
        for (int i = 0; i < found && written < buffer.Length; i++)
        {
            var m = _cols[i].GetComponentInParent<NetworkedMotion>();
            if (m == null || m.Id == excludeId)
                continue;
            buffer[written++] = m;
        }
        return written;
    }

    // ---- authoritative spawn (server) ----

    public int Spawn(ProjectileType type, Vector3 pos, Vector3 dir, int ownerId)
    {
        var entry = GetEntry(type);
        if (entry == null || entry.Prefab == null)
            return -1;

        Quaternion rot = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir) : Quaternion.identity;
        NetworkObject nob = InstanceFinder.NetworkManager.GetPooledInstantiated(entry.Prefab, pos, rot, true);
        if (nob.TryGetComponent(out NetworkedMotion motion))
        {
            // Funnel through SetupSpawn so the child gets the FULL payload — including SpawnTick, the
            // basis for its deterministic age.
            PreciseTick tick = InstanceFinder.TimeManager.GetPreciseTick(TickType.Tick);
            motion.SetupSpawn(ownerId, tick, pos, dir);
        }
        InstanceFinder.ServerManager.Spawn(nob);
        return nob.ObjectId;
    }

    // ---- debuff (placeholder until a debuff system exists) ----

    public void ApplyDebuff(GameObject target, DebuffType debuff)
    {
        // TODO: no debuff system exists yet. Intentionally a no-op until one is introduced.
    }

    // ---- cosmetic effects (server or owning client) ----

    public void SpawnVfx(ProjectileType type, Vector3 pos, Vector3 normal)
    {
        var entry = GetEntry(type);
        if (entry == null || entry.ImpactVfx == null)
            return;
        Quaternion rot = normal.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(normal) : Quaternion.identity;
        Instantiate(entry.ImpactVfx, pos, rot); // non-networked, cosmetic
    }

    // ---- owner lookup ----

    /// <summary>
    /// Resolve the projectile's OWNER on the server and return the bundle of extra reactions they
    /// contribute (passives that buff the player's projectiles). The owner is resolved from the id carried
    /// in the spawn payload — the reactions are never sent over the network, only read here on the server.
    /// </summary>
    public OwnerReactions GetOwnerReactions(int ownerId)
    {
        var player = ResolvePlayer(ownerId);
        var pawn = player != null ? player.ControlledPawn.Value : null;
        if (pawn != null && pawn.TryGetComponent<IProjectileEffectProvider>(out var source))
            return source.GetReactions();
        return default;
    }

    /// <summary>Resolve a NetworkPlayer by ObjectId (used for damage attribution by effects).</summary>
    public NetworkPlayer ResolvePlayer(int objectId)
    {
        var players = Players;
        if (players == null || objectId < 0)
            return null;
        foreach (var player in players.ActivePlayers)
            if (player != null && player.ObjectId == objectId)
                return player;
        return null;
    }

    // ---- helpers ----

    private TypePrefab GetEntry(ProjectileType type)
    {
        for (int i = 0; i < catalog.Count; i++)
            if (catalog[i].Type == type)
                return catalog[i];
        return null;
    }
}
