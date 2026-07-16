using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// W3 regression: the countdown / postgame views consume the NetworkUIEvents bus, they do
    /// NOT reach through MatchFlowManager.Instance.CountdownTime or
    /// DeathmatchManager.Instance.leadPlayer. Each test sets the manager's SyncVar to one value
    /// (the "internal storage"), enables a fresh PostgameCountdownView, and shows that:
    ///   - enabling the view does NOT copy the singleton value into the text (the old reach-in), and
    ///   - firing a bus event with a DIFFERENT value is what actually drives the text.
    /// If a view is ever wired back to a manager singleton, the "singleton value must not leak in"
    /// assertions fail. Runs on the single-host fixture against the manager-only test scene; all
    /// Assembly-CSharp access is via MatchTestRig by reflection.
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

            // Storage says 7. This publishes to nobody yet (the view does not exist).
            Rig("SetCountdownTime", 7);

            _view = (GameObject)Rig("CreatePostgameCountdownView");
            Rig("SetViewActive", _view, true); // OnEnable: subscribe only, must NOT read the singleton
            yield return null;

            Assert.AreNotEqual("7", (string)Rig("ViewCountdownText", _view),
                "enabling the view must NOT copy MatchFlowManager.CountdownTime.Value into the text " +
                "(that would be the old singleton reach-in). The view should wait for a bus event.");

            // A bus event with a DIFFERENT value is what drives the view.
            Rig("FireCountdownEvent", 3);
            yield return null;

            Assert.AreEqual("3", (string)Rig("ViewCountdownText", _view),
                "the countdown text must track NetworkUIEvents.OnCountdownChanged, not the singleton");
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

            // Storage says the leader is Alpha. Publishes to nobody yet.
            Rig("SetLeadPlayer", alpha);

            _view = (GameObject)Rig("CreatePostgameCountdownView");
            Rig("SetViewActive", _view, true); // OnEnable: subscribe only, must NOT read leadPlayer
            yield return null;

            Assert.AreNotEqual("Alpha", (string)Rig("ViewWinnerText", _view),
                "enabling the view must NOT copy DeathmatchManager.leadPlayer.Value.Username into the " +
                "text (the old singleton reach-in). The view should wait for a bus event.");

            // A bus event naming a DIFFERENT leader is what drives the view.
            Rig("FireLeadPlayerEvent", bravo);
            yield return null;

            Assert.AreEqual("Bravo", (string)Rig("ViewWinnerText", _view),
                "the winner text must track NetworkUIEvents.OnLeadPlayerChanged, not the singleton");
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
