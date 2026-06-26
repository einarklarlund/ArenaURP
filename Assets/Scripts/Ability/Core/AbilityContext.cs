using UnityEngine;

namespace Arena.Ability
{
    /// <summary>
    /// Everything an ability effect needs to know about the caster and their aim, gathered by the
    /// imperative shell (PawnAbilities) and passed IN as data. Pure — the ability core never reads the
    /// world itself. This mirrors <c>HitInfo</c> for projectile reactions: the shell does the I/O, the
    /// effect just decides what should happen.
    /// </summary>
    public struct AbilityContext
    {
        /// <summary>ObjectId of the casting NetworkPlayer — carried into effects for attribution.</summary>
        public int CasterPlayerId;

        /// <summary>World position effects originate from (typically the muzzle / pawn position).</summary>
        public Vector3 Origin;

        /// <summary>Normalized direction the caster is aiming.</summary>
        public Vector3 AimDirection;

        /// <summary>World point the caster is aiming at, if the shell resolved one (else <see cref="Origin"/> + dir).</summary>
        public Vector3 AimPoint;
    }
}
