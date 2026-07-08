---
name: functional-core-imperative-shell
description: Where gameplay logic GOES — in a pure functional core (Arena.*.Core processors), NOT in the thin MonoBehaviour shell (Pawn<X>) that only does I/O. Use this BEFORE adding or modifying ANY gameplay behaviour in a MonoBehaviour/NetworkBehaviour — decide where the logic lives first and default to the Core processor. Also use when a core must read the world (raycasts, overlaps, lookups) or cause side effects (damage, spawn, VFX) — pass query results IN as data and return decisions/effects OUT as data, never inject an IWorld seam whose only second implementation is a test double; when you catch yourself adding an interface "so it's testable"; and for shell hygiene: required serialized references get a Debug.LogError(…, this) null-guard that bails, never a silent no-op or a downstream NRE.
---

# Functional core, imperative shell

**Dependencies point inward, as data — not as a callback seam.** A core is pure functions: deterministic, no `Physics`/`Time`/FishNet/GameObject mutation. The shell is a thin MonoBehaviour doing ALL I/O. They communicate only through plain values:

- The shell gathers what the core needs (cast results, overlaps, lookups) and passes it **in as data**.
- The core returns what it wants done **out as data** — a new state, a classification, an effect list. It performs nothing.
- The call arrow is shell → core only. That's what makes the core EditMode-testable with plain structs and deterministic for networking.

House shape: `static Process(state, in config, in input, dt) → newState` in an `Arena.<X>.Core` asmdef; the `Pawn<X>` shell builds the input from `PawnInput.Data`, calls it, applies the result. Reference implementation for the hard case: the projectile — `ServerCollision` (shell) casts and overlaps; the pure `ServerCollisionProcessor` classifies (`TryClassifyWorldHit` → owner-safe enemy/surface/projectile); the shell then runs the Unity-coupled [[effect-commands]] the classification routes to. The core decides *what kind of hit happened*; the shell decides *what to do about it*.

## The anti-pattern: an interface whose only second implementation is a test double

The tempting shape is `interface IProjectileWorld { CastWorld(...); ApplyDamage(...); Spawn(...); … }` injected into the core. Three things are wrong, in increasing order:

1. **Interface segregation** — every caller receives the whole surface; the width betrays a Facade over "everything stateful".
2. **One real implementation** — the fake is a *consequence* of the interface, not a reason for it. "I need it to write a test double" is the weakest justification for an abstraction.
3. **It forces the core out of its pure assembly** — a core that *calls* a service touching Physics/FishNet can't live in `Arena.*.Core`, and its riskiest logic (gating, owner-safety, dedupe) becomes PlayMode-only to test. Backwards.

An interface IS earned by ≥2 *production* implementations (`IMotionStrategy`: Linear/Arc/Laser). The test: would you have two implementations even if you never wrote a test?

## How to apply it

1. **Find the I/O.** Reads become query inputs; writes become returned decisions/effects.
2. **Phase the core when a query depends on a computed result** (can't cast until movement produced the new position): `PositionAtAge` → shell casts → `TryClassifyWorldHit` → shell overlaps → `TryClassifyProjectileHit`. A slightly chattier pure API + shell orchestration beats a smuggled callback.
3. **Two shapes for writes, both pure:** return **effect data** (required inside predicted `[Replicate]` code, which re-runs on reconcile and must gate execution to the authoritative tick), or return a **classification** the shell maps to command objects (the server-only default here).
4. **Prefer deleting the gate over building one.** Gating exists only because effect-producing code runs on multiple peers or replays. Making collision server-authoritative-only deleted this codebase's `EffectGate` entirely — running in exactly one authoritative context beats any gate. If you must predict effects, keep the rule pure (`ShouldApply(kind, isServer, isOwner, fresh)`) and unit-test it.
5. **Shared scene services (registries, lookups) stay concrete** — the shell may call them directly; only the core must stay pure.

## Shell hygiene: guard required references, fail loud

A shell reference the object cannot function without (forgotten inspector slot, unwired variant, null `GetComponent`) must not fail as a silent `if (x == null) return;` nor as an NRE three frames later. **Guard it with `Debug.LogError` naming the field, pass `this` as context (clickable in the hierarchy), then bail** (`return`/`enabled = false`):

```csharp
if (type == null) {
    Debug.LogError($"{name}: TypeObjectRoot has no 'type' assigned — cannot configure itself.", this);
    return;
}
```

Guard "cannot function without", not everything — over-guarding optional fields trains you to ignore the console. Lifetime refs: guard once in `Awake`/`OnStartNetwork`. Per-tick resolves: guard at the call site, log once (cache a `_warned` bool). This is shell-only — a core's inputs are structs; there's nothing to null-guard.

## Testing shape & smells

- Core → EditMode: construct structs, call, assert on returned state/effects. Gating rule (if any) → EditMode. Shell → a few PlayMode tests for the seams you don't trust ([[integration-testing]]).
- Smells: a "pure" processor taking a service it calls; an interface growing methods as the system grows (a world-facade in disguise); reaching for PlayMode to test branchy decision logic (it belongs in the core); a required serialized ref with no loud guard. Conversely: collapsing ≥2 real behaviours into a `switch` just to avoid an interface — keep the interface.
