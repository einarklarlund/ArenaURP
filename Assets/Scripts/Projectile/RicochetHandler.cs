using Arena.NetworkedMotion;
using UnityEngine;

/// <summary>
/// Hit handler (component-handler shape, ADR 0003): reflect the source projectile across the struck surface normal
/// and continue on a new authoritative segment (which syncs to every peer). Discovered by presence at the hit site
/// — on the projectile's own prefab, or contributed by an owner's equipped passive (the "bouncy" powerup) — and
/// harvested by <see cref="HitResolver"/>; no serialized list, no courier.
///
/// Pure reflection needs only the hit geometry and the source's motion, present on EVERY channel, so this behaviour
/// CAN serve all three (<see cref="SupportedChannels"/>); which channels a KIND actually bounces on is per-prefab
/// authoring (<see cref="enabledChannels"/> — the LayerMask idiom). It reflects the hit's INCOMING direction
/// (<see cref="HitContext.Direction"/>), captured once at classification, NOT the projectile's live velocity: that
/// is what makes a ricochet IDEMPOTENT for a single hit, so two Ricochets on one impact (one on the projectile, one
/// from the owner's passive) still bounce instead of composing to the identity and driving straight through — the
/// atomic-hit rule. Speed is the live velocity magnitude, which a reflection preserves.
/// </summary>
public sealed class RicochetHandler : MonoBehaviour, IHitHandler
{
    [Tooltip("Which hit channels this KIND bounces on — per-prefab-copy authoring (the LayerMask idiom), " +
             "intersected with SupportedChannels and the current channel at dispatch.")]
    [SerializeField] private HitChannels enabledChannels = HitChannels.Surface;

    public HitChannels SupportedChannels => HitChannels.Surface | HitChannels.Player | HitChannels.Projectile;
    public HitChannels EnabledChannels => enabledChannels;

    public void OnHit(in HitContext context)
    {
        if (context.Motion == null)
            return;

        var state = context.Motion.State;
        float speed = NetworkedMotionProcessor.VelocityAt(context.Motion.Strategy, in state).magnitude;
        if (speed < 0.0001f)
            return;   // a stationary mover (e.g. a laser) has no bounce; leave the segment unchanged

        // Resume at the mover's CENTRE at contact (SourceCenterPoint), which stands off the struck face by the
        // collision radius, NOT the surface impact Point — beginning there would leave the projectile embedded and
        // re-colliding next tick.
        if (RicochetMath.TryReflect(context.Direction, context.Normal, out Vector3 dir, out float _))
            context.Motion.BeginSegment(context.SourceCenterPoint, dir, speed);
    }
}
