---
name: presence-dispatch
description: How swappable combat behaviour is composed — behaviour that varies is a COMPONENT discovered by presence (GetComponents), behaviour that varies only in data is an authored struct; we own no dispatch machinery. Use BEFORE adding or editing any hit reaction, buff, or ability behaviour; adding an IHitMessageHandler component, hit channel, or per-datum controller; deciding where a behaviour's config lives; or inventing a new family of interchangeable behaviour variants. Pairs with [[type-object-pattern]] and [[component-design]].
---

# Swappable combat behaviour

**The idea:** behaviour that *varies* is a **component you attach**; the site finds it by presence (`GetComponents`) and calls it. We own no dispatch machinery.

**Why (Elegance — engine-native beats owned):** Unity already gives us composition *and* discovery — components. Owning a second system for the same job (the deleted `IEffect` lists, the variance-aware picker + drawer, a command interface, a context class tree) was redundant machinery. ADR 0003 deleted all of it for `GetComponents` dispatch; that deletion is the receipt. Machinery earns its keep at *system interactions* — the upgrade axis, the essence — and here that machinery is the engine's, not ours.

**Decide by what varies:**
- Varies in **behaviour** → a component. A hit reaction is an `IHitMessageHandler` (`RedirectionController`, `InstantiationController`, `PassiveStealController`); an ability's use-time spawn is a `SpawnNetworkObjects`.
- Varies in **data** → an authored struct, no behaviour-bearing type (`MovementModifier` / `WeaponModifier` / `SpawnModifier`, default `Identity`).
- **Deal-X-on-hit** → a **controller** that owns both halves of the datum (below).
- A one-off with no siblings → just a method.

> **OPEN — why buffs-as-data, not presence-dispatch?** This runs TWO mechanisms: presence-discovered components for *behaviour*, authored structs for *data*. The data path buys terseness (one `SpawnModifier` authors any mix of speed/scale/health), but it is a second dispatch shape, and Elegance asks for the fewest. Could a buff instead be a component the fold discovers by presence, unifying on one mechanism? Revisit this before adding a THIRD data-buff surface (the damage-buff channel is the likely trigger) — decide whether the data/behaviour split earns its second mechanism or should collapse into presence dispatch.

## The hit context: one typed `HitMessage`, dispatched to `IHitMessageHandler`s (ADR 0005)

Hit is the ONE fan-out context — many unrelated systems (damage, knockback, hitstun, redirect, steal) respond to one event on the source projectile. So it is a **pooled, typed `ExecuteEvents` message** (`HitMessage`), and we own **no dispatch machinery**: one message type, one static pool, one channel-guard functor, zero dispatch classes. The collision site builds a `HitMessage` and hands it to `HitMessage.Dispatch`, which runs it against every `IHitMessageHandler` **on the source**, in component order, via engine-maintained `ExecuteEvents`.

- A handler declares only `EnabledHitChannels` (which channel it reacts on — serialized per prefab where it varies, a code constant where nothing does). The channel guard (enabled ∩ current channel) lives **only** in `HitMessage.Functor`; handler bodies are unconditional. What a handler ACTS ON it resolves itself from the message's `Source`/`Victim` by `GetComponent` — so no pooled component ref can go stale.
- **Disabled component = silent** (`ExecuteEvents` skips it) — this IS the composability/strip hook; `ResetState` re-enables.
- **`HitMessage.Dispatch` is the atomic hit sequence:** durability spent FIRST (presence of a live `HealthController` on the source is the switch), then every handler runs unconditionally — so authored **order can never change a hit's outcome** and a mid-hit death can't retract a handler (pinned by `HitDispatchOrderTests` + `HitAtomicityTests`). Death gates only despawn afterwards.
- **Any handler may build and dispatch a further `HitMessage`** (an explosion, a ricochet) — layers are vocabulary, not a budget. **Extraction trigger:** the first second-hit-source (explosion over N victims, a cast) *extracts* the sequence to a shared home + ships the deferred cycle-guard with it — never hand-rolls a private pipeline.
- Adding reaction N+1 = one `IHitMessageHandler` component + prefab authoring + a PlayMode test.

## Deal-X-on-hit: per-datum controllers, both halves in one home (ADR 0005)

Damage / knockback / hitstun are dealt through **controllers**, not a dealer-meets-receiver pair. A `<X>Controller` owns state and exposes an API to modify it (the state-owning half of the dispatch model): `KnockbackController`/`HitstunController` each merge the old dealer+receiver and inline the old `*.Core` arithmetic; `DamageController` owns the outgoing `Amount`; `HealthController` is the hit-point controller. The **dealer half** is a layer-2 `IHitMessageHandler` — on a Player-channel hit it resolves the outgoing value and delivers it with a **direct call** into the victim's own controller (one `GetComponentInParent` walk to the entity root — a single-receiver operation, not a message). The **receiver half** is the layer-3 operation (`ApplyKnockback`/`ApplyHitstun`/`TakeDamage`). Carrying the controller IS the switch (shove-without-damage = `KnockbackController`, no `DamageController`); each end folds only its own modifier registry; controllers don't run bullet-vs-bullet.

## The rest, briefly

- **Durability** — a projectile spends 1 per hit iff it carries a live `HealthController` (spent first in `HitMessage.Dispatch`); no `HealthController` = immortal. Per-channel cost, if ever wanted, is a future `HitDurability` component — not built.
- **On-spawn buffs** — `PassiveAbility.SpawnBuff` (a `SpawnModifier`: speed / scale / max-health multipliers). `SpawnContributionReceiver` folds the roster's buffs (product) on every peer at spawn — and also applies each passive's `IOwnedObjectContribution` hit handlers — through each system's idempotent per-spawn multiplier seam, never a raw write. **Pooling two-layer rule:** `effective = baseline × multiplier`; the multiplier resets in `ResetState`, the baseline is never wiped.
- **Config** — a scalar the behaviour owns → a field; a shared/chunky list → the controller holds its own (`InstantiationController` carries its prefab list) or a sibling data-holder (`SpawnableObjects`) resolved by GetComponent. Non-trivial math → a pure `*.Core` helper + EditMode test **only when determinism-critical or heavily branchy** (`SpawnFanMath`); the hit datums' arithmetic (knockback angle-clamp, hitstun window, damage clamp, redirect) is server-resolved-then-shipped, so it is inlined into its controller, not a core (ADR 0005 corrects ADR 0008's kept-list for `KnockbackHelper`/`HitstunProcessor`/`DamageProcessor`/`DeflectMath`/`RicochetMath`).
- **`HitMessage.Dispatch` is THE hit pipeline** — anything that discovers targets at apply time (an explosion over N victims, a cast) dispatches a `HitMessage` through it or extracts the sequence to a shared home; never hand-roll a private one.
- **Rule of two** — no interface with one concretion. `IHitMessageHandler` earns it (many handlers + presence dispatch), and when an interface is warranted it lives **on the component** that carries the behaviour, not on a separate context. There is deliberately **no** `IUseHandler` — `SpawnNetworkObjects` is the only use-site behaviour, run by a concrete `GetComponents<SpawnNetworkObjects>()`.
- **Networking** — components/data resolve locally on every peer, never over the wire. The `[SerializeReference]` rename-fragility is gone for reactions (they're components); the MackySoft pin stays only because `NetworkedMotion`'s strategy field still uses `[SubclassSelector]`.

## OPEN: the damage-buff channel (dedicated design session — do not improvise)

How runtime buffs/crits/level-ups reach a projectile's **outgoing** damage/knockback/hitstun is undesigned: the controllers' `OutgoingModifiers` registries exist but have no contributors. The old per-target `damageMultiplier` slot on `HitResolver` **is gone** (deleted with the class) — a per-target multiplier (blast falloff) must be **re-established** as a fresh seam, not filled. Don't improvise it, and don't grow `HitMessage` for it — see DESIGN.md's open question.
