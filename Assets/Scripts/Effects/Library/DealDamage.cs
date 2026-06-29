using Arena.Damage;
using UnityEngine;

/// <summary>
/// Deal damage to the single target that was hit. Reads the invoker's <see cref="Damage"/> component for the
/// base amount (or a fixed <see cref="_amount"/> for a bonus, e.g. a passive's extra-damage contribution),
/// resolves it through the pure <see cref="DamageProcessor"/>, and applies it to the target's
/// <see cref="IDamageable"/>. Needs a target — no-ops on a context without one (e.g. a no-target ability).
/// </summary>
public sealed class DealDamage : IEffect
{
    readonly bool _useInvokerDamage;
    readonly int _amount;
    readonly bool _despawnAfter;

    /// <param name="useInvokerDamage">Read the base amount from the invoker's Damage component (the projectile's
    /// own damage). False for a fixed extra (a passive's bonus) that uses <paramref name="amount"/>.</param>
    /// <param name="amount">The base amount when not reading the invoker's Damage component.</param>
    /// <param name="despawnAfter">Kill the invoking projectile after it deals damage (most bullets die on contact).</param>
    public DealDamage(bool useInvokerDamage = true, int amount = 10, bool despawnAfter = true)
    {
        _useInvokerDamage = useInvokerDamage;
        _amount = amount;
        _despawnAfter = despawnAfter;
    }

    public void Apply(IEffectContext ctx)
    {
        if (ctx is not ITargetContext tc) return;
        if (tc.Target.GetComponentInParent<IDamageable>() is not { } damageable) return;

        int baseAmount = _useInvokerDamage && ctx.Invoker != null && ctx.Invoker.TryGetComponent<Damage>(out var dmg)
            ? dmg.Amount
            : _amount;

        // HitPoint/Direction are informational (knockback dir, hit FX) — read them if available, else zero.
        Vector3 hitPoint = ctx is IPointContext p ? p.Point : Vector3.zero;
        Vector3 direction = ctx is IDirectedContext d ? d.Direction : Vector3.zero;

        damageable.TakeDamage(new DamageInfo
        {
            Amount = DamageProcessor.Resolve(baseAmount),
            Attacker = ctx.InvokerPlayer,
            HitPoint = hitPoint,
            Direction = direction,
            Type = DamageType.Bullet,
        });

        if (_despawnAfter && ctx.Invoker != null && ctx.Invoker.TryGetComponent<Health>(out var health))
            health.ServerLose(health.CurrentHealth.Value); // drain to zero; ServerCollision despawns when dead
    }
}
