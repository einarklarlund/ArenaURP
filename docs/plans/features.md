# Features — the grill-ready backlog

_Forward-looking index of everything not yet built. Each entry is a small, self-contained brief written to be fed to a **`/grill-with-docs`** session one at a time — fuzzy-but-scoped, with the open decisions surfaced (not pre-answered) so the grill can walk them and emit an ADR. Governing north stars and the deferred-decision register live in [DESIGN.md](../../DESIGN.md); the doctrine each brief leans on lives in `.claude/skills/`._

**How to use this file.** Pick a feature. Read its brief. If it names a **blocking decision**, grill that decision first (`decisions/*.md`) — a feature can't be specced until the design decision it rides is settled. If it names only a **ship gate** (a playtest), build it and hold the release. Buildable-now features have neither.

**The shape of the work.** Two of the biggest items below are *design decisions*, not features — a channel or a lifetime several features share. Those are the real grill targets (`decisions/`); the features hang off them. A decision earns its own mechanism only when **two or more** features genuinely need it (rule of two) — so the timed-victim-debuff and per-observer clusters are built **feature-first**: ship the second instance, then extract the shared shape the third confirms. Don't build a channel before its features exist.

## The essence gate (every brief answers this)

An addition earns a place only if it **(a) drastically changes how someone plays** — its owner *or* their opponent — **and (b) interacts with an existing system** (movement, health, weapons, projectiles, pickups, passives, actives, kills). Passing the gate is necessary, not sufficient: a legal-but-shallow feature (changes play, but writes no upgrade×upgrade interaction) is *last in line*, never cut for illegality. See DESIGN.md's scope guard.

## Open features

| Feature | One line | Blocking decision | Ship gate |
|---|---|---|---|
| [Proximity explosion](features/proximity-explosion.md) | A new explosion projectile kind; re-dispatches a per-victim hit | — (coordinates with damage-buff channel for wide falloff) | — |
| [Passive-steal active](features/passive-steal-active.md) | Active capsule-cast that steals a passive (on-hit steal already ships) | — | — |
| [Combo fire-rate](features/combo-fire-rate.md) | Sustained fire ramps fire-rate (passive) | — | — |
| [Bigger blast + self-resist](features/bigger-blast-self-resist.md) | Passive: larger blast radius + resist own blast | **[damage-buff channel](decisions/damage-buff-channel.md)** | — |
| [Hitstun passive](features/hitstun-passive.md) | On-hit hitstun as a passive upgrade | — | hitstun-feel playtest |
| [Goop-slow & blind](features/goop-slow-and-blind.md) | Timed slow zone + timed screen-blind — the pair that earns the debuff channel | **[timed-modifier lifetime](decisions/timed-modifier-lifetime.md)** | goop: terrain-vs-attached playtest |
| [Movement-steal projectile](features/movement-steal-projectile.md) | Steals a movement buff on hit, timed (built LAST) | **[timed-modifier lifetime](decisions/timed-modifier-lifetime.md)** (incl. wire-payload) | — |
| [Per-observer features](features/per-observer-features.md) | Invisibility, smoke cloud, see-only-to-me health drop | **[per-observer rendering](decisions/per-observer-rendering.md)** | smoke also: terrain-zone playtest |
| [Reflect shield](features/reflect-shield.md) | Reflect incoming damage back at the shooter | — | **essence gate** (shallow → last) |

## Open design decisions (grill these → ADRs)

These are deferred on purpose so they aren't improvised. Each is a real trade-off with more than one live candidate; the trigger that should force it is named in the brief.

| Decision | What it settles | Features waiting on it |
|---|---|---|
| [Damage-buff channel](decisions/damage-buff-channel.md) | How a runtime buff/crit/level-up reaches a projectile's dealt damage (and knockback/hitstun); re-establishing the per-target multiplier | Bigger blast + self-resist; the wide version of blast falloff |
| [Timed-modifier lifetime](decisions/timed-modifier-lifetime.md) | The third modifier lifetime — `(modifier, expiry)` — plus its wire-payload; the shared debuff channel these earn | Goop-slow, blind, movement-steal |
| [Per-observer rendering](decisions/per-observer-rendering.md) | How each client can see a different world under server authority | Invisibility, smoke, see-only-to-me health |
| [Acquisition & persistence](decisions/acquisition-and-persistence.md) | Where abilities come from (pickup vs XP) and whether they survive death (per-life vs per-game) | The grant path behind every pickup/passive |
| [Hitstun feel](decisions/hitstun-feel.md) | Whether sustained fire locking a target out is balanced or needs a cap | Hitstun passive (ships gated on this) |

## Suggested order

- **Buildable now** (no blocking decision, no gate): Combo fire-rate · Proximity explosion · Passive-steal active. These three are disjoint and can go in any order.
- **Blocked on a decision**: Bigger blast + self-resist (grill damage-buff channel first) · Goop-slow & blind (grill timed-modifier lifetime) · Movement-steal (same, incl. wire-payload) · Per-observer features (grill per-observer rendering).
- **Gated on a playtest**: Hitstun passive (build it, hold on hitstun-feel).
- **Essence-gated, last regardless**: Reflect shield, and the shallow end of the per-observer cluster (invisibility-as-self-loop, smoke). Legal, but they write no upgrade×upgrade interaction — they wait until a *composing* version is pitched or they're accepted as shallow gadgets.

## Not on the backlog (recorded so they aren't re-proposed)

| Idea | Verdict |
|---|---|
| Blinding flashlight (standalone) | **Cut** — blind now exists via the timed-debuff channel; a distinct flashlight composes with nothing. Revive only as a reskin of blind. |
| Blink teleport | **Cut** — pure self-mobility, folds with no other system, orthogonal to build-a-character. No decision changes this. |
| Wings / extra air-jumps | **Re-pitch or cut** — needs an enemy-facing interaction to earn the essence. Do **not** schedule the additive-`MovementModifier` fold. |
| See-only-to-me health drop | **Revivable** — both original cut-reasons are gone (per-observer rendering adopted; map/resource control un-excluded). Re-pitch against the interaction test; rides the per-observer decision. Tracked in [per-observer features](features/per-observer-features.md). |
