#if UNITY_EDITOR
using System.Collections.Generic;
using Arena.NetworkedMotion;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot migration for the stateless-commands refactor (Phases 1+2). Effects lost their config fields
/// (state lives on components now), and ProjectileType lost ALL gameplay config (radius / health / damage /
/// speed / lifetime / motion / hit-operation lists moved onto the instance prefab's components). This
/// repopulates everything the serialized assets lost:
///
///  1. The Bullet prefab's components get the values previously authored on Bullet.asset (including the
///     die-on-player-hit LoseHealth that the pre-refactor DealDamage.despawnAfter used to provide).
///  2. ScatterBurst.asset's OnUse gets a stateless SpawnNetworkObjects (its old SpawnProjectiles class was
///     deleted, leaving a missing managed reference).
///  3. Every pawn prefab carrying PawnAbilities gets the scatter fan config as a SpawnableObjects on the
///     Pawn's own GameObject (the ability context's Owner is the pawn, so that is where GetComponent looks).
///
/// Idempotent — safe to re-run. Run via the menu or <c>-executeMethod StatelessEffectsMigration.Run</c>.
/// Delete once the assets are committed.
/// </summary>
public static class StatelessEffectsMigration
{
    const string BulletPath = "Assets/Data/Projectiles/Bullet.asset";
    const string ScatterBurstPath = "Assets/Data/Abilities/ScatterBurst.asset";

    // Values previously authored on Bullet.asset (the ProjectileType SO).
    const float BulletRadius = 0.2f;
    const float BulletLifetime = 5.15f;
    const int BulletMaxHealth = 1;
    const int BulletDamage = 3;
    const float BulletSpeed = 60f;

    const int ScatterCount = 12;
    const float ScatterSpreadDegrees = 360f;

    [MenuItem("Tools/Migrations/Populate Stateless Effect Assets (stateless-commands refactor)")]
    public static void Run()
    {
        var bullet = AssetDatabase.LoadAssetAtPath<ProjectileType>(BulletPath);
        if (bullet == null || bullet.InstancePrefab == null)
        {
            Debug.LogWarning($"[StatelessEffectsMigration] Missing {BulletPath} or its InstancePrefab — aborting.");
            return;
        }

        // 1) Bullet prefab: bake the values that used to live on Bullet.asset into the prefab's components.
        string bulletPrefabPath = AssetDatabase.GetAssetPath(bullet.InstancePrefab);
        var contentsRoot = PrefabUtility.LoadPrefabContents(bulletPrefabPath);
        try
        {
            if (contentsRoot.TryGetComponent<NetworkedMotion>(out var motion))
            {
                motion.Motion = new LinearMotion(0f);
                motion.InitialSpeed = BulletSpeed;
            }
            if (contentsRoot.TryGetComponent<ServerCollision>(out var collision))
            {
                collision.Radius = BulletRadius;
                collision.Lifetime = BulletLifetime;
                collision.OnSurface = new List<IEffect<ProjectileSurfaceCollision>> { new LoseHealth() };
                // Die-on-player-hit is authored data: LoseHealth AFTER DealDamage, so the bullet spends its
                // 1 health on the hit and ServerCollision despawns it.
                collision.OnPlayer = new List<IEffect<ProjectilePlayerCollision>> { new DealDamage(), new LoseHealth() };
                collision.OnProjectile = new List<IEffect<ProjectileProjectileCollision>>();
            }
            if (contentsRoot.TryGetComponent<Health>(out var health))
            {
                var so = new SerializedObject(health);
                so.FindProperty("maxHealth").intValue = BulletMaxHealth;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            if (contentsRoot.TryGetComponent<Damage>(out var damage))
                damage.Amount = BulletDamage;

            PrefabUtility.SaveAsPrefabAsset(contentsRoot, bulletPrefabPath);
            Debug.Log($"[StatelessEffectsMigration] Baked Bullet.asset values into {bulletPrefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contentsRoot);
        }

        // 2) ScatterBurst: replace the old serialized SpawnProjectiles (deleted class -> missing managed
        //    reference) with a stateless SpawnNetworkObjects. Source is left null: the effect resolves the
        //    caster pawn's SpawnableObjects via GetComponent on the context Owner at use time.
        var scatter = AssetDatabase.LoadAssetAtPath<ActiveAbilityType>(ScatterBurstPath);
        if (scatter != null)
        {
            scatter.OnUse = new List<IEffect<AbilityAim>> { new SpawnNetworkObjects() };
            EditorUtility.SetDirty(scatter);
            Debug.Log($"[StatelessEffectsMigration] Migrated {ScatterBurstPath}");
        }
        else
        {
            Debug.LogWarning($"[StatelessEffectsMigration] Missing {ScatterBurstPath}");
        }

        // 3) Every pawn prefab carrying PawnAbilities gets the scatter fan config as a SpawnableObjects on the
        //    Pawn's own GameObject (AbilityAim.Owner is pawn.gameObject, so that is where GetComponent looks).
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null || asset.GetComponentInChildren<PawnAbilities>(includeInactive: true) == null)
                continue;

            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var pawn = contents.GetComponentInChildren<Pawn>(includeInactive: true);
                if (pawn == null)
                {
                    Debug.LogWarning($"[StatelessEffectsMigration] {path} has PawnAbilities but no Pawn — skipped.");
                    continue;
                }

                if (!pawn.TryGetComponent<SpawnableObjects>(out var spawnable))
                    spawnable = pawn.gameObject.AddComponent<SpawnableObjects>();

                var so = new SerializedObject(spawnable);
                var prefabs = so.FindProperty("prefabs");
                prefabs.arraySize = 1;
                prefabs.GetArrayElementAtIndex(0).objectReferenceValue = bullet.InstancePrefab;
                so.FindProperty("count").intValue = ScatterCount;
                so.FindProperty("spreadDegrees").floatValue = ScatterSpreadDegrees;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(contents, path);
                Debug.Log($"[StatelessEffectsMigration] Configured SpawnableObjects on {path}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[StatelessEffectsMigration] Complete.");
    }
}
#endif
