namespace Arena.Steering.Core
{
    /// <summary>
    /// Per-entity runtime movement instance. Holds entity-specific state and writes
    /// to ctx.Body via a deterministic Tick(). Strategies assume target is valid;
    /// missing-target handling is the FSM's responsibility.
    /// </summary>
    public interface IMovementStrategy
    {
        void Tick(PerceptionContext ctx, TargetInfo target);
        void DrawGizmos(PerceptionContext ctx, TargetInfo target);
    }
}
