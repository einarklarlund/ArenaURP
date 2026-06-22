using Arena.Steering.Core;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Utility.Template;
using GameKit.Dependencies.Utilities;
using UnityEngine;

namespace Arena.Steering
{
    /// <summary>
    /// Thin glue between the steering-strategy layer and FishNet's PredictionRigidbody.
    /// Subscribes to TimeManager ticks, runs the active strategy on the server inside
    /// [Replicate], and reconciles rigidbody state to clients via [Reconcile].
    ///
    /// Does NOT arbitrate physics writes. Strategies write to ctx.Body directly; the
    /// controller just simulates pending forces and broadcasts state.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class MovementController : TickNetworkBehaviour
    {
        public struct ReplicateData : IReplicateData
        {
            private uint _tick;
            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        public struct ReconcileData : IReconcileData
        {
            public PredictionRigidbody Body;
            private uint _tick;

            public ReconcileData(PredictionRigidbody body) { Body = body; _tick = 0; }
            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        [SerializeField] private MovementStrategySO _initialMovement;
        [SerializeField] private TargetProviderSO _initialTargetProvider;

        private Rigidbody _rigidbody;
        private PredictionRigidbody _predRb;
        private PerceptionContext _ctx;

        private IMovementStrategy _strategy;
        private ITargetProvider _provider;

        public IMovementStrategy CurrentStrategy => _strategy;
        public ITargetProvider CurrentProvider => _provider;
        public PerceptionContext Perception => _ctx;
        public PredictionRigidbody PredictedBody => _predRb;

        private void Awake()
        {
            _predRb = ResettableObjectCaches<PredictionRigidbody>.Retrieve();
            _predRb = new();
            _rigidbody = GetComponent<Rigidbody>();
            _predRb.Initialize(_rigidbody);
            _ctx = new()
            {
                Body = _predRb
            };
        }

        private void OnDestroy()
        {
            ResettableObjectCaches<PredictionRigidbody>.StoreAndDefault(ref _predRb);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            TimeManager.OnTick += TimeManager_OnTick;
            TimeManager.OnPostTick += TimeManager_OnPostTick;

            if (_initialMovement != null)
                _strategy = _initialMovement.CreateRuntime();
            if (_initialTargetProvider != null)
                _provider = _initialTargetProvider.CreateRuntime();
        }

        public override void OnStopNetwork()
        {
            TimeManager.OnPostTick -= TimeManager_OnPostTick;
        }

        /// <summary>
        /// Server-side hot-swap. Pass null to clear. Safe to call from FSM state transitions.
        /// </summary>
        public void SetMovementStrategy(IMovementStrategy strategy)
        {
            if (!IsServerInitialized) return;
            _strategy = strategy;
        }

        public void SetTargetProvider(ITargetProvider provider)
        {
            if (!IsServerInitialized) return;
            _provider = provider;
        }

        protected override void TimeManager_OnTick()
        {
            PerformReplicate(default);
        }

        protected override void TimeManager_OnPostTick()
        {
            CreateReconcile();
        }

        public override void CreateReconcile()
        {
            ReconcileData rd = new(_predRb);
            PerformReconcile(rd);
        }

        [Replicate]
        private void PerformReplicate(
            ReplicateData rd,
            ReplicateState state = ReplicateState.Invalid,
            Channel channel = Channel.Unreliable)
        {
            if (_strategy != null)
            {
                _ctx.SelfPosition = _rigidbody.position;
                _ctx.SelfVelocity = _rigidbody.linearVelocity;
                _ctx.SelfRotation = _rigidbody.rotation;
                _ctx.DeltaTime = (float)TimeManager.TickDelta;

                TargetInfo target = _provider != null ? _provider.GetTargetInfo(_ctx) : default;
                _strategy.Tick(_ctx, target);
            }

            _predRb.Simulate();
        }

        [Reconcile]
        private void PerformReconcile(ReconcileData rd, Channel channel = Channel.Unreliable)
        {
            _predRb.Reconcile(rd.Body);
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;
            if (_strategy == null) return;

            _ctx.SelfPosition = _rigidbody != null ? _rigidbody.position : transform.position;
            _ctx.SelfVelocity = _rigidbody != null ? _rigidbody.linearVelocity : Vector3.zero;

            TargetInfo target = _provider != null ? _provider.GetTargetInfo(_ctx) : default;
            _strategy.DrawGizmos(_ctx, target);
            _provider?.DrawGizmos(_ctx);
        }
    }
}
