using UnityEngine;

/// <summary>
/// Identity of an always-on ability. ALL gameplay config — the pawn value multipliers and the extra
/// projectile operations — lives on the <see cref="ScriptableTypeObject.InstancePrefab"/>'s
/// <see cref="PassiveAbility"/> component.
/// </summary>
[CreateAssetMenu(fileName = "New Passive Ability", menuName = "Arena/Ability/Passive")]
public sealed class PassiveAbilityType : AbilityType
{
}
