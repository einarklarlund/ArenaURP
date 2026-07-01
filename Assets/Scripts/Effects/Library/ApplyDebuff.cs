using UnityEngine;

/// <summary>
/// Apply a debuff to the target that was hit. No-op until a debuff system exists; when one is added, this
/// resolves the target's debuff receiver and applies <see cref="debuff"/> directly. It reads only the target,
/// so it is typed to <see cref="ITargetContext"/> — the target is guaranteed present, and it is authorable on
/// any target-bearing hit.
/// </summary>
[System.Serializable]
public sealed class ApplyDebuff : IEffect<ITargetContext>
{
    [SerializeField] private DebuffType debuff = DebuffType.Slow;

    public ApplyDebuff() { }

    public ApplyDebuff(DebuffType debuff) => this.debuff = debuff;

    public void Apply(ITargetContext ctx)
    {
        if (ctx.Target == null)
            return;
        // TODO: no debuff system exists yet. Intentionally a no-op until one is introduced —
        // e.g. ctx.Target.GetComponentInParent<IDebuffable>()?.Apply(debuff);
    }
}
