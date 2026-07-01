/// <summary>
/// Debuffs an operation can apply to a hit player. Placeholder vocabulary until a debuff system exists; the
/// Unity-coupled <see cref="ApplyDebuff"/> operation maps these to concrete behaviour.
/// </summary>
public enum DebuffType
{
    None,
    Slow,
    Burn,
}
