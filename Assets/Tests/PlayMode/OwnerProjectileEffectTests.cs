using System;
using System.Collections;
using FishNet.Object;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Integration test for the owner-lookup projectile-effect path: a bullet finds its OWNER on the
    /// server and applies the owner's projectile passive (here, bonus damage on enemy hit), proving the
    /// chain Projectile -> owner reactions -> WorldEffect -> target, with NO extra spawn payload. The
    /// owner's effect is read server-side from the live owner, not shipped with the projectile.
    ///
    /// The bullet is the REAL authored Bullet type (own damage read from the asset via the rig); the owner
    /// bonus is a runtime passive granted through AbilityTestRig. The Assembly-CSharp types are
    /// unreferenceable from this asmdef, so all typed access goes through the rigs by reflection.
    /// </summary>
    public sealed class OwnerProjectileEffectTests : NetworkedPlayModeTestFixture
    {
        const string NpmPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestNpm.prefab";
        const string PlayerPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestPlayer.prefab";
        const string PawnPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestTargetPawn.prefab";
        const string BulletTypePath = "Assets/Data/Projectiles/Bullet.asset";
        const int StartHealth = 21;
        const int OwnerBonus = 4;

        protected override string ScenePath => "Assets/Tests/PlayMode/Scenes/TestProjectileDamage.unity";

        [UnityTest]
        public IEnumerator Owners_projectile_passive_adds_bonus_damage_on_hit_without_payload()
        {
            var npmGo = SpawnServerPrefab(NpmPrefabPath, Vector3.zero);

            // Target pawn sits in the bullet's path (z = +5).
            var targetPlayerGo = SpawnServerPrefab(PlayerPrefabPath, Vector3.zero);
            var targetPawnGo = SpawnServerPrefab(PawnPrefabPath, new Vector3(0, 1, 5));

            // Owner: a separate player whose pawn carries the projectile passive. Parked off the path.
            var ownerPlayerGo = SpawnServerPrefab(PlayerPrefabPath, new Vector3(20, 0, 0));
            var ownerPawnGo = SpawnServerPrefab(PawnPrefabPath, new Vector3(20, 1, 0));
            for (int i = 0; i < 3; i++) yield return null;

            var npm = npmGo.GetComponent("NetworkPlayerManager");
            Dmg("Wire", npm, targetPlayerGo.GetComponent("NetworkPlayer"), targetPawnGo.GetComponent("Pawn"), StartHealth);
            Dmg("Wire", npm, ownerPlayerGo.GetComponent("NetworkPlayer"), ownerPawnGo.GetComponent("Pawn"), StartHealth);
            int ownerId = (int)Dmg("PlayerObjectId", ownerPlayerGo.GetComponent("NetworkPlayer"));

            // The owner equips "your bullets deal +OwnerBonus on enemy hit" ??? found server-side at hit time.
            Ability("GrantProjectilePassive", ownerPawnGo, OwnerBonus);
            for (int i = 0; i < 2; i++) yield return null;

            int before = (int)Dmg("Health", targetPawnGo.GetComponent("Pawn"));
            int typeDamage = (int)Dmg("TypeDamageAt", BulletTypePath);

            // A bullet OWNED BY the owner; the real Bullet type deals its own damage and despawns on hit.
            var bullet = SpawnProjectile(BulletTypePath, new Vector3(0, 1, 0), Vector3.forward, ownerId: ownerId);
            var nob = bullet.GetComponent<NetworkObject>();
            float t = 0f;
            while (nob != null && nob.IsSpawned && t < 2f) { t += Time.deltaTime; yield return null; }
            yield return null;

            int after = (int)Dmg("Health", targetPawnGo.GetComponent("Pawn"));
            Assert.AreEqual(typeDamage + OwnerBonus, before - after,
                "the bullet's own damage PLUS the owner's looked-up projectile passive should both land");
        }

        static readonly Type DmgRigType = Type.GetType("ProjectileDamageTestRig, Assembly-CSharp");
        static readonly Type AbilityRigType = Type.GetType("AbilityTestRig, Assembly-CSharp");
        static object Dmg(string m, params object[] a) => Invoke(DmgRigType, m, a);
        static object Ability(string m, params object[] a) => Invoke(AbilityRigType, m, a);

        static object Invoke(Type t, string m, object[] a)
        {
            Assert.IsNotNull(t, "rig type not found in Assembly-CSharp");
            var mi = t.GetMethod(m, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(mi, $"{t.Name}.{m} not found");
            return mi.Invoke(null, a);
        }
    }
}
