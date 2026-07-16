using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// W3 regression: the countdown / postgame views consume the NetworkUIEvents bus; they do NOT
    /// reach through MatchFlowManager.Instance.CountdownTime or DeathmatchManager.leadPlayer.
    ///
    /// Each test drives the same three-beat proof, so it cannot pass vacuously:
    ///   1. PRECONDITION - the manager really stores value A (asserted, not assumed). Without this
    ///      a broken harness would leave storage empty and the whole contrast would be fake.
    ///   2. PROOF        - with storage holding A, a bus event carrying B makes the view render B.
    ///      Rendering B while storage says A is the decoupling: a singleton reader would show A.
    ///   3. CONTROL      - a bus event carrying A makes the view render A. This is what stops the
    ///      "view must not show A when enabled" assert from being vacuously true: it proves the
    ///      view is wired and CAN render A, so A's absence at enable is a real fact about the
    ///      enable path rather than an inability to ever display it.
    ///
    /// Runs on the single-host fixture against the manager-only test scene; all Assembly-CSharp
    /// access is via MatchTestRig by reflection.
    /// </summary>
    public sealed class UiBusDecouplingTests : NetworkedPlayModeTestFixture
    {
        protected override string ScenePath
            => "Assets/Tests/PlayMode/Scenes/TestMatchMediator.unity";

        GameObject _view;

        [UnityTest]
        public IEnumerator Countdown_view_reads_the_bus_not_the_MatchFlowManager_singleton()
        {
            yield return new WaitForSeconds(0.2f); // let OnStartServer wire the managers
            Assert.IsTrue((bool)Rig("ManagersPresent"), "TestMatchMediator scene should load the managers");

            // 1. PRECONDITION: storage really holds 7 (publishes to nobody; no view exists yet).
            Rig("SetCountdownTime", 7);
            Assert.AreEqual(7, (int)Rig("CountdownTime"),
                "precondition: MatchFlowManager must actually store 7, otherwise 'the view does not " +
                "show 7' proves nothing.");

            _view = (GameObject)Rig("CreatePostgameCountdownView");
            Rig("SetViewActive", _view, true); // OnEnable: subscribe only, must NOT read the singleton
            yield return null;

            Assert.AreNotEqual("7", (string)Rig("ViewCountdownText", _view),
                "enabling the view must NOT copy MatchFlowManager.CountdownTime.Value into the text " +
                "(the old singleton reach-in). Meaningful because the CONTROL below proves this same " +
                "view can render \"7\" when the BUS says 7.");

            // 2. PROOF: storage says 7, the bus says 3 -> the view must show 3.
            Rig("FireCountdownEvent", 3);
            yield return null;
            Assert.AreEqual("3", (string)Rig("ViewCountdownText", _view),
                "the countdown text must track NetworkUIEvents.OnCountdownChanged. Showing 3 while " +
                "storage still holds 7 is the decoupling; a singleton reader would show 7.");
            Assert.AreEqual(7, (int)Rig("CountdownTime"),
                "sanity: the bus event must not have written back to the manager's storage");

            // 3. CONTROL: the view CAN render "7" - from the bus.
            Rig("FireCountdownEvent", 7);
            yield return null;
            Assert.AreEqual("7", (string)Rig("ViewCountdownText", _view),
                "control: the view renders \"7\" when the BUS carries 7. So the earlier absence of " +
                "\"7\" was the enable path not reading the singleton - not an inability to show it.");
        }

        [UnityTest]
        public IEnumerator Winner_view_reads_the_bus_not_the_DeathmatchManager_singleton()
        {
            yield return new WaitForSeconds(0.2f);
            Assert.IsTrue((bool)Rig("ManagersPresent"));

            var alpha = Rig("RegisterPlayer", true);
            var bravo = Rig("RegisterPlayer", true);
            Rig("SetUsername", alpha, "Alpha");
            Rig("SetUsername", bravo, "Bravo");

            // 1a. PRECONDITION: the harness really set the usernames. NetworkPlayer.ServerSetUsername
            //     is a [ServerRpc] that silently no-ops in this fixture; without this assert that
            //     failure surfaces later as an inscrutable null.
            Assert.AreEqual("Alpha", (string)Rig("Username", alpha),
                "precondition: the harness must actually set alpha's username");
            Assert.AreEqual("Bravo", (string)Rig("Username", bravo),
                "precondition: the harness must actually set bravo's username");

            // 1b. PRECONDITION: the manager really stores Alpha as the leader. Asserted BEFORE the
            //     view subscribes, because reading the stored leader re-broadcasts it on the bus.
            Rig("SetLeadPlayer", alpha);
            Assert.AreEqual("Alpha", (string)Rig("StoredLeadUsername"),
                "precondition: DeathmatchManager must actually store Alpha as the leader, otherwise " +
                "'the view shows Bravo, not Alpha' is not a real contrast.");

            _view = (GameObject)Rig("CreatePostgameCountdownView");
            Rig("SetViewActive", _view, true); // OnEnable: subscribe only, must NOT read leadPlayer
            yield return null;

            Assert.AreNotEqual("Alpha", (string)Rig("ViewWinnerText", _view),
                "enabling the view must NOT copy DeathmatchManager.leadPlayer's username into the " +
                "text (the old singleton reach-in). Meaningful because the CONTROL below proves this " +
                "same view can render \"Alpha\" when the BUS says Alpha.");

            // 2. PROOF: storage says Alpha, the bus says Bravo -> the view must show Bravo.
            Rig("FireLeadPlayerEvent", bravo);
            yield return null;
            Assert.AreEqual("Bravo", (string)Rig("ViewWinnerText", _view),
                "the winner text must track NetworkUIEvents.OnLeadPlayerChanged. Showing Bravo while " +
                "DeathmatchManager still stores Alpha is the decoupling; a singleton reader shows Alpha.");

            // 3. CONTROL: the view CAN render "Alpha" - from the bus.
            Rig("FireLeadPlayerEvent", alpha);
            yield return null;
            Assert.AreEqual("Alpha", (string)Rig("ViewWinnerText", _view),
                "control: the view renders \"Alpha\" when the BUS carries Alpha. So the earlier " +
                "absence of \"Alpha\" at enable was the view not reading DeathmatchManager.Instance - " +
                "not an inability to render that name.");
        }

        [UnityTearDown]
        public IEnumerator UiTearDown()
        {
            if (_view != null) Rig("DestroyGameObject", _view);
            _view = null;
            Rig("ResetNetworkUIEvents"); // static bus: null every field so it can't leak across tests
            yield return null;
        }

        // --- MatchTestRig reflection bridge (Assembly-CSharp is unreferenceable here) ---

        static readonly Type RigType = Type.GetType("MatchTestRig, Assembly-CSharp");

        static object Rig(string method, params object[] args)
        {
            Assert.IsNotNull(RigType, "MatchTestRig not found in Assembly-CSharp");
            var mi = RigType.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(mi, $"MatchTestRig.{method} not found");
            return mi.Invoke(null, args);
        }
    }
}
