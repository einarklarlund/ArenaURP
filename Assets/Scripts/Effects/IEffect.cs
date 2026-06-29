/// <summary>
/// A single, reusable unit of "something that happens in the world" — deal damage, ricochet, spawn
/// projectiles, knock back, play a VFX. A plain C# command (NOT a ScriptableObject): authored in code and
/// composed by whoever runs it (a projectile type's default reactions, an ability's effects, a passive's
/// contributions). Per-variant config is constructor arguments (e.g. <c>new DealDamage(amount: 8)</c>),
/// which replaces the old "one tuned SO asset per variant".
///
/// <see cref="Apply"/> receives an <see cref="IEffectContext"/> and reads only the CAPABILITIES it needs
/// (<c>ctx is ISurfaceContext s</c>); it no-ops gracefully when the triggering action didn't provide them.
/// Effects are deliberately Unity-COUPLED — they GetComponent off the context's participants, Instantiate,
/// FishNet-spawn, call IDamageable — that engine reach is their whole point. Keep them stateless flyweights:
/// hold only immutable config from the constructor, never per-invocation state, so one instance is safe to
/// share across every caller. Non-trivial math belongs in a pure *.Core helper (RicochetMath, SpawnFanMath).
/// </summary>
public interface IEffect
{
    /// <summary>Perform the effect. Runs in a server-authoritative context; read the context, act, return.</summary>
    void Apply(IEffectContext ctx);
}
