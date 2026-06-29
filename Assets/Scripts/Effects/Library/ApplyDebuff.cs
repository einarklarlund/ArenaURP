/// <summary>
/// Apply a debuff to the single target that was hit. No-op until a debuff system exists; when one is added,
/// this resolves the target's debuff receiver and applies <see cref="_debuff"/> directly. Needs a target.
/// </summary>
public sealed class ApplyDebuff : IEffect
{
    readonly DebuffType _debuff;

    public ApplyDebuff(DebuffType debuff = DebuffType.Slow) => _debuff = debuff;

    public void Apply(IEffectContext ctx)
    {
        if (ctx is not ITargetContext) return;
        // TODO: no debuff system exists yet. Intentionally a no-op until one is introduced —
        // e.g. tc.Target.GetComponentInParent<IDebuffable>()?.Apply(_debuff);
    }
}
