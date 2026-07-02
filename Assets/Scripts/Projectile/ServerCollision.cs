using System.Collections.Generic;
using Arena.NetworkedMotion;
using FishNet.Object;
using UnityEngine;
using SC = Arena.ServerCollision;

/// <summary>
/// The server-only collision driver of a projectile. Each tick it advances the sibling
/// <see cref="NetworkedMotion"/>'s age, casts the world broadphase and overlaps the projectile broadphase
/// itself, classifies each hit with the pure <see cref="SC.ServerCollisionProcessor"/>, builds the matching
/// concrete operation context (<see cref="ProjectileSurfaceCollision"/> / <see cref="ProjectilePlayerCollision"/>
/// / <see cref="ProjectileProjectileCollision"/>), and runs this prefab's authored operations for that
/// category, then the owner's runtime contributions (passive abilities / powerups, looked up server-side).
///
/// It holds no ProjectileType: its collision radius, lifetime, and hit operations are AUTHORED ON THE PREFAB
/// (prefab-per-kind), so they are runtime-tunable per instance and identical on every peer with nothing on the
/// wire. Motion — including the per-tick collision sweep (a beam casts forward; a mover sweeps prev→new) —
/// comes polymorphically from the sibling motion's <see cref="IMotionStrategy"/>, replacing the old
/// <c>if (Movement == Laser)</c> branch. Collision is SERVER-AUTHORITATIVE and not predicted, so operations
/// perform their I/O directly. Despawn is driven here off the sibling <see cref="Health"/> reaching zero, or
/// the authored lifetime.
/// </summary>
public sealed class ServerCollision : NetworkBehaviour
{
    [Header("Broadphase layers")]
    [SerializeField] private LayerMask worldMask;       // Stage | Players
    [SerializeField] private LayerMask projectileMask;  // Bullet-To-Bullet

    [Header("Collision (per-kind config, authored on the prefab)")]
    [Tooltip("Cast/overlap radius of this projectile.")]
    public float Radius = 0.05f;
    [Tooltip("Seconds before the projectile self-despawns.")]
    public float Lifetime = 5f;

    [Header("Hit reactions (operations run per hit category)")]
    [Tooltip("Operations run when this projectile hits a surface (wall/stage).")]
    [SerializeReference, EffectSelector]
    public List<IEffect<ProjectileSurfaceCollision>> OnSurface = new();
    [Tooltip("Operations run when this projectile hits a player.")]
    [SerializeReference, EffectSelector]
    public List<IEffect<ProjectilePlayerCollision>> OnPlayer = new();
    [Tooltip("Operations run when this projectile hits another projectile.")]
    [SerializeReference, EffectSelector]
    public List<IEffect<ProjectileProjectileCollision>> OnProjectile = new();

    private NetworkedMotion _motion;
    private Health _health;
    private NetPlayerOwnership _ownership;
    private NetworkPlayerManager _players;

    // Projectile-channel one-shot-on-enter dedupe guard (was ProjectileState.LastHitProjectileId).
    private int _guard = SC.ServerCollisionProcessor.NoGuard;

    // Scratch buffers for the broadphase queries.
    private readonly NetworkedMotion[] _overlaps = new NetworkedMotion[32];
    private readonly Collider[] _cols = new Collider[32];

    private void Awake()
    {
        _motion = GetComponent<NetworkedMotion>();
        _health = GetComponent<Health>();
        _ownership = GetComponent<NetPlayerOwnership>();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        TimeManager.OnTick += ServerTick;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        if (TimeManager != null)
            TimeManager.OnTick -= ServerTick;
        _guard = SC.ServerCollisionProcessor.NoGuard;
    }

    private void ServerTick()
    {
        if (_motion == null)
            return;
        if (_health != null && _health.IsDead)
            return;

        IMotionStrategy strategy = _motion.Strategy;
        float dt = (float)TimeManager.TickDelta;
        float age = _motion.ElapsedSinceSpawn();
        if (age >= Lifetime)
        {
            ServerDespawn();
            return;
        }

        float prevAge = Mathf.Max(0f, age - dt);
        Vector3 prevPos = _motion.PositionAtAge(prevAge);
        Vector3 newPos = _motion.PositionAtAge(age);
        _motion.ServerSetAge(age);     // State now reflects this tick for velocity + cross-projectile queries
        transform.position = newPos;    // keep this projectile's trigger collider current for the overlap

        var state = _motion.State;
        Vector3 vel = NetworkedMotionProcessor.VelocityAt(strategy, in state);
        int ownerId = _ownership != null ? _ownership.SpawnOwnerId : -1;
        NetworkPlayer owner = ResolvePlayer(ownerId);
        ProjectileEffects ownerOps = GetOwnerOperations(owner);

        // ---- world channel: the strategy chooses the cast segment (a beam casts forward; a mover sweeps prev->new) ----
        MotionSweep sweep = strategy != null
            ? strategy.CollisionSweep(in state, prevPos, newPos)
            : new MotionSweep(prevPos, newPos);

        if (CastWorld(sweep.From, sweep.To, Radius, out SC.WorldHit hit, out GameObject hitObject) &&
            SC.ServerCollisionProcessor.TryClassifyWorldHit(in state, ownerId, vel, in hit, out SC.HitInfo worldInfo))
        {
            if (worldInfo.Category == SC.HitCategory.Enemy)
            {
                var ctx = new ProjectilePlayerCollision(gameObject, hitObject, owner,
                    worldInfo.Point, worldInfo.Normal, worldInfo.Direction);
                RunEffects(OnPlayer, ctx);
                RunEffects(ownerOps.OnPlayer, ctx);
            }
            else
            {
                var ctx = new ProjectileSurfaceCollision(gameObject, owner,
                    worldInfo.Point, worldInfo.Normal, worldInfo.Direction);
                RunEffects(OnSurface, ctx);
                RunEffects(ownerOps.OnSurface, ctx);
            }
        }

        // ---- projectile channel ----
        if (!IsDead() && WantsProjectileChannel(in ownerOps))
            ResolveProjectileChannel(newPos, vel, in state, ownerId, owner, in ownerOps);

        if (IsDead())
            ServerDespawn();
    }

    private void ResolveProjectileChannel(Vector3 selfPos, Vector3 vel, in NetworkedMotionState self, int selfOwnerId, NetworkPlayer owner, in ProjectileEffects ownerOps)
    {
        int n = OverlapProjectiles(selfPos, Radius, _overlaps, self.Id);
        int firstHandled = SC.ServerCollisionProcessor.NoGuard;
        bool stillGuarded = false;

        for (int i = 0; i < n; i++)
        {
            NetworkedMotion other = _overlaps[i];
            if (other == null)
                continue;
            var otherState = other.State;
            int otherOwnerId = other.TryGetComponent<NetPlayerOwnership>(out var otherOwnership) ? otherOwnership.SpawnOwnerId : -1;
            if (!SC.ServerCollisionProcessor.TryClassifyProjectileHit(in self, selfOwnerId, selfPos, vel, in otherState, otherOwnerId, _guard, out SC.HitInfo info, out bool stillGuardedThis))
            {
                stillGuarded |= stillGuardedThis;
                continue;
            }

            var ctx = new ProjectileProjectileCollision(gameObject, other.gameObject, owner,
                info.Point, info.Normal, info.Direction);
            RunEffects(OnProjectile, ctx);
            RunEffects(ownerOps.OnProjectile, ctx);

            if (firstHandled < 0)
                firstHandled = other.Id;
            if (IsDead())
                break;
        }

        _guard = SC.ServerCollisionProcessor.ResolveGuard(_guard, firstHandled, stillGuarded);
    }

    private bool WantsProjectileChannel(in ProjectileEffects owner)
    {
        if (OnProjectile != null && OnProjectile.Count > 0)
            return true;
        return owner.OnProjectile != null && owner.OnProjectile.Count > 0;
    }

    // ---- broadphase (the physics formerly in ProjectileWorldProvider) ----

    private bool CastWorld(Vector3 from, Vector3 to, float radius, out SC.WorldHit hit, out GameObject hitObject)
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
        hit = new SC.WorldHit { Point = rh.point, Normal = rh.normal, Distance = rh.distance, PlayerId = playerId };
        return true;
    }

    private int OverlapProjectiles(Vector3 center, float radius, NetworkedMotion[] buffer, int excludeId)
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

    // ---- owner lookup (server-side; the owner's operations are never payloaded) ----

    private NetworkPlayerManager Players => _players != null
        ? _players
        : (_players = FindAnyObjectByType<NetworkPlayerManager>());

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

    private static ProjectileEffects GetOwnerOperations(NetworkPlayer owner)
    {
        var pawn = owner != null ? owner.ControlledPawn.Value : null;
        if (pawn != null && pawn.TryGetComponent<IProjectileEffectProvider>(out var source))
            return source.GetProjectileEffects();
        return default;
    }

    // ---- operation running (short-circuits if the projectile dies mid-list) ----

    private void RunEffects<T>(IReadOnlyList<IEffect<T>> ops, T ctx) where T : IEffectContext
    {
        if (ops == null)
            return;
        for (int i = 0; i < ops.Count; i++)
        {
            ops[i]?.Apply(ctx);
            if (IsDead())
                break;
        }
    }

    private bool IsDead() => _health != null && _health.IsDead;

    private void ServerDespawn()
    {
        if (IsServerInitialized && IsSpawned)
            Despawn(DespawnType.Pool);
    }
}
