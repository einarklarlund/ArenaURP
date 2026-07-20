using UnityEngine;

/// <summary>
/// A buff passive: adds a standing movement modifier to a pawn (config = the multipliers). It knows its target
/// hierarchy — it finds the pawn's <see cref="PawnMovement"/> itself. Authored on a child of the shared passive
/// registry, composable with other <see cref="Passive"/> components. With pooling/removal cleanup deferred, the
/// modifier is not un-applied on loadout change; a fresh pawn re-derives at spawn.
/// </summary>
public sealed class MovementBuffPassive : Passive
{
    [Tooltip("Standing movement multipliers folded by the pawn's movement processor. Identity (all 1) = no effect.")]
    [SerializeField] private MovementModifier buff = MovementModifier.Identity;

    public override bool CanApplyTo(GameObject target)
        => target != null && target.GetComponentInChildren<PawnMovement>() != null;

    public override void Apply(GameObject target)
    {
        var movement = target.GetComponentInChildren<PawnMovement>();
        if (movement != null)
            movement.Modifiers.Add(buff);
    }
}
