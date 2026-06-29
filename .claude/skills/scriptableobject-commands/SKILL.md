---
name: scriptableobject-commands
description: How to encapsulate a single piece of SWAPPABLE BEHAVIOUR as a COMMAND/STRATEGY whenever you have many interchangeable variants of one behaviour that share a common context/signature and you don't want to write a whole script per variant. Applies far beyond the Type Object pattern: hit reactions, ability effects, movement strategies, target providers, AI behaviours, scoring/win rules. KEY DISCRETION CALL — SO asset vs plain-C# class — decided by WHO composes the command: a human dragging it as data in the inspector (swapped/shared across consumers) → ScriptableObject asset (the default for movement strategies, target providers, AI behaviours, over [SerializeReference] or enum+switch); code inside a parent SO that already defines its behaviour → plain C# class with NO asset (the IEffect family — effects are composed in code by ProjectileType/AbilityType because the parent already needs code, ctor args replace variant assets, and powerups add effects at runtime; we REVERSED the old SO-per-effect design). Covers: that discretion test; the stateless-vs-stateful split (CreateRuntime() factory only when per-instance runtime state is needed, like MovementStrategySO); capability contexts (effects query a polymorphic IEffectContext via `is ITargetContext`/`ISurfaceContext` for data only SOME invocations have — no SingleTargetEffect base, no context-typed effects; category≠capability); defaults composed in code via feature flags with runtime contribution through the owner-lookup channel; flyweight discipline (no per-invocation state — ctor config or serialized config only); where parameters live; and WHO touches the Unity API — effects are the deliberate engine-coupling layer doing direct I/O on the context's GameObjects (no provider/data-struct seam; cores stay pure with non-trivial math in *.Core helpers; emit effect data only for predicted [Replicate] code). Biased for a SOLO, code-first Unity dev using the Processor + thin-MonoBehaviour pattern over FishNet. Use this BEFORE adding a new behaviour variant, choosing SO vs plain-class vs [SerializeReference] vs enum, deciding how a command gets the data it needs, or deciding whether a behaviour emits effect-data or calls the world directly. Pairs with [[type-object-pattern]] (a common consumer) and [[functional-core-imperative-shell]] (where the pure computation goes).
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

`[SerializeReference]` is what we're **moving away from** (fragile to rename/move, not shareable). An
`enum`+`switch` (today's `MovementKind`/`MovementSelector`) is worse — three edits and a switch per
variant. The SO trades a little authoring ceremony for reuse, refactor-stability, tooling, and type-safety.

### SO asset vs. plain-C# command — use discretion; the test is WHO composes it

Not every command earns an SO asset. Decide per command by asking **who assembles it**:

- **A human, as data, in the inspector — swapped/shared across many consumers** → **SO asset.** This is the
  default above (movement strategies, target providers, AI behaviours: `MovementStrategySO` et al.). The
  asset buys inspector authoring, GUID-stable reuse, `find_references`, central tuning. Worth the ceremony.
- **Code, inside a parent SO that already defines "what it does" in C#** → **plain C# command, no asset.**
  This is the `IEffect` family. An effect only ever exists *as part of* a projectile/ability type, and that
  type already needs code to declare its behaviour (`ProjectileType.BuildDefaultReactions`,
  `ActiveAbilityType.BuildEffects`). Once the parent composes its effects in code, a separate SO **per effect
  buys nothing**: there's no inspector slot a human fills, no cross-consumer asset to share — just a `new
  DealDamage(8)` in the parent's builder. Making it an SO would add a GUID, an asset file, and a load step
  for zero benefit. So keep it in code. (The parent SO still holds the asset *references* an effect needs —
  a VFX prefab, a sub-projectile type — and injects them into the code-built effect.)

The smell test: *"Would a human ever drag this command into a slot, or is it always assembled by code that
already lives in a parent SO?"* Draggable-as-data → SO asset. Always code-composed by its owner → plain class.

## Stateless → call the SO directly. Stateful → make it a factory.

- **Stateless** (most reactions, simple commands): the SO *is* the runtime behaviour. Call its method
  directly, passing a context. Pure flyweight, shared by all callers.
- **Stateful** (a strategy that accumulates per-instance runtime state — a homing tracker): the SO is a
  **factory** — `CreateRuntime()` yields a runtime object that owns the state. This is exactly what
  `MovementStrategySO` does, and it's the escape hatch for "SOs are shared, so they can't hold per-instance
  state."

## Effects: plain-C# commands, capability contexts, defaults composed in code

This is the landed shape of the `IEffect` family (the discretion call above resolved to "code, not SO").
Four parts:

**1. The command is a plain class; config is constructor args.**
```csharp
public interface IEffect { void Apply(IEffectContext ctx); }

public sealed class DealDamage : IEffect {                    // ctor config replaces a "DealDamage_10" asset
    readonly int _amount; readonly bool _useInvokerDamage, _despawnAfter;
    public DealDamage(bool useInvokerDamage = true, int amount = 10, bool despawnAfter = true) { ... }
    public void Apply(IEffectContext ctx) {
        if (ctx is not ITargetContext t) return;              // needs a target — no-ops without one
        ...
    }
}
```

**2. The context advertises CAPABILITIES; effects pull only what they need.** The data an effect needs is
ONE composable thing — a polymorphic context that implements capability interfaces. An effect queries the
slice it wants and no-ops gracefully when absent. There is **no** `SingleTargetEffect` base and no per-axis
effect subclass — "needs a target" is just `ctx is ITargetContext`, the same mechanism as everything else.
```csharp
public interface IEffectContext   { GameObject Invoker { get; } NetworkPlayer InvokerPlayer { get; } int InvokerPlayerId { get; } }
public interface ITargetContext   : IEffectContext { GameObject Target { get; } }
public interface IPointContext    : IEffectContext { Vector3 Point { get; } }
public interface IDirectedContext : IEffectContext { Vector3 Direction { get; } }
public interface ISurfaceContext  : IPointContext  { Vector3 Normal { get; } }

public sealed class CollisionContext : ITargetContext, ISurfaceContext, IDirectedContext { ... } // a hit: everything
public sealed class AimContext       : IPointContext, IDirectedContext { ... }                    // an ability: no target/normal
```
Capabilities live on a **polymorphic context** (not flat fields, not the effect's type) because "implements
interface X" is per-type — the only way "do I have a surface normal?" can vary by what happened is for the
concrete context type to vary. This is what keeps one `DealDamage` reusable across a projectile hit AND an
ability use: each shell builds the context that truthfully advertises its data.

**3. `category ≠ capability`.** The surface/enemy/projectile split is *what kind of thing was hit* (a
routing/authoring axis), NOT *what data is present*. All three build the same `CollisionContext`; the
category only selects which reaction list runs. So adding a hit category is never adding a capability, and
the capability interfaces don't replace the category routing.

**4. Defaults are composed IN CODE by the owning type; runtime adds more.** A `HitReaction` is now a runtime
ordered `List<IEffect>` (no SO asset, no typed marker subclasses — the category is the list you add to, so
mis-slotting is impossible). The type builds its defaults from config + **feature flags**; the
owner-contribution path appends more at runtime.
```csharp
// ProjectileType (the SO) composes defaults in code from serialized config/flags:
void BuildDefaultReactions(ProjectileReactions r) {
    if (SurfaceHealthLoss > 0) r.OnSurface.Add(new LoseHealth(SurfaceHealthLoss));
    if (RicochetsByDefault)    r.OnSurface.Add(new Ricochet());            // feature flag = a bouncy KIND
    r.OnEnemy.Add(new DealDamage(useInvokerDamage: true, despawnAfter: true));
}
// A per-run POWERUP contributes extra effects at runtime via the owner path (NOT a flag on the type):
// PassiveAbilityType.BuildProjectileContributions(r) -> r.OnSurface.Add(new Ricochet());
// ServerCollision runs the type's defaults, then the owner's contributed reaction, per category.
```
A type DEFAULT that varies by kind → a feature flag on the type (`RicochetsByDefault`). A per-run, per-player
addition → a runtime contribution through the existing owner-lookup channel ([[cross-component-communication]],
[[fishnet-conventions]]) — reuse that channel, don't invent a new one.

### Why not the shapes we rejected
- **Type the effect to its context (`Effect<TContext>`) or per-category (`ISurfaceCollisionEffect`)** → welds
  the effect to one site, kills cross-site reuse, and `List<Effect<T>>`/`List<IEffect-interface>` won't
  serialize in Unity. Compatibility is decided by *where you compose the effect* (which list in the type's
  builder), not by the effect's type.
- **Capabilities as flat sometimes-zero fields on one context struct** → a single type can't conditionally
  implement an interface, so absence can't be expressed truthfully; you'd be back to zero-sentinels.

## Flyweight discipline

A command is shared — an SO is one instance across every user, and a code command built once in a type's
default-builder is reused by every instance of that kind. **Never store per-invocation state on a command.**
The method only reads its inputs/context and acts; anything mutable is per-instance and belongs on the
instance (or in a `CreateRuntime` runtime object). Same rule as the type SO in [[type-object-pattern]]. A
code `IEffect` holds only its immutable ctor config; an SO strategy holds only its serialized config.

## Where parameters live

- **Shared / derived values** (the bullet's own `Damage`, its owner id, the seeker's target) → carried on
  the **context**, not duplicated onto every command.
- **Behaviour-specific tuning** (a knockback's force, a seek's max-speed):
  - code command (`IEffect`) → **constructor arguments** (`new DealDamage(amount: 8)`), set where the type
    composes its defaults. No variant asset, no number-suffixed asset library.
  - SO command/strategy → fields on the **asset**; make a variant asset when you need two values.
  - Either way, don't push every variant's params onto the consumer's config — that re-creates the bloat
    that splitting `AbilityType` into Passive/Active was meant to kill.

## Effects are the Unity-API coupling layer — direct I/O, no provider, no data seam

This is a deliberate architectural decision about **who is allowed to touch the engine.** An `IEffect`
is **shell, and pointedly Unity-coupled**: its whole job is to *do something to the world*, so its `Apply`
reaches the engine **directly** — `GetComponent` on the context's `Invoker`/`Target`, `Instantiate`, FishNet
spawn (`ProjectileSpawner.Spawn`), `IDamageable.TakeDamage`, anything. The context hands it the GameObjects
(via its capability interfaces) precisely so it can. (Being a plain class rather than an SO changes nothing
here — code-composed or asset-authored, the effect is still the engine-coupling layer.)

```csharp
public sealed class DealDamage : IEffect {                 // a thin, Unity-coupled command (plain class)
    public void Apply(IEffectContext ctx) {
        if (ctx is not ITargetContext t) return;
        if (t.Target.GetComponentInParent<IDamageable>() is {} d)
            d.TakeDamage(new DamageInfo { Amount = DamageProcessor.Resolve(...), Attacker = ctx.InvokerPlayer, ... });
    }
}
```

We previously had a **data seam** here — effects emitted a `WorldEffect` *struct* and
`ProjectileWorldProvider.Execute(in WorldEffect)` performed it as the single world surface — and we
**deleted the struct, the `Execute` switch, and the provider entirely.** Why we dropped the
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
serialized over the wire; a consumer that syncs as a database index already carries them. Code `IEffect`s
are even simpler: they're built locally from the type's code on every peer, so there's nothing to sync at
all — the type (synced as a database index) carries the recipe. Both are safe under the deterministic /
server-authoritative model ([[fishnet-conventions]]). If a behaviour ever needs to be referenced
individually over the network, sync it as a database index like the types do — never as a raw object
reference.

## Quick checklist

1. A *family* of interchangeable variants sharing a context? → a command per variant. **WHO composes it?**
   a human dragging it as data in the inspector → **SO class + asset** (not `[SerializeReference]`, not an
   enum case); code inside a parent SO that already defines its behaviour (the `IEffect` family) → **plain
   C# class, no asset**. One-off with no siblings? → just write the method.
2. Does it need per-instance runtime state? → **`CreateRuntime()` factory**; else call it directly (stateless).
3. Does the command need data only SOME invocations have? → put it on a **capability interface** the
   polymorphic context implements; the command queries `ctx is IXxxContext`. No per-context command subclass,
   no context-typed command. Routing/category that decides *which* commands run lives in the **composer**
   (the list you add to), not the command's type.
4. Per-invocation state on the command? → **no** — flyweight; ctor config (code) or serialized config (SO) only.
5. Params: shared/derived → **context**; behaviour-specific → **ctor arg** (code command) or **asset field**
   (SO command).
6. Effect doing world I/O? → it's the Unity-coupling layer: **direct engine I/O on the context's
   GameObjects** (no provider, no data struct), with non-trivial computation extracted to a pure `*.Core`
   helper and tested. Keep that coupling *only* in effects/abilities — cores stay pure. Inside predicted
   `[Replicate]`? → **emit effect data** and execute on the authoritative tick.
7. A type DEFAULT that varies by kind → a **feature flag** on the type's code builder. A per-run, per-player
   addition (a powerup) → a **runtime contribution** through the existing owner-lookup channel, not a new one.
