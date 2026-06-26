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
    /// Integration test for the active-ability HUD wiring: the <c>AbilityBar</c> binds to the local pawn's
    /// server-authoritative <c>PawnAbilities</c> and must reflect the synced <c>Actives</c> list as it changes.
    /// Proves the seam EditMode can't reach — a SyncList mutation on a live host driving the real widget through
    /// the <c>NetworkUIEvents.OnLocalPawnSpawned</c> bind + the <c>Actives.OnChange</c>/<c>SelectedIndex.OnChange</c>
    /// subscriptions. The slot-removal/selection-clamp math itself is owned by EditMode AbilityInventoryLogic tests;
    /// here we only pin that the HUD reacts.
    ///
    /// AbilityBar/AbilitySlotWidget live in Assembly-CSharp (unreferenceable here), so the bar is built/queried
    /// through AbilityBarTestRig by reflection; the inventory is granted/used through AbilityTestRig.
    /// </summary>
    public sealed class AbilityBarHudTests : NetworkedPlayModeTestFixture
    {
        const string PawnPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestTargetPawn.prefab";
        const string ScatterBoxPath = "Assets/Prefabs/Pickups/Scatter Burst Box.prefab";
        const string BurstPath = "Assets/Data/Abilities/ScatterBurst.asset";
        const int BurstUses = 3; // ScatterBurst.asset Uses
        const float BurstCooldown = 0.5f; // ScatterBurst.asset Cooldown — uses are server-gated by it

        protected override string ScenePath => "Assets/Tests/PlayMode/Scenes/TestProjectile.unity";

        GameObject _bar;

        [UnityTest]
        public IEnumerator Picking_up_an_active_ability_fills_a_slot_in_the_bar()
        {
#if UNITY_EDITOR
            var pawn = SpawnServerPrefab(PawnPrefabPath, new Vector3(0f, 1f, 0f));
            for (int i = 0; i < 3; i++) yield return null;

            _bar = (GameObject)BarRig("BuildBar");
            BarRig("Bind", pawn);
            yield return null;

            // Precondition: bound to an empty inventory, every slot reads empty.
            Assert.AreEqual(PawnAbilitiesMaxActives, (int)BarRig("SlotCount", _bar), "bar builds one widget per active slot");
            Assert.IsFalse((bool)BarRig("SlotFilled", _bar, 0), "slot 0 starts empty before any pickup");

            // Drop the real active item box on top of the pawn; the server-tick overlap grants the active.
            var box = SpawnServerPrefab(ScatterBoxPath, new Vector3(0f, 1f, 0f));
            var boxNob = box.GetComponent<NetworkObject>();
            float t = 0f;
            while (boxNob != null && boxNob.IsSpawned && t < 2f) { t += Time.deltaTime; yield return null; }
            yield return null;

            Assert.AreEqual(1, (int)Ability("ActivesCount", pawn), "precondition: the pickup granted the active");
            Assert.IsTrue((bool)BarRig("SlotFilled", _bar, 0), "the granted active must appear in slot 0 of the HUD");
            Assert.AreEqual(BurstUses.ToString(), (string)BarRig("SlotUsesText", _bar, 0),
                "slot 0 must display the ability's starting uses");
            Assert.IsTrue((bool)BarRig("SlotSelected", _bar, 0), "the only active is the selected one and is highlighted");
            Assert.IsFalse((bool)BarRig("SlotFilled", _bar, 1), "the other slots stay empty");
#else
            Assert.Ignore("editor only");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Using_an_active_decrements_then_clears_the_slot_in_the_bar()
        {
#if UNITY_EDITOR
            var pawn = SpawnServerPrefab(PawnPrefabPath, new Vector3(0f, 1f, 0f));
            for (int i = 0; i < 3; i++) yield return null;

            // Grant via the same server path a pickup uses, then bind the HUD to the populated inventory.
            Ability("AddActive", pawn, BurstPath);
            _bar = (GameObject)BarRig("BuildBar");
            BarRig("Bind", pawn);
            yield return null;

            Assert.AreEqual(BurstUses.ToString(), (string)BarRig("SlotUsesText", _bar, 0), "HUD shows full uses after grant");

            // Each use consumes one charge; the displayed count must follow the synced state down. Uses are
            // server-gated by the ability's cooldown, so wait it out between activations (real sim-time, per
            // the PlayMode timing rule) rather than spamming within a single cooldown window.
            Ability("Use", pawn);
            yield return null;
            Assert.AreEqual((BurstUses - 1).ToString(), (string)BarRig("SlotUsesText", _bar, 0),
                "using the active must decrement the displayed uses");

            yield return new WaitForSeconds(BurstCooldown + 0.1f);
            Ability("Use", pawn);
            yield return null;
            Assert.AreEqual((BurstUses - 2).ToString(), (string)BarRig("SlotUsesText", _bar, 0),
                "the HUD keeps tracking each use");

            // Final use exhausts the slot -> it's removed from Actives and the HUD slot must go empty.
            yield return new WaitForSeconds(BurstCooldown + 0.1f);
            Ability("Use", pawn);
            yield return null;
            Assert.AreEqual(0, (int)Ability("ActivesCount", pawn), "precondition: the exhausted active was removed");
            Assert.IsFalse((bool)BarRig("SlotFilled", _bar, 0), "the removed active must disappear from the HUD");
            Assert.IsFalse((bool)BarRig("SlotSelected", _bar, 0), "an empty slot is not highlighted");
#else
            Assert.Ignore("editor only");
            yield break;
#endif
        }

        [UnityTearDown]
        public IEnumerator TearDownBar()
        {
            // Destroy the bar so its OnDisable unsubscribes from the static NetworkUIEvents (a leaked
            // subscription would refresh a destroyed bar in the next test). Runs before the fixture teardown.
            if (_bar != null) UnityEngine.Object.Destroy(_bar);
            _bar = null;
            yield return null;
        }

        // PawnAbilities.MaxActives lives in Assembly-CSharp; mirror the constant rather than reference it.
        const int PawnAbilitiesMaxActives = 5;

        static readonly Type BarRigType = Type.GetType("AbilityBarTestRig, Assembly-CSharp");
        static readonly Type AbilityRigType = Type.GetType("AbilityTestRig, Assembly-CSharp");

        static object BarRig(string m, params object[] a) => Invoke(BarRigType, m, a);
        static object Ability(string m, params object[] a) => Invoke(AbilityRigType, m, a);

        static object Invoke(Type t, string m, object[] a)
        {
            Assert.IsNotNull(t, "rig type not found in Assembly-CSharp");
            var mi = t.GetMethod(m, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(mi, $"{t.Name}.{m} not found");
            return mi.Invoke(null, a);
        }
    }
}
