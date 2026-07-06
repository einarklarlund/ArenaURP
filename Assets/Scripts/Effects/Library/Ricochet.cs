using Arena.NetworkedMotion;
using UnityEngine;

/// <summary>
/// Reflect the source's velocity across the surface normal and continue on a new segment. Coupled to the MOTION
/// system: it names <see cref="IMotionSurfaceContext"/> and takes the source's resolved
/// <see cref="NetworkedMotion"/> handle from the context (no <c>GetComponent</c>), reads its live velocity,
/// reflects it with the pure <see cref="RicochetMath"/>, and begins a new authoritative segment (which syncs to
/// every peer). Authorable on any collision (reflect off a wall, a player, or another projectile).
/// </summary>
[System.Serializable]
public sealed class Ricochet : IEffect<IMotionSurfaceContext>
{
    public void Apply(IMotionSurfaceContext ctx)
    {
        if (ctx.Motion == null)
            return;

        var state = ctx.Motion.State;
        Vector3 vel = NetworkedMotionProcessor.VelocityAt(ctx.Motion.Strategy, in state);

        if (RicochetMath.TryReflect(vel, ctx.Normal, out Vector3 dir, out float speed))
            ctx.Motion.BeginSegment(ctx.Point, dir, speed);
    }
}
