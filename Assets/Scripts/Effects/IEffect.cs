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
/// inspector via <c>[SerializeReference, SubclassSelector]</c> (the MackySoft SerializeReferenceExtensions
/// library) on a typed list, AND constructed in code via <see cref="Commands"/>. Operations are stateless
/// flyweights — they hold only their immutable serialized/ctor config and read everything else from the
/// context. They are the Unity-coupling layer: they <c>GetComponent</c> off the context's participants,
/// <c>Instantiate</c>, FishNet-spawn, and call <c>IDamageable</c> directly; non-trivial math belongs in a
/// pure <c>*.Core</c> helper (RicochetMath, SpawnFanMath, DeflectMath).
///
/// <typeparamref name="TContext"/> is contravariant (<c>in</c>): an operation that only needs the base
/// <see cref="IEffectContext"/> is usable wherever a richer context is expected.
/// </summary>
public interface IEffect<in TContext> where TContext : IEffectContext
{
    /// <summary>Perform the operation. Runs in a server-authoritative context; read the context, act, return.</summary>
    void Apply(TContext context);
}

/// <summary>
/// The data EVERY operation context carries: who caused it and which player owns them. Concrete context types
/// (a projectile-surface hit, a projectile-player hit, a projectile-projectile hit, an ability's aim) add the
/// extra data specific to that kind — and an operation names the exact context type it needs, which is what
/// enforces the contract. Built fresh per invocation by the imperative shell that runs the operations
/// (ServerCollision for a hit, PawnAbilities for an ability use).
/// </summary>
public interface IEffectContext
{
    /// <summary>What is causing the operation: the projectile on a hit, or the caster's pawn on an ability use.</summary>
    GameObject Invoker { get; }

    /// <summary>NetworkPlayer.ObjectId of the invoker's owner (the firer / caster), or -1.</summary>
    int InvokerPlayerId { get; }

    /// <summary>The resolved owner NetworkPlayer (for damage attribution), or null.</summary>
    NetworkPlayer InvokerPlayer { get; }
}
