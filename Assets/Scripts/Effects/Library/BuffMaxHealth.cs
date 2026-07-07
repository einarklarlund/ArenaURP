using UnityEngine;

/// <summary>
/// On-spawn buff: multiply the spawned object's max health (its durability) over the prefab-authored baseline —
/// e.g. a bullet that survives twice as many ricochets/bounces. Typed to <see cref="OnSpawnContext"/> so the
/// variance-aware picker offers it only on passives' OnSpawn list (never in a collision list). Reaches the HEALTH
/// system by <c>GetComponent</c> off the spawned object and folds through the idempotent
/// <see cref="Health.SetMaxHealthMultiplier"/> — order-safe whether it runs before or after
/// <see cref="Health.OnStartServer"/>, and pooling-safe (the multiplier is a per-spawn layer reset in ResetState).
/// Health is SERVER-AUTHORITATIVE (its Current/Max are ServerOnly SyncVars), so the write lands on the server and a
/// client that missed the passive self-heals from the seeded values (see Health / OnSpawnContext), unlike the
/// per-peer cosmetic <see cref="BuffScale"/>. Stateless flyweight: only the immutable serialized multiplier.
/// </summary>
[System.Serializable]
public sealed class BuffMaxHealth : IEffect<OnSpawnContext>
{
    [SerializeField] float multiplier = 2f;

    public BuffMaxHealth() { }
    public BuffMaxHealth(float multiplier) { this.multiplier = multiplier; }

    public void Apply(OnSpawnContext ctx)
    {
        if (ctx.Owner.TryGetComponent<Health>(out var health))
            health.SetMaxHealthMultiplier(multiplier);
    }
}
