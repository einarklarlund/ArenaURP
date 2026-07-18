using System;

/// <summary>
/// The hit channels a projectile hit can be classified into — the flags currency of handler dispatch (ADR 0005).
/// A hit handler declares which channels this KIND reacts on (<see cref="IHitMessageHandler.EnabledHitChannels"/>,
/// the LayerMask idiom — a serialized field where it varies per prefab copy, a code constant where it doesn't);
/// what a handler ACTS ON is resolved from the hit context by lookup, channel-independently. A handler runs iff its
/// enabled channels ∩ the current channel is non-empty (the guard in <see cref="HitMessage.Functor"/>).
/// </summary>
[Flags]
public enum HitChannels
{
    None = 0,
    /// <summary>The projectile struck a wall / the stage.</summary>
    Surface = 1 << 0,
    /// <summary>The projectile struck a pawn that is not its owner.</summary>
    Player = 1 << 1,
    /// <summary>The projectile overlapped another projectile.</summary>
    Projectile = 1 << 2,
}
