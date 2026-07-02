/// <summary>
/// Apply a debuff to the target that was hit. No-op until a debuff system exists; when one is added, this
/// resolves the target's debuff receiver and applies the debuff configured on a component on the owner. It
/// reads only the target, so it is typed to <see cref="ITargetContext"/> — the target is guaranteed present,
/// and it is authorable on any target-bearing hit.
///
/// Stateless: the debuff kind/params will live on a component on the owner, not on this effect.
/// </summary>
[System.Serializable]
public sealed class ApplyDebuff : IEffect<ITargetContext>
{
    public void Apply(ITargetContext ctx)
    {
        if (ctx.Target == null)
            return;
        // TODO: no debuff system exists yet. Intentionally a no-op until one is introduced — the debuff
        // kind/params will be read from a component on the owner, e.g.:
        //   if (ctx.Owner.TryGetComponent<DebuffSpec>(out var spec))
        //       ctx.Target.GetComponentInParent<IDebuffable>()?.Apply(spec.Debuff);
    }
}
