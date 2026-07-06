using Arena.Damage;
using UnityEngine;

/// <summary>
/// Deal damage across a damage EXCHANGE: read the SOURCE's <see cref="Damage"/> component for the amount, resolve
/// it through the pure <see cref="DamageProcessor"/>, and apply it to the struck <see cref="IDamageable"/>.
///
/// Coupled to the damage SYSTEM, not to any archetype: it names <see cref="IDamageExchangeContext"/>, so both the
/// damage source and the damage target are guaranteed present and it does no <c>GetComponent</c> and no
/// null-gambling — the site that built the context resolved them. The same operation lands on a pawn, a prop, or
/// a damageable projectile. Stateless: the amount lives on the source's <see cref="Damage"/> component.
/// </summary>
[System.Serializable]
public sealed class DealDamage : IEffect<IDamageExchangeContext>
{
    public void Apply(IDamageExchangeContext ctx)
    {
        if (ctx.DamageTarget == null || ctx.DamageSource == null)
            return;

        int amount = DamageProcessor.Resolve(ctx.DamageSource.Amount);

        ctx.DamageTarget.TakeDamage(new DamageInfo
        {
            Amount = amount,
            Attacker = ctx.Instigator,
            HitPoint = ctx.Point,
            Direction = ctx.Direction,
            Type = DamageType.Bullet,
        });
    }
}
