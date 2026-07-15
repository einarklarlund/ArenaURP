using UnityEngine;

/// <summary>
/// Identity of a triggered ability (used with F, occupies one of the 5 active inventory slots). ALL gameplay
/// config — default uses, cooldown, and the use-time <see cref="SpawnNetworkObjects"/> behaviours run on use —
/// lives on the <see cref="ScriptableTypeObject.InstancePrefab"/>'s <see cref="ActiveAbility"/> component,
/// alongside the <see cref="SpawnableObjects"/> those behaviours read.
/// </summary>
[CreateAssetMenu(fileName = "New Active Ability", menuName = "Arena/Ability/Active")]
public sealed class ActiveAbilityType : AbilityType
{
}
