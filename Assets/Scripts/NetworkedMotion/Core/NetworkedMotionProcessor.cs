using UnityEngine;

namespace Arena.NetworkedMotion
{
    /// <summary>
    /// Thin pure entry point for closed-form motion: evaluate a selected <see cref="IMotionStrategy"/> against
    /// a <see cref="NetworkedMotionState"/> segment. Every peer evaluates locally and deterministically;
    /// nothing is integrated or synced per tick. (The old <c>MotionKind</c>→strategy switch is gone — the
    /// strategy is now selected as data on the type and passed in directly.) EditMode-testable.
    /// </summary>
    public static class NetworkedMotionProcessor
    {
        /// <summary>Current position: the strategy evaluated at <see cref="NetworkedMotionState.SegmentElapsed"/>.</summary>
        public static Vector3 PositionAt(IMotionStrategy strategy, in NetworkedMotionState s) =>
            strategy.PositionAt(in s, s.SegmentElapsed);

        /// <summary>Current velocity: the strategy evaluated at <see cref="NetworkedMotionState.SegmentElapsed"/>.</summary>
        public static Vector3 VelocityAt(IMotionStrategy strategy, in NetworkedMotionState s) =>
            strategy.VelocityAt(in s, s.SegmentElapsed);

        /// <summary>Position at an explicit elapsed time within the current segment (cast endpoints / sampling).</summary>
        public static Vector3 PositionAt(IMotionStrategy strategy, in NetworkedMotionState s, float elapsed) =>
            strategy.PositionAt(in s, elapsed);
    }
}
