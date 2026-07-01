---
name: scriptableobject-commands
description: How to encapsulate a single piece of SWAPPABLE BEHAVIOUR as a COMMAND/STRATEGY whenever you have many interchangeable variants of one behaviour that share a common context/signature and you don't want to write a whole script per variant. Applies far beyond the Type Object pattern: hit reactions, ability effects, movement strategies, target providers, AI behaviours, scoring/win rules. KEY DISCRETION CALL — SO asset vs plain-C# class — decided by WHO composes the command: a human dragging it as data in the inspector (swapped/shared across consumers) → ScriptableObject asset (the default for movement strategies, target providers, AI behaviours, over [SerializeReference] or enum+switch); the OPERATION family (projectile hit reactions, ability effects, projectile motion) → `[SerializeReference,SubclassSelector]` context-typed commands (`IEffect<TContext>`) authored per-asset in the inspector via the MackySoft SerializeReferenceExtensions package — NEITHER SO-per-effect NOR code-composed; chosen for per-asset inspector authoring + compile-time context-safety (the command-context refactor reversed the earlier code-composed capability-context design). Covers: that discretion test; the stateless-vs-stateful split (CreateRuntime() factory only when per-instance runtime state is needed, like MovementStrategySO); concrete context-per-site (a command names the one `IEffectContext` type it runs in — ProjectileSurfaceCollision/ProjectilePlayerCollision/ProjectileProjectileCollision/AbilityAim — so the data it needs is compile-time guaranteed, no `is` capability query and no silent no-op; a multi-site command implements `IEffect<T>` per site; category≠capability, now expressed as separate context types + separate typed lists); defaults composed in code via feature flags with runtime contribution through the owner-lookup channel; flyweight discipline (no per-invocation state — ctor config or serialized config only); where parameters live; and WHO touches the Unity API — effects are the deliberate engine-coupling layer doing direct I/O on the context's GameObjects (no provider/data-struct seam; cores stay pure with non-trivial math in *.Core helpers; emit effect data only for predicted [Replicate] code). Biased for a SOLO, code-first Unity dev using the Processor + thin-MonoBehaviour pattern over FishNet. Use this BEFORE adding a new behaviour variant, choosing SO vs plain-class vs [SerializeReference] vs enum, deciding how a command gets the data it needs, or deciding whether a behaviour emits effect-data or calls the world directly. Pairs with [[type-object-pattern]] (a common consumer) and [[functional-core-imperative-shell]] (where the pure computation goes).
---

# ScriptableObject commands & strategies (curated for a solo, code-first Unity dev)

**Reach for an SO command/strategy whenever you have many interchangeable variants of one behaviour that
share a common context/signature, and you want to share and swap them *as data* without writing a whole
script per variant.** The behaviour has a fixed shape — a method plus the context it runs in — and the
variants differ in what they do inside it. Encapsulating each variant as a tiny SO asset is a very general
form of encapsulation; it is *not* tied to the Type Object pattern.

It does **compose** with [[type-object-pattern]] — a type's config often holds a list/slot of these (a
projectile's hit reactions, its movement strategy) — but it applies anywhere behaviour is shared and
swapped: hit reactions, ability effects, movement strategies, **target providers** (`TargetProviderSO`),
AI/steering behaviours (`SeekStrategySO`, `LinearVelocitySO`), interaction handlers, scoring/win rules.
The repo precedent is `MovementStrategySO` (`CreateRuntime() → IMovementStrategy.Tick`), hosted by
`MovementController`: a thin component runs whatever strategy SO it's handed. Author the rest the same way
and the codebase tells one story.

This is the house realization of the **Command / Strategy** patterns from [[game-programming-patterns]],
with [[functional-core-imperative-shell]] governing where the *pure computation* inside a command lives.

**When it fits:** a *family* of interchangeable variants sharing one context/signature, swapped as data.
**When it doesn't:** a one-off behaviour with no siblings (just write the method/component) — the SO
machinery only pays off when variants are many and swappable.

## Default: an SO per behaviour, not `[SerializeReference]` or an enum+switch

Three ways to make behaviour pluggable in the inspector; the house default is **SO assets**:

| | `[SerializeReference]` inline | **SO asset** (default) | enum + `switch` |
|---|---|---|---|
| Reuse across consumers | none (private copy per list) | **one asset reused, tuned once** | n/a |
| Refactor safety | fragile (managed-ref breakage) | **GUID-stable** | brittle switch |
| Tooling | dropdown, no preview | first-class asset; MCP `find_references` | — |
| Per-context typing | flat `List<HitReaction>` | **typed lists, no mis-slot** | — |
| Add a variant | a class | a class + `[CreateAssetMenu]` + an asset | class + enum + switch case |
| Per-instance config | trivial inline | needs a variant asset or context | n/a |

`[SerializeReference]` trades away cross-consumer reuse and refactor-stability (managed-ref breakage), so for
human-authored, SHARED strategies (movement, target providers, AI behaviours) the **SO asset** default stands.
But for the per-asset **OPERATION family** (hit reactions, ability effects, projectile motion) we now
deliberately USE `[SerializeReference]` + `[SubclassSelector]` (the MackySoft package): each type authors its
OWN operations, so there's no cross-consumer asset to share, and the payoff is per-asset inspector authoring +
compile-time context-typing — see the discretion test below and the Operations section. An `enum`+`switch`
(the old `MotionKind`/`MovementKind`) is worse than either — three edits and a switch per variant.

### SO asset vs. plain-C# command — use discretion; the test is WHO composes it

Not every command earns an SO asset. Decide per command by asking **who assembles it**:

- **A human, as data, in the inspector — swapped/shared across many consumers** → **SO asset.** This is the
  default above (movement strategies, target providers, AI behaviours: `MovementStrategySO` et al.). The
  asset buys inspector authoring, GUID-stable reuse, `find_references`, central tuning. Worth the ceremony.
- **The OPERATION family (projectile hit reactions, ability effects, projectile motion) → `[SerializeReference]`
  context-typed command, authored per-asset.** Post command-context refactor these are NEITHER an SO asset per
  variant NOR code-composed: each is a plain `[System.Serializable]` class implementing `IEffect<TContext>`,
  selected in the inspector via `[SubclassSelector]` on a typed list on the owning type (`ProjectileType`,
  `ActiveAbilityType`, `PassiveAbilityType`). Chosen because a human DOES author them per-asset (so plain
  code-composed defaults were the wrong call) but they are per-type, not shared across consumers (so a
  GUID-stable SO asset buys nothing). The `[SerializeReference]` per-copy tradeoff is therefore free here. See
  the Operations section below.

The smell test: *"Would a human drag this command into a slot?"* Yes, AND it's shared/reused across many
consumers → **SO asset** (movement strategies, target providers). Yes, but authored per-asset on its owning
type (each type its own) → **`[SerializeReference]` context-typed command** (the operation family). Never
touched by a human, always assembled by code → **plain class**.

**If your reason for reaching for an SO is "I want to pick and tune effects in the inspector on the
ability/type, without recompiling" — that is NOT a reason to make an SO-per-effect.** It's a reason to put
`[SerializeReference, SubclassSelector]` on the parent type's effect list (the MackySoft picker this project
already uses for its command/operation objects). That gives you an inspector dropdown of effect *classes*,
each with its own per-slot params, with no separate asset per effect. The trade-off is that
`[SerializeReference]` is fragile across renames/moves and each list holds a *private copy* (no cross-consumer
sharing) — which is exactly fine here, precisely because effects aren't meant to be shared assets in the first
place. (Reach for an SO-per-variant only when the command genuinely IS a shared, reused asset — a movement
strategy tuned once and dropped on many entities — not merely because you want inspector authoring.)

## Stateless → call the SO directly. Stateful → make it a factory.

- **Stateless** (most reactions, simple commands): the SO *is* the runtime behaviour. Call its method
  directly, passing a context. Pure flyweight, shared by all callers.
- **Stateful** (a strategy that accumulates per-instance runtime state — a homing tracker): the SO is a
  **factory** — `CreateRuntime()` yields a runtime object that owns the state. This is exactly what
  `MovementStrategySO` does, and it's the escape hatch for "SOs are shared, so they can't hold per-instance
  state."

## Operations: context-typed commands, authored as data via `[SerializeReference]`

This is the landed shape of the operation family (the old context-agnostic `IEffect`, now the context-typed `IEffect<TContext>` — the
**command-context refactor** reversed the earlier "code-composed, capability-context" design). Four parts:

**1. The command is TYPED to the exact context it runs in; config is `[SerializeField]` fields (+ a ctor).**
The command names the one context it operates on, so the caller is compile-time forced to supply it and the
command reads its data directly — no `ctx is X` capability query, no silent no-op.
```csharp
public interface IEffect<in TContext> where TContext : IEffectContext { void Apply(TContext ctx); }

[System.Serializable]
public sealed class DealDamage : IEffect<ProjectilePlayerCollision> {   // only authorable where a player was hit
    [SerializeField] bool useInvokerDamage = true; [SerializeField] int amount = 10; [SerializeField] bool despawnAfter = true;
    public DealDamage() {}  public DealDamage(bool useInvokerDamage, int amount = 10, bool despawnAfter = true) { ... }
    public void Apply(ProjectilePlayerCollision ctx) { ... ctx.Target ... }   // Target GUARANTEED — no null-check
}
```
`[SerializeField]` fields are what the inspector authors; the ctor is the code counterpart (tests, the static
`Commands` factory). Same flyweight rule as an SO command: hold only immutable config, no per-invocation state.

**2. A concrete context type per invocation SITE — not capability interfaces.** Each site builds a concrete
context carrying exactly its data; a command names the one it needs. No capability-query soup, no no-ops.
```csharp
public interface IEffectContext { GameObject Invoker; int InvokerPlayerId; NetworkPlayer InvokerPlayer; }
public sealed class ProjectileSurfaceCollision    : IEffectContext { Point; Normal; Direction; }
public sealed class ProjectilePlayerCollision     : IEffectContext { Target; Point; Normal; Direction; }
public sealed class ProjectileProjectileCollision : IEffectContext { Target; Point; Normal; Direction; }
public sealed class AbilityAim                     : IEffectContext { Point; Direction; }   // no target/normal
```
A command usable at several sites implements `IEffect<T>` for EACH concrete context (overloaded `Apply`
forwarding to a shared private helper — `SpawnProjectiles` implements all four, `SpawnVfx` the three
collisions, single-site ops just one). **Do NOT rely on `in`-variance for the dropdown:** the
`[SubclassSelector]` drawer collects candidates via `TypeCache.GetTypesDerivedFrom(closedGenericInterface)`,
which is exact-match, not variance-aware — a command only appears under a context it *explicitly* implements.

**3. `category ≠ capability` still holds — as separate context TYPES + separate typed LISTS.** The
surface/player/projectile split is *what kind of thing was hit* (a routing/authoring axis). Each maps to a
concrete context type AND a `[SerializeReference] List<IEffect<ThatContext>>` on the owner, so the inspector
can't even offer a target-needing op on the surface list — mis-slotting is an authoring impossibility, not a
runtime no-op.

**4. Reactions/effects/motion are AUTHORED DATA on the type; runtime still adds more via the owner channel.**
```csharp
// ProjectileType (the SO) — the human fills these dropdowns; there is NO BuildDefaultReactions:
[SerializeReference, SubclassSelector] public IMotionStrategy Motion = new LinearMotion();       // motion is a command too
[SerializeReference, SubclassSelector] public List<IEffect<ProjectileSurfaceCollision>>    OnSurface    = new();
[SerializeReference, SubclassSelector] public List<IEffect<ProjectilePlayerCollision>>     OnPlayer     = new();
[SerializeReference, SubclassSelector] public List<IEffect<ProjectileProjectileCollision>> OnProjectile = new();
// A per-run POWERUP authors its OWN contribution lists on PassiveAbilityType; PawnAbilities aggregates them
// and exposes them server-side via IProjectileEffectProvider — the owner-lookup channel; reuse it, don't
// invent a new one ([[cross-component-communication]], [[fishnet-conventions]]).
```
`IMotionStrategy` (Linear/Arc/Laser) is authored the same way and carries its own tuning — `MotionKind` /
`MotionSelector` are gone; its `CollisionSweep` folds the old laser branch in. The `Commands` static factory
(`Effects/Commands.cs`) builds the same command classes in code for tests + the owner-contribution path.

### Why the reversal — and why the old objections no longer hold
The earlier design composed effects IN CODE and explicitly rejected context-typing + `[SerializeReference]`.
The goal changed to **per-asset inspector authoring with compile-time context-safety**, and the objections
fell:
- **"`Effect<TContext>` / a `List<IEffect-interface>` won't serialize in Unity"** — FALSE on Unity 6 + the
  MackySoft package. Generic-interface `[SerializeReference]` lists serialize fine — VERIFIED: the asset's
  `references: RefIds` block carries each op's `{class, ns, asm}` (motion strategies resolve to their Core
  asm, operations to Assembly-CSharp). That was the load-bearing objection; it's gone.
- **"context-typing kills cross-site reuse"** — a real cost, paid deliberately: a multi-site op implements
  `IEffect<T>` per site (a few forwarding one-liners). In exchange every list is compile-time correct and a
  human can only slot valid ops. The old "compatibility decided by which builder list you add to" became
  "compatibility decided by which typed list the inspector shows it in" — same idea, now compiler-enforced.
- **`[SerializeReference]`'s per-copy / not-shareable tradeoff** (the table) is ACCEPTED here because each type
  authors its OWN operations — no cross-consumer asset to share, so GUID-stable reuse buys nothing. (Shared,
  human-authored STRATEGIES — movement, target providers — still default to **SO assets**; only the operation
  family moved.)

## `[SerializeReference]` gotchas — the authoring cost you're buying

The `[SubclassSelector]` picker is lovely, but `[SerializeReference]` binds a stored instance to its type by a
`{class, ns, asm}` name-string (in the asset's `references: RefIds` block), **not** by the script `.meta` GUID
that normal fields use. So it is **rename-fragile**, and most gotchas follow from that:

- **Rename/move a command/context/motion class → its authored instances silently go NULL** (missing managed
  reference; no compile error, the behaviour just vanishes from the asset). **Add `[MovedFrom]`
  (`UnityEngine.Scripting.APIUpdating`) before renaming or moving** — the sharp edge the day you lift these
  Assembly-CSharp global-namespace types into a namespace/asmdef.
- **An empty/unauthored list is a silent no-op**, not an error (an empty `ProjectileType.OnSurface` = a bullet
  that never dies on walls). Guard suspicious empties with an `OnValidate` warning.
- **Keep commands stateless** — `[SerializeReference]` is per-copy AND copy/paste can *alias* two slots to one
  instance; with one SO shared across every instance of a kind, any mutable state corrupts across instances.
  Class only (never a struct), `[Serializable]`, non-`UnityEngine.Object`, accessible parameterless ctor (the
  picker `Activator`-creates it).
- **Pin the MackySoft git package** — `manifest.json` tracks the repo's branch HEAD, so CI isn't reproducible;
  pin `#<tag/commit>` or use the OpenUPM versioned package.
- **The `[SubclassSelector]` dropdown is exact-match** (`TypeCache.GetTypesDerivedFrom`), not variance-aware —
  multi-site commands implement each closed command interface explicitly; `[AddTypeMenu("path")]` groups them
  as the library grows. The drawer is editor-only: if it broke you'd lose the picker, not the data.

## Flyweight discipline

A command is shared — an SO is one instance across every user, and a code command built once in a type's
default-builder is reused by every instance of that kind. **Never store per-invocation state on a command.**
The method only reads its inputs/context and acts; anything mutable is per-instance and belongs on the
instance (or in a `CreateRuntime` runtime object). Same rule as the type SO in [[type-object-pattern]]. A
code-built `IEffect` holds only its immutable serialized/ctor config; an SO strategy holds only its serialized config.

## Where parameters live

- **Shared / derived values** (the bullet's own `Damage`, its owner id, the seeker's target) → carried on
  the **context**, not duplicated onto every command.
- **Behaviour-specific tuning** (a knockback's force, a seek's max-speed):
  - `IEffect` command → **`[SerializeField]` fields (inspector-authored) or ctor args (code)** (`new
    DealDamage(amount: 8)`). No variant asset, no number-suffixed asset library.
  - SO command/strategy → fields on the **asset**; make a variant asset when you need two values.
  - Either way, don't push every variant's params onto the consumer's config — that re-creates the bloat
    that splitting `AbilityType` into Passive/Active was meant to kill.

## Effects are the Unity-API coupling layer — direct I/O, no provider, no data seam

This is a deliberate architectural decision about **who is allowed to touch the engine.** An `IEffect`
is **shell, and pointedly Unity-coupled**: its whole job is to *do something to the world*, so its `Apply`
reaches the engine **directly** — `GetComponent` on the context's `Invoker`/`Target`, `Instantiate`, FishNet
spawn (`ProjectileSpawner.Spawn`), `IDamageable.TakeDamage`, anything. The concrete context type hands it the
GameObjects precisely so it can. (Being authored via `[SerializeReference]` rather than an SO changes nothing
here — the operation is still the engine-coupling layer.)

```csharp
[System.Serializable]
public sealed class DealDamage : IEffect<ProjectilePlayerCollision> {   // a thin, Unity-coupled command
    public void Apply(ProjectilePlayerCollision ctx) {                    // ctx.Target GUARANTEED by the context type
        if (ctx.Target.GetComponentInParent<IDamageable>() is {} d)
            d.TakeDamage(new DamageInfo { Amount = DamageProcessor.Resolve(...), Attacker = ctx.InvokerPlayer, ... });
    }
}
```

We previously had a **data seam** here — effects emitted a `WorldEffect` *struct* and
`ProjectileWorldProvider.Apply(in WorldEffect)` performed it as the single world surface — and we
**deleted the struct, the `Apply` switch, and the provider entirely.** Why we dropped the
single-I/O-surface for direct coupling: the game's depth *is* effects interacting with many systems in
open-ended ways (damage, knockback, debuff, spawn, ricochet, AoE, pickups, level-ups). A provider with
typed methods becomes a god-object that must grow a method per interaction; letting each effect reach the
engine itself is what keeps the family open-ended. So **concentrate the broad engine coupling in the
effect/reaction/ability layer on purpose** — that is the one place we embrace "touch anything."

That freedom is bounded by two standing rules:

> **Keep the cores pure and push the engine to the edges.** The broad coupling lives *only* in effects/
> abilities. The processors, state, and config (`*.Core`) stay engine-free and tested; the components stay
> thin. Don't let Unity-API calls leak into a processor because an effect made it feel normal.

> **Extract non-trivial computation into pure static helpers** (a ricochet's reflected trajectory, a spawn
> fan's directions, an owner-safety check) into a `*.Core` assembly and unit-test *those*; the effect is a
> thin Unity wrapper that calls the helper then does the I/O. Direct I/O is fine; it must not swallow logic
> that deserves a test. (`Ricochet` → `RicochetMath.TryReflect`, `SpawnProjectiles` → `SpawnFanMath`.)

**Reserve emitting effects as DATA (collect-then-execute) for one case that genuinely remains:** predicted
`[Replicate]` code (the Rigidbody/prediction layer — `MovementController`, `ProjectileMovement`,
`SkullPowerup`). Replicate re-runs during reconcile, so a *direct* side effect re-fires per resimulation
(double-spawn, double-damage); there you emit data and gate execution to the authoritative tick. Projectile
collision is **server-only and not predicted**, so direct I/O is correct there.

## Networking & determinism

SO command/strategy assets resolve **locally on every peer** (each has the same asset) — they are never
serialized over the wire; a consumer that syncs as a database index already carries them. `[SerializeReference]`
`IEffect`s are the same: they're data on that same SO, resolved locally on every peer, so there's nothing
to sync at all — the type (synced as a database index) carries the recipe (VERIFIED: they serialize into the
asset's `references: RefIds` block and never ride the wire). Both are safe under the deterministic /
server-authoritative model ([[fishnet-conventions]]). If a behaviour ever needs to be referenced
individually over the network, sync it as a database index like the types do — never as a raw object
reference.

## Quick checklist

1. A *family* of interchangeable variants sharing a context? → a command per variant. **WHO composes it?**
   a human dragging a SHARED/reused asset across many consumers → **SO class + asset** (movement strategies,
   target providers); a human authoring per-asset on its owning type (the OPERATION family — hit reactions,
   ability effects, motion) → **`[SerializeReference, SubclassSelector]` context-typed command, no asset**
   (want inspector tuning without recompile? THIS, not SO-per-effect); never touched by a human, always
   code-assembled → **plain C# class**. One-off with no siblings? → just write the method.
2. Does it need per-instance runtime state? → **`CreateRuntime()` factory**; else call it directly (stateless).
3. Does the command need data only SOME sites have? → make a **concrete context type per site** and type the
   command to the one it needs (`IEffect<TContext>`); a multi-site command implements `IEffect<T>` per
   site (explicitly — the `[SubclassSelector]` dropdown is exact-match, not `in`-variance-aware). No
   capability-query no-ops. Routing/category that decides *which* commands run is the **typed list** the
   inspector shows it in (compile-enforced), not a runtime `is`-check.
4. Per-invocation state on the command? → **no** — flyweight; ctor config (code) or serialized config (SO) only.
5. Params: shared/derived → **context**; behaviour-specific → **ctor arg** (code command) or **asset field**
   (SO command).
6. Effect doing world I/O? → it's the Unity-coupling layer: **direct engine I/O on the context's
   GameObjects** (no provider, no data struct), with non-trivial computation extracted to a pure `*.Core`
   helper and tested. Keep that coupling *only* in effects/abilities — cores stay pure. Inside predicted
   `[Replicate]`? → **emit effect data** and execute on the authoritative tick.
7. A type DEFAULT that varies by kind → **author it in the type's `[SerializeReference]` operation list**
   (no feature-flag builder). A per-run, per-player addition (a powerup) → author it on the passive's own
   contribution list; PawnAbilities aggregates it and exposes it through the existing owner-lookup channel,
   not a new one.
