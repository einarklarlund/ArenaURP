using UnityEngine;

/// <summary>
/// Drain the owner's own <see cref="Health"/> by <see cref="amount"/> (ports the old surface/DevilDaggers
/// self-damage). Reads only <see cref="IEffectContext.Owner"/>, so it is typed to the base
/// <see cref="IEffectContext"/> and is authorable on ANY hit — a bullet dying on a wall, on a player, or on
/// contact with another projectile. Death at zero is enforced by ServerCollision (it despawns when Health is
/// dead), so this only needs to subtract.
/// </summary>
[System.Serializable]
public sealed class LoseHealth : IEffect<IEffectContext>
{
    [SerializeField, Tooltip("Health the owner loses on the hit (1 = a normal bullet dies on contact).")]
    private int amount = 1;

    public LoseHealth() { }

    public LoseHealth(int amount) => this.amount = amount;

    public void Apply(IEffectContext ctx)
    {
        if (ctx.Owner != null && ctx.Owner.TryGetComponent<Health>(out var health))
            health.ServerLose(amount);
    }
}
