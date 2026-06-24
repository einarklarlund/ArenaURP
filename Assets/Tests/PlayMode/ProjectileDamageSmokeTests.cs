using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Arena.Projectile;
using FishNet.Object;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// The richest projectile seam EditMode cannot reach: a server-authoritative hit that resolves a
    /// real collider back to a NetworkPlayer and lands damage on the controlled Pawn. This exercises the
    /// whole runtime chain end to end —
    ///
    ///   PawnProjectile.ServerTick -> ProjectileWorldProvider.CastWorld (real SphereCast, collider ->
    ///   GetComponentInParent&lt;Pawn&gt; -> ControllingPlayer.ObjectId) -> ResolveWorldHit (enemy) ->
    ///   the OnEnemy DealDamage reaction's effect -> ProjectileWorldProvider.ApplyDamage
    ///   (NetworkPlayerManager lookup -> ControlledPawn as IDamageable) -> Pawn.TakeDamage -> Health
    ///   SyncVar drops -> despawn.
    ///
    /// EditMode already proves the damage EFFECT is emitted with the right attacker/target/amount; this
    /// test only adds the parts that need the live engine + FishNet: the physics cast, the player-id
    /// resolution on both ends, and that TakeDamage actually mutates the synced Health.
    ///
    /// The typed Pawn/NetworkPlayer/NetworkPlayerManager wiring lives in the editor-only
    /// ProjectileDamageTestRig (Assembly-CSharp), reached by reflection — the test asmdef can't
    /// reference Assembly-CSharp.
    /// </summary>
    public sealed class ProjectileDamageSmokeTests : NetworkedPlayModeTestFixture
    {
        const string BulletPrefabPath = "Assets/Prefabs/Projectiles/Bullet Root.prefab";
        const string NpmPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestNpm.prefab";
        const string PlayerPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestPlayer.prefab";
        const string PawnPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestTargetPawn.prefab";
        const int StartHealth = 21;
        const int BulletDamage = 5;

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
            // Let the network spawns + SyncList init settle.
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

            // Runtime type with an authored DealDamage in OnEnemy (deterministic amount, decoupled from
            // whatever the shipped asset is tuned to). An enemy hit deals BulletDamage and despawns.
            var type = ScriptableObject.CreateInstance<ProjectileType>();
            type.Movement = MovementKind.Linear;
            type.Speed = 60f;
            type.Lifetime = 5f;
            type.MaxHealth = 1;
            type.Radius = 0.2f;
            type.OnSurface = new List<IHitReaction>();
            type.OnEnemy = new List<IHitReaction> { new DealDamage { Amount = BulletDamage, DespawnAfter = true } };
            type.OnProjectile = new List<IHitReaction>();

            // Fire from the origin straight at the pawn (z = +5), using the real prefab + this test type.
            // Owner id differs from the target player so owner-safety doesn't ignore the hit.
            var spawnPos = new Vector3(0f, 1f, 0f);
            var bullet = SpawnProjectile(BulletPrefabPath, spawnPos, Vector3.forward,
                ownerId: targetPlayerId + 1, typeOverride: type);

            var nob = bullet.GetComponent<NetworkObject>();
            float elapsed = 0f;
            while (nob != null && nob.IsSpawned && elapsed < 2f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            // A frame for the Health SyncVar mutation to settle.
            yield return null;

            Assert.IsFalse(nob != null && nob.IsSpawned,
                "the bullet should have hit the pawn and despawned");
            int healthAfter = (int)RigInvoke("Health", pawn);
            Assert.AreEqual(BulletDamage, healthBefore - healthAfter,
                "the server hit should resolve the collider to the NetworkPlayer and drop the controlled " +
                $"pawn's Health by the bullet damage ({BulletDamage}); before={healthBefore} after={healthAfter}");

            UnityEngine.Object.DestroyImmediate(type);
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
