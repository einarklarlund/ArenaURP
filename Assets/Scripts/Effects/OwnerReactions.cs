using System.Collections.Generic;
using Arena.ServerCollision;

/// <summary>
/// The extra hit reactions a projectile's OWNER contributes (passive abilities that buff the player's
/// bullets), split by hit category. Resolved SERVER-SIDE from the live owner during collision — never
/// serialized onto a projectile's spawn payload (the "find the owner's effects instead of shipping them"
/// rule). Runtime holds the base <see cref="HitReaction"/> type; the per-category typing that prevents
/// mis-slotting lives on the authoring side (PassiveAbilityType's typed lists).
/// </summary>
public readonly struct OwnerReactions
{
    public readonly IReadOnlyList<HitReaction> OnEnemy;
    public readonly IReadOnlyList<HitReaction> OnSurface;
    public readonly IReadOnlyList<HitReaction> OnProjectile;

    public OwnerReactions(IReadOnlyList<HitReaction> onEnemy, IReadOnlyList<HitReaction> onSurface, IReadOnlyList<HitReaction> onProjectile)
    {
        OnEnemy = onEnemy;
        OnSurface = onSurface;
        OnProjectile = onProjectile;
    }

    /// <summary>The owner's reactions for a hit category (the collision component's classification picks this).</summary>
    public IReadOnlyList<HitReaction> For(HitCategory category) => category switch
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
    /// <summary>The owner's per-category projectile reactions.</summary>
    OwnerReactions GetReactions();
}
