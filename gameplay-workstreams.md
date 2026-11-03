# Gameplay notes → workstreams

Each workstream is planned and built in its own session. **All sessions work in the same checkout**
(`/Users/einar/Projects/ArenaURP`, branch `pawn_refactor`) — no worktrees — so the streams are cut along file
boundaries: each one owns a set of files, and uses the others' existing public APIs instead of editing them.

## Rules for working in the shared checkout

- **Only edit files your stream owns.** If you need a change in another stream's file, ask the user rather than
  making it, unless the ownership table below grants it.
- **Re-read a file right before editing it.** Another session may have changed it since you last looked.
- **Append-only files** (`Events/NetworkUIEvents.cs`, `Events/LocalUIEvents.cs`): any stream may *add* events;
  nobody renames or removes existing ones.
- **One Unity Editor.** A compile error from any session blocks everyone's play-mode testing. Keep the project
  compiling between edits; don't leave half-finished signatures across a pause.
- **Scenes and shared prefabs** (the main scene, the HUD canvas, `UIManager`'s view list): build new UI as its own
  prefab, and wire it into the scene as a final, small step. Tell the user before saving the scene, so two sessions
  don't save it at the same time.
- **Don't commit other streams' files.** Stage only your own paths (`git add <paths>`), never `git add -A`.
  `pawn_refactor` has a large amount of the user's uncommitted work; leave it alone.

## Shared assumptions

- **"Room" means a level-gen plot** (`PlotDescription` in `LevelGen/Core/LevelDescription.cs`). Plots meet at
  `TransitionDescription` edges (PlotA/PlotB), which is the adjacency graph. B decides how sub-plots count.
- **B owns the room API** ("which room is this position in", "rooms adjacent to room X"). E and F consume it.
- **C owns the homing-pickup behaviour** (kinematic, flies at one claimed player). F reuses it; D's death drops
  reuse the existing `ExperienceDrop` for now and can switch later.
- **D owns the upgrade framework**; G fills in the catalogue.

## Waves

| Wave | Workstreams | Can start when |
|---|---|---|
| 1 | A, B, C, D | now, in parallel |
| 2 | E, F | B done (F also needs C) |
| 3 | G | D done; each upgrade also needs the system it touches |

## Wave-1 file ownership

| Stream | Owns (may edit) | Uses without editing |
|---|---|---|
| **A** Weapons + interaction | `Pawn/Pawn.cs`, `Pawn/Weapon/*`, weapon prefabs, `Managers/InputManager.cs` + the input actions asset, `UI/MainView.cs` | `NetworkPlayer` ammo API |
| **B** Level gen | `LevelGen/**`, `Tools/LevelGenTests`, `Networking/LevelSync.cs`, `UI/LoadingView.cs` | — |
| **C** Pickups + chests | new pickup folder, `AmmoChest/*` → `SupplyChest/*`, `WeaponChest/*`, chest prefabs, the chest-prefab references in `ChestDirector/*` | `NetworkPlayer.AddAmmo`, `Pawn.CurrentHealth`/`MaxHealth`, `Pawn.PrimaryWeapon` |
| **D** Progression | `Player/NetworkPlayer.cs`, `Managers/MatchManager.cs`, `ExperienceCapsule/*`, `UI/DeathView.cs`, new level-up / upgrade views, new upgrade folder | `Pawn.OnDeath` (via `MatchManager.ServerOnPawnKilled`), `Pawn.MaxHealth`, A's `UpgradeChoice1/2` input actions |

Grants across lines:
- **A adds `UpgradeChoice1` / `UpgradeChoice2` input actions** (keys 1 and 2) early on, for D.
- **C may add one server heal entry point to `Pawn.cs`** (health is `ServerOnly`, and chest pickups only exist on
  the opener's side), in its own clearly separated block, after telling the user — A owns the rest of the file.
- **A keeps `Pawn.PrimaryWeapon` meaning "the weapon in hand"** (or renames it and updates C's call site), since
  C's small ammo pickup reads its ammo type.
- **D's HUD needs (level display, etc.) go in D's own view**, not `MainView.cs`.

---

## A — Weapons and interaction prompt

- Primary and secondary slot; only the held one is in your hands and fires.
- Picking up a weapon replaces the held one.
- Switch with Tab, scroll wheel, Q or E.
- HUD: the held weapon's ammo text is bigger than the other ammo texts.
- Hover prompt when in range of and looking at an interactable: "Open weapon chest", "Pick up Machine Gun".
  Use the GameObject's name; no new name field.

Plan questions: swap synced to other clients; swap delay; what you spawn holding; secondary on death;
stripping "(Clone)" from names; verb per interactable kind; showing the key to press.

## B — Level gen: spawn sites, room graph, loading screen

- **Chest sites**, tagged by kind: middle of sinkholes, corners of border walls, inside buildings (prefer second
  floor / balcony), hilltops and valley bottoms, behind fences, around cars.
- **Pickup-line sites**: 5 points lining a feature — building edge, boundary wall, fence, balcony, sinkhole edge,
  ledge, path up a ramp/bank.
- **Room graph** in `LevelDescription`: room per plot, adjacency from transitions, runtime position → room lookup.
- Emitted into `LevelDescription`, deterministic, tested with `dotnet test Tools/LevelGenTests`.
- **Loading screen** showing which generation step is running.

Plan questions: rooms = top-level plots or sub-plots; building interiors' room; minimum sites per room; features
with no representation yet; `GeneratorVersion` bump; generation moving off the main thread so the UI can draw;
whether retries and `LevelBuilder.Build` show as steps.

## C — Supply pickups and chest contents

- Small health (+3 HP) and small ammo (by held weapon's type: 8 bullets, 4 shells, 3 bolts, 3 explosives,
  5 energy).
- Homing: within 7 units, flies to the player like XP drops but never a dynamic rigidbody; claimed by that player
  only once it starts flying. Radius is a tunable value (G's 2× upgrade).
- Rename Ammo chest → Supply chest. Opening it instantiates 4 pickups (≥1 health, ≥1 ammo, other 2 50/50) on the
  opener's side only, homing at the opener. Count is tunable (G's 2× upgrade).
- Weapon chests drop 1 weapon by default; count is tunable (G's 2× upgrade).

Plan questions: one component for local chest pickups and networked world pickups (F) or two; health/ammo over
max; ammo pickup when the held weapon uses none; full-health pickup consumed or not.

## D — Levels, level-up choice, death drops, respawn flow

- XP → levels: thresholds 15, 30, 50, 70, 90, 110, … (15, 15, then +20 each).
- Level-up panel at the bottom of the screen: "Level up!", then "Press 1 for:" / "Press 2 for:" each with an
  upgrade description; 1 or 2 picks.
- On death, drop XP drops = half your current XP (minimum 15), flying to your killer.
- Respawn no longer runs on a timer: before spawning you pick upgrades — 2, plus 1 per level — then spawn.
- Upgrade framework (definitions, random offers of 2, applying, stacking), proven with 2–3 simple upgrades.

Plan questions: does the victim lose the dropped XP / levels; upgrades kept across deaths; "2 + 1 per level"
before every spawn or only the first; queued level-ups; validating picks while XP is client-authoritative;
suicide/environment deaths; a timeout so a player can't stay dead forever.

## Wave-2 file ownership

Wave 1 is built (uncommitted). What wave 2 builds on:
- **B's room API:** `LevelSync.Rooms` (a `RoomGraph`, rebuilt each level) has `RoomAt(position)`, `Adjacent(room)`,
  `ChestSitesIn(room)`, `PickupLinesIn(room)` and `Centre(room)`. Sites are `ChestSite` (kind, room, position,
  yaw) and `PickupLine` (kind, room, points) in `LevelGen/Core/LevelDescription.cs`.
- **C's pickup:** `Pickup/Pickup.cs` (a MonoBehaviour: kind, `homingRadius`, `Launch`, `Claim`). Supply chests
  and XP drops use it locally on one client.
- **D's spawn flow:** `MatchManager.ServerSpawn` / `ServerSpawnPawn`; first spawn goes through `UI/FirstSpawnView.cs`.

| Stream | Owns (may edit) | Uses without editing |
|---|---|---|
| **E** Directors | `ChestDirector/*`, `Editor/ChestPlacementTests.cs`, `chest-director-design.md`, `ExperienceCapsule/*` (handed over from D), a new capsule director, the chest-director settings asset | `LevelSync.Rooms`, `SupplyChest`, `WeaponChest`, `MatchManager` |
| **F** World pickups | `Pickup/*` (handed over from C), a new world-pickup director and its settings | `LevelSync.Rooms`, `SupplyChest` |

Grants across lines:
- **E may add one server-side "pawn spawned" event to `MatchManager.cs`** (raised in `ServerSpawnPawn`, with a
  first-spawn flag) if nothing existing says when a player spawns. Tell the user first; D owns the rest.
- **F must keep `Pickup`'s local use working** (supply chests, XP drops) while adding networked world pickups.
- **Scene:** both add a director to the main scene. Same rule as wave 1: tell the user before saving it.

---

## E — Chest and experience-capsule directors (wave 2, needs B)

- Remove the raycast / distance-from-player chest placement (`ChestDirector/ChestPlacement.cs`).
- Chests spawn on B's chest sites, only in rooms adjacent to the player's room, never in it.
- Capsules: on a player's first spawn, 2 capsules in 2 different adjacent rooms; when that player's capsule
  breaks, another spawns in a different adjacent room. No timers.
- Per-player counts as settings (G's "+1 each" upgrade).

Plan questions: "a different adjacent room" when the player has moved; what chests keep from the timer/beacon
design; site reuse and spacing between players' chests.

## F — World pickup spawning (wave 2, needs B and C)

- Every room starts with 4 small pickups; once all 4 are taken, 4 new ones spawn in it after 10 s.
- At match start, 2 lines of 5 pickups on B's pickup-line sites.

Plan questions: health/ammo mix per room; placement within a room; whether lines respawn.

## Wave-3 file ownership

D's framework (`Assets/Scripts/Upgrade/`): an `Upgrade` ScriptableObject with `ApplyTo(NetworkPlayer | Pawn |
Weapon | Projectile)` hooks, applied once per stack on every peer, each hook changing state only on the peer that
owns it. Upgrades are listed in `Assets/Upgrades/Upgrade Catalogue.asset`, where the index is the network id.
A player's picks are `NetworkPlayer.Upgrades` (a `SyncList<byte>` of ids); `UpgradeCatalogue.StacksOf` counts
them. Already built: #11 max HP, #12 ammo capacity, #13 re-roll. Wave 3 builds the other 11 in three
parallel streams, split by the system each upgrade modifies:

| Stream | Upgrades | Owns (may edit) |
|---|---|---|
| **G1** Combat | #1 +1 damage, #7 fire rate after a hit, #8 −30% spread, #9 +30% projectile speed | `Pawn/Weapon/*`, `Projectile/*`, weapon/projectile prefabs, its new upgrade scripts and assets |
| **G2** Pawn | #2 +12.5% move speed, #4 health regen, #14 2× pickup radius | `Pawn/Pawn.cs`, `Pawn/Movement/**`, `Pickup/*` (its radius read), its new upgrade scripts and assets |
| **G3** Supplies | #3 chest spawn beam 2× (own beam only), #5 2× supply-chest pickups, #6 2× weapon-chest drops, #10 +1 chest/capsule each | `ChestDirector/*` + the chest-director prefab, `SupplyChest/*`, `WeaponChest/*`, `ExperienceCapsule/*`, its new upgrade scripts and assets; the server-side death-reset call (in `MatchManager.cs` or `NetworkPlayer.cs`) |

### Upgrade control flow (decided by the user)

Upgrades are hooks: an `Upgrade` changes its target through **public numeric fields** on that target. There are
two directions:

- **Pushed by the player** — targets with one owner for their whole life: the **pawn**, the pawn's **weapons**
  (held *and* the unheld secondary slot), and the player's **per-player entry on a director**. The player
  applies its upgrades when the target becomes its own, and to live targets when a pick is made.
- **Pulled by the target** — targets that are hard to push into, or that serve whoever interacts with them:
  - **Projectiles** (`PredictedSpawn` makes pushing awkward): read the `NetworkPlayer`'s upgrades through
    `owningPawn` and apply them to themselves.
  - **Chests**: a supply chest's or weapon chest's drop count is read from the **opener's** upgrades when opened
    (supply drops spawn on the opener's client).
  - **Pickups** don't hold upgrades: the homing radius is a field on the **pawn** (`pickupRadius`, pushed like any
    other pawn field), and a pickup reads it from each nearby pawn.

Field conventions:
1. **Keep base values and upgrade-written values in separate fields** — e.g. `baseSpread` from the prefab and
   `spreadMultiplier = 1` that upgrades write. The target uses the combination. On equip, a weapon resets its
   upgrade fields and the new holder re-applies, so a dropped weapon doesn't carry its old holder's upgrades.
2. **Stacking order** is not handled generally; the upgrade list is designed so it doesn't matter.
3. **Never write to a ScriptableObject asset at runtime.** Chest-director tunables move off
   `ChestDirectorSettings` onto fields on the chest-director prefab.
4. **Directors are per player**: the director keeps a per-player record (e.g. extra supply / weapon chests,
   extra capsules) that upgrades write. When the player's pawn dies, the server resets that player's record
   itself (from the server's death handling, e.g. `MatchManager.ServerOnPawnKilled`) — no client call.

Per-target notes:
- **Move speed (#2)** is client-predicted: owner and server must apply it on the same tick — fold the multiplier
  into the movement's predicted state/config rather than a field changed from outside, or reconciliation fights it.
- **Projectiles**: `owningPawn` → `NetworkPlayer` must be resolvable on the predicting client and the server
  before speed and damage are first read; other observers need the same speed.
- **Late joiners**: a client can get a pawn before `NetworkPlayer.Upgrades` arrives, so hooks also re-run when
  that list changes, not only on spawn.
- **Each hook states its peer** (e.g. `MaxHealth` is server-only, so that hook is a no-op on clients).
- **Behaviour upgrades** use the same field pattern; the target runs the behaviour: the pawn has
  `regenPerSecond` and ticks regen itself (#4); the weapon has an on-hit fire-rate bonus and a buff timer (#7),
  which needs a new projectile → weapon "hit an enemy" notification.
- **Re-roll (#13)** is an action in the offer UI, not a hook.

Rules:
- **New upgrade scripts go in `Assets/Scripts/Upgrade/`, assets in `Assets/Upgrades/`**, one file per upgrade;
  only touch your own. `Upgrade.cs`, `UpgradeCatalogue.cs` and `NetworkPlayer.cs` are shared: don't change them
  without telling the user first.
- **`Upgrade Catalogue.asset` is append-only** (index = network id). Re-read it right before adding, append only,
  and tell the user before saving it from Unity so two sessions don't save it at once.

---

## G — Upgrade catalogue (wave 3, needs D)

| # | Upgrade | Depends on |
|---|---|---|
| 1 | +1 projectile damage | Projectile |
| 2 | +12.5% move speed | Movement |
| 3 | Chest beacons last 2× | E |
| 4 | Health regen 3/s (spec unfinished in the notes) | Pawn health |
| 5 | 2× supply-chest pickups | C |
| 6 | 2× weapon-chest drops | C |
| 7 | Faster fire rate after hitting an enemy | A |
| 8 | −30% spread | A |
| 9 | +30% projectile speed | Projectile |
| 10 | +1 weapon chest, supply chest, capsule per player | E |
| 11 | +9 max HP | Pawn health |
| 12 | +40% ammo capacity | `NetworkPlayer` |
| 13 | Re-roll | D's UI |
| 14 | 2× pickup radius | C |
