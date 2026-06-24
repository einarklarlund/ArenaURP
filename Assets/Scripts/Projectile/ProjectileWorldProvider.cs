using System.Collections.Generic;
using Arena.Projectile;
using FishNet;
using FishNet.Managing.Timing;
using FishNet.Object;
using UnityEngine;
using CoreState = Arena.Projectile.ProjectileState;

/// <summary>
/// Scene-level concrete "world service" for projectiles. This is NOT an abstraction the pure core
/// depends on — the core (Arena.Projectile) is pure functions and never calls this. Only the
/// imperative shell (<see cref="PawnProjectile"/>) uses it, for the I/O the core hands back as data:
/// physics casts/overlaps, nearest-enemy lookup, damage/debuff/spawn/VFX, and the live-projectile
/// registry that resolves overlap colliders to <see cref="CoreState"/>.
///
/// Concrete by design (see the functional-core-imperative-shell skill): one real implementation, no
/// interface. Integration tests can subclass it as a spy. Soft singleton (scene-scoped, resettable).
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

    private readonly Dictionary<int, PawnProjectile> _registry = new();
    private readonly Collider[] _cols = new Collider[32];

    private void OnEnable() => Instance = this;
    private void OnDisable() { if (Instance == this) Instance = null; }

    private NetworkPlayerManager Players => playerManager != null
        ? playerManager
        : (playerManager = FindAnyObjectByType<NetworkPlayerManager>());

    // ---- registry ----
    public void Register(int id, PawnProjectile p) => _registry[id] = p;
    public void Unregister(int id) => _registry.Remove(id);
    public bool TryGet(int id, out PawnProjectile p) => _registry.TryGetValue(id, out p);

    // ---- queries ----

    public bool CastWorld(Vector3 from, Vector3 to, float radius, out WorldHit hit)
    {
        hit = default;
        Vector3 delta = to - from;
        float dist = delta.magnitude;
        if (dist < 0.0001f)
            return false;

        if (!Physics.SphereCast(from, radius, delta / dist, out RaycastHit rh, dist, worldMask, QueryTriggerInteraction.Ignore))
            return false;

        var pawn = rh.collider.GetComponentInParent<Pawn>();
        int playerId = pawn != null && pawn.ControllingPlayer.Value != null ? pawn.ControllingPlayer.Value.ObjectId : -1;
        hit = new WorldHit { Point = rh.point, Normal = rh.normal, Distance = rh.distance, PlayerId = playerId };
        return true;
    }

    /// <summary>Overlap the projectile broadphase; write each found projectile's state (excluding excludeId) into buffer; return count.</summary>
    public int OverlapProjectileStates(Vector3 center, float radius, CoreState[] buffer, int excludeId)
    {
        int found = Physics.OverlapSphereNonAlloc(center, radius, _cols, projectileMask, QueryTriggerInteraction.Collide);
        int written = 0;
        for (int i = 0; i < found && written < buffer.Length; i++)
        {
            var p = _cols[i].GetComponentInParent<PawnProjectile>();
            if (p == null || p.Id == excludeId)
                continue;
            buffer[written++] = p.State;
        }
        return written;
    }

    // ---- authoritative effects (server) ----

    public void ApplyDamage(int targetPlayerId, int amount, int attackerPlayerId, Vector3 point, Vector3 dir)
    {
        var target = ResolvePlayer(targetPlayerId);
        if (target?.ControlledPawn.Value is not IDamageable damageable)
            return;
        damageable.TakeDamage(new DamageInfo
        {
            Amount = amount,
            Attacker = ResolvePlayer(attackerPlayerId),
            HitPoint = point,
            Direction = dir,
            Type = DamageType.Bullet,
        });
    }

    public void ApplyDebuff(int targetPlayerId, DebuffType debuff)
    {
        // TODO: no debuff system exists yet. Intentionally a no-op until one is introduced.
    }

    public int Spawn(ProjectileType type, Vector3 pos, Vector3 dir, int ownerId)
    {
        var entry = GetEntry(type);
        if (entry == null || entry.Prefab == null)
            return -1;

        Quaternion rot = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir) : Quaternion.identity;
        NetworkObject nob = InstanceFinder.NetworkManager.GetPooledInstantiated(entry.Prefab, pos, rot, true);
        if (nob.TryGetComponent(out PawnProjectile pp))
        {
            // Funnel through SetupSpawn so the child gets the FULL payload — including SpawnTick, the
            // basis for its deterministic age. Hand-setting fields here previously omitted SpawnTick.
            PreciseTick tick = InstanceFinder.TimeManager.GetPreciseTick(TickType.Tick);
            pp.SetupSpawn(ownerId, tick, pos, dir, health: 0); // 0 -> ProjectileType.MaxHealth
        }
        InstanceFinder.ServerManager.Spawn(nob);
        return nob.ObjectId;
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

    // ---- helpers ----

    private TypePrefab GetEntry(ProjectileType type)
    {
        for (int i = 0; i < catalog.Count; i++)
            if (catalog[i].Type == type)
                return catalog[i];
        return null;
    }

    private NetworkPlayer ResolvePlayer(int objectId)
    {
        var players = Players;
        if (players == null || objectId < 0)
            return null;
        foreach (var player in players.ActivePlayers)
            if (player != null && player.ObjectId == objectId)
                return player;
        return null;
    }
}
