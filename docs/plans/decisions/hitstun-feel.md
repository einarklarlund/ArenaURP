# Decision — hitstun feel / balance

**Status:** open, settled only by a playtest (not a design grill — but named here because it *gates shipping* a feature).

## The decision

Is the current hitstun tuning fun, or does it need a cap? The numbers as they stand:

- a bullet deals **0.2s** of hitstun,
- stuns **extend** rather than replace (each hit pushes the expiry out),
- default fire rate is **0.1s**.

So sustained accurate fire can lock a target out of shooting **entirely** — you can perma-stun someone you keep hitting. This is the deliberate consequence of "cannot fire for a duration," not a bug. The question is whether it *feels* right or is oppressive.

## Why it's deferred

It's a feel judgment; only a real playtest of sustained fire answers it. No design work unblocks it.

## What rides on it

- **[Hitstun passive](../features/hitstun-passive.md)** — an upgrade that adds/extends hitstun ships *gated on this playtest*. Build the passive; hold the release until the base feel is confirmed (or capped).

## Candidates if it's oppressive

- A per-target stun **cap** (diminishing returns on repeated stuns).
- A **decay** window (stun can't be extended past N seconds without a gap).
- Leave it — perma-stun is a skill-expression reward for perfect tracking.

## Trigger

The first real playtest of sustained fire.
