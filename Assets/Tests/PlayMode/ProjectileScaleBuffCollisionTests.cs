using System;
using System.Collections;
using FishNet.Object;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Behavioural coverage for the on-spawn SCALE buff's effect on COLLISION: an owner's BuffScale passive must
    /// enlarge the projectile's server-side collision radius enough to actually land a hit the baseline radius
    /// misses — not merely raise the <c>EffectiveRadius</c> number (that state check lives in
    /// <see cref="OnSpawnBuffTests"/>). This is the only test that proves the ServerCollision sweep consumes the
    /// buffed radius, so a refactor that read the raw <c>Radius</c> instead would be caught here and nowhere else.
    ///
    /// The discriminator is pure geometry (no timing): the target pawn's BoxCollider spans x in [-1, 1]; a shot
    /// fired straight down +z along x = 1.3 clears the box by 0.1 at the baseline radius 0.2 (nearest approach
    /// 1.1) but overlaps it by 0.1 at the ×2 buffed radius 0.4 (nearest approach 0.9). Same shot, buff toggled —
    /// the buffed one damages the target, the baseline one whiffs. Owner and target are DISTINCT players so the
    /// hit classifies as an enemy hit, not owner-safety.
    /// </summary>
    public sealed class ProjectileScaleBuffCollisionTests : NetworkedPlayModeTestFixture
    {
        const string NpmPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestNpm.prefab";
        const string PlayerPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestPlayer.prefab";
        const string PawnPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestTargetPawn.prefab";
        const string BulletTypePath = "Assets/Data/Projectiles/Bullet.asset";
        const int StartHealth = 21;

        // Fire along x = 1.3: outside the box (half-width 1.0) + baseline radius 0.2, inside it + ×2 radius 0.4.
        const float GrazeOffsetX = 1.3f;

        protected override string ScenePath => "Assets/Tests/PlayMode/Scenes/TestProjectileDamage.unity";

        [UnityTest]
        public IEnumerator Baseline_radius_whiffs_the_grazing_target()
            => FireGrazingShot(withScaleBuff: false, expectHit: false);

        [UnityTest]
        public IEnumerator On_spawn_scale_buff_enlarges_the_radius_enough_to_land_the_graze()
            => FireGrazingShot(withScaleBuff: true, expectHit: true);

        IEnumerator FireGrazingShot(bool withScaleBuff, bool expectHit)
        {
            int ownerId = 0;
            GameObject targetPawnGo = null;
            yield return SetupOwnerAndTarget(withScaleBuff, id => ownerId = id, go => targetPawnGo = go);

            var targetPawn = targetPawnGo.GetComponent("Pawn");
            int healthBefore = (int)Dmg("Health", targetPawn);
            Assert.AreEqual(StartHealth, healthBefore, "precondition: the target starts at full health");

            // The owning player differs from the target, so a hit classifies as an enemy hit (not owner-safety).
            var bullet = SpawnProjectile(BulletTypePath, new Vector3(GrazeOffsetX, 1f, 0f), Vector3.forward, ownerId);
            var nob = bullet.GetComponent<NetworkObject>();
            Assert.IsNotNull(nob, "spawned bullet has no NetworkObject");

            float elapsed = 0f;
            while (nob != null && nob.IsSpawned && elapsed < 2f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            yield return null; // let the Health SyncVar settle

            int healthAfter = (int)Dmg("Health", targetPawn);
            if (expectHit)
                Assert.Less(healthAfter, healthBefore,
                    "the ×2 scale buff should enlarge the collision radius enough for the sweep to graze the target " +
                    $"the baseline radius misses (before={healthBefore} after={healthAfter}) — if it didn't drop, the " +
                    "ServerCollision sweep is not consuming the buffed radius");
            else
                Assert.AreEqual(healthBefore, healthAfter,
                    "the baseline (un-buffed) radius should clear the offset target and deal no damage — the control " +
                    "that makes the buffed hit meaningful");
        }

        /// <summary>
        /// Spawn an owner (parked off the shot line, optionally carrying the scale-buff passive) and a DISTINCT
        /// target pawn centred on the shot line at z = +5, both registered on one NetworkPlayerManager.
        /// </summary>
        IEnumerator SetupOwnerAndTarget(bool grantScaleBuff, Action<int> setOwnerId, Action<GameObject> setTargetPawn)
        {
            var npmGo = SpawnServerPrefab(NpmPrefabPath, Vector3.zero);
            var ownerPlayerGo = SpawnServerPrefab(PlayerPrefabPath, new Vector3(20f, 0f, 0f));
            var ownerPawnGo = SpawnServerPrefab(PawnPrefabPath, new Vector3(20f, 1f, 0f));
            var targetPlayerGo = SpawnServerPrefab(PlayerPrefabPath, new Vector3(0f, 0f, 10f));
            var targetPawnGo = SpawnServerPrefab(PawnPrefabPath, new Vector3(0f, 1f, 5f));
            for (int i = 0; i < 3; i++) yield return null;

            var npm = npmGo.GetComponent("NetworkPlayerManager");
            Dmg("Wire", npm, ownerPlayerGo.GetComponent("NetworkPlayer"), ownerPawnGo.GetComponent("Pawn"), StartHealth);
            Dmg("Wire", npm, targetPlayerGo.GetComponent("NetworkPlayer"), targetPawnGo.GetComponent("Pawn"), StartHealth);
            setOwnerId((int)Dmg("PlayerObjectId", ownerPlayerGo.GetComponent("NetworkPlayer")));
            setTargetPawn(targetPawnGo);

            if (grantScaleBuff)
                Ability("GrantScaleBuffPassive", ownerPawnGo);
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
