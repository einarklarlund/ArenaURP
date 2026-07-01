#if UNITY_EDITOR
using System.Collections.Generic;
using Arena.NetworkedMotion;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot migration for the command/context refactor. The old <c>ProjectileType</c> motion enum + reaction
/// feature flags and the ability spawn-flags were removed, so those serialized values were dropped when the
/// classes changed. This repopulates the new <c>[SerializeReference]</c> motion + operation lists on the
/// existing type assets so behaviour matches the pre-refactor code-composed defaults exactly.
///
/// Idempotent — safe to re-run. Run via the menu or <c>-executeMethod OperationRefactorMigration.Run</c>.
/// Delete once the assets are committed.
/// </summary>
public static class OperationRefactorMigration
{
    const string BulletPath = "Assets/Data/Projectiles/Bullet.asset";
    const string ScatterBurstPath = "Assets/Data/Abilities/ScatterBurst.asset";

    [MenuItem("Tools/Migrations/Populate Operation Assets (command-context refactor)")]
    public static void Run()
    {
        var bullet = AssetDatabase.LoadAssetAtPath<ProjectileType>(BulletPath);
        if (bullet != null)
        {
            // Was: Movement=Linear, Acceleration=0; SurfaceHealthLoss=1; RicochetsByDefault=off;
            //      enemy default = DealDamage(useOwnerDamage, despawn).
            bullet.Motion = new LinearMotion(0f);
            bullet.OnSurface = new List<IEffect<ProjectileSurfaceCollision>> { new LoseHealth(1) };
            bullet.OnPlayer = new List<IEffect<ProjectilePlayerCollision>>
            {
                new DealDamage(useOwnerDamage: true, amount: 10),
            };
            bullet.OnProjectile = new List<IEffect<ProjectileProjectileCollision>>();
            EditorUtility.SetDirty(bullet);
            Debug.Log($"[OperationRefactorMigration] Migrated {BulletPath}");
        }
        else
        {
            Debug.LogWarning($"[OperationRefactorMigration] Missing {BulletPath}");
        }

        var scatter = AssetDatabase.LoadAssetAtPath<ActiveAbilityType>(ScatterBurstPath);
        if (scatter != null)
        {
            // Was: SpawnProjectile=Bullet, SpawnCount=12, SpawnSpread=360 (an even ring).
            scatter.OnUse = new List<IEffect<AbilityAim>>
            {
                new SpawnProjectiles(bullet, count: 12, spreadDegrees: 360f),
            };
            EditorUtility.SetDirty(scatter);
            Debug.Log($"[OperationRefactorMigration] Migrated {ScatterBurstPath}");
        }
        else
        {
            Debug.LogWarning($"[OperationRefactorMigration] Missing {ScatterBurstPath}");
        }

        // Swift (PassiveAbilityType) needs no operation authoring — its walk multipliers keep their field
        // names (preserved on import) and both projectile-contribution flags were off.

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[OperationRefactorMigration] Complete.");
    }
}
#endif
