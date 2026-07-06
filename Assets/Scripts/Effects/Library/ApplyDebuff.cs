/// <summary>
/// Apply a debuff to the thing that was hit. No-op until a debuff system exists; when one is added it will name
/// its own capability (an <c>IDebuffTargetContext</c> exposing the resolved debuff receiver), the same way this
/// operation family couples to a SYSTEM rather than an archetype. For now it names
/// <see cref="IDamageTargetContext"/> — the leanest capability that guarantees "something was struck" — so it is
/// authorable on any hit and does no <c>GetComponent</c>. Stateless.
/// </summary>
[System.Serializable]
public sealed class ApplyDebuff : IEffect<IDamageTargetContext>
{
    public void Apply(IDamageTargetContext ctx)
    {
        if (ctx.DamageTarget == null)
            return;
        // TODO: no debuff system exists yet. Intentionally a no-op until one is introduced — when it lands, add
        // an IDebuffTargetContext capability that resolves the target's debuff receiver at the collision site,
        // and read the debuff spec from a component on the source, e.g.:
        //   if (ctx.Source.TryGetComponent<DebuffSpec>(out var spec)) ctx.DebuffTarget.Apply(spec.Debuff);
    }
}
