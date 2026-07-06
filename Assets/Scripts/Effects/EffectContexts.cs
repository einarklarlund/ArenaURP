using Arena.NetworkedMotion;
using UnityEngine;

/// <summary>
/// A projectile hit a SURFACE (a wall / the stage). Exposes the source's geometry (<see cref="ISurfaceContext"/>),
/// its MOTION system (<see cref="IMotionContext"/>, for a ricochet) and its own HEALTH system
/// (<see cref="IHealthContext"/>, for durability). No damage systems — a surface isn't a damage target. Operations
/// authorable here: <see cref="LoseHealth"/>, <see cref="Ricochet"/>, <see cref="InstantiateGameObjects"/>,
/// <see cref="SpawnNetworkObjects"/>.
/// </summary>
public sealed class ProjectileSurfaceCollision : ISurfaceContext, IMotionContext, IHealthContext
{
    public GameObject Source { get; }
    public NetworkPlayer Instigator { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    public Vector3 Direction { get; }
    public NetworkedMotion Motion { get; }
    public Health SelfHealth { get; }

    public ProjectileSurfaceCollision(GameObject source, NetworkPlayer instigator,
        Vector3 point, Vector3 normal, Vector3 direction,
        NetworkedMotion motion, Health selfHealth)
    {
        Source = source;
        Instigator = instigator;
        Point = point;
        Normal = normal;
        Direction = direction;
        Motion = motion;
        SelfHealth = selfHealth;
    }
}

/// <summary>
/// A projectile hit a PLAYER. Exposes every system a projectile hit can reach: the damage EXCHANGE
/// (<see cref="IDamageExchangeContext"/> — the source's <see cref="Damage"/> + owner buffs, and the struck
/// <see cref="IDamageable"/>), the surface (<see cref="ISurfaceContext"/>), and the source's own motion + health.
/// So <see cref="DealDamage"/>, <see cref="Ricochet"/>, <see cref="LoseHealth"/> et al. are all authorable, each
/// reaching only the system it names.
/// </summary>
public sealed class ProjectilePlayerCollision :
    IDamageExchangeContext, ISurfaceContext, IMotionContext, IHealthContext
{
    public GameObject Source { get; }
    public NetworkPlayer Instigator { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    public Vector3 Direction { get; }
    public Damage DamageSource { get; }
    public IDamageable DamageTarget { get; }
    public NetworkedMotion Motion { get; }
    public Health SelfHealth { get; }

    public ProjectilePlayerCollision(GameObject source, NetworkPlayer instigator,
        Vector3 point, Vector3 normal, Vector3 direction,
        Damage damageSource, IDamageable damageTarget,
        NetworkedMotion motion, Health selfHealth)
    {
        Source = source;
        Instigator = instigator;
        Point = point;
        Normal = normal;
        Direction = direction;
        DamageSource = damageSource;
        DamageTarget = damageTarget;
        Motion = motion;
        SelfHealth = selfHealth;
    }
}

/// <summary>
/// A projectile hit ANOTHER projectile. Exposes the same systems as a player hit (a projectile can carry a
/// <see cref="Health"/> and be an <see cref="IDamageable"/> target), PLUS the OTHER projectile's motion system
/// via <see cref="OtherMotion"/> — read by <see cref="DeflectProjectile"/>, which stays typed to this concrete
/// context precisely because it depends on the target being a projectile.
/// </summary>
public sealed class ProjectileProjectileCollision :
    IDamageExchangeContext, ISurfaceContext, IMotionContext, IHealthContext
{
    public GameObject Source { get; }
    public NetworkPlayer Instigator { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    public Vector3 Direction { get; }
    public Damage DamageSource { get; }
    public IDamageable DamageTarget { get; }
    public NetworkedMotion Motion { get; }
    public Health SelfHealth { get; }

    /// <summary>The struck projectile's motion system (its heading/velocity), for a deflect.</summary>
    public NetworkedMotion OtherMotion { get; }

    public ProjectileProjectileCollision(GameObject source, NetworkPlayer instigator,
        Vector3 point, Vector3 normal, Vector3 direction,
        Damage damageSource, IDamageable damageTarget,
        NetworkedMotion motion, Health selfHealth, NetworkedMotion otherMotion)
    {
        Source = source;
        Instigator = instigator;
        Point = point;
        Normal = normal;
        Direction = direction;
        DamageSource = damageSource;
        DamageTarget = damageTarget;
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
