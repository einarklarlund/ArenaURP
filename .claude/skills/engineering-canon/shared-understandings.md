# Shared understandings — the canon-interview ledger

Positions Einar and Claude have **explicitly settled**, one dated line each, so interviews never re-ask them and past agreements aren't re-litigated. Maintained under the [[engineering-canon]] skill; loaded only with it, so this memory costs no ambient context.

**Rules:** append-only; check before interviewing; when a position changes, add a superseding line and strike the old one (`~~…~~ superseded YYYY-MM-DD`) — don't silently edit. Past ~40 live entries, compact: merge overlapping lines, delete struck ones.

Format: `- YYYY-MM-DD · <lens> — <the position, one line>`

## Charter — seeded 2026-07-15 from standing doctrine

_These map already-agreed project values onto the canon. Seeded by Claude at skill creation; the 2026-07-15 first interview confirmed the Constantine line and left the rest standing — unchallenged, and like everything here, open to revision._

- 2026-07-15 · Dijkstra + Brooks — the Elegance north star IS this project's form of intellectual manageability + conceptual integrity; canon arguments route through it rather than adding a parallel value (`DESIGN.md`).
- 2026-07-15 · Parnas — the rule of two is the honesty condition on information hiding: a decision must be *seen* to vary before a seam hides it ([[no-speculative-interfaces]]).
- 2026-07-15 · Constantine — contribution collections + notifications are a deliberate ceiling at data coupling; a proposal that introduces control coupling (mode flags, callee-selecting parameters) triggers redesign, not accommodation ([[cross-component-communication]]).
- 2026-07-15 · Brooks — Einar + the main thread are the "one mind"; subagents search, implement, and validate but never own design decisions (the surgical-team reading of the delegation rule in [[ai-workflow]]).
- 2026-07-15 · Brooks's law — fan-out (multi-agent Workflows) is for partitionable work only; applied to a design problem it adds communication paths, not output (practices register, "Workflows" row).
- 2026-07-15 · Lehman — the workflow-auditor cadence and "merge overlapping rules" are law-II conservation applied to the tooling; the same budget question ("what does this pay down?") now applies to every feature plan.
- 2026-07-15 · Conway — delegation boundaries are a design act: whether an agent can own a module without the whole design in context is *evidence* about whether the module boundary is right.

## Settled in interviews

- 2026-07-15 · meta — settled ≠ frozen: every entry here is open to revision, and *how* we apply the canon is itself expected to keep evolving (Lehman's law I applied to this register); supersede lines rather than treating them as precedent-locked.
- 2026-07-15 · Brooks — the second-system question ("is this embellishing what ADR 0002/0003 proved, rather than what the game needs?") is a STANDING question for the current roadmap phase (the post-refactor feature builds); ask it on every feature plan in this phase.
- 2026-07-15 · Lehman — the pay-down rule is strict, not advisory: every feature plan names its offset (a mechanism collapsed, a type deleted, rules merged) or names the debt knowingly with a repayment trigger.
- 2026-07-15 · scope — the interview runs at plan-finalization only; small architectural changes (a new SyncVar, a new channel) are NOT individually gated — wallpaper risk outweighs the coverage; the per-area skills and hooks remain their guard.
- 2026-07-15 · Constantine — confirmed: control coupling (mode flags, callee-selecting parameters) triggers redesign, not accommodation.
- 2026-07-15 · Parnas/Lehman — the [[ai-workflow]] skill keeps NO inventory of hooks/agents/skills/memories: each artifact's single source is its own file/index, and `workflow-auditor` checks the layer — prevention over detection (from the ai-workflow refactor interview).
- 2026-07-15 · Conway — design-decision methodology has ONE owner: the evidence method ("questioning an existing design") lives in [[engineering-canon]] beside the interview; [[ai-workflow]] only points to it.
- 2026-07-15 · Brooks/Constantine — the always-on mandates (educator, anti-sycophancy) are memories ([[ai-workflow-coaching]]) summarized by the `session-start` hook, per the artifact litmus; the ai-workflow skill teaches them by pointer, not restatement.
- 2026-07-15 · Parnas — whether to add a **component or system** is decided by a *recurring-decision count*: tally the features that need the design decision it would hide (one → inline, rule of two; several → its own module). Einar-endorsed; now the operative test in the `DESIGN.md` scope guard beside the interaction test, and the concrete operationalization of the [[no-speculative-interfaces]] rule of two.
- 2026-07-17 · Martin — Clean Code added as lens 7 (the only sub-module lens: reader-cost at the line). Explicitly **subordinate to Elegance** where they collide: apply its values (intention-revealing names, single responsibility, DRY), reject its quantitative dogma (extract-till-you-drop, length limits, comments-as-failure) when it would multiply tiny methods — that proliferation is what the Elegance north star forbids. Never cite Clean Code to win a mechanism-proliferation argument against Elegance.
