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
    /// Integration test for the server-authoritative, cast-based pickup: a pawn overlapping an
    /// <c>AbilityPickup</c> is granted the ability and the pickup despawns — all decided by the pickup's
    /// SERVER tick (a physics overlap), never by client-side trigger callbacks. Covers the networked
    /// behaviour changed in this work, per the "always playmode-test networked changes" rule.
    /// </summary>
    public sealed class AbilityPickupServerAuthTests : NetworkedPlayModeTestFixture
    {
        const string SwiftPickupPath = "Assets/Prefabs/Pickups/Swift Pickup.prefab";
        const string PawnPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestTargetPawn.prefab";
        const string PlayerPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestPlayer.prefab";
        const string RegistryPrefabPath = "Assets/Prefabs/Abilities/PassiveRegistry.prefab"; // carries the "Swift" KIND

        protected override string ScenePath => "Assets/Tests/PlayMode/Scenes/TestProjectileDamage.unity";

        [UnityTest]
        public IEnumerator Pickup_grants_ability_to_overlapping_pawn_and_despawns()
        {
#if UNITY_EDITOR
            var pawnGo = SpawnServerPrefab(PawnPrefabPath, new Vector3(0, 1, 0));
            // A passive pickup grants into the pawn OWNER's PlayerPassiveLoadout (ownership on the player), resolving
            // the KIND by name against the spawned registry — so the pawn needs an owner and the "Swift" KIND must be
            // spawned before the pickup's server tick fires.
            var playerGo = SpawnServerPrefab(PlayerPrefabPath, new Vector3(30, 0, 0));
            SpawnServerPrefab(RegistryPrefabPath, Vector3.zero);
            for (int i = 0; i < 3; i++) yield return null;
            Ability("WireOwner", pawnGo, playerGo);
            for (int i = 0; i < 2; i++) yield return null;

            var pickupGo = SpawnServerPrefab(SwiftPickupPath, new Vector3(0, 1, 0));
            var pickupNob = pickupGo.GetComponent<NetworkObject>();
            // No "starts at 0 passives" precondition: the pickup's server tick can grant within the very
            // first frames (the pawn overlaps from spawn), which would race the assert. We assert only the
            // outcome — the grant landed and the pickup despawned.
            for (int i = 0; i < 3; i++) yield return null;

            // Wait for the pickup's server tick to overlap the pawn and grant.
            float t = 0f;
            while (pickupNob != null && pickupNob.IsSpawned && t < 2f) { t += Time.deltaTime; yield return null; }
            yield return null;

            Assert.IsFalse(pickupNob != null && pickupNob.IsSpawned, "the pickup should despawn after granting");
            Assert.AreEqual(1, (int)Ability("PassivesCount", pawnGo),
                "the overlapping pawn should receive the passive via the server-side overlap (no trigger callback)");
#else
            yield break;
#endif
        }

        static readonly Type AbilityRigType = Type.GetType("AbilityTestRig, Assembly-CSharp");
        static object Ability(string m, params object[] a)
        {
            Assert.IsNotNull(AbilityRigType, "AbilityTestRig not found in Assembly-CSharp");
            var mi = AbilityRigType.GetMethod(m, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(mi, $"AbilityTestRig.{m} not found");
            return mi.Invoke(null, a);
        }
    }
}
