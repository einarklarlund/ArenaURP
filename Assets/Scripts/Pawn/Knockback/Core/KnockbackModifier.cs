using System.Collections.Generic;

/// <summary>
/// A multiplicative modifier on a knockback impulse. Stacked multiplicatively, exactly like
/// <see cref="MovementModifier"/> / <see cref="WeaponModifier"/>, and folded by
/// <see cref="KnockbackHelper.FoldMultiplier"/>.
///
/// The SAME struct serves both ends of a knockback exchange, because both ends scale the same quantity:
/// a <c>KnockbackDealer</c> folds its list into the impulse it emits (how hard this bullet shoves), and a
/// knockback receiver folds its own list into the impulse it accepts (armour, a heavyweight passive). Neither end
/// knows the other exists.
/// </summary>
[System.Serializable]
public struct KnockbackModifier
{
    /// <summary>Multiplier on the impulse magnitude. Less than 1 = weaker knockback.</summary>
    public float ImpulseMultiplier;

    /// <summary>A modifier that has no effect (identity), so an absent contributor changes nothing.</summary>
    public static KnockbackModifier Identity => new()
    {
        ImpulseMultiplier = 1f,
    };
}
