using Arena.NetworkedMotion;
using UnityEngine;

/// <summary>
/// Reflect the invoking projectile's velocity across the surface normal and continue on a new segment.
/// Reads the invoker's <see cref="NetworkedMotion"/> for the live velocity, reflects it with the pure
/// <see cref="RicochetMath"/>, and begins a new authoritative segment (which syncs to every peer). Needs a
/// surface — no-ops on a context without one (e.g. an ability's aim), so it ricochets off nothing.
/// </summary>
public sealed class Ricochet : IEffect
{
    public void Apply(IEffectContext ctx)
    {
        if (ctx.Invoker == null || !ctx.Invoker.TryGetComponent<NetworkedMotion>(out var motion))
            return;
        if (ctx is not ISurfaceContext surface)
            return;

        var state = motion.State;
        var config = motion.Config;
        Vector3 vel = NetworkedMotionProcessor.VelocityAt(in state, in config);

        if (RicochetMath.TryReflect(vel, surface.Normal, out Vector3 dir, out float speed))
            motion.BeginSegment(surface.Point, dir, speed);
    }
}
