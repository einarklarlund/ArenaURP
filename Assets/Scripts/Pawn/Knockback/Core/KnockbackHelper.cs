using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure, stateless functional core of the knockback system: turn a hit direction plus authored config into an
/// impulse vector, and fold modifier lists into it. Zero Unity lifecycle dependencies.
///
/// A knockback exchange folds TWICE, once per end, and this core supplies both:
/// the dealer calls <see cref="ResolveImpulse"/> (config + its outgoing modifiers), the receiver calls
/// <see cref="ResolveIncoming"/> (that impulse + its own resistance modifiers).
/// </summary>
public static class KnockbackHelper
{
    /// <summary>
    /// Computes a knockback impulse vector from a hit direction and config.
    /// Enforces a minimum launch angle from horizontal.
    /// </summary>
    /// <param name="hitDirection">Direction the hit came from (will be normalized).</param>
    /// <param name="config">Knockback tuning parameters.</param>
    /// <returns>An impulse vector to add to the pawn's velocity.</returns>
    public static Vector3 ComputeImpulse(Vector3 hitDirection, in KnockbackConfig config)
    {
        Vector3 dir = hitDirection.normalized;

        float horizontalMag = Vector3.ProjectOnPlane(dir, Vector3.up).magnitude;
        float minHeight = horizontalMag * Mathf.Tan(config.MinimumAngleOnHit * Mathf.Deg2Rad);
        dir.y = Mathf.Max(dir.y, minHeight);
        dir = dir.normalized;

        return config.SpeedOnHit * dir;
    }

    /// <summary>
    /// Folds a modifier list into a single multiplier. An empty or null list yields the identity (1), so an
    /// absent contributor changes nothing.
    /// </summary>
    public static float FoldMultiplier(IReadOnlyList<KnockbackModifier> modifiers)
    {
        float multiplier = 1f;
        if (modifiers == null)
            return multiplier;

        for (int i = 0; i < modifiers.Count; i++)
            multiplier *= modifiers[i].ImpulseMultiplier;

        return multiplier;
    }

    /// <summary>
    /// The OUTGOING impulse a dealer emits: its authored config aimed along the hit direction, scaled by the
    /// dealer's own modifiers. Never mutates the authored config.
    /// </summary>
    public static Vector3 ResolveImpulse(Vector3 hitDirection, in KnockbackConfig config,
        IReadOnlyList<KnockbackModifier> modifiers)
        => ComputeImpulse(hitDirection, in config) * FoldMultiplier(modifiers);

    /// <summary>
    /// The INCOMING impulse a receiver accepts: the dealt impulse scaled by the receiver's own resistance
    /// modifiers. A multiplier of 0 absorbs the hit entirely.
    /// </summary>
    public static Vector3 ResolveIncoming(Vector3 impulse, IReadOnlyList<KnockbackModifier> modifiers)
        => impulse * FoldMultiplier(modifiers);
}
