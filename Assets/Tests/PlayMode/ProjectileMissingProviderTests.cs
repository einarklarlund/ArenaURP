using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Guards the silent footgun that made "projectiles don't interact" invisible: when the active
    /// scene has NO ProjectileWorldProvider, PawnProjectile.ServerTick early-returns every tick, so the
    /// bullet renders and flies but never casts — it passes through every surface, player, and
    /// projectile, with no error to explain why.
    ///
    /// The shell now warns once when this happens. This test spawns the real Bullet Root into a scene
    /// with no provider (TestSmoke) and asserts the warning fires — so a mis-set-up gameplay scene is
    /// loud instead of mysterious. (The collision behaviour itself is covered, WITH a provider, by the
    /// projectile surface/damage smoke tests.)
    /// </summary>
    public sealed class ProjectileMissingProviderTests : NetworkedPlayModeTestFixture
    {
        const string BulletPrefabPath = "Assets/Prefabs/Projectiles/Bullet Root.prefab";

        protected override string ScenePath
            => "Assets/Tests/PlayMode/Scenes/TestSmoke.unity"; // no ProjectileWorldProvider

        [UnityTest]
        public IEnumerator Spawning_a_projectile_with_no_provider_warns_loudly()
        {
            ResetWarnedFlag(); // the warning is one-shot per session; reset so it reliably fires here
            LogAssert.Expect(LogType.Warning, new Regex("No ProjectileWorldProvider"));

            SpawnProjectile(BulletPrefabPath, new Vector3(0f, 1f, 0f), Vector3.forward);

            // Let OnStartServer run (where the missing-provider check lives).
            yield return null;
            yield return null;

            // LogAssert.Expect fails the test in teardown if the warning never arrived.
        }

        static void ResetWarnedFlag()
        {
            var t = Type.GetType("PawnProjectile, Assembly-CSharp");
            Assert.IsNotNull(t, "PawnProjectile not found in Assembly-CSharp");
            var f = t.GetField("_warnedNoProvider", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(f, "PawnProjectile._warnedNoProvider not found (renamed?)");
            f.SetValue(null, false);
        }
    }
}
