using UnityEngine;

/// <summary>
/// Apply a debuff to the target that was hit. No-op until a debuff system exists; routed through the
/// single world surface so it lands the day one is added.
/// </summary>
[CreateAssetMenu(menuName = "Arena/World Effects/Apply Debuff", fileName = "ApplyDebuff")]
public sealed class ApplyDebuff : WorldEffect
{
    public DebuffType Debuff = DebuffType.Slow;

    public override void Apply(in EffectContext ctx)
    {
        if (ctx.Target != null)
            ProjectileWorldProvider.Instance?.ApplyDebuff(ctx.Target, Debuff);
    }
}
