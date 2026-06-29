using System;
using System.Collections.Generic;

/// <summary>
/// A runtime, ordered bundle of <see cref="IEffect"/>s for ONE reaction (e.g. a projectile's surface hit).
/// No longer a ScriptableObject asset — reactions are built IN CODE: a projectile/ability type populates its
/// defaults (see ProjectileType.BuildDefaultReactions), and other systems APPEND extra effects at runtime
/// (a "bouncy" powerup contributing a Ricochet to the owner's surface reaction). So a HitReaction defines
/// what happens by default, and the contribution path adds more to the same reaction.
/// </summary>
public sealed class HitReaction
{
    readonly List<IEffect> _effects = new();

    public IReadOnlyList<IEffect> Effects => _effects;
    public int Count => _effects.Count;

    /// <summary>Append an effect (null ignored). Returns this for chaining in a type's default builder.</summary>
    public HitReaction Add(IEffect effect)
    {
        if (effect != null) _effects.Add(effect);
        return this;
    }

    /// <summary>
    /// Run every effect in order against the context. <paramref name="stop"/> is checked AFTER each effect
    /// (e.g. <c>() =&gt; projectile.IsDead</c>) so a reaction can short-circuit when the invoker dies mid-list,
    /// matching the old ServerCollision behaviour.
    /// </summary>
    public void Run(IEffectContext ctx, Func<bool> stop = null)
    {
        for (int i = 0; i < _effects.Count; i++)
        {
            _effects[i].Apply(ctx);
            if (stop != null && stop()) break;
        }
    }
}

/// <summary>
/// The three per-category reactions a projectile runs, grouped. A type fills these in code; ServerCollision
/// runs the one matching the classified hit, then runs the owner's contributed reaction for that category.
/// The category lives in the field you add to (you can't mis-slot a surface effect into the enemy list), so
/// the old typed marker subclasses (SurfaceHitReaction/EnemyHitReaction/ProjectileHitReaction) are gone.
/// </summary>
public sealed class ProjectileReactions
{
    public readonly HitReaction OnSurface = new();
    public readonly HitReaction OnEnemy = new();
    public readonly HitReaction OnProjectile = new();
}
