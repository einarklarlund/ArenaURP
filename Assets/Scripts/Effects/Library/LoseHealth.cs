/// <summary>
/// Consume one point of the SOURCE's own <see cref="Health"/> on a hit — a projectile spending durability (a
/// normal bullet dies on contact; a ricochet bullet with MaxHealth &gt; 1 survives that many bounces). Coupled to
/// the HEALTH system: it names <see cref="IHealthContext"/> and takes the source's resolved <see cref="Health"/>
/// handle from the context, so it is authorable on ANY hit (a bullet dying on a wall, on a player, or on contact
/// with another projectile) and does no <c>GetComponent</c>. Death at zero is enforced by ServerCollision (it
/// despawns when the health is dead). Stateless: durability lives on the source's <see cref="Health"/> component.
/// </summary>
[System.Serializable]
public sealed class LoseHealth : IEffect<IHealthContext>
{
    public void Apply(IHealthContext ctx)
    {
        if (ctx.SelfHealth != null)
            ctx.SelfHealth.ServerLose(1);
    }
}
