using UnityEngine;

namespace Arena.NetworkedMotion
{
    /// <summary>
    /// Pure trajectory math for a ricochet — reflect an incoming velocity across a surface normal. Lives
    /// in the motion core (it produces the next motion segment's direction/speed) and is EditMode-tested,
    /// so the thin Unity-coupled <c>Ricochet</c> effect can stay an I/O wrapper around this.
    /// </summary>
    public static class RicochetMath
    {
        private const float Eps = 0.0001f;

        /// <summary>
        /// Reflect <paramref name="incomingVelocity"/> across <paramref name="normal"/>. Returns false
        /// (degenerate — caller should leave the segment unchanged) when the reflection vanishes.
        /// </summary>
        public static bool TryReflect(Vector3 incomingVelocity, Vector3 normal, out Vector3 direction, out float speed)
        {
            Vector3 reflected = Vector3.Reflect(incomingVelocity, normal);
            if (reflected.sqrMagnitude < Eps)
            {
                direction = Vector3.zero;
                speed = 0f;
                return false;
            }
            speed = reflected.magnitude;
            direction = reflected / speed;
            return true;
        }
    }
}
