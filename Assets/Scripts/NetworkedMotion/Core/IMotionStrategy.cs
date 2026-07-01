using UnityEngine;

namespace Arena.NetworkedMotion
{
    /// <summary>The world-space segment a collision cast should probe for a mover on a given tick.</summary>
    public readonly struct MotionSweep
    {
        public readonly Vector3 From;
        public readonly Vector3 To;
        public MotionSweep(Vector3 from, Vector3 to) { From = from; To = to; }
    }

    /// <summary>
    /// A motion strategy: a closed-form command/strategy that operates on a <see cref="NetworkedMotionState"/>
    /// segment to yield position/velocity at an elapsed time, plus the collision sweep a hit-cast should probe
    /// this tick. This REPLACES the old <c>MotionKind</c> enum + <c>MotionSelector</c> switch: rather than
    /// serializing an enum and dispatching, a projectile type serializes the strategy INSTANCE itself (via
    /// <c>[SerializeReference]</c>), and that instance both carries its own tuning (acceleration/gravity/range)
    /// AND defines how it moves — the "operation on a context" concept applied to motion.
    ///
    /// Selected as data in the inspector; resolved locally and identically on every peer, so motion stays
    /// deterministic with nothing on the wire. Implementations are stateless flyweights (all per-instance state
    /// lives in <see cref="NetworkedMotionState"/>), so one instance is safely shared by every mover of a kind.
    /// Pure and EditMode-testable — no Physics/FishNet/engine lifecycle, only math.
    /// </summary>
    public interface IMotionStrategy
    {
        /// <summary>Position at <paramref name="elapsed"/> seconds into the current segment.</summary>
        Vector3 PositionAt(in NetworkedMotionState s, float elapsed);

        /// <summary>Velocity at <paramref name="elapsed"/> seconds into the current segment.</summary>
        Vector3 VelocityAt(in NetworkedMotionState s, float elapsed);

        /// <summary>
        /// The world segment a collision cast should sweep this tick. Moving strategies sweep the path
        /// travelled (<paramref name="prevPos"/> → <paramref name="newPos"/>); a stationary beam overrides
        /// this to cast forward over its range. Folds the old <c>if (Movement == Laser)</c> branch into the kind.
        /// </summary>
        MotionSweep CollisionSweep(in NetworkedMotionState s, Vector3 prevPos, Vector3 newPos);
    }
}
