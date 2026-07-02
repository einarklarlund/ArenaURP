using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Integration test for the server-authoritative ability inventory through a live FishNet host:
    /// granting a passive and an active, then USING the active and proving it spawns projectiles through
    /// the shared <c>SpawnNetworkObjects</c> effect path (the SAME path projectile reactions use) and
    /// consumes a use. PawnAbilities lives in Assembly-CSharp (unreferenceable here), so typed access goes
    /// through AbilityTestRig by reflection; the spawned-projectile count goes through ProjectileSpawnTestRig.
    ///
    /// Slot-removal-at-zero and cycle wrap are covered by EditMode AbilityInventoryLogic tests; this test
    /// pins the networked glue (SyncList grant + the effect -> spawn) that EditMode can't reach, and runs
    /// the REAL authored Scatter Burst asset so it also catches asset/wiring regressions.
    /// </summary>
    public sealed class AbilityInventoryPlayModeTests : NetworkedPlayModeTestFixture
    {
        const string ShooterPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestShooter.prefab";
        const string SwiftPath = "Assets/Data/Abilities/Swift.asset";
        const string BurstPath = "Assets/Data/Abilities/ScatterBurst.asset";
        const int ScatterCount = 12; // the caster pawn's SpawnableObjects.Count (ScatterBurst's SpawnNetworkObjects reads it off the pawn)

        protected override string ScenePath => "Assets/Tests/PlayMode/Scenes/TestProjectile.unity";

        [UnityTest]
        public IEnumerator Granting_and_using_an_active_spawns_projectiles_and_consumes_a_use()
        {
            var pawn = SpawnServerPrefab(ShooterPrefabPath, new Vector3(0f, 50f, 0f));
            for (int i = 0; i < 3; i++) yield return null; // let the spawn settle

            // Passive grant -> a PassiveAbility instance spawned + parented under the pawn.
            AbRig("AddPassive", pawn, SwiftPath);
            Assert.AreEqual(1, (int)AbRig("PassivesCount", pawn), "passive ability instance under the pawn");

            // Active grant -> 1 slot (an ActiveAbility instance), uses initialised from the prefab (3).
            AbRig("AddActive", pawn, BurstPath);
            Assert.AreEqual(1, (int)AbRig("ActivesCount", pawn), "active slot filled");
            Assert.AreEqual(3, (int)AbRig("Uses", pawn, 0), "uses initialised from ActiveAbility.DefaultUses");

            // Use the active. Count projectiles SYNCHRONOUSLY (before the next server tick resolves the
            // freshly-overlapping ring against each other) to prove the spawn happened.
            Assert.AreEqual(0, (int)ProjRig("CountSpawnedProjectiles"), "precondition: no projectiles yet");
            AbRig("Use", pawn);
            int spawned = (int)ProjRig("CountSpawnedProjectiles");
            Assert.AreEqual(ScatterCount, spawned,
                "using Scatter Burst must spawn its projectiles through the shared SpawnNetworkObjects path");
            Assert.AreEqual(2, (int)AbRig("Uses", pawn, 0), "one use consumed by the activation");

            yield return null;
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
