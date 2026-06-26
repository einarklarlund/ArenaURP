namespace Arena.NetworkedMotion
{
    /// <summary>
    /// Maps a <see cref="MotionKind"/> to its stateless strategy singleton. The strategies hold no
    /// per-instance state (all state is in <see cref="NetworkedMotionState"/>), so one shared instance per
    /// kind is reused across every mover.
    /// </summary>
    public static class MotionSelector
    {
        private static readonly IMotionStrategy _linear = new LinearMotion();
        private static readonly IMotionStrategy _arc = new ArcMotion();
        private static readonly IMotionStrategy _laser = new LaserMotion();

        public static IMotionStrategy For(MotionKind kind) => kind switch
        {
            MotionKind.Arc => _arc,
            MotionKind.Laser => _laser,
            _ => _linear,
        };
    }
}
