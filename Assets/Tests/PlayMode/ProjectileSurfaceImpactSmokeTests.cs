using System.Collections;
using FishNet.Object;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// PlayMode smoke for the world-surface channel: the real Bullet Root prefab, fired at a real wall
    /// collider, must be stopped by an actual Physics.SphereCast and despawn — driven entirely by the
    /// prefab's own wired ProjectileType (no synthetic type). This proves the surface path end to end:
    ///
    ///   PawnProjectile.ServerTick -> ProjectileWorldProvider.CastWorld (real SphereCast vs the wall) ->
    ///   ResolveWorldHit (surface) -> the OnSurface LoseHealth reaction kills the projectile -> ServerDespawn.
    ///
    /// This only passes because the type authors a kill-on-surface reaction in OnSurface; a bullet whose
    /// OnSurface is empty would snap to the wall and continue through it. That's the contract this pins.
    /// </summary>
    public sealed class ProjectileSurfaceImpactSmokeTests : NetworkedPlayModeTestFixture
    {
        const string BulletTypePath = "Assets/Data/Projectiles/Bullet.asset";

        protected override string ScenePath
            => "Assets/Tests/PlayMode/Scenes/TestProjectileWall.unity";

        [UnityTest]
        public IEnumerator Bullet_fired_at_wall_is_stopped_by_real_cast_and_despawned()
        {
            // Fire from the origin straight at the wall (front face z ~4.5 in TestProjectileWall.unity).
            var spawnPos = new Vector3(0f, 1f, 0f);
            var bullet = SpawnProjectile(BulletTypePath, spawnPos, Vector3.forward);
            var nob = bullet.GetComponent<NetworkObject>();
            Assert.IsNotNull(nob, "spawned bullet has no NetworkObject");

            // Tick until it despawns (or time out). Track how far it got while alive so we can prove the
            // cast stopped it at the wall rather than letting it tunnel through.
            float elapsed = 0f;
            float maxZ = spawnPos.z;
            while (nob != null && nob.IsSpawned && elapsed < 2f)
            {
                if (bullet != null)
                    maxZ = Mathf.Max(maxZ, bullet.transform.position.z);
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.IsFalse(nob != null && nob.IsSpawned,
                "the bullet should have hit the wall and despawned within the timeout; if it never " +
                "despawned, either the cast didn't fire or the type lost its OnSurface kill reaction");
            Assert.Greater(maxZ, 2f, "the bullet should have travelled toward the wall before stopping");
            Assert.Less(maxZ, 6f, "the bullet should have stopped at the wall (front face z ~4.5), not tunnelled past it");
        }
    }
}
