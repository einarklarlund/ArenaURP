using System.Collections.Generic;
using Arena.Damage;
using UnityEngine;

/// <summary>
/// The reusable hit-resolution site (ADR 0002): given the DEALING side's components, a struck object, and the
/// hit geometry, fire the hit EXCHANGES from component presence and run authored reaction lists. Extracted from
/// ServerCollision so that apply-time target discovery — an explosion overlapping N victims, a cast hitting a
/// pawn — resolves through the SAME pipeline instead of hand-rolling a private copy (both review spikes had to;
/// see the ADR's evidence). Server-only by construction: every caller is server-side hit resolution.
/// </summary>
public static class HitResolver
{
    /// <summary>
    /// Fire the damage / knockback / hitstun exchanges against <paramref name="victim"/>: each fires iff its
    /// dealer is passed (non-null) AND the victim carries the matching receiver, each end folding only its own
    /// modifiers. <paramref name="damageMultiplier"/> is the per-TARGET fold slot (e.g. blast falloff) — it
    /// scales only damage and is 1 for a plain hit. Receivers are resolved here with one GetComponentInParent
    /// walk up from the struck collider: receivers live on the entity ROOT.
    /// </summary>
    public static void ResolveExchanges(
        Damage damage, KnockbackDealer knockback, HitstunDealer hitstun,
        GameObject victim, Vector3 point, Vector3 direction, NetworkPlayer instigator,
        float damageMultiplier = 1f)
    {
        if (damage != null)
        {
            var damageable = ResolveDamageable(victim);
            if (damageable != null)
                damageable.TakeDamage(new DamageInfo
                {
                    Amount = DamageProcessor.Resolve(damage.Amount, damageMultiplier),
                    Attacker = instigator,
                    HitPoint = point,
                    Direction = direction,
                    Type = DamageType.Bullet,
                });
        }

        if (knockback != null)
        {
            var receiver = ResolveKnockbackReceiver(victim);
            if (receiver != null)
                receiver.ApplyImpulse(knockback.EffectiveImpulse(direction));
        }

        if (hitstun != null)
        {
            var receiver = ResolveHitstunReceiver(victim);
            if (receiver != null)
                receiver.ApplyHitstun(hitstun.EffectiveSeconds);
        }
    }

    /// <summary>Run every operation in <paramref name="ops"/> against <paramref name="ctx"/>. Null list and null
    /// entries are skipped — an unauthored list is a valid empty authoring, and a nulled managed reference must
    /// not abort its siblings (atomic-hit rule).</summary>
    public static void RunEffects<T>(IReadOnlyList<IEffect<T>> ops, T ctx) where T : EffectContext
    {
        if (ops == null)
            return;
        for (int i = 0; i < ops.Count; i++)
            ops[i]?.Apply(ctx);
    }

    // Receiver resolution: one walk UP from the struck collider to the entity root.

    public static IDamageable ResolveDamageable(GameObject hit)
        => hit != null ? hit.GetComponentInParent<IDamageable>() : null;

    public static KnockbackReceiver ResolveKnockbackReceiver(GameObject hit)
        => hit != null ? hit.GetComponentInParent<KnockbackReceiver>() : null;

    public static HitstunReceiver ResolveHitstunReceiver(GameObject hit)
        => hit != null ? hit.GetComponentInParent<HitstunReceiver>() : null;
}
