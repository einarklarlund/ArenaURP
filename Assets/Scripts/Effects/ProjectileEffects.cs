using System.Collections.Generic;

/// <summary>
/// A projectile's per-category collision operations, grouped — the OWNER's runtime contributions (passives /
/// powerups, resolved server-side from the live owner and never payloaded — the "find the owner's operations
/// instead of shipping them" rule). A projectile's own defaults are authored directly on its prefab's
/// ServerCollision. Each list is typed to its category's context, so an operation can only appear where its
/// context is available.
/// </summary>
public readonly struct ProjectileEffects
{
    public readonly IReadOnlyList<IEffect<ProjectileSurfaceCollision>> OnSurface;
    public readonly IReadOnlyList<IEffect<ProjectilePlayerCollision>> OnPlayer;
    public readonly IReadOnlyList<IEffect<ProjectileProjectileCollision>> OnProjectile;

    public ProjectileEffects(
        IReadOnlyList<IEffect<ProjectileSurfaceCollision>> onSurface,
        IReadOnlyList<IEffect<ProjectilePlayerCollision>> onPlayer,
        IReadOnlyList<IEffect<ProjectileProjectileCollision>> onProjectile)
    {
        OnSurface = onSurface;
        OnPlayer = onPlayer;
        OnProjectile = onProjectile;
    }
}

/// <summary>
/// Implemented by a pawn-side component (<c>PawnPassiveAbilities</c>) that contributes EXTRA projectile operations
/// to the projectiles a player owns (from equipped passive abilities / powerups). The projectile's
/// ServerCollision resolves its owner server-side and reads this bundle; the operations are NEVER sent on the
/// spawn payload.
/// </summary>
public interface IProjectileEffectProvider
{
    /// <summary>The owner's per-category projectile operations, aggregated from equipped passives.</summary>
    ProjectileEffects GetProjectileEffects();
}
