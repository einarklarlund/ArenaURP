using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// PlayMode smoke for the projectile seam EditMode can't reach: the real Bullet Root prefab,
    /// server-spawned, with FishNet ticking the imperative shell. Unlike a Core movement unit test (which
    /// calls PositionAt on a struct), this proves the live glue — that the spawn PAYLOAD the shell reads
    /// (SpawnPosition / SpawnForward / SpawnTick) actually drives the NetworkObject's world transform via
    /// PawnProjectile.ServerTick + Render, using the prefab's own wired ProjectileType (no synthetic type,
    /// no overridden _type).
    ///
    /// Parameterized over several spawn positions and directions: for each, the bullet must appear AT its
    /// spawn position and travel ALONG its spawn forward. The scene (TestProjectile.unity) has no
    /// colliders and a zero worldMask, so the bullet flies freely regardless of direction.
    /// </summary>
    public sealed class ProjectileServerTickSmokeTests : NetworkedPlayModeTestFixture
    {
        const string BulletPrefabPath = "Assets/Prefabs/Projectiles/Bullet Root.prefab";

        protected override string ScenePath
            => "Assets/Tests/PlayMode/Scenes/TestProjectile.unity";

        /// <summary>One spawn (position, direction) case. ToString drives the test-case name in the runner.</summary>
        public readonly struct Flight
        {
            public readonly string Name;
            public readonly Vector3 Pos;
            public readonly Vector3 Dir;
            public Flight(string name, Vector3 pos, Vector3 dir) { Name = name; Pos = pos; Dir = dir.normalized; }
            public override string ToString() => Name;
        }

        static IEnumerable<Flight> FlightCases()
        {
            yield return new Flight("forward_from_origin", new Vector3(0f, 1f, 0f), Vector3.forward);
            yield return new Flight("right_from_origin", new Vector3(0f, 1f, 0f), Vector3.right);
            yield return new Flight("up_from_offset", new Vector3(3f, 2f, -4f), Vector3.up);
            yield return new Flight("back_from_offset", new Vector3(-5f, 1f, 8f), Vector3.back);
            yield return new Flight("diagonal_from_offset", new Vector3(2f, 6f, -3f), new Vector3(1f, 1f, 1f));
        }

        [UnityTest]
        public IEnumerator Bullet_spawns_at_position_and_moves_along_forward(
            [ValueSource(nameof(FlightCases))] Flight f)
        {
            var bullet = SpawnProjectile(BulletPrefabPath, f.Pos, f.Dir);

            // Captured on the spawn frame (before any tick advances it): it must appear at its origin.
            Vector3 startPos = bullet.transform.position;
            Assert.Less(Vector3.Distance(startPos, f.Pos), 0.5f,
                $"bullet should spawn at {f.Pos} but was at {startPos}");

            // Let FishNet tick the shell. WaitForSeconds (not frame-stepping): the motion is driven by
            // simulated tick time, which barely advances per editor frame in PlayMode tests.
            yield return new WaitForSeconds(0.2f);

            Vector3 disp = bullet.transform.position - f.Pos;
            Assert.Greater(disp.magnitude, 1f,
                $"bullet should have travelled a meaningful distance from {f.Pos}; moved {disp}");
            float alongness = Vector3.Dot(disp.normalized, f.Dir);
            Assert.Greater(alongness, 0.999f,
                $"bullet should move along its spawn forward {f.Dir}; displacement {disp} (dot {alongness:F4})");
        }
    }
}
