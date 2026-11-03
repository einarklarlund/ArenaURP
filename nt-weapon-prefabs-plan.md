# Nuclear Throne weapons plan: prefabs

Follows [nt-weapons-plan.md](nt-weapons-plan.md), whose code steps 1–9 are done. That file's
"Weapon configs" table is the source for every weapon's stats. This plan covers the assets: folders,
a base weapon prefab, 27 weapon prefabs with cube blockout models, and the projectile prefabs they
fire.

## Status (2026-10-21)

**All 27 weapons are built (Batches 1–3).** Next step: the user's review of Batch 3's preview sheet,
then any silhouette fixes they ask for (edit the weapon's entry in its batch builder and rerun it).
Nothing in this plan is left after that.

Done:
- **Phases 0–3**, all compiled and play-tested on the host.
- **Batch 1:** Machinegun, Assault Rifle, Shotgun, Slugger, Crossbow, plus NT Bullet, NT Pellet,
  NT Slug and NT Bolt. All pass the validator and are registered with FishNet. The user approved the
  silhouettes.
- **Batch 2:** Grenade Launcher, SMG, Triple Machinegun, Double Shotgun, Assault Slugger, Heavy
  Crossbow, Bazooka, Minigun, Sawed-off Shotgun, Auto Shotgun, plus NT Heavy Bolt, NT Grenade and
  NT Rocket. All 15 weapons pass the validator, all 13 new prefabs are registered with FishNet, and
  no new console errors. Preview sheet: `Temp/nt-weapon-previews/Batch 2 previews.png`. The user
  moved on to Batch 3 after seeing it.
- **Batch 3:** Grenade Shotgun, Splinter Gun, Auto Crossbow, Super Crossbow, Splinter Pistol, Super
  Splinter Gun, Hyper Rifle, Heavy Revolver, Gatling Slugger, Super Slugger, Quadruple Machinegun,
  Double Minigun, plus NT Heavy Bullet, NT Splinter and NT Mini Grenade. All 27 weapons pass the
  validator, all 15 new prefabs are registered with FishNet, and no new console errors. Preview
  sheet: `Temp/nt-weapon-previews/Batch 3 previews.png`. Awaiting the user's review.
- **Scene ids (2026-10-21):** weapons placed in a scene several at once can miss their FishNet
  `SceneId` (it's rebuilt at most every 250 ms from `OnValidate`). Such an instance stays inactive in
  Play mode. Fix: Fish-Networking → Utility → Reserialize NetworkObjects → scenes, with the scenes saved.
- **Explosions (answered 2026-10-20):** `Projectile.Die` resolves the explosion on every death, so
  explosive projectiles explode both on contact and when their lifetime runs out. The user wants it
  that way, for the rocket too.
- **Wiki sprites:** the Weapons page has no plain Grenade Launcher sprite (the wiki has no page for
  it); the Golden Grenade Launcher sprite was used as its silhouette. Triple Machinegun's sprite is
  "Tri machinegun" and Sawed-off Shotgun's is "Sawedoffshotgun". Batch 3: "Splinter" (Splinter Gun),
  "Autocross", "Splinterpistol", "Supersplintergun", "Gattling Slugger", "QuadrupleMachinegun2".
- **Blockout details (2026-10-21):** the user lifted the 6-cube limit for Quadruple Machinegun (10
  cubes: stock, banded housing, sight, four barrels, front band) and Super Crossbow (11: two swept-back
  limbs, five separate bolts); `validate_weapons.cs` allows up to 12 for those two (`MAX_CUBES`).
  Hyper Rifle's fins use the new `Blockout Hyper` (blue) material, created by `batch3_builder.cs`
  and listed in `phase2_base_weapon.cs`. Heavy Revolver and Sawed-off Shotgun grips rake back (+20°)
  like their sprites; every other grip uses −12°, which leans the grip's bottom forward.
- **Revolver (2026-11-02):** the starting weapon, built by `revolver_builder.cs` (Handheld, NT Bullet,
  single, 0.20 s, 4° spread, 1 ammo; straight grip like its sprite "Pistol3"). It replaced the test
  Uzi as `Pawn Root`'s `startingPrimaryWeapon`; the test Shotgun stays the secondary. Passes the
  validator, is registered with FishNet, and a host play-test spawned both pawns holding it.

Since Phase 3:
- The user deleted `Pawn Root`'s `Tertiary Weapon` and re-enabled `Secondary Weapon`.
- The Uzi showed a wrong held pose, fixed by `Weapon.RecordAnimatorDefaults` (see Phase 3,
  "Animator").

Nothing is committed.

## Decisions (answered 2026-10-20)

1. **Damage scale:** none. Nuclear Throne damage numbers are used as-is.
2. **Play-testing:** no play-test swaps. Weapons reach the game through Phase 3's pickups instead
   (place them in a scene).
3. **Model origin:** each blockout is positioned so the top of its grip (where the trigger sits) is
   at the weapon prefab's local (0, 0, 0). The hold points in `Pawn Root` are where that spot goes.
4. **Pickups (Phase 3):** Nuclear Throne swap rule; starting weapons are spawned separately, not
   nested in `Pawn Root`; held weapons drop when their pawn dies.

## Ground rules for the session

- **Drive Unity through the Unity CLI** (see the memory note `unity-cli.md`). The Editor must be open
  on the project.
  - Move and copy assets with `AssetDatabase` (`MoveAsset`, `CopyAsset`) so GUIDs and references
    survive. Never use `mv` / `cp` in the shell.
  - Build prefabs with `script eval_file` and `PrefabUtility.LoadPrefabContents` /
    `SaveAsPrefabAsset` / `UnloadPrefabContents`.
  - `component set_field` doesn't save to disk by itself. Follow it with
    `AssetDatabase.SaveAssets()`.
  - After `unity recompile`, run `unity status --until-ready --timeout 90` before the next command.
- **Keep the generator scripts in `nt-weapon-builders/`** at the repo root. That folder is outside
  `Assets/`, so Unity doesn't compile it. Run them with
  `unity command script eval_file "$PWD/nt-weapon-builders/<file>.cs" --result-only`.
  - Each batch's builder is one C# file. Weapons and projectiles are described as data in it (see
    "Blockout specs"), so a weapon can be rebuilt by editing its entry and rerunning. All builders
    are safe to rerun: existing prefabs are overwritten in place, keeping their GUIDs.
  - For Batch 2, copy `batch1_builder.cs` to `batch2_builder.cs`. Keep its helpers (`Projectile`,
    `Weapon`, `Cube`, `V`) and replace the PROJECTILES and WEAPONS sections.
  - The other scripts:

    | Script | What it does | Edit per batch |
    |---|---|---|
    | `phase2_base_weapon.cs` | Blockout materials and `Base Weapon` | — |
    | `phase3_prefabs.cs` | Layer 11, world physics on the base and test weapons, `Pawn Root` wiring | — |
    | `validate_weapons.cs` | The end-of-batch validation | Add rows to `EXPECTED` |
    | `render_previews.cs` + `contact_sheet.cs` | Preview PNGs and one sheet, in `Temp/nt-weapon-previews/` | Set `NAMES` |
    | `pt_*.cs` | Play-test probes; see "Play-testing" | — |
- **Stop after each phase and each batch** for review. Don't commit unless asked.
- **Don't edit scenes or the UIManager prefab.** `Pawn Root` is only edited by Phase 3.
- **Projectile code is out of scope.** Projectile prefabs only set fields `Projectile` already has.
  Grenade bounce and fuse, rocket acceleration, bolt piercing, and close-range pellet damage are not
  supported yet.

## Facts the session needs

- **Layers:** a weapon's root is on layer 11 (`Weapon`, added in Phase 3) so pickups can be
  raycast without hitting pawns and free weapons don't block bullets. Everything under the root is
  on layer 7 (`Player`), like the Uzi.
- **Axes:** the Uzi's model faces **+Z** (barrel), with **+Y** up and the grip hanging down −Y. The
  root is at the hand: the top of the grip (decision 3), with the barrel line near y ≈ 0.
- **Uzi (handheld reference):** model bounds size (0.126, 0.538, 0.861), local z ≈ −0.16 … 0.70,
  y ≈ −0.48 … 0.06.
- **Shotgun (long reference):** model bounds size (0.204, 0.457, 2.735), local z ≈ −0.96 … 1.78
  around its root.
- **How `Weapon` uses its points:**
  - `muzzlePoint` is both the projectile spawn position and its aim direction, and the visual
    prediction point.
  - `spawnPoint` is serialized but not read by any code yet. Place it anyway, per the brief.
- **VFX:**
  - `muzzleFlash` is a `ParticleSystem` *in the prefab*. It's an instance of
    `Gun Muzzle Flash Effect.prefab` (named "Uzi Muzzle Flash Effect" inside the Uzi), with local
    rotation (270, 0, 0).
  - `smokeEffects` is the `Gun Smoke Effect.prefab` asset. It's instantiated at the muzzle on each
    shot.
  - Both prefabs live in the Uzi folder, which becomes `Assets/Weapons/Test Uzi/`.
- **FishNet registration:** weapons carry a `NetworkObject`. Projectiles are spawned network
  objects, so both must end up in `Assets/DefaultPrefabObjects.asset`. FishNet's default-prefab
  generator is on and normally adds new prefabs by itself. Check after each batch. If one is
  missing, run Fish-Networking's "refresh default prefabs" menu item (find its exact path with the
  CLI's menu listing).
- **The wiki blocks plain fetches** (HTTP 402). Use the built-in browser pane to view weapon pages,
  for example `https://nuclear-throne.fandom.com/wiki/Machinegun`. Zoom into the sprite with a
  screenshot. Don't download images without asking.

## Phase 0: code prerequisite

`Weapon.PlayVfx` and `PlayReloadAnimation` call `animator.SetTrigger` with no null check. The
blockout weapons have no Animator, so every shot would throw. Guard both calls with
`if (animator != null)`. Then compile and confirm the Uzi and Shotgun are unaffected.

## Phase 1: folders and moves

1. Create `Assets/Weapons`, `Assets/Weapons/Nuclear Throne`,
   `Assets/Weapons/Nuclear Throne/Materials` and `Assets/Projectiles` with
   `AssetDatabase.CreateFolder`.
2. Move `Assets/Models/shotgun` to `Assets/Weapons/Test Shotgun` and `Assets/Models/uzi` to
   `Assets/Weapons/Test Uzi`, using `AssetDatabase.MoveAsset`. That moves and renames in one step.
   Check the returned error string is empty.
3. Verify the moves:
   - `Pawn Root`'s weapon slots still resolve.
   - `component get_fields` on both test prefabs shows the same references as before.
   - The console has no missing-reference errors.
4. Update paths that mention the old folders: the memory note `unity-cli.md` ("Weapon prefabs:"
   line) and [nt-weapons-plan.md](nt-weapons-plan.md) if it names them.
5. Note for git: `Assets/Models/shotgun` was untracked and `Assets/Models/uzi` tracked, so git will
   show the Uzi as deleted and re-added.

## Phase 2: base weapon prefab and materials

**Blockout materials** in `Assets/Weapons/Nuclear Throne/Materials`, all URP `Lit`:
`Blockout Metal` (dark grey), `Blockout Light Metal` (mid grey), `Blockout Wood` (brown), and one
accent color per ammo type: `Blockout Bullet` (yellow), `Blockout Shell` (red), `Blockout Bolt`
(green), `Blockout Explosive` (orange). Each weapon uses metal plus at most one or two others.

**`Assets/Weapons/Base Weapon.prefab`:**

```
Base Weapon            [Weapon, NetworkObject, Rigidbody, BoxCollider 1×1×1]   layer 11, at origin
├── Weapon Model       (empty; each weapon's 3–6 cubes go here)
├── Muzzle Point       (empty)
├── Spawn Point        (empty)
└── Muzzle Flash       (instance of Gun Muzzle Flash Effect.prefab, local rotation 270,0,0)
```

**`Weapon` defaults on the base:**
- **Points and VFX:** `muzzlePoint` = Muzzle Point, `spawnPoint` = Spawn Point,
  `muzzleFlash` = the Muzzle Flash's ParticleSystem, `smokeEffects` = `Gun Smoke Effect.prefab`.
- **Ammo:** `clipSize 0`.
- **Left empty:** `animator`, `ragdoll` and `projectilePrefab`.

**Making each weapon:**
1. `AssetDatabase.CopyAsset` the base to `Assets/Weapons/Nuclear Throne/<Weapon Name>.prefab`.
   That's a copy, not a variant, so each weapon is independent with its own GUID, and FishNet
   assigns its own prefab id.
2. Open the copy with `LoadPrefabContents` and rename the root to the weapon name.
3. Build the cubes, then move them all by the same offset so the top-centre of the grip part is at
   (0, 0, 0) (decision 3). Then set the points, set the `Weapon` fields, save.

**Cubes:**
- Created with `GameObject.CreatePrimitive(PrimitiveType.Cube)`.
- **Remove the `BoxCollider`** (`DestroyImmediate`), so weapon blockouts never block projectiles or
  pawns.
- Set layer 7, assign a blockout material, and name each cube after the part it represents
  ("Barrel", "Stock", "Grip", "Magazine", "Limbs" …).

**Placing the points**, from the combined renderer bounds of `Weapon Model` in root-local space:
- **Muzzle Point:** 0.2 ahead of the barrel tip, at the barrel cube's centre (x, y) and z = barrel
  front face + 0.2. Weapons with several barrels (Triple/Quad Machinegun, Double Minigun, Double
  Shotgun, Super Crossbow, Super Slugger) use the centre of the barrel group.
- **Spawn Point:** the rear end, on the same barrel line, at z = model bounds min z.
- **Muzzle Flash:** at the barrel tip, on the barrel line, at z = barrel front face + 0.1.

## Phase 3: world weapons and pickup

A `Weapon` is also a world object: it lies in the scene until a pawn picks it up. Everything is
client-authoritative: the server trusts the picking client and doesn't check range or line of sight,
and free weapons' physics isn't networked, so every peer simulates its own copy.

**Free vs held.** A weapon with no network owner is *free*: its `Rigidbody` simulates (default
settings, gravity on) and its 1×1×1 `BoxCollider` is enabled. A weapon with an owner is *held*:
kinematic, collider off, parented to a hold point on its pawn. A held weapon stays kinematic even
when its pawn has no owner (e.g. a pawn placed in a scene). This also holds for weapons placed in a
scene for debugging: scene weapons have no owner, so they fall and can be picked up.

**Animator.** A weapon's `Animator` runs only while it's held. An Animator writes every property any
of its clips animates, on every frame and in every state (even one with no clip). Before a clip has
run, it writes the default values it recorded when its GameObject activated. A weapon activates
before it's parented, so for a clip that animates the weapon's root (the Uzi's Reload clip does),
those defaults are a world pose, and the held weapon sits in the wrong spot until that clip has
played once. Neither `Rebind()`, re-enabling the Animator, nor swapping its controller records the
defaults again; reactivating the GameObject does. So after parenting a weapon to a new holder,
`Weapon.RecordAnimatorDefaults` enables the Animator and switches the GameObject off and on.
FishNet's `NetworkObject.OnDisable` only acts while deinitializing, so this is safe on a spawned
weapon. Blockout weapons have no Animator.

**Layers.** Add layer 11 `Weapon` and put weapon roots on it. In the collision matrix layer 11
collides with `Default` only, and the box collider also excludes every other layer. Free weapons rest
on level geometry, pawns walk through them, projectiles (which hit Default, Player and Bullet) fly
through them, and weapons pass through each other, so a weapon dropped where another lay is never
pushed out of it.

**Who holds what** is synced on the weapon, as one server-written `SyncVar` of
`{ holder pawn ObjectId, slot (None / Primary / Secondary) }`. It's an id, not a `Pawn` reference,
because a reference that arrives before its object spawns reads as null (FishNet's
`ReadNetworkObject`). Each peer applies a change by parenting the weapon to that pawn's hold point
for the slot. If the pawn hasn't spawned yet, the pawn applies it when it starts. `Pawn` keeps
local, unsynced `primary` / `secondary` fields that follow these changes.

**Picking up** (owner client, `Interact` action, bound to G):
1. Raycast from the camera along its forward for `pickupRange` (default 3) against `Default` and
   `Weapon`, so walls block it. If the hit collider's rigidbody has a free `Weapon`, send
   `Pawn.ServerPickUp(weapon)`.
2. The server ignores the request if the weapon isn't free any more (the first claim wins).
   Otherwise it applies the Nuclear Throne rule: an empty primary slot first, then an empty
   secondary slot. If both are full, the new weapon takes the primary slot, then the old primary
   drops at the position and rotation the new weapon had.
3. Holding: `GiveOwnership(pawn.Owner)`, then set the weapon's holder to that pawn and slot.
   Dropping: `RemoveOwnership()`, clear the holder, and an observers RPC (run locally) puts the
   weapon at the drop pose. Late joiners get free weapons' positions from their spawn message.

**Starting weapons.** `Pawn` gets `startingPrimaryWeapon` / `startingSecondaryWeapon` prefab fields
(the test Uzi and test Shotgun on `Pawn Root`). In `OnStartServer` the server instantiates each one
at its hold point, spawns it owned by the pawn's owner, and holds it in its slot. The nested Uzi and
Shotgun instances are removed from `Pawn Root`. The `Primary Weapon` / `Secondary Weapon` transforms
under the camera become the hold points, mirrored at (±0.46, −0.187, 0.75). (The user later
deleted `Tertiary Weapon`, a manual play-test slot.)

**Death.** In `OnDespawnServer` the pawn drops each held weapon where it is. Every peer also unparents
the pawn's weapons in `OnStopNetwork`, so a pooled pawn never takes a weapon with it.

**Empty slots.** `Pawn` skips an empty slot when ticking triggers and reloading.

**Prefabs.** `Base Weapon`, the test Uzi and the test Shotgun get the `Rigidbody` and `BoxCollider`
and move their root to layer 11. Batch 1 is rebuilt from the updated base with grip-origin models
(decision 3).

**Done when** (met on 2026-10-20): it compiles, `Pawn Root` holds no nested weapons, the prefabs pass the checks, and a
play session on the host shows: the pawn spawns holding the Uzi and Shotgun; a weapon placed in the
scene falls and lands; looking at it and pressing G swaps it into the primary slot and drops the Uzi
where it lay; and dying drops both weapons.

## Blockout specs

Each weapon is one entry in the batch's builder file:
`name`, `size class`, a list of 3–6 cubes `{part, material, localPosition, localEulerAngles,
localScale}`, the `barrel` part name (for the points), the `grip` part name (for the origin, "Grip"
unless a weapon names another part), the `projectile`, and the `Weapon` field values from the configs
table.

Before writing a weapon's cubes, look at its wiki sprite and match the silhouette: barrel length
against body, where the grip and magazine sit, stock or no stock, and any distinctive part.

**Size classes** (overall length along Z, width X, height Y):

| Class | Length | Width | Height |
|---|---|---|---|
| Handheld | ≈ 0.85 | ≈ 0.13 | ≈ 0.5 |
| Compact | ≈ 1.5 | ≈ 0.18 | ≈ 0.45 |
| Long | ≈ 2.7 | ≈ 0.2 | ≈ 0.46 |
| Heavy | 2.7–3.0 | 0.3–0.45 | 0.5–0.6 |

Where the model sits along Z follows from where its grip is (decision 3).

Crossbow limbs are the exception to the width limits. They span X like the sprite shows, about 1.0
for a crossbow and 1.4 for the Super Crossbow.

**Per weapon.** The silhouette hints are from memory of the sprites; the wiki image wins wherever
they disagree.

| # | Weapon | Class | Projectile | Silhouette hints |
|---|---|---|---|---|
| 1 | Machinegun | Long | NT Bullet | receiver box, long barrel, stock, grip |
| 2 | Assault Rifle | Long | NT Bullet | rifle body, curved/angled magazine, stock |
| 3 | Shotgun | Long | NT Pellet | barrel + pump under it, wooden stock |
| 4 | Slugger | Long | NT Slug | shotgun with a thick barrel |
| 5 | Crossbow | Long | NT Bolt | stock, horizontal limbs at front, bolt on top |
| 6 | Grenade Launcher | Compact | NT Grenade | fat short barrel, drum/body, stock |
| 7 | SMG | Handheld | NT Bullet | small body, magazine in grip |
| 8 | Triple Machinegun | Heavy | NT Bullet | three barrels stacked/fanned |
| 9 | Double Shotgun | Long | NT Pellet | two barrels stacked, stock |
| 10 | Assault Slugger | Long | NT Slug | slugger with magazine |
| 11 | Heavy Crossbow | Heavy | NT Heavy Bolt | bigger crossbow, thick limbs |
| 12 | Bazooka | Heavy | NT Rocket | long tube, grip below, sight |
| 13 | Minigun | Heavy | NT Bullet | barrel cluster, carry handle |
| 14 | Sawed-off Shotgun | Compact | NT Pellet | two short barrels, no stock |
| 15 | Auto Shotgun | Long | NT Pellet | shotgun body with drum magazine |
| 16 | Grenade Shotgun | Compact | NT Mini Grenade | wide barrel, short body |
| 17 | Splinter Gun | Long | NT Splinter | boxy body, short spikes at front |
| 18 | Auto Crossbow | Long | NT Bolt | crossbow with magazine on top |
| 19 | Super Crossbow | Heavy | NT Bolt | wide limbs, several bolts across |
| 20 | Splinter Pistol | Handheld | NT Splinter | pistol body, spikes at front |
| 21 | Super Splinter Gun | Heavy | NT Splinter | large splinter gun |
| 22 | Hyper Rifle | Long | NT Bullet | sleek rifle |
| 23 | Heavy Revolver | Handheld | NT Heavy Bullet | cylinder, long barrel, grip |
| 24 | Gatling Slugger | Heavy | NT Slug | gatling barrel cluster, thick |
| 25 | Super Slugger | Heavy | NT Slug | several wide barrels across |
| 26 | Quadruple Machinegun | Heavy | NT Bullet | four barrels |
| 27 | Double Minigun | Heavy | NT Bullet | two barrel clusters side by side |

**`Weapon` fields** come from the configs table in nt-weapons-plan.md: `ammoType`, `fireMode`,
`cooldown`, `burstCount`, `projectilesPerShot`, `spreadAngle`, `fixedSpreadAngle`, `ammoPerShot`,
and `clipSize 0`. Super Splinter Gun also gets `burstInterval 0.017`. Every other weapon keeps the
default.

## Projectile prefabs

Each one is an `AssetDatabase.CopyAsset` of an existing projectile into `Assets/Projectiles/`,
renamed and with fields changed through `SerializedObject` or `set_field` plus `SaveAssets`.
Only fields `Projectile` already has are touched: `speed`, `damage`, `lifetime`, the root scale or
`Visuals` scale, and `explosion`. Damage uses the Nuclear Throne values as-is (decision 1). Speed and lifetime are starting
points for tuning: the existing bullet is speed 90 and lifetime 0.25, a range of about 22 units.

| Prefab | Copied from | damage | speed | lifetime | Notes |
|---|---|---|---|---|---|
| NT Bullet | Bullet Prefab | 3 | 90 | 0.25 | same as the existing bullet |
| NT Heavy Bullet | Bullet Prefab | 7 | 90 | 0.25 | Visuals × 1.5 |
| NT Pellet | Bullet Prefab | 2 | 70 | 0.15 | Visuals × 0.7, short range |
| NT Slug | Bullet Prefab | 22 | 80 | 0.3 | Visuals × 1.6 |
| NT Bolt | Bullet Prefab | 20 | 140 | 0.4 | thin, long visual (scale Z) |
| NT Heavy Bolt | Bullet Prefab | 50 | 110 | 0.5 | thicker bolt visual |
| NT Splinter | Bullet Prefab | 4 | 120 | 0.35 | thin, short visual |
| NT Grenade | Exploding Bullet Prefab | 15 | 35 | 0.6 | explosion radius 4, damage 5 |
| NT Mini Grenade | Exploding Bullet Prefab | 3 | 40 | 0.4 | explosion radius 2.5, damage 5 |
| NT Rocket | Exploding Bullet Prefab | 20 | 45 | 0.8 | explosion radius 4.5, damage 5 |

Before making the explosive ones, read `Projectile.cs` to see whether an explosion also happens when
the lifetime runs out or only on contact, and tell the user which.

## Batches

Each batch makes the projectiles it needs first, then its weapons. Stop at the end of each batch.

| Batch | Weapons | New projectiles |
|---|---|---|
| 1 (5) | Machinegun, Assault Rifle, Shotgun, Slugger, Crossbow | NT Bullet, NT Pellet, NT Slug, NT Bolt |
| 2 (10) | Grenade Launcher, SMG, Triple Machinegun, Double Shotgun, Assault Slugger, Heavy Crossbow, Bazooka, Minigun, Sawed-off Shotgun, Auto Shotgun | NT Grenade, NT Rocket, NT Heavy Bolt |
| 3 (12) | Grenade Shotgun, Splinter Gun, Auto Crossbow, Super Crossbow, Splinter Pistol, Super Splinter Gun, Hyper Rifle, Heavy Revolver, Gatling Slugger, Super Slugger, Quadruple Machinegun, Double Minigun | NT Mini Grenade, NT Splinter, NT Heavy Bullet |

## Checks at the end of every batch

1. **Validation script** (one `eval_file`), run over every prefab in the batch. Each should report:
   - The root has `Weapon`, `NetworkObject`, a `Rigidbody` and a 1×1×1 `BoxCollider`, and is on
     layer 11. Everything under it is on layer 7 with no colliders.
   - The top-centre of the grip part is at (0, 0, 0).
   - `Weapon Model` has 3–6 cube children.
   - Model bounds fit the weapon's size class within about 25% (crossbow width excepted).
   - Muzzle Point is 0.2 (±0.02) ahead of the barrel's front face. Spawn Point is at the model's
     rear.
   - `muzzlePoint`, `spawnPoint`, `muzzleFlash`, `smokeEffects` and `projectilePrefab` are set.
     `animator` and `ragdoll` are empty.
   - Every `Weapon` stat matches the configs table.
2. **Registration:** every new weapon and projectile prefab appears in `DefaultPrefabObjects.asset`.
3. **Compile and console:** 0 errors.
4. **Previews:** render each weapon from the side (looking down −X) and at a three-quarter angle with
   `render_previews.cs` (it uses `PreviewRenderUtility`, so no scene is touched), then combine them
   with `contact_sheet.cs`. Send the sheet to the user, with the wiki sprites shown at 8× in the
   browser pane for comparison.
5. **Report** what was made and anything that didn't pass. Offer a play test of a new weapon (see
   "Play-testing").

## Play-testing

Weapons reach the game through pickups (decision 2). Don't edit `Pawn Root` or scenes for this.
The memory note `unity-cli.md` has the full recipe. In short:
1. Enter Play mode. The Editor runs Boot + Debug + Online together, and Boot's Dev bootstrap starts
   a host.
2. Run `pt_ready.cs`, which readies up the local player. The match starts 3 s later and the pawn
   spawns.
3. Run `pt_spawn.cs`, which spawns a free weapon (it names the prefab, the Machinegun by default)
   ahead of the pawn.
4. Run `pt_pickup.cs`, which aims the camera at it and calls the pawn's own pickup.
5. Check the result with `pt_slots.cs`.
6. Exit Play mode, and don't save scenes.

Each eval runs within one frame, so poll across several evals to watch physics settle.
