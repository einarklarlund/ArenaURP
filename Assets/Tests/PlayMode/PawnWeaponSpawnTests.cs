using System;
using System.Collections;
using System.Reflection;
using FishNet.Object;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Integration test for the FIRST projectile spawner: <c>PawnWeapon.ExecuteShot</c>. Drives a real
    /// shooter (Pawn + PawnWeapon + FakePawnInput) through the live FishNet host, fires once, and asserts
    /// the weapon spawned a <c>PawnProjectile</c> (the new path ??? not the legacy bullet) with a COMPLETE
    /// payload: owner = the firing player's ObjectId, position = the fire point, direction = the fire
    /// forward, and a current <c>SpawnTick</c>. Pairs with ProjectileProviderSpawnTests (the child-spawn
    /// spawner) so both code paths that create a PawnProjectile are covered end to end.
    ///
    /// PlayMode because PawnWeapon/PawnProjectile live in Assembly-CSharp and need a live host;
    /// typed access goes through ProjectileSpawnTestRig by reflection.
    /// </summary>
    public sealed class PawnWeaponSpawnTests : NetworkedPlayModeTestFixture
    {
        const string ShooterPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestShooter.prefab";
        const string PlayerPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestPlayer.prefab";

        protected override string ScenePath => "Assets/Tests/PlayMode/Scenes/TestProjectile.unity";

        [UnityTest]
        public IEnumerator PawnWeapon_fires_a_PawnProjectile_with_full_payload()
        {
            var playerGo = SpawnServerPrefab(PlayerPrefabPath, Vector3.zero);
            var shooterGo = SpawnServerPrefab(ShooterPrefabPath, Vector3.zero);
            for (int i = 0; i < 3; i++) yield return null; // let spawns settle

            RigInvoke("WireOwner", shooterGo, playerGo);
            int ownerId = (int)RigInvoke("PlayerObjectId", playerGo);

            var firePoint = shooterGo.transform.Find("FirePoint");
            Assert.IsNotNull(firePoint, "TestShooter prefab must have a FirePoint child");
            Vector3 firePos = firePoint.position;
            Vector3 fireDir = firePoint.forward;

            Assert.AreEqual(0, (int)RigInvoke("CountSpawnedProjectiles"), "precondition: nothing fired yet");

            // Fire exactly one shot: hold the button briefly (FireRate 0.1s >> a few editor frames, so
            // only the first shot fires), then release.
            uint tickBefore = (uint)RigInvoke("CurrentTick");
            RigInvoke("SetFire", shooterGo, true);
            for (int i = 0; i < 3; i++) yield return null;
            RigInvoke("SetFire", shooterGo, false);
            yield return null;

            Assert.AreEqual(1, (int)RigInvoke("CountSpawnedProjectiles"),
                "PawnWeapon should have fired exactly one shot");
            var bullet = (GameObject)RigInvoke("FindSpawnedProjectile");
            Assert.IsNotNull(bullet, "the weapon must spawn a PawnProjectile (the new path), not the legacy bullet");

            // Payload completeness ??? the SetupSpawn funnel, fed from the weapon's firing context.
            Assert.AreEqual(ownerId, (int)RigInvoke("SpawnOwnerId", bullet),
                "bullet owner must be the firing player's ObjectId");
            AssertVecClose(firePos, (Vector3)RigInvoke("SpawnPosition", bullet), "bullet spawns at the fire point");
            AssertVecClose(fireDir, (Vector3)RigInvoke("SpawnForward", bullet), "bullet travels along the fire direction");

            uint spawnTick = (uint)RigInvoke("SpawnTickValue", bullet);
            Assert.GreaterOrEqual(spawnTick, tickBefore, "SpawnTick must be the spawn-time tick, not default(0)");
            Assert.Less(spawnTick, tickBefore + 10, "SpawnTick should be ~the current tick");

            // Behavioral proof the payload is sane: the bullet lives and travels.
            var nob = bullet.GetComponent<NetworkObject>();
            yield return new WaitForSeconds(0.2f);
            Assert.IsTrue(nob != null && nob.IsSpawned, "the bullet should still be alive");
            Assert.Greater(Vector3.Distance(bullet.transform.position, firePos), 0.5f,
                "the bullet should travel along its forward");
        }

        static void AssertVecClose(Vector3 expected, Vector3 actual, string what)
            => Assert.Less(Vector3.Distance(expected, actual), 0.05f, $"{what}: expected {expected}, got {actual}");

        static readonly Type RigType = Type.GetType("ProjectileSpawnTestRig, Assembly-CSharp");

        static object RigInvoke(string method, params object[] args)
        {
            Assert.IsNotNull(RigType, "ProjectileSpawnTestRig not found in Assembly-CSharp");
            var mi = RigType.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(mi, $"ProjectileSpawnTestRig.{method} not found");
            return mi.Invoke(null, args);
        }
    }
}
