using System;
using System.Collections;
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
    /// Integration tests for EVERY <see cref="WorldEffect"/> kind, driven through the single production
    /// executor <c>ProjectileWorldProvider.Execute</c> on a live host. This is the shared seam both
    /// projectiles and abilities funnel through, so covering each kind here covers the "-> World" half of
    /// every effect chain once. Typed provider/pawn access goes through the Assembly-CSharp rigs by
    /// reflection (the test asmdef can't reference them).
    /// </summary>
    public sealed class WorldEffectExecuteTests : NetworkedPlayModeTestFixture
    {
        const string NpmPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestNpm.prefab";
        const string PlayerPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestPlayer.prefab";
        const string PawnPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestTargetPawn.prefab";
        const string BulletPrefabPath = "Assets/Prefabs/Projectiles/Bullet Root.prefab";
        const string BulletTypePath = "Assets/Prefabs/Projectiles/Primary Bullet.asset";
        const string VfxMarkerPath = "Assets/Tests/PlayMode/Prefabs/TestVfxMarker.prefab";

        protected override string ScenePath => "Assets/Tests/PlayMode/Scenes/TestProjectileDamage.unity";

        [UnityTest]
        public IEnumerator Damage_effect_drops_the_target_pawns_health()
        {
#if UNITY_EDITOR
            var npm = SpawnServerPrefab(NpmPrefabPath, Vector3.zero).GetComponent("NetworkPlayerManager");
            var playerGo = SpawnServerPrefab(PlayerPrefabPath, Vector3.zero);
            var pawnGo = SpawnServerPrefab(PawnPrefabPath, new Vector3(0, 1, 5));
            for (int i = 0; i < 3; i++) yield return null;

            var player = playerGo.GetComponent("NetworkPlayer");
            var pawn = pawnGo.GetComponent("Pawn");
            Dmg("Wire", npm, player, pawn, 21);
            int targetId = (int)Dmg("PlayerObjectId", player);
            int before = (int)Dmg("Health", pawn);

            var effect = WorldEffect.Damage(targetId, 5, targetId + 1, Vector3.zero, Vector3.forward);
            Spawn("Execute", Provider(), effect);
            yield return null;

            Assert.AreEqual(5, before - (int)Dmg("Health", pawn), "Damage effect should reduce the controlled pawn's Health");
#else
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Spawn_effect_creates_projectiles_in_the_world()
        {
#if UNITY_EDITOR
            for (int i = 0; i < 2; i++) yield return null;
            var provider = Provider();
            var type = AssetDatabase.LoadAssetAtPath<ProjectileType>(BulletTypePath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BulletPrefabPath).GetComponent<NetworkObject>();
            Spawn("ConfigureCatalog", provider, type, prefab);

            int before = (int)Spawn("CountSpawnedProjectiles");
            var effect = WorldEffect.Spawn(type, 4, new Vector3(0, 50, 0), Vector3.forward, ownerId: 1);
            Spawn("Execute", provider, effect);
            // Count synchronously — the next server tick would resolve the freshly-overlapping spawns.
            Assert.AreEqual(before + 4, (int)Spawn("CountSpawnedProjectiles"), "Spawn effect should create N projectiles");
#else
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Debuff_effect_executes_as_a_safe_noop()
        {
#if UNITY_EDITOR
            for (int i = 0; i < 2; i++) yield return null;
            var provider = Provider();
            // No debuff system exists yet (ProjectileWorldProvider.ApplyDebuff is a documented no-op).
            // The contract this pins: executing a Debuff effect runs cleanly and changes nothing — so when
            // a debuff system lands, this test is the place that starts asserting its world change.
            Spawn("Execute", provider, WorldEffect.Debuff_(123, DebuffType.Slow));
            yield return null;
            Assert.Pass("Debuff effect executed without error (no-op until a debuff system exists)");
#else
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Vfx_effect_instantiates_the_impact_prefab()
        {
#if UNITY_EDITOR
            for (int i = 0; i < 2; i++) yield return null;
            var provider = Provider();
            var type = AssetDatabase.LoadAssetAtPath<ProjectileType>(BulletTypePath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BulletPrefabPath).GetComponent<NetworkObject>();
            var vfx = AssetDatabase.LoadAssetAtPath<GameObject>(VfxMarkerPath);
            Spawn("ConfigureCatalogVfx", provider, type, prefab, vfx);

            int before = (int)Spawn("CountClones", "TestVfxMarker");
            Spawn("Execute", provider, WorldEffect.Vfx(type, new Vector3(0, 1, 0), Vector3.up));
            Assert.AreEqual(before + 1, (int)Spawn("CountClones", "TestVfxMarker"), "Vfx effect should instantiate the impact prefab");
#else
            yield break;
#endif
        }

        // ---- rig access ----
        object Provider() => Spawn("FindProvider");

        static readonly Type SpawnRigType = Type.GetType("ProjectileSpawnTestRig, Assembly-CSharp");
        static readonly Type DmgRigType = Type.GetType("ProjectileDamageTestRig, Assembly-CSharp");
        static object Spawn(string m, params object[] a) => Invoke(SpawnRigType, m, a);
        static object Dmg(string m, params object[] a) => Invoke(DmgRigType, m, a);

        static object Invoke(Type t, string m, object[] a)
        {
            Assert.IsNotNull(t, "rig type not found in Assembly-CSharp");
            var mi = t.GetMethod(m, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(mi, $"{t.Name}.{m} not found");
            return mi.Invoke(null, a);
        }
    }
}
