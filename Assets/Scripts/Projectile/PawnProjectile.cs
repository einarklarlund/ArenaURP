using System;
using System.Collections.Generic;
using Arena.Projectile;
using FishNet.Connection;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Serializing;
using FishNet.Transporting;
using UnityEngine;
using CoreState = Arena.Projectile.ProjectileState;

/// <summary>
/// Imperative shell for one projectile. Motion is DETERMINISTIC (closed-form from the spawn tick +
/// trajectory segment), so every peer computes position locally with nothing synced per tick — there
/// is no <c>[Replicate]</c>/<c>[Reconcile]</c> loop. Collisions are SERVER-AUTHORITATIVE: only the
/// server ticks the cast/overlap, runs the pure <see cref="ProjectileStepProcessor"/>, performs the
/// returned effects (directly — no gating needed, the code only runs on the server), and publishes the
/// rare authoritative changes (health, a ricochet's new segment, death) via SyncVars + despawn.
///
/// Spawn data rides the predicted-spawn payload (owner, spawn tick, origin pos/forward/speed, health).
/// See the functional-core-imperative-shell skill: the core stays pure; this shell does all the I/O.
/// </summary>
public sealed class PawnProjectile : NetworkBehaviour
{
    [SerializeField] private ProjectileType _type;

    // Authoritative mutable state, synced only when it CHANGES (i.e. on an authoritative collision):
    // health on damage; the segment on ricochet/deflect. A straight bullet that dies on contact never
    // touches these after spawn — it just despawns.
    private readonly SyncVar<int> _health = new();
    private readonly SyncVar<Vector3> _segPos = new();
    private readonly SyncVar<Vector3> _segForward = new();
    private readonly SyncVar<float> _segSpeed = new();
    private readonly SyncVar<float> _segAge = new();

    // Spawn parameters carried via the (predicted-)spawn payload; set by the shooter before Spawn.
    public int SpawnOwnerId;
    public PreciseTick SpawnTick;
    public Vector3 SpawnPosition;
    public Vector3 SpawnForward;
    public int SpawnHealth;

    private CoreState _state;     // authoritative working state on the server
    private ProjectileWorldProvider _world;
    private bool _initialized;

    private readonly List<ProjectileEffect> _effects = new(8);
    private readonly CoreState[] _overlaps = new CoreState[32];

    public int Id => (int)ObjectId;
    public CoreState State => _state;

    /// <summary>
    /// The ONE correct way to configure a projectile's spawn payload. Every spawner (PawnWeapon and
    /// ProjectileWorldProvider.Spawn for child projectiles) MUST call this before
    /// <c>ServerManager.Spawn</c> — funnelling through a single method is what guarantees no field
    /// (notably <see cref="SpawnTick"/>, the basis for deterministic age) is ever forgotten.
    /// Pass <paramref name="health"/> = 0 to default to the type's MaxHealth. The projectile's
    /// <see cref="ProjectileType"/> is the prefab's serialized <c>_type</c> and is not set here.
    /// </summary>
    public void SetupSpawn(int ownerId, PreciseTick tick, Vector3 pos, Vector3 dir, int health = 0)
    {
        SpawnOwnerId = ownerId;
        SpawnTick = tick;
        SpawnPosition = pos;
        SpawnForward = dir;
        SpawnHealth = health;
        ApplySpawnTransform(); // position immediately so a pooled/instantiated object never shows at the origin
    }

    /// <summary>
    /// Snap the transform to the spawn pose. Called the instant the spawn data is known — on the
    /// spawner via <see cref="SetupSpawn"/>, and on every networked peer via <see cref="ReadPayload"/>
    /// — so the projectile is never visible at (0,0,0) for a frame while waiting for OnStartNetwork.
    /// The prefab has no NetworkTransform, so nothing else positions it.
    /// </summary>
    private void ApplySpawnTransform()
    {
        transform.position = SpawnPosition;
        if (SpawnForward.sqrMagnitude > 1e-4f)
            transform.rotation = Quaternion.LookRotation(SpawnForward);
    }

    // ---- spawn payload (immutable initial data; seeds the predicting owner + all observers) ----

    public override void WritePayload(NetworkConnection connection, Writer writer)
    {
        base.WritePayload(connection, writer);
        writer.WriteInt32(SpawnOwnerId);
        writer.WritePreciseTick(SpawnTick);
        writer.WriteVector3(SpawnPosition);
        writer.WriteVector3(SpawnForward);
        writer.WriteInt32(SpawnHealth);
    }

    public override void ReadPayload(NetworkConnection connection, Reader reader)
    {
        base.ReadPayload(connection, reader);
        SpawnOwnerId = reader.ReadInt32();
        SpawnTick = reader.ReadPreciseTick();
        SpawnPosition = reader.ReadVector3();
        SpawnForward = reader.ReadVector3();
        SpawnHealth = reader.ReadInt32();
        ApplySpawnTransform(); // remote peers: position the moment the payload lands, before OnStartNetwork
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();

        _world = ProjectileWorldProvider.Instance;
        _world?.Register(Id, this);

        int health = SpawnHealth > 0 ? SpawnHealth : (_type != null ? _type.MaxHealth : 1);
        float speed = _type != null ? _type.Speed : 0f;
        _state = CoreState.Spawn(SpawnOwnerId, Id, SpawnPosition, SpawnForward, speed, health);
        _initialized = _type != null;

        // Clients fold server-driven changes (ricochet segment, health) into _state via OnChange.
        _segPos.OnChange += OnSegPosChanged;
        _segForward.OnChange += OnSegForwardChanged;
        _segSpeed.OnChange += OnSegSpeedChanged;
        _segAge.OnChange += OnSegAgeChanged;
        _health.OnChange += OnHealthChanged;

        Render();
    }

    private static bool _warnedNoProvider;

    public override void OnStartServer()
    {
        base.OnStartServer();

        // Loud failure for a silent footgun: with no ProjectileWorldProvider in the scene, _world is
        // null and ServerTick early-returns every tick — the projectile still renders and flies
        // (motion is provider-free) but NEVER casts, so it passes through every surface, player, and
        // projectile. That looks exactly like "projectiles don't interact" with no error to explain it.
        // Warn once so a mis-set-up gameplay scene is obvious instead of mysterious.
        if (_world == null && !_warnedNoProvider)
        {
            _warnedNoProvider = true;
            Debug.LogWarning("[PawnProjectile] No ProjectileWorldProvider in the active scene — " +
                "projectiles will move and render but never collide (no surface/enemy/projectile hits). " +
                "Add a ProjectileWorldProvider to the gameplay scene.", this);
        }

        // Initialize the synced authoritative state from the spawn data.
        _health.Value = _state.Health;
        _segPos.Value = _state.OriginPos;
        _segForward.Value = _state.OriginForward;
        _segSpeed.Value = _state.OriginSpeed;
        _segAge.Value = _state.OriginAge;

        TimeManager.OnTick += ServerTick;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        if (TimeManager != null)
            TimeManager.OnTick -= ServerTick;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _segPos.OnChange -= OnSegPosChanged;
        _segForward.OnChange -= OnSegForwardChanged;
        _segSpeed.OnChange -= OnSegSpeedChanged;
        _segAge.OnChange -= OnSegAgeChanged;
        _health.OnChange -= OnHealthChanged;
        ProjectileWorldProvider.Instance?.Unregister(Id);
    }

    // ---- server: the only place collisions are resolved ----

    private void ServerTick()
    {
        if (!_initialized || _world == null || !_state.Alive)
            return;

        float dt = (float)TimeManager.TickDelta;
        float age = ElapsedSinceSpawn();
        if (age >= _type.Lifetime)
        {
            ServerDespawn();
            return;
        }

        float prevAge = Mathf.Max(0f, age - dt);
        _state.Age = prevAge;
        Vector3 prevPos = ProjectileStepProcessor.PositionAt(in _state, _type);
        _state.Age = age;
        Vector3 newPos = ProjectileStepProcessor.PositionAt(in _state, _type);
        transform.position = newPos; // keep this projectile's trigger collider current for overlap

        _effects.Clear();

        // World channel: sweep prev->new; a laser casts its beam forward instead.
        Vector3 from = prevPos, to = newPos;
        if (_type.Movement == MovementKind.Laser)
        {
            from = newPos;
            to = newPos + _state.OriginForward * _type.MaxRange;
        }
        if (_world.CastWorld(from, to, _type.Radius, out WorldHit hit))
            _state = ProjectileStepProcessor.ResolveWorldHit(_state, _type, in hit, _effects);

        // Projectile channel.
        if (_state.Alive && _type.OnProjectile != null && _type.OnProjectile.Count > 0)
        {
            int n = _world.OverlapProjectileStates(newPos, _type.Radius, _overlaps, Id);
            _state = ProjectileStepProcessor.ResolveProjectileHits(_state, _type, new ReadOnlySpan<CoreState>(_overlaps, 0, n), _effects);
        }

        ApplyEffects();
        PublishAuthoritativeState();

        if (!_state.Alive)
            ServerDespawn();
    }

    /// <summary>Perform the core's emitted effects. Server-only context, so no gating is needed.</summary>
    private void ApplyEffects()
    {
        for (int i = 0; i < _effects.Count; i++)
        {
            var e = _effects[i];
            switch (e.Kind)
            {
                case EffectKind.Damage:
                    _world.ApplyDamage(e.TargetPlayerId, e.Amount, e.AttackerPlayerId, e.Point, e.Dir);
                    break;
                case EffectKind.Spawn:
                    for (int n = 0; n < e.Count; n++)
                        _world.Spawn(e.Type, e.Point, e.Dir, e.AttackerPlayerId);
                    break;
                case EffectKind.Debuff:
                    _world.ApplyDebuff(e.TargetPlayerId, e.Debuff);
                    break;
                case EffectKind.Vfx:
                    RpcImpactVfx(e.Point, e.Normal); // cosmetic; broadcast to clients (+ runs on server)
                    break;
            }
        }
    }

    /// <summary>Push the rare authoritative changes to clients. SyncVars only send on actual change.</summary>
    private void PublishAuthoritativeState()
    {
        _health.Value = _state.Health;
        _segPos.Value = _state.OriginPos;
        _segForward.Value = _state.OriginForward;
        _segSpeed.Value = _state.OriginSpeed;
        _segAge.Value = _state.OriginAge;
    }

    [ObserversRpc(RunLocally = true)]
    private void RpcImpactVfx(Vector3 point, Vector3 normal)
    {
        // Custom vfx-type from SpawnImpactVfx isn't carried over the RPC; default to this projectile's type.
        _world?.SpawnVfx(_type, point, normal);
    }

    // ---- rendering (all peers, deterministic from spawn tick + segment) ----

    private void Update()
    {
        if (_initialized)
            Render();
    }

    private void Render()
    {
        if (_type == null)
            return;
        CoreState s = RenderState();
        transform.position = ProjectileStepProcessor.PositionAt(in s, _type);
        Vector3 vel = ProjectileStepProcessor.VelocityAt(in s, _type);
        if (vel.sqrMagnitude > 1e-4f)
            transform.rotation = Quaternion.LookRotation(vel.normalized);
        else if (s.OriginForward.sqrMagnitude > 1e-4f)
            transform.rotation = Quaternion.LookRotation(s.OriginForward);
    }

    /// <summary>
    /// State for rendering on any peer: <see cref="_state"/> with the current age. <see cref="_state"/>
    /// is the source of truth everywhere — built from the spawn PAYLOAD in OnStartNetwork (so it is
    /// correct the instant the object exists, including a freshly PREDICTED object whose server SyncVars
    /// have not arrived yet) and updated by the server-driven SyncVar OnChange handlers below. Reading the
    /// SyncVars directly here was the bug: on a predicted/just-spawned client they are still at their
    /// default (0,0,0), so the projectile rendered at the origin until the server's values replicated.
    /// </summary>
    private CoreState RenderState()
    {
        CoreState s = _state;
        s.Age = ElapsedSinceSpawn();
        return s;
    }

    // Server-authoritative changes (ricochet's new segment, health) reach clients as SyncVar changes;
    // fold them into _state. asServer writes are the server's own and already in _state.
    private void OnSegPosChanged(Vector3 _, Vector3 next, bool asServer) { if (!asServer) _state.OriginPos = next; }
    private void OnSegForwardChanged(Vector3 _, Vector3 next, bool asServer) { if (!asServer) _state.OriginForward = next; }
    private void OnSegSpeedChanged(float _, float next, bool asServer) { if (!asServer) _state.OriginSpeed = next; }
    private void OnSegAgeChanged(float _, float next, bool asServer) { if (!asServer) _state.OriginAge = next; }
    private void OnHealthChanged(int _, int next, bool asServer) { if (!asServer) _state.Health = next; }

    private float ElapsedSinceSpawn() => (float)TimeManager.TimePassed(SpawnTick);

    public void ServerDespawn()
    {
        if (IsServerInitialized && IsSpawned)
            Despawn(DespawnType.Pool);
    }

    public override void ResetState(bool asServer)
    {
        base.ResetState(asServer);
        _state = default;
        _initialized = false;
        SpawnOwnerId = 0;
        SpawnTick = default;
        SpawnPosition = Vector3.zero;
        SpawnForward = Vector3.zero;
        SpawnHealth = 0;
    }
}
