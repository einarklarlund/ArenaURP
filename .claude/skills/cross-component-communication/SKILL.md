---
name: cross-component-communication
description: How one gameplay system passes data to another WITHOUT coupling to its behaviour — with as FEW distinct mechanisms as possible (contribution collections, notifications, one manager wiring spot). Use whenever system A must read from or affect system B (buffs, knockback, temperature, score/match flow, HUD), before adding a serialized reference to call another component's methods, before wiring two managers, and ESPECIALLY before inventing a new pub/sub channel.
---

# Cross-component communication

**One problem:** system A must read from or contribute to system B *without coupling to how B works*, so adding C later doesn't force B to change.

**A shrinking solution set:** every distinct mechanism is a thing future-you must recognize and debug. Prefer an existing mechanism over a new one; actively look to collapse two into one. A new bespoke channel is a maintainability regression even when locally convenient — argue it explicitly or don't add it.

## The governing rule

**Reference a sibling for its DATA SURFACE, never its BEHAVIOUR; cross a composition boundary only through a notification.**

- ✅ Read a sibling's published state, or contribute to a collection it exposes on purpose (`weapon.Modifiers.Add(...)`).
- ❌ Call a sibling's logic, drive its lifecycle, or reach past its surface into its processor.
- ❌ Across managers / gameplay↔UI / non-siblings: only a notification the producer fires without knowing listeners.

Litmus: *if I deleted the consumer, would the producer's code change?* If yes, invert it.

## Family A — a contribution collection the producer folds (data, pulled)

The producer owns a public collection its **processor folds every tick**; contributors push their own entries. The producer never references a contributor.

```csharp
public readonly List<MovementModifier> Modifiers = new();      // PawnMovement owns the surface
foreach (var mod in input.Modifiers) speedMult *= mod.WalkSpeedMultiplier;  // processor folds blindly
movement.Modifiers.Remove(prev); movement.Modifiers.Add(next); // a contributor swaps its OWN entry
```

`PawnMovement.Modifiers`, `PawnWeapon.Modifiers`, and `PawnMovement.PendingImpulses` are one family — the only difference is lifetime (a modifier persists until its contributor removes it; an impulse is consume-once, cleared by the producer after folding). A `PassiveAbility`'s plain `MovementModifier`/`WeaponModifier` struct fields (`MovementBuff`/`WeaponBuff`) are this family's authored-data face: the buff IS a modifier; `PawnPassiveAbilities` owns the Remove-old/Add-new bookkeeping on every roster change. Contributors: `PawnTemperature`, `PawnMovementEffects`, `PawnPassiveAbilities`, `KnockbackReceiver`.

The hit-exchange components (`Damage`/`KnockbackDealer`/`HitstunDealer` and `Health`/`KnockbackReceiver`/`HitstunReceiver`) are this family too, folded at READ time rather than per tick: each carries a modifier list with a neutral identity, and each end folds only its OWN list — the dealer scales what it emits, the receiver scales what it accepts. The exchanges fire from component presence (dealer AND receiver both exist) via `HitResolver.ResolveExchanges` — no courier effect, no authored row (ADR 0002); the resolver performs no arithmetic of its own, which is what keeps the two ends ignorant of each other.

*Discipline:* a contributor owns only its own entries (never `Clear()` a producer's list — you'd wipe others). Every contribution struct has a **neutral identity** (multiplier `1f`, `Vector3.zero`) so an absent contributor changes nothing.

## Family B — a notification you subscribe to (event, pushed)

Something happened/changed; an open-ended set reacts; the producer doesn't know them. One family at three scopes — prefer the **narrowest** that reaches the listeners:

```csharp
health.OnDamageTaken += OnDamageTaken;         // instance C# event (collaborators of one object)
health.CurrentHealth.OnChange += OnChanged;    // SyncVar notification (server-synced state — but first ask
                                               // whether it should be synced at all, [[fishnet-conventions]])
NetworkUIEvents.OnMatchStateChanged?.Invoke(s);// static bus — genuinely cross-cutting UI ONLY
```

*Disciplines:* every `+=` has a matching `-=` in teardown (`OnDestroy`/`OnStopServer`…). A static bus MUST have a `Reset()` nulling every field, called on teardown/scene reload (`LocalUIEvents`/`NetworkUIEvents` both do) — a static event holder without one is a Service Locator in disguise and the #1 test-isolation bug. Never use the UI buses to connect two gameplay systems.

## The one wiring spot — MatchMediator

Managers never subscribe to each other. `MatchMediator` is the single place the manager graph is wired: it subscribes to each manager's events (`OnPlayerRegistered`, `State.OnChange`, `OnPawnKilled`, `OnGameModeEnded`, per-player `IsReady.OnChange`/`OnDespawn`) and calls the others in response. New cross-manager behaviour goes here, never as a manager-to-manager field.

## Decision heuristic

Most needs: **reuse the mechanism already present in the files you're editing.** Otherwise:

1. Standing or instant contribution to a per-tick simulation → **Family A** (extend an existing list; consume-once iff one-shot).
2. Thing-happened / value-changed → **Family B** at the narrowest scope; across managers → **MatchMediator**.
3. Just reading a sibling's state → a direct serialized ref to its published surface is fine.

The *content* still comes from a pure core: the modifier a contributor pushes is computed by a processor and EditMode-tested; the MonoBehaviour only plumbs it ([[functional-core-imperative-shell]]).

## Smells

- A new channel kind when an existing one stretches — or a second collection shape that's Family A with a different lifetime (unify).
- A calls a logic method on sibling B → invert; B exposes a surface or fires an event.
- A manager referencing a manager → move to MatchMediator.
- A contributor `Clear()`ing a shared list; a contribution whose absent-value isn't neutral.
- `+=` without `-=`; a static bus without `Reset()`.
- Shipping a value over the wire so another component can read it → [[fishnet-conventions]] Rule 1 first.
