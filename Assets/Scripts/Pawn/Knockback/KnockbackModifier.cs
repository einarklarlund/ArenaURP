using System.Collections.Generic;

/// <summary>
/// A multiplicative modifier on a knockback impulse. Stacked multiplicatively, exactly like
/// <see cref="MovementModifier"/> / <see cref="WeaponModifier"/>, and folded by <see cref="KnockbackController"/>.
///
/// The SAME struct serves both halves the controller owns, because both scale the same quantity: the dealer half
/// folds its OutgoingModifiers into the impulse it emits (how hard this bullet shoves), and the receiver half
/// folds its IncomingModifiers into the impulse it accepts (armour, a heavyweight passive). Neither half needs the
/// other's list.
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
