namespace Arena.Projectile
{
    /// <summary>
    /// Maps a <see cref="MovementKind"/> to its stateless strategy singleton. The strategies hold no
    /// per-instance state (all state is in <see cref="ProjectileState"/>), so one shared instance per
    /// kind is reused across every projectile.
    /// </summary>
    public static class MovementSelector
    {
        private static readonly IProjectileMovement _linear = new LinearMovement();
        private static readonly IProjectileMovement _arc = new ArcMovement();
        private static readonly IProjectileMovement _laser = new LaserMovement();

        public static IProjectileMovement For(MovementKind kind) => kind switch
        {
            MovementKind.Arc => _arc,
            MovementKind.Laser => _laser,
            _ => _linear,
        };
    }
}
