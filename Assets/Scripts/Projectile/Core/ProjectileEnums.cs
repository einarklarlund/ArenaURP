namespace Arena.Projectile
{
    /// <summary>Which reaction list a hit routes to.</summary>
    public enum HitCategory
    {
        Surface,
        Enemy,
        Projectile,
    }

    /// <summary>Selects the pure movement strategy for a projectile type.</summary>
    public enum MovementKind
    {
        Linear,
        Arc,
        Laser,
    }

    /// <summary>
    /// Debuffs a projectile reaction can apply to a hit player through the seam.
    /// Engine-free placeholder enum; the Unity seam maps these to concrete effects.
    /// </summary>
    public enum DebuffType
    {
        None,
        Slow,
        Burn,
    }
}
