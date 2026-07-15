---
name: integration-testing
description: How to write Unity integration tests that verify components interacting — and which interactions deserve a test at all. Use when adding tests above the unit layer, choosing EditMode vs PlayMode, testing cross-system wiring, scaffolding or debugging a PlayMode fixture (the FishNet pitfalls live here), or deciding whether a test earns its keep. Consult BEFORE writing a PlayMode test; the floor: every networked change gets one.
---

# Integration testing

**The job of this suite is to replace manual regression playtesting.** Any flow currently verified by launching the game is a flow a PlayMode test can drive headless. Automate the *rules* (a kill scores, a death respawns, the match ends); leave to manual only *feel* (camera, juice, latency).

**The governing rule: capture the manual check.** If you can describe a manual playtest — "start a match, let two bots fight, confirm someone wins" — that description is the test's spec.

**The floor: every networked change gets a PlayMode test** (SyncVar/RPC/payload/server tick/collision/ownership — [[fishnet-conventions]] Rule 5). EditMode structurally can't reach where those bugs live.

Two disciplines bound the work:

- **Push each test down the pyramid.** Logic in a Core processor → EditMode owns it; don't re-verify it through a live GameObject. PlayMode is for what EditMode can't reach: lifecycle, wiring, SyncVar replication, real physics, multi-frame timing. Reaching for PlayMode to test branchy decision logic means logic leaked out of Core — move it back.
- **Assert observable behaviour, never internal structure**, so tests survive refactors. For broad coverage cheap, use **invariant/soak assertions**: run a bot match 20–30s and assert what must always hold (no error logs — the framework fails on them for free; health never negative; score only increases; the match actually ends).

## Worth writing? Five gates

1. **Load-bearing?** Would a silent break cause a player-visible bug (divergence, an event that stops firing, a timer that never expires)?
2. **Is PlayMode the cheapest level that catches it?** Down-rank to EditMode when possible; escalate only for the glue.
3. **Stable enough to pin?** Pin contracts, not seams you're redesigning weekly.
4. **Will it survive a refactor?** If it only works coupled to internals, narrow the assertion to one observable effect.
5. **CAN IT FAIL?** Before trusting a green test, name the change that would turn it red.

### Gate 5: the test that cannot fail

A test asserting the **absence** of an effect — "the victim is not stunned", "it did not move", "no projectile spawned", "no error was logged" — passes just as happily when the feature **does not exist at all**: the component is missing from the prefab, the effect was never authored on the list, the wire was never connected. It reports green over a hole.

This is not hypothetical. `HitstunKnockbackTests` once shipped `Hitstun_resistance_of_zero_makes_the_victim_immune` and `Knockback_resistance_of_zero_makes_the_victim_immovable` — both **passed against a Bullet prefab carrying neither dealer component nor either operation.** Two green tests, zero feature.

So: **every absence-assertion needs a positive control** — a companion assertion, ideally in the same fixture and preferably in the *same test*, proving the mechanism fires when it isn't suppressed.

```csharp
// NOT ENOUGH — green when the feature is absent.
SetHitstunResistance(target, 0f);
ShootUntilHit(shooter, target);
Assert.IsFalse(IsStunned(target));

// Positive control first: prove the baseline mechanism actually works...
ShootUntilHit(shooter, control);
Assert.IsTrue(IsStunned(control), "control: an unresisted pawn IS stunned");
// ...only then is the suppression meaningful.
SetHitstunResistance(target, 0f);
ShootUntilHit(shooter, target);
Assert.IsFalse(IsStunned(target), "0x resistance absorbs the stun");
```

The same trap wears other clothes: `Assert.AreEqual(0, spawnCount)` when nothing could ever spawn; `LogAssert.NoUnexpectedReceived()` when the code path never ran; `Assert.IsFalse(died)` against a pawn that was never shot. **Assert the precondition that proves the code under test executed** (`Assert.Less(Health(target), StartHealth, "sanity: it really was hit")`) — an absence-assertion is only evidence if you first prove the trigger occurred.

Corollary for authored data: a test exercising a prefab-authored effect list is also testing the **authoring**. Assert the component/operation is present, or make the mechanism fire visibly, or a later prefab edit silently deletes your coverage.

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
