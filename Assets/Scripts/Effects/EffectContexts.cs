using Arena.NetworkedMotion;
using UnityEngine;

/// <summary>
/// The root of the context CLASS TREE — the shallow taxonomy of SITES an operation can run at. A context
/// describes "what happened + what you're allowed to reach"; each level adds the handles its site can honestly
/// provide, resolved ONCE by the imperative shell that runs the operations (ServerCollision for a hit,
/// PawnActiveAbilities for an ability use, SpawnBuffReceiver for a spawn). An operation types itself to the
/// SHALLOWEST level that serves it and the variance-aware picker offers it on every deeper list.
///
/// This is deliberately a tree of concrete classes, NOT capability interfaces (ADR 0002): the shipped sites form
/// a strict tree, and class inheritance expresses that with a fraction of the types. If a genuinely non-tree
/// site ever arrives (a capability two unrelated branches must share), extract an interface for it at that
/// second concretion — rule of two — not before.
/// </summary>
public abstract class EffectContext
{
    /// <summary>
    /// The GameObject that triggered the operation — the projectile on a hit, the caster's pawn on an ability.
    /// This is the ESCAPE HATCH for operations that resolve a plain data-holder off it (a <c>SpawnableObjects</c>
    /// prefab list); operations that touch a first-class system (motion / health) take that system's resolved
    /// handle from the context instead of reaching through here.
    /// </summary>
    public GameObject Source { get; }

    /// <summary>
    /// The player to attribute the operation to — the firer / caster (for damage attribution and owner-id
    /// lookups), or null. Read its <c>ObjectId</c> when an operation needs the instigating player's id.
    /// </summary>
    public NetworkPlayer Instigator { get; }

    protected EffectContext(GameObject source, NetworkPlayer instigator)
    {
        Source = source;
        Instigator = instigator;
    }
}

/// <summary>
/// A networked object was just SPAWNED and the owner's passive abilities get one chance to transform it — on
/// EVERY peer, at the spawn instant (see <c>SpawnBuffReceiver</c>). Deliberately a LEAF off the root — no aim, no
/// surface, no motion — so on-spawn buffs reach their target system by <c>GetComponent</c> on the spawned object
/// (a generic sibling like <c>NetworkedMotion</c> or <c>SpawnScale</c>), and the variance-aware picker keeps them
/// out of the collision lists (and vice-versa).
/// </summary>
public sealed class OnSpawnContext : EffectContext
{
    /// <summary>The just-spawned object being buffed (same value as <see cref="EffectContext.Source"/>, named for the buff site).</summary>
    public GameObject Owner => Source;

    /// <summary>The firer whose passives are transforming the spawn (same value as <see cref="EffectContext.Instigator"/>).</summary>
    public NetworkPlayer OwnerPlayer => Instigator;

    public OnSpawnContext(GameObject owner, NetworkPlayer ownerPlayer)
        : base(owner, ownerPlayer) { }
}

/// <summary>
/// The context has a world ORIGIN and a DIRECTION — the origin + aim of an ability use, or (deeper in the tree)
/// the impact point + travel direction of a hit. The context an active ability's <c>OnUse</c> list is typed to:
/// an operation typed here (<see cref="SpawnNetworkObjects"/>) is authorable on abilities AND on every collision
/// list below.
/// </summary>
public class AimContext : EffectContext
{
    /// <summary>The world position the operation acts from (the caster's origin, or the impact point).</summary>
    public Vector3 Point { get; }

    /// <summary>The heading the operation acts along (the aim, or the projectile's travel direction).</summary>
    public Vector3 Direction { get; }

    public AimContext(GameObject source, NetworkPlayer instigator, Vector3 point, Vector3 direction)
        : base(source, instigator)
    {
        Point = point;
        Direction = direction;
    }
}

/// <summary>
/// A projectile HIT something: a surface (wall/stage) or a player — the two lists share this one context, since
/// presence-dispatch (ADR 0002) moved the exchanges out of the reaction layer and no remaining reaction
/// distinguishes them. Adds the struck face's <see cref="Normal"/>, the source's MOTION system (for a
/// <see cref="Ricochet"/> redirect) and its own HEALTH (<see cref="LoseHealth"/> durability spend).
///
/// The hit EXCHANGES (damage/knockback/hitstun) are NOT reachable here: they fire from COMPONENT PRESENCE
/// (dealer on the projectile ∧ receiver on the victim), dispatched by ServerCollision in the same atomic hit —
/// independent (shove without damaging, stun without shoving) purely by which dealer components the prefab
/// carries.
/// </summary>
public class ProjectileImpact : AimContext
{
    /// <summary>The surface normal at the hit point.</summary>
    public Vector3 Normal { get; }

    /// <summary>
    /// Where the source mover's CENTRE sits at this contact — the point a redirect (ricochet/deflect) resumes the
    /// trajectory from. For a world hit this stands off the impact <see cref="AimContext.Point"/> (a point on the
    /// struck collider's SURFACE, reported by the sweep) by the mover's collision radius along the
    /// <see cref="Normal"/>, so the resumed segment starts the mover TANGENT to the surface rather than embedded
    /// a radius deep in it (which would immediately re-collide — the sticking/jitter a naive resume at the
    /// surface point causes). Resolved by the context builder, which owns the radius; the impact
    /// <see cref="AimContext.Point"/> stays the surface point for VFX. In the projectile-vs-projectile channel
    /// the impact point is already this mover's centre, so the two coincide.
    /// </summary>
    public Vector3 SourceCenterPoint { get; }

    /// <summary>The source's motion system (its live velocity / segment anchor), for a redirect.</summary>
    public NetworkedMotion Motion { get; }

    /// <summary>The source's OWN health system — a projectile spends durability per hit (a normal bullet dies on
    /// contact; a ricochet bullet with MaxHealth &gt; 1 survives that many bounces).</summary>
    public Health SelfHealth { get; }

    public ProjectileImpact(GameObject source, NetworkPlayer instigator,
        Vector3 point, Vector3 normal, Vector3 direction, Vector3 sourceCenterPoint,
        NetworkedMotion motion, Health selfHealth)
        : base(source, instigator, point, direction)
    {
        Normal = normal;
        SourceCenterPoint = sourceCenterPoint;
        Motion = motion;
        SelfHealth = selfHealth;
    }
}

/// <summary>
/// A projectile hit ANOTHER projectile. Adds the OTHER projectile's motion system via <see cref="OtherMotion"/>
/// — read by <see cref="DeflectProjectile"/>, which types itself here precisely because it depends on the target
/// being a projectile.
///
/// The hit exchanges deliberately do NOT fire in the projectile channel today: no content deals
/// projectile-vs-projectile damage, and running them here on component presence would let every damage-dealing
/// bullet destroy other bullets. If that interaction is ever wanted, it needs its own decision (a dealer-side
/// victim mask) — see ADR 0002.
/// </summary>
public sealed class ProjectileProjectileCollision : ProjectileImpact
{
    /// <summary>The struck projectile's motion system (its heading/velocity), for a deflect.</summary>
    public NetworkedMotion OtherMotion { get; }

    public ProjectileProjectileCollision(GameObject source, NetworkPlayer instigator,
        Vector3 point, Vector3 normal, Vector3 direction, Vector3 sourceCenterPoint,
        NetworkedMotion motion, Health selfHealth, NetworkedMotion otherMotion)
        : base(source, instigator, point, normal, direction, sourceCenterPoint, motion, selfHealth)
    {
        OtherMotion = otherMotion;
    }
}
