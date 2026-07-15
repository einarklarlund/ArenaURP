# ADR 0003 — Hit reactions are presence-discovered handler components; the command core retires

- **Status:** Accepted (2026-07-14; evidence spike run the same day)
- **Date:** 2026-07-14
- **Supersedes:** ADR 0002 *in part* — its "keep the command core" decision and its "the 3 on-spawn buffs stay commands (they vary in behaviour)" claim. Everything 0002 deleted stays deleted; its presence-dispatched exchanges, both-end folds, atomic-hit rule, and public fan-out site all stand and are what this shape extends.

## Context

ADR 0002 kept `IEffect<TContext>` command lists as the reaction mechanism (10 machinery types / 8 effects / 568 LOC). The 2026-07-14 hit-handler session re-examined that clause under the Elegance value's engine-native test: command lists and component discovery are BOTH fully SOLID-open, so conformance cannot pick — but the command core is OWNED machinery (a `[SerializeReference]` drawer, a variance-aware picker, the MackySoft pin, flyweight/aliasing discipline, rename-fragility), while component discovery is the engine's own composition unit. The session's first shape (typed `IHitHandler<TContext>` + context subclasses) was itself revised the same day by the typed-context/bridge-component review: channel-as-type re-grows a class tree whose only information is the call site.

## Decision

**Replace the `IEffect` reaction lists with presence-discovered `IHitHandler` MonoBehaviours; channel is the call site, not a type.**

1. **Dispatch:** one flat `IHitHandler` (`SupportedChannels` + `EnabledChannels` + `OnHit(in HitContext)`), a `HitChannels` flags enum, ONE `HitContext` readonly struct (`OtherMotion` populated only on the Projectile channel). `HitResolver` gains `ResolveSurfaceHit`/`ResolvePlayerHit`/`ResolveProjectileHit`; each runs the exchanges its channel has, then every harvested handler whose `Supported ∩ Enabled ∩ current ≠ ∅` — the LayerMask idiom: what a behaviour CAN do is code, what a KIND does is per-prefab-copy authoring.
2. **Harvest = presence:** the projectile's own handler components first, then the owner's — components authored on the passive instance prefabs, collected at the hit site (`PawnPassiveAbilities.CollectHitHandlers`). No serialized handler list anywhere; no `AddComponent` on pooled instances.
3. **The triage rule — the resolver enumerates mechanisms, never content:** varies-in-behaviour → a handler component that owns real config/behaviour (a body that is a single forward to a handle the context resolved is a courier reborn — use presence dispatch or data); varies-in-data → an authored struct; pure forwarding → presence, no type at all.
4. **Migration targets:** `Ricochet`/`DeflectProjectile`/`InstantiateGameObjects` → handler components; `BuffSpeed`+`BuffScale`+`BuffMaxHealth` → ONE identity-default `SpawnModifier` struct on `PassiveAbility` folded by `SpawnBuffReceiver` (the on-spawn SITE dissolves into a data fold; no handler/resolver/applier trio); `LoseHealth` dissolves (carrying `Health` IS the durability switch); `SpawnNetworkObjects` → concrete component on the active instance prefab via `GetComponents<SpawnNetworkObjects>()` (no `IUseHandler` until a second use-site effect — rule of two). Then DELETE `IEffect<T>`, the five context classes, the `[EffectSelector]` drawer, `Commands`, and the MackySoft pin.

## Alternatives rejected

- **Typed/contravariant `IHitHandler<TContext>` + context subclasses** (the thread's own first shape): the context tree's only remaining information after the exchanges left the lists WAS the call site; a type parameter that restates the call site is machinery without bits, and contravariant `GetComponents` dispatch re-owns the variance test the drawer used to own. Killed by the same review that killed the bridge components.
- **Keep the command core, handlers only for new content:** two reaction mechanisms forever; fails Elegance's merge rule with no coverage gain.
- **Enum/method dispatch on the dealer components:** still destroys per-prefab composed authoring (unchanged from 0002).

## Evidence

This spike (branch `worktree-ieffect-component-handler-spike`): passive-steal-ON-HIT built as an owner-contributed handler — one feature exercising the harvest path, the channel filter, and the interface's rule-of-two credentials.

- **Counts, as predicted:** 3 machinery types (`HitChannels` 20 / `HitContext` 58 / `IHitHandler` 20 LOC) + dispatch in `HitResolver` (~70 lines) + one harvest seam; the resolver was NOT edited for content. The feature cost: ONE 56-LOC component + the `RemovePassive` capability it genuinely needed (the first individual passive removal — `Despawn` + the existing OnStop re-derive) + a test seam.
- **Suites:** EditMode 285/285; PlayMode green including two new tests — the steal moves the KIND (instance despawns on the victim, fresh instance granted to the attacker, the weapon-buff fold swaps sides), and an authored channel mask of `None` silences the steal while the damage exchange still lands on the same hits.
- **Interface delta discovered:** the authored mask must be readable AT the dispatch site, so `IHitHandler` carries `EnabledChannels` beside `SupportedChannels` — two properties, not one.
- **Production gap exposed (feature-owned, not shape-owned):** the first individual passive removal found that a despawned instance's corpse stays enumerable as a child until end-of-frame destruction, and FishNet flips `IsSpawned` only AFTER the stop callbacks (instrumented: the corpse is counted, spawned-flagged, and parented while its own OnStop notify re-derives) — so the OnStop-triggered re-derive would re-fold the dying instance's buffs with nothing re-running after destruction. Fixed engine-native: `GetPassives` filters on FishNet's `IsSpawned`, and the stop-path notify passes the leaver for self-exclusion (the RemoveAt-before-Despawn removal-first discipline, extended to the passives side — no bespoke lifecycle flag).
- **Known gate:** `ServerCollision.WantsProjectileChannel` still wakes the projectile channel off the legacy lists only; it must learn handlers before any projectile-channel handler ships (noted in code).

## Consequences

- **Work:** migrate the four remaining effects per §4, re-author the Bullet/ability prefab lists as components (editor session — MCP on a live editor), delete the command core, re-run the baseline counts (the method requires it; fix the 10-vs-11 inclusion-rule ambiguity noted 2026-07-14 while at it), rewrite the `effect-commands` skill on acceptance of the migrated state — not before, while the legacy lists still run beside the handlers.
- **Easy:** reaction N+1 = one component + prefab authoring; the `[SerializeReference]` gotchas (rename-fragility, silent-null lists, the pin) retire with the machinery; flyweight/aliasing discipline relaxes to the normal pooling rule.
- **Deferred, unchanged:** `HitContext` must not grow damage-buff-channel fields — the damage-buff session owns outgoing-modifier plumbing; the timed-modifier lifetime; the wire-payload class.

**North-star rationale:** Essence — reactions stay per-prefab composed content, which is the upgrade axis. Elegance — the engine's own composition unit (components + `GetComponents`) replaces owned machinery; one dispatch mechanism, fewer types, and the channel taxonomy lives in exactly one place (the call sites).
