using Arena.ServerCollision;

/// <summary>
/// The extra hit reactions a projectile's OWNER contributes (passive abilities / powerups that buff the
/// player's bullets), split by hit category. Resolved SERVER-SIDE from the live owner during collision —
/// never serialized onto a projectile's spawn payload (the "find the owner's effects instead of shipping
/// them" rule). Each category is a runtime <see cref="HitReaction"/> built in code by the owner's passives.
/// </summary>
public readonly struct OwnerReactions
{
    public readonly HitReaction OnEnemy;
    public readonly HitReaction OnSurface;
    public readonly HitReaction OnProjectile;

    public OwnerReactions(HitReaction onEnemy, HitReaction onSurface, HitReaction onProjectile)
    {
        OnEnemy = onEnemy;
        OnSurface = onSurface;
        OnProjectile = onProjectile;
    }

    /// <summary>The owner's reaction for a hit category (the collision component's classification picks this).</summary>
    public HitReaction For(HitCategory category) => category switch
    {
        HitCategory.Enemy => OnEnemy,
        HitCategory.Surface => OnSurface,
        HitCategory.Projectile => OnProjectile,
        _ => null,
    };
}

/// <summary>
/// Implemented by a pawn-side component (PawnAbilities) that contributes EXTRA hit reactions to the
/// projectiles a player owns. The projectile's ServerCollision resolves its owner server-side and reads
/// this bundle; the reactions are NEVER sent on the spawn payload.
/// </summary>
public interface IProjectileEffectProvider
{
    /// <summary>The owner's per-category projectile reactions (runtime, built in code from equipped passives).</summary>
    OwnerReactions GetReactions();
}
