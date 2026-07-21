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
    /// <summary>Prefab-backed test passive whose PassiveAbility contributes an InstantiateGameObjects reaction
    /// (a TestHitMarker at the hit point) on player hits — the observable for the owner-lookup pin.</summary>
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
    /// Grant the prefab-backed test passive that contributes an on-player-hit reaction (instantiate a
    /// TestHitMarker at the hit point) to the owner's projectiles. Exercises the owner-lookup path
    /// (projectile finds owner's effects server-side, no payload) with an inert, directly observable effect.
    /// The old shape — an extra DealDamage stacking the exchange — died with the couriers (ADR 0002); owner
    /// damage bonuses are the deferred damage-buff channel's job.
    /// </summary>
    public static void GrantProjectilePassive(GameObject pawn)
        => AddPassive(pawn, TestProjectilePassivePath);

    /// <summary>Passive whose PassiveAbility.SpawnBuff multiplies spawned projectiles' speed ×2 (on every peer, at spawn).</summary>
    const string TestSpeedBuffPassivePath = "Assets/Tests/PlayMode/Prefabs/TestSpeedBuffPassive.asset";

    /// <summary>Passive whose PassiveAbility.SpawnBuff multiplies spawned projectiles' scale ×2 (on every peer, at spawn).</summary>
    const string TestScaleBuffPassivePath = "Assets/Tests/PlayMode/Prefabs/TestScaleBuffPassive.asset";

    /// <summary>Grant the on-spawn speed passive KIND — exercises the every-peer on-spawn buff path (SpeedPassive → NetworkedMotion).</summary>
    public static void GrantSpeedBuffPassive(GameObject pawn) => GrantPassive(pawn, "TestSpeed");

    /// <summary>Grant the on-spawn scale passive KIND — exercises the no-sync-channel snapshot path (ScalePassive + CollisionRadiusPassive).</summary>
    public static void GrantScaleBuffPassive(GameObject pawn) => GrantPassive(pawn, "TestScale");

    /// <summary>Grant a passive KIND (a child of the spawned <see cref="PassiveRegistry"/>) into the pawn owner's
    /// <see cref="PlayerPassiveLoadout"/> — the new-model server grant path (a reference add). Requires the registry
    /// to be spawned in the scene and the pawn's owner (NetworkPlayer) resolved.</summary>
    public static void GrantPassive(GameObject pawn, string kindName)
    {
        var owner = OwnerPlayer(pawn);
        var kind = PassiveRegistry.Resolve(kindName);
        owner.PassiveLoadout.Grant(kind);
    }

    static NetworkPlayer OwnerPlayer(GameObject pawn)
    {
        var p = pawn.GetComponentInParent<Pawn>();
        return p != null ? p.ControllingPlayer.Value : null;
    }

    /// <summary>
    /// Product of every <see cref="WeaponModifier.FireRateMultiplier"/> currently folded into the pawn's weapon
    /// (1 when none). Used to assert a passive's authored <c>WeaponBuff</c> stat modifier reaches
    /// <c>PawnWeapon.Modifiers</c> through the passive re-derive path.
    /// </summary>
    public static float FireRateProduct(GameObject pawn)
    {
        var weapon = pawn.GetComponent<PawnWeapon>();
        float product = 1f;
        foreach (var m in weapon.Modifiers) product *= m.FireRateMultiplier;
        return product;
    }
}
#endif
