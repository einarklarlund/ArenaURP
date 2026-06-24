using System.Collections.Generic;
using UnityEngine;

namespace Arena.Projectile
{
    /// <summary>
    /// A movement strategy (Strategy pattern — a genuinely earned interface: three real runtime
    /// implementations Linear/Arc/Laser, selected by <see cref="MovementKind"/>). Movement is expressed
    /// CLOSED-FORM: <see cref="PositionAt"/>/<see cref="VelocityAt"/> evaluate the trajectory at an
    /// elapsed time within the current segment, so position is exact at any tick (every peer derives it
    /// deterministically from the spawn data; nothing is integrated or synced per frame).
    /// All methods are pure and deterministic.
    /// </summary>
    public interface IProjectileMovement
    {
        /// <summary>Position at <paramref name="elapsed"/> seconds into the current segment.</summary>
        Vector3 PositionAt(in ProjectileState s, in ProjectileType t, float elapsed);

        /// <summary>Velocity at <paramref name="elapsed"/> seconds into the current segment.</summary>
        Vector3 VelocityAt(in ProjectileState s, in ProjectileType t, float elapsed);
    }

    /// <summary>
    /// A composable reaction to a hit. Reactions mutate the reacting projectile's own state directly
    /// (Health/Alive/segment) and emit world side effects by APPENDING <see cref="ProjectileEffect"/>
    /// to <paramref name="effects"/>. They never call the engine and never gate — gating is the shell's
    /// job (see <see cref="EffectGate"/>). This is what removes the wide injected-world seam.
    /// </summary>
    public interface IHitReaction
    {
        void Apply(in HitInfo hit, ref ProjectileState self, List<ProjectileEffect> effects);
    }
}
