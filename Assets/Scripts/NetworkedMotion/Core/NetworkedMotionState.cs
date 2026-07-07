using UnityEngine;

namespace Arena.NetworkedMotion
{
    /// <summary>
    /// Per-instance state of the generic networked-motion system, stored as a <b>trajectory segment</b>
    /// rather than an integrated position/velocity. Position and velocity are computed closed-form from
    /// the segment + elapsed time by the active motion strategy (see <see cref="IMotionStrategy"/>), so
    /// nothing redundant is stored per tick and a FishNet reconcile re-derives an EXACT position at any
    /// tick (no accumulated Euler drift). This is the generic successor of the projectile-specific
    /// <c>ProjectileState</c>: health and the collision dedup-guard have been moved out to the Health and
    /// ServerCollision systems respectively, leaving a pure motion segment.
    ///
    /// The segment (<see cref="OriginPos"/>/<see cref="OriginForward"/>/<see cref="OriginSpeed"/>/
    /// <see cref="OriginAge"/>) is reset on spawn and on every direction-changing event (ricochet/deflect,
    /// and every tick for homing) via <see cref="BeginSegment"/>. A mover that dies on contact never opens
    /// a second segment.
    ///
    /// <see cref="Id"/> is an identity field (network object id) the motion math itself ignores; it rides
    /// along here because the collision system reads it off the states it gathers from an overlap query to
    /// dedupe hits. Ownership is NOT here — it lives on a separate NetPlayerOwnership component.
    ///
    /// A value type with no behaviour and no engine deps beyond <see cref="Vector3"/>.
    /// </summary>
    [System.Serializable]
    public struct NetworkedMotionState
    {
        public int Id;        // resolves a cast/overlap hit back to this mover (identity; motion ignores it)

        // ---- Current trajectory segment ----
        public Vector3 OriginPos;       // where this segment began
        public Vector3 OriginForward;   // facing at segment start (normalized); valid even when Speed==0 (laser)
        public float   OriginSpeed;     // speed at segment start (may be 0 for a stationary laser)
        public float   OriginAge;       // Age (s since spawn) at which this segment began

        // ---- Whole-life ----
        public float Age;     // seconds since spawn; advanced by the shell each tick

        /// <summary>Seconds elapsed within the current segment.</summary>
        public readonly float SegmentElapsed => Age - OriginAge;

        /// <summary>A freshly-spawned mover at the origin pose.</summary>
        public static NetworkedMotionState Spawn(int id, Vector3 pos, Vector3 forward, float speed)
        {
            Vector3 fwd = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            return new NetworkedMotionState
            {
                Id = id,
                OriginPos = pos,
                OriginForward = fwd,
                OriginSpeed = speed,
                OriginAge = 0f,
                Age = 0f,
            };
        }

        /// <summary>
        /// Start a new trajectory segment at the current <see cref="Age"/>. Used by ricochet/deflect
        /// (new direction off an impact point) and by homing (re-aim each tick).
        /// </summary>
        public static NetworkedMotionState BeginSegment(NetworkedMotionState s, Vector3 originPos, Vector3 forward, float speed)
        {
            s.OriginPos = originPos;
            s.OriginForward = forward.sqrMagnitude > 0.0001f ? forward.normalized : s.OriginForward;
            s.OriginSpeed = speed;
            s.OriginAge = s.Age;
            return s;
        }

        /// <summary>
        /// A copy of this state with a different <see cref="OriginSpeed"/>. Used by an on-spawn speed buff
        /// that lands AFTER the state was already built: recomputing OriginSpeed is lossless during init
        /// because <see cref="Age"/> is 0 there, and every strategy's closed-form position at age 0 is the
        /// spawn point regardless of speed (see NetworkedMotion.SetSpeedMultiplier). Pure — no engine deps.
        /// </summary>
        public readonly NetworkedMotionState WithOriginSpeed(float speed)
        {
            NetworkedMotionState s = this;
            s.OriginSpeed = speed;
            return s;
        }

        /// <summary>
        /// Cheap linear position estimate from the segment, ignoring acceleration/gravity. Used for
        /// cross-mover queries where the other mover's exact motion model isn't available.
        /// </summary>
        public readonly Vector3 ApproxPosition() => OriginPos + OriginForward * (OriginSpeed * SegmentElapsed);
    }
}
