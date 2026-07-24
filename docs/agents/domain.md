# Domain Docs

How the engineering skills should consume this repo's design documentation. **Single-context repo.**

## Before exploring, read these

- **`CONTEXT.md`** at the repo root — the glossary of Arena's own terms, prefaced by the game's **essence** (the north star every feature answers to). Read it so output names concepts as this project does.
- **`docs/adr/`** — the ADRs (`NNNN-kebab-title.md`), indexed by [`docs/adr/README.md`](../adr/README.md). Read the ones that touch the area you're about to work in. This is the location the mattpocock skills expect by default, so they find ADRs here without an override.
- **`docs/plans/features.md`** — the grill-ready backlog (open features + the deferred design decisions in `docs/plans/decisions/` each rides). What's next, and what must be settled before it.
- The **`design-elegance`** and **`engineering-canon`** skills carry the design *values and lenses* a plan is judged against; `arena-systems-map` narrates the codebase. These load on demand — the docs above are the always-read set.

## Writing an ADR

ADRs live in `docs/adr/`. The repo's own convention governs and takes precedence over the generic skill format:

- **Few and small** — one screen. Write one only for a load-bearing, contested choice whose rejected alternatives are worth remembering. Most decisions need none.
- **The shape:** Context / Decision / Alternatives rejected (why each lost — the part future-you needs) / Evidence (the spike result or measured numbers the decision rests on) / Consequences, plus a one-line note on how it serves the essence and Elegance. The **Evidence** and **Alternatives rejected** sections are the method's receipt — an empty one means the method was skipped, not that the section was optional, and the decision is not yet trustworthy.
- **Claim the number when you write the file** — take the next free `NNNN` at creation; never reserve one in advance. Until written, refer to an ADR by title. (Reserving has drifted twice — a plan holding a number another ADR then took.)
- **Immutable once accepted, and cheap to break.** To change one, add a short superseding ADR or a one-line "Superseded by NNNN — <why>" note on the old one. Never rewrite an accepted ADR. If explaining a break takes more than a sentence, the ADR was too big or too speculative to have existed.
- After adding one, add its row to [`docs/adr/README.md`](../adr/README.md).

## Flag ADR conflicts

If your output contradicts an existing ADR, surface it explicitly rather than silently overriding:

> _Contradicts ADR-0005 (hit-pipeline layered messages) — but worth reopening because…_

## Use the project's vocabulary

When your output names a domain concept (issue title, refactor proposal, hypothesis, test name), use the term as `CONTEXT.md` defines it — not a synonym it lists under `_Avoid_`. The `arena-systems-map` skill narrates those nouns (pawn, projectile, match, ability) in motion and points at each governing skill.
