using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arena.Projectile
{
    /// <summary>
    /// The functional core of the projectile system: pure, deterministic functions the imperative
    /// shell (PawnProjectile) drives in phases each tick. The shell does ALL I/O — it casts, overlaps,
    /// and looks up targets, passing the RESULTS in as data, and it reads the emitted
    /// <see cref="WorldEffect"/> list back out and performs the effects. The core
    /// never touches Physics, FishNet, or the engine, which is what keeps it EditMode-testable and
    /// reconcile-correct.
    ///
    /// Per-tick order the (server) shell follows:
    ///   1. shell derives Age from the spawn tick, computes prevPos/newPos, culls on lifetime
    ///   2. shell casts prevPos->newPos (or the laser beam) -> WorldHit
    ///   3. ResolveWorldHit      (owner-safety, snap, OnEnemy/OnSurface reactions)
    ///   4. shell overlaps newPos -> other projectile states
    ///   5. ResolveProjectileHits(dedupe, owner filter, OnProjectile reactions)
    ///   6. shell applies effects (server-only, no gating) and despawns if !Alive
    /// </summary>
    public static class ProjectileStepProcessor
    {
        private const float Eps = 0.0001f;

        // ---- closed-form helpers the shell uses for cast endpoints / view ----

        public static Vector3 PositionAt(in ProjectileState s, ProjectileType type) =>
            MovementSelector.For(type.Movement).PositionAt(in s, in type, s.SegmentElapsed);

        public static Vector3 VelocityAt(in ProjectileState s, ProjectileType type) =>
            MovementSelector.For(type.Movement).VelocityAt(in s, in type, s.SegmentElapsed);

        /// <summary>
        /// Whether the projectile channel is worth running this tick — the type or the owner contributes
        /// OnProjectile reactions. Lets the shell skip the overlap query without itself reasoning about
        /// which reactions exist.
        /// </summary>
        public static bool WantsProjectileChannel(ProjectileType type, in OwnerReactions owner)
        {
            if (type.OnProjectile != null && type.OnProjectile.Count > 0)
                return true;
            var op = owner.For(HitCategory.Projectile);
            return op != null && op.Count > 0;
        }

        // ---- world channel ----

        /// <summary>
        /// Resolve a world hit the shell already cast. Owner hits are ignored (owner safety + muzzle
        /// overlap). Otherwise the projectile is snapped to the impact point (a zero-length segment)
        /// and the matching reaction list runs — OnEnemy if a player was hit, else OnSurface.
        /// </summary>
        public static ProjectileState ResolveWorldHit(ProjectileState s, ProjectileType type, in WorldHit hit, List<WorldEffect> effects, in OwnerReactions owner = default)
        {
            if (!s.Alive)
                return s;

            bool hitOwner = hit.PlayerId >= 0 && hit.PlayerId == s.OwnerId;
            if (hitOwner)
                return s;

            Vector3 vel = VelocityAt(in s, type);
            Vector3 dir = vel.sqrMagnitude > Eps ? vel.normalized : s.OriginForward;

            // Place the projectile at the impact point (begin a zero-length segment, keeping facing).
            s = ProjectileState.BeginSegment(s, hit.Point, dir, vel.magnitude);

            bool isPlayer = hit.PlayerId >= 0;
            var info = new HitInfo
            {
                Category = isPlayer ? HitCategory.Enemy : HitCategory.Surface,
                Point = hit.Point,
                Normal = hit.Normal,
                IncomingVelocity = vel,
                Direction = dir,
                OtherPlayerId = hit.PlayerId,
                OtherProjectileId = -1,
                OtherOwnerId = -1,
                OtherForward = Vector3.zero,
                OtherVelocity = Vector3.zero,
            };

            // Run the matching authored reaction list. Damage on an enemy hit is NOT applied implicitly
            // here — it is authored as a DealDamage reaction in the type's OnEnemy list (single source of
            // truth, same as every other reaction). The processor only ever runs the list; it never
            // appends to it (that shared flyweight list is one asset for every bullet of this kind).
            RunReactions(isPlayer ? type.OnEnemy : type.OnSurface, in info, ref s, effects);

            // Owner-contributed reactions for THIS hit's category (e.g. a passive that makes the owner's
            // bullets do extra damage / a debuff on enemy hit). The owner's reactions arrive as data; the
            // processor — which already decided the category above — selects the matching list. They run
            // for this hit even if the type's reactions already despawned the projectile (the hit occurred,
            // so the owner's on-hit effect should still land). Same shared <see cref="WorldEffect"/> vocab.
            RunReactions(owner.For(info.Category), in info, ref s, effects);

            return s;
        }

        // ---- projectile channel ----

        /// <summary>
        /// Resolve overlaps with other projectiles the shell already gathered. Resolves only across
        /// different owners, dedupes each pair to one-shot-on-enter via <see cref="ProjectileState.LastHitProjectileId"/>
        /// (matching the old NetworkCollision OnEnter), and runs OnProjectile reactions.
        /// </summary>
        public static ProjectileState ResolveProjectileHits(ProjectileState s, ProjectileType type, ReadOnlySpan<ProjectileState> overlaps, List<WorldEffect> effects, in OwnerReactions owner = default)
        {
            List<IHitReaction> ownerReactions = owner.For(HitCategory.Projectile);
            bool hasTypeReactions = type.OnProjectile != null && type.OnProjectile.Count > 0;
            bool hasOwnerReactions = ownerReactions != null && ownerReactions.Count > 0;
            if (!s.Alive || (!hasTypeReactions && !hasOwnerReactions))
                return s;

            Vector3 selfPos = PositionAt(in s, type);
            Vector3 vel = VelocityAt(in s, type);
            Vector3 dir = vel.sqrMagnitude > Eps ? vel.normalized : s.OriginForward;

            int firstHandled = -1;
            bool guardStillOverlapping = false;

            for (int i = 0; i < overlaps.Length; i++)
            {
                ProjectileState other = overlaps[i];
                if (other.Id == s.Id)
                    continue;
                if (other.OwnerId == s.OwnerId)
                    continue; // only across different owners
                if (other.Id == s.LastHitProjectileId)
                {
                    guardStillOverlapping = true; // stay suppressed until they separate
                    continue;
                }

                Vector3 otherPos = other.ApproxPosition();
                Vector3 normal = (selfPos - otherPos).sqrMagnitude > Eps
                    ? (selfPos - otherPos).normalized
                    : -dir;

                var info = new HitInfo
                {
                    Category = HitCategory.Projectile,
                    Point = selfPos,
                    Normal = normal,
                    IncomingVelocity = vel,
                    Direction = dir,
                    OtherPlayerId = -1,
                    OtherProjectileId = other.Id,
                    OtherOwnerId = other.OwnerId,
                    OtherForward = other.OriginForward,
                    OtherVelocity = other.OriginForward * other.OriginSpeed,
                };

                // Run the authored OnProjectile reactions only. (Do NOT append a DealDamage to
                // type.OnProjectile — same shared-flyweight hazard as ResolveWorldHit above; and damage
                // here is a no-op anyway since the projectile channel carries no OtherPlayerId.)
                RunReactions(type.OnProjectile, in info, ref s, effects);
                RunReactions(ownerReactions, in info, ref s, effects); // owner-contributed, server-side lookup; runs for this hit
                if (firstHandled < 0)
                    firstHandled = other.Id;
                if (!s.Alive)
                    break;
            }

            if (firstHandled >= 0)
                s.LastHitProjectileId = firstHandled;
            else if (!guardStillOverlapping)
                s.LastHitProjectileId = -1;

            return s;
        }

        private static void RunReactions(List<IHitReaction> reactions, in HitInfo hit, ref ProjectileState s, List<WorldEffect> effects)
        {
            if (reactions == null)
                return;
            for (int i = 0; i < reactions.Count; i++)
            {
                reactions[i]?.Apply(in hit, ref s, effects);

                // Death is intrinsic to Health now (ProjectileState.Alive => Health > 0): a reaction
                // kills the projectile purely by draining its Health to zero, so once it's no longer
                // alive, later reactions in this list don't run.
                if (!s.Alive)
                    break;
            }
        }
    }
}
