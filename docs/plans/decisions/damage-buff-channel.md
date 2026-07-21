# Decision — the damage-buff channel

**Status:** open. Grill this in its own session; the deliverable is an ADR (claims the next free number when the file is written — numbers are never reserved in advance). Do **not** bolt it onto a feature.

## The decision

How does a runtime buff — a crit, a level-up, an owner's damage passive, a pickup — reach a projectile's **dealt** damage (and, by the same shape, its dealt knockback and hitstun)? Today that path is undesigned: dealt values are authored constants with no way for a runtime source to scale them.

## Why it's deferred, and why it's hard

Three requirements pull in different directions, and no single candidate covers all three:

1. **Owner-scaled** — a buff owned by the shooter multiplies what *their* projectiles deal (on-spawn, once per shot).
2. **Per-target** — blast falloff needs a *different* multiplier per victim within one shot (near takes more than far). This is not an owner buff; it's computed at the hit site.
3. **Flat additive** — an owner adding a flat `+N` damage to their projectiles. The old pipeline faked this by contributing a second courier effect; it is now inexpressible by design and the channel owes it back.

## What exists today

- `DamageController` owns the outgoing `Amount` (mutable at runtime). `KnockbackController` and `HitstunController` each own their dealt magnitude the same way.
- All three carry an `OutgoingModifiers` registry — structurally present, **zero contributors**. The seam is built; nothing feeds it.
- On-spawn buffs already have a working shape elsewhere: `SetRadiusMultiplier` / `SetMaxHealthMultiplier` fold a per-spawn scalar. A damage multiplier *could* follow that shape.
- A prior `[SerializeReference] DamageModifier`-fold attempt was built and **reverted** — evidence the obvious answer has a trap. Understand why it failed before re-proposing it.

## Candidates

| Candidate | Covers | Cost |
|---|---|---|
| On-spawn damage multiplier folded like `SetRadiusMultiplier` | owner-scaled (1) | doesn't touch per-target (2) or flat-additive (3) |
| Standing dealing-end `OutgoingModifiers` contribution | owner-scaled (1) + flat (3) | per-target (2) still needs a hit-site seam |
| Per-target field on `HitMessage` | per-target (2) | wider wire/shape; only blast needs it today (rule-of-two watch) |
| Per-target scale of `DamageController.Amount` at the dealing handler (layer 2) | per-target (2), narrowly | local to the dispatch; the minimal seam |

The likely answer is a **combination**, and the grill's job is to find the fewest mechanisms that cover all three without building the wide per-target field before a second consumer earns it.

## What rides on it

- **[Bigger blast + self-resist](../features/bigger-blast-self-resist.md)** — self-resist is an owner-passive-scaled multiplier onto a damage value; it is this channel's **first real consumer** and forces the shape.
- **Blast falloff** in [proximity explosion](../features/proximity-explosion.md) — ships a *minimal local* per-target seam now (doctrine: narrow existing seam, don't unify prematurely). This decision decides whether that seam generalizes wide.
- Scalable/resistible knockback — the dealing-end contributor *is* this channel; no new hit plumbing.

## Also resolves

The `presence-dispatch` skill's standing **OPEN note** — buffs-as-data vs. presence-dispatch. This channel is the named trigger for deciding whether that data/behaviour split collapses into one mechanism. Shrink the note to a pointer here once the ADR exists.

## Trigger

The first ability or pickup that must boost outgoing damage. Self-resist (a passive that scales *incoming* damage from the owner's own blast) is arguably already that consumer — which is why it's blocked here.
