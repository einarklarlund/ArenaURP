using UnityEngine;

/// <summary>
/// Standing weapon buff: multiply the weapon's spread angle while the source passive is equipped (less than 1 =
/// tighter spread). Typed to <see cref="IWeaponModifierContext"/>; contributes one <see cref="WeaponModifier"/>
/// folded multiplicatively by the pure WeaponProcessor. Stateless flyweight: only the immutable serialized
/// multiplier.
/// </summary>
[System.Serializable]
public sealed class BuffSpread : IEffect<IWeaponModifierContext>
{
    [SerializeField] float multiplier = 0.75f;

    public BuffSpread() { }
    public BuffSpread(float multiplier) { this.multiplier = multiplier; }

    public void Apply(IWeaponModifierContext ctx)
    {
        var w = WeaponModifier.Identity;
        w.SpreadMultiplier = multiplier;
        ctx.AddWeaponModifier(w);
    }
}
