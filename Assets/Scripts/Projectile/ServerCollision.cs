using System.Collections.Generic;
using Arena.NetworkedMotion;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using SC = Arena.ServerCollision;

/// <summary>
/// The server-only collision driver of a projectile. Each tick it advances the sibling
/// <see cref="NetworkedMotion"/>'s age, casts the world broadphase and overlaps the projectile broadphase
/// through <see cref="ProjectileWorldProvider"/>, classifies each hit with the pure
/// <see cref="SC.ServerCollisionProcessor"/>, and runs the matching authored <see cref="HitReaction"/>
/// list — the projectile type's, then the owner's (passive abilities, looked up server-side). The
/// reactions run their Unity-coupled <see cref="WorldEffect"/>s against an <see cref="EffectContext"/> that
/// carries BOTH colliding GameObjects, so an effect can do anything to either side or the world.
///
/// Collision is SERVER-AUTHORITATIVE and not predicted, so effects perform their I/O directly (no gating).
/// Despawn is driven here off the sibling <see cref="Health"/> reaching zero, or the type's lifetime.
/// </summary>
public sealed class ServerCollision : NetworkBehaviour
{
    private NetworkedMotion _motion;
    private Health _health;
    private ProjectileWorldProvider _world;

    // Projectile-channel one-shot-on-enter dedupe guard (was ProjectileState.LastHitProjectileId).
    private int _guard = SC.ServerCollisionProcessor.NoGuard;

    // Scratch buffer for the projectile overlap broadphase.
    private readonly NetworkedMotion[] _overlaps = new NetworkedMotion[32];

    private static bool _warnedNoProvider;

    private void Awake()
    {
        _motion = GetComponent<NetworkedMotion>();
        _health = GetComponent<Health>();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        _world = ProjectileWorldProvider.Instance;

        // Loud failure for a silent footgun: with no provider in the scene the projectile still moves and
        // renders (motion is provider-free) but NEVER casts, so it passes through everything. Warn once.
        if (_world == null && !_warnedNoProvider)
        {
            _warnedNoProvider = true;
            Debug.LogWarning("[ServerCollision] No ProjectileWorldProvider in the active scene — " +
                "projectiles will move and render but never collide (no surface/enemy/projectile hits). " +
                "Add a ProjectileWorldProvider to the gameplay scene.", this);
        }

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
        if (_motion == null || _motion.Type == null || _world == null)
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
        OwnerReactions owner = _world.GetOwnerReactions(ownerId);

        // ---- world channel: sweep prev->new; a laser casts its beam forward instead ----
        Vector3 from = prevPos, to = newPos;
        if (type.Movement == MotionKind.Laser)
        {
            from = newPos;
            to = newPos + state.OriginForward * type.MaxRange;
        }

        if (_world.CastWorld(from, to, type.Radius, out SC.WorldHit hit, out GameObject hitObject) &&
            SC.ServerCollisionProcessor.TryClassifyWorldHit(in state, vel, in hit, out SC.HitInfo worldInfo))
        {
            var ctx = new EffectContext(gameObject, hitObject, worldInfo.Point, worldInfo.Normal, worldInfo.Direction, ownerId);
            if (worldInfo.Category == SC.HitCategory.Enemy)
            {
                RunReactions(type.OnEnemy, in ctx);
                RunReactions(owner.OnEnemy, in ctx);
            }
            else
            {
                RunReactions(type.OnSurface, in ctx);
                RunReactions(owner.OnSurface, in ctx);
            }
        }

        // ---- projectile channel ----
        if (!IsDead() && WantsProjectileChannel(type, in owner))
            ResolveProjectileChannel(newPos, vel, in state, in owner);

        if (IsDead())
            ServerDespawn();
    }

    private void ResolveProjectileChannel(Vector3 selfPos, Vector3 vel, in Arena.NetworkedMotion.NetworkedMotionState self, in OwnerReactions owner)
    {
        ProjectileType type = _motion.Type;
        int n = _world.OverlapProjectiles(selfPos, type.Radius, _overlaps, self.Id);
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

            var ctx = new EffectContext(gameObject, other.gameObject, info.Point, info.Normal, info.Direction, self.OwnerId);
            RunReactions(type.OnProjectile, in ctx);
            RunReactions(owner.OnProjectile, in ctx);

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

    private bool IsDead() => _health != null && _health.IsDead;

    // Run a typed reaction list (projectile type's), short-circuiting if the projectile dies mid-list.
    private void RunReactions<T>(List<T> reactions, in EffectContext ctx) where T : HitReaction
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

    // Run the owner-contributed reaction list (base type), short-circuiting on death.
    private void RunReactions(IReadOnlyList<HitReaction> reactions, in EffectContext ctx)
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

    private void ServerDespawn()
    {
        if (IsServerInitialized && IsSpawned)
            Despawn(DespawnType.Pool);
    }
}
