using UnityEngine;

/// <summary>
/// Consume one point of the owner's own <see cref="Health"/> on a hit — a projectile spending durability (a
/// normal bullet dies on contact; a ricochet bullet with MaxHealth &gt; 1 survives that many bounces). Reads
/// only <see cref="IEffectContext.Owner"/>, so it is typed to the base <see cref="IEffectContext"/> and is
/// authorable on ANY hit (a bullet dying on a wall, on a player, or on contact with another projectile). Death
/// at zero is enforced by ServerCollision (it despawns when Health is dead).
///
/// Stateless: durability lives on the owner's <see cref="Health"/> component (its MaxHealth), not on this
/// effect — one hit consumes one point.
/// </summary>
[System.Serializable]
public sealed class LoseHealth : IEffect<IEffectContext>
{
    public void Apply(IEffectContext ctx)
    {
        if (ctx.Owner != null && ctx.Owner.TryGetComponent<Health>(out var health))
            health.ServerLose(1);
    }
}
