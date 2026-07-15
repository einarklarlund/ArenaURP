# Shared understandings — the canon-interview ledger

Positions Einar and Claude have **explicitly settled**, one dated line each, so interviews never re-ask them and past agreements aren't re-litigated. Maintained under the [[engineering-canon]] skill; loaded only with it, so this memory costs no ambient context.

**Rules:** append-only; check before interviewing; when a position changes, add a superseding line and strike the old one (`~~…~~ superseded YYYY-MM-DD`) — don't silently edit. Past ~40 live entries, compact: merge overlapping lines, delete struck ones.

Format: `- YYYY-MM-DD · <lens> — <the position, one line>`

## Charter — seeded 2026-07-15 from standing doctrine

_These map already-agreed project values onto the canon. The mappings were made by Claude when the skill was created; confirm or amend at the first interview._

- 2026-07-15 · Dijkstra + Brooks — the Elegance north star IS this project's form of intellectual manageability + conceptual integrity; canon arguments route through it rather than adding a parallel value (`DESIGN.md`).
- 2026-07-15 · Parnas — the rule of two is the honesty condition on information hiding: a decision must be *seen* to vary before a seam hides it ([[no-speculative-interfaces]]).
- 2026-07-15 · Constantine — contribution collections + notifications are a deliberate ceiling at data coupling; a proposal that introduces control coupling (mode flags, callee-selecting parameters) triggers redesign, not accommodation ([[cross-component-communication]]).
- 2026-07-15 · Brooks — Einar + the main thread are the "one mind"; subagents search, implement, and validate but never own design decisions (the surgical-team reading of the delegation rule in [[ai-workflow]]).
- 2026-07-15 · Brooks's law — fan-out (multi-agent Workflows) is for partitionable work only; applied to a design problem it adds communication paths, not output (practices register, "Workflows" row).
- 2026-07-15 · Lehman — the workflow-auditor cadence and "merge overlapping rules" are law-II conservation applied to the tooling; the same budget question ("what does this pay down?") now applies to every feature plan.
- 2026-07-15 · Conway — delegation boundaries are a design act: whether an agent can own a module without the whole design in context is *evidence* about whether the module boundary is right.

## Settled in interviews

_(none yet — the first interview is the codification itself; open questions listed in the 2026-07-15 session summary)_
