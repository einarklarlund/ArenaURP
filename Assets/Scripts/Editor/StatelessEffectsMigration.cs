#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Arena.NetworkedMotion;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot migration for the stateless-commands refactor (Phases 1-3). Effects lost their config fields
/// (state lives on components), ProjectileType lost its gameplay config (moved to the Bullet prefab's
/// components), and abilities became INSTANCE PREFABS (spawned + parented under the pawn) with the old
/// PawnAbilities split into PawnActiveAbilities + PawnPassiveAbilities. This repopulates everything the
/// serialized assets lost:
///
///  1. The Bullet prefab's components get the values previously authored on Bullet.asset.
///  2. Ability instance prefabs are created/refreshed (ScatterBurst, Swift, and the test bonus-damage
///     passive) and each AbilityType asset's InstancePrefab is wired to its prefab.
///  3. Every pawn prefab carrying the old PawnAbilities gets its wiring copied onto the two new components,
///     then the old component (and the now-obsolete pawn-level SpawnableObjects) is removed.
///
/// Idempotent — safe to re-run. Run via the menu or <c>-executeMethod StatelessEffectsMigration.Run</c>.
/// Delete (along with the obsolete PawnAbilities shim) once the assets are committed.
/// </summary>
public static class StatelessEffectsMigration
{
    const string BulletPath = "Assets/Data/Projectiles/Bullet.asset";
    const string ScatterBurstPath = "Assets/Data/Abilities/ScatterBurst.asset";
    const string SwiftPath = "Assets/Data/Abilities/Swift.asset";
    const string AbilityPrefabFolder = "Assets/Prefabs/Abilities";
    const string ScatterBurstPrefabPath = AbilityPrefabFolder + "/ScatterBurst.prefab";
    const string SwiftPrefabPath = AbilityPrefabFolder + "/Swift.prefab";
    const string TestPassiveAssetPath = "Assets/Tests/PlayMode/Prefabs/TestBonusDamagePassive.asset";
    const string TestPassivePrefabPath = "Assets/Tests/PlayMode/Prefabs/TestBonusDamagePassive.prefab";

    // Values previously authored on Bullet.asset (the ProjectileType SO).
    const float BulletRadius = 0.2f;
    const float BulletLifetime = 5.15f;
    const int BulletMaxHealth = 1;
    const int BulletDamage = 3;
    const float BulletSpeed = 60f;

    // Values previously authored on ScatterBurst.asset / Swift.asset.
    const int ScatterUses = 3;
    const float ScatterCooldown = 0.5f;
    const int ScatterCount = 12;
    const float ScatterSpreadDegrees = 360f;
    const float SwiftWalkSpeed = 1.4f;
    const float SwiftWalkAcceleration = 1.2f;

    [MenuItem("Tools/Migrations/Populate Stateless Effect Assets (stateless-commands refactor)")]
    public static void Run()
    {
        var bullet = AssetDatabase.LoadAssetAtPath<ProjectileType>(BulletPath);
        if (bullet == null || bullet.InstancePrefab == null)
        {
            Debug.LogWarning($"[StatelessEffectsMigration] Missing {BulletPath} or its InstancePrefab — aborting.");
            return;
        }

        MigrateBulletPrefab(bullet);
        MigrateAbilityPrefabs(bullet);
        MigratePawnPrefabs();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[StatelessEffectsMigration] Complete.");
    }

    // ---- 1) Bullet prefab: bake the values that used to live on Bullet.asset into its components ----

    static void MigrateBulletPrefab(ProjectileType bullet)
    {
        string path = AssetDatabase.GetAssetPath(bullet.InstancePrefab);
        var contents = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (contents.TryGetComponent<NetworkedMotion>(out var motion))
            {
                motion.Motion = new LinearMotion(0f);
                motion.InitialSpeed = BulletSpeed;
            }
            if (contents.TryGetComponent<ServerCollision>(out var collision))
            {
                collision.Radius = BulletRadius;
                collision.Lifetime = BulletLifetime;
                collision.OnSurface = new List<IEffect<ProjectileSurfaceCollision>> { new LoseHealth() };
                // Die-on-player-hit is authored data: LoseHealth AFTER DealDamage, so the bullet spends its
                // 1 health on the hit and ServerCollision despawns it.
                collision.OnPlayer = new List<IEffect<ProjectilePlayerCollision>> { new DealDamage(), new LoseHealth() };
                collision.OnProjectile = new List<IEffect<ProjectileProjectileCollision>>();
            }
            if (contents.TryGetComponent<Health>(out var health))
            {
                var so = new SerializedObject(health);
                so.FindProperty("maxHealth").intValue = BulletMaxHealth;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            if (contents.TryGetComponent<Damage>(out var damage))
                damage.Amount = BulletDamage;

            PrefabUtility.SaveAsPrefabAsset(contents, path);
            Debug.Log($"[StatelessEffectsMigration] Baked Bullet.asset values into {path}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // ---- 2) Ability instance prefabs + type wiring ----

    static void MigrateAbilityPrefabs(ProjectileType bullet)
    {
        if (!AssetDatabase.IsValidFolder(AbilityPrefabFolder))
            AssetDatabase.CreateFolder("Assets/Prefabs", "Abilities");

        // ScatterBurst: active, ring of 12 bullets. The fan config is a sibling SpawnableObjects on the
        // ability's own prefab, referenced explicitly by the SpawnNetworkObjects op (the one allowed ref).
        var scatter = AssetDatabase.LoadAssetAtPath<ActiveAbilityType>(ScatterBurstPath);
        if (scatter != null)
        {
            BuildAbilityPrefab(ScatterBurstPrefabPath, scatter, go =>
            {
                var spawnable = go.AddComponent<SpawnableObjects>();
                var so = new SerializedObject(spawnable);
                var prefabs = so.FindProperty("prefabs");
                prefabs.arraySize = 1;
                prefabs.GetArrayElementAtIndex(0).objectReferenceValue = bullet.InstancePrefab;
                so.FindProperty("count").intValue = ScatterCount;
                so.FindProperty("spreadDegrees").floatValue = ScatterSpreadDegrees;
                so.ApplyModifiedPropertiesWithoutUndo();

                var active = go.AddComponent<ActiveAbility>();
                active.DefaultUses = ScatterUses;
                active.Cooldown = ScatterCooldown;
                active.OnUse = new List<IEffect<AbilityAim>> { new SpawnNetworkObjects(spawnable) };
            });
        }
        else
        {
            Debug.LogWarning($"[StatelessEffectsMigration] Missing {ScatterBurstPath}");
        }

        // Swift: passive walk-speed boost.
        var swift = AssetDatabase.LoadAssetAtPath<PassiveAbilityType>(SwiftPath);
        if (swift != null)
        {
            BuildAbilityPrefab(SwiftPrefabPath, swift, go =>
            {
                var passive = go.AddComponent<PassiveAbility>();
                passive.WalkSpeedMultiplier = SwiftWalkSpeed;
                passive.WalkAccelerationMultiplier = SwiftWalkAcceleration;
            });
        }
        else
        {
            Debug.LogWarning($"[StatelessEffectsMigration] Missing {SwiftPath}");
        }

        // Test passive: extra DealDamage on the owner's projectiles' player hits (used by
        // AbilityTestRig.GrantProjectilePassive / the owner-lookup PlayMode test).
        var testPassive = AssetDatabase.LoadAssetAtPath<PassiveAbilityType>(TestPassiveAssetPath);
        if (testPassive == null)
        {
            testPassive = ScriptableObject.CreateInstance<PassiveAbilityType>();
            testPassive.DisplayName = "Test Projectile Passive";
            AssetDatabase.CreateAsset(testPassive, TestPassiveAssetPath);
        }
        BuildAbilityPrefab(TestPassivePrefabPath, testPassive, go =>
        {
            var passive = go.AddComponent<PassiveAbility>();
            passive.ProjectileOnPlayer = new List<IEffect<ProjectilePlayerCollision>> { new DealDamage() };
        });
    }

    /// <summary>
    /// Create (or rebuild in place, preserving the GUID) one logical ability instance prefab:
    /// NetworkObject + TypeObjectRoot(baked type) + whatever <paramref name="configure"/> adds. Then wire the
    /// type asset's InstancePrefab back at the saved prefab (the deliberate bidirectional identity ref).
    /// </summary>
    static void BuildAbilityPrefab(string prefabPath, AbilityType type, System.Action<GameObject> configure)
    {
        GameObject working;
        bool fromExisting = File.Exists(prefabPath);
        if (fromExisting)
        {
            working = PrefabUtility.LoadPrefabContents(prefabPath);
            // Rebuild content deterministically: strip everything but transform + NetworkObject.
            foreach (var c in working.GetComponents<Component>())
                if (c is not Transform && c is not NetworkObject)
                    Object.DestroyImmediate(c);
        }
        else
        {
            working = new GameObject(type.name);
            working.AddComponent<NetworkObject>();
        }

        try
        {
            var root = working.GetComponent<TypeObjectRoot>();
            if (root == null)
                root = working.AddComponent<TypeObjectRoot>();
            var rootSo = new SerializedObject(root);
            rootSo.FindProperty("type").objectReferenceValue = type;
            rootSo.ApplyModifiedPropertiesWithoutUndo();

            configure(working);

            var saved = PrefabUtility.SaveAsPrefabAsset(working, prefabPath);

            var typeSo = new SerializedObject(type);
            typeSo.FindProperty("instancePrefab").objectReferenceValue = saved.GetComponent<NetworkObject>();
            typeSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(type);

            Debug.Log($"[StatelessEffectsMigration] Built ability prefab {prefabPath}");
        }
        finally
        {
            if (fromExisting)
                PrefabUtility.UnloadPrefabContents(working);
            else
                Object.DestroyImmediate(working);
        }
    }

    // ---- 3) Pawn prefabs: old PawnAbilities -> PawnActiveAbilities + PawnPassiveAbilities ----

#pragma warning disable 0618 // PawnAbilities is the obsolete migration shim we're draining
    static void MigratePawnPrefabs()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
                continue;
            bool hasOld = asset.GetComponentInChildren<PawnAbilities>(true) != null;
            bool hasNew = asset.GetComponentInChildren<PawnActiveAbilities>(true) != null;
            if (!hasOld && !hasNew)
                continue;

            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var old = contents.GetComponentInChildren<PawnAbilities>(true);
                var host = old != null ? old.gameObject : contents.GetComponentInChildren<PawnActiveAbilities>(true).gameObject;

                // Wiring: prefer the old component's (possibly variant-overridden) refs; fall back to siblings.
                Pawn pawn = old != null && old.Pawn != null ? old.Pawn : host.GetComponent<Pawn>();
                PawnInput input = old != null && old.Input != null ? old.Input : host.GetComponent<PawnInput>();
                PawnMovement movement = old != null && old.Movement != null ? old.Movement : host.GetComponent<PawnMovement>();
                PawnWeapon weapon = old != null && old.Weapon != null ? old.Weapon : host.GetComponent<PawnWeapon>();
                Transform aim = old != null ? old.AimSource : null;

                if (!host.TryGetComponent<PawnActiveAbilities>(out var actives))
                    actives = host.AddComponent<PawnActiveAbilities>();
                var aso = new SerializedObject(actives);
                aso.FindProperty("pawn").objectReferenceValue = pawn;
                aso.FindProperty("input").objectReferenceValue = input;
                aso.FindProperty("aimSource").objectReferenceValue = aim;
                aso.ApplyModifiedPropertiesWithoutUndo();

                if (!host.TryGetComponent<PawnPassiveAbilities>(out var passives))
                    passives = host.AddComponent<PawnPassiveAbilities>();
                var pso = new SerializedObject(passives);
                pso.FindProperty("movement").objectReferenceValue = movement;
                pso.FindProperty("weapon").objectReferenceValue = weapon;
                pso.ApplyModifiedPropertiesWithoutUndo();

                if (old != null)
                    Object.DestroyImmediate(old);

                // The Phase-1 pawn-level SpawnableObjects fallback is superseded: the scatter fan config now
                // lives on the ScatterBurst ability prefab with an explicit source ref.
                if (host.TryGetComponent<SpawnableObjects>(out var staleSpawnable))
                    Object.DestroyImmediate(staleSpawnable);

                PrefabUtility.SaveAsPrefabAsset(contents, path);
                Debug.Log($"[StatelessEffectsMigration] Migrated pawn abilities on {path}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
    }
#pragma warning restore 0618
}
#endif
