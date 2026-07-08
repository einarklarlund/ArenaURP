using UnityEngine;

/// <summary>
/// On-spawn buff: multiply the spawned object's size — both its visual scale and, where present, its
/// server-side collision radius — by the same factor. Typed to <see cref="OnSpawnContext"/> so the
/// variance-aware picker offers it only on passives' OnSpawn list. Reaches each system through its dedicated
/// per-spawn multiplier sibling — <see cref="SpawnScale.SetScaleMultiplier"/> for visuals,
/// <see cref="ServerCollision.SetRadiusMultiplier"/> for the collision sweep — NEVER a raw
/// <c>transform.localScale</c> / <c>Radius</c> write, which has no ResetState owner and would leak the last
/// spawn's buff across pooled reuse. Both are idempotent multiplier layers over the authored baseline (pooling
/// two-layer rule) that reset to identity on pooled reuse. Neither system has a sync channel here — visual
/// scale is per-peer cosmetic and collision is server-only — so this is a per-peer spawn-instant snapshot by
/// design: a peer that hasn't replicated a just-granted passive renders that one spawn un-buffed (the defined
/// semantics, not a bug; the server always resolves the authoritative collision size). A spawnable carrying
/// only one of the two siblings is buffed on whichever it has. Stateless flyweight: only the serialized
/// multiplier.
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
        if (ctx.Owner.TryGetComponent<ServerCollision>(out var collision))
            collision.SetRadiusMultiplier(multiplier);
    }
}
