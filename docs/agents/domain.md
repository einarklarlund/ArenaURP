# Domain Docs

How the engineering skills should consume this repo's design documentation. **Single-context repo.**

## Before exploring, read these

- **`DESIGN.md`** at the repo root — the design log: the two north stars (Essence + Elegance), the open-questions register (decisions deliberately deferred — do not improvise them), and the ADR index. Read this at the start of any design task or new feature.
- **`docs/adr/`** — the ADRs (`NNNN-kebab-title.md`). Read the ones that touch the area you're about to work in. This is the location the mattpocock skills expect by default, so they find ADRs here without an override.
- **`docs/plans/master-execution-dag.md`** — the roadmap / build-sequencing hub (what's next, in what order).

There is **no `CONTEXT.md` glossary yet.** DESIGN.md is a design log and ADR index, *not* a glossary — don't treat it as one. If a domain term genuinely needs pinning, `domain-modeling` creates `CONTEXT.md` at the repo root lazily, at the moment the first term is resolved. Until then, proceed without one — don't flag its absence.

## Writing an ADR

ADRs live in `docs/adr/`. The repo's own convention (documented in DESIGN.md → "Writing an ADR") governs and takes precedence over the generic skill format:

- **Few and small** — one screen. Write one only for a load-bearing, contested choice whose rejected alternatives are worth remembering. Most decisions need none.
- **Claim the number when you write the file** — take the next free `NNNN` at creation; never reserve one in advance. Until written, refer to an ADR by title.
- **Immutable once accepted.** To change one, add a short superseding ADR or a one-line "Superseded by NNNN — <why>" note on the old one. Never rewrite an accepted ADR.
- The **Evidence** and **Alternatives rejected** sections are the method's receipt — empty ones mean the method was skipped, not that they were optional.

## Flag ADR conflicts

If your output contradicts an existing ADR, surface it explicitly rather than silently overriding:

> _Contradicts ADR-0005 (hit-pipeline layered messages) — but worth reopening because…_

## Use the project's vocabulary

When your output names a domain concept (issue title, refactor proposal, hypothesis, test name), use the terms as this codebase uses them — start from the `arena-systems-map` skill, which narrates the core nouns (pawn, projectile, match, ability) and points at each governing skill. Don't drift to synonyms.
