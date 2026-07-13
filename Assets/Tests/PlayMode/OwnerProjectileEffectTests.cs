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
    /// server and runs the owner's contributed reaction on enemy hit (here, instantiating a marker object
    /// at the hit point), proving the chain Projectile -> owner reactions -> effect -> world, with NO extra
    /// spawn payload. The owner's effect is read server-side from the live owner, not shipped with the
    /// projectile.
    ///
    /// The exact-1x damage assertion is the flip side of the same pin: under presence-dispatch (ADR 0002)
    /// the damage exchange fires once from component presence, so an owner contribution can no longer stack
    /// it (the old bonus-damage trick) — outgoing damage buffs are the deferred damage-buff channel's job.
    ///
    /// The bullet is the REAL authored Bullet type (own damage read from the asset via the rig); the owner
    /// contribution is a runtime passive granted through AbilityTestRig. The Assembly-CSharp types are
    /// unreferenceable from this asmdef, so all typed access goes through the rigs by reflection.
    /// </summary>
    public sealed class OwnerProjectileEffectTests : NetworkedPlayModeTestFixture
    {
        const string NpmPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestNpm.prefab";
        const string PlayerPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestPlayer.prefab";
        const string PawnPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestTargetPawn.prefab";
        const string BulletTypePath = "Assets/Data/Projectiles/Bullet.asset";
        const int StartHealth = 21;

        protected override string ScenePath => "Assets/Tests/PlayMode/Scenes/TestProjectileDamage.unity";

        [UnityTest]
        public IEnumerator Owners_projectile_passive_runs_on_hit_without_payload()
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

            // The owner equips "your bullets leave a marker at the enemy hit point" — found server-side at hit
            // time. The marker instantiation is inert (a plain non-networked GameObject), so the pin has no
            // gameplay side effects to confound the damage assertion.
            Ability("GrantProjectilePassive", ownerPawnGo);
            for (int i = 0; i < 2; i++) yield return null;

            Assert.IsNull(GameObject.Find("TestHitMarker(Clone)"),
                "precondition: no marker exists before the hit");

            int before = (int)Dmg("Health", targetPawnGo.GetComponent("Pawn"));
            int typeDamage = (int)Dmg("TypeDamageAt", BulletTypePath);

            // A bullet OWNED BY the owner; the real Bullet type deals its own damage and despawns on hit.
            var bullet = SpawnProjectile(BulletTypePath, new Vector3(0, 1, 0), Vector3.forward, ownerId: ownerId);
            var nob = bullet.GetComponent<NetworkObject>();
            float t = 0f;
            while (nob != null && nob.IsSpawned && t < 2f) { t += Time.deltaTime; yield return null; }
            yield return null;

            int after = (int)Dmg("Health", targetPawnGo.GetComponent("Pawn"));
            Assert.IsNotNull(GameObject.Find("TestHitMarker(Clone)"),
                "the owner's looked-up projectile passive must run on the hit — its contributed effect " +
                "instantiates a marker at the hit point");
            Assert.AreEqual(typeDamage, before - after,
                "the damage exchange fires exactly ONCE from component presence — an owner contribution can " +
                "no longer stack it (outgoing damage buffs are the deferred damage-buff channel)");
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
