---
name: effect-commands
description: The IEffect<TContext> command system AND the presence-dispatched hit exchanges — how swappable gameplay behaviour (projectile hit reactions, ability effects, on-spawn buffs) is authored as inspector-picked command objects typed to a SITE in the concrete context tree, and how damage/knockback/hitstun fire from COMPONENT PRESENCE with no courier effect (ADR 0002). Use this BEFORE adding or editing any effect/buff/reaction, adding a context, deciding where an effect's config lives, touching a [SerializeReference]/[EffectSelector] list, adding a hit exchange or dealer/receiver component, or creating any new family of interchangeable behaviour variants. Also covers the flyweight/statelessness rules, stat-buffs-as-data, the rule of two for context interfaces, HitResolver as the one hit pipeline, the pooling two-layer rule, and the OPEN damage-buff design question (do not improvise it). Pairs with [[type-object-pattern]] (whose prefabs host the authored lists) and [[functional-core-imperative-shell]] (where pure math goes).
---

# Effect commands & hit exchanges

Two mechanisms cover all swappable combat behaviour, decided by **what varies** (ADR 0002):

- Varies in **behaviour** (a family of interchangeable reactions sharing one site) → an `IEffect<TContext>` **command** authored on a `[SerializeReference, EffectSelector]` list, buildable in code via the static `Commands` factory. A one-off with no siblings is just a method.
- Varies in **data** → plain authored data, no command. Standing pawn stat buffs are the canonical case: `PassiveAbility.MovementBuff`/`WeaponBuff` are serialized `MovementModifier`/`WeaponModifier` structs (default `Identity`; one row authors any mix of that system's multipliers). Six single-field buff commands used to do this — deleted.
- The **hit exchanges** (damage/knockback/hitstun) are NEITHER: they fire from **component presence** (below).

```csharp
public interface IEffect<in TContext> where TContext : EffectContext { void Apply(TContext context); }
```

## Contexts are a concrete class TREE, not capability interfaces

The shallow taxonomy of SITES an operation can run at (`EffectContexts.cs`); each level adds the handles its site resolves ONCE — an effect never `GetComponent`s off a raw archetype and gambles:

```
EffectContext (abstract)             Source, Instigator
├─ OnSpawnContext                    deliberately bare — on-spawn buffs GetComponent on the spawned object
└─ AimContext                        Point, Direction — the ability OnUse list; SpawnNetworkObjects types here
   └─ ProjectileImpact               +Normal, SourceCenterPoint, Motion, SelfHealth — BOTH the OnSurface and
      │                              OnPlayer lists (they differ by nothing since the exchanges left the lists)
      └─ ProjectileProjectileCollision  +OtherMotion — DeflectProjectile's true dependency
```

**Type an effect to the SHALLOWEST level that serves it.** `TContext` is contravariant and the `[EffectSelector]` drawer's variance test is plain class assignability, so an `IEffect<AimContext>` is offered on ability lists and every collision list; mis-slotting stays an authoring impossibility.

**Rule of two:** there are NO capability interfaces. The 16-interface lattice was deleted (ADR 0002) because the real sites form a tree and every interface had one consumer — the one-concretion hook killed the doctrinal route twice during the review's spikes. An interface returns only when a SECOND, genuinely non-tree site actually exists — never speculatively.

**Naming: name the system knob, never the archetype** (`BuffSpeed`/`BuffScale`/`BuffMaxHealth` — never `BuffProjectile*`). Contexts are named for the SITE (`ProjectileImpact`).

## Hit exchanges fire from COMPONENT PRESENCE (no courier, no authored row)

A "deal X on hit" is two components and nothing else:

- **Dealer** on the dealing entity: `Damage` / `KnockbackDealer` / `HitstunDealer` — authored baseline + public modifier list + `Effective*` fold. Server-read only.
- **Receiver** on the struck entity's ROOT (beside `Health`, one `GetComponentInParent` walk): `IDamageable` / `KnockbackReceiver` / `HitstunReceiver` — its own resistance fold. The receiver owns cross-peer application (ADR 0001, still standing).

`HitResolver.ResolveExchanges` fires each exchange iff dealer ∧ receiver are both present. **Carrying the dealer component IS the switch**: shove-without-damage = carry `KnockbackDealer`, omit `Damage`. Each end folds only its own modifiers. Pinned by `HitstunKnockbackTests.Stripping_the_hitstun_dealer_component_silences_only_that_exchange`.

- Exchanges run BEFORE the reaction lists, inside the same **atomic** hit: a classified hit resolves its exchanges and ALL authored reactions (prefab list, then owner contributions) even if the projectile dies mid-hit; death gates only the projectile channel, further hits, and despawn (pinned by `ProjectileDamageSmokeTests.Hit_resolves_atomically_regardless_of_effect_order`).
- Exchanges deliberately do NOT run in the projectile-vs-projectile channel (no content wants bullet-destroys-bullet; if ever wanted, that's a dealer-side victim-mask decision — see ADR 0002).
- An owner contribution can NOT stack an exchange (the pre-refactor double-`DealDamage` trick is inexpressible). Outgoing damage/knockback buffs are the **deferred damage-buff channel** (see OPEN below).
- Adding exchange #4: dealer + receiver + Core assembly + a few lines in `HitResolver.ResolveExchanges` + which-peer-simulates decision ([[fishnet-conventions]] Rule 4b) + a PlayMode test. No interfaces, no courier, no context change.

## HitResolver is THE hit pipeline

Anything that discovers targets at apply time — an explosion overlapping N victims, a cast hitting a pawn — resolves each victim through `HitResolver.ResolveExchanges(dealers…, victim, point, direction, instigator, damageMultiplier)` + `HitResolver.RunEffects(list, ctx)`. The `damageMultiplier` parameter is the per-TARGET fold slot (blast falloff). **Never hand-roll a private hit pipeline inside an effect** — both review spikes had to, which is why this is public.

## Where config lives — the decision rule

1. **A scalar read only by the effect that owns it** → `[SerializeField]` on the effect + a ctor for code composition.
2. **Chunky or shared config another system must read** (prefab fan lists, an entity's base `Damage`/`Health`) → a component, with an explicit serialized `source` ref on the effect (`SpawnNetworkObjects.source → SpawnableObjects`).
3. **Runtime tuning NEVER mutates authored values** — contribute a modifier folded at read/apply time. Mutation is pooling-unsafe and unstackable.
4. Balancing ergonomics is an editor-tooling problem, never a reason to move config.

**Flyweight discipline:** commands hold immutable config only — no state mutated during `Apply` (`[SerializeReference]` copy/paste can alias two slots to one instance). Class (never struct), `[Serializable]`, accessible parameterless ctor.

## Effects are the ONE deliberately Unity-coupled layer

`Apply` reaches the engine directly — pooled instantiate, FishNet spawn — on the handles the context resolved. No provider, no effect-data structs (built and deleted). Two bounds: cores stay pure; non-trivial math goes to a pure `*.Core` helper with EditMode tests (`RicochetMath`, `SpawnFanMath`, `DeflectMath`, `DamageProcessor`). Projectile collision is server-only and unpredicted, so direct I/O is correct there.

## The buff surfaces on a passive

- **Standing stat buffs = DATA**: `PassiveAbility.MovementBuff`/`WeaponBuff` structs; `PawnPassiveAbilities` collects non-Identity structs into `PawnMovement.Modifiers`/`PawnWeapon.Modifiers` on every roster re-derive (Remove-old/Add-new). The modifier STRUCTS are the fold currency the pure processors consume.
- **Owner projectile contributions** (`ProjectileOn*` lists): reaction commands aggregated server-side and run by the hit site after the prefab's own list — looked up from the owner id at hit time, never payloaded ([[fishnet-conventions]]). Reactions only — they cannot re-fire exchanges.
- **On-spawn buffs** (`OnSpawn`, `IEffect<OnSpawnContext>`): applied ONCE at spawn, on EVERY peer, by `SpawnBuffReceiver`. Semantics are a per-peer spawn-instant snapshot: properties with a sync channel self-heal on clients; pure cosmetics accept the snapshot — do NOT add sync channels for cosmetics.

**Pooling two-layer rule** (any spawn-time contributor): the authored **baseline** and the **per-spawn multiplier layer** never share a field; `effective = baseline × multiplier`; the multiplier resets to identity in `ResetState`, the baseline is never wiped. Setters are idempotent and order-independent (`SetSpeedMultiplier`, `SetMaxHealthMultiplier`, `SetRadiusMultiplier`).

## OPEN: the damage-buff channel (dedicated design session — do not improvise)

How runtime buffs/crits/level-ups reach a projectile's OUTGOING damage/knockback/hitstun is UNDESIGNED. The dealer-side `Modifiers` lists exist with no contributors; `HitResolver`'s `damageMultiplier` is the per-target slot; the old owner-contributed-courier stacking is gone and its capability (an owner adding flat damage) is owed by this channel. See DESIGN.md's open question for candidates and trigger.

## Networking & gotchas

- Effects are data on prefabs/instances, resolved locally on every peer — never serialized over the wire.
- `[SerializeReference]` binds by type-NAME string (rename-fragile: add `[MovedFrom]` before renaming an EFFECT class; authored instances go silently null otherwise); an empty list is a silent no-op; pin the MackySoft package. Context classes are only field types — renaming them is YAML-safe. Full list: [[serializereference-gotchas]] memory.

## Checklist

1. Varies in behaviour → a command per variant; varies in data → an authored struct/field; a one-off → a method; deal-X-on-hit → dealer + receiver components, no effect at all.
2. Type to the shallowest tree level; a new site → a new context CLASS at its tree position; an interface only at the second non-tree concretion (rule of two).
3. Config: own scalar → field+ctor; shared/chunky → component + `source` ref; runtime tuning → modifier fold, never mutation.
4. No mutable state on commands. Name the system knob. Non-trivial math → `*.Core` helper + test.
5. Apply-time target discovery → `HitResolver`, never a private pipeline.
6. Touching outgoing damage/knockback buffs? → STOP; that's the open damage-buff design session.
