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
    /// The richest projectile seam EditMode cannot reach: a server-authoritative hit that resolves a real
    /// collider back to a NetworkPlayer and lands damage on the controlled Pawn. End-to-end runtime chain:
    ///
    ///   ServerCollision.ServerTick -> SphereCast (collider -> GetComponentInParent&lt;Pawn&gt; ->
    ///   ControllingPlayer.ObjectId) -> classify as enemy -> the damage EXCHANGE fires from component presence
    ///   (the bullet's Damage ∧ the pawn's IDamageable) -> Health SyncVar drops -> despawn.
    ///
    /// Fires the REAL authored `Bullet` ProjectileType (whose OnEnemy reaction deals its Damage), spawned
    /// via the production path (`ProjectileType.Create`), so this also catches asset/wiring regressions.
    /// The typed Pawn/NetworkPlayer wiring lives in the editor-only ProjectileDamageTestRig (Assembly-CSharp),
    /// reached by reflection.
    /// </summary>
    public sealed class ProjectileDamageSmokeTests : NetworkedPlayModeTestFixture
    {
        const string BulletTypePath = "Assets/Data/Projectiles/Bullet.asset";
        const string NpmPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestNpm.prefab";
        const string PlayerPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestPlayer.prefab";
        const string PawnPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestTargetPawn.prefab";
        const int StartHealth = 21;

        protected override string ScenePath
            => "Assets/Tests/PlayMode/Scenes/TestProjectileDamage.unity";

        [UnityTest]
        public IEnumerator Bullet_that_hits_a_player_damages_the_controlled_pawn()
        {
            // Server-spawn the player stack as runtime prefab instances (registered NetworkObjects).
            // The target pawn sits at z = +5 on the Player layer so the bullet's cast resolves it.
            var npmGo = SpawnServerPrefab(NpmPrefabPath, Vector3.zero);
            var playerGo = SpawnServerPrefab(PlayerPrefabPath, Vector3.zero);
            var pawnGo = SpawnServerPrefab(PawnPrefabPath, new Vector3(0f, 1f, 5f));
            for (int i = 0; i < 3; i++) yield return null;

            var npm = npmGo.GetComponent("NetworkPlayerManager");
            var player = playerGo.GetComponent("NetworkPlayer");
            var pawn = pawnGo.GetComponent("Pawn");
            Assert.IsNotNull(npm, "TestNpm has no NetworkPlayerManager");
            Assert.IsNotNull(player, "TestPlayer has no NetworkPlayer");
            Assert.IsNotNull(pawn, "TestTargetPawn has no Pawn");

            // Wire pawn <-> player and register the player (server-side, typed, via the rig).
            RigInvoke("Wire", npm, player, pawn, StartHealth);
            int targetPlayerId = (int)RigInvoke("PlayerObjectId", player);
            int healthBefore = (int)RigInvoke("Health", pawn);
            Assert.AreEqual(StartHealth, healthBefore, "precondition: target pawn starts at full health");

            // Fire the real Bullet type straight at the pawn (z = +5). Owner id differs from the target so
            // owner-safety doesn't ignore the hit.
            var spawnPos = new Vector3(0f, 1f, 0f);
            var bullet = SpawnProjectile(BulletTypePath, spawnPos, Vector3.forward, ownerId: targetPlayerId + 1);

            var nob = bullet.GetComponent<NetworkObject>();
            float elapsed = 0f;
            while (nob != null && nob.IsSpawned && elapsed < 2f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            yield return null; // let the Health SyncVar mutation settle

            Assert.IsFalse(nob != null && nob.IsSpawned, "the bullet should have hit the pawn and despawned");
            int healthAfter = (int)RigInvoke("Health", pawn);
            Assert.Less(healthAfter, healthBefore,
                "the server hit should resolve the collider to the NetworkPlayer and drop the controlled " +
                $"pawn's Health (before={healthBefore} after={healthAfter})");
        }

        /// <summary>
        /// Pins the ATOMIC-HIT rule: a classified hit resolves its exchanges AND all authored reactions as one
        /// unit even if the projectile dies mid-hit. The bullet's OnPlayer list is rewritten to [LoseHealth]
        /// with 1 HP, so the bullet dies inside the hit; the damage exchange (component presence, no authored
        /// row) must still land on the pawn — dying cannot retract it.
        /// </summary>
        [UnityTest]
        public IEnumerator Hit_resolves_atomically_regardless_of_effect_order()
        {
            var npmGo = SpawnServerPrefab(NpmPrefabPath, Vector3.zero);
            var playerGo = SpawnServerPrefab(PlayerPrefabPath, Vector3.zero);
            var pawnGo = SpawnServerPrefab(PawnPrefabPath, new Vector3(0f, 1f, 5f));
            for (int i = 0; i < 3; i++) yield return null;

            var npm = npmGo.GetComponent("NetworkPlayerManager");
            var player = playerGo.GetComponent("NetworkPlayer");
            var pawn = pawnGo.GetComponent("Pawn");
            RigInvoke("Wire", npm, player, pawn, StartHealth);
            int targetPlayerId = (int)RigInvoke("PlayerObjectId", player);
            int healthBefore = (int)RigInvoke("Health", pawn);

            var bullet = SpawnProjectile(BulletTypePath, new Vector3(0f, 1f, 0f), Vector3.forward,
                ownerId: targetPlayerId + 1);
            // Same frame as the spawn, before any server tick can classify a hit.
            RigInvoke("AuthorLethalFirstOnPlayer", bullet);

            var nob = bullet.GetComponent<NetworkObject>();
            float elapsed = 0f;
            while (nob != null && nob.IsSpawned && elapsed < 2f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            yield return null;

            Assert.IsFalse(nob != null && nob.IsSpawned,
                "the 1-HP bullet should have died on the hit and despawned");
            int healthAfter = (int)RigInvoke("Health", pawn);
            Assert.Less(healthAfter, healthBefore,
                "the bullet died to its own LoseHealth inside the hit and the damage exchange must still land — " +
                $"a hit resolves atomically (before={healthBefore} after={healthAfter})");
        }

        // ---- rig access (Assembly-CSharp types are unreferenceable, so reflect) ----

        static readonly Type RigType = Type.GetType("ProjectileDamageTestRig, Assembly-CSharp");

        static object RigInvoke(string method, params object[] args)
        {
            Assert.IsNotNull(RigType, "ProjectileDamageTestRig not found in Assembly-CSharp");
            var mi = RigType.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(mi, $"ProjectileDamageTestRig.{method} not found");
            return mi.Invoke(null, args);
        }
    }
}
