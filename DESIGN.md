# DESIGN.md — design log, roadmap, and open questions

The **why** layer. This is where design rationale, alternatives considered, and open questions live — the things that are neither doctrine (that's `.claude/skills/`), nor a durable fact/norm (that's memory), nor a code-navigation index (that's `CLAUDE.md`).

**The division that keeps these from overlapping:**
- A **skill** says *"how we always do X"* (doctrine — the effect-command pattern, the MCP-vs-shell rule).
- An **ADR** (below) says *"why we chose X over Y for this feature, and what we rejected."* Rationale, dated, immutable once accepted.
- **This file** is the living index of those ADRs + the roadmap + the open questions not yet decided.

**How the agent uses it:** consult DESIGN.md at the start of any design task or new feature (the `arena-systems-map` and `ai-workflow` skills both point here). When a real decision is made — one with alternatives worth remembering — write an ADR. Don't inflate CLAUDE.md with rationale; link to the ADR instead.

---

## Essence (the north star — guard its size)

**What makes this game unique and interesting to PLAY** — not the marketing pitch. Every system, feature, and abstraction answers to this. Kept deliberately tiny: if you can't recite it, it isn't doing its job.

> An arena FPS deathmatch where players compete to **build the best character** (rogue-like / deckbuilder) — **rather than** control maps and resources.
>
> The depth comes from a **handful of systems interacting**; upgrades target those systems and, above all, the **interactions between them**.

**Current systems — the scope boundary, NOT the essence:** movement, health, weapons, projectiles, pickups, passive abilities, active abilities, kills. This list *will* change and must not live inside the two sentences above. Adding a new system is a major decision: it must earn its place against the essence, and something should probably be cut to make room.

Keep one distinction conscious: systems upgrades **modify** (movement, health, weapons, projectiles) vs. systems that **drive/deliver** interactions (pickups deliver upgrades; **kills** is an interaction *source* — an on-kill event feeding a projectile/weapon/health bonus or a dropped pickup). Kills earns its place as *interaction*, which is the essence — not because it drives progression specifically. Weapons (firing cadence/spread/burst) and projectiles (ballistics + hit reactions) stay separate only because upgrades target them separately.

**Scope guard (the agent holds the user to this — see the anti-sycophancy mandate in the `ai-workflow` skill):**
- The essence stays ~2 sentences. A proposed addition must **replace**, not append — argue what it displaces.
- The essence's power is its **exclusions** ("rather than control maps and resources"). A pure inclusion justifies anything. When you extend the essence, state what it now rules **out**.
- **This decides where code complexity is warranted.** System *interactions* and *upgrades* are the essence, so composition/effect machinery earns its keep **there**. A bespoke one-off that doesn't compose into an upgrade, or generality no upgrade will ever use, is the suspect case — no matter how clean the code. (This is the sharpened test the `IEffect` review applied — resolved in ADR 0002/0003: not "23 types for 4 shapes," but "does each piece serve upgrade-driven system interaction?")

## Elegance (the other north star)

Two north stars govern everything, and **every skill, rule, ADR, and abstraction is an instance of one of them** — not a separate thing to memorize: the **Essence** (what the game is, above) and **Elegance** (how we build it).

**Elegance = the fewest principles, mechanisms, and abstractions that cover the real cases.** A simple mental model of *why/how* beats a complete one.

- Prefer **reusing or collapsing** an existing mechanism over adding a new one. A new principle/channel/abstraction/ADR is a maintainability cost even when locally convenient — justify it against what already exists, or don't add it. When two things overlap, merge them.
- The project's anti-cruft rules — *no speculative interfaces* ([[no-speculative-interfaces]]), *fewest cross-component mechanisms* (`cross-component-communication` skill), *earn every artifact* (`ai-workflow` skill) — are all **this one value wearing different hats.** Cite the value, not a growing list of rules.
- **Engine-native beats owned** ("use Unity to its fullest", folded in here — an instance of this value, not a third star): a mechanism the engine provides and maintains (components as the unit of composition, `GetComponents` dispatch, plain serialization, prefab authoring) is nearly free against the mechanism budget; one we own (custom drawers, `[SerializeReference]` machinery, bespoke registries) costs full price. When both cover the case, take the engine's.
- **SOLID is a diagnostic, not a ranking**: a violation (a closed enumeration at a hot extension point, a class accreting unrelated jobs) flags inelegance worth fixing — but two competing designs frequently BOTH conform, and conformance cannot pick between them; decide on mechanism count and coverage. (Receipt: the 2026-07-14 hit-handler session — command lists and component discovery were both fully SOLID-open; engine-nativeness decided.)
- **Composition and inheritance have assigned layers** (generalized from [ADR 0002](Docs/design/0002-ieffect-appropriateness-verdict.md)): *which systems interact* is expressed by **composing participants** — components an entity carries, commands on an authored list, modifiers in a fold — never by a bespoke type per interaction. **Inheritance** appears only as the shallow taxonomy of sites/events that gates *where* behaviour can be authored, and never carries behaviour itself. The scar to remember: "composition" at the TYPE level (the deleted capability lattice) is where speculative generality hides — expressive type machinery is earned by a second real case that doesn't fit the tree (rule of two), never defaulted to.
- This binds the **workflow itself**: skills, hooks, agents, ADRs, memories are under the same discipline. The knowledge layer should trend toward *fewer, sharper* statements. When in doubt, remove.

## Roadmap / active threads

_Keep this short — a few lines per thread, newest first. Delete finished threads once their outcome lives in code + an ADR._

- **Spike features → real content** (NEXT, one session each) — the ADR 0002 review spiked three upgrade-interactions as throwaway evidence; build them for real, in this order. Spike code in `.claude/worktrees/agent-*` is REFERENCE ONLY (pre-refactor APIs — the pattern strains it found are exactly what the refactor fixed); do not merge it.
  1. **Proximity explosion** — first real consumer of `HitResolver` fan-out + the per-target `damageMultiplier` (falloff). No open-question dependencies. Watch: dedupe victims across the overlap, and decide the direct-hit double-dip (direct exchange + blast falloff on the same victim) explicitly.
  2. **Passive-steal active ability** — capsule-cast victim, remove-then-grant across two rosters (ability↔ability interaction, the essence). Spike learnings: passives need an individual-removal path (`RemovePassive`); the steal moves the KIND, not the instance; resolve the stolen kind BEFORE despawning. Every networked change → PlayMode test.
  3. **The damage-buff channel design session** (already in Open questions — run it HERE, before item 4): it owns spike 4 (scalable/resistible knockback = the dealing-end contributor), owner-adds-flat-damage, and the per-target multiplier's contributor story. Deliverable: its own ADR.
  4. **Movement-stealing projectile** (attacker speeds up, victim slows, timed) — deliberately LAST: it forces two deferred decisions whose triggers fire here (the timed-modifier lifetime — build the generic receiver once, not per-system twins — and the wire-payload class for dealer-configured, receiver-simulated effects → `fishnet-conventions`).

## Open design questions

_The valuable part of this file: decisions deliberately deferred, so they aren't improvised under pressure. Each names the trigger that should force the decision._

- **The damage-buff channel** — how runtime buffs/crits/level-ups reach a projectile's dealt damage (and now knockback/hitstun) is undesigned. `HitResolver` reads the `Damage` dealer's base value; the *dealing-end modifier list* exists structurally but has no contributors. A `[SerializeReference]` `DamageModifier`-fold attempt was reverted once. Candidates: an on-spawn damage multiplier folded like the other on-spawn buffs (a field on `SpawnModifier` + a per-spawn multiplier seam, the `SetRadiusMultiplier`/`SetMaxHealthMultiplier` shape) vs. a standing dealing-end contribution. The review's spike 1 added a third requirement neither candidate covers: a **per-target** multiplier (blast falloff) — `HitResolver.ResolveExchanges` now carries that slot (`damageMultiplier`, unused). Also covers spike 4 (scalable/resistible knockback): the dealing-end contributor IS this channel — no new hit plumbing. The channel also OWES a capability the refactor removed: an owner adding flat damage to their projectiles (previously faked by contributing a second courier; now inexpressible by design). **Trigger:** the first ability or pickup that must boost outgoing damage. Design it in a dedicated session; do not bolt it onto another feature. (Governed today by the `presence-dispatch` skill's "OPEN" note — that note should shrink to a pointer here once an ADR exists.)
- **Hitstun feel / balance** — bullet deals 0.2s, stuns *extend* rather than replace, default fire rate is 0.1s, so sustained accurate fire can lock a target out of shooting entirely. Deliberate consequence of "cannot fire for a duration," not yet playtested. **Trigger:** first real playtest of sustained fire.
- **Debuffs** — `ApplyDebuff` / `DebuffType` are placeholder vocabulary with no system behind them (ADR 0002 deletes both as dead weight; the *question* of a debuff system stays open). **Trigger:** the first real debuff (slow, burn).
- **The timed-modifier lifetime** — the review's spike 3 showed a timed effect (3s slow) needs a stateful receiver owning `(modifier, expiry)` entries: a *third* modifier lifetime beside persistent-until-removed and consume-once, which the stateless effect layer cannot host. Build it ONCE, generically — not a `Timed<X>Modifiers` twin per system. **Trigger:** the first shipped timed buff/debuff.
- **The wire-payload class** — a dealer-configured effect on receiver-simulated state (e.g. a slow whose magnitude is authored dealer-side, applied to a client-simulated pawn) cannot stay payload-free: the victim's client can't derive what hit it. The review's spike 3 needed the hit pipeline's first gameplay TargetRpc payload. Decide the sanctioned payload shape and write it into `fishnet-conventions` (Rule 1 is unsatisfiable for this class; knockback escapes only because its config lives receiver-side). **Trigger:** shipping any movement-steal-like effect.

## ADR index

_One line each; the file holds context + decision + alternatives + consequences._

| # | Decision | Status | Date |
|---|---|---|---|
| [0001](Docs/design/0001-hit-exchange-pattern.md) | Hit exchanges = dealer component + receiver component (courier layer superseded by 0002) | Accepted | 2026-07-10 |
| [0002](Docs/design/0002-ieffect-appropriateness-verdict.md) | IEffect verdict: keep command core; delete lattice/couriers/sink-buffs; add fan-out site | Superseded in part by 0003 | 2026-07-13 |
| [0003](Docs/design/0003-hit-reactions-as-handler-components.md) | Hit reactions = presence-discovered IHitHandler components; channel = call site; command core retires | Accepted | 2026-07-14 |

## Writing an ADR

ADRs are **few and small** — subject to the Elegance value like everything else. Most decisions need none; write one only for a **load-bearing, contested** choice whose rejected alternatives are worth remembering. Keep each to **one screen**:

**Context** / **Decision** / **Alternatives rejected** (why each lost — the part future-you needs) / **Evidence** (the spike result or measured numbers the decision rests on) / **Consequences**, plus a one-line **north-star rationale** (how it serves Essence and Elegance).

**The ADR is the method's receipt — this is how we verify the right steps were taken.** You cannot fill in *Alternatives rejected* without having named an alternative, or *Evidence* without having spiked or measured. So the deliverable's shape enforces the method, and no separate checklist is needed. An empty *Evidence* or *Alternatives* section does not mean those sections were optional — it means the method was skipped, and the decision is not yet trustworthy.

**Breaking an ADR must be cheap.** To change one, add a short superseding ADR, or a one-line note on the old one: *"Superseded by NNNN — <one sentence why>."* Never rewrite an accepted ADR. If explaining a break takes more than a sentence, the ADR was too big or too speculative to begin with — that is the signal it shouldn't have existed. Copy the last one under `Docs/design/NNNN-kebab-title.md`; this practice is tracked in the `ai-workflow` skill's practices register.
