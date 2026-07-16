# Master execution DAG — the three plans reconciled

_Drafted 2026-07-15. **Transient reconciliation snapshot**, not a fourth standing plan: it only sequences and de-conflicts the open items already owned by the three source plans + DESIGN.md — it restates none of them. **Fold the settled ordering into [DESIGN.md](../../DESIGN.md)'s roadmap and delete this file** once the waves ship (same self-delete rule `spike-features-to-real-content.md` applies to itself). Corrected against an adversarial (design-critic) pass on 2026-07-15 — see the Corrections ledger._

Source plans (this file points into them; detail lives there):
- [parnas-modularity-plan.md](parnas-modularity-plan.md) — W1–W5 (modularity leaks, decided, none executed)
- [spike-features-to-real-content.md](spike-features-to-real-content.md) — Sessions 1–4, OQ1–2
- [feature-wishlist-consolidation.md](feature-wishlist-consolidation.md) — survivors A1/P1/P2/P5, cuts, ADR agenda

## How this DAG was derived (read before trusting an edge)

The edges below are settled by **which source files / seams each item touches** (`git grep`), **not** by a paper. Parnas's 1972 CACM paper supplies one relevant idea — its *Independent Development* benefit: items that hide **disjoint** design decisions behind stable interfaces can be built concurrently. (The full "uses" partial-ordering-as-schedule is Parnas's *later* work, 1974/79 — the 1972 hierarchy section is a caveat that a module ≠ a call-graph level, not a scheduling tool. Attribution kept honest so this isn't an appeal to authority.)

**The distinction that governs every edge — sequencing ≠ coupling:**
- **Coupling** = when A changes in a *likely* way, B must change too (co-change / ripple). The bad thing. Detected by walking a plausible change through the edge.
- **Sequencing** = B can't *start* until A's interface exists. A partial ordering — desirable layering, says nothing bad about modularity.

Edge count is **not** a modularity metric: a deep chain over stable interfaces has zero coupling; a "fully parallel" plan where everyone edits one god-object is a coupling disaster. Below, each edge is tagged by kind.

## Ratified 2026-07-15 (this session)

- **Map/resource control is NOT excluded.** The essence dropped that exclusion (commit `d50d5a4e`; scope guard pared to the interaction test in `6cda6855`/`49642ec0`). The consolidation's cuts of P6/P7 rested on removed doctrine and are **reversed**. Stale copies synced (`habit-check.sh`, `design-critic.md`, `game-essence` memory).
- **Acquisition** (resolves DESIGN.md's "ability acquisition source" open question): primary objective = fight over **smash-ball pickups** granting **permanent** passives + actives; secondary = **limited-use** active pickups. Two-tier direct-grant, no XP track. → ADR. Caveat: "fight over pickups on the map" carries mild positional pressure — mitigate with a draft/choice framing, not a spawn scramble.
- **Per-observer rendering: ADOPT as a system** (resolves the open question) — consumers P3, P7.
- **Terrain-persistent zones: ADOPT** (resolves the open question) — consumers P6, P7; both build as designed, persistent volumes included.

## The two tracks

```mermaid
graph LR
  subgraph TrackA["Track A — modularity (mostly partitionable)"]
    W1["W1 delete Level Gen<br/>(isolated)"]
    W4["W4 HealthFraction"]
    W5["W5 match-state<br/>exhaustiveness"]
    W3["W3 UI to bus"]
    W2["W2 collapse WeaponData<br/>(rewrites PawnWeapon.Update)"]
    W4 -. "soft: same file<br/>NetworkUIController" .- W3
  end
  subgraph TrackB["Track B — features (largely a SEQUENCE)"]
    OQ1["OQ1 no double-dip"] --> S1["Session 1<br/>Proximity explosion"]
    OQ2["OQ2 plain cast*"] --> S2["Session 2 / A1<br/>Passive-steal active"]
    S1 --> S3["Session 3 / Q1<br/>Damage-buff channel<br/>= ADR 0005"]
    S1 --> P2["P2 bigger blast<br/>+ self-resist"]
    S3 --> P2
    S3 --> ITEM4["Item 4<br/>Movement-steal"]
    P1["P1 combo fire-rate"]
    P5["P5 hitstun passive"]
  end
  W2 -->|"uses: same PawnWeapon.Update"| P1
  W2 -->|"uses: same PawnWeapon.Update"| P5
```

`*` OQ2's *deeper* question is unsettled — see Restored items.

### Track A — modularity (the only genuinely partitionable slice)

All five hide different leaked decisions, so they are independent commits — **except** the two edges the code forces:

| Item | Touches | Parallel? |
|---|---|---|
| **W1** delete Level Generation | `Level Generation/` only | fully isolated — parallel to everything |
| **W4** `HealthFraction` | Health surface, `BotBrain`, `NetworkUIController` | parallel, but **soft edge with W3** (both edit `NetworkUIController`) |
| **W5** match-state exhaustiveness | `Arena.UIFlow.Core` mapping | fully isolated |
| **W3** UI → `NetworkUIEvents` bus | `NetworkUIController`, `PostgameCountdownView` | parallel, soft edge with W4 |
| **W2** collapse `WeaponData` | **rewrites `PawnWeapon.Update` (`:36-48`)** | **NOT parallel to features** — gates P1 (`:59`) and P5 (`:58`), which sit in the same method |

W1/W4/W5/W3 are safe to fan out (low-design, mechanical) — this is the partitionable work the project's ratified understanding permits. W2 is a shell-method rewrite that **two feature items sit inside**, so it lands first.

### Track B — features (one-mind, largely sequential — and that's correct)

Design-laden work; **not** fanned out (per the ratified understanding: subagents search/implement/validate, they don't own design). The ordering:

1. **Wave 0 — ratify tunables:** OQ1 double-dip → **no** (feeds Session 1). OQ2 → **plain cast** for the *mechanics*, but its deeper question (*should the steal BE a hit?*) is **not** foreclosed — see Restored items.
2. **Session 1** (proximity explosion) ∥ **Session 2 / A1** (passive-steal active) — genuinely disjoint seams (own component + prefab-copy vs. own `IUseHandler` use-site). The one real parallel pair in Track B.
3. **W2 → P1, P5** — after W2 lands, P1 (combo fire-rate, `ComboProcessor`) and P5 (hitstun passive) build on the reworked `PawnWeapon.Update`. P5 *ships* gated on the hitstun-feel playtest.
4. **Session 1 → Session 3 / Q1** — the damage-buff channel design session (= **ADR 0005**, not 0004). Runs after Session 1, which gives the per-target `damageMultiplier` slot a live consumer (falloff) = its real *Evidence*. **P2 is NOT its evidence** (P2 is shipped content, not a throwaway spike).
5. **Session 3 → P2** — P2's self-resist is the channel's **first real consumer** (owner-passive-scaled multiplier onto a damage value), so it follows the ADR. P2 also **re-opens Session 1's self-exclusion** (rocket-jump wants self-knockback) — not a clean `SpawnModifier` +2-fields extension. Falsifiable check below.
6. **Session 3 → Item 4** (movement-steal) — LAST; forces the timed-modifier lifetime (Q4) + wire-payload (Q5), both resolved by the ADR.

### Parallel decision track (one-mind, alongside code — blocks *shipping/tuning*, not building)
- **Q1** acquisition source (pickup vs XP) — interview + ADR; grant path is abstract, blocks no wave.
- **Q2** persistence (per-life vs per-game) — interview + ADR + playtest; interacts with respawn.
- **Hitstun feel** — playtest gate; blocks *shipping* P5, adjacent to P1.
- **P4** (extra air-jumps) — **re-pitch around an enemy-facing interaction or cut**; do **not** schedule the additive-`MovementModifier` fold.

## Track C — ability & perception expansion (added 2026-07-15)

Re-adds the five consolidation cuts (**A2** reflect, **A3** pepper, **P3** invisibility, **P6** goop, **P7** smoke) under the ratified decisions. The batch **activates most of DESIGN.md's deferred open questions at once**, and each now meets rule-of-two — so the elegant order is Parnas's "uses" hierarchy: **design + build the shared enabling channels first, then the features ride them.**

```mermaid
graph TD
  subgraph EN["Enabling systems — ADR/design session each (rule-of-two met)"]
    N1["N1 timed-modifier lifetime"]
    N2["N2 wire-payload"]
    N3["N3 victim hit-received"]
    N4["N4 per-observer render"]
    N5["N5 terrain zone"]
    N6["N6 debuff / vision-denial"]
  end
  A2["A2 reflect"] --> N3
  A3["A3 pepper (blind half)"] --> N1
  A3 --> N2
  A3 --> N6
  P3["P3 invisibility"] --> N3
  P3 --> N1
  P3 --> N4
  P6["P6 goop"] --> N5
  P6 --> N1
  P6 --> N2
  P7["P7 smoke"] --> N4
  P7 --> N5
  P7 --> N3
  P7 --> N6
```

### Enabling systems (low level — each an ADR/design session; rule-of-two met by the batch)

| # | System | Consumers | Note |
|---|---|---|---|
| **N1** | Timed-modifier lifetime (generic receiver) | A3-blind, P3-invis, P6-slow (+ spike 4) | DESIGN.md: build **once, generic** — not per-system twins |
| **N2** | Wire-payload class (dealer-authored effect on client-simulated victim) | A3-blind, P6-slow | DESIGN.md Q5; `fishnet-conventions` Rule 1 is unsatisfiable for this class |
| **N3** | Victim-side hit-received dispatch (react-when-I'm-hit handler) | A2, P3, P7 | `HitResolver` already walks to the victim root — an **extension**, not new plumbing |
| **N4** | Per-observer rendering (server-authoritative visibility) | P3, P7 | Adopted; each client sees a different world |
| **N5** | Terrain-persistent zone (ticking world-space volume) | P6, P7 | Adopted |
| **N6** | Debuff / vision-denial | A3-blind (first real debuff), P7 | resolves DESIGN.md's "Debuffs" open question |

**THE central elegance question for these sessions — do NOT pre-decide; canon-interview each:**
- **Do N1 + N2 + N6 collapse into ONE channel** — *"a dealer-authored, timed, wire-replicated modifier applied to a victim"* (blind, slow, future debuffs are instances)? Very likely **yes** — the Parnas hide-one-decision + Elegance collapse-mechanisms move. Standing up three separate mechanisms here would be the anti-pattern. This likely turns "5 new systems" into ~3.
- **Is "perception" one system or several?** Blind (dim the victim's OWN screen) ≠ invisibility (hide you from OTHERS = N4) ≠ smoke (block others' view through a volume = N4+N5). Decide whether N4 subsumes all vision-denial or only self-visibility.

### Features (upper level — ride the N-systems)

| Feature | = cut | Scope | After |
|---|---|---|---|
| **A2 reflect** | A2 | **reflect-only** — drop "block ALL damage" (it *negates* an interaction, anti-essence). Reflect = N3 + inline projectile-redirection (N=1, no system) | N3 |
| **A3 pepper** | A3 | splits: **knock-up + AoE → authored config on Session 1's proximity explosion (nearly free)**; **blind → N1 + N2 + N6 + a client screen effect** | Session 1 (knock-up); N1/N2/N6 (blind) |
| **P3 invisibility** | P3 | N3 (trigger on hit) + N1 (timed self-buff) + N4 (per-observer render) | N3, N1, N4 |
| **P6 goop** | P6 | N5 (zone) + N1 (timed slow) + N2 (wire) — the slow itself is spike 4's interaction, now delivered in a zone | N5, N1, N2 |
| **P7 smoke** (heaviest) | P7 | N4 + N5 + N3 + N6 | all four |

### System-budget flag (the one essence-guard point that survives)

Map-control is no longer the objection — but the essence still says *"adding a new system is a major decision… something should probably be cut to make room."* This batch adopts **up to ~5 new systems (N1–N6, minus collapses)** — the largest scope increase on the roadmap. Two mitigations already in the plan: (1) collapse N1/N2/N6 into one channel (above) → ~3 systems; (2) under the *current* guard these are genuine interaction depth — upgrades that change how your **enemy** plays (the essence's top axis), not gadgets. **Recommendation: proceed, hold the collapse questions as gates, and revisit "cut to make room" only if N4/N5 each spawn sub-systems.**

## Does a deep DAG mean the features are too coupled? (the question that prompted this)

**No — and deriving the DAG from evidence (not from Parnas's prose) made it *more* sequential, not less, by exposing two over-claimed "parallel" edges (W2∥features, P2∥Session 3).** That sequentiality is healthy:
- Design work is **one-mind** — parallelizing it is Brooks's law, not throughput.
- The edges are **uses-edges** (P2 rides `SpawnModifier`/`damageMultiplier`, not the explosion internals — change the falloff, P2 is untouched) and **decision-funnels** (Session 3 is the single home for a deferred decision → *anti*-duplication, the opposite of a modularity failure).
- Exactly **one** edge is shared-code coupling: W2/P1/P5 on `PawnWeapon.Update`. It's minor, soft (merge churn, not co-change), and in the **shell**, not a Core.

**The one thing to watch** is not coupling but **concentration**: Session 3 (the channel) is a chokepoint several features route near. The risk is building it *wide* — one broad interface many features share (the over-reveal Parnas warns of in the circular-shift example). Mitigation is already doctrine: build P2/P5 on **narrow existing seams; don't unify prematurely.**

## Corrections ledger (what the adversarial pass changed)
- **W2 is not parallel to the feature track** (was: "Track A fully parallel to Track B"). W2→P1 **and** W2→P5, same `PawnWeapon.Update`. W2 lands first.
- **P2 moves back after Session 3** (was: "P2 doesn't need Session 3"). Its self-resist is the channel's first consumer; building it first would pre-decide the ADR in shipped code.
- **Parnas attribution corrected** — 1972 paper supplies *Independent Development*, not the uses-DAG-as-schedule (later work). DAG derived from touched files.
- **ADR number:** the damage-buff channel session's deliverable is **ADR 0005** (`spike-…-content.md:57` still says 0004, which is taken — cross-peer cosmetics). Fix on that file's next edit.

## Restored items (dropped in the first draft)
- **P4** — re-pitch-or-cut verdict above; do not schedule the additive-movement fold.
- **OQ2 deeper question** — "plain cast" settles the *mechanics*; whether the steal should **BE a hit** (trigger victim reactions, later stack with damage/knockback) is a Parnas module-ownership question owed an interview before Session 2 finalizes.
- **DESIGN.md housekeeping (Lehman paydown — fold into the relevant commits, not a separate wave):** rescope roadmap item 2 to the active variant; delete the stale `agent-*` worktree note; record the 8 cuts (A2–A6, P3, P6, P7) with their one-line reasons so they aren't re-proposed; log the victim-side hit-received gap as an Open-questions observation; delete each source plan when its thread ships.

## Falsifiable checks (each yields a number)
1. **P2-is-the-channel:** count inputs feeding P2's self-resist value — 0 (authored constant) ⇒ not the channel, could precede Session 3; ≥1 (owner passive state) ⇒ it IS the channel, Session 3 first. *(Expected ≥1 — P2 is a passive upgrade.)*
2. **P2-reopens-Session-1:** lines of Session 1 blast code changed to include the instigator's own pawn; >0 ⇒ not an additive extension.
3. **B1 pair really parallel:** intersection of touched source files per pair; non-empty ⇒ serialized. `{W2,P5} ∩ = PawnWeapon.cs` (count 1) ⇒ not parallel.
4. **Not proliferation:** when Session 1 ships, count docs that must be edited to stay consistent — must be **1** (DESIGN.md). If this file still exists and must also change, it has become the fourth drifting artifact it was warned against → delete it.
