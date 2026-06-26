---
name: cross-component-communication
description: How one gameplay system passes data to another WITHOUT coupling to its behavior — and, more importantly, the standing goal of solving that with as FEW distinct communication mechanisms as possible. The codebase currently has several (producer-owned contribution lists, Observer events, the MatchMediator, static UI buses, SyncVar.OnChange); these are EXAMPLES of solutions to one problem, not rules to conform to, and we are actively consolidating toward fewer. Biased for a SOLO, code-first Unity dev using the Processor + thin-MonoBehaviour pattern over FishNet. Use this whenever a feature needs system A to read from or affect system B (movement debuffs, knockback, temperature, damage reactions, score/match flow, HUD); whenever you're about to add a serialized reference from one component to another and call its methods; or when wiring two managers together. Consult BEFORE making A call into B's logic, before adding a cross-manager reference, and ESPECIALLY before inventing a new pub/sub channel — default to reusing an existing one and look for a chance to collapse two into one.
---

# Cross-Component Communication (curated for a solo, code-first, test-driven Unity dev)

This skill governs the **horizontal** boundary: how one gameplay system gets data to or from another.
It is the sibling of [[functional-core-imperative-shell]] (the *vertical* boundary — a shell and its
own pure processor) and a specialization of [[game-programming-patterns]]'s Observer / Mediator /
Component entries.

Honor the same standing biases: solo dev, code over editor, maintainability over performance, let the
engine be the engine, keep the decision-making logic in a pure EditMode-testable core.

---

## There is ONE problem, and a set of solutions we want to SHRINK

**The problem (the invariant):** system A must read from, or contribute to, system B *without coupling
to how B works* — so that adding C later doesn't force B to change.

**The solutions (contingent, and currently too many):** this codebase has accreted several distinct
communication mechanisms. They are **examples of how this problem has been solved so far — not an API
to conform to, and not a menu to expand.** Every distinct mechanism is a thing future-you must
recognize, debug, and hold in their head. So the standing goal is:

> **Solve cross-component needs with as FEW distinct mechanisms as possible. In every refactor and new
> feature, prefer an existing mechanism over a new one, and actively look for a chance to collapse two
> mechanisms into one.**

This is a direction of travel, not a finished state. When you touch code that uses one of these
mechanisms, the default question is *"can this need be served by a mechanism already present nearby,
and can these two be unified?"* — not *"which of the six do I pick?"* A new bespoke channel is a
regression in maintainability even when it's locally convenient; justify it explicitly or don't add it.

---

## The governing rule (independent of which mechanism)

**Reference a sibling for its DATA SURFACE, never for its BEHAVIOR. Cross a composition boundary only through a notification.**

"Avoid direct references" is too blunt — sibling components on a `Pawn` *do* hold serialized references
to each other, and that's fine. The rule is about **what you reference them for**:

- ✅ read a sibling's published state (`movement.State`, `movement.Config`), or contribute to a
  collection it exposes on purpose (`weapon.Modifiers.Add(...)`). You touch a stable *surface*.
- ❌ call a sibling's *logic*, drive its lifecycle/update order, or reach past its surface into its
  processor. That couples you to *how* it works.
- ❌ across a composition boundary (manager↔manager, gameplay↔UI, non-siblings): go through a
  **notification** the producer fires without knowing who listens — wired, for managers, in one place.

Litmus test: *if I deleted the consumer, would the producer's code change?* If yes, invert it — the
producer exposes a surface or fires a notification; the consumer opts in.

---

## The two families these mechanisms actually reduce to

The codebase looks like it has six mechanisms. It really has **two families plus one wiring spot** —
and seeing that is the first step toward fewer. Read the existing mechanisms as *members* of a family,
so that when you add or refactor, you reach for the family, not a new variant.

### Family A — a contribution collection the producer folds (data, pulled)

The producer owns a public collection and its **processor folds it every tick**; contributors add
their own entry. The producer never references a contributor — that's the inversion that makes new
contributors purely additive.

```csharp
// Producer owns + exposes the surface (PawnMovement.cs:24)
public readonly List<MovementModifier> Modifiers = new();

// Its processor folds blindly; identity element = 1f, so an absent contributor is a no-op
foreach (var mod in input.Modifiers) walkSpeedMult *= mod.WalkSpeedMultiplier;   // MovementProcessor.cs

// A contributor swaps its OWN previous entry each tick (PawnTemperature.cs)
movement.Modifiers.Remove(prevMove);
movement.Modifiers.Add(state.MovementModifier);
```

`PawnMovement.Modifiers`, `PawnWeapon.Modifiers`, and `PawnMovement.PendingImpulses` are **the same
family.** The only difference is lifetime: a modifier persists until its contributor removes it; an
impulse is consume-once (the producer `PendingImpulses.Clear()`s after folding it, `PawnMovement.cs:69`).
That's a *flag on one mechanism* — "does the producer clear it each tick?" — not two mechanisms. When
you need a new standing-or-instant contribution to a system's per-tick sim, extend this family; don't
invent a third collection shape.

*Discipline:* a contributor owns only its own entries (remove-then-add the same struct — never
`Clear()` the whole list, you'd wipe others). The contribution struct must have a **neutral identity**
(multiplier `1f`, offset `Vector3.zero`) so a missing contributor changes nothing.

### Family B — a notification you subscribe to (event, pushed)

Something *happened* or *changed*; an open-ended set of systems react; the producer doesn't know them.
The codebase's "different" notification types are all this one family:

```csharp
pawn.OnDamageTaken += OnDamageTaken;          // instance C# event   (Pawn.cs:21)
pawn.Health.OnChange += OnHealthChanged;      // SyncVar notification (server-synced state)
LocalUIEvents.OnTemperatureChanged?.Invoke(t);// static bus           (cross-cutting → UI)
```

They differ only by *scope*: an instance event (one object's collaborators), a `SyncVar.OnChange`
(networked state — but first ask whether it should be synced at all, [[fishnet-conventions]]), or a
process-wide static bus (genuinely cross-cutting UI signals only). Prefer the **narrowest** scope that
works — an instance event over a static bus — because narrower scope is less global state to reset and
reason about. Reach past an instance event to the static bus *only* when the listener genuinely can't
hold a reference to the producer (decoupled UI).

**Two mandatory disciplines for Family B:**
- Every `+=` has a matching `-=` in teardown (`OnDestroy`/`OnDisable`/`OnStopServer`). Forgetting is
  the classic Unity leak/null-ref.
- A static bus MUST have a `Reset()` that nulls every field, called on teardown/scene-reload
  (`LocalUIEvents.Reset()` exists for this). A static event holder without a reset is a
  Service-Locator-in-disguise and the #1 test-isolation bug in this style — see
  [[game-programming-patterns]].

### The one wiring spot — MatchMediator (not a third family)

When Family B crosses a *manager* boundary, don't let managers subscribe to each other. **MatchMediator**
is the single place the manager graph is wired: it subscribes to each manager's events and calls the
others in response. New cross-manager behavior goes *here*, never as a manager-to-manager field.

```csharp
// MatchMediator.cs — one place owns the graph
networkPlayerManager.OnPlayerRegistered += ServerHandlePlayerRegistered;
private void ServerHandlePlayerRegistered(NetworkPlayer p) {
    if (matchFlowManager.State.Value == MatchState.During)
        pawnManager.SpawnPawnForPlayer(p);   // mediator calls a DIFFERENT manager
}
```

---

## The decision heuristic (deliberately tiny)

Most needs are answered in one line: **reuse the mechanism already present in the files you're editing.**
Only when none fits:

1. A *standing or instant contribution* to another system's per-tick simulation → **Family A**
   (extend an existing contribution list; clear-each-tick iff it's one-shot).
2. A *thing-happened / value-changed* others react to → **Family B**, at the narrowest scope that
   reaches the listeners. Across managers, wire it in **MatchMediator**.
3. Just reading a sibling's current state → a direct serialized ref to its **published surface** is
   fine; that isn't the coupling we avoid.

If a need seems to demand something outside both families, treat that as a finding, not a green light:
state out loud what new surface area it would add and why no existing mechanism stretches to cover it.
Adding a new mechanism is the exception that has to be argued, because it moves the codebase the wrong
way on the one metric this skill optimizes — *how many distinct ways things talk to each other.*

---

## Keep the *decision* logic pure (so it's EditMode-testable)

This skill is about plumbing; the *content* flowing through it still comes from a pure processor. The
modifier a contributor pushes is **computed by a `Processor`** and tested in EditMode by asserting on
the returned struct — `TemperatureProcessor.Process(...)` returns the `MovementModifier`;
`PawnTemperature` only does the `Remove`/`Add`. So: **what** to communicate → pure core, EditMode test
([[functional-core-imperative-shell]]); **how** it's wired → thin MonoBehaviour, covered (if at all) by
a focused integration test for the seam you don't trust ([[integration-testing]]). Most wiring is too
thin to test; the contribution *math* is what gets tested.

---

## Smell checklist

- **About to add a new kind of channel** (a fresh static event, a new bespoke list shape, a new
  manager-to-manager hookup)? Stop — can a mechanism already in these files serve it? Can adding it
  instead *collapse* two existing mechanisms? Proliferation is the regression this skill guards against.
- Component A calls a *logic method* on sibling B (not reading its state, not adding to its list)?
  Invert it — B exposes a surface or fires a notification.
- A manager references another manager? Move the wiring to **MatchMediator**.
- A second contribution-list shape that's really Family A with a different lifetime? Unify it onto the
  existing family with a clear-each-tick flag instead of a new type.
- A contributor calls `.Clear()` on a producer-owned list? It's wiping other contributors — remove only
  its own previous entry.
- A contribution struct whose "absent" value isn't a no-op (multiplier defaulting to `0`, not `1`)? The
  fold breaks when that contributor is missing — give it a neutral identity.
- Reaching for the static UI bus to connect *two gameplay systems*? That bus is cross-cutting UI only —
  use Family A, or a narrower instance event.
- A `+=` with no matching `-=`, or a `public static Action` with no line in a `Reset()`? Leak / test-
  isolation bug — fix before moving on.
- Shipping a value over the network so another component can read it? [[fishnet-conventions]] first —
  derive it on the peer that needs it.
- The *content* of the message computed inside the MonoBehaviour rather than a processor? Move it into
  the Core so it's EditMode-testable; the MonoBehaviour only plumbs it.
