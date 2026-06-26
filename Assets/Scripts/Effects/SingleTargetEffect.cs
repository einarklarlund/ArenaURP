using UnityEngine;

/// <summary>
/// A <see cref="WorldEffect"/> that acts on ONE specific target GameObject (the thing that was hit / the
/// ability's single target). The base pulls the target out of the <see cref="EffectContext"/> and skips
/// the effect when there is none, so subclasses just implement <see cref="ApplyToTarget"/> against a
/// guaranteed-non-null target. Effects with no single target (spawn a fan, ricochet, play a world VFX)
/// derive from <see cref="WorldEffect"/> directly instead.
///
/// Still a flyweight SO command — no per-invocation state; the subclass decides whether it can actually
/// act on the target (e.g. only if it has an IDamageable).
/// </summary>
public abstract class SingleTargetEffect : WorldEffect
{
    public sealed override void Apply(in EffectContext ctx)
    {
        if (ctx.Target != null)
            ApplyToTarget(in ctx, ctx.Target);
    }

    /// <summary>Act on the (non-null) target. The rest of the context (point/dir/invoker) is still available.</summary>
    protected abstract void ApplyToTarget(in EffectContext ctx, GameObject target);
}
