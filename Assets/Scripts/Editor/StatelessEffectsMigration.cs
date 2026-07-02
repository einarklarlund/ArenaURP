#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot migration for the stateless-commands refactor. <c>SpawnProjectiles</c> and <c>SpawnVfx</c> were
/// deleted (replaced by the stateless <c>SpawnNetworkObjects</c> / <c>InstantiateGameObjects</c>, whose config
/// lives on <c>SpawnableObjects</c> / <c>InstantiableObjects</c> components), so ScatterBurst.asset's
/// serialized <c>SpawnProjectiles {count:12, spread:360}</c> is now a missing-type entry. This replaces it
/// with a <c>SpawnNetworkObjects</c> (resolved via GetComponent on the caster at use time) and puts the
/// equivalent <c>SpawnableObjects</c> fan config (Bullet prefab, 12, 360°) on every pawn prefab that carries
/// <c>PawnAbilities</c> — the ability context's Owner is the pawn, so that is where the effect looks.
///
/// Idempotent — safe to re-run. Run via the menu or <c>-executeMethod StatelessEffectsMigration.Run</c>.
/// Delete once the assets are committed.
/// </summary>
public static class StatelessEffectsMigration
{
    const string BulletPath = "Assets/Data/Projectiles/Bullet.asset";
    const string ScatterBurstPath = "Assets/Data/Abilities/ScatterBurst.asset";
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

        // 1) ScatterBurst: replace the old serialized SpawnProjectiles (deleted class -> missing managed
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

        // 2) Every pawn prefab carrying PawnAbilities gets the scatter fan config as a SpawnableObjects on the
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
