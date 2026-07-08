using System.Collections.Generic;
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
/// A projectile hit a PLAYER. Exposes every system a projectile hit can reach: the damage EXCHANGE
/// (<see cref="IDamageExchangeContext"/> — the source's <see cref="Damage"/> + owner buffs, and the struck
/// <see cref="IDamageable"/>), the surface (<see cref="ISurfaceContext"/>), and the source's own motion + health.
/// So <see cref="DealDamage"/>, <see cref="Ricochet"/>, <see cref="LoseHealth"/> et al. are all authorable, each
/// reaching only the system it names.
/// </summary>
public sealed class ProjectilePlayerCollision :
    IDamageExchangeContext, IMotionSurfaceContext, IHealthContext
{
    public GameObject Source { get; }
    public NetworkPlayer Instigator { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    public Vector3 Direction { get; }
    public Vector3 SourceCenterPoint { get; }
    public Damage DamageSource { get; }
    public IDamageable DamageTarget { get; }
    public NetworkedMotion Motion { get; }
    public Health SelfHealth { get; }

    public ProjectilePlayerCollision(GameObject source, NetworkPlayer instigator,
        Vector3 point, Vector3 normal, Vector3 direction, Vector3 sourceCenterPoint,
        Damage damageSource, IDamageable damageTarget,
        NetworkedMotion motion, Health selfHealth)
    {
        Source = source;
        Instigator = instigator;
        Point = point;
        Normal = normal;
        Direction = direction;
        SourceCenterPoint = sourceCenterPoint;
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
    IDamageExchangeContext, IMotionSurfaceContext, IHealthContext
{
    public GameObject Source { get; }
    public NetworkPlayer Instigator { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    public Vector3 Direction { get; }
    public Vector3 SourceCenterPoint { get; }
    public Damage DamageSource { get; }
    public IDamageable DamageTarget { get; }
    public NetworkedMotion Motion { get; }
    public Health SelfHealth { get; }

    /// <summary>The struck projectile's motion system (its heading/velocity), for a deflect.</summary>
    public NetworkedMotion OtherMotion { get; }

    public ProjectileProjectileCollision(GameObject source, NetworkPlayer instigator,
        Vector3 point, Vector3 normal, Vector3 direction, Vector3 sourceCenterPoint,
        Damage damageSource, IDamageable damageTarget,
        NetworkedMotion motion, Health selfHealth, NetworkedMotion otherMotion)
    {
        Source = source;
        Instigator = instigator;
        Point = point;
        Normal = normal;
        Direction = direction;
        SourceCenterPoint = sourceCenterPoint;
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

/// <summary>
/// The owner's PASSIVE abilities get to fold their standing stat buffs into the pawn. Exposes both modifier
/// systems as contribution SINKS (<see cref="IMovementModifierContext"/> + <see cref="IWeaponModifierContext"/>),
/// so one authored list can carry a mix of movement and weapon buffs. Built by <c>PawnPassiveAbilities</c> each
/// time the passive roster changes; every buff a passive contributes is appended to the two sink lists, which the
/// facade then applies to the pawn's live <c>PawnMovement.Modifiers</c>/<c>PawnWeapon.Modifiers</c> (and removes
/// on the next re-derive / when the passive is dropped). A stat buff never touches a pawn component directly — it
/// only contributes a modifier, matching the "fold at apply time, never mutate authored values" rule. Not a
/// surface/aim/damage/motion context, so the variance-aware picker keeps buffs out of the collision lists.
/// </summary>
public sealed class PawnStatBuff : IMovementModifierContext, IWeaponModifierContext
{
    public GameObject Source { get; }
    public NetworkPlayer Instigator { get; }

    private readonly List<MovementModifier> _movementSink;
    private readonly List<WeaponModifier> _weaponSink;

    /// <param name="source">The pawn whose passives are contributing the buffs.</param>
    /// <param name="instigator">The pawn's controlling player (may be null; buffs don't read it).</param>
    /// <param name="movementSink">List the contributed movement modifiers are appended to.</param>
    /// <param name="weaponSink">List the contributed weapon modifiers are appended to.</param>
    public PawnStatBuff(GameObject source, NetworkPlayer instigator,
        List<MovementModifier> movementSink, List<WeaponModifier> weaponSink)
    {
        Source = source;
        Instigator = instigator;
        _movementSink = movementSink;
        _weaponSink = weaponSink;
    }

    public void AddMovementModifier(MovementModifier modifier) => _movementSink.Add(modifier);
    public void AddWeaponModifier(WeaponModifier modifier) => _weaponSink.Add(modifier);
}
