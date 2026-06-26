using Arena.NetworkedMotion;
using FishNet.Connection;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Serializing;
using UnityEngine;
using MotionCoreState = Arena.NetworkedMotion.NetworkedMotionState;

/// <summary>
/// The deterministic-motion + spawn-identity component of a projectile, on EVERY peer. Motion is
/// CLOSED-FORM from the spawn tick + the current trajectory segment (see
/// <see cref="NetworkedMotionProcessor"/>), so every peer renders position locally with nothing synced
/// per tick. The only synced state is the rare authoritative segment change (a ricochet/deflect), pushed
/// via SyncVars; a straight bullet never touches them after spawn.
///
/// This is the "spawn coordinator" of the projectile composition: it carries the
/// <see cref="ProjectileType"/>, owns the <see cref="MotionCoreState"/> the ServerCollision component
/// reads, and on the server seeds the sibling generic systems (<see cref="Health"/>, <see cref="Damage"/>)
/// from the type. Spawn data rides the predicted-spawn payload. (Successor to the motion half of the old
/// PawnProjectile.)
/// </summary>
public sealed class NetworkedMotion : NetworkBehaviour
{
    [SerializeField] private ProjectileType _type;

    // Authoritative segment, synced only when it CHANGES (a ricochet/deflect begins a new segment).
    private readonly SyncVar<Vector3> _segPos = new();
    private readonly SyncVar<Vector3> _segForward = new();
    private readonly SyncVar<float> _segSpeed = new();
    private readonly SyncVar<float> _segAge = new();

    // Spawn parameters carried via the (predicted-)spawn payload; set by the spawner before Spawn.
    public int SpawnOwnerId;
    public PreciseTick SpawnTick;
    public Vector3 SpawnPosition;
    public Vector3 SpawnForward;

    private MotionCoreState _state;
    private MotionConfig _config;
    private bool _initialized;

    public int Id => (int)ObjectId;
    public ProjectileType Type => _type;
    public MotionConfig Config => _config;

    /// <summary>The current motion state (read by ServerCollision for this and other projectiles' positions).</summary>
    public MotionCoreState State => _state;

    /// <summary>
    /// The one correct way to configure a projectile's spawn payload. Every spawner (PawnWeapon and
    /// ProjectileWorldProvider.Spawn for child projectiles) MUST call this before <c>ServerManager.Spawn</c>
    /// so no field — notably <see cref="SpawnTick"/>, the basis for deterministic age — is ever forgotten.
    /// </summary>
    public void SetupSpawn(int ownerId, PreciseTick tick, Vector3 pos, Vector3 dir)
    {
        SpawnOwnerId = ownerId;
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

    // ---- spawn payload (immutable initial data; seeds the predicting owner + all observers) ----

    public override void WritePayload(NetworkConnection connection, Writer writer)
    {
        base.WritePayload(connection, writer);
        writer.WriteInt32(SpawnOwnerId);
        writer.WritePreciseTick(SpawnTick);
        writer.WriteVector3(SpawnPosition);
        writer.WriteVector3(SpawnForward);
    }

    public override void ReadPayload(NetworkConnection connection, Reader reader)
    {
        base.ReadPayload(connection, reader);
        SpawnOwnerId = reader.ReadInt32();
        SpawnTick = reader.ReadPreciseTick();
        SpawnPosition = reader.ReadVector3();
        SpawnForward = reader.ReadVector3();
        ApplySpawnTransform();
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();

        _config = _type != null ? _type.ToMotionConfig() : default;
        float speed = _type != null ? _type.Speed : 0f;
        _state = MotionCoreState.Spawn(SpawnOwnerId, Id, SpawnPosition, SpawnForward, speed);
        _initialized = _type != null;

        ProjectileWorldProvider.Instance?.Register(Id, this);

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

        // Seed the sibling generic systems from this projectile's type.
        if (_type != null)
        {
            if (TryGetComponent<Health>(out var health))
                health.ServerInitMax(_type.MaxHealth);
            if (TryGetComponent<Damage>(out var damage))
                damage.ServerInit(_type.Damage);
        }
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _segPos.OnChange -= OnSegPosChanged;
        _segForward.OnChange -= OnSegForwardChanged;
        _segSpeed.OnChange -= OnSegSpeedChanged;
        _segAge.OnChange -= OnSegAgeChanged;
        ProjectileWorldProvider.Instance?.Unregister(Id);
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

    /// <summary>Server-authoritative VFX broadcast: runs the cosmetic impact VFX on every peer (+ server).</summary>
    [ObserversRpc(RunLocally = true)]
    public void BroadcastImpactVfx(ProjectileType vfxType, Vector3 point, Vector3 normal)
    {
        ProjectileWorldProvider.Instance?.SpawnVfx(vfxType != null ? vfxType : _type, point, normal);
    }

    // Server-authoritative segment changes reach clients as SyncVar changes; fold them into _state.
    private void OnSegPosChanged(Vector3 _, Vector3 next, bool asServer) { if (!asServer) _state.OriginPos = next; }
    private void OnSegForwardChanged(Vector3 _, Vector3 next, bool asServer) { if (!asServer) _state.OriginForward = next; }
    private void OnSegSpeedChanged(float _, float next, bool asServer) { if (!asServer) _state.OriginSpeed = next; }
    private void OnSegAgeChanged(float _, float next, bool asServer) { if (!asServer) _state.OriginAge = next; }

    public override void ResetState(bool asServer)
    {
        base.ResetState(asServer);
        _state = default;
        _initialized = false;
        SpawnOwnerId = 0;
        SpawnTick = default;
        SpawnPosition = Vector3.zero;
        SpawnForward = Vector3.zero;
    }
}
