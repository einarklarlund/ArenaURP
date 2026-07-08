---
name: effect-commands
description: The IEffect<TContext> command system — how ALL swappable gameplay behaviour (projectile hit reactions, ability effects, stat/on-spawn buffs) is authored as inspector-picked command objects typed to the SYSTEM CAPABILITY they touch. Use this BEFORE adding or editing any effect/buff/reaction/debuff, adding a context or capability interface, deciding where an effect's config lives, touching a [SerializeReference]/[EffectSelector] list, or creating any new family of interchangeable behaviour variants. Also covers the flyweight/statelessness rules, effects as the one deliberately Unity-coupled layer, standing-vs-on-spawn buffs, the pooling two-layer rule, and the OPEN damage-buff design question (next design task — do not improvise it). Pairs with [[type-object-pattern]] (whose prefabs host the authored lists) and [[functional-core-imperative-shell]] (where pure math goes).
---

# Effect commands: `IEffect<TContext>`

When you have a **family of interchangeable behaviours sharing one context** — hit reactions, ability effects, buffs — each variant is a plain `[System.Serializable]` command class implementing `IEffect<TContext>` (`Assets/Scripts/Effects/IEffect.cs`), authored in the inspector on a typed `[SerializeReference, EffectSelector]` list, and buildable in code via the static `Commands` factory (tests, runtime composition). A one-off behaviour with no siblings is just a method — the machinery only pays off for families.

```csharp
public interface IEffect<in TContext> where TContext : IEffectContext { void Apply(TContext context); }

[System.Serializable]
public sealed class BuffFireRate : IEffect<IWeaponModifierContext> {
    [SerializeField] float multiplier = 0.75f;
    public BuffFireRate() {}  public BuffFireRate(float multiplier) { ... }
    public void Apply(IWeaponModifierContext ctx) => ctx.AddWeaponModifier(...);
}
```

## Contexts are SYSTEM capabilities, not object types

The rule written on `IEffect.cs`: **system boundaries, not object types.** A context describes "what happened + what systems you may reach", as small composable capability interfaces; the SITE that builds the context resolves the component handles ONCE and hands them over — an effect never does `GetComponent` off a raw archetype and gambles it's there.

- Root `IEffectContext`: `Source` (GameObject that triggered it — the escape hatch for plain data-holders like `SpawnableObjects`) + `Instigator` (attributed `NetworkPlayer`).
- Geometry: `IAimContext` (Point + Direction) → `ISurfaceContext` (+ Normal).
- Resolved system handles: `IDamageSourceContext` (`Damage`), `IDamageTargetContext` (`IDamageable`), `IMotionContext` (`NetworkedMotion`), `IHealthContext` (own `Health`).
- Composites for multi-system ops (C# can't say "A and B" as one type arg): `IDamageExchangeContext`, `IMotionSurfaceContext`.
- Contribution SINKS for standing buffs (see below): `IMovementModifierContext`, `IWeaponModifierContext`.

Concrete contexts (`EffectContexts.cs`) implement the capability subset their site can honestly provide: `ProjectileSurfaceCollision` (motion+surface+own-health, NO damage — a wall can't take damage), `ProjectilePlayerCollision` / `ProjectileProjectileCollision` (everything), `AbilityAim` (aim only), `OnSpawnContext` (base only, deliberately), `PawnStatBuff` (the two sinks).

**Type an effect to the LEANEST capability that serves it** — `TContext` is contravariant, and the custom `[EffectSelector]` drawer (`Editor/EffectSelectorDrawer.cs`) is **variance-aware**: an effect appears in every authoring list whose context satisfies its need (`TNeed.IsAssignableFrom(TAuthored)`). So `DealDamage : IEffect<IDamageExchangeContext>` is offered on player and projectile hits but not surfaces, and lands on a pawn, a prop, or a damageable projectile alike. Typing to a concrete context is legitimate only when the effect truly depends on it (`DeflectProjectile` needs the other projectile's motion → `ProjectileProjectileCollision`). Mis-slotting is an authoring impossibility, not a runtime no-op. (MackySoft `[SubclassSelector]` is exact-match, not variance-aware — it remains only on `NetworkedMotion.Motion` for `IMotionStrategy`.)

**Naming: name the system knob, never the archetype.** `Buff<knob-of-the-system>`: `BuffSpeed`/`BuffScale`/`BuffMaxHealth`, `BuffWalkSpeed`/`BuffFireRate` — never `BuffProjectileSpeed`/`BuffPawn*`. The same op lands on anything carrying that system; an archetype name lies about coupling the context design removed. (Contexts named for a collision SITE — `ProjectilePlayerCollision` — are fine: they name the event's participants.)

## Where config lives — the decision rule

1. **A scalar read only by the effect that owns it** (a buff's multiplier) → `[SerializeField]` on the effect + a ctor for code composition. Several entries of the same effect with different values just work.
2. **Chunky or shared config another system must read** (prefab fan lists, an entity's base `Damage`/`Health`) → a component, with an explicit serialized `source` ref on the effect (`SpawnNetworkObjects.source → SpawnableObjects`).
3. **Runtime tuning NEVER mutates authored values** — contribute a modifier folded at apply time. Mutation is pooling-unsafe and unstackable.
4. Balancing ergonomics is an editor-tooling problem, never a reason to move config.

**Flyweight discipline:** commands hold immutable config only — no state mutated during `Apply` (`[SerializeReference]` copy/paste can alias two slots to one instance). Class (never struct), `[Serializable]`, accessible parameterless ctor.

## Effects are the ONE deliberately Unity-coupled layer

An effect's job is to do something to the world, so `Apply` reaches the engine directly — pooled instantiate, FishNet spawn, `IDamageable.TakeDamage` — on the handles the context resolved. No provider, no effect-data structs (we built and deleted both: a typed world-facade must grow a method per interaction and becomes a god object). Two bounds:

- Cores stay pure; don't let engine calls leak into a processor because effects made it feel normal.
- Non-trivial math goes to a pure `*.Core` helper with EditMode tests (`RicochetMath`, `SpawnFanMath`, `DeflectMath`, `DamageProcessor`); the effect is a thin wrapper.

Projectile collision is server-only and unpredicted, so direct I/O is correct there. Only predicted `[Replicate]` code (which re-runs on reconcile) must emit effect data gated to the authoritative tick.

## Hit resolution is ATOMIC

A classified hit runs ALL its authored effects — the prefab's list, then the owner's contributions — even if the projectile dies mid-list. Authored order can never change a hit's outcome; death gates only the projectile channel, further hits, and despawn (`ServerCollision.RunEffects`; pinned by `ProjectileDamageSmokeTests.Hit_resolves_atomically_regardless_of_effect_order`).

## The three buff families on a passive

- **Standing stat buffs** (`PassiveAbility.StatBuffs`, `IEffect<PawnStatBuff>`): a buff CONTRIBUTES one modifier through the sink; `PawnPassiveAbilities` owns applying the accumulated lists to `PawnMovement.Modifiers`/`PawnWeapon.Modifiers` and the Remove-old/Add-new re-derive on every roster change. The `MovementModifier`/`WeaponModifier` STRUCTS stay — they're the fold currency the pure processors consume, and non-authored producers (`PawnTemperature`, `PawnMovementEffects`) build them directly.
- **Owner projectile contributions** (`ProjectileOn*` lists): aggregated server-side and run by `ServerCollision` after the prefab's own list — looked up from the owner id at hit time, never payloaded ([[fishnet-conventions]]).
- **On-spawn buffs** (`OnSpawn`, `IEffect<OnSpawnContext>`): applied ONCE at spawn, on EVERY peer, by `SpawnBuffReceiver` (owner id → replicated passives → apply). `OnSpawnContext` implements only the base capability on purpose — buffs reach their target by `GetComponent` on the spawned object, and the lean context keeps them out of collision lists. Semantics are a per-peer spawn-instant snapshot: properties with a sync channel (speed via seg SyncVars, max-health via Health SyncVars) self-heal on clients; pure cosmetics (scale) accept the snapshot — do NOT add sync channels for cosmetics.

**Pooling two-layer rule** (any spawn-time contributor): the authored **baseline** (`InitialSpeed`, prefab scale, `maxHealth`) and the **per-spawn multiplier layer** never share a field; `effective = baseline × multiplier`; the multiplier resets to identity in `ResetState`, the baseline is never wiped. Setters are idempotent and order-independent (`SetSpeedMultiplier`, `SetMaxHealthMultiplier` — the house answer to sibling init with no guaranteed `OnStartNetwork` order). A per-spawn transform mutation needs an owning component with its own `ResetState` (`SpawnScale`) — never a raw `transform.localScale` write, which has no reset owner and leaks across reuse.

## OPEN: the damage-buff channel (next design task)

`DealDamage` has NO config fields — it reads the source's base `Damage.Amount` through `DamageProcessor.Resolve` (a deliberate seam with an unused multiplier overload). How runtime buffs/crits/level-ups reach a projectile's damage is UNDESIGNED; a `DamageModifier`-fold attempt was reverted. Candidates: a `Damage.Modifiers` contribution list (Family A of [[cross-component-communication]]) vs. a context-carried contribution. **Design it in a dedicated session; do not bolt it onto another feature.**

## Networking & gotchas

- Effects are data on prefabs/instances, resolved locally on every peer — never serialized over the wire.
- `[SerializeReference]` binds by type-NAME string (rename-fragile: add `[MovedFrom]` before renaming; authored instances go silently null otherwise); an empty list is a silent no-op; pin the MackySoft package. Full list: [[serializereference-gotchas]] memory.

## Checklist

1. Family of variants sharing a context → a command per variant; one-off → just a method.
2. Type to the leanest capability; concrete context only for a true dependency. New site → build a concrete context implementing the honest capability subset, resolve handles at the site.
3. Config: own scalar → field+ctor; shared/chunky → component + `source` ref; runtime tuning → modifier fold, never mutation.
4. No mutable state on commands. Name the system knob. Non-trivial math → `*.Core` helper + test.
5. Touching damage buffs? → STOP; that's the open design task.
