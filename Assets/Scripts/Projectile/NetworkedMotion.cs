using Arena.NetworkedMotion;
using FishNet.Connection;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Serializing;
using UnityEngine;
using MotionCoreState = Arena.NetworkedMotion.NetworkedMotionState;

/// <summary>
/// Generic deterministic-motion component, on EVERY peer. Motion is CLOSED-FORM from the spawn tick + the
/// current trajectory segment (see <see cref="NetworkedMotionProcessor"/>), so every peer renders position
/// locally with nothing synced per tick; the only synced state is the rare authoritative segment change
/// (a ricochet/deflect), pushed via SyncVars.
///
/// It is FULLY DECOUPLED from the projectile concept: it holds no <c>ProjectileType</c>. Its
/// <see cref="IMotionStrategy"/> + initial speed are ASSIGNED locally on every peer by the instance's
/// <c>TypeObjectRoot</c> (which calls <c>ProjectileType.Initialize</c> → <see cref="Configure"/>) at Awake
/// from the baked type — so the strategy (an inspector-authored [SerializeReference] on the type) is NOT on
/// the wire; only the per-spawn anchor (tick/pos/dir) rides the payload, and every peer resolves the same
/// strategy from its identical copy of the SO. It knows nothing of Health/Damage/ownership/VFX.
/// </summary>
public sealed class NetworkedMotion : NetworkBehaviour
{
    // Authoritative segment, synced only when it CHANGES (a ricochet/deflect begins a new segment).
    private readonly SyncVar<Vector3> _segPos = new();
    private readonly SyncVar<Vector3> _segForward = new();
    private readonly SyncVar<float> _segSpeed = new();
    private readonly SyncVar<float> _segAge = new();

    // Motion strategy: instance-lifetime config, set once locally by the TypeObjectRoot via Configure.
    // NOT on the payload and NOT wiped on despawn — prefab-per-kind fixes a pooled instance's kind forever.
    // A stateless flyweight shared from the type's [SerializeReference] Motion field (all per-instance state
    // lives in _state), so it is safe to hold the SO's single instance here.
    private IMotionStrategy _strategy;
    private float _initialSpeed;

    // Per-spawn payload (set by the spawner before Spawn). Ownership lives on NetPlayerOwnership.
    public PreciseTick SpawnTick;
    public Vector3 SpawnPosition;
    public Vector3 SpawnForward;

    private MotionCoreState _state;
    private bool _initialized;

    public int Id => (int)ObjectId;

    /// <summary>The motion strategy this mover evaluates (read by effects for live velocity, by ServerCollision for the cast sweep).</summary>
    public IMotionStrategy Strategy => _strategy;

    /// <summary>The current motion state (read by ServerCollision for this and other movers' positions).</summary>
    public MotionCoreState State => _state;

    /// <summary>
    /// Assign the motion parameters from the type. Called by the instance's <c>TypeObjectRoot</c> at Awake
    /// on every peer (before the payload + OnStartNetwork), so these are present when <see cref="OnStartNetwork"/>
    /// seeds the state. Read locally from the baked SO — never serialized over the network.
    /// </summary>
    public void Configure(IMotionStrategy strategy, float initialSpeed)
    {
        if (strategy == null)
            Debug.LogError($"{nameof(NetworkedMotion)} on '{name}' was configured with a null motion strategy " +
                           "— the projectile type's Motion field is unassigned. Falling back to LinearMotion.", this);
        _strategy = strategy ?? new LinearMotion();
        _initialSpeed = initialSpeed;
    }

    /// <summary>
    /// Set the per-spawn payload (tick/pos/dir). Owner is on NetPlayerOwnership and config via
    /// <see cref="Configure"/>; both must be set before <c>ServerManager.Spawn</c> — notably
    /// <see cref="SpawnTick"/>, the basis for deterministic age.
    /// </summary>
    public void SetupSpawn(PreciseTick tick, Vector3 pos, Vector3 dir)
    {
        SpawnTick = tick;
        SpawnPosition = pos;
        SpawnForward = dir;
        ApplySpawnTransform();
    }

    private void ApplySpawnTransform()
    {
        transform.position = SpawnPosition;
        if (SpawnForward.sqrMagnitude > 1e-4f)
            transform.rotation = Quaternion.LookRotation(SpawnForward);
    }

    // ---- spawn payload: the PER-SPAWN ANCHOR only (tick/pos/dir). Config is set locally by the
    //      TypeObjectRoot from the baked SO, so it is deliberately NOT on the wire. ----

    public override void WritePayload(NetworkConnection connection, Writer writer)
    {
        base.WritePayload(connection, writer);
        writer.WritePreciseTick(SpawnTick);
        writer.WriteVector3(SpawnPosition);
        writer.WriteVector3(SpawnForward);
    }

    public override void ReadPayload(NetworkConnection connection, Reader reader)
    {
        base.ReadPayload(connection, reader);
        SpawnTick = reader.ReadPreciseTick();
        SpawnPosition = reader.ReadVector3();
        SpawnForward = reader.ReadVector3();
        ApplySpawnTransform();
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();

        _state = MotionCoreState.Spawn(Id, SpawnPosition, SpawnForward, _initialSpeed);
        _initialized = true;

        _segPos.OnChange += OnSegPosChanged;
        _segForward.OnChange += OnSegForwardChanged;
        _segSpeed.OnChange += OnSegSpeedChanged;
        _segAge.OnChange += OnSegAgeChanged;

        Render();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        _segPos.Value = _state.OriginPos;
        _segForward.Value = _state.OriginForward;
        _segSpeed.Value = _state.OriginSpeed;
        _segAge.Value = _state.OriginAge;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _segPos.OnChange -= OnSegPosChanged;
        _segForward.OnChange -= OnSegForwardChanged;
        _segSpeed.OnChange -= OnSegSpeedChanged;
        _segAge.OnChange -= OnSegAgeChanged;
    }

    // ---- rendering (all peers, deterministic from spawn tick + segment) ----

    private void Update()
    {
        if (_initialized)
            Render();
    }

    private void Render()
    {
        if (_strategy == null)
            return;
        MotionCoreState s = _state;
        s.Age = ElapsedSinceSpawn();
        transform.position = NetworkedMotionProcessor.PositionAt(_strategy, in s);
        Vector3 vel = NetworkedMotionProcessor.VelocityAt(_strategy, in s);
        if (vel.sqrMagnitude > 1e-4f)
            transform.rotation = Quaternion.LookRotation(vel.normalized);
        else if (s.OriginForward.sqrMagnitude > 1e-4f)
            transform.rotation = Quaternion.LookRotation(s.OriginForward);
    }

    /// <summary>Seconds since spawn, derived from the spawn tick on every peer.</summary>
    public float ElapsedSinceSpawn() => (float)TimeManager.TimePassed(SpawnTick);

    /// <summary>Closed-form position at an explicit age within the current segment (server cast endpoints).</summary>
    public Vector3 PositionAtAge(float age)
    {
        MotionCoreState s = _state;
        s.Age = age;
        return NetworkedMotionProcessor.PositionAt(_strategy, in s);
    }

    /// <summary>Server: set the working age so <see cref="State"/> reflects the current tick for collision queries.</summary>
    public void ServerSetAge(float age) => _state.Age = age;

    /// <summary>
    /// Server: begin a new trajectory segment (ricochet/deflect) at the current age and publish it so every
    /// peer re-derives the bent path. Effects call this through the owner's NetworkedMotion.
    /// </summary>
    [Server]
    public void BeginSegment(Vector3 pos, Vector3 dir, float speed)
    {
        _state = MotionCoreState.BeginSegment(_state, pos, dir, speed);
        _segPos.Value = _state.OriginPos;
        _segForward.Value = _state.OriginForward;
        _segSpeed.Value = _state.OriginSpeed;
        _segAge.Value = _state.OriginAge;
    }

    // Server-authoritative segment changes reach clients as SyncVar changes; fold them into _state.
    private void OnSegPosChanged(Vector3 _, Vector3 next, bool asServer) { if (!asServer) _state.OriginPos = next; }
    private void OnSegForwardChanged(Vector3 _, Vector3 next, bool asServer) { if (!asServer) _state.OriginForward = next; }
    private void OnSegSpeedChanged(float _, float next, bool asServer) { if (!asServer) _state.OriginSpeed = next; }
    private void OnSegAgeChanged(float _, float next, bool asServer) { if (!asServer) _state.OriginAge = next; }

    public override void ResetState(bool asServer)
    {
        base.ResetState(asServer);
        // Reset per-spawn + working state only. _strategy / _initialSpeed are INSTANCE-LIFETIME (set once by
        // the TypeObjectRoot at Awake, which pooling does not re-run) — wiping them would leave a reused
        // pooled instance unconfigured, since the config no longer arrives on the payload.
        _state = default;
        _initialized = false;
        SpawnTick = default;
        SpawnPosition = Vector3.zero;
        SpawnForward = Vector3.zero;
    }
}
