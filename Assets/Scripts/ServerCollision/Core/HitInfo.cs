using UnityEngine;

namespace Arena.ServerCollision
{
    /// <summary>
    /// The pure, engine-free classification of one server-authoritative hit, produced by
    /// <see cref="ServerCollisionProcessor"/>. It carries the geometry + the ids of what was hit; the shell
    /// (the ServerCollision component) resolves those ids to actual <c>GameObject</c>s and packs them, with
    /// this geometry, into the Unity-coupled effect context the hit reactions run against.
    /// </summary>
    public struct HitInfo
    {
        public HitCategory Category;

        public Vector3 Point;
        public Vector3 Normal;

        /// <summary>The reacting mover's velocity at impact (for reflect/deflect speed + direction).</summary>
        public Vector3 IncomingVelocity;

        /// <summary>Convenience: <see cref="IncomingVelocity"/> normalized (travel direction).</summary>
        public Vector3 Direction;

        /// <summary>ObjectId of the player hit, or -1 when the hit was not a player.</summary>
        public int OtherPlayerId;

        /// <summary>Id of the other mover (projectile channel), or -1 otherwise.</summary>
        public int OtherProjectileId;

        /// <summary>OwnerId of the other mover (projectile channel), or -1 otherwise.</summary>
        public int OtherOwnerId;

        /// <summary>Forward of the other mover (projectile channel), for deflection.</summary>
        public Vector3 OtherForward;

        /// <summary>Velocity estimate of the other mover (projectile channel).</summary>
        public Vector3 OtherVelocity;
    }
}
