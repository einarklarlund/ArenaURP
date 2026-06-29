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
/// <see cref="MotionConfig"/> + initial speed are ASSIGNED locally on every peer by the instance's
/// <c>TypeObjectRoot</c> (which calls <c>ProjectileType.Initialize</c> → <see cref="Configure"/>) at Awake
/// from the baked type — so the config is NOT on the wire; only the per-spawn anchor (tick/pos/dir) rides
/// the payload. It knows nothing of Health/Damage/ownership/VFX — those are other components.
/// </summary>
public sealed class NetworkedMotion : NetworkBehaviour
{
    // Authoritative segment, synced only when it CHANGES (a ricochet/deflect begins a new segment).
    private readonly SyncVar<Vector3> _segPos = new();
    private readonly SyncVar<Vector3> _segForward = new();
    private readonly SyncVar<float> _segSpeed = new();
    private readonly SyncVar<float> _segAge = new();

    // Motion parameters: instance-lifetime config, set once locally by the TypeObjectRoot via Configure.
    // NOT on the payload and NOT wiped on despawn — prefab-per-kind fixes a pooled instance's kind forever.
    private MotionConfig _config;
    private float _initialSpeed;

    // Per-spawn payload (set by the spawner before Spawn). Ownership lives on NetPlayerOwnership.
    public PreciseTick SpawnTick;
    public Vector3 SpawnPosition;
    public Vector3 SpawnForward;

    private MotionCoreState _state;
    private bool _initialized;

    public int Id => (int)ObjectId;
    public MotionConfig Config => _config;

    /// <summary>The current motion state (read by ServerCollision for this and other movers' positions).</summary>
    public MotionCoreState State => _state;

    /// <summary>
    /// Assign the motion parameters from the type. Called by the instance's <c>TypeObjectRoot</c> at Awake
    /// on every peer (before the payload + OnStartNetwork), so these are present when <see cref="OnStartNetwork"/>
    /// seeds the state. Read locally from the baked SO — never serialized over the network.
    /// </summary>
    public void Configure(MotionConfig config, float initialSpeed)
    {
        _config = config;
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
        MotionCoreState s = _state;
        s.Age = ElapsedSinceSpawn();
        transform.position = NetworkedMotionProcessor.PositionAt(in s, in _config);
        Vector3 vel = NetworkedMotionProcessor.VelocityAt(in s, in _config);
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
        return NetworkedMotionProcessor.PositionAt(in s, in _config);
    }

    /// <summary>Server: set the working age so <see cref="State"/> reflects the current tick for collision queries.</summary>
    public void ServerSetAge(float age) => _state.Age = age;

    /// <summary>
    /// Server: begin a new trajectory segment (ricochet/deflect) at the current age and publish it so every
    /// peer re-derives the bent path. Effects call this through the invoker's NetworkedMotion.
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
        // Reset per-spawn + working state only. _config / _initialSpeed are INSTANCE-LIFETIME (set once by
        // the TypeObjectRoot at Awake, which pooling does not re-run) — wiping them would leave a reused
        // pooled instance unconfigured, since the config no longer arrives on the payload.
        _state = default;
        _initialized = false;
        SpawnTick = default;
        SpawnPosition = Vector3.zero;
        SpawnForward = Vector3.zero;
    }
}
