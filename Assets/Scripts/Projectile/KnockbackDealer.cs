using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic knockback-DEALING component: how hard this entity shoves what it hits, AUTHORED ON THE PREFAB. The
/// sibling of <see cref="Damage"/> for the knockback exchange; carrying it IS what makes this entity's hits
/// shove — ServerCollision fires the exchange from component presence (ADR 0002) and hands
/// <see cref="EffectiveImpulse"/> to the struck <see cref="KnockbackReceiver"/>.
///
/// Knockback is server-authoritative and only read server-side, so it needs no networking of its own.
///
/// The authored <see cref="config"/> is a BASELINE and is never mutated. Runtime tuning contributes to
/// <see cref="Modifiers"/> instead, which <see cref="EffectiveImpulse"/> folds at read time — the pooling-safe,
/// stackable half of the "fold at apply time, never mutate authored values" rule.
/// </summary>
public sealed class KnockbackDealer : MonoBehaviour
{
    [SerializeField] private KnockbackConfig config = KnockbackConfig.Default;

    /// <summary>Outgoing modifiers folded into every impulse this entity deals. Empty list = the identity.</summary>
    public readonly List<KnockbackModifier> Modifiers = new();

    /// <summary>The impulse this entity deals to something struck along <paramref name="hitDirection"/>.</summary>
    public Vector3 EffectiveImpulse(Vector3 hitDirection)
        => KnockbackHelper.ResolveImpulse(hitDirection, in config, Modifiers);
}
