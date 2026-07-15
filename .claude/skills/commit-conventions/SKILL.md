---
name: commit-conventions
description: House style for commits in this repo — describe FEATURES and BEHAVIOUR, never implementation; scope unrelated tooling noise out of the commit; NEVER add a Co-Authored-By Claude trailer. Use BEFORE running `git commit`, or when asked to summarize/describe a diff for a commit message. Pairs with the [[no-claude-coauthor-on-commits]] memory.
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
- ❌ "Added a `HitResolver.ResolveExchanges` presence check over the `_damage`/`_knockback`/`_hitstun` handles and slimmed the `ProjectileImpact` constructor…" (implementation detail — belongs in code)

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
