using Arena.Damage;
using UnityEngine;

/// <summary>
/// Deal damage to the player that was hit. Reads the invoker's <see cref="Damage"/> component for the base
/// amount (or a fixed <see cref="amount"/> for a bonus — e.g. a passive's extra-damage contribution),
/// resolves it through the pure <see cref="DamageProcessor"/>, and applies it to the target's
/// <see cref="IDamageable"/>. Typed to <see cref="ProjectilePlayerCollision"/>, so the target is guaranteed
/// present — no "did I have a target" check.
/// </summary>
[System.Serializable]
public sealed class DealDamage : IEffect<ProjectilePlayerCollision>
{
    [SerializeField, Tooltip("Read the base amount from the invoker's Damage component (the projectile's own damage). Off = use the fixed Amount below (a passive's bonus).")]
    private bool useInvokerDamage = true;
    [SerializeField, Tooltip("The base amount when not reading the invoker's Damage component.")]
    private int amount = 10;
    [SerializeField, Tooltip("Kill the invoking projectile after it deals damage (most bullets die on contact).")]
    private bool despawnAfter = true;

    public DealDamage() { }

    public DealDamage(bool useInvokerDamage, int amount = 10, bool despawnAfter = true)
    {
        this.useInvokerDamage = useInvokerDamage;
        this.amount = amount;
        this.despawnAfter = despawnAfter;
    }

    public void Apply(ProjectilePlayerCollision ctx)
    {
        if (ctx.Target == null || ctx.Target.GetComponentInParent<IDamageable>() is not { } damageable)
            return;

        int baseAmount = useInvokerDamage && ctx.Invoker != null && ctx.Invoker.TryGetComponent<Damage>(out var dmg)
            ? dmg.Amount
            : amount;

        damageable.TakeDamage(new DamageInfo
        {
            Amount = DamageProcessor.Resolve(baseAmount),
            Attacker = ctx.InvokerPlayer,
            HitPoint = ctx.Point,
            Direction = ctx.Direction,
            Type = DamageType.Bullet,
        });

        if (despawnAfter && ctx.Invoker != null && ctx.Invoker.TryGetComponent<Health>(out var health))
            health.ServerLose(health.CurrentHealth.Value); // drain to zero; ServerCollision despawns when dead
    }
}
