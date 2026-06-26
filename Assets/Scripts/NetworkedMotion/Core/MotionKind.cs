namespace Arena.NetworkedMotion
{
    /// <summary>Selects the pure, closed-form motion strategy a <see cref="NetworkedMotionState"/> evolves under.</summary>
    public enum MotionKind
    {
        Linear,
        Arc,
        Laser,
    }
}
