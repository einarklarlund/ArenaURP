using UnityEngine;

namespace Arena.NetworkedMotion
{
    /// <summary>
    /// The pure functional core of the networked-motion system: closed-form position/velocity from a
    /// <see cref="NetworkedMotionState"/> segment + its <see cref="MotionConfig"/>. Every peer evaluates
    /// this locally and deterministically; nothing is integrated or synced per tick. EditMode-testable —
    /// no Physics, no FishNet, no engine lifecycle.
    /// </summary>
    public static class NetworkedMotionProcessor
    {
        /// <summary>Current position: the active strategy evaluated at <see cref="NetworkedMotionState.SegmentElapsed"/>.</summary>
        public static Vector3 PositionAt(in NetworkedMotionState s, in MotionConfig c) =>
            MotionSelector.For(c.Movement).PositionAt(in s, in c, s.SegmentElapsed);

        /// <summary>Current velocity: the active strategy evaluated at <see cref="NetworkedMotionState.SegmentElapsed"/>.</summary>
        public static Vector3 VelocityAt(in NetworkedMotionState s, in MotionConfig c) =>
            MotionSelector.For(c.Movement).VelocityAt(in s, in c, s.SegmentElapsed);

        /// <summary>Position at an explicit elapsed time within the current segment (cast endpoints / sampling).</summary>
        public static Vector3 PositionAt(in NetworkedMotionState s, in MotionConfig c, float elapsed) =>
            MotionSelector.For(c.Movement).PositionAt(in s, in c, elapsed);
    }
}
