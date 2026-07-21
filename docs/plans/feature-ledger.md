# Feature ledger — the 13-feature batch: disposition + analysis

_Durable record. **Supersedes `feature-wishlist-consolidation.md`** (folded here 2026-07-15). The [master execution DAG](master-execution-dag.md) points here for per-feature status; this file exists so cut features aren't re-proposed and the original effect×dispatch analysis isn't lost. Status reflects the Track C decisions (2026-07-15). Design rationale for the deferred mechanisms lives in [DESIGN.md](../../DESIGN.md)'s open questions._

## Disposition (current)

| # | Feature | Status | Where |
|---|---|---|---|
| **A1** | passive-steal active | **Building** | DAG Track B / Session 2 |
| **A2** | reflect shield | **Re-added, gated** — reflect-only (drop the damage-block, which negates an interaction); needs a *composing* victim-reaction to earn victim-side hit-received | DAG Track C |
| **A3** | pepper (knock-up + blind) | **Re-added** — knock-up + AoE = free config on Session 1's explosion; blind rides the timed victim-debuff channel | DAG Track C |
| **A4** | blinding flashlight | **Cut** — blind now exists via the channel (A3); A4 as a distinct feature still composes with nothing. Revive only as a reskin of blind | — |
| **A5** | self-only-visible health drop | **Revivable** — both original cut-reasons are now removed: per-observer rendering is adopted, and map/resource control is no longer excluded. Re-pitch against the interaction test | — |
| **A6** | blink teleport | **Cut** — pure self-mobility, folds with nothing, orthogonal to build-a-character; no new decision changes this | — |
| **P1** | combo fire-rate | **Building** | DAG Track B |
| **P2** | bigger blast + self-resist | **Building** (after Session 3) | DAG Track B |
| **P3** | invisible-after-hit | **Re-added, gated** — per-observer render (design session) + timed self-buff + victim hit-received; closed self-loop, re-pitch to compose or accept as a shallow-but-legal gadget | DAG Track C |
| **P4** | wings / extra air-jumps | **Re-pitch or cut** — needs an enemy-facing interaction; do NOT schedule the additive-`MovementModifier` fold | — |
| **P5** | hitstun passive | **Building** (gated on hitstun-feel playtest) | DAG Track B |
| **P6** | slowdown goop | **Re-added** — timed slow on the debuff channel; terrain-zone persistence gated on P6's own playtest | DAG Track C |
| **P7** | smoke cloud on hit | **Re-added, gated** — per-observer render + terrain zone + victim hit-received; heaviest, composes with nothing | DAG Track C |

**Net:** 4 building (A1/P1/P2/P5), 5 re-added-gated (A2/A3/P3/P6/P7), 1 re-pitch (P4), 1 revivable (A5), 2 cut (A4 reskin-only, A6). **What moved features off the cut list:** the essence dropping the map/resource-control exclusion + adopting per-observer rendering (2026-07-15) — that reversed A2/A3/P3/P6/P7 from cut to gated, and A5 from cut to revivable. A6 and A4-as-distinct stay cut because they compose with nothing, which no decision changed.

## Mechanism analysis (original snapshot, 2026-07-15)

The batch was originally analysed as an effect × dispatch matrix. Its load-bearing finding: **every then-surviving feature routed AROUND the victim-side `hit-received` column** — that column was demanded only by the (then) cut features. Track C revisits exactly this: with the map-control and per-observer-rendering decisions, victim-side hit-received now has real (if non-composing) consumers (A2/P3/P7), so it is **adopted-but-gated** (wait for a composing first payload — heal-on-hit, thorns), not absent. The other originally-missing column a survivor touched (`timer`, via P1) is satisfied by the shipped `HitstunController`-shaped pattern (post-H1: the old `HitstunReceiver`/`HitstunDealer` pair merged into one controller, its `Arena.Hitstun.Core` inlined — ADR 0005), not a new mechanism.

Original matrix (preserved for reference; cut-decisions in it are superseded by the disposition table above):

| effect \ dispatch | use | hit-dealt | passive-fold | spawn-time | movement-input | hit-received | timer |
|---|---|---|---|---|---|---|---|
| steal/grant/remove-passive | A1 | A1 | | | | | |
| reflect-damage / damage-immunity | A2 | | | | | A2 | A2 |
| apply-blind | A3,A4 | A3 | | P7 | | A3,A4,P7 | A3,A4,P7 |
| knockback-variant | A3 | A3 | | | | | |
| spawn-owned-zone | A5 | P6 | | A5,P6,P7 | | P7 | P6,P7 |
| invisibility | A5 | | | A5 | | P3 | P3 |
| teleport | A6 | | | | | | |
| stat-buff (fire-rate) | | P1 | P1 | | | | P1 |
| spawn-stat-buff (blast radius) | | | P2 | P2 | | | |
| self-damage-resist | | | P2 | P2 | | | |
| extra-jump | | | P4 | | P4 | | |
| apply-hitstun | | P5 | P5 | | P5 | | P5 |
| slow-field / stat-debuff | | P6 | P6 | | | P6 | P6 |

## Open questions this batch raised

Live in [DESIGN.md](../../DESIGN.md)'s "Open design questions" (canonical). This session decided **acquisition** (two-tier smash-ball pickups + limited-use active pickups; ADR pending) and adopted **per-observer rendering** + the **timed victim-debuff channel** (generalize `HitstunController` — post-H1 noun). Still open: the damage-buff channel (ADR by title — 0005 went to H1), hitstun-feel playtest, and passive **persistence** (per-life vs per-game).
