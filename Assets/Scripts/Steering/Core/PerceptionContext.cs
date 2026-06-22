using FishNet.Object.Prediction;
using UnityEngine;

namespace Arena.Steering.Core
{
    /// <summary>
    /// Single surface for world reads strategies and target providers may need.
    /// Built and refreshed by MovementController each tick before strategy/provider runs.
    /// Spatial queries, navmesh queries, and alert state are stubbed at this stage and
    /// will be filled in once the vertical slice is verified.
    /// </summary>
    public class PerceptionContext
    {
        public Vector3 SelfPosition;
        public Vector3 SelfVelocity;
        public Quaternion SelfRotation;
        public PredictionRigidbody Body;

        /// <summary>Tick delta in seconds. Strategies must read dt from Tick(), not from this — kept here for providers that need it.</summary>
        public float DeltaTime;
    }
}
