using Arena.NetworkedMotion;
using UnityEngine;

/// <summary>
/// A command that performs ONE operation against a specific KIND of context. An operation is TYPED to the exact
/// context it runs in — <see cref="IEffect{TContext}"/> — so the caller is compile-time forced to supply the
/// right context and the operation reads its data directly, with no <c>is</c>-checks and no silent no-ops.
///
/// A concrete operation is a plain <c>[System.Serializable]</c> class, so it can be AUTHORED AS DATA in the
/// inspector via <c>[SerializeReference, EffectSelector]</c> (a variance-aware picker — an operation typed to a
/// capability interface is offered on every context that satisfies it) on a typed list, AND constructed in code
/// via <see cref="Commands"/>. Operations are STATELESS flyweights: they hold no runtime state, only immutable
/// serialized/ctor config.
///
/// SYSTEM BOUNDARIES, NOT OBJECT TYPES. A context describes "what happened + what you're allowed to reach", and
/// the REACH is expressed as the SYSTEM COMPONENTS the operation participates in — the damage system
/// (<see cref="IDamageSourceContext"/> / <see cref="IDamageTargetContext"/>), the motion system
/// (<see cref="IMotionContext"/>), the health system (<see cref="IHealthContext"/>) — resolved ONCE by the site
/// that built the context (which already knows the participants). So an operation couples to
/// "something that deals damage" and "something that can take damage", NOT to "a projectile" or "a pawn": the
/// same <see cref="DealDamage"/> lands on a pawn, a destructible prop, or a damageable projectile. Operations
/// are the Unity-coupling layer — they still <c>Instantiate</c>, FishNet-spawn, and call <c>IDamageable</c>
/// directly — but they no longer <c>GetComponent</c> a system off a raw archetype GameObject and gamble it is
/// there; the context hands them the resolved system handle. Non-trivial math belongs in a pure <c>*.Core</c>
/// helper (RicochetMath, SpawnFanMath, DeflectMath, DamageProcessor).
///
/// <typeparamref name="TContext"/> is contravariant (<c>in</c>): an operation typed to a leaner context (a
/// single system capability) is usable wherever a richer context is expected.
/// </summary>
public interface IEffect<in TContext> where TContext : IEffectContext
{
    /// <summary>Perform the operation. Runs in a server-authoritative context; read the context, act, return.</summary>
    void Apply(TContext context);
}

/// <summary>
/// The data EVERY operation context carries: the SOURCE that triggered the operation and the player to attribute
/// it to. This is the root of a family of small, composable capability interfaces — a concrete context declares
/// WHAT SYSTEMS it exposes by implementing the subset of capability interfaces that fit it. An operation names
/// the exact context type (or the single system capability) it needs, which is what enforces the contract. Built
/// fresh per invocation by the imperative shell that runs the operations (ServerCollision for a hit,
/// PawnActiveAbilities for an ability use).
/// </summary>
public interface IEffectContext
{
    /// <summary>
    /// The GameObject that triggered the operation — the projectile on a hit, the caster's pawn on an ability.
    /// This is the ESCAPE HATCH for operations that resolve a plain data-holder off it (a <c>SpawnableObjects</c>
    /// prefab list); operations that touch a first-class system (damage / motion / health) take that system's
    /// resolved handle from the matching capability instead of reaching through here.
    /// </summary>
    GameObject Source { get; }

    /// <summary>
    /// The player to attribute the operation to — the firer / caster (for damage attribution and owner-id
    /// lookups), or null. Read its <c>ObjectId</c> when an operation needs the instigating player's id.
    /// </summary>
    NetworkPlayer Instigator { get; }
}

/// <summary>
/// The context has a world ORIGIN and a DIRECTION — the impact point + travel direction of a hit, or the origin
/// + aim of an ability. Consumed by operations that spawn or project from a point along a heading
/// (<see cref="SpawnNetworkObjects"/>) and by damage (for the hit point / direction stamped on the DamageInfo).
/// </summary>
public interface IAimContext : IEffectContext
{
    /// <summary>The world position the operation acts from (impact point, or the caster's origin).</summary>
    Vector3 Point { get; }

    /// <summary>The heading the operation acts along (the projectile's travel direction, or the aim).</summary>
    Vector3 Direction { get; }
}

/// <summary>
/// The context acts against something with an orientable FACE — a surface normal is available, so an operation
/// can reflect off it or orient VFX to it (<see cref="Ricochet"/>, <see cref="InstantiateGameObjects"/>). Extends
/// <see cref="IAimContext"/> because every surface hit also has a point + direction.
/// </summary>
public interface ISurfaceContext : IAimContext
{
    /// <summary>The surface normal at the hit point.</summary>
    Vector3 Normal { get; }
}

/// <summary>
/// The DAMAGE-DEALING system is reachable: the operation can read how much damage the source deals via its
/// <see cref="Damage"/> component (its authored base amount, resolved through
/// <see cref="Arena.Damage.DamageProcessor"/>). Consumed by <see cref="DealDamage"/>. (How buffs/modifiers reach
/// a source's damage is a separate, deliberately deferred design — this capability exposes only the source's own
/// <see cref="Damage"/> component.)
/// </summary>
public interface IDamageSourceContext : IEffectContext
{
    /// <summary>The component that authors this source's base outgoing damage.</summary>
    Damage DamageSource { get; }
}

/// <summary>
/// The DAMAGE-RECEIVING system is reachable: something that can take damage was hit. Exposes the struck
/// <see cref="IDamageable"/> directly (the pawn's, prop's, or projectile's health system) — guaranteed present,
/// so damage operations need no "did I hit something damageable" null-check. Consumed by <see cref="DealDamage"/>.
/// </summary>
public interface IDamageTargetContext : IEffectContext
{
    /// <summary>The damage-receiving system that was struck.</summary>
    IDamageable DamageTarget { get; }
}

/// <summary>
/// The MOTION system of the source is reachable: the operation can read and steer the source's live
/// <see cref="NetworkedMotion"/> (reflect, redirect). Consumed by <see cref="Ricochet"/> and
/// <see cref="DeflectProjectile"/>.
/// </summary>
public interface IMotionContext : IEffectContext
{
    /// <summary>The source's motion system (its live velocity / segment anchor).</summary>
    NetworkedMotion Motion { get; }
}

/// <summary>
/// The source's own HEALTH system is reachable: the operation can spend the source's durability. Exposes the
/// source's <see cref="Health"/> component so a projectile can consume a point of health per hit (a normal
/// bullet dies on contact; a ricochet bullet with MaxHealth &gt; 1 survives that many bounces). Consumed by
/// <see cref="LoseHealth"/>.
/// </summary>
public interface IHealthContext : IEffectContext
{
    /// <summary>The source's own health system.</summary>
    Health SelfHealth { get; }
}

// ---- composite capabilities: named combinations, for operations that touch more than one system at once ----
// C# can't express "IDamageSourceContext AND IDamageTargetContext" as a single generic argument, so an operation
// that reaches two systems names the composite. Every context that exposes both systems implements the composite,
// so the variance-aware picker offers the operation on exactly those sites.

/// <summary>
/// A damage EXCHANGE: a source that deals damage struck something that can take it, at a known point/direction
/// (<see cref="IDamageSourceContext"/> + <see cref="IDamageTargetContext"/> + <see cref="IAimContext"/>). This is
/// the one context <see cref="DealDamage"/> needs — both systems guaranteed, no archetype knowledge.
/// </summary>
public interface IDamageExchangeContext : IDamageSourceContext, IDamageTargetContext, IAimContext { }

/// <summary>
/// A motion redirect off a face: the source's motion system plus a surface normal (<see cref="IMotionContext"/> +
/// <see cref="ISurfaceContext"/>). What <see cref="Ricochet"/> needs — reflect the live velocity across the normal.
/// </summary>
public interface IMotionSurfaceContext : IMotionContext, ISurfaceContext { }

// ---- standing-contribution capabilities: for buffs that CONTRIBUTE a modifier rather than mutate a system ----
// Unlike the collision/spawn capabilities above (which hand the operation a resolved system HANDLE to act on
// once), a stat buff is a STANDING contribution folded every tick by the pure MovementProcessor/WeaponProcessor.
// So its reach is a SINK, not a handle: the operation adds one modifier and the site that built the context
// (PawnPassiveAbilities) owns applying it to the pawn's live modifier list and removing it when the passive is
// dropped (the Remove-old/Add-new discipline). This is the "contribute a modifier folded at apply time, never
// mutate an authored value" rule expressed as a context.

/// <summary>
/// The pawn's MOVEMENT-modifier system is reachable as a contribution sink: a buff calls
/// <see cref="AddMovementModifier"/> to fold one multiplicative <see cref="MovementModifier"/> into the pawn while
/// the passive is equipped. Consumed by the movement buffs (<see cref="BuffWalkSpeed"/>,
/// <see cref="BuffWalkAcceleration"/>, <see cref="BuffJumpForce"/>, <see cref="BuffGravity"/>).
/// </summary>
public interface IMovementModifierContext : IEffectContext
{
    /// <summary>Contribute one movement modifier for as long as the source passive is equipped.</summary>
    void AddMovementModifier(MovementModifier modifier);
}

/// <summary>
/// The pawn's WEAPON-modifier system is reachable as a contribution sink: a buff calls
/// <see cref="AddWeaponModifier"/> to fold one multiplicative <see cref="WeaponModifier"/> into the pawn while the
/// passive is equipped. Consumed by the weapon buffs (<see cref="BuffFireRate"/>, <see cref="BuffSpread"/>).
/// </summary>
public interface IWeaponModifierContext : IEffectContext
{
    /// <summary>Contribute one weapon modifier for as long as the source passive is equipped.</summary>
    void AddWeaponModifier(WeaponModifier modifier);
}
