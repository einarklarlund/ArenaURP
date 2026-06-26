using System;
using System.Collections;
using System.Reflection;
using Arena.Projectile;
using FishNet.Object;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Arena.PlayTests
{
    /// <summary>
    /// Integration test for the server-authoritative ability inventory through a live FishNet host:
    /// granting a passive and an active, then USING the active and proving it spawns projectiles through
    /// the SAME <c>ProjectileWorldProvider</c> projectiles use (the unified WorldEffect path) and consumes
    /// a use. PawnAbilities lives in Assembly-CSharp (unreferenceable here), so typed access goes through
    /// AbilityTestRig by reflection; the projectile catalog/count goes through ProjectileSpawnTestRig.
    ///
    /// Slot-removal-at-zero and cycle wrap are covered exhaustively by EditMode AbilityInventoryLogic
    /// tests; this test pins the networked glue (SyncList grant + the effect→provider spawn) that EditMode
    /// can't reach.
    /// </summary>
    public sealed class AbilityInventoryPlayModeTests : NetworkedPlayModeTestFixture
    {
        const string ShooterPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestShooter.prefab";
        const string BulletPrefabPath = "Assets/Prefabs/Projectiles/Bullet Root.prefab";
        const string BulletTypePath = "Assets/Prefabs/Projectiles/Primary Bullet.asset";
        const string SwiftPath = "Assets/Data/Abilities/Swift.asset";
        const string BurstPath = "Assets/Data/Abilities/ScatterBurst.asset";
        const int ScatterCount = 12; // SpawnProjectilesEffect.Count on Scatter Burst

        protected override string ScenePath => "Assets/Tests/PlayMode/Scenes/TestProjectile.unity";

        [UnityTest]
        public IEnumerator Granting_and_using_an_active_spawns_projectiles_and_consumes_a_use()
        {
#if UNITY_EDITOR
            // Wire the provider catalog so Primary Bullet resolves to the networked bullet prefab.
            var provider = ProjRig("FindProvider");
            Assert.IsNotNull(provider, "TestProjectile scene must contain a ProjectileWorldProvider");
            var type = AssetDatabase.LoadAssetAtPath<ProjectileType>(BulletTypePath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BulletPrefabPath).GetComponent<NetworkObject>();
            Assert.IsNotNull(type, $"missing {BulletTypePath}");
            Assert.IsNotNull(prefab, $"missing NetworkObject on {BulletPrefabPath}");
            ProjRig("ConfigureCatalog", provider, type, prefab);

            var pawn = SpawnServerPrefab(ShooterPrefabPath, new Vector3(0f, 50f, 0f));
            for (int i = 0; i < 3; i++) yield return null; // let the spawn settle

            // Passive grant -> server SyncList.
            AbRig("AddPassive", pawn, SwiftPath);
            Assert.AreEqual(1, (int)AbRig("PassivesCount", pawn), "passive ability added to the synced list");

            // Active grant -> 1 of 5 slots, uses initialised from the asset (3).
            AbRig("AddActive", pawn, BurstPath);
            Assert.AreEqual(1, (int)AbRig("ActivesCount", pawn), "active slot filled");
            Assert.AreEqual(3, (int)AbRig("Uses", pawn, 0), "uses initialised from AbilityType.Uses");

            // Use the active. Count projectiles SYNCHRONOUSLY (before the next server tick, which would
            // resolve the freshly-overlapping ring against each other) to prove the spawn happened.
            Assert.AreEqual(0, (int)ProjRig("CountSpawnedProjectiles"), "precondition: no projectiles yet");
            AbRig("Use", pawn);
            int spawned = (int)ProjRig("CountSpawnedProjectiles");
            Assert.AreEqual(ScatterCount, spawned,
                "using Scatter Burst must spawn its projectiles through the shared ProjectileWorldProvider");
            Assert.AreEqual(2, (int)AbRig("Uses", pawn, 0), "one use consumed by the activation");

            yield return null;
#else
            Assert.Ignore("editor only");
            yield break;
#endif
        }

        static readonly Type ProjRigType = Type.GetType("ProjectileSpawnTestRig, Assembly-CSharp");
        static readonly Type AbRigType = Type.GetType("AbilityTestRig, Assembly-CSharp");

        static object ProjRig(string method, params object[] args) => Invoke(ProjRigType, method, args);
        static object AbRig(string method, params object[] args) => Invoke(AbRigType, method, args);

        static object Invoke(Type t, string method, object[] args)
        {
            Assert.IsNotNull(t, "rig type not found in Assembly-CSharp");
            var mi = t.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(mi, $"{t.Name}.{method} not found");
            return mi.Invoke(null, args);
        }
    }
}
