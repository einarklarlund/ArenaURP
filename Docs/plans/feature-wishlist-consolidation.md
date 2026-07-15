> Generated 2026-07-15 by a gate→decompose→synthesize→adversarial-critique multi-agent workflow over the 13-feature wishlist. Leans and cuts are the strict-essence reading, NOT decisions — the survivors (A1, P1, P2, P5) and the ADR agenda are the actionable output; the 8 cuts and Q1/Q2 leans still want the six-lens canon interview before anything is settled.

# Combat/Upgrade Feature Consolidation — Final Report

Handoff artifact. Distills a full Effect×Dispatch analysis of 13 proposed features (A1–A6 actives, P1–P7 passives) against the game ESSENCE (character-BUILDING via a handful of interacting systems, NOT map/resource control), the SCOPE GUARD (an addition must displace, not append), and the mechanism doctrine (`presence-dispatch`: behaviour-that-varies = a component found by presence; data-that-varies = an authored struct; we own NO dispatch machinery — ADR 0003 deleted it).

**Headline result:** Four features survive (A1, P1, P2, P5); one is sent back for re-pitch (P4); eight are cut. **The surviving set needs ZERO net-new mechanisms** — every keeper lands on an existing seam (or a two-field extension of one). Every proposed new mechanism (damage-buff channel as a unified build, generic timed receiver, `IUseHandler`, additive-movement fold) is deferred: each has exactly one surviving consumer and fails rule-of-two. This is an elegance confirmation, not a shortfall.

---

## 1. GATE RESULTS

### Survives (keep)
- **A1 — passive-steal active** (hitscan capsulecast + wind-up that steals a passive from the target). Deepens the essence's top axis (ability↔ability); its payload is another upgrade. Roadmap spike 2 verbatim. Adds no system.
- **P1 — combo fire-rate passive** (each hit raises fire rate + refreshes a combo timer). Closes a real cross-system loop: more projectiles → more hits → higher fire rate. Reads existing hit-dealt event, writes existing `PawnWeapon.Modifiers` fold.
- **P2 — bigger blasts + reduced self-damage** (splash mastery / rocket-jump). Composes projectiles×movement×health; rocket-jump becomes a repeatable mobility tool. Both halves ride existing seams. **Sequenced AFTER roadmap items 1 (proximity explosion) and 3 (Q1 session).**
- **P5 — hits stun the enemy's weapon** (cannot fire briefly). Textbook essence: an upgrade that changes how the ENEMY plays. Mechanism already shipped (HitstunDealer/HitstunReceiver). It is authored config + a passive contributor, not net-new code.

### Borderline (re-pitch or cut)
- **P4 — "wings" / extra air-jumps.** As framed it is an enemy-blind solo-traversal gadget that composes into nothing AND invents a new fold contract (an additive field in the uniformly-multiplicative `MovementModifier`). **Do NOT build as specified.** Keep ONLY if re-pitched around an enemy-facing interaction (air-jumps as knockback-recovery vs. an enemy's launch); even then prefer the existing impulse lane over a second fold semantics. No re-pitch → cut.

### Cut as one-offs (one-line reason each)
- **A2 — reflect shield:** the block half negates an interaction (anti-essence); needs a victim-side hit-received dispatch that doesn't exist, for one consumer.
- **A3 — pepper-shaker blind/knock-up:** its identity is a vision-denial gag composing into no upgrade axis; demands a new perception system + hit-received + 3 deferred questions. Residue (AoE + up-knock) is already covered by proximity-explosion.
- **A4 — blinding flashlight:** blind duplicates hitstun's "briefly disable" for a whole new vision system; fires all of Q3/Q4/Q5 at once with zero compositional payoff.
- **A5 — self-only-visible health drop:** identity is observer-relative rendering (new system, breaks per-peer-identical dispatch) and it IS resource caching / map control.
- **A6 — blink teleport:** pure self-mobility gadget; touches no enemy, folds with nothing; the deathmatch button orthogonal to build-a-character.
- **P3 — invisible-after-hit:** closed get-hit→hide→ambush loop; invisibility lands only in enemy rendering/AI vision, composes with no upgrade; needs a new perception system + hit-received + timed lifetime.
- **P6 — slowdown goop puddle:** the persistent occupancy-zone is a new area-denial system = map control; the salvageable interaction (timed enemy slow via your projectiles) is already roadmap spike 4, built through the hit pipeline.
- **P7 — smoke cloud on hit:** its own pitch ("claim space, play around it") is the excluded map-control axis; server-authoritative it needs a new vision-denial system, client-only it's cosmetic; composes with nothing.

### New systems demanded by cut features — none earned (each displaces nothing)
- **Perception / vision-denial** (A3, A4, P3, P7) — no upgrade reads "how blind the target is"; leans to the excluded control axis.
- **Observer-relative differential rendering** (A5) — violates per-peer-identical dispatch; breaks server-authoritative pickups.
- **Persistent area-denial zones / continuous-occupancy volumes** (P6) — a 4th modifier lifetime; = map control.
- **Reflect / projectile-redirection** (A2).

---

## 2. THE MATRIX

| effect \ dispatch | use | hit-dealt | passive-fold | spawn-time | movement-input | hit-received `*` | timer `*` |
|---|---|---|---|---|---|---|---|
| steal-ability | **A1** | **A1** | | | | | |
| remove-passive | **A1** | **A1** | | | | | |
| grant-passive | **A1** | **A1** | | | | | |
| reflect-damage | A2 | | | | | A2 | |
| damage-immunity | A2 | | | | | A2 | A2 |
| apply-blind | A3, A4 | A3 | | P7 | | A3, A4, P7 | A3, A4, P7 |
| knockback-variant | A3 | A3 | | | | | |
| spawn-owned-zone | A5 | P6 | | A5, P6, P7 | | P7 | P6, P7 |
| invisibility | A5 | | | A5 | | P3 | P3 |
| teleport | A6 | | | | | | |
| stat-buff (fire-rate) | | **P1** | **P1** | | | | **P1** |
| spawn-stat-buff (blast radius) | | | **P2** | **P2** | | | |
| self-damage-resist | | **P2** | **P2** | | | (P2 rejected) | |
| extra-jump | | | **P4** | | **P4** | | |
| apply-hitstun | | **P5** | **P5** | | **P5** | (P5 rejected) | (P5 read-time SyncVar) |
| slow-field / stat-debuff | | P6 | P6 | | | P6 | P6 |

**Reading:** `hit-received*` is the single most-populated MISSING column (9 features) yet **every survivor routes around it** — it is demanded ONLY by cut features. Surviving mass concentrates in `use / hit-dealt / passive-fold / spawn-time / movement-input`, all of which already exist. The one missing column a survivor touches (`timer`, via P1) is satisfied by an existing Core-processor pattern, not a new generic mechanism (see §3).

---

## 3. MECHANISMS

### Surviving mechanisms — existing seams the keepers land on (all engine-native / already in code)
1. **Attacker-side hit-dealt `IHitHandler` harvest** (`HitResolver.RunHandlers` → `PawnPassiveAbilities.CollectHitHandlers`, owner via `context.Instigator`). Carries A1's steal (`PassiveStealOnHit`, unchanged) and P1's combo-timer bump. Note: `PassiveStealOnHit` already does the attacker-write-back move P1 needs — no "novel move."
2. **`SpawnNetworkObjects` use-site + the HitResolver/projectile pipeline.** A1's capsulecast is a short-range/short-lifetime projectile prefab (Health(1), no Damage/Knockback dealer) spawned by the existing component; owner-handler harvest fires the steal. Zero new types, zero edited core files.
3. **`SpawnModifier` struct + `SpawnBuffReceiver` on-spawn fold — EXTENDED with two authored fields** (blast radius for P2; outgoing-hitstun scaling for P5, poked into `HitstunDealer.Modifiers` at spawn). Genuine rule-of-two for *extending the struct*: two new fields, an existing engine-native fold, no ADR.
4. **The existing per-target `damageMultiplier` slot** at `HitResolver.ResolveExchanges` (currently unused). P2's self-damage-resist is this slot conditioned on `victim.OwnerId == projectile.OwnerId`. No new mechanism, no ADR, no victim-side dispatch.
5. **The temperature-shaped Core state-processor pattern** (`PawnTemperature`/`TemperatureProcessor`, mirrored by `MovementEffectsProcessor` and `PawnPassiveAbilities`). P1 is a concrete `ComboProcessor` in this exact shape — a refresh/decay timer deriving a `WeaponModifier`, swapped via the existing Remove/Add fold discipline. This is a **4th contributor of a thrice-used pattern**, not a new lifetime.
6. **`PawnWeapon.Modifiers` fold** — output surface for P1 (fire-rate) and P5's victim-side weapon suppression.

### KILLED / DEFERRED candidate mechanisms (reason each)
- **Damage-buff channel as a UNIFIED deliverable — DEFERRED.** "Channel" is one word over three structurally different existing seams with three lifetimes (per-target-resolve, spawn-once, standing-per-hit). Incidental similarity ("all scale a hit"), not a shared mechanism. Its one genuinely-new part — an owner adding OUTGOING damage via a `Damage.Modifiers` contributor — has NO surviving consumer (neither P2 nor P5). Build P2/P5 on the seams above; run the Q1 ADR only for its real isolated question (the `Damage.cs`-vs-siblings asymmetry), on its stated trigger, which is neither P2 nor P5. Falsification: if after building P2/P5 nothing writes the standing dealer `Modifiers` lists at runtime, the "standing contributor" half correctly stayed unbuilt.
- **Generic feeder-agnostic timed-modifier receiver — DEFERRED.** Justified by N=1 (P1). The lifetime it advertises as new is already hosted by the imperative-shell Core-processor pattern (Q4 conflates "the stateless fold can't host it" with "the shell can't host it" — the shell does, in triplicate). Build P1 concrete now; defer the generic receiver to roadmap spike 4 (movement-steal), whose victim-simulated, wire-payload second consumer is materially different and earns the abstraction under rule-of-two — shaping it by the hard case, not P1's trivial local self-buff.
- **`IUseHandler` use-site seam — KILLED (single concretion).** A1 is the SOLE surviving driver; the claimed "second concretion" (`SpawnNetworkObjects`) doesn't need the interface (it works today via concrete `GetComponents`), so this is one concretion wearing two hats — the exact speculative interface CLAUDE.md and [[no-speculative-interfaces]] forbid. It also spreads the change across the ability-dispatch core and builds a half-null `HitContext` (Motion/Source meaningless from a cast) that silently makes Ricochet/Deflect-shaped handlers cast-unsafe. A1 reduces to a spawned projectile with zero new types. Falsifiable revival trigger: a SECOND use-site behaviour that genuinely cannot be a spawned projectile AND playtest showing the projectile ruins A1's melee feel.
- **Additive `MovementModifier` air-jump field — DEFERRED with P4.** One producer (P4, itself borderline); introduces a second fold semantics (additive in a uniformly-multiplicative struct) and enlarges `MovementState` (the Reconcile snapshot). Existing multiplicative fields already express floaty/high-jump upgrades. Sent back for the re-pitch; if it lands, prefer the existing impulse lane before a new fold semantics.

---

## 4. ADR AGENDA (ordered by surviving-feature blocking count)

1. **Q2 — Hitstun feel / balance (playtest gate, no new code).** Blocks shipping **P5** (sustained accurate fire can permanently lock a target out of firing) and is adjacent to **P1** (fire-rate feedback risks runaway sustained fire). A tuning gate at first real playtest, not an architecture decision. Highest block count among survivors.
2. **Q1 — Damage-buff channel (roadmap item 3, own ADR).** **P2** is sequenced after it — but per the critique the ADR's job is narrow: ratify HOW runtime buffs reach outgoing damage/knockback/hitstun (on-spawn fold vs. a standing `Damage.Modifiers` list — the latter re-opens the reverted `[SerializeReference]` fold). P2/P5 themselves land on existing seams and do NOT force it; do not let them improvise it. Its real trigger (first ability that must boost OUTGOING damage) is still absent.
3. **Q4 — Timed-modifier lifetime (generic receiver).** Resolved for now as: **build P1 concrete** (temperature-shaped `ComboProcessor`). The GENERIC receiver stays deferred to spike 4 (movement-steal), where a genuinely-different second consumer earns it. No survivor forces the generic build.
4. **Q3 — Debuffs** and **Q5 — Wire-payload class.** **NOT forced by any survivor** — every feature that would force them prematurely (A3, A4, P3, P6, P7: dealer-authored effects on client-simulated victims) was cut. Their real driver remains the roadmapped movement-steal projectile (spike 4). Confirm the deferral.
5. **Observation, not an agenda item:** victim-side **hit-received** dispatch is a legitimate structural gap (HitResolver already walks to the victim root) but is demanded only by cut features. Record it; do not build it until a payload that COMPOSES into an existing system (heal-on-hit → health; thorns → dealer+projectile) is its first consumer — invisibility/smoke are the wrong first payload.

---

## 5. PROPOSED DESIGN.md ROADMAP EDITS

- Spike 2 (passive-steal): note A1 builds as a **short-range projectile via `SpawnNetworkObjects`** (Health(1), no damage/knockback dealer), wind-up = **local scalar** on the ability. NOT `IUseHandler`. Existing owner-handler harvest fires the steal.
- Add **P5 (hitstun passive)** as near-free content on the existing HitstunDealer/HitstunReceiver exchange — authored config + a passive contributor; gated on the Q2 playtest, not new code.
- Add **P1 (combo fire-rate passive)** as a concrete **temperature-shaped `ComboProcessor`** (Core state processor) fed by a hit-dealt handler; explicitly NOT the generic timed receiver (that stays on spike 4).
- Add **P2 (bigger blast + self-damage-resist)** sequenced **after spikes 1 and 3**; two new `SpawnModifier` fields (radius, outgoing-hitstun) + the existing per-target `damageMultiplier` slot conditioned on owner identity. No new dispatch.
- Under Open questions / spike 4: move the **generic timed-modifier receiver** and **wire-payload class** trigger explicitly to movement-steal; note P1 does NOT force either.
- Record **P4 as needing a re-pitch** (enemy-facing interaction) or cut; do not schedule the additive-movement fold.
- Record the eight cuts (A2–A6, P3, P6, P7) with their one-line reasons so they aren't re-proposed; note the recurring cut-driver is the excluded map/perception-control axis + an unearned victim-side/vision system.
- Add the **victim-side hit-received gap** as a logged observation under Open questions (needs a composing first payload, not invisibility/smoke).

---

## 6. Q1 / Q2 RECOMMENDATION (leans only — NOT decided here; each needs the six-lens `engineering-canon` interview + an ADR)

**Q1 — do pickups grant passives directly, or grant actives + XP that unlocks passives?**
**Lean: pickups grant abilities directly (passives and actives), no XP/leveling track.** Essence rationale: passives are the *composing* upgrades — they fold into the modifier collections and hit-dealt harvest that deepen system interactions, which IS the essence; a pickup then equals an upgrade-interaction *choice* every time. Elegance: one delivery mechanism (pickup = grant) vs. two currencies + a leveling system. The XP+passives route adds a progression/accumulation track that drifts toward the excluded "resource" axis and is a standing scope/maintenance commitment (Lehman/Conway). Snowball/scope tradeoff to weigh in the interview: direct-grant couples power to pickup presence on the map (mild map-control pressure — mitigate by making pickups a DRAFT/choice, not a positional scramble); XP decouples power from grabbing but at the cost of the extra system. **Flag: canon interview + ADR required.**

**Q2 — passive persistence: per-life or per-game?**
**Lean: per-game (build persists across deaths within a match), with steal as the built-in catch-up valve.** Essence rationale: "build the best character" is a roguelike/deckbuilder ARC — it wants the build to *accumulate* so interactions compound over a match; per-life resets collapse that arc into round-based skirmishes and waste the interaction-depth the essence is spent on. The dominant risk is snowball (rich-get-richer → the excluded map-dominance shape) — but the roadmap's own steal mechanics (A1 passive-steal, spike-4 movement-steal) are essence-native comeback valves that let a losing player TAKE from the leader, making per-game viable where a plain shooter's wouldn't be. Per-life is the snowball-safe fallback if playtest shows steal can't close the gap. **Flag: this is a balance + scope decision — canon interview + ADR + playtest required; do not decide from theory.**
