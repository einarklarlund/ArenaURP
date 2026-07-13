using Arena.NetworkedMotion;
using UnityEngine;

/// <summary>
/// A projectile hit a SURFACE (a wall / the stage). Exposes the source's geometry (<see cref="ISurfaceContext"/>),
/// its MOTION system (<see cref="IMotionContext"/>, for a ricochet) and its own HEALTH system
/// (<see cref="IHealthContext"/>, for durability). No damage systems — a surface isn't a damage target. Operations
/// authorable here: <see cref="LoseHealth"/>, <see cref="Ricochet"/>, <see cref="InstantiateGameObjects"/>,
/// <see cref="SpawnNetworkObjects"/>.
/// </summary>
public sealed class ProjectileSurfaceCollision : IMotionSurfaceContext, IHealthContext
{
    public GameObject Source { get; }
    public NetworkPlayer Instigator { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    public Vector3 Direction { get; }
    public Vector3 SourceCenterPoint { get; }
    public NetworkedMotion Motion { get; }
    public Health SelfHealth { get; }

    public ProjectileSurfaceCollision(GameObject source, NetworkPlayer instigator,
        Vector3 point, Vector3 normal, Vector3 direction, Vector3 sourceCenterPoint,
        NetworkedMotion motion, Health selfHealth)
    {
        Source = source;
        Instigator = instigator;
        Point = point;
        Normal = normal;
        Direction = direction;
        SourceCenterPoint = sourceCenterPoint;
        Motion = motion;
        SelfHealth = selfHealth;
    }
}

/// <summary>
/// A projectile hit a PLAYER. Exposes the surface geometry (<see cref="ISurfaceContext"/>) and the source's own
/// motion + health, so <see cref="Ricochet"/>, <see cref="LoseHealth"/>, the spawners et al. are authorable.
///
/// The hit EXCHANGES (damage/knockback/hitstun) are NOT reachable here: they fire from COMPONENT PRESENCE
/// (dealer on the projectile ∧ receiver on the victim), dispatched by ServerCollision in the same atomic hit —
/// so they stay independent (shove without damaging, stun without shoving) purely by which dealer components the
/// prefab carries. See ADR 0002.
/// </summary>
public sealed class ProjectilePlayerCollision : IMotionSurfaceContext, IHealthContext
{
    public GameObject Source { get; }
    public NetworkPlayer Instigator { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    public Vector3 Direction { get; }
    public Vector3 SourceCenterPoint { get; }
    public NetworkedMotion Motion { get; }
    public Health SelfHealth { get; }

    public ProjectilePlayerCollision(GameObject source, NetworkPlayer instigator,
        Vector3 point, Vector3 normal, Vector3 direction, Vector3 sourceCenterPoint,
        NetworkedMotion motion, Health selfHealth)
    {
        Source = source;
        Instigator = instigator;
        Point = point;
        Normal = normal;
        Direction = direction;
        SourceCenterPoint = sourceCenterPoint;
        Motion = motion;
        SelfHealth = selfHealth;
    }
}

/// <summary>
/// A projectile hit ANOTHER projectile. Exposes the same systems as a player hit PLUS the OTHER projectile's
/// motion system via <see cref="OtherMotion"/> — read by <see cref="DeflectProjectile"/>, which stays typed to
/// this concrete context precisely because it depends on the target being a projectile.
///
/// The hit exchanges deliberately do NOT fire in the projectile channel today: no content deals
/// projectile-vs-projectile damage, and running them here on component presence would let every damage-dealing
/// bullet destroy other bullets. If that interaction is ever wanted, it needs its own decision (a dealer-side
/// victim mask) — see ADR 0002.
/// </summary>
public sealed class ProjectileProjectileCollision : IMotionSurfaceContext, IHealthContext
{
    public GameObject Source { get; }
    public NetworkPlayer Instigator { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    public Vector3 Direction { get; }
    public Vector3 SourceCenterPoint { get; }
    public NetworkedMotion Motion { get; }
    public Health SelfHealth { get; }

    /// <summary>The struck projectile's motion system (its heading/velocity), for a deflect.</summary>
    public NetworkedMotion OtherMotion { get; }

    public ProjectileProjectileCollision(GameObject source, NetworkPlayer instigator,
        Vector3 point, Vector3 normal, Vector3 direction, Vector3 sourceCenterPoint,
        NetworkedMotion motion, Health selfHealth, NetworkedMotion otherMotion)
    {
        Source = source;
        Instigator = instigator;
        Point = point;
        Normal = normal;
        Direction = direction;
        SourceCenterPoint = sourceCenterPoint;
        Motion = motion;
        SelfHealth = selfHealth;
        OtherMotion = otherMotion;
    }
}

/// <summary>
/// An active ability was used: the caster's pawn (<see cref="IEffectContext.Source"/>) aiming from a
/// <see cref="IAimContext.Point"/> in a <see cref="IAimContext.Direction"/>. Exposes only
/// <see cref="IAimContext"/> — no surface, no damage systems, no motion — so an operation authored on an ability
/// can only be one that needs just an origin + aim (e.g. <see cref="SpawnNetworkObjects"/>).
/// </summary>
public sealed class AbilityAim : IAimContext
{
    public GameObject Source { get; }
    public NetworkPlayer Instigator { get; }
    public Vector3 Point { get; }
    public Vector3 Direction { get; }

    public AbilityAim(GameObject source, NetworkPlayer instigator, Vector3 point, Vector3 direction)
    {
        Source = source;
        Instigator = instigator;
        Point = point;
        Direction = direction;
    }
}

/// <summary>
/// A networked object was just SPAWNED and the owner's passive abilities get one chance to transform it — on
/// EVERY peer, at the spawn instant (see <c>SpawnBuffReceiver</c>). Exposes only the base
/// <see cref="IEffectContext"/> capability: the spawned object (<see cref="Owner"/> / <see cref="Source"/>) and
/// the firer (<see cref="OwnerPlayer"/> / <see cref="Instigator"/>). Deliberately NOT a surface/aim/damage/motion
/// context — an on-spawn buff reaches its target system by <c>GetComponent</c> off the spawned object (a generic
/// sibling like <c>NetworkedMotion</c> or <c>SpawnScale</c>), and typing buffs to this exact context keeps the
/// variance-aware picker from offering them in the collision lists (and vice-versa).
/// </summary>
public sealed class OnSpawnContext : IEffectContext
{
    /// <summary>The just-spawned object being buffed (same value as <see cref="Source"/>, named for the buff site).</summary>
    public GameObject Owner { get; }

    /// <summary>The firer whose passives are transforming the spawn (same value as <see cref="Instigator"/>).</summary>
    public NetworkPlayer OwnerPlayer { get; }

    public GameObject Source => Owner;
    public NetworkPlayer Instigator => OwnerPlayer;

    public OnSpawnContext(GameObject owner, NetworkPlayer ownerPlayer)
    {
        Owner = owner;
        OwnerPlayer = ownerPlayer;
    }
}
