using UnityEngine;

namespace Arena.Projectile
{
    /// <summary>
    /// Per-instance projectile state, stored as a <b>trajectory segment</b> rather than an
    /// integrated position/velocity. Position and velocity are computed closed-form from the
    /// segment + elapsed time by the movement strategy (see <see cref="IProjectileMovement"/>),
    /// so nothing redundant is stored per tick and a FishNet reconcile re-derives an EXACT
    /// position at any tick (no accumulated Euler drift).
    ///
    /// The segment (<see cref="OriginPos"/>/<see cref="OriginForward"/>/<see cref="OriginSpeed"/>/
    /// <see cref="OriginAge"/>) is reset on spawn and on every direction-changing event
    /// (ricochet/deflect, and every tick for homing) via <see cref="BeginSegment"/>. Ballistic
    /// projectiles that die on contact never open a second segment.
    ///
    /// A value type with no behaviour and no engine deps beyond <see cref="Vector3"/>.
    /// </summary>
    [System.Serializable]
    public struct ProjectileState
    {
        public int OwnerId;   // NetworkPlayer.ObjectId of the firer
        public int Id;        // resolves a cast/overlap hit back to this projectile

        // ---- Current trajectory segment ----
        public Vector3 OriginPos;       // where this segment began
        public Vector3 OriginForward;   // facing at segment start (normalized); valid even when Speed==0 (laser)
        public float   OriginSpeed;     // speed at segment start (may be 0 for a stationary laser)
        public float   OriginAge;       // Age (s since spawn) at which this segment began

        // ---- Whole-life ----
        public float Age;     // seconds since spawn; advanced by the shell each tick
        public int   Health;

        /// <summary>
        /// A projectile is alive exactly while it still has health. Death is therefore intrinsic to
        /// <see cref="Health"/> — a reaction kills the projectile simply by draining Health to zero
        /// (LoseHealth) or zeroing it outright (DealDamage's DespawnAfter). There is no separate Alive
        /// flag to keep in sync, which removes the class of bug where Alive and Health disagreed.
        /// The shell (PawnProjectile.ServerTick) despawns when this is false.
        /// </summary>
        public readonly bool Alive => Health > 0;

        // ---- Scratch ----
        public int LastHitProjectileId;  // projectile-channel one-shot guard, or -1

        /// <summary>Seconds elapsed within the current segment.</summary>
        public readonly float SegmentElapsed => Age - OriginAge;

        /// <summary>A freshly-spawned, alive projectile.</summary>
        public static ProjectileState Spawn(int ownerId, int id, Vector3 pos, Vector3 forward, float speed, int health)
        {
            Vector3 fwd = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            return new ProjectileState
            {
                OwnerId = ownerId,
                Id = id,
                OriginPos = pos,
                OriginForward = fwd,
                OriginSpeed = speed,
                OriginAge = 0f,
                Age = 0f,
                Health = health,
                LastHitProjectileId = -1,
            };
        }

        /// <summary>
        /// Start a new trajectory segment at the current <see cref="Age"/>. Used by ricochet/deflect
        /// (new direction off an impact point) and by homing (re-aim each tick).
        /// </summary>
        public static ProjectileState BeginSegment(ProjectileState s, Vector3 originPos, Vector3 forward, float speed)
        {
            s.OriginPos = originPos;
            s.OriginForward = forward.sqrMagnitude > 0.0001f ? forward.normalized : s.OriginForward;
            s.OriginSpeed = speed;
            s.OriginAge = s.Age;
            return s;
        }

        /// <summary>
        /// Cheap linear position estimate from the segment, ignoring acceleration/gravity. Used for
        /// cross-projectile queries where the other projectile's <see cref="ProjectileType"/> (and
        /// thus its exact movement model) isn't available.
        /// </summary>
        public readonly Vector3 ApproxPosition() => OriginPos + OriginForward * (OriginSpeed * SegmentElapsed);
    }
}
