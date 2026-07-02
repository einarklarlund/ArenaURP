using UnityEngine;

/// <summary>
/// A projectile hit a SURFACE (a wall / the stage). Implements <see cref="ISurfaceHitContext"/> = impact point
/// + travel direction (<see cref="IAimContext"/>) + surface normal (<see cref="ISurfaceContext"/>). No
/// <see cref="ITargetContext"/> — surfaces aren't operated on, only reflected off / marked with VFX. Operations
/// authored here: <see cref="LoseHealth"/>, <see cref="Ricochet"/>, <see cref="InstantiateGameObjects"/>,
/// <see cref="SpawnNetworkObjects"/>.
/// </summary>
public sealed class ProjectileSurfaceCollision : ISurfaceHitContext
{
    public GameObject Owner { get; }
    public NetworkPlayer OwnerPlayer { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    public Vector3 Direction { get; }

    public ProjectileSurfaceCollision(GameObject owner, NetworkPlayer ownerPlayer,
        Vector3 point, Vector3 normal, Vector3 direction)
    {
        Owner = owner;
        OwnerPlayer = ownerPlayer;
        Point = point;
        Normal = normal;
        Direction = direction;
    }
}

/// <summary>
/// A projectile hit a PLAYER. Implements both <see cref="ISurfaceHitContext"/> (point + direction + normal) and
/// <see cref="ITargetHitContext"/> (point + direction + target) — the extra <see cref="ITargetContext.Target"/>
/// is the hit player, so target-consuming operations (<see cref="DealDamage"/>, <see cref="ApplyDebuff"/>) read
/// it directly with no null-checks for "did I have a target".
/// </summary>
public sealed class ProjectilePlayerCollision : ISurfaceHitContext, ITargetHitContext
{
    public GameObject Owner { get; }
    public NetworkPlayer OwnerPlayer { get; }
    public GameObject Target { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    public Vector3 Direction { get; }

    public ProjectilePlayerCollision(GameObject owner, GameObject target, NetworkPlayer ownerPlayer,
        Vector3 point, Vector3 normal, Vector3 direction)
    {
        Owner = owner;
        Target = target;
        OwnerPlayer = ownerPlayer;
        Point = point;
        Normal = normal;
        Direction = direction;
    }
}

/// <summary>
/// A projectile hit ANOTHER projectile. Implements the same interfaces as a player hit (<see cref="ISurfaceHitContext"/>
/// + <see cref="ITargetHitContext"/>); the <see cref="ITargetContext.Target"/> is the other mover (its
/// NetworkedMotion is read by <see cref="DeflectProjectile"/>, which stays typed to this concrete context).
/// </summary>
public sealed class ProjectileProjectileCollision : ISurfaceHitContext, ITargetHitContext
{
    public GameObject Owner { get; }
    public NetworkPlayer OwnerPlayer { get; }
    public GameObject Target { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    public Vector3 Direction { get; }

    public ProjectileProjectileCollision(GameObject owner, GameObject target, NetworkPlayer ownerPlayer,
        Vector3 point, Vector3 normal, Vector3 direction)
    {
        Owner = owner;
        Target = target;
        OwnerPlayer = ownerPlayer;
        Point = point;
        Normal = normal;
        Direction = direction;
    }
}

/// <summary>
/// An active ability was used: the caster's pawn (<see cref="IEffectContext.Owner"/>) aiming from a
/// <see cref="IAimContext.Point"/> in a <see cref="IAimContext.Direction"/>. Composes only
/// <see cref="IAimContext"/> — no surface normal and no target — so an operation authored on an ability can
/// only be one that needs just an origin + aim (e.g. <see cref="SpawnNetworkObjects"/>).
/// </summary>
public sealed class AbilityAim : IAimContext
{
    public GameObject Owner { get; }
    public NetworkPlayer OwnerPlayer { get; }
    public Vector3 Point { get; }
    public Vector3 Direction { get; }

    public AbilityAim(GameObject owner, NetworkPlayer ownerPlayer, Vector3 point, Vector3 direction)
    {
        Owner = owner;
        OwnerPlayer = ownerPlayer;
        Point = point;
        Direction = direction;
    }
}
