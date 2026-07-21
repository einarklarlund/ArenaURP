using UnityEngine;

/// <summary>
/// A passive coupled to <see cref="PawnWeapon"/>: adds a standing weapon modifier to a pawn (config = the
/// multipliers; &gt;1 buffs, &lt;1 debuffs, identity = no effect). Coupled to the ONE component it affects — it
/// finds the pawn's <see cref="PawnWeapon"/> in the target hierarchy itself. Authored on a child of the shared
/// passive registry, composable with other <see cref="Passive"/> components on the same KIND. Removal cleanup is
/// deferred (pooling); a fresh pawn re-derives at spawn.
/// </summary>
public sealed class WeaponPassive : Passive
{
    [Tooltip("Standing weapon multipliers folded by the pawn's weapon processor. Identity (all 1) = no effect; " +
             ">1 buffs, <1 debuffs.")]
    [SerializeField] private WeaponModifier modifier = WeaponModifier.Identity;

    public override bool CanApplyTo(GameObject target)
        => target != null && target.GetComponentInChildren<PawnWeapon>() != null;

    public override void Apply(GameObject target)
    {
        var weapon = target.GetComponentInChildren<PawnWeapon>();
        if (weapon != null)
            weapon.Modifiers.Add(modifier);
    }
}
