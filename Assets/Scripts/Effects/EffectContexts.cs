using UnityEngine;

/// <summary>
/// A projectile hit a SURFACE (a wall / the stage). Carries the impact point, the surface normal, and the
/// projectile's travel direction. No target GameObject — surfaces aren't operated on, only reflected off /
/// marked with VFX. Operations authored here: <see cref="LoseHealth"/>, <see cref="Ricochet"/>,
/// <see cref="SpawnVfx"/>, <see cref="SpawnProjectiles"/>.
/// </summary>
public sealed class ProjectileSurfaceCollision : IEffectContext
{
    public GameObject Invoker { get; }
    public int InvokerPlayerId { get; }
    public NetworkPlayer InvokerPlayer { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    public Vector3 Direction { get; }

    public ProjectileSurfaceCollision(GameObject invoker, int invokerPlayerId, NetworkPlayer invokerPlayer,
        Vector3 point, Vector3 normal, Vector3 direction)
    {
        Invoker = invoker;
        InvokerPlayerId = invokerPlayerId;
        InvokerPlayer = invokerPlayer;
        Point = point;
        Normal = normal;
        Direction = direction;
    }
}

/// <summary>
/// A projectile hit a PLAYER. Adds the hit player's <see cref="Target"/> GameObject to the collision data, so
/// target-consuming operations (<see cref="DealDamage"/>, <see cref="ApplyDebuff"/>) can read it directly
/// with no null-checks for "did I have a target".
/// </summary>
public sealed class ProjectilePlayerCollision : IEffectContext
{
    public GameObject Invoker { get; }
    public int InvokerPlayerId { get; }
    public NetworkPlayer InvokerPlayer { get; }
    public GameObject Target { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    public Vector3 Direction { get; }

    public ProjectilePlayerCollision(GameObject invoker, GameObject target, int invokerPlayerId,
        NetworkPlayer invokerPlayer, Vector3 point, Vector3 normal, Vector3 direction)
    {
        Invoker = invoker;
        Target = target;
        InvokerPlayerId = invokerPlayerId;
        InvokerPlayer = invokerPlayer;
        Point = point;
        Normal = normal;
        Direction = direction;
    }
}

/// <summary>
/// A projectile hit ANOTHER projectile. Adds the other mover's <see cref="Target"/> GameObject (its
/// NetworkedMotion is read by <see cref="DeflectProjectile"/>).
/// </summary>
public sealed class ProjectileProjectileCollision : IEffectContext
{
    public GameObject Invoker { get; }
    public int InvokerPlayerId { get; }
    public NetworkPlayer InvokerPlayer { get; }
    public GameObject Target { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    public Vector3 Direction { get; }

    public ProjectileProjectileCollision(GameObject invoker, GameObject target, int invokerPlayerId,
        NetworkPlayer invokerPlayer, Vector3 point, Vector3 normal, Vector3 direction)
    {
        Invoker = invoker;
        Target = target;
        InvokerPlayerId = invokerPlayerId;
        InvokerPlayer = invokerPlayer;
        Point = point;
        Normal = normal;
        Direction = direction;
    }
}

/// <summary>
/// An active ability was used: the caster's pawn (<see cref="IEffectContext.Invoker"/>) aiming from a
/// <see cref="Point"/> in a <see cref="Direction"/>. No target and no surface normal — so an operation
/// authored on an ability can only be one that needs just an origin + aim (e.g. <see cref="SpawnProjectiles"/>).
/// </summary>
public sealed class AbilityAim : IEffectContext
{
    public GameObject Invoker { get; }
    public int InvokerPlayerId { get; }
    public NetworkPlayer InvokerPlayer { get; }
    public Vector3 Point { get; }
    public Vector3 Direction { get; }

    public AbilityAim(GameObject invoker, int invokerPlayerId, NetworkPlayer invokerPlayer,
        Vector3 point, Vector3 direction)
    {
        Invoker = invoker;
        InvokerPlayerId = invokerPlayerId;
        InvokerPlayer = invokerPlayer;
        Point = point;
        Direction = direction;
    }
}
