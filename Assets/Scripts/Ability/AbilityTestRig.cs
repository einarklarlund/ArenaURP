#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor/test-only helper for the ability-inventory PlayMode tests. Lives in Assembly-CSharp so it can
/// touch <see cref="PawnActiveAbilities"/> / <see cref="PlayerPassiveLoadout"/> / <see cref="PassiveRegistry"/> / <see cref="AbilityType"/>
/// directly — the Arena.PlayTests asmdef can't reference Assembly-CSharp, so the tests call these by
/// reflection (one contained seam, per the integration-testing skill). Gated by UNITY_EDITOR so it never ships.
/// </summary>
public static class AbilityTestRig
{
    static readonly List<ActiveAbility> ActivesBuf = new();

    public static AbilityType LoadAbility(string path) => AssetDatabase.LoadAssetAtPath<AbilityType>(path);

    public static void AddActive(GameObject pawn, string abilityPath)
        => pawn.GetComponent<PawnActiveAbilities>().AddActive(LoadAbility(abilityPath) as ActiveAbilityType);

    /// <summary>Bidirectionally link a spawned pawn and NetworkPlayer (as the match flow does) so the pawn's owner —
    /// and its <see cref="PlayerPassiveLoadout"/> — resolves. Needed before granting a passive to a bare pawn.</summary>
    public static void WireOwner(GameObject pawn, GameObject player)
    {
        var p = pawn.GetComponentInChildren<Pawn>(true);
        var np = player.GetComponent<NetworkPlayer>();
        p.ControllingPlayer.Value = np;
        np.ControlledPawn.Value = p;
    }

    /// <summary>Invoke the real server-side use path (same method the F-key ServerRpc calls).</summary>
    public static void Use(GameObject pawn) => pawn.GetComponent<PawnActiveAbilities>().ServerUse();

    public static int ActivesCount(GameObject pawn) => pawn.GetComponent<PawnActiveAbilities>().ActivesCount;

    /// <summary>Count of passive KINDs the pawn's owner holds in its <see cref="PlayerPassiveLoadout"/> (the new
    /// shared-registry model), or -1 if the owner/loadout is not resolved.</summary>
    public static int PassivesCount(GameObject pawn)
    {
        var owner = OwnerPlayer(pawn);
        return owner != null && owner.PassiveLoadout != null ? owner.PassiveLoadout.Owned.Count : -1;
    }

    public static int SelectedIndex(GameObject pawn) => pawn.GetComponent<PawnActiveAbilities>().SelectedIndex.Value;

    public static int Uses(GameObject pawn, int index)
    {
        pawn.GetComponent<PawnActiveAbilities>().GetActives(ActivesBuf);
        return index >= 0 && index < ActivesBuf.Count ? ActivesBuf[index].UsesRemaining.Value : -1;
    }

    /// <summary>
    /// Grant the "TestBonusDamage" KIND — a passive whose InstantiationController contributes an on-player-hit
    /// reaction (instantiate a TestHitMarker at the hit point) to the owner's projectiles. Exercises the
    /// owner-contribution path (the projectile self-clones the owner's KIND handlers at spawn, no payload) with an
    /// inert, directly observable effect. Owner damage bonuses are the deferred damage-buff channel's job.
    /// </summary>
    public static void GrantProjectilePassive(GameObject pawn)
        => GrantPassive(pawn, "TestBonusDamage");

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
