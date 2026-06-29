using Arena.NetworkedMotion;
using UnityEngine;

/// <summary>
/// Shared, immutable-at-runtime per-kind projectile data (Type Object + Flyweight): one asset per
/// projectile kind (bullet, grenade, laser, arc, homing). A projectile is a COMPOSITION of generic systems
/// (NetworkedMotion / ServerCollision / Health / Damage); this type configures each of them and supplies the
/// hit reactions.
///
/// Hit reactions are defined IN CODE, not authored as asset references: <see cref="DefaultReactions"/>
/// composes the <see cref="IEffect"/>s for each category from this asset's serialized config + feature flags
/// (e.g. <see cref="RicochetsByDefault"/>). New kinds = new assets tuning the config; new default behaviours
/// = a flag + a line here. Per-run powerups add MORE effects at runtime via the owner-contribution path
/// (see PawnAbilities / OwnerReactions) — they are not flags on the type.
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

    [Header("Default reactions (feature flags)")]
    [Tooltip("Health the projectile loses on a surface hit (1 = a normal bullet dies on contact).")]
    public int SurfaceHealthLoss = 1;
    [Tooltip("If set, the projectile reflects off surfaces by default (a bouncy bullet kind).")]
    public bool RicochetsByDefault = false;

    // Built once and cached — defaults are immutable flyweights; owner contributions are merged separately
    // at hit time by ServerCollision, so this cache is never mutated per projectile.
    ProjectileReactions _defaultReactions;

    /// <summary>The plain kinematics struct the pure <see cref="NetworkedMotionProcessor"/> consumes.</summary>
    public MotionConfig ToMotionConfig() => new(Movement, Acceleration, Gravity, Lifetime, MaxRange);

    /// <summary>The per-category default reactions for this kind, composed in code (cached flyweight).</summary>
    public ProjectileReactions DefaultReactions
    {
        get
        {
            if (_defaultReactions == null)
            {
                _defaultReactions = new ProjectileReactions();
                BuildDefaultReactions(_defaultReactions);
            }
            return _defaultReactions;
        }
    }

    /// <summary>
    /// Compose this kind's default effects per category from the serialized config + flags. Behaviour
    /// variation comes from the flags, keeping kinds as ASSETS rather than subclasses.
    /// </summary>
    void BuildDefaultReactions(ProjectileReactions r)
    {
        if (SurfaceHealthLoss > 0) r.OnSurface.Add(new LoseHealth(SurfaceHealthLoss));
        if (RicochetsByDefault) r.OnSurface.Add(new Ricochet());

        // Enemy hit: deal the projectile's own Damage (read from the Damage component) and despawn on contact.
        r.OnEnemy.Add(new DealDamage(useInvokerDamage: true, despawnAfter: true));
    }

    /// <summary>
    /// Initialize a freshly-instantiated projectile's components from this type — the composition lives here,
    /// not in any component. ServerCollision gets the collision radius + this kind's default reactions; the
    /// rest get their values. No component reaches into a sibling or back into this type.
    /// </summary>
    protected override void Initialize(GameObject instance)
    {
        if (instance.TryGetComponent<NetworkedMotion>(out var motion))
            motion.Configure(ToMotionConfig(), Speed);
        if (instance.TryGetComponent<ServerCollision>(out var collision))
            collision.Configure(Radius, DefaultReactions);
        if (instance.TryGetComponent<Health>(out var health))
            health.ConfigureMax(MaxHealth);
        if (instance.TryGetComponent<Damage>(out var damage))
            damage.Amount = Damage;
    }
}
