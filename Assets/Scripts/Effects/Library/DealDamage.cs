using Arena.Damage;
using UnityEngine;

/// <summary>
/// Deal damage to the target that was hit. Reads the invoker's <see cref="Damage"/> component for the base
/// amount (falling back to <see cref="FallbackAmount"/> when the invoker has none — e.g. an ability),
/// resolves it through the pure <see cref="DamageProcessor"/>, and applies it to the target's
/// <see cref="IDamageable"/>. The effect decides for itself that it can only act on something damageable,
/// so it's safe to slot anywhere. Owner-safety is already enforced upstream by the collision classifier.
/// </summary>
[CreateAssetMenu(menuName = "Arena/World Effects/Deal Damage", fileName = "DealDamage")]
public sealed class DealDamage : WorldEffect
{
    [Tooltip("Read the base amount from the invoker's Damage component (the projectile's own damage). " +
             "Turn off for a fixed extra (e.g. a passive's bonus-damage reaction) that uses Amount.")]
    public bool UseInvokerDamage = true;

    [Tooltip("The base amount when not reading the invoker's Damage component (fixed/bonus damage).")]
    public int Amount = 10;

    [Tooltip("Kill the invoking projectile after it deals damage (most bullets die on contact).")]
    public bool DespawnAfter = true;

    public override void Apply(in EffectContext ctx)
    {
        if (ctx.Target == null || ctx.Target.GetComponentInParent<IDamageable>() is not { } damageable)
            return;

        int baseAmount = UseInvokerDamage && ctx.Invoker != null && ctx.Invoker.TryGetComponent<Damage>(out var dmg)
            ? dmg.Amount
            : Amount;
        int amount = DamageProcessor.Resolve(baseAmount);

        var attacker = ProjectileWorldProvider.Instance != null
            ? ProjectileWorldProvider.Instance.ResolvePlayer(ctx.InvokerPlayerId)
            : null;

        damageable.TakeDamage(new DamageInfo
        {
            Amount = amount,
            Attacker = attacker,
            HitPoint = ctx.Point,
            Direction = ctx.Direction,
            Type = DamageType.Bullet,
        });

        if (DespawnAfter && ctx.Invoker != null && ctx.Invoker.TryGetComponent<Health>(out var health))
            health.ServerLose(health.CurrentHealth.Value); // drain to zero; ServerCollision despawns when dead
    }
}
