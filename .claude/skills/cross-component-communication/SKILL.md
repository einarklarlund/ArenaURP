---
name: cross-component-communication
description: How one gameplay system passes data to another WITHOUT coupling to its behaviour — with as FEW distinct mechanisms as possible (contribution collections, notifications, one manager wiring spot). Use whenever system A must read from or affect system B (buffs, knockback, temperature, score/match flow, HUD), before adding a serialized reference to call another component's methods, before wiring two managers, and ESPECIALLY before inventing a new pub/sub channel.
---

# Cross-component communication

**One problem:** system A must read from or contribute to system B *without coupling to how B works*, so adding C later doesn't force B to change.

**A shrinking solution set:** every distinct mechanism is a thing future-you must recognize and debug. Prefer an existing mechanism over a new one; actively look to collapse two into one. A new bespoke channel is a maintainability regression even when locally convenient — argue it explicitly or don't add it.

## The governing rule — the layered model (ADR 0005)

**Pick the fewest mechanisms, by the SHAPE of the communication:**

- **Fan-out context** (many unrelated systems respond to one event) → a pooled, typed `ExecuteEvents` **message** — but ONLY at all four clauses: **≥2 unrelated responders, on the dispatch target's own GameObject, on the same peer, served synchronously.** "Hit" (`HitMessage`) is the one context that qualifies. Drop any clause and it is *not* a message:
  - **cross-peer fan-out rides a replicated `SyncVar.OnChange`** (ADR 0004) — `ExecuteEvents` is in-process and reaches no other peer;
  - **off-hierarchy fan-out stays a C# event or a `MatchMediator` wire** — a message is GameObject-scoped (`ExecuteEvents` calls `GetComponents` on ONE GO), while a C# event is reference-scoped and reaches a child, parent, or other scene.
  - (Why all four: the one-clause form greenlit promoting `PawnManager.OnPawnKilled`, which would silently break the client ragdoll — its responders are peer-disjoint. See ADR 0005.)
- **Single-receiver operation** (one component's capability, a known receiver) → a **direct call into that component's public operation API** — contribute to a collection it exposes on purpose (`weapon.Modifiers.Add(...)`), or call a controller's layer-3 operation (`ApplyKnockback`/`TakeDamage`). A message with a known singular receiver is dispatch theater.
- ❌ Still forbidden: reach **past** a component's public surface into its processor/internals, or drive its lifecycle.
- ❌ Across managers / gameplay↔UI / non-siblings: only a notification the producer fires without knowing listeners.

Litmus: *if I deleted the consumer, would the producer's code change?* If yes, invert it. (The layered model supersedes the old "never call a sibling's behaviour": a controller's public layer-3 operation, called from a typed layer-2 handler, is legitimate and named — reaching past the surface into internals is not.)

## Family A — a contribution collection the producer folds (data, pulled)

The producer owns a public collection its **processor folds every tick**; contributors push their own entries. The producer never references a contributor.

```csharp
public readonly List<MovementModifier> Modifiers = new();      // PawnMovement owns the surface
foreach (var mod in input.Modifiers) speedMult *= mod.WalkSpeedMultiplier;  // processor folds blindly
movement.Modifiers.Remove(prev); movement.Modifiers.Add(next); // a contributor swaps its OWN entry
```

`PawnMovement.Modifiers`, `PawnWeapon.Modifiers`, and `PawnMovement.PendingImpulses` are one family — the only difference is lifetime (a modifier persists until its contributor removes it; an impulse is consume-once, cleared by the producer after folding). A `MovementPassive`/`WeaponPassive`'s serialized `MovementModifier`/`WeaponModifier` is this family's authored-data face: the buff IS a modifier, added to `PawnMovement.Modifiers`/`PawnWeapon.Modifiers` when `PawnPassiveLoadout.ApplyOwnedTo` runs the owner's loadout at pawn spawn (ADR 0009; no per-tick re-derive — removal cleanup is deferred, so a stolen pawn buff lingers until respawn). Contributors: `PawnTemperature`, `PawnMovementEffects`, the applied `MovementPassive`/`WeaponPassive`, `KnockbackController`.

The per-datum controllers (`DamageController`, `KnockbackController`, `HitstunController`, `HealthController`) are this family too, folded at READ time rather than per tick: each owns two modifier registries with a neutral identity (`OutgoingModifiers` / `IncomingModifiers`), and each half folds only its OWN — the dealer half scales what it emits, the receiver half scales what it accepts. The exchange fires when the dealer half (a layer-2 `IHitMessageHandler`) responds to a `HitMessage` and makes a direct call into the victim's controller (ADR 0005) — no courier effect, no authored row (ADR 0002); the two ends stay ignorant of each other because neither reads the other's registry.

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

When a contribution's content is determinism-critical or branchy, it comes from a kept pure core: the modifier a contributor pushes is computed by a processor and EditMode-tested; the MonoBehaviour only plumbs it. Otherwise the component computes it directly ([[component-design]]).

## Smells

- A new channel kind when an existing one stretches — or a second collection shape that's Family A with a different lifetime (unify).
- A calls a logic method on sibling B → invert; B exposes a surface or fires an event.
- A manager referencing a manager → move to MatchMediator.
- A contributor `Clear()`ing a shared list; a contribution whose absent-value isn't neutral.
- `+=` without `-=`; a static bus without `Reset()`.
- Shipping a value over the wire so another component can read it → [[fishnet-conventions]] Rule 1 first.
