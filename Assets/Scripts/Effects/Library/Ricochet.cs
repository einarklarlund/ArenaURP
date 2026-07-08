using Arena.NetworkedMotion;
using UnityEngine;

/// <summary>
/// Reflect the source across the surface normal and continue on a new segment. Coupled to the MOTION
/// system: it names <see cref="IMotionSurfaceContext"/> and takes the source's resolved
/// <see cref="NetworkedMotion"/> handle from the context (no <c>GetComponent</c>), reflects the hit's INCOMING
/// direction with the pure <see cref="RicochetMath"/>, and begins a new authoritative segment (which syncs to
/// every peer). Authorable on any collision (reflect off a wall, a player, or another projectile).
///
/// It reflects <see cref="IAimContext.Direction"/> — the incoming heading captured ONCE at hit classification,
/// before any effect ran — NOT the projectile's live velocity. That is what makes a ricochet IDEMPOTENT for a
/// single hit: if two Ricochets resolve on the same impact (e.g. one authored on the projectile's own
/// <c>OnSurface</c> and another contributed by the owner's "bouncy" powerup), reflecting the live velocity twice
/// composes to the identity (<c>reflect(reflect(v)) == v</c>) and the shot drives straight through the wall;
/// reflecting the fixed incoming direction, every Ricochet on the hit computes the SAME bounce, so stacking or
/// reordering can never cancel it — the atomic-hit rule (authored order can't change a hit's outcome). Speed is
/// taken from the live velocity magnitude, which a reflection preserves, so a prior bounce this hit doesn't skew it.
/// </summary>
[System.Serializable]
public sealed class Ricochet : IEffect<IMotionSurfaceContext>
{
    public void Apply(IMotionSurfaceContext ctx)
    {
        if (ctx.Motion == null)
            return;

        var state = ctx.Motion.State;
        float speed = NetworkedMotionProcessor.VelocityAt(ctx.Motion.Strategy, in state).magnitude;
        if (speed < 0.0001f)
            return;   // a stationary mover (e.g. a laser) has no bounce; leave the segment unchanged

        // Resume at the mover's CENTRE at contact (SourceCenterPoint), NOT the surface impact Point: the sweep's
        // Point sits a radius deep on the struck surface, so beginning there would leave the projectile embedded and
        // re-colliding next tick. SourceCenterPoint stands it off tangent to the surface.
        if (RicochetMath.TryReflect(ctx.Direction, ctx.Normal, out Vector3 dir, out float _))
            ctx.Motion.BeginSegment(ctx.SourceCenterPoint, dir, speed);
    }
}
