/// <summary>
/// A swappable hit reaction as a COMPONENT, discovered by presence at the hit site (component-handler shape,
/// DESIGN.md migration thread): authored on the projectile prefab, or on a passive-ability instance prefab for
/// the owner-contribution path — never serialized into a list, never AddComponent-ed onto pooled instances.
/// <see cref="HitResolver"/> harvests handlers per hit (the projectile's own first, then the owner's) and runs
/// each iff <see cref="SupportedChannels"/> ∩ <see cref="EnabledChannels"/> ∩ the current channel is non-empty.
/// Dispatch is server-only by construction: every caller is server-side hit resolution.
/// </summary>
public interface IHitHandler
{
    /// <summary>Which channels this BEHAVIOUR can serve — declared in code, a fact about the implementation
    /// (e.g. a steal needs a struck pawn, so it supports only Player).</summary>
    HitChannels SupportedChannels { get; }

    /// <summary>Which channels this KIND reacts on — a serialized field on the component, authored per prefab
    /// copy (the LayerMask idiom). The dispatch site intersects it with <see cref="SupportedChannels"/>.</summary>
    HitChannels EnabledChannels { get; }

    void OnHit(in HitContext context);
}
