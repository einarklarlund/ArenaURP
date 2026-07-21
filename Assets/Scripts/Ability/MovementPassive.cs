using UnityEngine;

/// <summary>
/// A passive coupled to <see cref="PawnMovement"/>: adds a standing movement modifier to a pawn (config = the
/// multipliers; &gt;1 buffs, &lt;1 debuffs, identity = no effect). Coupled to the ONE component it affects, not to
/// the moment it is applied — it finds the pawn's <see cref="PawnMovement"/> itself. Authored on a child of the
/// shared passive registry, composable with other <see cref="Passive"/> components on the same KIND. With
/// pooling/removal cleanup deferred, the modifier is not un-applied on loadout change; a fresh pawn re-derives at
/// spawn.
/// </summary>
public sealed class MovementPassive : Passive
{
    [Tooltip("Standing movement multipliers folded by the pawn's movement processor. Identity (all 1) = no effect; " +
             ">1 buffs, <1 debuffs.")]
    [SerializeField] private MovementModifier modifier = MovementModifier.Identity;

    public override bool CanApplyTo(GameObject target)
        => target != null && target.GetComponentInChildren<PawnMovement>() != null;

    public override void Apply(GameObject target)
    {
        var movement = target.GetComponentInChildren<PawnMovement>();
        if (movement != null)
            movement.Modifiers.Add(modifier);
    }
}
