#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor/test-only helper for the ability-inventory PlayMode tests. Lives in Assembly-CSharp so it can
/// touch <see cref="PawnAbilities"/> / <see cref="AbilityType"/> directly — the Arena.PlayTests asmdef
/// can't reference Assembly-CSharp, so the tests call these by reflection (one contained seam, per the
/// integration-testing skill). Gated by UNITY_EDITOR so it never ships.
/// </summary>
public static class AbilityTestRig
{
    public static AbilityType LoadAbility(string path) => AssetDatabase.LoadAssetAtPath<AbilityType>(path);

    public static void AddActive(GameObject pawn, string abilityPath)
        => pawn.GetComponent<PawnAbilities>().AddActive(LoadAbility(abilityPath) as ActiveAbilityType);

    public static void AddPassive(GameObject pawn, string abilityPath)
        => pawn.GetComponent<PawnAbilities>().AddPassive(LoadAbility(abilityPath) as PassiveAbilityType);

    /// <summary>Invoke the real server-side use path (same method the F-key ServerRpc calls).</summary>
    public static void Use(GameObject pawn) => pawn.GetComponent<PawnAbilities>().ServerUse();

    public static int ActivesCount(GameObject pawn) => pawn.GetComponent<PawnAbilities>().Actives.Count;
    public static int PassivesCount(GameObject pawn) => pawn.GetComponent<PawnAbilities>().Passives.Count;
    public static int SelectedIndex(GameObject pawn) => pawn.GetComponent<PawnAbilities>().SelectedIndex.Value;

    public static int Uses(GameObject pawn, int index)
    {
        var a = pawn.GetComponent<PawnAbilities>().Actives;
        return index >= 0 && index < a.Count ? a[index].UsesRemaining : -1;
    }

    /// <summary>
    /// Grant a runtime passive that adds a bonus DealDamage to the owner's projectiles on enemy hit.
    /// Exercises the owner-lookup path (projectile finds owner's effects server-side, no payload). The
    /// runtime SO isn't in the AbilityDatabase, which is fine: this is read on the host server where the
    /// SyncList holds the real reference.
    /// </summary>
    public static void GrantProjectilePassive(GameObject pawn, int bonusDamage)
    {
        var ability = ScriptableObject.CreateInstance<PassiveAbilityType>();
        ability.DisplayName = "Test Projectile Passive";
        var bonus = ScriptableObject.CreateInstance<DealDamage>();
        bonus.UseInvokerDamage = false; // a fixed bonus, not the projectile's own damage
        bonus.Amount = bonusDamage;
        bonus.DespawnAfter = false;
        ability.ProjectileOnEnemy = new System.Collections.Generic.List<EnemyHitReaction>
        {
            HitReaction.CreateRuntime<EnemyHitReaction>(bonus),
        };
        pawn.GetComponent<PawnAbilities>().AddPassive(ability);
    }
}
#endif
