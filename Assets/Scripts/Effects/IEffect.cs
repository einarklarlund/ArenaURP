/// <summary>
/// A command that performs ONE operation against a specific KIND of context. An operation is TYPED to the exact
/// context it runs in — <see cref="IEffect{TContext}"/> — so the caller is compile-time forced to supply the
/// right context and the operation reads its data directly, with no <c>is</c>-checks and no silent no-ops.
///
/// A concrete operation is a plain <c>[System.Serializable]</c> class, so it can be AUTHORED AS DATA in the
/// inspector via <c>[SerializeReference, EffectSelector]</c> (a variance-aware picker — an operation typed to an
/// ancestor context is offered on every list whose context descends from it) on a typed list, AND constructed in
/// code via <see cref="Commands"/>. Operations are STATELESS flyweights: they hold no runtime state, only
/// immutable serialized/ctor config.
///
/// Contexts form a small concrete CLASS TREE (see <see cref="EffectContext"/> in EffectContexts.cs) — the
/// shallow taxonomy of SITES an operation can run at. The site that builds the context resolves every system
/// handle ONCE and hands it over: an operation couples to the systems its context carries, NOT to "a projectile"
/// or "a pawn". Operations are the Unity-coupling layer — they still <c>Instantiate</c>, FishNet-spawn, and act
/// on resolved handles directly — but they never <c>GetComponent</c> a system off a raw archetype GameObject and
/// gamble it is there. (The hit EXCHANGES — damage/knockback/hitstun — are not operations at all: they fire from
/// component presence at the site; ADR 0002.) Non-trivial math belongs in a pure <c>*.Core</c> helper
/// (RicochetMath, SpawnFanMath, DeflectMath, DamageProcessor).
///
/// <typeparamref name="TContext"/> is contravariant (<c>in</c>): an operation typed to an ancestor context is
/// usable wherever a descendant context is supplied.
/// </summary>
public interface IEffect<in TContext> where TContext : EffectContext
{
    /// <summary>Perform the operation. Runs in a server-authoritative context; read the context, act, return.</summary>
    void Apply(TContext context);
}
