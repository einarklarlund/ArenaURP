using FishNet.Object;
using UnityEngine;

/// <summary>
/// The stateful half of an always-on ability, living on the ability's INSTANCE prefab — a logical
/// NetworkObject spawned by <see cref="PawnPassiveAbilities"/> and parented under the owning pawn. The stat
/// buffs and on-spawn buff are serialized prefab data (runtime-tunable per instance); while this instance exists
/// under a pawn, <see cref="PawnPassiveAbilities"/> folds the authored <see cref="MovementBuff"/>/<see cref="WeaponBuff"/>
/// modifiers into the pawn's movement/weapon modifier lists. Contributed hit reactions are
/// <see cref="IOwnedObjectContribution"/> components on this prefab, self-cloned onto the owner's spawned objects at
/// spawn (<see cref="SpawnContributionReceiver"/>), never harvested at the hit site.
///
/// Identity (display name / icon) resolves through the sibling <see cref="TypeObjectRoot"/>'s baked
/// <see cref="AbilityType"/>.
/// </summary>
public sealed class PassiveAbility : NetworkBehaviour
{
    [Header("Stat buffs (folded into the pawn's movement/weapon modifiers while equipped)")]
    [Tooltip("Standing movement multipliers folded by the pawn's movement processor while equipped. Identity (all 1) = no effect.")]
    public MovementModifier MovementBuff = MovementModifier.Identity;

    [Tooltip("Standing weapon multipliers folded by the pawn's weapon processor while equipped. Identity (all 1) = no effect.")]
    public WeaponModifier WeaponBuff = WeaponModifier.Identity;

    // Contributed hit reactions (redirect, on-hit instantiate, steal-on-hit, …) are IOwnedObjectContribution
    // components authored directly on this instance prefab beside PassiveAbility (ADR 0005) — each self-clones its
    // handler onto the owner's spawned objects at spawn (SpawnContributionReceiver), no serialized list and no hit-site
    // harvest. See RedirectionController / InstantiationController / PassiveStealController.

    [Header("On-spawn buffs (applied on every peer to each object this pawn spawns, at spawn)")]
    [Tooltip("Transform the owner's spawned objects at spawn (e.g. faster/bigger/tougher bullets). Identity (all 1) " +
             "= no effect. Multiple equipped passives' SpawnBuffs multiply. On-spawn only.")]
    public SpawnModifier SpawnBuff = SpawnModifier.Identity;

    private PawnPassiveAbilities _inventory;

    /// <summary>This ability's identity SO, via the sibling TypeObjectRoot (null if none baked).</summary>
    public AbilityType Type => TryGetComponent<TypeObjectRoot>(out var root) ? root.Type as AbilityType : null;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _inventory = GetComponentInParent<PawnPassiveAbilities>();
        NotifyInventory();
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        // Removal-first (the RemoveAt-before-Despawn discipline): destruction is deferred to end of frame and
        // FishNet flips IsSpawned only AFTER the stop callbacks, so while this notify re-derives, our corpse is
        // still an enumerable, spawned-looking child — pass ourselves as the leaver so the re-derive skips us.
        if (_inventory != null)
            _inventory.NotifyInventoryChanged(leaving: this);
        _inventory = null;
    }

    private void NotifyInventory()
    {
        if (_inventory != null)
            _inventory.NotifyInventoryChanged();
    }
}
