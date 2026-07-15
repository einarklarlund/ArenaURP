---
name: game-programming-patterns
description: Curated guidance from Nystrom's "Game Programming Patterns", biased for a solo Unity dev who favors maintainable, testable code over raw performance. Use when structuring or refactoring gameplay systems; organizing entity, AI, input, state, or event code; or choosing a design pattern (or talking the user out of one) — even if no pattern is named. Consult BEFORE reaching for Singletons, "manager" classes, deep inheritance, or premature optimization.
---

# Game Programming Patterns (curated)

Standing biases: solo dev (optimize for readable-in-six-months), code over editor, maintainability over performance, use the engine's version of a pattern, testability first-class.

**Rule 1 — Earn the pattern.** A pattern is debt taken on to buy flexibility. Add one only when it removes a coupling you can already feel; if you can't name the future change it protects, don't.

**Rule 2 — Keep logic out of MonoBehaviour.** Put real logic in plain C# classes; the MonoBehaviour is a thin adapter forwarding lifecycle/input. Plain classes are EditMode-testable; thick `Update()`s are not. In a networked game "plain class" also means *deterministic and side-effect-free* — every dependency in the signature (`static Process(state, in config, in input, dt) → newState`), no `Random.value`/`Time` reads inside. Same inputs → same outputs is what lets server and clients agree. This is the whole thesis of [[functional-core-imperative-shell]].

## Tier 1 — reach for these freely

- **Command** — an action as a first-class value (rebindable input, undo, queues, AI issuing player actions). Plain `ICommand`; the input MonoBehaviour only produces commands. This project's authored-behaviour realization is [[presence-dispatch]] — consult it for any family of swappable behaviour variants.
- **State (FSM)** — anything with modes (AI, control modes, game flow). A plain C# machine the MonoBehaviour ticks; the moment an object grows mutually-exclusive bool flags, make the states explicit. Never put *logic* in the Animator — drive it from the code FSM.
- **Component** — Unity's native structure; keep each component small and delegate its brain to a plain class. Cross-component data flow rules live in [[cross-component-communication]].
- **Observer** — announce "something happened" without knowing listeners. Plain C# `event`; subscribe in `OnEnable`, ALWAYS unsubscribe in teardown. Defer-in-time needs an Event Queue, not an Observer.
- **Type Object** — many kinds of a thing without a subclass per kind. House implementation (identity-only SO + prefab-per-kind): [[type-object-pattern]] — note it deliberately relaxes "differ by data only"; kinds differ in behaviour via prefab components and authored effect lists.

## Tier 2 — situational

- **Object Pool** — only after profiling shows GC/instantiation hitches. Use `UnityEngine.Pool.ObjectPool<T>` (or FishNet's pooling for NetworkObjects); the real work is reset-on-reuse — this project's rules are the baseline/per-spawn split in [[type-object-pattern]].
- **Event Queue** — decoupling in *time* (defer, smooth bursts). A plain queue drained in one explicit, manually-callable method.
- **Service Locator** — try passing the reference first. If unavoidable: registrable, replaceable, resettable. **Static event holders are Service Locators in disguise** — any `public static Action` bus needs a `Reset()` or it leaks subscribers across tests/scene reloads.
- **Subclass Sandbox** — a *closed* family built from a fixed toolkit of protected primitives. If the set is open-ended, prefer Type Object / composition.
- **Prototype/Spawning** — prefab + `Instantiate` *is* it; keep the what/when/where decision logic in a plain class.
- **Dirty Flag** — cache + `isDirty` for an expensive derived value recomputed more often than its inputs change; test the invalidation paths.
- **Flyweight** — Unity already shares meshes/materials/SOs; the discipline that matters here is "no per-instance state on shared objects" ([[presence-dispatch]], [[type-object-pattern]]).

## Skip list — don't implement these here

**Game Loop** / **Update Method** (the engine owns them — just keep `Update` thin), **Singleton** (global mutable state is the enemy of test isolation; prefer injection, and if a manager must be a scene singleton give it a `ResetInstance()`), **Bytecode** (over-engineering solo), **Data Locality** (trades away maintainability; a deliberate DOTS decision if ever), **Spatial Partition** (Unity physics queries already partition), **Double Buffer** (engine-handled; only for bespoke grid sims).

## Decision heuristic

1. Does Unity already provide it? Use the engine's version.
2. Is it a performance pattern? Demand a profiling reason.
3. Is it global mutable state? Prefer injection; make it resettable.
4. Otherwise Tier 1 — with the logic in plain C#, and world reads/side effects crossing the boundary as data ([[functional-core-imperative-shell]]), never via an interface whose only second implementation is a test double.
