---
name: integration-testing
description: How to write Unity integration tests that verify components interacting — and which interactions are worth a test at all. Biased for a SOLO dev automating his own QA on the Processor + thin-MonoBehaviour pattern. Use this whenever adding tests above the unit layer, choosing EditMode vs PlayMode, testing cross-system wiring (modifier lists, MatchMediator, static pub/sub, SyncVars), scaffolding or debugging a PlayMode fixture (the FishNet pitfalls live here), or deciding whether a test earns its keep. Consult BEFORE writing a PlayMode test — much "integration" is cheaper in EditMode — and remember the floor: every networked change gets a PlayMode test.
---

# Integration testing

**The job of this suite is to replace manual regression playtesting.** Any flow currently verified by launching the game is a flow a PlayMode test can drive headless. Automate the *rules* (a kill scores, a death respawns, the match ends); leave to manual only *feel* (camera, juice, latency).

**The governing rule: capture the manual check.** If you can describe a manual playtest — "start a match, let two bots fight, confirm someone wins" — that description is the test's spec.

**The floor: every networked change gets a PlayMode test** (SyncVar/RPC/payload/server tick/collision/ownership — [[fishnet-conventions]] Rule 5). EditMode structurally can't reach where those bugs live.

Two disciplines bound the work:

- **Push each test down the pyramid.** Logic in a Core processor → EditMode owns it; don't re-verify it through a live GameObject. PlayMode is for what EditMode can't reach: lifecycle, wiring, SyncVar replication, real physics, multi-frame timing. Reaching for PlayMode to test branchy decision logic means logic leaked out of Core — move it back.
- **Assert observable behaviour, never internal structure**, so tests survive refactors. For broad coverage cheap, use **invariant/soak assertions**: run a bot match 20–30s and assert what must always hold (no error logs — the framework fails on them for free; health never negative; score only increases; the match actually ends).

## Worth writing? Four gates

1. **Load-bearing?** Would a silent break cause a player-visible bug (divergence, an event that stops firing, a timer that never expires)?
2. **Is PlayMode the cheapest level that catches it?** Down-rank to EditMode when possible; escalate only for the glue.
3. **Stable enough to pin?** Pin contracts, not seams you're redesigning weekly.
4. **Will it survive a refactor?** If it only works coupled to internals, narrow the assertion to one observable effect.

When a test is unusually hard to write, that's signal: either the seam relies on undocumented ordering/statics (fix production) or the test over-reaches (narrow it). Don't suppress the signal with `LogAssert`/reflection layers.

The existing suite covers the match layer (lifecycle, kill→score→win, death→respawn, disconnect, bot soak), projectile/damage/buff smokes, ability inventory, and two-peer replication — extend those patterns rather than inventing new harness shapes. Harness mechanics live in the [[playmode-match-test-harness]] and [[combat-playmode-test-harness]] memories.

## EditMode vs PlayMode

If the interaction is expressible as "given these input structs, the combined logic returns this state," it's **EditMode** (most cross-processor interaction is — the modifier/impulse lists are plain data). Escalate to **PlayMode** only when the bug requires the engine loop: `Update` ticking, `CharacterController` motion, coroutines, scene loading, serialized wiring, FishNet.

Mechanics for a PlayMode test: separate `Arena.PlayTests` asmdef (test asmdefs can't reference Assembly-CSharp — typed seams live in `*TestRig` classes reached by reflection, contained to one fixture helper); `AddComponent`, never `new`, for MonoBehaviours; `yield return null` after creating objects; build the minimum scene in `[UnitySetUp]`; destroy and **unsubscribe static handlers** in teardown; frame-step rather than `WaitForSeconds` — except for simulated-time physics (PlayMode runs ~500 FPS, so 30 frames ≈ 50ms of physics; there, wall-clock waits are correct).

**Test real assets; pre-bake scene data.** Load the production prefabs/SOs wherever they run headless — that catches authoring regressions, not just code. When one can't run headless, make the test prefab a **Variant and strip** the offending children (never fork). Bake NavMesh/lighting into the committed test scene; never bake at runtime.

## FishNet/Unity pitfalls (each cost real debugging time)

1. **Asmdef platforms decide the tab:** `includePlatforms: ["Editor"]` forces tests into EditMode; leave it empty for PlayMode and gate editor APIs behind `#if UNITY_EDITOR`.
2. **Strip `DefaultScene` from any test NetworkManager** — it force-loads its configured scene when the server starts, clobbering yours. Also null the `PlayerSpawner._playerPrefab` (keep the component; other code subscribes to it): production defaults pull in singletons a bare scene doesn't satisfy — neuter them rather than building infrastructure.
3. **Start the host BEFORE loading the test scene.** A scene NetworkObject that `Start()`s with no started server silently deactivates; `OnStartServer` never fires and `FindAnyObjectByType` can't see it. Fixture order: instantiate NM → `DontDestroyOnLoad` → start server+client, await both → `LoadSceneInPlayMode`. (Applies to production bootstrap too.)
4. **`FindAnyObjectByType` excludes inactive objects** — combined with #3, "find my dependency at runtime" fails during init. Prefer serialized refs; in fixtures wait 2–3 frames after scene load.
5. **Load a known-empty scene as step 0 of setup** — the previous test's scene NetworkObjects otherwise pollute the next `StartConnection` via `SetupSceneObjects`.
6. **Stray `Debug.LogError` from unrelated subsystems fails the test** — strip those components from the test prefab; don't `LogAssert.ignoreFailingMessages`.
7. **`TestRunnerApi` callbacks don't survive the play→edit domain reload** — Unity's written `TestResults.xml` is the source of truth for programmatic runs.

## Maintenance

A flaky test is worse than no test — fix the determinism or delete it. When a test breaks, ask which break it is: a caught regression (the test paying off) or an over-coupled test invalidated by a deliberate refactor (rewrite behaviourally or delete). Prune tests for deleted features and duplicate coverage; broad coverage stays affordable only while every test is behavioural and trusted.
