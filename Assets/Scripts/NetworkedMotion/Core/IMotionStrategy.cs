using UnityEngine;

namespace Arena.NetworkedMotion
{
    /// <summary>
    /// A motion strategy (Strategy pattern — three real runtime implementations Linear/Arc/Laser,
    /// selected by <see cref="MotionKind"/>). Motion is expressed CLOSED-FORM:
    /// <see cref="PositionAt"/>/<see cref="VelocityAt"/> evaluate the trajectory at an elapsed time within
    /// the current segment, so position is exact at any tick (every peer derives it deterministically from
    /// the spawn data; nothing is integrated or synced per frame). All methods are pure and deterministic.
    /// </summary>
    public interface IMotionStrategy
    {
        /// <summary>Position at <paramref name="elapsed"/> seconds into the current segment.</summary>
        Vector3 PositionAt(in NetworkedMotionState s, in MotionConfig c, float elapsed);

        /// <summary>Velocity at <paramref name="elapsed"/> seconds into the current segment.</summary>
        Vector3 VelocityAt(in NetworkedMotionState s, in MotionConfig c, float elapsed);
    }
}
