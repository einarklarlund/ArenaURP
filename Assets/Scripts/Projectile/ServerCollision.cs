using Arena.NetworkedMotion;
using FishNet.Object;
using UnityEngine;
using SC = Arena.ServerCollision;

/// <summary>
/// The server-only collision driver of a projectile. Each tick it advances the sibling
/// <see cref="NetworkedMotion"/>'s age, casts the world broadphase and overlaps the projectile broadphase
/// itself (the physics that used to live in ProjectileWorldProvider), classifies each hit with the pure
/// <see cref="SC.ServerCollisionProcessor"/>, and runs the matching authored <see cref="HitReaction"/>
/// list — the projectile type's, then the owner's (passive abilities, looked up server-side). The
/// reactions run their Unity-coupled <see cref="WorldEffect"/>s against an <see cref="EffectContext"/> that
/// carries BOTH colliding GameObjects + the resolved attacker, so an effect can do anything to either side
/// or the world.
///
/// Collision is SERVER-AUTHORITATIVE and not predicted, so effects perform their I/O directly (no gating).
/// Despawn is driven here off the sibling <see cref="Health"/> reaching zero, or the type's lifetime.
/// </summary>
public sealed class ServerCollision : NetworkBehaviour
{
    [Header("Broadphase layers")]
    [SerializeField] private LayerMask worldMask;       // Stage | Players
    [SerializeField] private LayerMask projectileMask;  // Bullet-To-Bullet

    private NetworkedMotion _motion;
    private Health _health;
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
        if (_motion == null || _motion.Type == null)
            return;
        if (_health != null && _health.IsDead)
            return;

        ProjectileType type = _motion.Type;
        float dt = (float)TimeManager.TickDelta;
        float age = _motion.ElapsedSinceSpawn();
        if (age >= type.Lifetime)
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
        MotionConfig config = _motion.Config;
        Vector3 vel = NetworkedMotionProcessor.VelocityAt(in state, in config);
        int ownerId = state.OwnerId;
        NetworkPlayer owner = ResolvePlayer(ownerId);
        OwnerReactions ownerReactions = GetOwnerReactions(owner);

        // ---- world channel: sweep prev->new; a laser casts its beam forward instead ----
        Vector3 from = prevPos, to = newPos;
        if (type.Movement == MotionKind.Laser)
        {
            from = newPos;
            to = newPos + state.OriginForward * type.MaxRange;
        }

        if (CastWorld(from, to, type.Radius, out SC.WorldHit hit, out GameObject hitObject) &&
            SC.ServerCollisionProcessor.TryClassifyWorldHit(in state, vel, in hit, out SC.HitInfo worldInfo))
        {
            var ctx = new EffectContext(gameObject, hitObject, worldInfo.Point, worldInfo.Normal, worldInfo.Direction, ownerId, owner);
            if (worldInfo.Category == SC.HitCategory.Enemy)
            {
                RunReactions(type.OnEnemy, in ctx);
                RunReactions(ownerReactions.OnEnemy, in ctx);
            }
            else
            {
                RunReactions(type.OnSurface, in ctx);
                RunReactions(ownerReactions.OnSurface, in ctx);
            }
        }

        // ---- projectile channel ----
        if (!IsDead() && WantsProjectileChannel(type, in ownerReactions))
            ResolveProjectileChannel(newPos, vel, in state, owner, in ownerReactions);

        if (IsDead())
            ServerDespawn();
    }

    private void ResolveProjectileChannel(Vector3 selfPos, Vector3 vel, in NetworkedMotionState self, NetworkPlayer owner, in OwnerReactions ownerReactions)
    {
        ProjectileType type = _motion.Type;
        int n = OverlapProjectiles(selfPos, type.Radius, _overlaps, self.Id);
        int firstHandled = SC.ServerCollisionProcessor.NoGuard;
        bool stillGuarded = false;

        for (int i = 0; i < n; i++)
        {
            NetworkedMotion other = _overlaps[i];
            if (other == null)
                continue;
            var otherState = other.State;
            if (!SC.ServerCollisionProcessor.TryClassifyProjectileHit(in self, selfPos, vel, in otherState, _guard, out SC.HitInfo info, out bool stillGuardedThis))
            {
                stillGuarded |= stillGuardedThis;
                continue;
            }

            var ctx = new EffectContext(gameObject, other.gameObject, info.Point, info.Normal, info.Direction, self.OwnerId, owner);
            RunReactions(type.OnProjectile, in ctx);
            RunReactions(ownerReactions.OnProjectile, in ctx);

            if (firstHandled < 0)
                firstHandled = other.Id;
            if (IsDead())
                break;
        }

        _guard = SC.ServerCollisionProcessor.ResolveGuard(_guard, firstHandled, stillGuarded);
    }

    private static bool WantsProjectileChannel(ProjectileType type, in OwnerReactions owner)
    {
        if (type.OnProjectile != null && type.OnProjectile.Count > 0)
            return true;
        var op = owner.OnProjectile;
        return op != null && op.Count > 0;
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

    // ---- owner lookup (server-side; the owner's reactions are never payloaded) ----

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

    private void RunReactions<T>(System.Collections.Generic.List<T> reactions, in EffectContext ctx) where T : HitReaction
    {
        if (reactions == null)
            return;
        for (int i = 0; i < reactions.Count; i++)
        {
            reactions[i]?.React(in ctx);
            if (IsDead())
                break;
        }
    }

    private void RunReactions(System.Collections.Generic.IReadOnlyList<HitReaction> reactions, in EffectContext ctx)
    {
        if (reactions == null)
            return;
        for (int i = 0; i < reactions.Count; i++)
        {
            reactions[i]?.React(in ctx);
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
