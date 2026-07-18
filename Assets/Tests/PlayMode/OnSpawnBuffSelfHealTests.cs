using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Two-peer test for the §5 speed self-heal: a client that never applied an on-spawn speed buff locally is
    /// corrected in the SAME spawn iteration by the server-seeded initial segment SyncVars — no persistent
    /// divergence, no visible pop. The divergence is CONSTRUCTED directly instead of raced: the server spawns a
    /// bullet with SpawnOwnerId = -1 (so the client's SpawnContributionReceiver no-ops) but with a per-spawn speed
    /// multiplier set on the server instance before Spawn (standing in for a server-side-only buff resolution).
    /// FishNet network-ownership is granted to the client purely so the object replicates — that is independent of
    /// SpawnOwnerId; nothing in the buff path reads NetworkObject.Owner.
    /// The client must still converge to the buffed OriginSpeed because the seg SyncVars ship the server's seeded
    /// (buffed) value and their cached OnChange flushes into NetworkedMotion after OnStartNetwork.
    /// </summary>
    public sealed class OnSpawnBuffSelfHealTests : TwoPeerPlayModeTestFixture
    {
        const string BulletPrefabPath = "Assets/Prefabs/Projectiles/Types/Bullet.prefab";
        const float Multiplier = 1.5f;
        const float Tol = 1e-2f;

        [UnityTest]
        public IEnumerator Client_that_missed_the_buff_self_heals_speed_from_seeded_syncvars()
        {
            SetLatency(50); // make the transit window real, so the flush genuinely lands after Initialize

            float baseline = (float)Dmg("TypeInitialSpeedAt", "Assets/Data/Projectiles/Bullet.asset");
            int objectId = (int)Dmg("ServerSpawnBulletWithSpeedMultiplier", ServerNM, ClientOnServer, BulletPrefabPath, Vector3.up, Multiplier);

            // Wait for the client to replicate the spawn.
            GameObject clientBullet = null;
            float t = 0f;
            while (clientBullet == null && t < 5f)
            {
                clientBullet = FindSpawnedOn(ClientNM, objectId);
                t += Time.deltaTime;
                yield return null;
            }
            Assert.IsNotNull(clientBullet, "client never replicated the server-spawned bullet");

            // A couple more frames for the cached initial seg-SyncVar OnChange to flush into the client's motion.
            for (int i = 0; i < 3; i++) yield return null;

            float clientSpeed = (float)Dmg("OriginSpeed", clientBullet);
            Assert.AreEqual(baseline * Multiplier, clientSpeed, Tol,
                "the client, which never applied the buff locally, must self-heal to the server's buffed OriginSpeed");
        }

        static readonly Type DmgRigType = Type.GetType("ProjectileDamageTestRig, Assembly-CSharp");
        static object Dmg(string m, params object[] a)
        {
            Assert.IsNotNull(DmgRigType, "ProjectileDamageTestRig not found in Assembly-CSharp");
            var mi = DmgRigType.GetMethod(m, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(mi, $"ProjectileDamageTestRig.{m} not found");
            return mi.Invoke(null, a);
        }
    }
}
