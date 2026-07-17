using Arena.NetworkedMotion;
using UnityEngine;

/// <summary>
/// The ONE hit context handed to every <see cref="IHitHandler"/> — the handles the hit site resolved once, so a
/// handler never GetComponents off a raw archetype and gambles (component-handler shape, DESIGN.md migration
/// thread). Which channel a hit belongs to is the CALL SITE (<see cref="HitResolver.ResolveSurfaceHit"/> /
/// <see cref="HitResolver.ResolvePlayerHit"/> / <see cref="HitResolver.ResolveProjectileHit"/>), not a context
/// subtype; per-channel population is a documented contract: <see cref="OtherMotion"/> is non-null only on the
/// Projectile channel, and the supported-channels filter is what keeps it non-null where consumed.
///
/// Deliberately does NOT carry damage-buff-channel fields (outgoing damage/knockback modifiers) — that is the
/// deferred damage-buff design session's surface (DESIGN.md open question).
/// </summary>
public readonly struct HitContext
{
    /// <summary>The channel this hit was classified into (exactly one flag set).</summary>
    public readonly HitChannels Channel;
    /// <summary>The dealing entity — the projectile whose hit is being resolved.</summary>
    public readonly GameObject Source;
    /// <summary>The player (human or bot) whose action caused the hit; null when the owner is gone.</summary>
    public readonly NetworkPlayer Instigator;
    /// <summary>The struck object as reported by the sweep — a collider's GameObject; receivers/rosters resolve
    /// from it with one GetComponentInParent walk to the entity root.</summary>
    public readonly GameObject Victim;
    /// <summary>The hit point on the struck surface.</summary>
    public readonly Vector3 Point;
    /// <summary>The struck surface's normal at the hit point.</summary>
    public readonly Vector3 Normal;
    /// <summary>The projectile's travel direction at the hit.</summary>
    public readonly Vector3 Direction;
    /// <summary>This mover's centre at contact — stands off the surface point by the sweep radius along the
    /// normal, so a redirect resumes tangent to the surface instead of embedded in it.</summary>
    public readonly Vector3 SourceCenterPoint;
    /// <summary>The source projectile's motion (velocity, strategy, age).</summary>
    public readonly NetworkedMotion Motion;
    /// <summary>The source projectile's own durability, if it has one.</summary>
    public readonly HealthController SelfHealth;
    /// <summary>The OTHER projectile's motion — populated only on the Projectile channel.</summary>
    public readonly NetworkedMotion OtherMotion;

    public HitContext(HitChannels channel, GameObject source, NetworkPlayer instigator, GameObject victim,
        Vector3 point, Vector3 normal, Vector3 direction, Vector3 sourceCenterPoint,
        NetworkedMotion motion, HealthController selfHealth, NetworkedMotion otherMotion = null)
    {
        Channel = channel;
        Source = source;
        Instigator = instigator;
        Victim = victim;
        Point = point;
        Normal = normal;
        Direction = direction;
        SourceCenterPoint = sourceCenterPoint;
        Motion = motion;
        SelfHealth = selfHealth;
        OtherMotion = otherMotion;
    }
}
