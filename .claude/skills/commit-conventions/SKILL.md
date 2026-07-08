---
name: commit-conventions
description: How to summarize working changes and write a commit for this repo — the house style for commit messages and what to leave OUT of a feature commit. The rule that matters: describe changes at the level of FEATURES and BEHAVIOUR (what the player/system can now do, what was renamed, what content was added), NOT how they were implemented (no class names, no method signatures, no line-by-line rationale — that lives in the code's own doc comments). Keep the subject one concise line; keep the body a short bulleted "what changed" list. Also covers scoping a commit to the actual feature: exclude unrelated Claude/editor tooling noise (`.claude/settings.json`, `.claude/scheduled_tasks.lock`, stray FishNet/Unity reimport `.meta` files) and NEVER add a `Co-Authored-By: Claude` trailer. Use this BEFORE running `git commit` when asked to commit the current changes, or when asked to describe a diff for a commit message. Pairs with the [[no-claude-coauthor-on-commits]] memory.
---

# Commit conventions (curated for this repo)

When asked to "summarize the changes and commit," produce a **feature-level** description, not an
implementation walkthrough. Someone reading `git log` should learn *what the game/system can now do* and
*what moved*, without needing the diff.

## Message shape

- **Subject** — one concise line naming the feature(s). Semicolon-join if a commit legitimately spans two
  related things (e.g. `Passive stat buffs as effects; new max-health buff + Big Bullets/Bouncy Ball abilities`).
- **Body** — a short bulleted list of *what changed* in behaviour/content terms. Each bullet is an outcome,
  not a code change.

## Describe features, NOT implementation

Say what changed at the level of behaviour, content, and renames. Do **not** narrate class names, method
signatures, contexts, or the reasoning for a particular design — that already lives in the code's doc comments
and in memories/skills.

- ✅ "Passives now author a list of stat-buff effects (walk speed, jump, fire rate, …) folded into the pawn while equipped."
- ✅ "Spawned objects can be granted extra durability (survive more ricochets/bounces)."
- ✅ "New content abilities Big Bullets and Bouncy Ball, plus their pickups."
- ✅ "Rename the on-spawn buffs to name the system, not the object: BuffProjectileSpeed/Scale → BuffSpeed/Scale."
- ❌ "Added `IMovementModifierContext`/`IWeaponModifierContext` sink capabilities and a `PawnStatBuff` context that appends to `_appliedMove`/`_appliedWeapon`…" (implementation detail — belongs in code)

Rule of thumb: if a bullet only makes sense to someone who has the diff open, it's too low-level — lift it to
the behaviour it produces.

## Scope the commit to the feature

Before committing, keep unrelated noise OUT of the feature commit (stage the feature, then unstage these):

- Claude/editor tooling churn: `.claude/settings.json`, `.claude/scheduled_tasks.lock`.
- Stray Unity/FishNet reimport artifacts that aren't part of the work (e.g. incidental `*.csproj.meta` /
  `*.sln.meta` files that appear from a package reimport).
- Include skill/doc updates only when they document *this* change.

A Unity feature commit legitimately spans code + `.meta` + prefabs + scenes + `.asset` content — those belong
together; keep them in.

## Never add a Claude co-author

Do not append a `Co-Authored-By: Claude …` trailer. See [[no-claude-coauthor-on-commits]].
