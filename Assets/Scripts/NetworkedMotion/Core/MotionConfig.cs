namespace Arena.NetworkedMotion
{
    /// <summary>
    /// Immutable-at-runtime kinematics for the <b>generic</b> networked-motion system: the plain
    /// config struct the pure <see cref="NetworkedMotionProcessor"/> consumes (the functional-core rule
    /// — feed the core a struct, never the authoring ScriptableObject). A projectile's
    /// <c>ProjectileType</c> builds one of these via <c>ToMotionConfig()</c>; any other entity that wants
    /// deterministic closed-form motion can build one too.
    ///
    /// Engine-free apart from the value the strategies need; carries no per-instance state (that lives in
    /// <see cref="NetworkedMotionState"/>).
    /// </summary>
    public readonly struct MotionConfig
    {
        public readonly MotionKind Movement;

        /// <summary>Constant acceleration along facing (Linear).</summary>
        public readonly float Acceleration;

        /// <summary>Vertical gravity per second (Arc); negative = down.</summary>
        public readonly float Gravity;

        /// <summary>Seconds before the mover should be culled by its shell.</summary>
        public readonly float Lifetime;

        /// <summary>Maximum beam length probed when <see cref="Movement"/> is <see cref="MotionKind.Laser"/>.</summary>
        public readonly float MaxRange;

        public MotionConfig(MotionKind movement, float acceleration, float gravity, float lifetime, float maxRange)
        {
            Movement = movement;
            Acceleration = acceleration;
            Gravity = gravity;
            Lifetime = lifetime;
            MaxRange = maxRange;
        }
    }
}
