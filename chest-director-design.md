# Chest and experience-capsule directors: design

Status (2026-10-30): stream E built, uncommitted. Chests and experience capsules spawn on level generation's
chest sites, in rooms next to their player's room. Code is under `Assets/Scripts/ChestDirector/` and
`Assets/Scripts/ExperienceCapsule/ExperienceCapsuleDirector.cs`. The distance-and-raycast placement from the first
version (milestones 1–5, 2026-10-22 to 10-26) is gone. This doc is the source of truth.

## Goal

The server keeps every player supplied during a match:

- **2 supply chests and 2 weapon chests** per player. A chest lasts **20 s**; an empty slot refills after 1–4 s.
- **2 experience capsules** per player, each in a different room. A capsule has no timer; when one breaks, another
  appears.
- Everything appears in a **room next to the player's room, never in it**, on a chest site that level
  generation chose.
- Each count is a setting plus a per-player extra, so an upgrade can raise it ("+1 each", stream G).

## Where sites come from

Level generation finds the sites, not the directors. `SiteStage` (stream B, `LevelGen/Core/Generation/`) looks at
the level's features and emits `ChestSite { Kind, Room, Position, YawDegrees }` into `LevelDescription`: sinkhole
middles, border corners, building rooms (ground, upper floors), balconies, hilltops, valley bottoms, behind fences,
near cars, and open ground to top up any room with fewer than 3. `Position` is on the surface; the yaw faces the
room's middle. Sites are at least 1.5 apart.

A room is a level-gen plot. At runtime the directors read only `LevelSync.Instance.Rooms` (a `RoomGraph`):
`RoomAt(position)`, `Adjacent(room)` (rooms a player can walk, jump or drop into) and `ChestSitesIn(room)`. They never
look at plots, buildings or terrain. What they decide at runtime is only what depends on live state: which room
the player is in now, and which sites are taken.

## Rules

### Picking a site (`SitePicker.TryPick`)

1. The player's room is `RoomAt(pawn position)`. Outside every room (-1): nothing spawns; the slot tries again later.
2. Candidate rooms are `Adjacent(playerRoom)`, minus the player's room and any rooms the caller wants avoided,
   in random order. The first one with a free site wins.
3. Within a room, a free **BuildingUpper** or **Balcony** site comes first (B's brief: prefer second floors and
   balconies); otherwise any free site. Random among equals.
4. If no adjacent room has a free site, rooms **two steps out** are tried the same way (never the player's room or
   an adjacent one). A caller can turn this off.
5. **One object per site**, shared by every player and by chests and capsules (`SiteClaims`). A site is free again
   once its chest or capsule is no longer spawned. No other spacing rule: sites are already 1.5 apart.

### Chests (`ChestDirector`)

1. Chests exist only while `MatchManager.State == MatchState.During`. On leaving `During`, or when a **new level is
   issued** mid-match (`LevelSync.OnServerLevelIssued`), every chest goes and all slots are cleared, so the next tick
   starts a fresh, staggered first wave on the new level.
2. Each player has `supplyChestsPerPlayer + extra` supply slots and `weaponChestsPerPlayer + extra` weapon slots.
   A slot holds at most one chest.
3. An empty slot (opened or expired) refills after a random delay in `respawnDelay` (1–4 s). If no site is free, it
   waits another delay and tries again.
4. **First wave:** a slot's first chest gets a random starting age in `[0, lifetime)`, so a wave doesn't expire
   together. Its timer bar starts partway through.
5. A slot only refills while its player has a living pawn. A dead player's chests run out as normal.
6. A player who leaves keeps their chests until they expire or are opened; their slots go.
7. Anyone can open any chest. If an enemy opens yours, your slot refills next to wherever you are.
8. The server's expiry is final: if it lands before a client's predicted open, the open doesn't happen.

Chest prefabs have their origin at a bottom corner. `SitePicker.CornerFor` turns the chest to the site's yaw and
offsets the corner so the chest's bottom centre is on the site.

Dropped in this version: the distance bands, the score-based weapon-chest distance (a trailing player's weapon
chests used to spawn closer), the ray fan, the floor and wall-fit checks. Sites are already on clear floor.

### Experience capsules (`ExperienceCapsuleDirector`)

1. **First wave:** the first tick a player has a living pawn in a match (or on a new level), they are owed
   `capsulesPerPlayer + extra` capsules. Each goes in a **different** room next to theirs.
2. **Breaking:** when one of a player's capsules is no longer spawned (shot open; `ExperienceCapsule` pool-despawns
   itself), another is owed at once. No timer.
3. A replacement goes next to the room the player is in **now**. It avoids the rooms of the player's other live
   capsules, and the room the broken one was in. If only the broken capsule's room is left among the adjacent ones,
   it's used again before trying a room two steps out.
4. If no site is free, the owed capsule tries again after a `respawnDelay` roll. A dead player's owed capsules wait
   for their pawn.
5. The capsule stands on its site, raised by half its collider's height so its bottom rests on the surface.
6. Capsules go when the match ends or a new level is issued, like chests. A player who leaves keeps their capsules
   standing until broken or the match ends.

There is no "pawn spawned" event: the director polls `ControlledPawn` each tick, as `ChestDirector` does, so
`MatchManager` needed no change.

### Per-player extras (for stream G's "+1 each" upgrade)

- Upgrades push into a server-only per-player record: `ChestDirector.Instance.RecordFor(player)` has
  `ExtraSupplyChests` and `ExtraWeaponChests`; `ExperienceCapsuleDirector.Instance.RecordFor(player)` has
  `ExtraCapsules`. `ExtraChestsUpgrade.ApplyTo(NetworkPlayer)` adds 1 to each per stack, on the server.
- Each director reads the record every tick: new chest slots fill at once and removed slots leave their chests
  standing; a capsule raise spawns at once and a cut lets the surplus stand until broken.
- `NetworkPlayer` calls `ServerResetRecord(player)` on both when its upgrades clear (death, match reset). Records are
  also dropped when the player leaves or the match ends; a new level keeps them.

### Visuals (client side, unchanged)

Driven by `ChestTimer`'s synced times, so a late joiner sees them where everyone else does:

- **Timer bar:** a billboarded bar above the chest. It keeps its on-screen size beyond 10 units
  (`timerBarFullSizeDistance`).
- **Spawn beam:** a `LineRenderer` 15 units up, thick at the bottom, fading out over 2.5 s (longer with
  `ChestBeaconUpgrade`). Only the player the chest was spawned for (`ChestTimer.ForPlayer`) sees its beam, including
  a first-wave chest with a nonzero starting age; anyone can open the chest.

## Components

- **`ChestDirector`** and **`ExperienceCapsuleDirector`**: server-side `NetworkBehaviour`s on the Match Manager
  prefab. Their tuning is in fields on that prefab (no ScriptableObject, so nothing writes an asset at runtime):
  `ChestDirector` has the two chest prefabs, `supplyChestsPerPlayer`, `weaponChestsPerPlayer`, `lifetime` and
  `respawnDelay`; `ExperienceCapsuleDirector` has the `experienceCapsule` prefab, `capsulesPerPlayer` and
  `retryDelay`. Visual tuning is on the chest prefabs. Each keeps a server-only `PlayerRecord` per player
  (`RecordFor`) that upgrades add to (`ExtraChestsUpgrade`), reset by `NetworkPlayer` when its upgrades clear.
- **`SitePicker`** (static, plain C#): `TryPick`, `CornerFor`, `Footprint` (a chest prefab's combined
  `BoxCollider` bounds in its own space).
- **`SiteClaims`** (static, server): site position → the `NetworkObject` on it. Cleared when the server starts and
  stops.
- **`ChestTimer`**: unchanged. Synced `StartTime`, `SpawnTime`, `ExpireTime`; server despawns at expiry; the
  client draws the bar and the beam.

## Tests

`Assets/Scripts/Editor/SitePickerTests.cs` (Assembly-CSharp-Editor) builds a four-room level by hand (0–1–2 linked
both ways, a cliff from 2 down to 3) and checks over 50 seeds: only adjacent rooms, never the player's own; a
one-way cliff counts only downhill; avoided rooms and taken sites are skipped; the two-steps-out fallback and
turning it off; nothing free or outside the level picks nothing; balconies come first; `CornerFor` puts the
footprint's bottom centre on the site; both chest prefabs' footprints run from their corner. Run them with
`unity command script eval 'return EditModeTestRunner.RunInline("SitePickerTests");'`.

Play-tested 2026-10-30 on a host with one player on a generated level: chests landed exactly on sites in rooms
adjacent to the player's, never in it; two capsules in two different adjacent rooms on first spawn; breaking one
gave a replacement in the third adjacent room; chests expired and refilled; one extra each (then `ServerSetExtra`, now the
record) added a third chest of each kind and a third capsule; regenerating the level cleared everything and re-seeded on the new
rooms; capsules rest with their bottom on the ground. Not tested: two players competing for sites, a remote client.

## Open items

- A player in a room with no outgoing adjacency (e.g. at the bottom of a one-way cliff with no way out) gets
  nothing until they move: `Adjacent` is empty there, so there's no "two steps out" either.
- Whether weapon chests should favour trailing players again in some room-based form (e.g. nearer rooms first).

## Decisions

- 2026-10-30 (stream E): chests and capsules use level generation's chest sites in rooms next to the player's,
  never in it. The ray-fan placement, distance bands and score-based weapon distance were removed.
- 2026-10-30: chests keep their 20 s lifetime, timer bar, beam, 1–4 s respawn delay and staggered first wave.
- 2026-10-30: one object per site, across players and between chests and capsules; no extra spacing rule.
- 2026-10-30: capsules stand on free chest sites. A replacement goes next to the player's current room, avoiding the
  broken capsule's room if it can.
- 2026-10-30: no `MatchManager` "pawn spawned" event; the capsule director polls for each player's first living
  pawn.
- 2026-10-22: dropped the ammo report. Supply chests ignore the player's ammo so the user can judge the game without
  that help.
- 2026-10-26: chest prefabs have their origin at a bottom corner.
