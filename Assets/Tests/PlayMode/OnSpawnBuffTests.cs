using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Integration tests for the on-spawn passive-buff path: a spawned projectile snapshots the firer's passives
    /// as resolved on this peer at the spawn instant (SpawnBuffReceiver → PawnPassiveAbilities.GetSpawnBuffs →
    /// each OnSpawn effect), with NOTHING extra on the wire. Exercises both buff categories — speed (Option A
    /// idempotent motion setter) and scale (the no-sync-channel SpawnScale path) — plus the unbuffed no-op.
    ///
    /// Mirrors <see cref="OwnerProjectileEffectTests"/>: the bullet is the REAL authored Bullet type, the buff is a
    /// runtime passive granted through AbilityTestRig, and Assembly-CSharp types are reached only through the rigs
    /// by reflection.
    /// </summary>
    public sealed class OnSpawnBuffTests : NetworkedPlayModeTestFixture
    {
        const string NpmPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestNpm.prefab";
        const string PlayerPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestPlayer.prefab";
        const string PawnPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestTargetPawn.prefab";
        const string BulletTypePath = "Assets/Data/Projectiles/Bullet.asset";
        const int StartHealth = 21;
        const float Tol = 1e-3f;

        protected override string ScenePath => "Assets/Tests/PlayMode/Scenes/TestProjectileDamage.unity";

        [UnityTest]
        public IEnumerator On_spawn_speed_buff_scales_the_projectiles_initial_speed()
        {
            int ownerId = 0;
            yield return RegisterOwner(id => ownerId = id, grantMethod: "GrantSpeedBuffPassive");

            float baseline = (float)Dmg("TypeInitialSpeedAt", BulletTypePath);
            var bullet = SpawnProjectile(BulletTypePath, new Vector3(0, 1, 0), Vector3.forward, ownerId: ownerId);
            yield return null;

            Assert.AreEqual(baseline * 2f, (float)Dmg("OriginSpeed", bullet), Tol,
                "the owner's on-spawn speed passive (×2) should scale the just-spawned bullet's segment speed");
        }

        [UnityTest]
        public IEnumerator On_spawn_scale_buff_scales_the_projectiles_localScale()
        {
            int ownerId = 0;
            yield return RegisterOwner(id => ownerId = id, grantMethod: "GrantScaleBuffPassive");

            float baseline = (float)Dmg("TypeLocalScaleXAt", BulletTypePath);
            var bullet = SpawnProjectile(BulletTypePath, new Vector3(0, 1, 0), Vector3.forward, ownerId: ownerId);
            yield return null;

            Assert.AreEqual(baseline * 2f, (float)Dmg("LocalScaleX", bullet), Tol,
                "the owner's on-spawn scale passive (×2) should scale the just-spawned bullet's localScale");
        }

        [UnityTest]
        public IEnumerator An_unbuffed_owner_spawns_a_baseline_projectile()
        {
            int ownerId = 0;
            yield return RegisterOwner(id => ownerId = id, grantMethod: null); // no passive granted

            float baseline = (float)Dmg("TypeInitialSpeedAt", BulletTypePath);
            var bullet = SpawnProjectile(BulletTypePath, new Vector3(0, 1, 0), Vector3.forward, ownerId: ownerId);
            yield return null;

            Assert.AreEqual(baseline, (float)Dmg("OriginSpeed", bullet), Tol,
                "with no on-spawn passive, the receiver is a quiet no-op and the bullet keeps its baseline speed");
        }

        /// <summary>
        /// Spawn + register an owner pawn (parked off the bullet's path), optionally grant it a passive by rig
        /// method name, and hand the owner's ObjectId back through <paramref name="setOwnerId"/>.
        /// </summary>
        IEnumerator RegisterOwner(Action<int> setOwnerId, string grantMethod)
        {
            var npmGo = SpawnServerPrefab(NpmPrefabPath, Vector3.zero);
            var ownerPlayerGo = SpawnServerPrefab(PlayerPrefabPath, new Vector3(20, 0, 0));
            var ownerPawnGo = SpawnServerPrefab(PawnPrefabPath, new Vector3(20, 1, 0));
            for (int i = 0; i < 3; i++) yield return null;

            Dmg("Wire", npmGo.GetComponent("NetworkPlayerManager"),
                ownerPlayerGo.GetComponent("NetworkPlayer"), ownerPawnGo.GetComponent("Pawn"), StartHealth);
            setOwnerId((int)Dmg("PlayerObjectId", ownerPlayerGo.GetComponent("NetworkPlayer")));

            if (grantMethod != null)
                Ability(grantMethod, ownerPawnGo);
            for (int i = 0; i < 2; i++) yield return null; // let the passive instance replicate/enumerate
        }

        static readonly Type DmgRigType = Type.GetType("ProjectileDamageTestRig, Assembly-CSharp");
        static readonly Type AbilityRigType = Type.GetType("AbilityTestRig, Assembly-CSharp");
        static object Dmg(string m, params object[] a) => Invoke(DmgRigType, m, a);
        static object Ability(string m, params object[] a) => Invoke(AbilityRigType, m, a);

        static object Invoke(Type t, string m, object[] a)
        {
            Assert.IsNotNull(t, "rig type not found in Assembly-CSharp");
            var mi = t.GetMethod(m, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(mi, $"{t?.Name}.{m} not found");
            return mi.Invoke(null, a);
        }
    }
}
