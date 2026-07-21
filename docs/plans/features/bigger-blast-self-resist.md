# Feature — bigger blast + self-resist passive

**Blocking decision:** **[damage-buff channel](../decisions/damage-buff-channel.md)** — grill that first. **Ship gate:** none.

## What it is

A passive with two coupled effects: **larger blast radius** on your explosions, and **resist your own blast damage** (so you can stand closer / rocket-jump without killing yourself).

## Essence gate

(a) Changes how the owner plays — enables aggressive close-range and self-launch play. (b) Interacts with projectiles (the explosion), passives, and health. Passes — and it's the feature that makes [proximity explosion](proximity-explosion.md) compose with the passive system.

## Why it's blocked

The **self-resist** is the damage-buff channel's **first real consumer**: it's an owner-passive-scaled multiplier applied to a damage value. Build it before the channel exists and you pre-decide the ADR in shipped code. So it follows the channel decision.

The **bigger radius** half is *not* blocked — it folds like the existing on-spawn buffs (`SetRadiusMultiplier` shape). It could ship independently; the self-resist waits.

## Re-opens

Self-resist makes the rocket-jump fantasy real — which **re-opens [proximity explosion](proximity-explosion.md)'s self-exclusion** (v1 excludes the instigator's own pawn). Wanting self-knockback here means the blast must include the instigator when this passive is present. That is **not** a clean `SpawnModifier` +2-fields extension — count the Session-1 blast lines that must change to include the instigator's own root; if >0, it's a reshape, not an additive fold. Flag during the grill.

## Open questions to grill

1. **Is self-resist a multiplier or a flat reduction?** (Feeds the channel's shape — additive vs. multiplicative.) **Recommend:** multiplicative (scales with blast damage naturally).
2. **Does bigger radius change falloff shape or just the outer radius?** **Recommend:** outer radius only in v1 (falloff stays linear-to-edge), consistent with the explosion's v1 pin.

## Proof

PlayMode: owner with the passive rocket-jumps (takes reduced/zero self-damage, gains knockback); a victim in the enlarged radius takes the scaled hit. EditMode covers whatever multiplier math the channel lands.
