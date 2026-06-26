using Arena.Damage;
using UnityEngine;

/// <summary>
/// Deal damage to the single target that was hit. Reads the invoker's <see cref="Damage"/> component for
/// the base amount (or a fixed <see cref="Amount"/> for a bonus, e.g. a passive's extra-damage reaction),
/// resolves it through the pure <see cref="DamageProcessor"/>, and applies it to the target's
/// <see cref="IDamageable"/>. Acts only on something damageable; owner-safety is enforced upstream by the
/// collision classifier.
/// </summary>
[CreateAssetMenu(menuName = "Arena/World Effects/Deal Damage", fileName = "DealDamage")]
public sealed class DealDamage : SingleTargetEffect
{
    [Tooltip("Read the base amount from the invoker's Damage component (the projectile's own damage). " +
             "Turn off for a fixed extra (e.g. a passive's bonus-damage reaction) that uses Amount.")]
    public bool UseInvokerDamage = true;

    [Tooltip("The base amount when not reading the invoker's Damage component (fixed/bonus damage).")]
    public int Amount = 10;

    [Tooltip("Kill the invoking projectile after it deals damage (most bullets die on contact).")]
    public bool DespawnAfter = true;

    protected override void ApplyToTarget(in EffectContext ctx, GameObject target)
    {
        if (target.GetComponentInParent<IDamageable>() is not { } damageable)
            return;

        int baseAmount = UseInvokerDamage && ctx.Invoker != null && ctx.Invoker.TryGetComponent<Damage>(out var dmg)
            ? dmg.Amount
            : Amount;

        damageable.TakeDamage(new DamageInfo
        {
            Amount = DamageProcessor.Resolve(baseAmount),
            Attacker = ctx.InvokerPlayer,
            HitPoint = ctx.Point,
            Direction = ctx.Direction,
            Type = DamageType.Bullet,
        });

        if (DespawnAfter && ctx.Invoker != null && ctx.Invoker.TryGetComponent<Health>(out var health))
            health.ServerLose(health.CurrentHealth.Value); // drain to zero; ServerCollision despawns when dead
    }
}
