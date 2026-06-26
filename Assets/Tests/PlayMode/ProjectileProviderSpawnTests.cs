using System;
using System.Collections;
using System.Reflection;
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
    /// Integration test for the child-projectile spawn path ??? <c>ProjectileSpawner.Spawn</c>, the path the
    /// SpawnProjectiles reaction/ability drives. It pins that this spawner produces a COMPLETE payload:
    /// owner, position, forward, and crucially <c>SpawnTick</c>. SpawnTick is the basis for the child's
    /// deterministic age; if a spawner forgets it (the bug this guards), the child computes its age from
    /// tick 0 and despawns the instant it ticks.
    ///
    /// Runs through the live FishNet host so the real <c>ProjectileType.Create</c> + ServerManager.Spawn +
    /// OnStartNetwork path executes ??? EditMode cannot reach it. Typed access goes through
    /// ProjectileSpawnTestRig by reflection (it lives in Assembly-CSharp, unreferenceable from this asmdef).
    /// </summary>
    public sealed class ProjectileProviderSpawnTests : NetworkedPlayModeTestFixture
    {
        const string BulletTypePath = "Assets/Data/Projectiles/Bullet.asset";

        protected override string ScenePath => "Assets/Tests/PlayMode/Scenes/TestProjectile.unity";

        [UnityTest]
        public IEnumerator Child_spawn_sets_full_payload_including_current_tick()
        {
#if UNITY_EDITOR
            var type = AssetDatabase.LoadMainAssetAtPath(BulletTypePath);
            Assert.IsNotNull(type, $"missing {BulletTypePath}");

            var pos = new Vector3(1f, 2f, 3f);
            var dir = Vector3.forward;
            const int ownerId = 777;

            uint tickBefore = (uint)RigInvoke("CurrentTick");
            int id = (int)RigInvoke("SpawnViaType", type, pos, dir, ownerId);
            Assert.Greater(id, 0, "SpawnViaType should spawn the child and return its ObjectId");

            yield return null; // let the network spawn register
            var child = (GameObject)RigInvoke("FindSpawned", id);
            Assert.IsNotNull(child, "the spawned child should be resolvable by its ObjectId");

            // Payload completeness ??? the SetupSpawn funnel must have set every field.
            Assert.AreEqual(ownerId, (int)RigInvoke("SpawnOwnerId", child), "child owner id");
            AssertVecClose(pos, (Vector3)RigInvoke("SpawnPosition", child), "child spawn position");
            AssertVecClose(dir, (Vector3)RigInvoke("SpawnForward", child), "child spawn forward");

            // The regression: SpawnTick must be ~the current tick, never default(0).
            uint spawnTick = (uint)RigInvoke("SpawnTickValue", child);
            Assert.GreaterOrEqual(spawnTick, tickBefore,
                "SpawnTick must be the spawn-time tick; a default/0 tick is the bug this guards");
            Assert.Less(spawnTick, tickBefore + 5, "SpawnTick should be ~the current tick");

            // Behavioral proof the tick is sane: the child lives and travels rather than instantly dying.
            var nob = child.GetComponent<NetworkObject>();
            yield return new WaitForSeconds(0.2f);
            Assert.IsTrue(nob != null && nob.IsSpawned,
                "child should still be alive after a moment; a wrong SpawnTick would despawn it immediately");
            Assert.Greater(Vector3.Distance(child.transform.position, pos), 0.5f,
                "child should travel along its forward (deterministic age advancing from a correct SpawnTick)");
#else
            Assert.Ignore("editor only");
            yield break;
#endif
        }

        static void AssertVecClose(Vector3 expected, Vector3 actual, string what)
            => Assert.Less(Vector3.Distance(expected, actual), 0.01f, $"{what}: expected {expected}, got {actual}");

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
