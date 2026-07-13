/// <summary>
/// Shove what was hit. Reads the SOURCE's <see cref="KnockbackDealer"/> for the impulse (its authored config
/// folded with the dealer's outgoing modifiers, aimed along the hit direction) and hands it to the struck
/// <see cref="KnockbackReceiver"/>, which folds its own resistance before applying.
///
/// A COURIER: it owns no config and performs no arithmetic. Both folds belong to the ends — the dealer decides
/// how hard it shoves, the receiver decides how hard it is to shove — so neither knows the other exists and a new
/// contributor on either side needs no change here. Coupled to the knockback SYSTEM, not to any archetype: it
/// names <see cref="IKnockbackExchangeContext"/>, so both components are resolved by the site that built the
/// context and no <c>GetComponent</c> is gambled. The same operation lands on a pawn or on anything else carrying
/// a receiver.
///
/// It aims along <see cref="IAimContext.Direction"/> — the incoming heading captured once at hit classification,
/// before any operation ran — so a ricochet earlier in the same hit's list cannot skew the shove direction.
/// </summary>
[System.Serializable]
public sealed class DealKnockback : IEffect<IKnockbackExchangeContext>
{
    public void Apply(IKnockbackExchangeContext ctx)
    {
        if (ctx.KnockbackSource == null || ctx.KnockbackTarget == null)
            return;

        ctx.KnockbackTarget.ApplyImpulse(ctx.KnockbackSource.EffectiveImpulse(ctx.Direction));
    }
}
