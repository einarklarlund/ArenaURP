using UnityEngine;

/// <summary>
/// On-spawn buff: multiply the spawned object's initial motion speed. Typed to <see cref="OnSpawnContext"/> so the
/// variance-aware picker offers it only on passives' OnSpawn list (never in a collision list). Reaches the MOTION
/// system by <c>GetComponent</c> off the spawned object and folds through the idempotent
/// <see cref="NetworkedMotion.SetSpeedMultiplier"/> — order-safe whether it runs before or after
/// <c>NetworkedMotion.OnStartNetwork</c>, and pooling-safe (the multiplier is a per-spawn layer reset in
/// ResetState). Speed has a sync channel, so a client that missed the passive self-heals from the seeded segment
/// SyncVars (see NetworkedMotion). Stateless flyweight: only the immutable serialized multiplier.
/// </summary>
[System.Serializable]
public sealed class BuffProjectileSpeed : IEffect<OnSpawnContext>
{
    [SerializeField] float multiplier = 1.5f;

    public BuffProjectileSpeed() { }
    public BuffProjectileSpeed(float multiplier) { this.multiplier = multiplier; }

    public void Apply(OnSpawnContext ctx)
    {
        if (ctx.Owner.TryGetComponent<NetworkedMotion>(out var motion))
            motion.SetSpeedMultiplier(multiplier);
    }
}
