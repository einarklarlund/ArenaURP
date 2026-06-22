namespace Arena.Steering.Core
{
    public interface ITargetProvider
    {
        /// <summary>
        /// Always returns a TargetInfo. If no target is available, the returned struct has
        /// HasTarget == false; otherwise Position/Velocity/Transform describe the target.
        /// Strategies are expected to handle both cases.
        /// </summary>
        TargetInfo GetTargetInfo(PerceptionContext ctx);

        void DrawGizmos(PerceptionContext ctx);
    }
}
