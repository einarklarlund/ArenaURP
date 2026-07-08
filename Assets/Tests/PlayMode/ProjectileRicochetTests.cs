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
    /// PlayMode coverage for the RICOCHET reaction end to end: a real Bullet fired at the real wall in
    /// TestProjectileWall.unity, whose OnSurface Ricochet must reflect its heading through the live
    /// ServerCollision -> Ricochet -> NetworkedMotion.BeginSegment chain (which EditMode's RicochetMath
    /// tests cannot exercise — they only cover the pure reflection).
    ///
    /// The second test pins the regression the powerups hit: when TWO Ricochets resolve on ONE impact (one
    /// authored on the projectile, one contributed by the owner's bouncy passive), reflecting the projectile's
    /// LIVE velocity twice across the same normal composes to the identity — the shot cancels its own bounce and
    /// drives straight through the wall. Reflecting the hit's fixed INCOMING direction (as the fixed Ricochet
    /// does) is idempotent, so stacking sources still bounces. Nothing tested this interaction before.
    /// </summary>
    public sealed class ProjectileRicochetTests : NetworkedPlayModeTestFixture
    {
        const string BulletTypePath = "Assets/Data/Projectiles/Bullet.asset";

        protected override string ScenePath
            => "Assets/Tests/PlayMode/Scenes/TestProjectileWall.unity";

        [UnityTest]
        public IEnumerator Single_ricochet_reverses_the_heading_off_the_wall()
            => FireAtWallAndExpectBounce(ricochetCount: 1);

        [UnityTest]
        public IEnumerator Two_stacked_ricochets_on_one_hit_still_bounce_and_do_not_cancel()
            => FireAtWallAndExpectBounce(ricochetCount: 2);

        [UnityTest]
        public IEnumerator Ricochet_resumes_a_radius_off_the_wall_not_embedded_in_it()
        {
            // Wall front face is at z = 4.5 (centre 5, scale z 1) in TestProjectileWall.unity.
            const float WallFrontZ = 4.5f;

            var bullet = SpawnProjectile(BulletTypePath, new Vector3(0f, 1f, 0f), Vector3.forward);
            RigInvoke("AuthorRicochetOnSurface", bullet, 1);
            float radius = (float)RigInvoke("CollisionRadius", bullet);

            var nob = bullet.GetComponent<NetworkObject>();
            bool bounced = false;
            float elapsed = 0f;
            while (nob != null && nob.IsSpawned && elapsed < 2f)
            {
                if ((float)RigInvoke("MotionForwardZ", bullet) < -0.1f)
                {
                    bounced = true;
                    break;
                }
                elapsed += Time.deltaTime;
                yield return null;
            }
            Assert.IsTrue(bounced, "precondition: the bullet must ricochet off the wall for this assertion to mean anything");

            // The resumed segment origin is the mover's CENTRE, which sits a collision radius off the struck face —
            // not the surface impact point (which would leave the centre embedded, standoff 0, and re-colliding).
            float standoff = WallFrontZ - (float)RigInvoke("MotionOriginZ", bullet);
            Assert.AreEqual(radius, standoff, 0.05f,
                $"the ricochet should resume a collision radius off the wall face (standoff={standoff}, radius={radius}); " +
                "a naive resume at the surface impact point would sit the projectile centre on the wall (standoff ~0)");
        }

        /// <summary>
        /// Fire a bullet straight at the +z wall with <paramref name="ricochetCount"/> Ricochet ops authored on its
        /// OnSurface, and assert its motion heading reverses to -z (it bounced) before it either despawns or the
        /// window elapses. With the live-velocity bug, count==2 never reverses (the double reflection cancels) and
        /// the bullet tunnels on through — so this fails without the fix.
        /// </summary>
        IEnumerator FireAtWallAndExpectBounce(int ricochetCount)
        {
            var spawnPos = new Vector3(0f, 1f, 0f);
            var bullet = SpawnProjectile(BulletTypePath, spawnPos, Vector3.forward);
            // Same frame as the spawn, before the first server tick can classify the wall hit.
            RigInvoke("AuthorRicochetOnSurface", bullet, ricochetCount);

            var nob = bullet.GetComponent<NetworkObject>();
            Assert.IsNotNull(nob, "spawned bullet has no NetworkObject");
            Assert.Greater((float)RigInvoke("MotionForwardZ", bullet), 0f,
                "precondition: the bullet starts heading toward the +z wall");

            bool bounced = false;
            float maxZ = spawnPos.z;
            float elapsed = 0f;
            while (nob != null && nob.IsSpawned && elapsed < 2f)
            {
                if (bullet != null)
                {
                    maxZ = Mathf.Max(maxZ, bullet.transform.position.z);
                    if ((float)RigInvoke("MotionForwardZ", bullet) < -0.1f)
                    {
                        bounced = true;
                        break;
                    }
                }
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.Greater(maxZ, 2f,
                "the bullet should have travelled toward the wall (front face z ~4.5) before any bounce");
            Assert.IsTrue(bounced,
                $"a {ricochetCount}-Ricochet OnSurface hit should reflect the bullet's heading back to -z; it never " +
                "reversed — with two stacked Ricochets that means the reflections cancelled and it drove through the wall");
        }

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
