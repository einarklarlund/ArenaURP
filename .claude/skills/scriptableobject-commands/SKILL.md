---
name: scriptableobject-commands
description: A general encapsulation tool — author a single piece of SWAPPABLE BEHAVIOUR as a ScriptableObject COMMAND/STRATEGY asset whenever you have many interchangeable variants of one behaviour that share a common context/signature and you don't want to write a whole script per variant. Applies far beyond the Type Object pattern: hit reactions, ability effects, movement strategies, target providers, AI behaviours, scoring/win rules — anything with a fixed shape and many variants. The house default is SO-per-behaviour (referenced/swapped by whoever runs it), over [SerializeReference] inline objects or an enum+switch. Covers why; the stateless-vs-stateful split (call the SO directly when stateless; make it a CreateRuntime() factory when it needs per-instance runtime state, like MovementStrategySO); per-context typing (typed HitReaction marker subclasses that own a list of context-agnostic WorldEffects sharing ONE EffectContext, so the markers stop mis-slotting while the effects stay reusable across categories AND abilities); the flyweight discipline (never store per-invocation state on the SO); where parameters live (shared/derived via the context, behaviour-specific tuning on the asset); and the decision about WHO touches the Unity API — effects are the deliberate engine-coupling layer doing direct I/O on the context's GameObjects (we DELETED the old WorldEffect-data-struct + ProjectileWorldProvider.Execute seam; cores stay pure with non-trivial math extracted to *.Core helpers; reserve emitting effects as data only for predicted [Replicate] code). Biased for a SOLO, code-first Unity dev using the Processor + thin-MonoBehaviour pattern over FishNet. Use this BEFORE adding a new behaviour variant, choosing between SO / [SerializeReference] / enum, or deciding whether a behaviour emits effect-data or calls the world directly. Pairs with [[type-object-pattern]] (a common consumer of these) and [[functional-core-imperative-shell]] (where the pure computation goes).
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
variant. The SO trades a little authoring ceremony for reuse, refactor-stability, tooling, and
type-safety. Accept a small **library of configured assets** (`DealDamage_10`, `Knockback_8`, `Seek_Fast`)
as reusable, centrally tunable building blocks rather than fighting the proliferation.

## Stateless → call the SO directly. Stateful → make it a factory.

- **Stateless** (most reactions, simple commands): the SO *is* the runtime behaviour. Call its method
  directly, passing a context. Pure flyweight, shared by all callers.
- **Stateful** (a strategy that accumulates per-instance runtime state — a homing tracker): the SO is a
  **factory** — `CreateRuntime()` yields a runtime object that owns the state. This is exactly what
  `MovementStrategySO` does, and it's the escape hatch for "SOs are shared, so they can't hold per-instance
  state."

## Per-context typing — variants that can't be mis-slotted

Put the mis-slot typing on a **grouping** layer, and keep the actual behaviour units **context-agnostic**
so one asset is reusable everywhere. The projectile shows the split we landed on — a typed `HitReaction`
that *owns a list of* `WorldEffect`s, where the `WorldEffect`s share **one** `EffectContext`:

```csharp
// The reusable behaviour unit — ONE shared context, so the same asset works for a projectile hit
// reaction AND an ability use. Not per-category-typed.
public abstract class WorldEffect : ScriptableObject { public abstract void Apply(in EffectContext ctx); }
public abstract class SingleTargetEffect : WorldEffect { /* base no-ops when ctx.Target is null */ }

// The grouping layer carries the typing — empty marker subclasses, just to make slots un-mis-fillable:
public abstract class HitReaction : ScriptableObject {
    [SerializeField] List<WorldEffect> effects;          // runs them on a hit, given the context
}
public sealed class SurfaceHitReaction    : HitReaction {}
public sealed class ProjectileHitReaction : HitReaction {}
public sealed class EnemyHitReaction      : HitReaction {}

// On the consumer (ProjectileType) — you literally cannot drop a surface reaction in the enemy slot:
public List<SurfaceHitReaction>    OnSurface;
public List<EnemyHitReaction>      OnEnemy;
public List<ProjectileHitReaction> OnProjectile;
```

Why this shape, not per-context-typed `React` methods: a single `EffectContext` (carrying both colliding
GameObjects + point/normal/dir/owner) means a `DealDamage` or `SpawnProjectiles` effect authored once is
reused by every category **and** by abilities (which build the same context from the caster's aim). The
typing that prevents mis-slotting lives on the cheap `HitReaction` marker subclasses; the valuable,
numerous units (`WorldEffect`s) stay context-agnostic and maximally reusable. `SingleTargetEffect` is the
one sanctioned refinement — it pulls `ctx.Target` and skips when null, so target-needing effects don't each
re-check.

## Flyweight discipline

An SO is one shared instance across every user. **Never store per-invocation state on the command SO.**
The method only reads its inputs/context and acts; anything mutable is per-instance and belongs on the
instance (or in a `CreateRuntime` runtime object). Same rule as the type SO in [[type-object-pattern]].

## Where parameters live

- **Shared / derived values** (the bullet's own `Damage`, its owner id, the seeker's target) → carried on
  the **context**, not duplicated onto every asset.
- **Behaviour-specific tuning** (a knockback's force, a seek's max-speed) → fields on the **asset**; make a
  variant asset when you need two values. Don't push every variant's params onto the consumer's config —
  that re-creates the bloat that splitting `AbilityType` into Passive/Active was meant to kill.

## Effects are the Unity-API coupling layer — direct I/O, no provider, no data seam

This is a deliberate architectural decision about **who is allowed to touch the engine.** A `WorldEffect`
is **shell, and pointedly Unity-coupled**: its whole job is to *do something to the world*, so its `Apply`
reaches the engine **directly** — `GetComponent` on `ctx.Invoker`/`ctx.Target`, `Instantiate`, FishNet
spawn (`type.Create()` + `ServerManager.Spawn`), `IDamageable.TakeDamage`, anything. The
`EffectContext` hands it the two colliding GameObjects (+ point/normal/dir/owner) precisely so it can.

```csharp
public sealed class DealDamage : SingleTargetEffect {                 // a thin, Unity-coupled command
    protected override void ApplyToTarget(in EffectContext ctx, GameObject target) {
        if (target.GetComponentInParent<IDamageable>() is {} d)
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
serialized over the wire; a consumer that syncs as a database index already carries them. So they are safe
under the deterministic / server-authoritative model ([[fishnet-conventions]]). If a behaviour ever needs
to be referenced individually over the network, sync it as a database index like the types do — never as a
raw object reference.

## Quick checklist

1. A *family* of interchangeable variants sharing a context? → an **SO class + asset** per variant, not
   `[SerializeReference]`, not an enum case. One-off with no siblings? → just write the method.
2. Does it need per-instance runtime state? → **`CreateRuntime()` factory**; else call the SO directly.
3. Do variants run in different contexts? → subclass a typed base so they can't be mis-slotted.
4. Per-invocation state on the SO? → **no** — flyweight; push it to the instance/runtime object.
5. Params: shared → **context**; behaviour-specific → **asset field** (variant asset if needed).
6. Effect doing world I/O? → it's the Unity-coupling layer: **direct engine I/O on the context's
   GameObjects** (no provider, no data struct), with non-trivial computation extracted to a pure `*.Core`
   helper and tested. Keep that coupling *only* in effects/abilities — cores stay pure. Inside predicted
   `[Replicate]`? → **emit effect data** and execute on the authoritative tick.
