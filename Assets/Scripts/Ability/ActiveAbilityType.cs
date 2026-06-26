using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Triggered ability (used with F): runs a list of <see cref="WorldEffect"/> commands, has limited uses and
/// an optional cooldown, and occupies one of the 5 active inventory slots. The effects are the SAME
/// reusable <see cref="WorldEffect"/> assets a projectile hit reaction runs — referenced here, not authored
/// inline — so an effect written once works for both abilities and projectiles.
/// </summary>
[CreateAssetMenu(fileName = "New Active Ability", menuName = "Arena/Ability/Active")]
public sealed class ActiveAbilityType : AbilityType
{
    [Header("Effects run on use")]
    [Tooltip("World effect assets run when this active ability is used (the same effects projectile reactions use).")]
    public List<WorldEffect> Effects = new();

    [Min(1)] public int Uses = 1;
    [Min(0f)] public float Cooldown = 0f;
}
