using UnityEngine;

/// <summary>
/// Identity of a triggered ability (used with F, occupies one of the 5 active inventory slots). ALL gameplay
/// config — default uses, cooldown, and the authored <see cref="IEffect{TContext}"/> operations run on use —
/// lives on the <see cref="ScriptableTypeObject.InstancePrefab"/>'s <see cref="ActiveAbility"/> component,
/// alongside any <see cref="SpawnableObjects"/> / <see cref="InstantiableObjects"/> its effects reference.
/// </summary>
[CreateAssetMenu(fileName = "New Active Ability", menuName = "Arena/Ability/Active")]
public sealed class ActiveAbilityType : AbilityType
{
}
