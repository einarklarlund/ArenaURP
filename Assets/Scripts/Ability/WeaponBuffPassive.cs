using UnityEngine;

/// <summary>
/// A buff passive: adds a standing weapon modifier to a pawn (config = the multipliers). Finds the pawn's
/// <see cref="PawnWeapon"/> in the target hierarchy itself. Authored on a child of the shared passive registry,
/// composable with other <see cref="Passive"/> components. Removal cleanup is deferred (pooling); a fresh pawn
/// re-derives at spawn.
/// </summary>
public sealed class WeaponBuffPassive : Passive
{
    [Tooltip("Standing weapon multipliers folded by the pawn's weapon processor. Identity (all 1) = no effect.")]
    [SerializeField] private WeaponModifier buff = WeaponModifier.Identity;

    public override bool CanApplyTo(GameObject target)
        => target != null && target.GetComponentInChildren<PawnWeapon>() != null;

    public override void Apply(GameObject target)
    {
        var weapon = target.GetComponentInChildren<PawnWeapon>();
        if (weapon != null)
            weapon.Modifiers.Add(buff);
    }
}
