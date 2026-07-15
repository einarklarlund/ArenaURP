using System;

/// <summary>
/// The hit channels a projectile hit can be classified into — the flags currency of handler dispatch
/// (component-handler shape, DESIGN.md migration thread). A hit handler declares which channels its behaviour
/// CAN serve in code (<see cref="IHitHandler.SupportedChannels"/>) and which channels this KIND reacts on as
/// per-prefab-copy authoring (<see cref="IHitHandler.EnabledChannels"/> — the LayerMask idiom); a handler runs
/// iff supported ∩ enabled ∩ the current channel is non-empty.
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
