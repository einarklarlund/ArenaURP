using UnityEngine;
using Arena.NetworkedMotion;

namespace Arena.ServerCollision
{
    /// <summary>
    /// The pure functional core of the server-authoritative collision system: it CLASSIFIES hits the shell
    /// already cast/overlapped and decides whether a hit should react, but it performs no effects and
    /// touches no engine API. The shell (the ServerCollision component) does the I/O — casting,
    /// overlapping, resolving ids to GameObjects, and running the Unity-coupled hit reactions — using the
    /// <see cref="HitInfo"/> this produces. EditMode-testable.
    ///
    /// Two channels, mirroring the two broadphases the shell queries:
    ///   • World channel  — a single forward cast against Stage|Players. Owner hits are ignored (owner
    ///     safety + muzzle overlap); otherwise the hit is Enemy (a player) or Surface.
    ///   • Projectile channel — an overlap sphere against other movers. Resolves only across different
    ///     owners and dedupes each pair to one-shot-on-enter via a caller-held guard id.
    /// </summary>
    public static class ServerCollisionProcessor
    {
        private const float Eps = 0.0001f;

        /// <summary>The "no guard" sentinel for the projectile-channel one-shot-on-enter dedupe.</summary>
        public const int NoGuard = -1;

        /// <summary>
        /// Classify a world cast hit. Returns false when the hit should be ignored (the mover hit its own
        /// owner — owner safety + muzzle overlap). Otherwise fills <paramref name="info"/> with the
        /// Enemy/Surface classification and impact geometry.
        /// </summary>
        public static bool TryClassifyWorldHit(in NetworkedMotionState self, Vector3 incomingVelocity, in WorldHit hit, out HitInfo info)
        {
            info = default;
            bool hitOwner = hit.PlayerId >= 0 && hit.PlayerId == self.OwnerId;
            if (hitOwner)
                return false;

            Vector3 dir = incomingVelocity.sqrMagnitude > Eps ? incomingVelocity.normalized : self.OriginForward;
            bool isPlayer = hit.PlayerId >= 0;
            info = new HitInfo
            {
                Category = isPlayer ? HitCategory.Enemy : HitCategory.Surface,
                Point = hit.Point,
                Normal = hit.Normal,
                IncomingVelocity = incomingVelocity,
                Direction = dir,
                OtherPlayerId = hit.PlayerId,
                OtherProjectileId = -1,
                OtherOwnerId = -1,
                OtherForward = Vector3.zero,
                OtherVelocity = Vector3.zero,
            };
            return true;
        }

        /// <summary>
        /// Classify one overlapping mover for the projectile channel. Returns false (skip) when
        /// <paramref name="other"/> is self, shares this mover's owner, or is still the guarded id (the pair
        /// hasn't separated since the last shot). When it is the guarded id,
        /// <paramref name="stillGuarded"/> is set so the caller keeps suppressing until they separate.
        /// </summary>
        public static bool TryClassifyProjectileHit(in NetworkedMotionState self, Vector3 selfPos, Vector3 incomingVelocity, in NetworkedMotionState other, int guard, out HitInfo info, out bool stillGuarded)
        {
            info = default;
            stillGuarded = false;

            if (other.Id == self.Id)
                return false;
            if (other.OwnerId == self.OwnerId)
                return false; // only across different owners
            if (other.Id == guard)
            {
                stillGuarded = true; // stay suppressed until they separate
                return false;
            }

            Vector3 dir = incomingVelocity.sqrMagnitude > Eps ? incomingVelocity.normalized : self.OriginForward;
            Vector3 otherPos = other.ApproxPosition();
            Vector3 toSelf = selfPos - otherPos;
            Vector3 normal = toSelf.sqrMagnitude > Eps ? toSelf.normalized : -dir;

            info = new HitInfo
            {
                Category = HitCategory.Projectile,
                Point = selfPos,
                Normal = normal,
                IncomingVelocity = incomingVelocity,
                Direction = dir,
                OtherPlayerId = -1,
                OtherProjectileId = other.Id,
                OtherOwnerId = other.OwnerId,
                OtherForward = other.OriginForward,
                OtherVelocity = other.OriginForward * other.OriginSpeed,
            };
            return true;
        }

        /// <summary>
        /// The next projectile-channel guard id after a tick's overlap loop. If anything was handled, guard
        /// the first one handled; otherwise keep the current guard while it is still overlapping, else clear
        /// it. Mirrors the old NetworkCollision OnEnter/OnExit behaviour.
        /// </summary>
        public static int ResolveGuard(int currentGuard, int firstHandled, bool stillGuarded) =>
            firstHandled >= 0 ? firstHandled : (stillGuarded ? currentGuard : NoGuard);
    }
}
