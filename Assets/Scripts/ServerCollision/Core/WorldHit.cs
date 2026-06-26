using UnityEngine;

namespace Arena.ServerCollision
{
    /// <summary>
    /// Result of a world cast (against the Stage|Players broadphase), produced by the shell and handed
    /// into <see cref="ServerCollisionProcessor"/>. Engine-free data — the shell keeps the actual
    /// <c>Collider</c>/<c>GameObject</c> separately so it can fill the Unity-coupled effect context.
    /// </summary>
    public struct WorldHit
    {
        public Vector3 Point;
        public Vector3 Normal;
        public float Distance;

        /// <summary>NetworkPlayer.ObjectId of the player hit, or -1 if a non-player surface was hit.</summary>
        public int PlayerId;
    }
}
