using Arena.Damage;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Deal damage to the target that was hit. Reads the owner's <see cref="Damage"/> component for the base
/// amount (or a fixed <see cref="amount"/> for a bonus — e.g. a passive's extra-damage contribution),
/// resolves it through the pure <see cref="DamageProcessor"/>, and applies it to the target's
/// <see cref="IDamageable"/>. It reads target + point + direction, so it is typed to
/// <see cref="ITargetHitContext"/> — the target is guaranteed present (no "did I have a target" check), and it
/// is authorable on any target-bearing hit (a player, or a damageable projectile).
/// </summary>
[System.Serializable]
public sealed class DealDamage : IEffect<ITargetHitContext>
{
    [SerializeField, FormerlySerializedAs("useInvokerDamage"),
     Tooltip("Read the base amount from the owner's Damage component (the projectile's own damage). Off = use the fixed Amount below (a passive's bonus).")]
    private bool useOwnerDamage = true;
    [SerializeField, Tooltip("The base amount when not reading the owner's Damage component.")]
    private int amount = 10;

    public DealDamage() { }

    public DealDamage(bool useOwnerDamage, int amount = 10)
    {
        this.useOwnerDamage = useOwnerDamage;
        this.amount = amount;
    }

    public void Apply(ITargetHitContext ctx)
    {
        if (ctx.Target == null || ctx.Target.GetComponentInParent<IDamageable>() is not { } damageable)
            return;

        int baseAmount = useOwnerDamage && ctx.Owner != null && ctx.Owner.TryGetComponent<Damage>(out var dmg)
            ? dmg.Amount
            : amount;

        damageable.TakeDamage(new DamageInfo
        {
            Amount = DamageProcessor.Resolve(baseAmount),
            Attacker = ctx.OwnerPlayer,
            HitPoint = ctx.Point,
            Direction = ctx.Direction,
            Type = DamageType.Bullet,
        });
    }
}
