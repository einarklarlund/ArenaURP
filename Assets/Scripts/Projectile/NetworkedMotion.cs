using Arena.NetworkedMotion;
using FishNet.Connection;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Serializing;
using MackySoft.SerializeReferenceExtensions;
using UnityEngine;
using MotionCoreState = Arena.NetworkedMotion.NetworkedMotionState;

/// <summary>
/// Generic deterministic-motion component, on EVERY peer. Motion is CLOSED-FORM from the spawn tick + the
/// current trajectory segment (see <see cref="NetworkedMotionProcessor"/>), so every peer renders position
/// locally with nothing synced per tick; the only synced state is the rare authoritative segment change
/// (a ricochet/deflect), pushed via SyncVars.
///
/// It is FULLY DECOUPLED from the projectile concept: it holds no <c>ProjectileType</c>. Its
/// <see cref="IMotionStrategy"/> + initial speed are AUTHORED ON THE PREFAB (prefab-per-kind), so every peer
/// has them baked into its identical copy of the prefab — the strategy is NOT on the wire; only the per-spawn
/// anchor (tick/pos/dir) rides the payload. Being serialized prefab data, the config is runtime-tunable per
/// instance and survives pooling for free. It knows nothing of Health/Damage/ownership/VFX.
/// </summary>
public sealed class NetworkedMotion : NetworkBehaviour
{
    [Header("Motion (per-kind config, authored on the prefab)")]
    [SerializeReference, SubclassSelector]
    [Tooltip("The motion strategy this mover evaluates (select a kind; it carries its own tuning).")]
    public IMotionStrategy Motion = new LinearMotion();
    [Tooltip("Speed of the initial spawn segment.")]
    public float InitialSpeed = 50f;

    // Authoritative segment, synced only when it CHANGES (a ricochet/deflect begins a new segment).
    private readonly SyncVar<Vector3> _segPos = new();
    private readonly SyncVar<Vector3> _segForward = new();
    private readonly SyncVar<float> _segSpeed = new();
    private readonly SyncVar<float> _segAge = new();

    // Per-spawn anchor. On the INITIATOR (server bot-spawn or the predicting client) these are self-derived in
    // OnStartNetwork from the instantiate pose + the current tick — FishNet invokes OnStartNetwork
    // synchronously inside the Spawn call, before WritePayload. On RECEIVERS they arrive via ReadPayload
    // (which runs before OnStartNetwork). No spawner pushes them in. Ownership lives on NetPlayerOwnership.
    public PreciseTick SpawnTick;
    public Vector3 SpawnPosition;
    public Vector3 SpawnForward;

    private MotionCoreState _state;
    private bool _initialized;
    // Per-spawn buff layer (§ on-spawn buffs): a multiplier folded onto InitialSpeed, kept SEPARATE from the
    // baseline InitialSpeed so pooled reuse by a different owner never compounds. Reset to identity in ResetState.
    private float _speedMultiplier = 1f;
    // Set by ReadPayload: this peer RECEIVED the spawn. Still false at OnStartNetwork = this peer INITIATED
    // the spawn, so the anchor is self-derived there instead.
    private bool _payloadApplied;

    public int Id => (int)ObjectId;

    /// <summary>The motion strategy this mover evaluates (read by effects for live velocity, by ServerCollision for the cast sweep).</summary>
    public IMotionStrategy Strategy => Motion;

    /// <summary>The current motion state (read by ServerCollision for this and other movers' positions).</summary>
    public MotionCoreState State => _state;

    private void ApplySpawnTransform()
    {
        transform.position = SpawnPosition;
        if (SpawnForward.sqrMagnitude > 1e-4f)
            transform.rotation = Quaternion.LookRotation(SpawnForward);
    }

    // ---- spawn anchor payload (tick/pos/dir). Initiator: self-derived in OnStartNetwork, which FishNet
    //      runs BEFORE WritePayload on the spawning side. Receivers: ReadPayload fills the fields BEFORE
    //      OnStartNetwork. Config (strategy/speed) is serialized prefab data every peer already has, so it
    //      is deliberately NOT on the wire. ----

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
        _payloadApplied = true;
        ApplySpawnTransform();
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();

        // No payload was read, so this peer INITIATED the spawn (server bot-fire, or the predicting client):
        // derive the anchor from the instantiate pose and the current tick. FishNet runs this callback
        // synchronously inside the Spawn call and only writes the payload afterwards, so receivers get
        // exactly what is derived here.
        if (!_payloadApplied)
        {
            SpawnPosition = transform.position;
            SpawnForward = transform.forward;
            SpawnTick = TimeManager.GetPreciseTick(TickType.Tick);
        }

        if (Motion == null)
        {
            Debug.LogError($"{nameof(NetworkedMotion)} on '{name}' has no Motion strategy authored on the " +
                           "prefab. Falling back to LinearMotion.", this);
            Motion = new LinearMotion();
        }

        // Effective spawn speed = baseline × per-spawn buff layer. An on-spawn buff (SpawnContributionReceiver) may have
        // called SetSpeedMultiplier before this — its OnStartNetwork can run either side of ours — so fold the
        // multiplier in here rather than assuming it arrives later (see SetSpeedMultiplier for the after-case).
        _state = MotionCoreState.Spawn(Id, SpawnPosition, SpawnForward, InitialSpeed * _speedMultiplier);
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
        if (Motion == null)
            return;
        MotionCoreState s = _state;
        s.Age = ElapsedSinceSpawn();
        transform.position = NetworkedMotionProcessor.PositionAt(Motion, in s);
        Vector3 vel = NetworkedMotionProcessor.VelocityAt(Motion, in s);
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
        return NetworkedMotionProcessor.PositionAt(Motion, in s);
    }

    /// <summary>Server: set the working age so <see cref="State"/> reflects the current tick for collision queries.</summary>
    public void ServerSetAge(float age) => _state.Age = age;

    /// <summary>
    /// Apply an on-spawn speed buff as a multiplier over the baseline <see cref="InitialSpeed"/>. ORDER-INDEPENDENT
    /// (house rule for shared init across siblings with no guaranteed OnStartNetwork order): safe to call before
    /// OnStartNetwork (the multiplier is stored and folded in when <see cref="_state"/> is built) or after (the
    /// already-built OriginSpeed is recomputed in place — lossless, since Age is 0 throughout init and every
    /// strategy's closed-form position at age 0 is the spawn point regardless of speed).
    ///
    /// On the server this is invoked from a sibling's OnStartNetwork, and FishNet runs ALL OnStartNetwork callbacks
    /// before any OnStartServer, so the buffed OriginSpeed is already in <see cref="_state"/> when
    /// <see cref="OnStartServer"/> seeds the segment SyncVars — the buffed value ships in the spawn message with
    /// nothing extra on the wire. It is a per-spawn buff layer, reset to identity in <see cref="ResetState"/>.
    /// </summary>
    public void SetSpeedMultiplier(float multiplier)
    {
        _speedMultiplier = multiplier;
        if (_initialized)
            _state = _state.WithOriginSpeed(InitialSpeed * _speedMultiplier);
    }

    /// <summary>The current per-spawn speed multiplier (identity 1). Lets an on-spawn buff COMPOSE onto it
    /// (<c>SetSpeedMultiplier(SpeedMultiplier * buff)</c>) so several owned buff passives multiply rather than
    /// overwrite; the setter itself stays last-write-wins.</summary>
    public float SpeedMultiplier => _speedMultiplier;

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
    //
    // These also carry the SPAWN self-heal for on-spawn speed buffs: the seg SyncVars ride the spawn message with
    // their server-seeded (buffed) values, and FishNet caches their initial OnChange until after Initialize, then
    // flushes them (InvokeOnStartSyncTypeCallbacks, asServer:false) — AFTER OnStartNetwork has built _state and
    // subscribed these handlers. So on every receiving client the server-authoritative OriginSpeed lands in _state
    // during the SAME spawn iteration, before the next Update/Render. If the client applied the same buff locally
    // (normal case) the fold is an idempotent no-op; if the client MISSED the passive, the fold corrects speed
    // immediately — no persistent divergence, no visible pop. The server value always wins on receivers. This
    // safety net exists ONLY for properties with a sync channel (speed) — scale/tint have none and keep their
    // per-peer spawn-instant snapshot by design.
    private void OnSegPosChanged(Vector3 _, Vector3 next, bool asServer) { if (!asServer) _state.OriginPos = next; }
    private void OnSegForwardChanged(Vector3 _, Vector3 next, bool asServer) { if (!asServer) _state.OriginForward = next; }
    private void OnSegSpeedChanged(float _, float next, bool asServer) { if (!asServer) _state.OriginSpeed = next; }
    private void OnSegAgeChanged(float _, float next, bool asServer) { if (!asServer) _state.OriginAge = next; }

    public override void ResetState(bool asServer)
    {
        base.ResetState(asServer);
        // Reset per-spawn + working state only. Motion / InitialSpeed are serialized prefab config — pooling
        // never touches them, so a reused instance keeps its kind's tuning (any runtime mutation of them is
        // deliberate per-instance state and persists across pooled reuse by design).
        _state = default;
        _initialized = false;
        _payloadApplied = false;
        _speedMultiplier = 1f;   // per-spawn buff layer back to identity; baseline InitialSpeed is left untouched
        SpawnTick = default;
        SpawnPosition = Vector3.zero;
        SpawnForward = Vector3.zero;
    }
}
