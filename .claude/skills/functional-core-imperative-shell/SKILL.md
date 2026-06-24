---
name: functional-core-imperative-shell
description: How to structure a Unity gameplay/networked system as a pure "functional core" of plain functions wrapped by a thin MonoBehaviour "imperative shell" that does all I/O — and specifically how to avoid the trap of inventing an interface seam whose only second implementation is a test double. Biased for a SOLO Unity dev using the Processor + thin-MonoBehaviour pattern (logic in Arena.*.Core, glue in Pawn<X>) over FishNet prediction. Use this whenever a Core processor needs to read the world (raycasts, neighbor queries, player lookups) or cause world side effects (damage, spawn, despawn, VFX); when deciding between an injected `IWorld`-style seam and a data boundary; or when you catch yourself adding an interface "so it's testable." Consult BEFORE introducing a service interface that will only ever have one real implementation.
---

# Functional Core, Imperative Shell (curated for a solo, code-first, test-driven Unity dev)

This skill extends the project's Processor + thin-`MonoBehaviour` convention to the hard case: a
system whose logic must **read the world and cause side effects**, not just transform its own state.
Pawn movement is easy to keep pure — its input is a struct and its output is a struct. Projectiles,
abilities, AOE, sensors, and anything that casts rays / deals damage / spawns objects are harder,
because the obvious move is to inject a `IWorld` service into the Core so it can "ask the engine
things." That move is usually a mistake. This skill says what to do instead.

Honor the same standing biases as [[game-programming-patterns]] and [[integration-testing]]:

1. **Solo developer** — future-you maintains every abstraction alone.
2. **Code over editor.**
3. **Maintainable/extensible over performance** — spend complexity only to remove a coupling you can feel.
4. **Unity** — let the engine do engine things; don't wrap them in ceremony.
5. **Edit/play-mode tests** — testability is a goal, but a *test double is not a justification for an abstraction.*

---

## The governing rule

**Dependencies point inward, as data — not as a callback seam.**

A functional core is a set of **pure functions**: deterministic, no `Physics`, no `Time`, no FishNet,
no `MonoBehaviour`, no GameObject mutation. The imperative shell is a thin `MonoBehaviour` that does
**all** I/O. The two communicate only through plain values:

- The shell gathers whatever the core needs to know — raycast results, the nearest enemy's position,
  overlapping entities — and passes them **in as data** (a query-result struct).
- The core returns whatever it wants done — damage, spawns, despawns, VFX — **out as data** (a list of
  effect structs). It does not perform them.
- The shell reads the returned effects and executes them, applying any authority/replay gating.

```
        ┌──────────────────────── shell (MonoBehaviour) ────────────────────────┐
input → │  gather queries (cast/overlap/lookup)  →  call core  →  apply effects  │ → world
        └────────────────────────────────│──────────────────────│───────────────┘
                                     query data            effect data
                                          ↓                      ↑
                                   ┌──── functional core (pure) ────┐
                                   │  newState, effects = f(state,  │
                                   │            type, queries, dt)  │
                                   └────────────────────────────────┘
```

The call arrow points **shell → core** only. The core never holds, calls, or knows about the shell or
the engine. This is what makes the core trivially unit-testable with plain structs **and** keeps it
deterministic for FishNet prediction/reconcile (same inputs → same outputs, replayable).

This codebase's reference implementation is the projectile system: `PawnProjectile` (the shell) casts,
overlaps, finds targets, and applies damage/spawns; `Arena.Projectile.Core` (the functions) takes
`WorldHit`/overlap states in and returns a list of `ProjectileEffect` out. The shell decides *whether*
each effect runs (server vs client, fresh tick vs reconcile replay); the core just decides *what*
should happen.

---

## The anti-pattern this skill exists to prevent

**Do not introduce an interface whose only second implementation is a test double.**

The tempting shape is an injected service:

```csharp
// SMELL: a wide "world" interface the core calls into.
public interface IProjectileWorld {
    bool CastWorld(Vector3 from, Vector3 to, float r, out WorldHit hit);
    void ApplyDamage(int target, int amount, ...);
    int  Spawn(ProjectileType t, ...);
    void Despawn(int id);
    // ...9 methods...
}
ProjectileState Step(ProjectileState s, ..., IProjectileWorld world); // core CALLS the world
```

Three things are wrong with it, in increasing order of importance:

1. **Interface Segregation.** Most callers use a sliver of it. A `Ricochet` reaction that only reflects
   a velocity still receives the whole 9-method surface. The interface is wide because it's really a
   Facade over "everything stateful," which is a sign the boundary is in the wrong place.
2. **It only has one real implementation.** The production class and a `FakeWorld` are not two
   implementations earning a Strategy — the fake is a *consequence* of the interface, not a reason for
   it. "I need it to write a test double" is the weakest possible justification for an abstraction. If
   the seam would have exactly one production impl forever, you inverted a dependency you didn't need to.
3. **It forces the core out of its pure assembly.** This is the decisive one in *this* project. The
   `Arena.*.Core` asmdefs are engine-free and cannot reference `Assembly-CSharp` or FishNet. The moment
   the core *calls into* a service that touches `Physics`/FishNet, the core can no longer live in its
   pure assembly — it collapses into `Assembly-CSharp`, and its orchestration logic (the bug-prone
   gating / owner-safety / reconcile-dedupe code) becomes **PlayMode-only** to test. You trade cheap,
   exhaustive EditMode coverage of the riskiest code for slow, scenario-heavy PlayMode setup. That is
   backwards for an overhead-averse solo dev.

The data-boundary version has none of these problems: no interface, no fake, the core stays pure and
EditMode-testable, and the shell is the one concrete thing — tested (if at all) by a few integration
tests against the *real* engine, per [[integration-testing]]'s "test the seam you don't trust" rule.

**When an interface IS earned here:** genuine polymorphism with multiple *real* implementations. The
projectile *movement* strategies (`Linear`/`Arc`/`Homing`/`Laser`) are a legitimate `IProjectileMovement`
Strategy — four real behaviors selected at runtime, not one impl plus a mock. That interface stays.
The distinction: an interface earns its place when you'd have ≥2 production implementations even if you
never wrote a single test.

---

## How to apply it

1. **Find the I/O.** List everything the system reads from the world and everything it does to the
   world. Reads become **query inputs**; writes become **returned effects**.
2. **Split the data dependency into phases if needed.** Sometimes a query depends on a result the core
   just computed (e.g. you can't cast the collision ray until movement has produced the new position).
   Don't smuggle a callback in to bridge it — *phase* the core into functions the shell drives in order:
   `AdvanceMovement` → (shell casts) → `ResolveWorldHit` → (shell overlaps) → `ResolveProjectileHits`.
   One wide `Step(world)` becomes a slightly chattier pure API plus orchestration in the shell. That's
   the trade, and it's a good one: the chatter is all pure and testable.
3. **Return effects as value structs.** A small tagged struct (`ProjectileEffect { Kind, Target, Amount,
   Type, ... }`) accumulated into a reused `List<>`/buffer (no per-tick GC). The core appends; it never
   executes.
4. **Prefer making side effects unnecessary-to-gate over building a gate.** Gating only exists because
   the effect-producing code runs on more than one peer or re-runs during a reconcile replay. If you
   can make that code run in exactly ONE authoritative context, the gate disappears entirely — which is
   simpler than any gate. Concretely: if motion is deterministic (closed-form from spawn data), you
   don't need to predict-and-reconcile it per peer, so you don't need to run the side-effect-producing
   collision step anywhere but the server. Then the shell just applies every returned effect — there's
   no "should this run here?" question because it only ever runs on the server. **This codebase took
   exactly that route: an early design had an `EffectGate` (server/owner/replay matrix); making
   collisions server-authoritative-only deleted the need for it.** Only if you genuinely must predict
   effects on clients (e.g. predicted cosmetic VFX for feel) do you reintroduce gating — and then keep
   the *rule* pure (`ShouldApply(kind, isServer, isOwner, fresh)`) so it's unit-tested without a fake,
   rather than scattering `if (IsServer && !replaying)` through the shell.
5. **Shared/scene-level I/O can be a concrete `MonoBehaviour` service** (registry, prefab catalog,
   player lookup, child spawning). Let the shell call it directly — concrete, no interface. It's a
   collaborator with one real implementation; that's fine *because the shell, not the pure core, depends
   on it.* The rule is only that the **core** stays pure; the shell may freely use concrete engine
   objects.

---

## Testing shape

- **Functional core → EditMode unit tests.** Construct state + type + query-result structs, call the
  function, assert on the returned state and the returned effect list. No fake, no scene, milliseconds.
  This is *simpler* than maintaining a mock of a wide interface.
- **Gating rule (only if you have one) → EditMode unit test** of the pure helper (server/owner/replay matrix). If you eliminated gating by making effects server-authoritative-only (point 4), there's nothing to test here — which is the better outcome.
- **Shell → a few PlayMode integration tests**, only for the seams you don't trust (real cast hits a
  real collider, reconcile doesn't double-apply). Earn each one per [[integration-testing]]; most of
  what you'd reach for PlayMode to check is already covered by the core's EditMode tests.

---

## Smell checklist

- About to add an `interface IFooWorld` and the only implementors will be `FooWorld` + `FakeFooWorld`? **Stop.** Use a data boundary; the core returns effects, the shell does I/O.
- A "pure" Core processor takes a service parameter it *calls*? It isn't pure, and it can't stay in `Arena.*.Core`. Invert it: pass query results in, return effects out.
- An interface keeps growing methods as the system grows? It's a Facade over "the world" in disguise — the boundary belongs as data, not behavior.
- Reaching for PlayMode to test branchy decision logic (owner-safety, dedupe, any gating you kept)? That logic should be in the pure core and covered in EditMode; PlayMode is for the engine glue only.
- Conversely: about to collapse four real runtime behaviors into one class with a `switch` *just* to avoid an interface? An interface with ≥2 real implementations is earned — keep it (see `IProjectileMovement`).
