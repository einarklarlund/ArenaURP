using System.Collections.Generic;
using Arena.Ability;
using UnityEngine;

/// <summary>
/// Triggered ability (used with F): emits composable effects, has limited uses and an optional cooldown,
/// and occupies one of the 5 active inventory slots. Effects are authored polymorphically via
/// <c>[SerializeReference]</c>, exactly like the reactions on a projectile type.
/// </summary>
[CreateAssetMenu(fileName = "New Active Ability", menuName = "Arena/Ability/Active")]
public sealed class ActiveAbilityType : AbilityType
{
    [Header("Effects emitted on use")]
    [Tooltip("Composable effects run when this active ability is used (authored polymorphically, like projectile reactions).")]
    [SerializeReference] public List<IAbilityEffect> Effects = new();

    [Min(1)] public int Uses = 1;
    [Min(0f)] public float Cooldown = 0f;
}
