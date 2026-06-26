using UnityEngine;

namespace Arena.NetworkedMotion
{
    /// <summary>
    /// Pure direction math for spawning N movers fanned evenly across a spread around an axis. A full 360°
    /// spread produces an even ring; a smaller spread produces an arc centred on the base direction.
    /// Deterministic (no Random), EditMode-tested; the Unity-coupled <c>SpawnProjectiles</c> effect (used
    /// by both projectile reactions and abilities) wraps this.
    /// </summary>
    public static class SpawnFanMath
    {
        private const float Eps = 1e-4f;

        /// <summary>
        /// The direction for fan element <paramref name="index"/> of <paramref name="count"/>, spread over
        /// <paramref name="spreadDegrees"/> around <paramref name="axis"/>, centred on
        /// <paramref name="baseDir"/>.
        /// </summary>
        public static Vector3 Direction(Vector3 baseDir, int index, int count, float spreadDegrees, Vector3 axis)
        {
            Vector3 b = baseDir.sqrMagnitude > Eps ? baseDir.normalized : Vector3.forward;
            if (count <= 1)
                return b;

            bool fullRing = spreadDegrees >= 359.999f;
            // Full ring: divide the circle into count slices. Arc: place endpoints on the arc edges.
            float step = spreadDegrees / (fullRing ? count : count - 1);
            float start = fullRing ? 0f : -spreadDegrees * 0.5f;
            float angle = start + step * index;
            return Quaternion.AngleAxis(angle, axis) * b;
        }
    }
}
