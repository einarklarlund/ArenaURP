using UnityEngine;

/// <summary>
/// Drain the invoking projectile's own <see cref="Health"/> by <see cref="Amount"/> (ports the old
/// surface/DevilDaggers self-damage). Death at zero is enforced by ServerCollision (it despawns when
/// Health is dead), so this only needs to subtract.
/// </summary>
[CreateAssetMenu(menuName = "Arena/World Effects/Lose Health", fileName = "LoseHealth")]
public sealed class LoseHealth : WorldEffect
{
    public int Amount = 1;

    public override void Apply(in EffectContext ctx)
    {
        if (ctx.Invoker != null && ctx.Invoker.TryGetComponent<Health>(out var health))
            health.ServerLose(Amount);
    }
}
