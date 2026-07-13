/// <summary>
/// A multiplicative modifier on a hitstun duration. Stacked multiplicatively, exactly like
/// <see cref="KnockbackModifier"/>, and folded by <see cref="HitstunProcessor.FoldMultiplier"/>.
///
/// The SAME struct serves both ends of a hitstun exchange: a <c>HitstunDealer</c> folds its list into the
/// duration it emits, and a hitstun receiver folds its own list into the duration it accepts (stun resistance).
/// Neither end knows the other exists.
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
