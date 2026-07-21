# Decision — ability acquisition & persistence

**Status:** acquisition **decided in principle** (grill to confirm + ADR); persistence **open**. Two questions the ability batch raised; both live in one home so they aren't re-answered per ability.

## The decisions

### 1. Acquisition — where do abilities come from?

**Direction (to confirm):** a two-tier direct-grant model, no XP track:
- **Primary** — fight over **smash-ball pickups** on the map that grant **permanent** passives + actives.
- **Secondary** — **limited-use** active pickups.

Keep the acquisition source **hidden from the abilities themselves** — the grant path is already abstract (`AbilityPickup` → grant), so the source can change without touching an ability. Caveat to grill: "fight over pickups on the map" carries mild positional pressure; mitigate with a draft/choice framing rather than a spawn scramble.

### 2. Persistence — do abilities survive death?

Per-life vs. per-game. This must live in **one** home — the roster/ownership lifecycle + `ResetState` — not be re-answered per passive. It interacts with respawn.

## What exists today

The passive model already carries the switch: `persistAcrossDeath` is a single per-life/per-game knob on the shared-registry loadout (ownership on the player, `PlayerPassiveLoadout.Owned`; execution on the pawn/projectile). **The switch is built — this decision picks its value(s)** and whether the value is uniform or per-ability. The grant path (`AbilityPickup` → `Grant(registryChild)`) is the implementation home for the acquisition side.

## What rides on it

Nothing is *blocked* on it — the grant path is abstract, so features build against it regardless. It gates *shipping/tuning* the pickup economy, and it's the home for the `persistAcrossDeath` value that [movement-steal](../features/movement-steal-projectile.md) and every passive read.

## Trigger

The first ability that must survive death, or the first non-pickup grant source.
