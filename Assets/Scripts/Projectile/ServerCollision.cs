using Arena.NetworkedMotion;
using FishNet.Object;
using UnityEngine;
using SC = Arena.ServerCollision;

/// <summary>
/// The server-only collision driver of a projectile. Each tick it advances the sibling
/// <see cref="NetworkedMotion"/>'s age, casts the world broadphase and overlaps the projectile broadphase
/// itself, classifies each hit with the pure <see cref="SC.ServerCollisionProcessor"/>, and runs the
/// matching <see cref="HitReaction"/> — the projectile type's defaults (built in code), then the owner's
/// runtime contributions (passive abilities / powerups, looked up server-side). The reactions run their
/// Unity-coupled <see cref="IEffect"/>s against a <see cref="CollisionContext"/> carrying both colliding
/// GameObjects + the resolved attacker.
///
/// It holds no ProjectileType: its collision radius + default reactions are INJECTED by
/// <c>ProjectileType.Initialize</c> via <see cref="Configure"/> (server-side at spawn); kinematics come from
/// the sibling motion's <see cref="MotionConfig"/>. Collision is SERVER-AUTHORITATIVE and not predicted, so
/// effects perform their I/O directly. Despawn is driven here off the sibling <see cref="Health"/> reaching
/// zero, or the motion config's lifetime.
/// </summary>
public sealed class ServerCollision : NetworkBehaviour
{
    [Header("Broadphase layers")]
    [SerializeField] private LayerMask worldMask;       // Stage | Players
    [SerializeField] private LayerMask projectileMask;  // Bullet-To-Bullet

    private NetworkedMotion _motion;
    private Health _health;
    private NetPlayerOwnership _ownership;
    private NetworkPlayerManager _players;

    // Collision config injected by ProjectileType.Initialize (server-side at spawn).
    private float _radius;
    private ProjectileReactions _defaults;

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

    /// <summary>Server: inject the projectile type's collision radius + code-built default reactions at spawn.</summary>
    public void Configure(float radius, ProjectileReactions defaults)
    {
        _radius = radius;
        _defaults = defaults;
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

        MotionConfig config = _motion.Config;
        float dt = (float)TimeManager.TickDelta;
        float age = _motion.ElapsedSinceSpawn();
        if (age >= config.Lifetime)
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
        Vector3 vel = NetworkedMotionProcessor.VelocityAt(in state, in config);
        int ownerId = _ownership != null ? _ownership.SpawnOwnerId : -1;
        NetworkPlayer owner = ResolvePlayer(ownerId);
        OwnerReactions ownerReactions = GetOwnerReactions(owner);

        // ---- world channel: sweep prev->new; a laser casts its beam forward instead ----
        Vector3 from = prevPos, to = newPos;
        if (config.Movement == MotionKind.Laser)
        {
            from = newPos;
            to = newPos + state.OriginForward * config.MaxRange;
        }

        if (CastWorld(from, to, _radius, out SC.WorldHit hit, out GameObject hitObject) &&
            SC.ServerCollisionProcessor.TryClassifyWorldHit(in state, ownerId, vel, in hit, out SC.HitInfo worldInfo))
        {
            var ctx = new CollisionContext(gameObject, hitObject, ownerId, owner,
                worldInfo.Point, worldInfo.Normal, worldInfo.Direction);
            if (worldInfo.Category == SC.HitCategory.Enemy)
            {
                RunReaction(_defaults?.OnEnemy, ctx);
                RunReaction(ownerReactions.OnEnemy, ctx);
            }
            else
            {
                RunReaction(_defaults?.OnSurface, ctx);
                RunReaction(ownerReactions.OnSurface, ctx);
            }
        }

        // ---- projectile channel ----
        if (!IsDead() && WantsProjectileChannel(in ownerReactions))
            ResolveProjectileChannel(newPos, vel, in state, ownerId, owner, in ownerReactions);

        if (IsDead())
            ServerDespawn();
    }

    private void ResolveProjectileChannel(Vector3 selfPos, Vector3 vel, in NetworkedMotionState self, int selfOwnerId, NetworkPlayer owner, in OwnerReactions ownerReactions)
    {
        int n = OverlapProjectiles(selfPos, _radius, _overlaps, self.Id);
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

            var ctx = new CollisionContext(gameObject, other.gameObject, selfOwnerId, owner,
                info.Point, info.Normal, info.Direction);
            RunReaction(_defaults?.OnProjectile, ctx);
            RunReaction(ownerReactions.OnProjectile, ctx);

            if (firstHandled < 0)
                firstHandled = other.Id;
            if (IsDead())
                break;
        }

        _guard = SC.ServerCollisionProcessor.ResolveGuard(_guard, firstHandled, stillGuarded);
    }

    private bool WantsProjectileChannel(in OwnerReactions owner)
    {
        if (_defaults != null && _defaults.OnProjectile.Count > 0)
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

    // ---- owner lookup (server-side; the owner's effects are never payloaded) ----

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

    private static OwnerReactions GetOwnerReactions(NetworkPlayer owner)
    {
        var pawn = owner != null ? owner.ControlledPawn.Value : null;
        if (pawn != null && pawn.TryGetComponent<IProjectileEffectProvider>(out var source))
            return source.GetReactions();
        return default;
    }

    // ---- reaction running (short-circuits if the projectile dies mid-list) ----

    private void RunReaction(HitReaction reaction, IEffectContext ctx)
    {
        if (reaction == null || reaction.Count == 0)
            return;
        reaction.Run(ctx, IsDead);
    }

    private bool IsDead() => _health != null && _health.IsDead;

    private void ServerDespawn()
    {
        if (IsServerInitialized && IsSpawned)
            Despawn(DespawnType.Pool);
    }
}
