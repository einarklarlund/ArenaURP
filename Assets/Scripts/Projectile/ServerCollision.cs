using System.Collections.Generic;
using Arena.NetworkedMotion;
using FishNet.Object;
using UnityEngine;
using SC = Arena.ServerCollision;

/// <summary>
/// The server-only collision driver of a projectile. Each tick it advances the sibling
/// <see cref="NetworkedMotion"/>'s age, casts the world broadphase and overlaps the projectile broadphase
/// itself, classifies each hit with the pure <see cref="SC.ServerCollisionProcessor"/>, then resolves the hit:
/// on an enemy hit it first runs the hit EXCHANGES from component presence (dealer on this projectile ∧ receiver
/// on the victim — damage/knockback/hitstun, see <see cref="ResolveExchanges"/>; ADR 0002), spends the source's
/// per-hit durability, and dispatches the presence-discovered <see cref="IHitHandler"/> reactions — this
/// projectile's own components first, then the owner's passive-contributed ones (looked up server-side; ADR 0003).
/// This driver enumerates only the classification; it authors no reaction list.
///
/// It holds no ProjectileType: its collision radius and lifetime are AUTHORED ON THE PREFAB
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

    // Hit REACTIONS are IHitHandler components authored on this prefab (ADR 0003) — ricochet, on-hit instantiate,
    // etc. — discovered by presence at the hit site and dispatched by HitResolver's per-channel entry points; there
    // is no serialized reaction list here. The source's per-hit durability spend (the dissolved LoseHealth) also
    // lives in HitResolver. This driver only classifies the hit and builds the HitContext.

    private NetworkedMotion _motion;
    private Health _health;
    private Damage _damage;
    private KnockbackDealer _knockback;
    private HitstunDealer _hitstun;
    private NetPlayerOwnership _ownership;

    // This projectile's OWN handlers' effective Projectile-channel bit — a prefab constant (handlers are fixed at
    // authoring), computed once in Awake so the per-tick projectile-channel gate stays O(1). The owner's
    // passive-contributed mask folds in separately, on roster change (PawnPassiveAbilities.OwnerHitChannelMask).
    private HitChannels _ownProjectileMask;

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

        // Fold this prefab's own handlers' effective mask down to the Projectile bit, once. Handlers are fixed at
        // authoring (never AddComponent'd on a pooled instance), so this constant survives pooled reuse unchanged.
        var handlers = new List<IHitHandler>();
        GetComponents(handlers);
        _ownProjectileMask = HitChannels.None;
        for (int i = 0; i < handlers.Count; i++)
            _ownProjectileMask |= handlers[i].SupportedChannels & handlers[i].EnabledChannels;
        _ownProjectileMask &= HitChannels.Projectile;
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
        PawnPassiveAbilities ownerPassives = ResolveOwnerPassives(owner);

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
            HitChannels channel = worldInfo.Category == SC.HitCategory.Enemy ? HitChannels.Player : HitChannels.Surface;
            var hitCtx = new HitContext(channel, gameObject, owner, hitObject,
                worldInfo.Point, worldInfo.Normal, worldInfo.Direction, sourceCenter,
                _motion, _health);
            if (channel == HitChannels.Player)
                HitResolver.ResolvePlayerHit(_damage, _knockback, _hitstun, in hitCtx);
            else
                HitResolver.ResolveSurfaceHit(in hitCtx);
        }

        // ---- projectile channel ----
        if (!IsDead() && WantsProjectileChannel(ownerPassives))
            ResolveProjectileChannel(newPos, vel, in state, ownerId, owner);

        if (IsDead())
            ServerDespawn();
    }

    private void ResolveProjectileChannel(Vector3 selfPos, Vector3 vel, in NetworkedMotionState self, int selfOwnerId, NetworkPlayer owner)
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
            var hitCtx = new HitContext(HitChannels.Projectile, gameObject, owner, other.gameObject,
                info.Point, info.Normal, info.Direction, info.Point,
                _motion, _health, other);
            HitResolver.ResolveProjectileHit(in hitCtx);

            if (firstHandled < 0)
                firstHandled = other.Id;
            if (IsDead())
                break;
        }

        _guard = SC.ServerCollisionProcessor.ResolveGuard(_guard, firstHandled, stillGuarded);
    }

    /// <summary>
    /// The effective-channel-mask gate (ADR 0003): wake the projectile-vs-projectile overlap sweep iff some
    /// participating handler both SUPPORTS and is ENABLED on the Projectile channel — this projectile's OWN
    /// handlers (<see cref="_ownProjectileMask"/>, a prefab constant) OR the owner's passive-contributed handlers
    /// (<see cref="PawnPassiveAbilities.OwnerHitChannelMask"/>, folded on roster change). This is the SAME mask
    /// handler dispatch ANDs against the current channel, so "wants projectile" stays ONE notion, and an owner
    /// passive CAN wake the channel ("your projectiles deflect enemy projectiles").
    /// </summary>
    private bool WantsProjectileChannel(PawnPassiveAbilities ownerPassives)
    {
        HitChannels mask = _ownProjectileMask;
        if (ownerPassives != null)
            mask |= ownerPassives.OwnerHitChannelMask;
        return (mask & HitChannels.Projectile) != 0;
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

    private static PawnPassiveAbilities ResolveOwnerPassives(NetworkPlayer owner)
    {
        var pawn = owner != null ? owner.ControlledPawn.Value : null;
        return pawn != null && pawn.TryGetComponent<PawnPassiveAbilities>(out var passives) ? passives : null;
    }

    // ---- hit resolution policy ----
    // The exchanges (presence-dispatch: dealer + receiver; ADR 0002), the durability spend, and the reactions
    // (presence-discovered IHitHandler components; ADR 0003) all run through the shared HitResolver's per-channel
    // entry points. A classified hit resolves ATOMICALLY: the exchanges, the spend, and every reaction run (this
    // prefab's, then the owner's contributions) even if the projectile dies mid-hit, so authored order can never
    // change a hit's outcome. Death gates only what comes AFTER the hit: the projectile channel, further overlap
    // hits, and despawn. Exchanges deliberately do NOT run in the projectile channel. The projectile-vs-projectile
    // channel is woken by WantsProjectileChannel's effective-channel-mask gate (own-prefab constant OR the owner's
    // roster-folded mask), so a projectile-channel handler — own or owner-contributed — wakes it.

    private bool IsDead() => _health != null && _health.IsDead;

    private void ServerDespawn()
    {
        if (IsServerInitialized && IsSpawned)
            Despawn(DespawnType.Pool);
    }
}
