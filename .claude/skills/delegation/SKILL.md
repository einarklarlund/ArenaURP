---
name: delegation
description: Route investigation and testing to subagents, and match process weight to a task's coupling. Use when a task needs file-searching or test-running, when deciding between the full design flow and a quick try-and-verify, or when unsure how much ceremony a change deserves.
---

# Delegation

**Main-thread context is for the design.** Searching and validating are someone else's job — spawn a subagent and keep only its conclusion.

Skill drift is an attention failure, not a memory one: after fifteen file reads and a prefab dump, the skill loaded at turn 2 is no longer present. Delegating buys that attention back.

- More than ~3 files to answer, or a question crossing a Unity GUID boundary → **`unity-scout`**.
- Running tests, compile-checking, or parsing test XML → **`unity-validator`**.
- An open design question with several viable shapes → the mattpocock `grill-with-docs` flow, before any code.

A well-prompted subagent returns a conclusion; one that returns file dumps has the wrong prompt. In a multi-step autonomous run, validate *between* steps — per-step error compounds (four 95%-correct steps ≈ 81%), and an end-only gate can't localize where it drifted.

## Two speeds — weight tracks coupling, not size

- **Fast-path** — leaf code: dev-only tooling, editor scripts, throwaway converters. No non-local invariants, cheap to regenerate. Try it and verify (the game runs, a test goes green); a design interview would only be ceremony.
- **Gauntlet** — core code: anything networked, server-authoritative, or wired into the interacting-mechanisms web (`Pawn`, health, projectiles, abilities, match flow). Correctness lives in invariants no paragraph carries — the skills and ADRs *are* that description — so it earns the full mattpocock flow: `grill-with-docs` (grilled against the [[engineering-canon]] lenses) → `to-spec` → `to-tickets` → `implement` → `code-review`, with an ADR for any real decision.

The tell: **could a competent stranger regenerate this correctly from one paragraph?** Yes → fast-path. No → gauntlet. Size doesn't enter it: a five-line edit touching a `SyncVar`, an RPC, a `[Server]` method, or the hit-exchange web is coupled, so it takes the gauntlet.
