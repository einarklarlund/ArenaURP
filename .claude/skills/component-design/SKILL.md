---
name: component-design
description: How we build components — the component (Unity MonoBehaviour composition) is the primary unit of logic; generic-via-config and flat-hierarchy are the two authoring guidelines; extract a pure Arena.*.Core processor + EditMode test only for determinism-critical or heavily-branchy logic. Use BEFORE adding or modifying gameplay behaviour on a MonoBehaviour/NetworkBehaviour, deciding where a component's config lives, shaping the GameObject hierarchy, or deciding whether logic earns a pure core. Pairs with [[presence-dispatch]] and [[type-object-pattern]].
---

# Component design — the component is the primary unit

**Logic goes in the component by default.** Unity's composition — a GameObject carrying MonoBehaviours that find each other by presence — *is* our architecture, not a layer to escape. This is the Elegance north star's "engine-native beats owned": prefer the engine's own composition to a parallel structure we'd have to maintain. A `Pawn<X>` updater, a receiver, a handler — each is a component that reads its serialized refs, does its work, and contributes to or notifies its siblings ([[cross-component-communication]]).

Two authoring guidelines shape *how* a component is written, and one exception says *when* to pull logic out into a pure core.

## Guideline A — generic-via-config

**A component is agnostic to concretions; its *presence* defines a capability, never a specific object or situation. Specifics come from serialized configuration.**

The tell of a violation: a component (or a branch inside it) that only makes sense for *one* object, level, or scenario — a `SpawnTheBossOnFloor3` component, an `if (name == "Arena2")`. That knowledge belongs in **data on the component**, so the same component serves every case and the difference is authored, not coded. Make the component as generic as the cases in front of it require — no more (rule of two: don't generalize past the second real case), no less (don't bake a concretion into code a config field could carry).

This is the same split as [[presence-dispatch]], one level down: *behaviour that varies* is a **component** discovered by `GetComponents`; *behaviour that varies only in data* is **authored config** on that component (the `[[type-object-pattern]]` prefab copy carries it). Guideline A is that rule applied to a single component's own body — generality lives in config, not in special-case code.

Breakable in specific cases (a genuinely one-off orchestration component with no second case in sight is fine — the rule of two cuts both ways), but generic-via-config is the default because it is what makes composition modular.

## Guideline B — flat hierarchy

**Keep the GameObject hierarchy as flat as possible; co-locate components that communicate on the *same* GameObject.**

Presence-dispatch resolves siblings with `GetComponents` (same object) or `GetComponentInParent` (a walk up from a struck collider). Both are cheapest and most predictable when communicating components sit together. Deep nesting scatters a conversation across transforms and forces wider, more fragile walks.

The existing root-placement fact generalizes here: the hit-RECEIVING systems (`Health`, `KnockbackReceiver`, `HitstunReceiver`) sit on the entity **root** precisely so one `GetComponentInParent` walk from any child collider resolves them all. When you add a component that talks to others, put it where its interlocutors already are — don't introduce a child object unless the engine forces one (a separate collider, a distinct renderer/transform that must move independently).

## The exception — extract a pure `Arena.<X>.Core` processor

Pull logic out of the component into a pure, static, engine-free `Arena.<X>.Core` processor (with an EditMode test) **only when it is one of:**

- **Determinism-critical** — the logic is re-run on multiple peers or on reconcile, so a regression is a **silent peer desync** a PlayMode host typically won't catch. An EditMode test on the pure function catches it cheaply. This is the reference case: the projectile — `ServerCollision` (shell) casts and overlaps; the pure `ServerCollisionProcessor` classifies (`TryClassifyWorldHit` → owner-safe enemy/surface/projectile); the shell then runs the Unity-coupled [[presence-dispatch]] the classification routes to. The core decides *what kind of hit happened*; the shell decides *what to do about it*. The kept cluster: `MovementProcessor`, `WeaponProcessor`, `ServerCollisionProcessor`, `HitstunProcessor`, the `NetworkedMotion` strategies, `SpawnFanMath`, `Deflect`/`RicochetMath`.
- **Heavily branchy** — state machines, classification, geometry: logic whose correctness is *decided* rather than observed, where reaching for a live GameObject to test a decision is backwards. The bot AI is the standing example (`BotBrainLogic`/`BotStateMachine`/`BotInputMath`/`BotVisionMath`), plus `RespawnProcessor`, the match timer/countdown processors, `AbilityInventoryLogic`, `TemperatureProcessor`.

**Reuse is NOT a reason.** Nearly every processor has exactly one shell caller; the split does not earn its keep by sharing. What earns it is **test-isolation of math that is otherwise hard to test** — determinism you can't see fail, and branchy decisions a PlayMode test can't cheaply pin. If the logic is a clamp, a lerp, or a one-line transform with no branchiness and no cross-peer replay, **leave it in the component** — a pure core there is ceremony, and the test asserts a round-trip no one will break.

Two thin-looking cores are kept as **guards, not ceremony**, and are not evidence against this rule: `UIStateMapper` enforces `switch` exhaustiveness in an engine-free assembly (Unity's compiler won't fail the build on a missing case — the [[unity-ignores-editorconfig]] scar), and `DamageProcessor` is the deliberately-empty seam for the deferred damage-buff design.

When you *do* extract a core, its shape is `static Process(state, in config, in input, dt) → newState`: the shell gathers what the core needs (cast results, overlaps, lookups) and passes it **in as data**; the core returns what it wants done **out as data** (a new state, a classification, an effect list) and performs nothing. The call arrow is shell → core only — that is what keeps the core in `Arena.*.Core` (no UnityEngine/FishNet reference) and EditMode-testable with plain structs.

### The anti-pattern that forces the exception wrong: an interface whose only second implementation is a test double

Do not turn a core into `interface IProjectileWorld { CastWorld(...); ApplyDamage(...); Spawn(...); … }` injected into the processor. Three things are wrong, in increasing order:

1. **Interface segregation** — every caller receives the whole surface; the width betrays a Facade over "everything stateful".
2. **One real implementation** — the fake is a *consequence* of the interface, not a reason for it. "I need it to write a test double" is the weakest justification for an abstraction ([[no-speculative-interfaces]]).
3. **It forces the core out of its pure assembly** — a core that *calls* a service touching Physics/FishNet can't live in `Arena.*.Core`, and its riskiest logic (gating, owner-safety, dedupe) becomes PlayMode-only to test. Backwards.

An interface IS earned by ≥2 *production* implementations (`IMotionStrategy`: Linear/Arc/Laser). The test: would you have two implementations even if you never wrote a test? Instead of a callback seam, **phase the core** when a query depends on a computed result: `PositionAtAge` → shell casts → `TryClassifyWorldHit` → shell overlaps → `TryClassifyProjectileHit`. A chattier pure API + shell orchestration beats a smuggled callback. Shared scene services (registries, lookups) stay concrete — the shell calls them directly; only the core must stay pure.

## Shell hygiene: guard required references, fail loud

This is component-authoring advice, not core-specific — it applies to every component with a reference it cannot function without. A forgotten inspector slot, an unwired variant, a null `GetComponent` must not fail as a silent `if (x == null) return;` nor as an NRE three frames later. **Guard it with `Debug.LogError` naming the field, pass `this` as context (clickable in the hierarchy), then bail** (`return`/`enabled = false`):

```csharp
if (type == null) {
    Debug.LogError($"{name}: TypeObjectRoot has no 'type' assigned — cannot configure itself.", this);
    return;
}
```

Guard "cannot function without", not everything — over-guarding optional fields trains you to ignore the console. Lifetime refs: guard once in `Awake`/`OnStartNetwork`. Per-tick resolves: guard at the call site, log once (cache a `_warned` bool).

## Testing shape & smells

- **Component behaviour → PlayMode** for the seams you don't trust (lifecycle, wiring, SyncVar replication, real physics, multi-frame timing — [[integration-testing]]).
- **Extracted core → EditMode**: construct structs, call, assert on returned state/effects. Any gating rule → EditMode. This is the whole point of the extraction — don't extract, then re-verify through a live GameObject.
- **Smells that say "extract a core"**: reaching for PlayMode to test branchy decision logic (it belongs in a core); determinism-critical math with no EditMode test guarding it against silent desync.
- **Smells that say "you over-extracted"**: a `*Processor` that is a single clamp/lerp with one caller and no branchiness (fold it back into the component); a "pure" processor taking a service it calls; an interface growing methods as the system grows (a world-facade in disguise). Conversely — collapsing ≥2 real behaviours into a `switch` just to avoid an interface is the opposite error: keep the interface.
