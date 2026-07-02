#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor/test-only helper for the ability-inventory PlayMode tests. Lives in Assembly-CSharp so it can
/// touch <see cref="PawnActiveAbilities"/> / <see cref="PawnPassiveAbilities"/> / <see cref="AbilityType"/>
/// directly — the Arena.PlayTests asmdef can't reference Assembly-CSharp, so the tests call these by
/// reflection (one contained seam, per the integration-testing skill). Gated by UNITY_EDITOR so it never ships.
/// </summary>
public static class AbilityTestRig
{
    /// <summary>Prefab-backed test passive whose PassiveAbility contributes an extra DealDamage on player hits.</summary>
    const string TestProjectilePassivePath = "Assets/Tests/PlayMode/Prefabs/TestBonusDamagePassive.asset";

    static readonly List<ActiveAbility> ActivesBuf = new();

    public static AbilityType LoadAbility(string path) => AssetDatabase.LoadAssetAtPath<AbilityType>(path);

    public static void AddActive(GameObject pawn, string abilityPath)
        => pawn.GetComponent<PawnActiveAbilities>().AddActive(LoadAbility(abilityPath) as ActiveAbilityType);

    public static void AddPassive(GameObject pawn, string abilityPath)
        => pawn.GetComponent<PawnPassiveAbilities>().AddPassive(LoadAbility(abilityPath) as PassiveAbilityType);

    /// <summary>Invoke the real server-side use path (same method the F-key ServerRpc calls).</summary>
    public static void Use(GameObject pawn) => pawn.GetComponent<PawnActiveAbilities>().ServerUse();

    public static int ActivesCount(GameObject pawn) => pawn.GetComponent<PawnActiveAbilities>().ActivesCount;
    public static int PassivesCount(GameObject pawn) => pawn.GetComponent<PawnPassiveAbilities>().PassivesCount;
    public static int SelectedIndex(GameObject pawn) => pawn.GetComponent<PawnActiveAbilities>().SelectedIndex.Value;

    public static int Uses(GameObject pawn, int index)
    {
        pawn.GetComponent<PawnActiveAbilities>().GetActives(ActivesBuf);
        return index >= 0 && index < ActivesBuf.Count ? ActivesBuf[index].UsesRemaining.Value : -1;
    }

    /// <summary>
    /// Grant the prefab-backed test passive that adds an extra DealDamage to the owner's projectiles on
    /// enemy hit. Exercises the owner-lookup path (projectile finds owner's effects server-side, no
    /// payload). DealDamage is stateless (it reads the projectile's own Damage component), so the
    /// contributed hit deals the projectile's damage a second time.
    /// </summary>
    public static void GrantProjectilePassive(GameObject pawn)
        => AddPassive(pawn, TestProjectilePassivePath);
}
#endif
