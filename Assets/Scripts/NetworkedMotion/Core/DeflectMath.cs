using UnityEngine;

namespace Arena.NetworkedMotion
{
    /// <summary>
    /// Pure trajectory math for a deflection — redirect a mover along another mover's heading while
    /// preserving its own speed (ports the old DeflectProjectile). EditMode-tested; the thin
    /// Unity-coupled <c>DeflectProjectile</c> effect wraps this.
    /// </summary>
    public static class DeflectMath
    {
        private const float Eps = 0.0001f;

        /// <summary>
        /// Compute the redirected heading: prefer the other mover's forward, fall back to its velocity.
        /// Keeps <paramref name="incomingSpeed"/>, falling back to <paramref name="fallbackSpeed"/> when
        /// the incoming speed is ~0. Returns false (degenerate — leave the segment unchanged) when no
        /// usable direction exists.
        /// </summary>
        public static bool TryRedirect(Vector3 otherForward, Vector3 otherVelocity, float incomingSpeed, float fallbackSpeed, out Vector3 direction, out float speed)
        {
            Vector3 dir = otherForward.sqrMagnitude > Eps ? otherForward : otherVelocity;
            if (dir.sqrMagnitude < Eps)
            {
                direction = Vector3.zero;
                speed = 0f;
                return false;
            }
            direction = dir.normalized;
            speed = incomingSpeed > Eps ? incomingSpeed : fallbackSpeed;
            return true;
        }
    }
}
