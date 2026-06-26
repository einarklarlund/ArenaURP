using UnityEngine;

/// <summary>
/// Abstract Type Object (ScriptableObject) for one ability. Concrete kinds are
/// <see cref="PassiveAbilityType"/> (always-on pawn/projectile modifiers) and
/// <see cref="ActiveAbilityType"/> (triggered, limited-use effects).
///
/// Subtype REPLACES the former <c>AbilityKind</c> type-code: an ability IS its kind, so there is no
/// runtime field gating which half of the data is live, and no asset carries fields that don't apply to
/// it. Authored as assets, registered in the <see cref="AbilityDatabase"/>, and granted by pickups.
/// Synced over the wire as a database index (see <see cref="AbilityTypeSerializer"/>), so the base type
/// is what the network layer and inventory hold; call sites narrow with <c>is</c> where they need a kind.
/// </summary>
public abstract class AbilityType : ScriptableObject
{
    [Header("Identity")]
    public string DisplayName = "Ability";
    public Sprite Icon;
}
