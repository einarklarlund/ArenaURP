using UnityEngine;

/// <summary>
/// A command that performs ONE operation against a specific KIND of context. This REPLACES the old
/// context-agnostic <c>IEffect</c>: instead of a single <c>Apply(IEffectContext)</c> that queries
/// capabilities (<c>ctx is ISurfaceContext</c>) and silently no-ops when data is missing, an operation is
/// TYPED to the exact context it runs in — <see cref="IEffect{TContext}"/>. The caller is compile-time
/// forced to supply the right context, and the operation reads its data directly, with no <c>is</c>-checks
/// and no silent no-ops.
///
/// A concrete operation is a plain <c>[System.Serializable]</c> class, so it can be AUTHORED AS DATA in the
/// inspector via <c>[SerializeReference, EffectSelector]</c> (a variance-aware picker — an operation typed to a
/// capability interface is offered on every context that satisfies it) on a typed list, AND constructed in code
/// via <see cref="Commands"/>. Operations are stateless
/// flyweights — they hold only their immutable serialized/ctor config and read everything else from the
/// context. They are the Unity-coupling layer: they <c>GetComponent</c> off the context's participants,
/// <c>Instantiate</c>, FishNet-spawn, and call <c>IDamageable</c> directly; non-trivial math belongs in a
/// pure <c>*.Core</c> helper (RicochetMath, SpawnFanMath, DeflectMath).
///
/// <typeparamref name="TContext"/> is contravariant (<c>in</c>): an operation typed to a leaner context (the
/// base <see cref="IEffectContext"/>, or a single capability like <see cref="IAimContext"/>) is usable
/// wherever a richer context is expected.
/// </summary>
public interface IEffect<in TContext> where TContext : IEffectContext
{
    /// <summary>Perform the operation. Runs in a server-authoritative context; read the context, act, return.</summary>
    void Apply(TContext context);
}

/// <summary>
/// The data EVERY operation context carries: who owns the operation and which player they belong to. This is
/// the root of a family of small, composable capability interfaces — a concrete context (a projectile-surface
/// hit, a projectile-player hit, a projectile-projectile hit, an ability's aim) declares WHAT INFORMATION it
/// carries by implementing the subset of capability interfaces that fit it (<see cref="IAimContext"/>,
/// <see cref="ISurfaceContext"/>, <see cref="ITargetContext"/>), instead of every context re-declaring the same
/// fields. An operation then names the exact context type (or the single capability) it needs, which is what
/// enforces the contract. Built fresh per invocation by the imperative shell that runs the operations
/// (ServerCollision for a hit, PawnAbilities for an ability use).
/// </summary>
public interface IEffectContext
{
    /// <summary>What owns the operation: the projectile on a hit, or the caster's pawn on an ability use.</summary>
    GameObject Owner { get; }

    /// <summary>
    /// The owning NetworkPlayer — the firer / caster (for damage attribution and owner-id lookups), or null.
    /// Read its <c>ObjectId</c> when an operation needs the owning player's id.
    /// </summary>
    NetworkPlayer OwnerPlayer { get; }
}

/// <summary>
/// The context has a world ORIGIN and a DIRECTION — the impact point + travel direction of a hit, or the
/// origin + aim of an ability. Consumed by operations that spawn or project from a point along a heading
/// (<see cref="SpawnProjectiles"/>, <see cref="DealDamage"/>).
/// </summary>
public interface IAimContext : IEffectContext
{
    /// <summary>The world position the operation acts from (impact point, or the caster's origin).</summary>
    Vector3 Point { get; }

    /// <summary>The heading the operation acts along (the projectile's travel direction, or the aim).</summary>
    Vector3 Direction { get; }
}

/// <summary>
/// The context has a SURFACE NORMAL — the operation acts against something with an orientable face, so it can
/// reflect off it or orient VFX to it (<see cref="Ricochet"/>, <see cref="SpawnVfx"/>).
/// </summary>
public interface ISurfaceContext : IEffectContext
{
    /// <summary>The surface normal at the hit point.</summary>
    Vector3 Normal { get; }
}

/// <summary>
/// The context has a struck TARGET GameObject — a player or another projectile — so target-consuming
/// operations (<see cref="DealDamage"/>, <see cref="ApplyDebuff"/>, <see cref="DeflectProjectile"/>) can read
/// it directly with no null-check for "did I have a target".
/// </summary>
public interface ITargetContext : IEffectContext
{
    /// <summary>The GameObject that was struck (the hit player, or the other projectile).</summary>
    GameObject Target { get; }
}

// ---- composite capabilities: named combinations, for operations that need more than one at once ----
// C# can't express "IAimContext AND ISurfaceContext" as a single generic argument, so an operation that
// reads two capabilities names the composite instead. Every context that carries both capabilities
// implements the composite, so the variance-aware picker offers the operation on exactly those sites.

/// <summary>
/// A hit that has BOTH an origin/direction and a surface normal (<see cref="IAimContext"/> +
/// <see cref="ISurfaceContext"/>) — any projectile collision. Operations needing point + normal type to this
/// (<see cref="Ricochet"/>, <see cref="SpawnVfx"/>) and become authorable on every collision site.
/// </summary>
public interface ISurfaceHitContext : IAimContext, ISurfaceContext { }

/// <summary>
/// A hit that has BOTH an origin/direction and a struck target (<see cref="IAimContext"/> +
/// <see cref="ITargetContext"/>) — a player or projectile collision. Operations needing point + target type to
/// this (<see cref="DealDamage"/>) and become authorable on any target-bearing site.
/// </summary>
public interface ITargetHitContext : IAimContext, ITargetContext { }
