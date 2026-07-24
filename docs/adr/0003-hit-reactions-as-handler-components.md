# ADR 0003 — Hit reactions are presence-discovered handler components; the command core retires

- **Status:** Accepted (2026-07-14; evidence spike run the same day)
- **Date:** 2026-07-14
- **Supersedes:** ADR 0002 *in part* — its "keep the command core" decision and its "the 3 on-spawn buffs stay commands" claim. Everything 0002 deleted stays deleted; its presence-dispatched exchanges, both-end folds, atomic-hit rule, and public fan-out site all stand and are what this shape extends.
- **Superseded in part by [0005](0005-hit-pipeline-layered-messages.md)** — the `IHitHandler`/`HitContext`/`HitResolver` dispatch surface (replaced by `HitMessage`/`IHitMessageHandler` + per-datum controllers); the presence-discovery principle and the command-core deletion stand.

## Context

ADR 0002 kept `IEffect<TContext>` command lists as the reaction mechanism (10 machinery types / 8 effects / 568 LOC). Re-examined under the Elegance value's engine-native test: command lists and component discovery are BOTH fully SOLID-open, so conformance cannot pick — but the command core is OWNED machinery (a `[SerializeReference]` drawer, a variance-aware picker, the MackySoft pin, flyweight/aliasing discipline, rename-fragility), while component discovery is the engine's own composition unit. The session's first shape (typed `IHitHandler<TContext>` + context subclasses) was revised the same day: channel-as-type re-grows a class tree whose only information is the call site.

## Decision

**Replace the `IEffect` reaction lists with presence-discovered `IHitHandler` MonoBehaviours; channel is the call site, not a type.**

1. **Dispatch:** one flat `IHitHandler` (`SupportedChannels` + `EnabledChannels` + `OnHit(in HitContext)`), a `HitChannels` flags enum, ONE `HitContext` readonly struct. `HitResolver` runs each channel's exchanges, then every harvested handler whose `Supported ∩ Enabled ∩ current ≠ ∅` — the LayerMask idiom: what a behaviour CAN do is code, what a KIND does is per-prefab-copy authoring.
2. **Harvest = presence:** the projectile's own handler components first, then the owner's (authored on the passive instance prefabs, collected at the hit site). No serialized handler list anywhere; no `AddComponent` on pooled instances.
3. **The triage rule — the resolver enumerates mechanisms, never content:** varies-in-behaviour → a handler component owning real config/behaviour; varies-in-data → an authored struct; pure forwarding → presence, no type at all.
4. **Migration targets:** `Ricochet`/`DeflectProjectile`/`InstantiateGameObjects` → handler components; the 3 on-spawn buffs → ONE identity-default `SpawnModifier` struct folded by `SpawnBuffReceiver`; `LoseHealth` dissolves (carrying `Health` IS the durability switch); `SpawnNetworkObjects` → concrete component found by `GetComponents` (no `IUseHandler` until a second use-site effect — rule of two). Then DELETE `IEffect<T>`, the five context classes, the `[EffectSelector]` drawer, and `Commands`.

## Alternatives rejected

- **Typed/contravariant `IHitHandler<TContext>` + context subclasses:** a type parameter that restates the call site is machinery without bits; contravariant `GetComponents` dispatch re-owns the variance test the drawer used to own.
- **Keep the command core, handlers only for new content:** two reaction mechanisms forever; fails Elegance's merge rule with no coverage gain.
- **Enum/method dispatch on the dealer components:** destroys per-prefab composed authoring (unchanged from 0002).

## Evidence

Spike (branch `worktree-ieffect-component-handler-spike`): passive-steal-ON-HIT built as an owner-contributed handler, exercising the harvest path, the channel filter, and the interface's rule-of-two credentials. Counts as predicted — 3 machinery types + ~70-line dispatch in `HitResolver`, which was NOT edited for content; the feature cost one 56-LOC component. Suites green (EditMode 285/285; PlayMode incl. two new tests — the steal moves the KIND across owners, and an authored channel mask of `None` silences the steal while damage still lands). Interface delta: the authored mask must be readable AT the dispatch site, so `IHitHandler` carries `EnabledChannels` beside `SupportedChannels`. One production gap surfaced (feature-owned, fixed engine-native): a despawned passive corpse stays enumerable until end-of-frame, so `GetPassives` filters on FishNet's `IsSpawned` and the stop-path notify passes the leaver for self-exclusion. Known gate: `ServerCollision.WantsProjectileChannel` must learn handlers before any projectile-channel handler ships.

## Consequences

- **Work:** migrate the four remaining effects, re-author the Bullet/ability prefab lists as components (editor session), delete the command core, re-run the baseline counts, rewrite the `effect-commands` skill on acceptance — not before.
- **Easy:** reaction N+1 = one component + prefab authoring; the `[SerializeReference]` gotchas retire with the machinery; flyweight/aliasing discipline relaxes to the normal pooling rule.
- **Deferred, unchanged:** `HitContext` must not grow damage-buff-channel fields (the damage-buff session owns outgoing-modifier plumbing); the timed-modifier lifetime; the wire-payload class.

**North-star rationale:** Essence — reactions stay per-prefab composed content, which is the upgrade axis. Elegance — the engine's own composition unit replaces owned machinery; one dispatch mechanism, and the channel taxonomy lives in exactly one place (the call sites).

## Realized (2026-07-14, shipped)

Migration complete and green (285 EditMode / 47 PlayMode). Machinery **11 → 4** (`IHitHandler`/`HitChannels`/`HitContext`/`HitResolver`) — deleting `IEffect<T>`, the five context classes, the `[EffectSelector]` attribute + drawer, the `Commands` factory, and `ProjectileEffects`. **Correction — the MackySoft pin STAYS:** it was never the effect system's dependency; `NetworkedMotion`'s motion-strategy field still uses MackySoft's `[SubclassSelector]`. The §4 instruction to delete the pin was wrong and was not followed. `DeflectProjectileHandler` shipped but authored on zero content — the projectile channel has no live PlayMode coverage; a deflection test needs new infra, deferred and noted.
