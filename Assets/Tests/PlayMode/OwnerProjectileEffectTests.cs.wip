using System;
using System.Collections;
using System.Collections.Generic;
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
    /// Integration test for the owner-lookup projectile-effect path: a bullet finds its OWNER on the
    /// server and applies the owner's projectile passive (here, bonus damage on enemy hit) — proving the
    /// full chain Projectile -> owner reactions -> WorldEffect -> World, with NO extra spawn payload. The
    /// owner's effect is read server-side from the live owner, not shipped with the projectile.
    /// </summary>
    public sealed class OwnerProjectileEffectTests : NetworkedPlayModeTestFixture
    {
        const string NpmPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestNpm.prefab";
        const string PlayerPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestPlayer.prefab";
        const string PawnPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestTargetPawn.prefab";
        const string BulletPrefabPath = "Assets/Prefabs/Projectiles/Bullet Root.prefab";
        const int StartHealth = 21;
        const int TypeDamage = 5;
        const int OwnerBonus = 4;

        protected override string ScenePath => "Assets/Tests/PlayMode/Scenes/TestProjectileDamage.unity";

        [UnityTest]
        public IEnumerator Owners_projectile_passive_adds_bonus_damage_on_hit_without_payload()
        {
#if UNITY_EDITOR
            var npm = SpawnServerPrefab(NpmPrefabPath, Vector3.zero).GetComponent("NetworkPlayerManager");

            // Target pawn sits in the bullet's path.
            var targetPlayerGo = SpawnServerPrefab(PlayerPrefabPath, Vector3.zero);
            var targetPawnGo = SpawnServerPrefab(PawnPrefabPath, new Vector3(0, 1, 5));

            // Owner: a separate player whose pawn carries the projectile passive. Parked off the path.
            var ownerPlayerGo = SpawnServerPrefab(PlayerPrefabPath, new Vector3(20, 0, 0));
            var ownerPawnGo = SpawnServerPrefab(PawnPrefabPath, new Vector3(20, 1, 0));
            for (int i = 0; i < 3; i++) yield return null;

            Dmg("Wire", npm, targetPlayerGo.GetComponent("NetworkPlayer"), targetPawnGo.GetComponent("Pawn"), StartHealth);
            Dmg("Wire", npm, ownerPlayerGo.GetComponent("NetworkPlayer"), ownerPawnGo.GetComponent("Pawn"), StartHealth);
            int ownerId = (int)Dmg("PlayerObjectId", ownerPlayerGo.GetComponent("NetworkPlayer"));

            // The owner equips "your bullets deal +OwnerBonus on enemy hit" — found server-side at hit time.
            Ability("GrantProjectilePassive", ownerPawnGo, OwnerBonus);
            for (int i = 0; i < 2; i++) yield return null;

            int before = (int)Dmg("Health", targetPawnGo.GetComponent("Pawn"));

            // A bullet OWNED BY the owner; its type deals TypeDamage and despawns on hit.
            var type = ScriptableObject.CreateInstance<ProjectileType>();
            type.Movement = MovementKind.Linear; type.Speed = 60f; type.Lifetime = 5f; type.MaxHealth = 1; type.Radius = 0.2f;
            type.OnSurface = new List<IHitReaction>();
            type.OnEnemy = new List<IHitReaction> { new DealDamage { Amount = TypeDamage, DespawnAfter = true } };
            type.OnProjectile = new List<IHitReaction>();

            var bullet = SpawnProjectile(BulletPrefabPath, new Vector3(0, 1, 0), Vector3.forward, ownerId: ownerId, typeOverride: type);
            var nob = bullet.GetComponent<NetworkObject>();
            float t = 0f;
            while (nob != null && nob.IsSpawned && t < 2f) { t += Time.deltaTime; yield return null; }
            yield return null;

            int after = (int)Dmg("Health", targetPawnGo.GetComponent("Pawn"));
            Assert.AreEqual(TypeDamage + OwnerBonus, before - after,
                "the bullet's own damage PLUS the owner's looked-up projectile passive should both land");

            UnityEngine.Object.DestroyImmediate(type);
#else
            yield break;
#endif
        }

        static readonly Type DmgRigType = Type.GetType("ProjectileDamageTestRig, Assembly-CSharp");
        static readonly Type AbilityRigType = Type.GetType("AbilityTestRig, Assembly-CSharp");
        static object Dmg(string m, params object[] a) => Invoke(DmgRigType, m, a);
        static object Ability(string m, params object[] a) => Invoke(AbilityRigType, m, a);

        static object Invoke(Type t, string m, object[] a)
        {
            Assert.IsNotNull(t, "rig type not found in Assembly-CSharp");
            var mi = t.GetMethod(m, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(mi, $"{t.Name}.{m} not found");
            return mi.Invoke(null, a);
        }
    }
}
