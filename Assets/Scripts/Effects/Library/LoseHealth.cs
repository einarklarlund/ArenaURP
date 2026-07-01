using UnityEngine;

/// <summary>
/// Drain the invoking projectile's own <see cref="Health"/> by <see cref="amount"/> on a surface hit (ports
/// the old surface/DevilDaggers self-damage). Death at zero is enforced by ServerCollision (it despawns when
/// Health is dead), so this only needs to subtract.
/// </summary>
[System.Serializable]
public sealed class LoseHealth : IEffect<ProjectileSurfaceCollision>
{
    [SerializeField, Tooltip("Health the projectile loses on a surface hit (1 = a normal bullet dies on contact).")]
    private int amount = 1;

    public LoseHealth() { }

    public LoseHealth(int amount) => this.amount = amount;

    public void Apply(ProjectileSurfaceCollision ctx)
    {
        if (ctx.Invoker != null && ctx.Invoker.TryGetComponent<Health>(out var health))
            health.ServerLose(amount);
    }
}
