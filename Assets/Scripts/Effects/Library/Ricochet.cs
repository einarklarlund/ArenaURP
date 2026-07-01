using Arena.NetworkedMotion;
using UnityEngine;

/// <summary>
/// Reflect the invoking projectile's velocity across the surface normal and continue on a new segment. Reads
/// the invoker's <see cref="NetworkedMotion"/> for the live velocity, reflects it with the pure
/// <see cref="RicochetMath"/>, and begins a new authoritative segment (which syncs to every peer). Typed to
/// <see cref="ProjectileSurfaceCollision"/>, so a surface normal is always present.
/// </summary>
[System.Serializable]
public sealed class Ricochet : IEffect<ProjectileSurfaceCollision>
{
    public void Apply(ProjectileSurfaceCollision ctx)
    {
        if (ctx.Invoker == null || !ctx.Invoker.TryGetComponent<NetworkedMotion>(out var motion))
            return;

        var state = motion.State;
        Vector3 vel = NetworkedMotionProcessor.VelocityAt(motion.Strategy, in state);

        if (RicochetMath.TryReflect(vel, ctx.Normal, out Vector3 dir, out float speed))
            motion.BeginSegment(ctx.Point, dir, speed);
    }
}
