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
    /// (Health/Alive/segment) and emit world side effects by APPENDING <see cref="WorldEffect"/>
    /// to <paramref name="effects"/>. They never call the engine and never gate — the server-only shell
    /// performs the effects. This is what removes the wide injected-world seam, and the same
    /// <see cref="WorldEffect"/> vocabulary is reused by player abilities.
    /// </summary>
    public interface IHitReaction
    {
        void Apply(in HitInfo hit, ref ProjectileState self, List<WorldEffect> effects);
    }

    /// <summary>
    /// The extra hit reactions a projectile's OWNER contributes, split by hit category — passed into the
    /// pure processor as DATA so the processor (not the shell) decides which category applies to a given
    /// hit. Any list may be null/empty. The shell resolves the owner and hands over this bundle; it never
    /// reasons about which reactions go where.
    /// </summary>
    public readonly struct OwnerReactions
    {
        public readonly List<IHitReaction> OnEnemy;
        public readonly List<IHitReaction> OnSurface;
        public readonly List<IHitReaction> OnProjectile;

        public OwnerReactions(List<IHitReaction> onEnemy, List<IHitReaction> onSurface, List<IHitReaction> onProjectile)
        {
            OnEnemy = onEnemy;
            OnSurface = onSurface;
            OnProjectile = onProjectile;
        }

        /// <summary>The owner's reactions for a hit category (the processor's category decision picks this).</summary>
        public List<IHitReaction> For(HitCategory category) => category switch
        {
            HitCategory.Enemy => OnEnemy,
            HitCategory.Surface => OnSurface,
            HitCategory.Projectile => OnProjectile,
            _ => null,
        };
    }

    /// <summary>
    /// Implemented by a pawn-side component (e.g. PawnAbilities) that contributes EXTRA hit reactions to
    /// the projectiles a player owns — a passive that makes your bullets do bonus damage / apply a debuff
    /// on hit, etc. The projectile shell resolves its owner SERVER-SIDE (collision is server-authoritative)
    /// and reads this bundle; the reactions are NEVER sent on the spawn payload. This is the "find the
    /// owner's effects instead of shipping them" rule: don't pay network bytes for state already known on
    /// the server (see the fishnet-conventions skill).
    /// </summary>
    public interface IProjectileEffectProvider
    {
        /// <summary>The owner's per-category projectile reactions, as data for the processor to select from.</summary>
        OwnerReactions GetReactions();
    }
}
