using System.Collections.Generic;
using Arena.NetworkedMotion;
using MackySoft.SerializeReferenceExtensions;
using UnityEngine;

/// <summary>
/// Shared, immutable-at-runtime per-kind projectile data (Type Object + Flyweight): one asset per projectile
/// kind (bullet, grenade, laser, arc, homing). A projectile is a COMPOSITION of generic systems
/// (NetworkedMotion / ServerCollision / Health / Damage); this type configures each of them and supplies the
/// hit operations.
///
/// Motion and hit reactions are AUTHORED AS DATA on this asset, not composed in code: <see cref="Motion"/> is
/// a <c>[SerializeReference]</c> <see cref="IMotionStrategy"/> (Linear/Arc/Laser, each carrying its own
/// tuning), and each hit category is a <c>[SerializeReference]</c> list of <see cref="IEffect{TContext}"/>
/// typed to that category's context — so the inspector's dropdown only offers operations valid for that hit.
/// New kinds = new assets composing existing motion + operation building blocks. Per-run powerups add MORE
/// operations at runtime via the owner-contribution path (see PawnAbilities), resolved server-side.
///
/// The lists resolve locally and identically on every peer (they are data on the shared SO), so nothing rides
/// the wire — the same determinism guarantee the old code-composed defaults had.
/// </summary>
[CreateAssetMenu(menuName = "Projectiles/Type", fileName = "ProjectileType")]
public sealed class ProjectileType : ScriptableTypeObject
{
    [Header("Collision")]
    public float Radius = 0.05f;

    [Header("Health (how many hits the projectile itself survives)")]
    public int MaxHealth = 1;

    [Header("Damage dealt on an enemy hit")]
    public int Damage = 3;

    [Header("Kinematics")]
    public float Speed = 50f;
    public float Lifetime = 5f;

    [Header("Motion strategy (select a kind; it carries its own tuning)")]
    [SerializeReference, SubclassSelector]
    public IMotionStrategy Motion = new LinearMotion();

    [Header("Hit reactions (operations run per hit category)")]
    [Tooltip("Operations run when this projectile hits a surface (wall/stage).")]
    [SerializeReference, SubclassSelector]
    public List<IEffect<ProjectileSurfaceCollision>> OnSurface = new();

    [Tooltip("Operations run when this projectile hits a player.")]
    [SerializeReference, SubclassSelector]
    public List<IEffect<ProjectilePlayerCollision>> OnPlayer = new();

    [Tooltip("Operations run when this projectile hits another projectile.")]
    [SerializeReference, SubclassSelector]
    public List<IEffect<ProjectileProjectileCollision>> OnProjectile = new();

    /// <summary>The per-category operations this kind runs by default, as a bundle for ServerCollision.</summary>
    public ProjectileEffects DefaultEffects => new(OnSurface, OnPlayer, OnProjectile);

    /// <summary>
    /// Initialize a freshly-instantiated projectile's components from this type — the composition lives here,
    /// not in any component. Called by the instance's <see cref="TypeObjectRoot"/> at Awake on every peer:
    /// NetworkedMotion gets its motion strategy + speed (read locally from this SO, NOT off the wire);
    /// ServerCollision gets the collision radius + lifetime + this kind's authored operations (a harmless
    /// no-op on clients, which never collide); Health/Damage get their values. No component reaches into a
    /// sibling or back here.
    /// </summary>
    public override void Initialize(GameObject instance)
    {
        if (instance.TryGetComponent<NetworkedMotion>(out var motion))
            motion.Configure(Motion, Speed);
        if (instance.TryGetComponent<ServerCollision>(out var collision))
            collision.Configure(Radius, Lifetime, DefaultEffects);
        if (instance.TryGetComponent<Health>(out var health))
            health.ConfigureMax(MaxHealth);
        if (instance.TryGetComponent<Damage>(out var damage))
            damage.Amount = Damage;
    }
}
