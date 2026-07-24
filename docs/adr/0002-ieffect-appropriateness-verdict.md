# ADR 0002 — IEffect verdict: keep the command core, delete the lattice, the couriers, and the data-only buffs

- **Status:** Accepted (2026-07-13; review run the same day per the "Questioning an existing design" evidence method). **Superseded by [0003](0003-hit-reactions-as-handler-components.md)** *in part* — the "keep the command core" clause and the on-spawn-buffs-stay-commands claim; the deletions, the exchanges, both-end folds, and the fan-out site stand.
- **Date:** 2026-07-13
- **Supersedes:** ADR 0001 *in part* — its courier layer only. The dealer/receiver components, both-end modifier folds, and receiver-owned cross-peer application all stand.

## Context

DESIGN.md scheduled this review: is `IEffect<TContext>` + the capability-context system over-engineered? Evidence gathered before arguing: the six baseline counts re-verified by an independent scout, three compiled worktree spikes (proximity explosion, passive steal, movement steal — each chosen to break a different core assumption), and two bias-separated advocate briefs (keep vs replace).

## Decision

**REFACTOR.** Keep the core every spike validated (command objects on `[SerializeReference]` lists, the variance-aware picker, statelessness/flyweight rules, atomic hit resolution, site-resolves-handles, the dealer/receiver folds). Delete three layers that encode no independent information, and add the one site the spikes proved missing:

1. **Capability-interface lattice (16 interfaces) → ~5 concrete context classes.** The shipped contexts form a strict tree; an interface returns only at a second concretion (rule of two).
2. **Courier effects (`DealDamage`/`DealKnockback`/`DealHitstun`) + their 9 interfaces → presence-dispatch in `ServerCollision`:** an exchange fires iff dealer component ∧ receiver component. Composability survives as component presence (shove without damage = carry `KnockbackDealer`, omit `Damage`).
3. **The 6 single-field sink-buffs + 2 sink interfaces + `PawnStatBuff` context → authored `List<MovementModifier>`/`List<WeaponModifier>` on `PassiveAbility`.** They vary in data, not behaviour. (The 3 on-spawn buffs stay commands — the clause 0003 later superseded.)
4. **Extract the fan-out site both target-discovering spikes had to pirate:** a public `HitResolver.Resolve(dealers, victim, point, perTargetMultiplier)`, giving apply-time target discovery (explosions, casts) first-class dispatch and the per-target multiplier a home.
5. **Delete dead weight:** `ApplyDebuff` (no-op), `DebuffType` (unreferenced), `IProjectileEffectProvider` (one implementer — inline it).

Re-run the baseline counts after migration (the method requires it).

## Alternatives rejected

- **Keep with amendments** (the keep brief): its best points are absorbed; rejected as a verdict because the deleted layers empirically encode zero bits, and the doctrinal lean-capability route was killed by the project's own one-concretion hook in *both* spikes that attempted it — enforcement won every trial.
- **Full replacement** (enum dispatch / methods on dealer components, no command objects): destroys per-prefab composed authoring — the essence's content axis.
- **ADR 0001's rejected alternatives stay rejected.** No new evidence favored any of them.

## Evidence

- **Baselines (corrected from the critic's pass):** 29 core machinery types (not 23) for 18 effects; 9 of 18 effects carry one float; runtime-tunable fraction 0 confirmed, with one doctrine violation (`Damage.Amount` public setter — the open damage-buff gap).
- **Spikes:** all three tripped DESIGN.md's pre-registered refactor threshold, yet converged on ONE structural gap — a context can only carry participants the site already knows, so an effect that discovers targets at apply time becomes a private pirated site. Everything else held at 60–120 executable LOC per essence-grade feature. Spike 3's sharpest strain (dealer-configured magnitude on receiver-simulated state needs a TargetRpc payload) is a simulation-seam problem any architecture inherits — set aside as an open question.
- **Spike 4 (not built, resolved by analysis):** scalable/resistible knockback is the deferred damage-buff channel; spike 1 adds a *per-target* multiplier (falloff) requirement, which lands at the extracted site fold.

## Consequences

- **Easy:** explosion ≈ 1 new type (vs 4 in the spike); exchange N+1 ≈ half the files; the lean-capability-vs-one-concretion contradiction dissolves; blast/cast features get a lawful dispatch path.
- **Work:** migration touches `ServerCollision`, the effect/context files, `PassiveAbility` authoring, and the Bullet/Pawn Root prefab lists — every networked change gets its PlayMode test; rewrite the `effect-commands` skill on acceptance; re-run baselines after.
- **Deferred (each with its trigger):** the damage-buff channel session (now owed the per-target multiplier); a generic timed-modifier lifetime (trigger: first shipped timed effect); the sanctioned wire-payload class (trigger: shipping a movement-steal-like effect; lands in `fishnet-conventions`).

**North-star rationale:** Essence — the deleted layers maximized type-safety for single-target site-resolved hits, while the marquee upgrade interactions (explosions, steals, drains) discover targets at apply time, which the lattice structurally cannot express. Elegance — ~18 fewer types, zero lost cases, and the doctrine now matches what the hooks enforce.

---

**Outcome (2026-07-13, migration complete, all suites green — 285 EditMode / 46 PlayMode):** measured post-refactor counts beat the prediction — machinery 29 → **10** types (predicted ~13), effects 18 → **8**, effect-layer LOC 1140 → 568. Two amendments: (1) stacked couriers DID encode one bit — an owner-contributed second `DealDamage` doubled damage (test-only usage; capability formally moved to the deferred damage-buff channel); (2) the projectile channel is gated off by list-emptiness, so projectile-vs-projectile damage is an explicit future decision (dealer-side victim mask). Surface and player contexts merged into one `ProjectileImpact` — with exchanges gone, nothing distinguished them.
