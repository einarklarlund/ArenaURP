using System.Collections.Generic;
using Arena.NetworkedMotion;
using UnityEngine;

/// <summary>
/// Shared, immutable-at-runtime per-kind projectile data (Type Object + Flyweight): one asset per
/// projectile kind (bullet, grenade, laser, arc, homing). A projectile is a COMPOSITION of generic
/// systems (NetworkedMotion / ServerCollision / Health / Damage); this type configures each of them and
/// supplies the hit reactions.
///
/// Lives in the shell (not a pure-core assembly) because it references the Unity-coupled
/// <see cref="HitReaction"/> assets. The pure motion core never sees this object — it is fed the plain
/// <see cref="MotionConfig"/> via <see cref="ToMotionConfig"/> (the functional-core rule).
///
/// Hit reactions are authored as typed ScriptableObject assets per category, so a surface reaction can't
/// be dropped into the enemy slot. Each reaction owns a reusable list of <see cref="WorldEffect"/>s.
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
    public float Acceleration = 0f;
    public float Lifetime = 5f;

    [Header("Motion strategy")]
    public MotionKind Movement = MotionKind.Linear;

    [Tooltip("Vertical gravity applied per second by the Arc strategy (negative = down).")]
    public float Gravity = -9.81f;

    [Tooltip("Maximum beam length probed by the Laser strategy each step.")]
    public float MaxRange = 100f;

    [Header("Hit reactions (typed per category — can't be mis-slotted)")]
    public List<SurfaceHitReaction> OnSurface = new();
    public List<EnemyHitReaction> OnEnemy = new();
    public List<ProjectileHitReaction> OnProjectile = new();

    /// <summary>The plain kinematics struct the pure <see cref="NetworkedMotionProcessor"/> consumes.</summary>
    public MotionConfig ToMotionConfig() => new(Movement, Acceleration, Gravity, Lifetime, MaxRange);

    /// <summary>
    /// Initialize a freshly-instantiated projectile's components from this type — the composition lives
    /// here, not in any component. NetworkedMotion gets its motion config + speed (which then ride the
    /// payload to every peer); ServerCollision gets the collision radius + reaction lists; Health/Damage
    /// get their values. No component reaches into a sibling or back into this type.
    /// </summary>
    protected override void Initialize(GameObject instance)
    {
        if (instance.TryGetComponent<NetworkedMotion>(out var motion))
            motion.Configure(ToMotionConfig(), Speed);
        if (instance.TryGetComponent<ServerCollision>(out var collision))
            collision.Configure(Radius, OnSurface, OnEnemy, OnProjectile);
        if (instance.TryGetComponent<Health>(out var health))
            health.ConfigureMax(MaxHealth);
        if (instance.TryGetComponent<Damage>(out var damage))
            damage.Amount = Damage;
    }
}
