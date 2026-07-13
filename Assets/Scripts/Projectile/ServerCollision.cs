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
    [Tooltip("Cast/overlap radius of this projectile. This is the authored BASELINE — the per-spawn buff layer " +
             "(SetRadiusMultiplier) folds over it; the sweep uses EffectiveRadius, never this raw field.")]
    public float Radius = 0.05f;
    [Tooltip("Seconds before the projectile self-despawns.")]
    public float Lifetime = 5f;

    // Per-spawn buff layer over the authored Radius baseline (pooling two-layer rule): effective = Radius * multiplier.
    // Reset to identity in ResetState so pooled reuse starts unbuffed; the baseline field is never wiped. Set through
    // the idempotent SetRadiusMultiplier, the shape shared with SpawnScale.SetScaleMultiplier / SetSpeedMultiplier.
    private float _radiusMultiplier = 1f;

    // The radius the broadphase actually sweeps/overlaps with — baseline folded with the per-spawn buff layer.
    // Public as a read-only derived surface (like Radius): the authoritative collision size after any on-spawn buff.
    public float EffectiveRadius => Radius * _radiusMultiplier;

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
    private Damage _damage;
    private KnockbackDealer _knockback;
    private HitstunDealer _hitstun;
    private NetPlayerOwnership _ownership;

    // Projectile-channel one-shot-on-enter dedupe guard (was ProjectileState.LastHitProjectileId).
    private int _guard = SC.ServerCollisionProcessor.NoGuard;

    // Scratch buffers for the broadphase queries.
    private readonly NetworkedMotion[] _overlaps = new NetworkedMotion[32];
    private readonly Collider[] _cols = new Collider[32];

    private void Awake()
    {
        // This projectile IS the source of every hit it resolves, so its outgoing systems are resolved ONCE here
        // and handed to every context built below. An operation never asks for them.
        _motion = GetComponent<NetworkedMotion>();
        _health = GetComponent<Health>();
        _damage = GetComponent<Damage>();
        _knockback = GetComponent<KnockbackDealer>();
        _hitstun = GetComponent<HitstunDealer>();
        _ownership = GetComponent<NetPlayerOwnership>();

        WarnOnMissingDealers();
    }

    /// <summary>
    /// An operation whose source system is absent is a SILENT no-op — a bullet that authored DealDamage but has no
    /// Damage component simply stops hurting anyone, with nothing in the console. The capability contract only
    /// holds if the prefab carries the component, so say so loudly at load rather than at 3am.
    /// </summary>
    private void WarnOnMissingDealers()
    {
        if (_damage == null && AuthorsPlayerOperation<DealDamage>())
            Debug.LogError($"{name}: authors {nameof(DealDamage)} but has no {nameof(Damage)} component; it will deal no damage.", this);
        if (_knockback == null && AuthorsPlayerOperation<DealKnockback>())
            Debug.LogError($"{name}: authors {nameof(DealKnockback)} but has no {nameof(KnockbackDealer)} component; it will not knock back.", this);
        if (_hitstun == null && AuthorsPlayerOperation<DealHitstun>())
            Debug.LogError($"{name}: authors {nameof(DealHitstun)} but has no {nameof(HitstunDealer)} component; it will not stun.", this);
    }

    private bool AuthorsPlayerOperation<T>()
    {
        if (OnPlayer == null)
            return false;
        for (int i = 0; i < OnPlayer.Count; i++)
            if (OnPlayer[i] is T)
                return true;
        return false;
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

    /// <summary>
    /// Multiply the authored <see cref="Radius"/> baseline for this spawn — the collision-size buff seam, driven by
    /// the on-spawn <c>BuffScale</c> off the owner's passives. Idempotent and order-independent (house rule for
    /// on-spawn buffs; last write wins), mirroring <see cref="SpawnScale.SetScaleMultiplier"/> — collision is
    /// server-only so there is no sync channel and none is wanted.
    /// </summary>
    public void SetRadiusMultiplier(float multiplier) => _radiusMultiplier = multiplier;

    public override void ResetState(bool asServer)
    {
        base.ResetState(asServer);
        _radiusMultiplier = 1f;   // per-spawn buff layer resets to identity; the authored Radius baseline is never wiped
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
        NetworkPlayer owner = NetPlayerResolver.Resolve(ownerId);
        ProjectileEffects ownerOps = GetOwnerOperations(owner);

        // ---- world channel: the strategy chooses the cast segment (a beam casts forward; a mover sweeps prev->new) ----
        MotionSweep sweep = strategy != null
            ? strategy.CollisionSweep(in state, prevPos, newPos)
            : new MotionSweep(prevPos, newPos);

        if (CastWorld(sweep.From, sweep.To, EffectiveRadius, out SC.WorldHit hit, out GameObject hitObject) &&
            SC.ServerCollisionProcessor.TryClassifyWorldHit(in state, ownerId, vel, in hit, out SC.HitInfo worldInfo))
        {
            // The sweep reports Point on the struck SURFACE; this mover's centre at contact stands off it by the
            // sweep radius along the normal. A redirect (ricochet) resumes from there so the mover ends up tangent
            // to the surface, not embedded a radius deep in it (which would immediately re-collide).
            Vector3 sourceCenter = worldInfo.Point + worldInfo.Normal * EffectiveRadius;
            if (worldInfo.Category == SC.HitCategory.Enemy)
            {
                var ctx = new ProjectilePlayerCollision(gameObject, owner,
                    worldInfo.Point, worldInfo.Normal, worldInfo.Direction, sourceCenter,
                    _damage, ResolveDamageable(hitObject),
                    _knockback, ResolveKnockbackReceiver(hitObject),
                    _hitstun, ResolveHitstunReceiver(hitObject),
                    _motion, _health);
                RunEffects(OnPlayer, ctx);
                RunEffects(ownerOps.OnPlayer, ctx);
            }
            else
            {
                var ctx = new ProjectileSurfaceCollision(gameObject, owner,
                    worldInfo.Point, worldInfo.Normal, worldInfo.Direction, sourceCenter,
                    _motion, _health);
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
        int n = OverlapProjectiles(selfPos, EffectiveRadius, _overlaps, self.Id);
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

            // In the projectile channel the classified Point IS this mover's own centre (selfPos), so the
            // source-centre resume anchor coincides with it — no surface standoff to apply.
            var ctx = new ProjectileProjectileCollision(gameObject, owner,
                info.Point, info.Normal, info.Direction, info.Point,
                _damage, ResolveDamageable(other.gameObject),
                _motion, _health, other);
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
    // Owner id -> NetworkPlayer resolution lives in the shared NetPlayerResolver so this and SpawnBuffReceiver
    // can't drift.

    private static ProjectileEffects GetOwnerOperations(NetworkPlayer owner)
    {
        var pawn = owner != null ? owner.ControlledPawn.Value : null;
        if (pawn != null && pawn.TryGetComponent<IProjectileEffectProvider>(out var source))
            return source.GetProjectileEffects();
        return default;
    }

    // Resolve the struck entity's RECEIVING systems once, here at the site that knows what was hit — so each
    // operation takes a resolved handle from the context instead of gambling a GetComponent. All three walk UP
    // from the struck collider, which is why every receiver lives on the entity ROOT.
    private static IDamageable ResolveDamageable(GameObject hit)
        => hit != null ? hit.GetComponentInParent<IDamageable>() : null;

    private static KnockbackReceiver ResolveKnockbackReceiver(GameObject hit)
        => hit != null ? hit.GetComponentInParent<KnockbackReceiver>() : null;

    private static HitstunReceiver ResolveHitstunReceiver(GameObject hit)
        => hit != null ? hit.GetComponentInParent<HitstunReceiver>() : null;

    // ---- operation running ----
    // A classified hit resolves ATOMICALLY: every authored operation for that hit runs (this prefab's list,
    // then the owner's contributions) even if the projectile dies mid-list, so authored order can never change
    // a hit's outcome. Death gates only what comes AFTER the hit: the projectile channel, further overlap hits,
    // and despawn.

    private void RunEffects<T>(IReadOnlyList<IEffect<T>> ops, T ctx) where T : IEffectContext
    {
        if (ops == null)
            return;
        for (int i = 0; i < ops.Count; i++)
            ops[i]?.Apply(ctx);
    }

    private bool IsDead() => _health != null && _health.IsDead;

    private void ServerDespawn()
    {
        if (IsServerInitialized && IsSpawned)
            Despawn(DespawnType.Pool);
    }
}
