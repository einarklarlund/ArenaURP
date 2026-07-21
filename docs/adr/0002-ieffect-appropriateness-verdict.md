# ADR 0002 — IEffect verdict: keep the command core, delete the lattice, the couriers, and the data-only buffs

- **Status:** Accepted (2026-07-13; review run the same day per the "Questioning an existing design" evidence method — then in `ai-workflow`, since 2026-07-15 in `engineering-canon`). **Superseded by [0003](0003-hit-reactions-as-handler-components.md)** *in part* — the "keep the command core" clause and the on-spawn-buffs-stay-commands claim; the deletions, the exchanges, both-end folds, and the fan-out site stand.
- **Date:** 2026-07-13
- **Supersedes:** ADR 0001 *in part* — its courier layer only. The dealer/receiver components, both-end modifier folds, and receiver-owned cross-peer application all stand. (Add the one-line note to 0001 on acceptance.)

## Context

DESIGN.md scheduled this review: is `IEffect<TContext>` + the capability-context system over-engineered? Evidence gathered before arguing: the six baseline counts re-verified by an independent scout, three compiled worktree spikes (proximity explosion, passive steal, movement steal — each chosen to break a different core assumption), and two bias-separated advocate briefs (keep vs replace).

## Decision

**REFACTOR.** Keep the core every spike validated: `[Serializable]` command objects on `[SerializeReference]` lists, the variance-aware picker, statelessness/flyweight rules, atomic hit resolution, site-resolves-handles, and the dealer/receiver folds. Delete three layers that encode no independent information, and add the one site the spikes proved missing:

1. **Capability-interface lattice (16 interfaces) → a shallow tree of ~5 concrete context classes.** The shipped contexts form a strict tree; the picker's `IsAssignableFrom` test works unchanged on classes. An interface returns only at a second concretion (rule of two).
2. **Courier effects (`DealDamage`/`DealKnockback`/`DealHitstun`) + their 9 source/target/exchange interfaces → presence-dispatch in `ServerCollision`**: an exchange fires iff dealer component ∧ receiver component. The site already resolves both ends; courier-without-dealer was already a logged error — two switches, one decision. Composability survives as component presence (shove without damage = carry `KnockbackDealer`, omit `Damage`).
3. **The 6 single-field sink-buffs + 2 sink interfaces + `PawnStatBuff` context → authored `List<MovementModifier>`/`List<WeaponModifier>` on `PassiveAbility`.** They vary in data, not behaviour; one struct row replaces stacked one-float effects. The 3 on-spawn buffs stay commands (they vary in behaviour).
4. **Extract the fan-out site both target-discovering spikes had to pirate**: a public `HitResolver.Resolve(dealers, victim, point, perTargetMultiplier)` from `ServerCollision.RunEffects`, giving apply-time target discovery (explosions, casts) first-class dispatch and the per-target multiplier slot spike 1 proved has no home today.
5. **Delete dead weight now**: `ApplyDebuff` (no-op), `DebuffType` (referenced by nothing), `IProjectileEffectProvider` (one implementer — inline it).

Net: ~29 machinery types serving 18 effects → roughly 13 serving ~9, with every property that held in the spikes preserved or strengthened. Re-run the baseline counts after migration (the method requires it).

## Alternatives rejected

- **Keep with amendments** (the keep brief): its best points are absorbed (fan-out site, dead-weight deletion, marginal-cost framing). Rejected as a verdict because the deleted layers empirically encode zero bits — every exchange composite serves exactly one effect, courier∧dealer is one switch — and the doctrinal lean-capability route was killed by the project's own one-concretion hook in *both* spikes that attempted it. Doctrine and enforcement disagreed; enforcement won every trial. The refactor makes them agree by construction instead of by an exemption sentence.
- **Full replacement** (enum dispatch / methods on dealer components, no command objects): destroys per-prefab composed authoring — the essence's content axis (upgrades as inspector-authored lists). Everything the spikes showed holding lives in the command core.
- **ADR 0001's rejected alternatives stay rejected** (config-on-courier, GetComponent-off-Source, receiver interfaces). No new evidence favored any of them.

## Evidence

- **Baselines (corrected from the critic's pass):** 29 core machinery types (not 23) for 18 effects; 5 shapes + 1 no-op (not ~4); hitstun cost ~16 non-test files (not ~6); 9 of 18 effects carry one float (the "0 tunables" claim was true only of collision-list effects); runtime-tunable fraction 0 confirmed, with one doctrine violation (`Damage.Amount` public setter — the open damage-buff gap).
- **Spikes:** all three tripped DESIGN.md's pre-registered refactor threshold (pattern exception and/or parallel mechanism), yet converged on ONE structural gap — a context can only carry participants the site already knows, so an effect that discovers targets at apply time becomes a private site (spike 1: pirated `RunEffects` copy + meta-capability; spike 2: private cast/classify pipeline). Everything else held at 60–120 executable LOC per essence-grade feature. Spike 3's sharpest strain (dealer-configured magnitude on receiver-simulated state needs a TargetRpc payload) is a simulation-seam problem any architecture inherits — set aside as an open question, not an IEffect verdict.
- **Spike 4 (not built, resolved by analysis):** scalable/resistible knockback is the dealing-end contribution channel — i.e. the deferred damage-buff question, no new hit plumbing. Spike 1 adds a third requirement to that session: a *per-target* multiplier (falloff), which lands naturally at the extracted site fold.
- Spike code (throwaway, do not polish into features): `.claude/worktrees/agent-abe90da063abefaa3` (explosion), `agent-ae993e4fdab681d07` (steal), `agent-a68c564edee14ac42` (movement steal).

## Consequences

- **Easy:** explosion ≈ 1 new type (vs 4 in the spike, with no meta-capability or pipeline copy); exchange N+1 ≈ half the files; the lean-capability-vs-one-concretion contradiction dissolves; blast/cast features get a lawful dispatch path.
- **Work:** migration touches `ServerCollision`, `IEffect.cs`/`EffectContexts.cs`, `PassiveAbility` authoring, and the Bullet/Pawn Root prefab lists — every networked change gets its PlayMode test; rewrite the `effect-commands` skill on acceptance (not before); re-run baselines after.
- **Deferred (DESIGN.md open questions, each with its trigger):** damage-buff channel session (now owed the per-target multiplier requirement); a generic timed-modifier lifetime (trigger: first shipped timed effect — build it once, not per-system twins); the sanctioned wire-payload class for dealer-configured, receiver-simulated effects (trigger: shipping a movement-steal-like effect; lands in `fishnet-conventions`).

**North-star rationale:** Essence — the deleted layers maximized type-safety for single-target site-resolved hits, while the marquee upgrade interactions (explosions, steals, drains) discover targets at apply time, which the lattice structurally cannot express; the kept core plus the fan-out site is what serves them. Elegance — ~18 fewer types, zero lost cases, and the doctrine now matches what the hooks enforce.

---

**Outcome (2026-07-13, migration complete, all suites green — 285 EditMode / 46 PlayMode):** measured post-refactor counts beat the prediction — machinery 29 → **10** types (predicted ~13; `HitResolver` counted as collision site per the baseline's own rule), effects 18 → **8**, effect-layer LOC 1140 → 568. Two migration discoveries amended the evidence: (1) stacked couriers DID encode one bit — an owner-contributed second `DealDamage` doubled damage (test-only usage; capability formally moved to the deferred damage-buff channel); (2) the courier victim-class asymmetry was dead weight because the projectile channel is gated off by list-emptiness — exchanges now simply don't run in that channel, and projectile-vs-projectile damage is an explicit future decision (dealer-side victim mask). Surface and player contexts merged into one `ProjectileImpact` — with exchanges gone, nothing distinguished them.
