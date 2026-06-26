using UnityEngine;

/// <summary>
/// Apply a debuff to the single target that was hit. No-op until a debuff system exists; when one is
/// added, this resolves the target's debuff receiver and applies <see cref="Debuff"/> directly.
/// </summary>
[CreateAssetMenu(menuName = "Arena/World Effects/Apply Debuff", fileName = "ApplyDebuff")]
public sealed class ApplyDebuff : SingleTargetEffect
{
    public DebuffType Debuff = DebuffType.Slow;

    protected override void ApplyToTarget(in EffectContext ctx, GameObject target)
    {
        // TODO: no debuff system exists yet. Intentionally a no-op until one is introduced —
        // e.g. target.GetComponentInParent<IDebuffable>()?.Apply(Debuff);
    }
}
