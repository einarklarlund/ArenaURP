using UnityEngine;

/// <summary>
/// Apply a debuff to the player that was hit. No-op until a debuff system exists; when one is added, this
/// resolves the target's debuff receiver and applies <see cref="debuff"/> directly. Typed to
/// <see cref="ProjectilePlayerCollision"/>, so the target is guaranteed present.
/// </summary>
[System.Serializable]
public sealed class ApplyDebuff : IEffect<ProjectilePlayerCollision>
{
    [SerializeField] private DebuffType debuff = DebuffType.Slow;

    public ApplyDebuff() { }

    public ApplyDebuff(DebuffType debuff) => this.debuff = debuff;

    public void Apply(ProjectilePlayerCollision ctx)
    {
        if (ctx.Target == null)
            return;
        // TODO: no debuff system exists yet. Intentionally a no-op until one is introduced —
        // e.g. ctx.Target.GetComponentInParent<IDebuffable>()?.Apply(debuff);
    }
}
