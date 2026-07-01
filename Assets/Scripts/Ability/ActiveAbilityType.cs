using System.Collections.Generic;
using MackySoft.SerializeReferenceExtensions;
using UnityEngine;

/// <summary>
/// Triggered ability (used with F): runs a set of <see cref="IEffect{TContext}"/> commands AUTHORED AS DATA
/// against an <see cref="AbilityAim"/> context (the caster's origin + aim), has limited uses and an optional
/// cooldown, and occupies one of the 5 active inventory slots. These are the SAME reusable operation commands a
/// projectile reaction runs — an operation valid on an ability (e.g. <see cref="SpawnProjectiles"/>) is one
/// that only needs an origin + direction, which the <see cref="AbilityAim"/> typing enforces.
/// </summary>
[CreateAssetMenu(fileName = "New Active Ability", menuName = "Arena/Ability/Active")]
public sealed class ActiveAbilityType : AbilityType
{
    [Min(1)] public int Uses = 1;
    [Min(0f)] public float Cooldown = 0f;

    [Header("Operations run when the ability is used (aimed from the caster)")]
    [SerializeReference, EffectSelector]
    public List<IEffect<AbilityAim>> OnUse = new();
}
