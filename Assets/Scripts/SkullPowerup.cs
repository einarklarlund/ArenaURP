using System;
using System.Collections;
using FishNet;
using FishNet.Component.Prediction;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkTrigger))]
public class SkullPowerup : NetworkBehaviour
{
    [Header("Skull spawning")]
    [SerializeField] private NetworkObject skullProjectilePrefab;
    [SerializeField] private int skullCount = 7;
    [SerializeField] private float spawnDuration = 4f;
    [SerializeField] private float skullSpawnForwardOffset = 1.5f;
    [SerializeField] private float skullSpawnUpOffset = 1f;

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

        private uint _tick;
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
        public void Dispose() { }
    }

    private Rigidbody _rb;
    private PredictionRigidbody _predictionRb;
    private NetworkTrigger _trigger;
    private bool _consumed;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _predictionRb = new PredictionRigidbody();
        _predictionRb.Initialize(_rb);

        _trigger = GetComponent<NetworkTrigger>();
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        TimeManager.OnPostTick += TimeManager_OnPostTick;
        _trigger.OnEnter += OnTriggerEntered;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        TimeManager.OnPostTick -= TimeManager_OnPostTick;
        _trigger.OnEnter -= OnTriggerEntered;
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
        _predictionRb.Simulate();
    }

    public override void CreateReconcile()
    {
        var rd = new ReconcileData { PredictionRigidbody = _predictionRb };
        RunReconcile(rd, Channel.Unreliable);
    }

    [Reconcile]
    private void RunReconcile(ReconcileData reconcileData, Channel channel)
    {
        _predictionRb.Reconcile(reconcileData.PredictionRigidbody);
    }

    public override void ResetState(bool asServer)
    {
        base.ResetState(asServer);
        _consumed = false;

        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        _predictionRb?.ClearPendingForces();
    }

    private void OnTriggerEntered(Collider other, uint tick)
    {
        if (!IsServerInitialized) return;
        if (_consumed) return;

        if (!other.TryGetComponent<Pawn>(out var pawn)) return;
        if (pawn.ControllingPlayer.Value == null) return;

        _consumed = true;
        StartCoroutine(SpawnSkullsRoutine(pawn));
    }

    private IEnumerator SpawnSkullsRoutine(Pawn pawn)
    {
        var firer = pawn.ControllingPlayer.Value;
        Vector3 lastForward = pawn.transform.forward;
        Vector3 lastPosition = pawn.transform.position;

        // Hide visuals and disable collisions while skulls are spawning so the
        // powerup can't be picked up again. The object stays alive so this
        // coroutine can run to completion.
        foreach (var renderer in GetComponentsInChildren<Renderer>())
            renderer.enabled = false;
        foreach (var collider in GetComponentsInChildren<Collider>())
            collider.enabled = false;

        float interval = skullCount > 1 ? spawnDuration / (skullCount - 1) : 0f;
        for (int i = 0; i < skullCount; i++)
        {
            if (pawn != null && pawn.IsSpawned)
            {
                lastForward = pawn.transform.forward;
                lastPosition = pawn.transform.position;
            }

            SpawnSkull(lastPosition, lastForward, firer);

            if (i < skullCount - 1)
                yield return new WaitForSeconds(interval);
        }

        Despawn();
    }

    private void SpawnSkull(Vector3 pawnPosition, Vector3 pawnForward, NetworkPlayer firer)
    {
        Vector3 spawnPos = pawnPosition + pawnForward * skullSpawnForwardOffset + Vector3.up * skullSpawnUpOffset;

        NetworkObject nob = InstanceFinder.NetworkManager.GetPooledInstantiated(
            skullProjectilePrefab,
            spawnPos,
            Quaternion.LookRotation(pawnForward),
            false
        );

        var st = nob.GetComponent<ProjectileState>();
        var dt = nob.GetComponent<ProjectileData>();

        st.PreciseTick    = InstanceFinder.TimeManager.GetPreciseTick(TickType.Tick);
        st.StartDirection = pawnForward;
        st.StartPosition  = spawnPos;
        st.ID             = Guid.NewGuid().ToString();
        st.Firer          = firer;
        st.Health         = dt.MaxHealth;

        InstanceFinder.ServerManager.Spawn(nob);
    }
}
