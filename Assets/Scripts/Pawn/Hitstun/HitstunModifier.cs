/// <summary>
/// A multiplicative modifier on a hitstun duration. Stacked multiplicatively, exactly like
/// <see cref="KnockbackModifier"/>, and folded by <see cref="HitstunController"/>.
///
/// The SAME struct serves both halves the controller owns: the dealer half folds its OutgoingModifiers into the
/// duration it emits, and the receiver half folds its IncomingModifiers into the duration it accepts (stun
/// resistance). Neither half needs the other's list.
/// </summary>
[System.Serializable]
public struct HitstunModifier
{
    /// <summary>Multiplier on the stun duration. Less than 1 = shorter stun.</summary>
    public float DurationMultiplier;

    /// <summary>A modifier that has no effect (identity), so an absent contributor changes nothing.</summary>
    public static HitstunModifier Identity => new()
    {
        DurationMultiplier = 1f,
    };
}
