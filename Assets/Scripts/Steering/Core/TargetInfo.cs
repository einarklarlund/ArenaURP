using UnityEngine;

namespace Arena.Steering.Core
{
    /// <summary>
    /// Result of an ITargetProvider query. Always returned; HasTarget tells the strategy
    /// whether Position/Velocity/Transform are meaningful. Strategies decide how to react
    /// to a missing target (idle, drift, fall back to wander, etc.).
    /// </summary>
    public struct TargetInfo
    {
        public bool HasTarget;
        public Transform Transform;
    }
}
