---
name: effect-commands
description: How swappable combat behaviour is composed — behaviour that varies is a COMPONENT you attach, discovered by presence (GetComponents), so we own no dispatch machinery; behaviour that varies only in data is an authored struct. Covers hit reactions (IHitHandler components filtered by channel), the dealer+receiver hit exchanges (damage/knockback/hitstun fire from component presence, no courier), on-spawn buffs (a SpawnModifier struct), ability use-time spawns (SpawnNetworkObjects component), the dissolved-LoseHealth durability spend, HitResolver as the one hit pipeline, the rule of two (no speculative IUseHandler/HitDurability), the pooling two-layer rule, and the OPEN damage-buff design question. Use BEFORE adding or editing any hit reaction, buff, or ability behaviour, adding a handler component or a hit channel, adding a dealer/receiver exchange, deciding where a behaviour's config lives, or inventing a new family of interchangeable behaviour variants. Pairs with [[type-object-pattern]] (prefabs host these components) and [[functional-core-imperative-shell]] (where the math goes).
---

# Swappable combat behaviour

**The idea:** behaviour that *varies* is a **component you attach**; the site finds it by presence (`GetComponents`) and calls it. We own no dispatch machinery.

**Why (Elegance — engine-native beats owned):** Unity already gives us composition *and* discovery — components. Owning a second system for the same job (the deleted `IEffect` lists, the variance-aware picker + drawer, a command interface, a context class tree) was redundant machinery. ADR 0003 deleted all of it for `GetComponents` dispatch; that deletion is the receipt. Machinery earns its keep at *system interactions* — the upgrade axis, the essence — and here that machinery is the engine's, not ours.

**Decide by what varies:**
- Varies in **behaviour** → a component. A hit reaction is an `IHitHandler` (`RicochetHandler`, `InstantiateOnHitHandler`, `PassiveStealOnHit`); an ability's use-time spawn is a `SpawnNetworkObjects`.
- Varies in **data** → an authored struct, no behaviour-bearing type (`MovementModifier` / `WeaponModifier` / `SpawnModifier`, default `Identity`).
- **Deal-X-on-hit** → a dealer component meets a receiver component (below).
- A one-off with no siblings → just a method.

## Hit reactions: `IHitHandler`, channel = call site

One flat interface: `SupportedChannels` (what it CAN serve — a fact in code) + `EnabledChannels` (what this KIND does — serialized per prefab) + `OnHit(in HitContext)`. That split is the LayerMask idiom. The **channel is the call site** (`HitResolver.ResolveSurfaceHit` / `ResolvePlayerHit` / `ResolveProjectileHit`), not a type; one `HitContext` carries the handles the site resolved once (`OtherMotion` is populated only on the Projectile channel). A handler runs iff `Supported & Enabled & current`. `HitResolver` harvests the projectile's own components, then the owner's passive-contributed ones (`PawnPassiveAbilities.CollectHitHandlers`) — no serialized list, no `AddComponent` on pooled instances. Adding reaction N+1 = one component + prefab authoring + a PlayMode test.

## Hit exchanges: dealer + receiver presence (ADR 0002)

Damage / knockback / hitstun are not reactions. Each fires iff a **dealer** on the projectile (`Damage` / `KnockbackDealer` / `HitstunDealer`) meets its **receiver** on the struck entity's root (`IDamageable` / `KnockbackReceiver` / `HitstunReceiver`, one `GetComponentInParent` walk). Carrying the dealer IS the switch (shove-without-damage = `KnockbackDealer`, no `Damage`); each end folds only its own modifiers; they don't run bullet-vs-bullet. A hit is **atomic** — exchanges, the durability spend, and every reaction resolve together even if the projectile dies mid-hit; death gates only what comes after.

## The rest, briefly

- **Durability** — a projectile spends 1 per hit iff it carries a live `Health` (the dissolved `LoseHealth`); no `Health` = immortal. Per-channel cost, if ever wanted, is a future `HitDurability` component — not built.
- **On-spawn buffs** — `PassiveAbility.SpawnBuff` (a `SpawnModifier`: speed / scale / max-health multipliers). `SpawnBuffReceiver` folds the roster's buffs (product) on every peer at spawn, through each system's idempotent per-spawn multiplier seam — never a raw write. **Pooling two-layer rule:** `effective = baseline × multiplier`; the multiplier resets in `ResetState`, the baseline is never wiped.
- **Config** — a scalar the behaviour owns → a field; a shared/chunky list → a sibling data-holder (`SpawnableObjects` / `InstantiableObjects`) resolved by GetComponent. Non-trivial math → a pure `*.Core` helper + EditMode test (`RicochetMath`, `DeflectMath`, `SpawnFanMath`, `DamageProcessor`).
- **HitResolver is THE hit pipeline** — anything that discovers targets at apply time (an explosion over N victims, a cast) resolves through it; never hand-roll a private one.
- **Rule of two** — no interface with one concretion. `IHitHandler` earns it (many handlers + presence dispatch), and when an interface is warranted it lives **on the component** that carries the behaviour, not on a separate context. There is deliberately **no** `IUseHandler` — `SpawnNetworkObjects` is the only use-site behaviour, run by a concrete `GetComponents<SpawnNetworkObjects>()`.
- **Networking** — components/data resolve locally on every peer, never over the wire. The `[SerializeReference]` rename-fragility is gone for reactions (they're components); the MackySoft pin stays only because `NetworkedMotion`'s strategy field still uses `[SubclassSelector]`.

## OPEN: the damage-buff channel (dedicated design session — do not improvise)

How runtime buffs/crits/level-ups reach a projectile's **outgoing** damage/knockback/hitstun is undesigned: the dealer-side modifier lists have no contributors, and `HitResolver`'s `damageMultiplier` is the per-target slot. Don't improvise it, and don't grow `HitContext` for it — see DESIGN.md's open question.
