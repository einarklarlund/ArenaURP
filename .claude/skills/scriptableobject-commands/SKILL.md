---
name: scriptableobject-commands
description: A general encapsulation tool — author a single piece of SWAPPABLE BEHAVIOUR as a ScriptableObject COMMAND/STRATEGY asset whenever you have many interchangeable variants of one behaviour that share a common context/signature and you don't want to write a whole script per variant. Applies far beyond the Type Object pattern: hit reactions, ability effects, movement strategies, target providers, AI behaviours, scoring/win rules — anything with a fixed shape and many variants. The house default is SO-per-behaviour (referenced/swapped by whoever runs it), over [SerializeReference] inline objects or an enum+switch. Covers why; the stateless-vs-stateful split (call the SO directly when stateless; make it a CreateRuntime() factory when it needs per-instance runtime state, like MovementStrategySO); per-context typing (a HitReaction base with ProjectileHitReaction/SurfaceHitReaction/PlayerHitReaction subclasses each taking a typed context, so variants can't be mis-slotted); the flyweight discipline (never store per-invocation state on the SO); where parameters live (shared/derived via the context, behaviour-specific tuning on the asset); and the DATA-SEAM vs DIRECT-I/O decision (default: a thin command calls the world provider's typed methods directly; reserve emitting effects as data for predicted [Replicate] code that must gate execution to authoritative ticks). Biased for a SOLO, code-first Unity dev using the Processor + thin-MonoBehaviour pattern over FishNet. Use this BEFORE adding a new behaviour variant, choosing between SO / [SerializeReference] / enum, or deciding whether a behaviour emits effect-data or calls the world directly. Pairs with [[type-object-pattern]] (a common consumer of these) and [[functional-core-imperative-shell]] (where the pure computation goes).
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
| Per-context typing | flat `List<IHitReaction>` | **typed lists, no mis-slot** | — |
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

When variants run in *different* contexts (each carrying different data), type the variant to its context.
A base plus a subclass per context lets typed lists/slots on the consumer reject the wrong kind at author
time. The projectile's three hit categories are the example:

```csharp
public abstract class HitReaction : ScriptableObject {
    public abstract void React(in HitInfo hit, IReactionContext ctx);
}
public abstract class SurfaceHitReaction   : HitReaction { /* ctx is SurfaceReactionContext */ }
public abstract class ProjectileHitReaction: HitReaction { /* ctx is ProjectileReactionContext */ }
public abstract class PlayerHitReaction    : HitReaction { /* ctx is PlayerReactionContext */ }

// On the consumer — you literally cannot drop a surface reaction in the enemy slot:
[SerializeField] List<SurfaceHitReaction>    OnSurface;
[SerializeField] List<ProjectileHitReaction> OnProjectile;
[SerializeField] List<PlayerHitReaction>     OnEnemy;
```

(Keep context/variant names aligned to the categories — `Projectile` / `Surface` / `Player`(`Enemy`).) The
cost: a variant useful in *every* context must be written per context (or kept behind a context-agnostic
base) — the price of the mis-slot safety. When all variants share one context, skip the subclassing.

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

## Data seam vs. direct I/O — the call to make per command

**What a data seam is.** Instead of *performing* a side effect, a command emits a *description* of the
intended effect as plain data; a separate executor reads the data and performs it. `WorldEffect` +
`ProjectileWorldProvider.Execute(in WorldEffect)` is exactly this — commands append `WorldEffect`s to a
buffer; the shell executes them in one place. It **separates the decision (what should happen) from the
execution (the I/O)**, which is what makes the decision unit-testable, gateable, and replayable.

**The house default is DIRECT I/O for thin commands.** A command that just deals damage or spawns has no
decision worth separating — the decision *is* the call. Have it call the world provider's **typed methods
directly** (`ctx.World.DealDamage(...)`, `ctx.World.Spawn(...)`). This is less ceremony than a data struct
+ `EffectKind` + factory + switch case, and you **keep the single I/O surface** — the provider is still the
one place the world is mutated, just with typed methods instead of a dispatch switch. The SO command is
**shell**, not core (it's a Unity object doing I/O), so this respects [[functional-core-imperative-shell]]
— with one rider:

> **Extract non-trivial computation into pure static helpers** (a ricochet's new trajectory, a spawn fan's
> directions, a conditional check) and unit-test *those*; let the thin command do the I/O with the result.
> Direct I/O is fine for thin commands; it must not swallow logic that deserves a test.

**Reserve the data seam (emit effects as data) for two cases:**
1. **Predicted `[Replicate]` code** (the Rigidbody/prediction layer — `MovementController`,
   `ProjectileMovement`, `SkullPowerup`). Replicate re-runs during reconcile, so any *direct* side effect
   re-fires per resimulation (double-spawn, double-damage). Emitting effects as data lets you **gate
   execution to the authoritative (non-replayed) tick**. Projectile collision is server-only and *not*
   predicted, so this doesn't apply there — direct I/O is safe.
2. **Heavy decision logic** you specifically want pure, testable, recordable, or collected-then-executed
   for consistency (effects that must not interfere with each other mid-computation).

**Migration note:** projectile reactions and ability effects currently both go through the `WorldEffect →
Execute` data seam. Move the thin ones to direct provider calls so the codebase tells one story; keep the
provider as the single world surface (typed methods). Keep the data seam only where one of the two cases
above genuinely applies.

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
6. Effect: thin + server-only? → **call the provider directly**, computation in pure helpers. Inside
   predicted `[Replicate]`, or need testable/gated/recordable decisions? → **emit `WorldEffect` data** and
   execute in the shell.
