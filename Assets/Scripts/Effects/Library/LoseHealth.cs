using UnityEngine;

/// <summary>
/// Drain the invoking projectile's own <see cref="Health"/> by <see cref="_amount"/> (ports the old
/// surface/DevilDaggers self-damage). Death at zero is enforced by ServerCollision (it despawns when Health
/// is dead), so this only needs to subtract.
/// </summary>
public sealed class LoseHealth : IEffect
{
    readonly int _amount;

    public LoseHealth(int amount = 1) => _amount = amount;

    public void Apply(IEffectContext ctx)
    {
        if (ctx.Invoker != null && ctx.Invoker.TryGetComponent<Health>(out var health))
            health.ServerLose(_amount);
    }
}
