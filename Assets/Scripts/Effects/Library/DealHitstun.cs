/// <summary>
/// Stagger what was hit: prevent it from starting a new fire event for a duration. Reads the SOURCE's
/// <see cref="HitstunDealer"/> for the seconds (its authored config folded with the dealer's outgoing modifiers)
/// and hands them to the struck <see cref="HitstunReceiver"/>, which folds its own stun resistance before
/// applying. A stun EXTENDS an existing one rather than replacing it, so hit order can never shorten a stagger.
///
/// A COURIER, exactly like <see cref="DealDamage"/> and <see cref="DealKnockback"/>: no config, no arithmetic,
/// both folds owned by the ends. It names <see cref="IHitstunExchangeContext"/> — the leanest capability that
/// serves it, with no geometry, since a duration has none.
///
/// An in-flight burst is deliberately NOT cancelled; the stun blocks the next fire event. See
/// <c>WeaponProcessor</c>.
/// </summary>
[System.Serializable]
public sealed class DealHitstun : IEffect<IHitstunExchangeContext>
{
    public void Apply(IHitstunExchangeContext ctx)
    {
        if (ctx.HitstunSource == null || ctx.HitstunTarget == null)
            return;

        ctx.HitstunTarget.ApplyHitstun(ctx.HitstunSource.EffectiveSeconds);
    }
}
