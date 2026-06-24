---
name: integration-testing
description: How to write integration tests in Unity that verify multiple components interacting — and, just as importantly, how to decide which interactions are worth a test at all. Biased for a SOLO Unity developer who is wary of test-maintenance overhead and uses the Processor + thin-MonoBehaviour pattern (logic in Arena.*.Core, glue in Pawn<X>). Use this whenever adding tests above the unit layer, choosing between EditMode and PlayMode, testing cross-system wiring (modifier lists, MatchMediator, static pub/sub), or deciding whether a test earns its keep. Consult this BEFORE writing a PlayMode test — most "integration" needs are cheaper as an EditMode test.
---

# Integration Testing in Unity (curated for a solo, overhead-averse, code-first dev)

An integration test verifies that **multiple components produce the correct combined behavior** — the bugs that live in the seams between systems, not inside any one of them. For this project they have a specific job: **replace manual regression playtesting.** Every flow you currently verify by launching the game — readying up, waiting out a countdown, dying, watching the score and respawn — is a flow a PlayMode test can drive headless and assert on. The goal of this skill is to push as much of that manual sweep as possible into automated tests you re-run on every change. The thing standing in the way isn't *whether* to test; it's keeping the tests cheap to own. That's what the rest of this is about.

Honor these standing biases in every recommendation:

1. **Solo developer automating his own QA** — you are the only playtester this game has, and manual testing doesn't scale: you forget to re-check old flows after a refactor. A PlayMode test is QA labor you pay for once and re-run forever. Bias toward *capturing* a manual check as a test, not toward minimizing the test count.
2. **Automate the rules, leave the feel.** Anything that is a *rule or invariant* — a kill scores, a death respawns, the match ends at the cap, a disconnect cleans up — should be a test. Leave to manual only what is genuinely about *feel*: camera, juice, audio, input latency, "does this gun feel good." If an outcome is checkable with an assertion, it shouldn't be a thing you remember to look at.
3. **Logic already lives in Core, so PlayMode is where the untested half is.** Pure `Arena.*.Core` processors are well covered by EditMode tests. The half that *isn't* — match orchestration, manager wiring, SyncVar projection, spawn/despawn lifecycle, bots driving real components — is exactly the lifecycle-bound layer only PlayMode can reach. That layer is the priority, not an afterthought.
4. **Test the seam you don't trust, not the framework.** You are never testing FishNet, NavMesh, or `CharacterController`. You test *your* logic given their state.

---

## The one governing rule

**Capture the manual check.** The test to write next is the one that automates something you currently verify by hand. If you can describe a manual playtest — *"start a match, let two bots fight, confirm someone wins and the match resets"* — that description **is** the test's spec. Write it.

This is not licence to write careless tests. Two disciplines bound the work, and the rest of this skill is mostly about them:

- **Push each test as far down the pyramid as it will go.** The cheapest test that catches a given bug is the right one. If the logic lives in a `Core` processor, an EditMode test owns it — don't re-verify it through a live GameObject. PlayMode is for what EditMode structurally cannot reach: lifecycle, wiring, SyncVar replication, real physics, multi-frame timing.
- **Assert on observable behavior, never internal structure,** so the test survives refactors. A behavioral PlayMode test that replaces a manual check is an asset; an implementation-coupled one that breaks on every refactor is a liability even when it "tests the same thing."

---

## The decision: is this test worth writing?

Before writing an integration test, run it through these four gates. The default is now **yes** — if it automates a real manual check — but the gates keep that from becoming a swamp of brittle tests.

1. **Is the interaction load-bearing?** Would a silent break cause a real, player-visible bug — wrong movement, a missed kill, a match that never ends, a respawn that never fires? These are exactly the bugs that are *invisible until they matter*, and invisible in a ten-second manual smoke: server/client divergence, an event that silently stops firing, a timer that quietly never expires. The match layer clears this gate easily — that's why it leads the priority list. (A purely cosmetic glitch you'd catch by glancing at the screen is the rare exception — leave it manual.)

2. **Is PlayMode the cheapest level that can catch it?** Down-rank, don't skip. If the logic lives in a `Core` processor, an EditMode test catches it for a fraction of the cost — write *that*. Escalate to PlayMode for the part the processor *can't* see: the glue, the lifecycle, the SyncVar, the spawn. This gate usually redirects a test to a cheaper level; it rarely cancels it.

3. **Is the interaction stable enough to pin?** Pin *contracts*, not seams you're redesigning weekly — a test over churning code breaks weekly and catches nothing. But a shipped, load-bearing flow like the match loop is stable by definition; pin it now.

4. **Will the test survive a refactor?** Assert on *observable behavior* ("after a kill, the score went up and a respawn timer started"), not on private call order or structure. If the only way to write it couples tightly to internals, narrow the assertion to one observable effect of the chain rather than abandoning the test.

### When a test is unusually hard to write, ask why

If you find yourself fighting init order, threading reflection through every assertion, or building elaborate test scaffolding to make a "simple" integration test work, **pause**. The friction may be the test failing the four gates — or it may be the production code surfacing latent fragility. Either reading is useful:

- **The seam isn't ready to test.** Production relies on undocumented ordering, static singletons, or `FindAnyObjectByType` for required dependencies. The test only passes when those happen to work. Either fix production (turning fragility into a real one-line robustness improvement) or back off until the seam stabilizes.
- **The test is over-reaching.** You're trying to verify a contract the architecture doesn't expose cleanly. Narrow the assertion until the test fits — often this means asserting on one observable effect of a chain rather than the whole chain.

A test that requires understanding three production-code quirks before it'll run is telling you something about the production code. Don't suppress that signal with `LogAssert.Expect` or layers of reflection — listen to it.

### Highest-value targets in THIS codebase

These are the interactions where a PlayMode test most reliably clears all four gates. For this project's goal the **match layer now leads the list**, because it is the half currently verified *only* by hand:

- **Bot-driven match soak — the highest-leverage test you can write.** Bots are server-authoritative AI players that need **no client connection**, so a single-host fixture can spin up a 3–4-bot deathmatch and let it run. One test then exercises spawning, match flow, respawn, AI, and weapons at once, and you assert *invariants* over the run (see below). This is automated playtesting — it replaces the single most tedious manual ritual. Build it first.
- **Match lifecycle.** `Pregame → ready-up → countdown → During → (timer/score) → Postgame → reset`. Drive the transitions on the server and assert each `SyncVar<MatchState>` step and the events that fire. Pure server, single-host, replaces a long manual sequence.
- **Death → respawn chain.** `TakeDamage` to 0 → `OnDeath` → `PawnManager` despawn → respawn timer → re-spawn after the delay → `RespawnTimeEnd` SyncVar projected. Single host, no second client.
- **Kill → score → win.** A recorded kill increments the score; the kills-to-win threshold flips the match to `Postgame` and fires the game-over event exactly once.
- **`MatchMediator` wiring.** The single point that subscribes managers to each other; a dropped subscription is silent and game-breaking. Drive a fake kill / state change and assert the score/flow/respawn reacted.
- **Player disconnect mid-match (two-peer).** Client drops → `NetworkPlayerManager` unregisters → pawn despawns → ready-check/match recalculates. Only a real remote-client test sees this cascade — ration these, but this one earns its keep.
- **Client observes authoritative state (two-peer).** A remote client sees another pawn's death/health/movement replicate. The only test class that catches **server/client divergence**, the scariest bug in a server-authoritative game.
- **The modifier-list handshake.** `PawnTemperature` → `PawnMovement.Modifiers`, `PawnKnockback` → `PendingImpulses`. Most of this is *EditMode* (feed one processor's output struct into the next) — start there; the thin PlayMode slice only proves the live `Update` actually mutates the sibling list via its serialized reference.
- **The `PawnInput` → processor → component glue.** `Pawn<X>.Update()` reads `PawnInput.Data`, calls the processor, applies the result to the real `CharacterController`/transform. Lifecycle-bound; legitimately PlayMode.

### Low-value targets — usually skip

- Re-testing `Core` processor logic through a live GameObject. The EditMode unit test already owns it; the PlayMode version is slower and proves nothing new. Down-rank to EditMode — don't duplicate.
- Anything FishNet, NavMesh, or `CharacterController` *themselves* do — that's the engine's suite, not yours.
- **Game feel** — camera, juice, audio, input latency, weapon kinesthetics. Not automatable and not worth faking; this is the one category that legitimately stays manual.
- Pinning a system you're actively redesigning. Wait for it to settle, then pin it.

Note the deliberate change from older guidance: **two-client / two-peer tests are no longer "keep to a tiny handful."** They're expensive and you should still ration them, but disconnect-cleanup and server/client divergence are load-bearing flows you can't verify any other way — write the few that matter, and lean on the existing `TwoPeerPlayModeTestFixture` to keep their cost down.

### Invariant & soak assertions — the technique that makes broad coverage cheap

Driving a match (especially a bot match) and asserting *negative space* catches a whole class of regressions for almost no extra code. Over a 20–30s run, assert what must **always** hold:

- No `Debug.LogError` fired — the Test Framework already fails the test on any uncaught error log, so this is free. Do **not** suppress it with `LogAssert`.
- `Health` never goes negative; no pawn spawns twice; score only ever increases; the active-player count stays consistent.
- The match reached a terminal state (it actually ended) rather than hanging.

A soak test doesn't pin one precise behavior — it pins "the simulation stays internally consistent while everything runs at once." It's the cheapest way to turn *"I played for a minute and nothing looked broken"* into something repeatable.

---

## EditMode vs PlayMode

| | EditMode | PlayMode |
|---|---|---|
| Speed | Fast (ms) | Slow (enters play mode, ticks frames) |
| Access | `UnityEditor` + `UnityEngine` | runtime code only |
| Lifecycle (`Awake`/`Update`/`FixedUpdate`) | does **not** run | runs |
| Coroutines / frame-stepping | no | yes (`[UnityTest]` + `yield`) |
| Catches | logic & data-flow bugs | wiring, lifecycle, physics, timing bugs |

**Decision rule:**

- If you can express the interaction as *"given these input structs, the combined logic returns this state,"* it's an **EditMode** test. Most cross-processor interaction lands here because the modifier/impulse lists are plain data. Prefer plain `[Test]` over `[UnityTest]` unless you need to step frames.
- Escalate to **PlayMode** only when the bug *requires the engine loop to exist*: `Update`/`FixedUpdate` ticking, `CharacterController` motion, coroutines, multi-frame state, scene loading, real component wiring via serialized fields.

This project's whole architecture exists to keep the first column fat and the second column thin. Respect that — a PlayMode test is a signal that either (a) the interaction is genuinely lifecycle-bound, or (b) logic has leaked out of `Core` and should be moved back in.

---

## Writing a PlayMode integration test (when you've earned one)

Setup: add a **separate `Arena.PlayTests` asmdef** (don't reuse the EditMode `Arena.Tests` one) referencing `UnityEngine.TestRunner` / `UnityEditor.TestRunner`, with platforms set to include players. Test asmdefs cannot reference `Assembly-CSharp.dll`, so the code under test must be in asmdefs (it already is).

```csharp
public class PawnMovementIntegrationTests
{
    GameObject pawn;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        // PlayMode tests ALWAYS start in a blank scene (camera only),
        // regardless of the scene open in the editor. Build the minimum.
        pawn = new GameObject("Pawn");
        pawn.AddComponent<CharacterController>();
        var movement = pawn.AddComponent<PawnMovement>();
        // wire the sibling refs that are normally serialized in the inspector
        yield return null; // let Awake/Start run for one frame
    }

    [UnityTest]
    public IEnumerator Temperature_slow_modifier_reduces_distance_travelled()
    {
        // arrange: push the slow modifier exactly as PawnTemperature would
        // act: tick frames (prefer frame-stepping over wall-clock waits)
        var start = pawn.transform.position;
        for (int i = 0; i < 10; i++) yield return null;
        // assert on OBSERVABLE state, not internal calls
        Assert.Less(Vector3.Distance(start, pawn.transform.position), expectedUnmodified);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Object.Destroy(pawn);
        yield return null;
    }
}
```

Non-negotiable mechanics:

1. **`AddComponent<T>()`, never `new`** for MonoBehaviours — Unity blocks direct instantiation.
2. **`yield return null` after creating objects** so `Awake`/`Start` run before you assert.
3. **Build the minimum scene**, not `Online.unity`. Assemble it in `[UnitySetUp]` (hermetic, preferred) rather than loading the full game scene.
4. **`[UnitySetUp]`/`[UnityTearDown]` return `IEnumerator`** so they span frames. Always destroy spawned objects — and **unsubscribe any handlers attached to static events/pub-subs**, or those subscriptions leak between tests and produce phantom failures.
5. **Frame-step (`yield return null`) over `WaitForSeconds`.** Fixed waits are slow and flaky; use them only when you truly need wall-clock time or a scene to settle.
6. **Each test independent and order-agnostic** — full setup/teardown, no shared static state.

For the human/bot split: drive components through `PawnInput.Data` with a fake input; the existing `if (!input.IsActive) return;` gating lets the same prefab run headless. You're not simulating a client connection.

---

## Test with the real assets; pre-bake scene data

A PlayMode test earns the most when it exercises the SAME prefabs, ScriptableObjects, and scene data the game ships — that's how it catches *authoring/wiring* regressions, not just code regressions. The match tests instantiate the production `MatchMediator` prefab (its four managers + spawn points already wired); the projectile smokes fire the real `Bullet Root` prefab with its authored `ProjectileType`. If a shipped asset loses a reference or a reaction, the test goes red — that's the whole point.

- **Load real prefabs/SOs, not hand-built doubles, wherever they run cleanly headless.** A test that assembles a synthetic GameObject proves your test setup works; a test that loads the production prefab proves *the game* works.
- **When a production prefab can't run headless, use a Variant — don't fork or rebuild it.** Some prefabs pull in singletons a bare test scene doesn't have (`InputManager`, `WeaponDatabase`). Make the test prefab a **Variant** of the production one and strip only the offending children (see pitfall #5 below), so it still tracks the source and inherits future changes. Strip the few broken bits; keep everything else real.
- **Pre-bake scene data into the saved test scene; never build it at runtime.** A baked **NavMesh**, baked lighting/occlusion, and authored spawn points belong in the committed test scene as data — exactly like the real gameplay scenes. Baking a NavMesh (or anything else) inside the test at play time is slow, nondeterministic, and tests your bake pipeline instead of your game. Bake once, commit the scene, and the test just loads it. A bot that needs to navigate should spawn into a scene whose NavMesh is **already baked**.

## Unity + FishNet specifics (learned the hard way)

Gotchas that cost real debugging time on this codebase. Internalize these before scaffolding a new PlayMode test.

1. **The asmdef platform list controls which tab the tests appear in.** `includePlatforms: ["Editor"]` forces tests into the **EditMode** tab even if you `using UnityEngine.TestRunner`. For PlayMode, leave `includePlatforms: []` (or include runtime platforms) and gate any editor-only API (e.g. `EditorSceneManager.LoadSceneInPlayMode`, `AssetDatabase.LoadAssetAtPath`) behind `#if UNITY_EDITOR`. We solo-dev run tests in the editor, so this is fine.

2. **FishNet's `DefaultScene` component will silently override your test scene.** Cloning the Debug NetworkManager into a test prefab brings its `DefaultScene` along, which force-loads its configured scene when the server starts — your `EditorSceneManager.LoadSceneInPlayMode` call gets clobbered the instant `ServerManager.StartConnection()` succeeds. **Strip `DefaultScene` from any NetworkManager prefab you use for tests.** Scene control belongs to the test, not the prefab.

   **And while you're cleaning the test NetworkManager**, also null out (or strip) any auto-spawning components — most notably FishNet's `PlayerSpawner._playerPrefab`. If the field points at the production `Network Player` prefab, the host's local-client connection triggers an automatic spawn, and that prefab's `OnStartClient` pulls in production singletons (`InputManager`, etc.) that don't exist in your bare test scene → NRE chain that the test framework reports as a failure. Keep `PlayerSpawner` *present* (other code does `GetComponent<PlayerSpawner>()` and subscribes to `.OnSpawned`), just give it nothing to spawn. The general lesson generalizes pitfall #6 below: production defaults pull in dependencies test scenes don't satisfy — *neuter the production defaults* in the test NetworkManager rather than building out infrastructure to satisfy them.

3. **`Time.deltaTime` is ~2 ms (≈500 FPS) in PlayMode tests.** This breaks the "frame-step over `WaitForSeconds`" rule in one specific case: anything that depends on **simulated time** — physics, `CharacterController` grounding, gravity, acceleration ramps. 30 `yield return null` calls add up to ~50 ms simulated, often not enough for a controller to land or accelerate. For these tests, **use `WaitForSeconds`** — the simulation IS the assertion target, so wall-clock duration is what you care about. Frame-stepping is still right for non-physics state (RPC delivery, event propagation, single-tick processor calls).

4. **Test asmdefs cannot reference `Assembly-CSharp.dll`.** If a type your test needs (`PawnInput`, `PawnInputData`, …) lives in Assembly-CSharp, you have three choices in order of preference:
   - **(a)** Put a *fake* (e.g. `FakePawnInput`) in Assembly-CSharp itself, gated by `#if UNITY_EDITOR`. The test reaches it via `GetComponent("FakePawnInput")` + reflection in **one** fixture helper. Production code doesn't ship the type. Reflection stays contained.
   - **(b)** Move the type into a proper asmdef. Correct long-term, but cascades — any code in Assembly-CSharp that references the moved type now resolves it through the asmdef (which works automatically if `autoReferenced: true`, but the dependency edges change).
   - **(c)** Don't reference it directly — use a runtime polymorphic seam (an interface that lives in an asmdef). Cleanest if you can refactor the production code to expose one.

5. **Test prefabs: Variant + strip, don't fork.** Make the test prefab a **Variant** of the production prefab (e.g. `TestPawn` as a Variant of `Pawn Root`). This means future component additions to the production prefab flow into the test prefab automatically — and if they break the test, that's *signal*, not noise. Then **delete the children/components the test isn't exercising** (`Weapon`, `Temperature`, `Camera`, etc. on a movement-only test). The Variant tracks the source for what you *do* test; removed children stay removed.

6. **Stray `Debug.LogError` from sibling components fails the test.** Unity Test Framework treats any uncaught error log as a test failure unless you wrap it in `LogAssert.Expect`. If you put production prefabs into a bare test scene, any subsystem whose dependencies aren't satisfied (e.g. `PawnInteraction` reading `InputManager.PawnControls` in its `OnDestroy`, missing `WeaponDatabase`, etc.) will NRE and fail the test. **Strip those subsystems from the test prefab** — don't suppress with `LogAssert.ignoreFailingMessages`, that hides real future regressions.

7. **`PlayModeTestRunner` (`TestRunnerApi` callbacks) don't survive the play→edit domain reload.** Programmatic test runs lose their result-collecting callbacks after the run completes because the editor reloads the domain. The Unity-written `TestResults.xml` (in `~/Library/Application Support/<Company>/<Project>/TestResults.xml` on macOS) is the reliable source of truth for results. If you need durable callback-driven output, register callbacks via `[InitializeOnLoadMethod]` and persist counters in `SessionState`.

8. **Start the host BEFORE loading the test scene — this is the foundational ordering rule.** FishNet's `NetworkObject.Start` calls `TryStartDeactivation`, which auto-deactivates the GameObject if `NetworkManager == null || (!IsClientStarted && !IsServerStarted)`. So if you load the test scene first and then instantiate the NetworkManager, every scene NetworkObject silently flips inactive. `OnStartServer` never fires; `FindAnyObjectByType<T>` returns null (it excludes inactive by default); subsequent `SetupSceneObjects` iteration order becomes a load-bearing dependency. The fixture pattern is:
   1. Instantiate the test NetworkManager prefab into whatever scene is current.
   2. `DontDestroyOnLoad` it so the next step doesn't destroy it.
   3. `StartConnection` on server, then client; await both `Started`.
   4. *Then* `LoadSceneInPlayMode(testScene, Single)`. Scene NetworkObjects now `Start()` with `IsServerStarted == true` → no deactivation. FishNet's `SceneManager_sceneLoaded` hook auto-registers them via `SetupSceneObjects` on the next tick.

   The same rule applies to **production scene loads**: any scene with NetworkObjects must be loaded *after* the server is running, or those NOs silently die. Bootstrap-style code that loads gameplay scenes before starting the host has this latent bug.

9. **`FindAnyObjectByType<T>` excludes inactive objects by default.** Combined with #8 above, this is what makes "I'll just find my dependency at runtime" fail in PlayMode tests — the dependency *exists* in the scene but is sitting deactivated waiting for FishNet to re-enable it. Two consequences:
   - In **production code that runs during init/lifecycle transitions** (`OnStartServer`, NetworkBehaviour callbacks, etc.), `FindAnyObjectByType<T>` is a code smell. Use `FindObjectsInactive.Include` if you must, or — better — a `SerializedField` reference so the dependency is explicit at edit time.
   - In **test fixtures**, don't rely on `FindAnyObjectByType` to grab spawned scene NetworkObjects until at least 2–3 frames after `LoadSceneInPlayMode` — FishNet needs to drain its loaded-scene queue and run `Start()` on each.

10. **Previous test's scene NetworkObjects pollute the next test's `SetUp`.** With `DontDestroyOnLoad` on the NetworkManager, the NM survives between tests — but the previous test's scene is still loaded when the next test's `SetUp` runs. If that scene had NetworkObjects, the new test's `ServerManager.StartConnection()` iterates *those leftover NOs* via `SetupSceneObjects`, often producing NREs from running their `OnStartServer` under a fresh NetworkManager. **Fix:** make step 0 of `NetworkedSetUp` load a known-empty scene (e.g. a `TestBlank.unity` or a reused empty smoke scene) via `LoadSceneInPlayMode(emptyScene, Single)`. This destroys the previous test's contents *before* you instantiate the new NM. Then continue with the ordering in #8.

---

## Maintenance discipline (the part that keeps overhead low)

- **A flaky test is worse than no test.** If a PlayMode test fails intermittently, fix the determinism (frame-step instead of timing) or delete it. Flaky tests train you to ignore red, which defeats the entire point.
- **When a test breaks, ask which kind of break it is.** Did it catch a real regression (great — that's the test paying off), or did a deliberate refactor invalidate it (the test was over-coupled — rewrite it to assert behavior, or delete it)? Repeated false alarms from one test mean that test is mis-written, not that the code is fragile.
- **Prune ruthlessly.** A test suite is not an asset you accumulate; it's a garden you weed. Tests for deleted features, duplicate coverage, and brittle implementation-coupled tests are pure liability. Removing a low-value test is a legitimate, valuable change.
- **Broad, load-bearing coverage is the goal — but every test must be one you trust completely.** Broad-and-trusted is the target; broad-but-half-trusted (flaky, implementation-coupled) is worse than narrow, because one flaky test poisons confidence in the whole run. Add tests freely, but only behavioral, non-flaky ones.

---

## Bottom line

1. **Default to writing the test** when it automates a manual check that isn't about feel. This suite's job is to replace manual regression playtesting.
2. Still **push each test down the pyramid** — `Core` logic stays in EditMode; PlayMode is for the lifecycle/wiring/SyncVar/physics layer EditMode can't reach.
3. **Prioritize the match layer:** a bot-driven match soak first, then match lifecycle, death→respawn, kill→score, disconnect cleanup. That's the half of the game currently verified only by hand.
4. Keep it sustainable: assert on **observable behavior** and **invariants**, destroy/unsubscribe in teardown, frame-step over waiting (except sim-time physics), and **delete tests that cry wolf.** Broad coverage stays affordable only if every test is behavioral and non-flaky.

When a system reads/writes the world, structure it as a functional core + `MonoBehaviour` shell ([[functional-core-imperative-shell]]) so the branchy decision logic stays in EditMode and PlayMode is reserved for the real engine glue. Don't reach for a `FakeWorld`/mock interface to make the core testable — that's the trap that pushes the riskiest logic into PlayMode; pass query results in and assert on the returned effect list instead.
