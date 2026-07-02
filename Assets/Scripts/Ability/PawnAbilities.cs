using UnityEngine;

/// <summary>
/// OBSOLETE migration shell — the ability inventory was split into <see cref="PawnActiveAbilities"/> and
/// <see cref="PawnPassiveAbilities"/>. This class keeps only the previously-serialized wiring fields so
/// <c>StatelessEffectsMigration</c> can read each pawn prefab's (possibly variant-overridden) references and
/// copy them onto the two new components before removing this one. Delete this file once the pawn prefabs
/// are migrated and committed.
/// </summary>
[System.Obsolete("Split into PawnActiveAbilities + PawnPassiveAbilities; exists only for prefab migration.")]
public sealed class PawnAbilities : MonoBehaviour
{
    [SerializeField] private Pawn pawn;
    [SerializeField] private PawnInput input;
    [SerializeField] private PawnMovement movement;
    [SerializeField] private PawnWeapon weapon;
    [SerializeField] private Transform aimSource;

    public Pawn Pawn => pawn;
    public PawnInput Input => input;
    public PawnMovement Movement => movement;
    public PawnWeapon Weapon => weapon;
    public Transform AimSource => aimSource;
}
