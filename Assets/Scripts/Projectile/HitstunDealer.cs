using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic hitstun-DEALING component: how long this entity staggers what it hits, AUTHORED ON THE PREFAB. The
/// sibling of <see cref="Damage"/> for the hitstun exchange; <see cref="DealHitstun"/> reads it through the
/// context's hitstun-source capability and hands the duration to the struck <see cref="HitstunReceiver"/>.
///
/// Hitstun is dealt server-side (hit operations are server-only), so this component needs no networking; the
/// resulting stun WINDOW is what replicates, from the receiver.
///
/// The authored <see cref="config"/> is a BASELINE and is never mutated. Runtime tuning contributes to
/// <see cref="Modifiers"/> instead, which <see cref="EffectiveSeconds"/> folds at read time.
/// </summary>
public sealed class HitstunDealer : MonoBehaviour
{
    [SerializeField] private HitstunConfig config = HitstunConfig.Default;

    /// <summary>Outgoing modifiers folded into every stun this entity deals. Empty list = the identity.</summary>
    public readonly List<HitstunModifier> Modifiers = new();

    /// <summary>Seconds of stun this entity deals to something it strikes.</summary>
    public float EffectiveSeconds => HitstunProcessor.ResolveDuration(in config, Modifiers);
}
