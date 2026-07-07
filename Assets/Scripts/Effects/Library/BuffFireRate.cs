using UnityEngine;

/// <summary>
/// Standing weapon buff: multiply the weapon's fire-rate cooldown while the source passive is equipped (less than
/// 1 = faster fire). Typed to <see cref="IWeaponModifierContext"/> so the variance-aware picker offers it on a
/// passive's stat-buff list. Contributes one <see cref="WeaponModifier"/> folded multiplicatively by the pure
/// WeaponProcessor rather than mutating the pawn. Stateless flyweight: only the immutable serialized multiplier.
/// </summary>
[System.Serializable]
public sealed class BuffFireRate : IEffect<IWeaponModifierContext>
{
    [SerializeField] float multiplier = 0.75f;

    public BuffFireRate() { }
    public BuffFireRate(float multiplier) { this.multiplier = multiplier; }

    public void Apply(IWeaponModifierContext ctx)
    {
        var w = WeaponModifier.Identity;
        w.FireRateMultiplier = multiplier;
        ctx.AddWeaponModifier(w);
    }
}
