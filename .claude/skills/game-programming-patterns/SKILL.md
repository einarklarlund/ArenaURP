---
name: game-programming-patterns
description: Curated guidance from Robert Nystrom's "Game Programming Patterns," biased for a SOLO Unity developer who prefers writing code over using the editor/inspector, favors maintainable and extensible code over raw performance, and writes edit-mode and play-mode tests. Use this whenever structuring or refactoring Unity gameplay systems; deciding how to organize entity, AI, input, state, or event code; choosing a design pattern (or talking the user out of one); or making gameplay logic testable in edit mode — even if the user never names a specific pattern. Consult this BEFORE reaching for Singletons, "manager" classes, deep inheritance, or premature optimization in Unity.
---

# Game Programming Patterns (curated for a solo, code-first, test-driven Unity dev)

This skill distills Robert Nystrom's *Game Programming Patterns* and filters it through five standing biases. Honor them in every recommendation:

1. **Solo developer** — future-you is the only collaborator. Optimize for "readable in six months," not for team coordination or letting non-programmers author content.
2. **Code over editor** — prefer defining behavior and wiring in C#. Treat the inspector, drag-and-drop references, and Animator graphs as last resorts, not defaults.
3. **Maintainable/extensible over performance** — only spend complexity to remove a coupling you expect to fight later. Do not spend it to save microseconds.
4. **Unity** — the engine already implements several of these patterns. Use the engine's version; don't reinvent it.
5. **Edit/play-mode tests** — testability is a first-class goal, not an afterthought.

## The two governing rules

**Rule 1 — Earn the pattern.** A pattern is debt you take on to buy flexibility. Add one only when it removes a coupling you can already feel. If you can't name the future change it protects, don't add it yet. This is the thesis of the book's "Architecture, Performance, and Games" chapter, and it is the single most important idea here.

**Rule 2 — Keep logic out of `MonoBehaviour`.** In Unity, edit-mode tests run without the engine loop; play-mode tests are slower and engine-bound. So the master technique behind every recommendation below is: **put real logic in plain C# classes and let `MonoBehaviour` be a thin adapter** that forwards Unity's lifecycle (`Awake`/`Update`/input) into those classes. Plain classes are edit-mode testable; thick `MonoBehaviour`s are not. When a pattern is hard to test, it is almost always because logic leaked into the `MonoBehaviour`.

**In networked or replay-driven games, "plain class" also means *deterministic and side-effect-free*.** Given the same inputs the plain class must always return the same outputs — no `Random.value`, no event raising, no `Time.deltaTime` reads inside the class itself, no GameObject mutation. The `MonoBehaviour` adapter feeds the per-tick delta in and reacts to the returned state afterward. Determinism is what lets server and clients (or current frame and replay) reach the same state from the same data, and it's also why a `static Process(state, in config, in input, dt) → newState` shape is so common in this codebase: every dependency is in the signature.

---

## Tier 1 — Core patterns. Reach for these freely.

### Command
**When to reach for it.** An action needs to be a first-class value: rebindable input, undo/redo, action queues, replays, AI issuing the same actions a player can. If you find yourself writing `if (key == ...)` branches that directly mutate game state, convert each branch into a command.

**Unity form.** A plain C# `interface ICommand { void Execute(); void Undo(); }`. A thin input `MonoBehaviour` reads Unity input and produces command objects, then hands them to a plain executor/history class. The `MonoBehaviour` decides *nothing* about what the command does.

**Edit-mode testable.** Construct a command against a plain model object (not a GameObject), call `Execute()` then `Undo()`, and assert the model returns to its prior state. No play mode, because a well-built command holds no engine reference.

### State (finite state machines)
**When to reach for it.** Anything with clear modes: enemy AI, input/control modes, game-flow screens, interaction or ability logic. Prefer this the moment a single object grows mutually-exclusive boolean flags (`isJumping`, `isAttacking`, `isStunned`).

**Unity form.** A plain C# machine — `interface IState { void Enter(); IState Tick(float dt); void Exit(); }` or a small `switch`. The owning `MonoBehaviour` only calls `machine.Tick(Time.deltaTime)`. Do **not** use the Animator for *logic*; keep the Animator for visuals/blending and drive it from the code FSM. The Animator is editor-bound and untestable, which violates biases 2 and 5.

**Edit-mode testable.** Feed the machine synthetic inputs and a fake delta time, then assert the current state and the transitions taken. Keep states engine-free: have them return intents or raise events rather than calling `Instantiate`/`Physics` directly, so the test never needs the engine. Read Nystrom's pushdown/hierarchical extensions when states start to share sub-behavior.

### Component (apply the philosophy; don't reimplement)
**When to reach for it.** Always — it is how Unity structures entities. Reach for composition specifically whenever an entity "is several things at once" and you're tempted to express that with inheritance.

**Unity form.** It *is* `GameObject` + `MonoBehaviour`. The curated discipline: keep each component small and single-responsibility, and push its real logic into a plain C# class the component owns and delegates to. The `MonoBehaviour` is glue; the plain class is the brain.

**Edit-mode testable.** Test the plain brain classes directly with no scene. This is the highest-leverage habit in the whole skill: a thick `Update()` is play-mode-only and slow to test, while the same logic in a plain class is instant to test in edit mode.

### Observer
**When to reach for it.** A source needs to announce that something happened ("health changed", "item picked up", "level loaded") without knowing or depending on who listens. Use it to break a dependency that only exists so A can notify B.

**Unity form.** Plain C# `event`/`Action<T>`, or a small typed event bus — wired in code (subscribe in `OnEnable`, **unsubscribe in `OnDisable`/`OnDestroy`**). Forgetting to unsubscribe is the classic Unity leak/null-ref bug; treat teardown as mandatory. ScriptableObject "event channel" assets exist and are popular, but they're editor-authored — offer them only if the user explicitly wants designer-visible wiring.

**Edit-mode testable.** Subscribe a test handler, raise the event, assert the handler fired with the right payload — pure C#, no play mode. Keep observers synchronous and simple; if you need to defer in time, that's Event Queue (Tier 2), not Observer.

### Type Object
**When to reach for it.** You have many *kinds* of a thing (enemy types, item types, spell types) that differ by **data**, not by behavior, and you want to add new kinds without writing a new class each time. Prefer this over a subclass-per-kind explosion.

**Unity form.** A `ScriptableObject` per type holding the data; each instance references its type asset and reads stats/config from it. ScriptableObjects are the best bridge for a code-first dev: the *class* is code, the data is plain serializable fields, and you can create them in code. If you want zero editor footprint, define the type as a plain serializable C# class loaded from JSON instead — same pattern, no assets.

**Edit-mode testable.** `ScriptableObject.CreateInstance<T>()` (or just `new` the data class), set fields in code, run the behavior that consumes the type data, and assert — ScriptableObjects instantiate fine in edit mode, so no play mode needed.

---

## Tier 2 — Situational. Reach for these only when the specific need appears.

### Object Pool
**When to reach for it.** *Only* after profiling shows GC spikes / instantiation hitches from frequently spawned short-lived objects (bullets, particles, hit effects). This is the one performance pattern a maintainability-first dev still commonly needs, because GC hitches degrade *feel*, not just benchmarks. Don't pool pre-emptively.

**Unity form.** Use the built-in `UnityEngine.Pool.ObjectPool<T>` rather than hand-rolling. The only real work is correctly resetting an object's state on `Get` so a reused object behaves like a fresh one.

**Edit-mode testable.** Put the reset logic in a plain method and test it directly (reused object == fresh object). A pool of plain objects is edit-mode testable; a pool of GameObjects is play-mode territory — another reason to keep reset logic out of the `MonoBehaviour`.

### Event Queue
**When to reach for it.** You need decoupling in **time** — defer handling to a later frame, or smooth bursts of events (audio, achievements, analytics). Reach for it only after a synchronous Observer has proven insufficient; the queue adds ordering, latency, and determinism concerns.

**Unity form.** A plain queue owned by one object and drained in a single `Update`/`LateUpdate` tick. Keep the drain step explicit.

**Edit-mode testable.** Enqueue events, then call the drain method **manually** in the test (never rely on `Update` firing). Assert handlers ran, in order. Exposing drain as a callable method instead of burying it in `Update` is exactly what keeps this deterministic and testable.

### Service Locator
**When to reach for it.** A handful of genuinely cross-cutting services (audio, save system, logging) where threading references through every constructor is truly unwieldy. Try plain dependency injection (pass the reference in) *first* — it's more testable and more honest about dependencies.

**Unity form.** A registry you can register into and **replace at runtime**, ideally resettable; or ScriptableObject service references. Avoid the static-`Singleton` form — the only reason to prefer Service Locator over a Singleton is swappability, so preserve it.

**Edit-mode testable.** Register a mock/fake implementation in test setup and **reset the locator in teardown** so state doesn't leak between tests. If you can't reset it, it's a Singleton in disguise and will cause test-order bugs.

**Static event holders are Service Locators in disguise.** The same discipline applies to any `public static event Action OnX` / static `Action` field: if the bus has no `Reset()` that nulls every field, subscribers from one test (or one scene reload) fire in the next. Either give the bus a `Reset()` you call in test teardown, or move the events onto an instance you can throw away. This is the single most common Unity test-isolation bug in this style of codebase.

### Subclass Sandbox
**When to reach for it.** A *closed* family of similar behaviors built from a fixed toolkit of operations (e.g., abilities composed from `Move`, `PlaySound`, `SpawnProjectile` primitives provided by a base class). If the set is open-ended, prefer Type Object or component composition instead — inheritance gets rigid fast.

**Unity form.** An abstract base (plain C# where possible) exposing `protected` primitive operations; each subclass implements one `Activate()` using only those primitives.

**Edit-mode testable.** Make the primitives overridable so a test subclass records calls (e.g., into a list) instead of touching the engine, then assert the recorded sequence. This keeps the behavior logic verifiable without a scene.

### Prototype / Spawning
**When to reach for it.** Spawning configured instances from a template, or data-driven factories that decide *what* to spawn.

**Unity form.** Prefab + `Instantiate()` already *is* this pattern — use it, and instantiate in code to stay code-first. Don't hand-roll cloning. Keep the *decision* logic (what/when/where to spawn) in a plain class, separate from the `Instantiate` call.

**Edit-mode testable.** Hide `Instantiate` behind a small `ISpawner` interface. Test the decision logic against a fake spawner that records requests; the real engine call runs only in play mode.

### Dirty Flag
**When to reach for it.** An expensive derived value is recomputed more often than its inputs actually change. A clean, cheap optimization — but still only when you can see the wasted work.

**Unity form.** A plain cache holding the value plus an `isDirty` bool: mark dirty when an input changes, recompute lazily on read. (Unity already does this internally for transforms/canvases.)

**Edit-mode testable.** Trivial and worth doing: set inputs, read, assert it recomputed exactly once; change an input, read, assert it recomputed again. Watch for invalidation bugs — a stale cache is a maintainability cost, so test the invalidation paths.

### Flyweight
**When to reach for it.** Rarely by hand. Mostly: understand it, because Unity already shares meshes, materials, and ScriptableObjects for you, and it overlaps with Type Object's shared-data idea.

**Unity form.** Shared `ScriptableObject`/asset references rather than duplicated per-instance data.

**Edit-mode testable.** Usually N/A. If you do implement shared data, assert that multiple instances read from the same source object.

---

## Skip list — do not implement these for this use case

Each is either owned by the engine, or a performance-first pattern that trades away the maintainability you're prioritizing. Understand the idea; don't build it.

- **Game Loop** — Unity owns the loop entirely; implementing your own fights the engine. Read it only to understand fixed vs. variable timestep (why `FixedUpdate` exists, how `Time.deltaTime` works).
- **Update Method** — this *is* `MonoBehaviour.Update()`. Never reimplement it. The one takeaway: keep `Update` thin and delegate to plain, testable methods (Rule 2).
- **Singleton** — treat the chapter as a *warning*, not a recipe. Global mutable state is the direct enemy of test isolation (state leaks across tests, nothing is mockable). Default to dependency injection or ScriptableObject references; if a "manager Singleton" is proposed, push back.
- **Bytecode** — writing your own VM is over-engineering for a solo dev. Use C# itself (or an existing embedded scripting language) unless you are specifically shipping a modding/scripting layer for untrusted content.
- **Data Locality** — a pure cache-efficiency optimization that explicitly trades maintainability for speed (the opposite of bias 3). If you ever truly need it, that's a deliberate DOTS/ECS/Burst decision, not a hand-rolled hack.
- **Spatial Partition** — Unity's physics already partitions space (`OverlapSphere`, raycasts, physics queries). Implement a custom grid/quadtree only for bespoke, large-scale queries the engine can't serve.
- **Double Buffer** — the engine handles frame buffering. The only place you'd implement it yourself is a grid/cellular simulation that must compute its next state from a consistent snapshot of the current one.

---

## Quick decision heuristic

When the user is structuring a system, ask in order:

1. **Does Unity already provide this?** (Game Loop, Update Method, Component, Prefab/Prototype, Spatial Partition, Flyweight via shared assets.) If yes, use the engine's version and stop.
2. **Is this a performance pattern?** (Object Pool, Dirty Flag, Data Locality, Double Buffer.) If yes, require a profiling reason before adding it.
3. **Is it global mutable state?** (Singleton, Service Locator.) If yes, prefer dependency injection; if unavoidable, make it resettable for tests.
4. **Otherwise**, prefer the Tier 1 patterns — and always keep the logic in plain C# so it's edit-mode testable.

When the plain-C# logic also needs to **read the world or cause side effects** (raycasts, neighbor queries, damage, spawning), don't inject an `IWorld` service into it — see [[functional-core-imperative-shell]]: pass query results in as data and return effects out as data, and let the `MonoBehaviour` shell do the I/O. In particular, never add a service interface whose only second implementation is a test double.
