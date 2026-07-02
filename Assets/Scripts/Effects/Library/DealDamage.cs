using Arena.Damage;
using UnityEngine;

/// <summary>
/// Deal damage to the target that was hit. Reads the owner's <see cref="Damage"/> component for the amount,
/// resolves it through the pure <see cref="DamageProcessor"/>, and applies it to the target's
/// <see cref="IDamageable"/>. It reads target + point + direction, so it is typed to
/// <see cref="ITargetHitContext"/> — the target is guaranteed present (no "did I have a target" check), and it
/// is authorable on any target-bearing hit (a player, or a damageable projectile).
///
/// Stateless: the amount lives on the owner's <see cref="Damage"/> component (runtime-mutable for buffs/crits),
/// not on this effect.
/// </summary>
[System.Serializable]
public sealed class DealDamage : IEffect<ITargetHitContext>
{
    public void Apply(ITargetHitContext ctx)
    {
        if (ctx.Target == null || ctx.Target.GetComponentInParent<IDamageable>() is not { } damageable)
            return;
        if (ctx.Owner == null || !ctx.Owner.TryGetComponent<Damage>(out var dmg))
            return;

        damageable.TakeDamage(new DamageInfo
        {
            Amount = DamageProcessor.Resolve(dmg.Amount),
            Attacker = ctx.OwnerPlayer,
            HitPoint = ctx.Point,
            Direction = ctx.Direction,
            Type = DamageType.Bullet,
        });
    }
}
