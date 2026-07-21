using UnityEngine;

/// <summary>
/// Abstract Type Object for one ACTIVE ability: IDENTITY (display name, icon — the UI-facing data) plus the
/// <see cref="ScriptableTypeObject.InstancePrefab"/> holding the ability's gameplay config and live state.
/// The one concrete kind is <see cref="ActiveAbilityType"/>; its prefab carries an <see cref="ActiveAbility"/>
/// component. (Passives no longer use this shape — a passive KIND is a child of the shared
/// <see cref="PassiveRegistry"/>, referenced by a <see cref="PlayerPassiveLoadout"/>; see ADR 0009.)
///
/// Granting an active = spawning its instance prefab parented under the owning pawn (see
/// <see cref="PawnActiveAbilities"/>), so nothing about WHICH ability rides the wire — FishNet replicates the
/// spawned prefab, and every peer resolves identity locally via the instance's <see cref="TypeObjectRoot"/>
/// back-ref to this asset.
/// </summary>
public abstract class AbilityType : ScriptableTypeObject
{
    [Header("Identity")]
    public string DisplayName = "Ability";
    public Sprite Icon;
}
