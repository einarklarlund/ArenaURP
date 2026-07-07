using UnityEngine;

/// <summary>
/// On-spawn buff: multiply the spawned object's visual scale. Typed to <see cref="OnSpawnContext"/> so the
/// variance-aware picker offers it only on passives' OnSpawn list. Reaches scale through the dedicated
/// <see cref="SpawnScale"/> sibling — NEVER raw <c>transform.localScale</c>, which has no ResetState owner and
/// would leak the last spawn's scale across pooled reuse. Scale has no sync channel, so this is a per-peer
/// spawn-instant snapshot by design: a peer that hasn't replicated a just-granted passive renders that one
/// spawn un-buffed (the defined semantics, not a bug). Stateless flyweight: only the serialized multiplier.
/// </summary>
[System.Serializable]
public sealed class BuffScale : IEffect<OnSpawnContext>
{
    [SerializeField] float multiplier = 1.5f;

    public BuffScale() { }
    public BuffScale(float multiplier) { this.multiplier = multiplier; }

    public void Apply(OnSpawnContext ctx)
    {
        if (ctx.Owner.TryGetComponent<SpawnScale>(out var scale))
            scale.SetScaleMultiplier(multiplier);
    }
}
