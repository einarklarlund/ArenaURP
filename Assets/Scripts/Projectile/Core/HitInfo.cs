using UnityEngine;

namespace Arena.Projectile
{
    /// <summary>
    /// Result of a world cast (against the Stage|Players broadphase), produced by the shell and
    /// handed into <see cref="ProjectileStepProcessor.ResolveWorldHit"/>. Engine-free.
    /// </summary>
    public struct WorldHit
    {
        public Vector3 Point;
        public Vector3 Normal;
        public float Distance;

        /// <summary>NetworkPlayer.ObjectId of the player hit, or -1 if a non-player surface was hit.</summary>
        public int PlayerId;
    }

    /// <summary>
    /// A resolved collision handed to an <see cref="IHitReaction"/>. Carries everything a reaction
    /// needs without reaching into the engine. The processor fills it from a <see cref="WorldHit"/>
    /// (world channel) or another <see cref="ProjectileState"/> (projectile channel).
    /// </summary>
    public struct HitInfo
    {
        public HitCategory Category;

        public Vector3 Point;
        public Vector3 Normal;

        /// <summary>The reacting projectile's velocity at impact (for reflect/deflect speed + direction).</summary>
        public Vector3 IncomingVelocity;

        /// <summary>Convenience: <see cref="IncomingVelocity"/> normalized (travel direction).</summary>
        public Vector3 Direction;

        /// <summary>ObjectId of the player hit, or -1 when the hit was not a player.</summary>
        public int OtherPlayerId;

        /// <summary>Id of the other projectile (projectile channel), or -1 otherwise.</summary>
        public int OtherProjectileId;

        /// <summary>OwnerId of the other projectile (projectile channel), or -1 otherwise.</summary>
        public int OtherOwnerId;

        /// <summary>Forward of the other projectile (projectile channel), for deflection.</summary>
        public Vector3 OtherForward;

        /// <summary>Velocity estimate of the other projectile (projectile channel).</summary>
        public Vector3 OtherVelocity;
    }
}
