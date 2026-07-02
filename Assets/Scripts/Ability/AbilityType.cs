using UnityEngine;

/// <summary>
/// Abstract Type Object for one ability: IDENTITY (display name, icon — the UI-facing data) plus the
/// <see cref="ScriptableTypeObject.InstancePrefab"/> holding the ability's gameplay config and live state.
/// Concrete kinds are <see cref="PassiveAbilityType"/> and <see cref="ActiveAbilityType"/>; the prefab
/// carries the matching <see cref="PassiveAbility"/> / <see cref="ActiveAbility"/> component.
///
/// Granting an ability = spawning its instance prefab parented under the owning pawn (see PawnAbilities), so
/// nothing about WHICH ability rides the wire — FishNet replicates the spawned prefab, and every peer
/// resolves identity locally via the instance's <see cref="TypeObjectRoot"/> back-ref to this asset.
/// </summary>
public abstract class AbilityType : ScriptableTypeObject
{
    [Header("Identity")]
    public string DisplayName = "Ability";
    public Sprite Icon;
}
