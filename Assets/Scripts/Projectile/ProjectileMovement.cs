using FishNet;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using UnityEngine;

public partial class ProjectileMovement : NetworkBehaviour
{
    private struct ReplicateData : IReplicateData
    {
        private uint _tick;
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
        public void Dispose() { }
    }

    private struct ReconcileData : IReconcileData
    {
        public PredictionRigidbody PredictionRigidbody;
        public int Health;
        public Vector3 CurrentDirection;

        private uint _tick;
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
        public void Dispose() { }
    }

    protected ProjectileData data;
    protected ProjectileState state;
    private ProjectileHitbox _collision;
    private ProjectileLifecycle _lifecycle;

    private Rigidbody _rb;
    private PredictionRigidbody _predictionRb;
    private Vector3 _currentDirection;

    public Rigidbody Rigidbody => _rb;
    protected PredictionRigidbody PredictionRigidbody => _predictionRb;

    protected virtual void Awake()
    {
        data = GetComponent<ProjectileData>();
        state = GetComponent<ProjectileState>();
        _collision = GetComponent<ProjectileHitbox>();
        _lifecycle = GetComponent<ProjectileLifecycle>();
        _rb = GetComponent<Rigidbody>();
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation = RigidbodyInterpolation.Extrapolate;
        _predictionRb = new PredictionRigidbody();
        _predictionRb.Initialize(_rb);
    }

    public virtual void SetInitialMotion(float elapsed)
    {
        _currentDirection = state.StartDirection;

        Vector3 initialPosition = CalculatePosition(elapsed);
        Vector3 initialVelocity = CalculateVelocityAtTime(elapsed);

        _rb.freezeRotation = true;
        _rb.position = initialPosition;
        transform.position = initialPosition;
        _predictionRb.Velocity(initialVelocity);

        if (initialVelocity.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(initialVelocity);
    }

    public Vector3 GetVelocity()
    {
        return _predictionRb.Rigidbody.linearVelocity;
    }

    public void SetVelocity(Vector3 newVelocity)
    {
        _predictionRb.Velocity(newVelocity);
        _currentDirection = newVelocity.normalized;
    }

    public override void OnStartNetwork()
    {
        base.TimeManager.OnPostTick += TimeManager_OnPostTick;
    }

    public override void OnStopNetwork()
    {
        base.TimeManager.OnPostTick -= TimeManager_OnPostTick;
    }

    private void TimeManager_OnPostTick()
    {
        RunReplicate(default);
        if (IsServerInitialized)
            CreateReconcile();
    }

    [Replicate]
    private void RunReplicate(ReplicateData replicateData, ReplicateState replicateState = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
    {
        if (state.Health <= 0)
            return;

        ApplyForces();
        _predictionRb.Simulate();

        if (_collision != null && _collision.UsesManualCollisionCheck)
            _collision.CheckCollision(_rb.linearVelocity, Time.fixedDeltaTime);

        float elapsed = (float)InstanceFinder.TimeManager.TimePassed(state.PreciseTick);
        if (elapsed > data.lifetime)
            _lifecycle.Kill();

        Vector3 vel = _rb.linearVelocity;
        if (vel.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(vel);
    }

    protected virtual void ApplyForces()
    {
        if (data.acceleration != 0f)
            _predictionRb.AddForce(_currentDirection * data.acceleration, ForceMode.Acceleration);
    }

    public override void CreateReconcile()
    {
        var rd = new ReconcileData
        {
            PredictionRigidbody = _predictionRb,
            Health = state.Health,
            CurrentDirection = _currentDirection
        };
        RunReconcile(rd, Channel.Unreliable);
    }

    [Reconcile]
    private void RunReconcile(ReconcileData reconcileData, Channel channel)
    {
        _predictionRb.Reconcile(reconcileData.PredictionRigidbody);
        state.Health = reconcileData.Health;
        _currentDirection = reconcileData.CurrentDirection;
    }

    public override void ResetState(bool asServer)
    {
        base.ResetState(asServer);
        _currentDirection = Vector3.zero;

        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        _predictionRb?.ClearPendingForces();
    }

    protected virtual Vector3 CalculatePosition(float time)
    {
        return state.StartPosition
             + (data.speed * time * state.StartDirection)
             + (0.5f * data.acceleration * time * time * state.StartDirection);
    }

    protected virtual Vector3 CalculateVelocityAtTime(float time)
    {
        return state.StartDirection * (data.speed + data.acceleration * time);
    }
}
