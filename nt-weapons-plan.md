# Nuclear Throne weapons plan: weapon features

## Intent

Add the firing features `Weapon` needs so that every weapon in the list below can be built as a
prefab later, by setting serialized fields and no new code:

- **Firing mode:** single shot, automatic, burst, or burst/automatic.
- **Burst:** one trigger pull fires N shots, spaced out over time.
- **Fixed spread:** a shot's projectiles fan out evenly across a set arc, and each one also gets the
  existing random spread.
- **Ammo per shot:** how much ammo one trigger pull uses (a whole burst counts as one pull).
- **Clip size 0:** the weapon never reloads. Each trigger pull spends `ammoPerShot` from the
  player's ammo of the weapon's `ammoType` instead of from a clip.
- **Player ammo counts:** per-type ammo (bullet, shell, bolt, explosive, energy) on `NetworkPlayer`,
  client-authoritative.
- **Reloads draw from the player's ammo:** a weapon with a clip only reloads if the player has a full
  `clipSize` of its `ammoType`, and that much is taken when the reload finishes.
- **HUD ammo counts:** `MainView` shows every ammo count at the bottom right, updated live.

Out of scope: projectile changes (heavy bullets, slugs, splinters, bolts that pierce, grenades,
rockets), ammo pickups, recoil and screen shake, and the weapon prefabs
themselves.

## Ground rules for the session

- **Code steps don't touch prefabs by hand-editing YAML.** New serialized fields must have code
  defaults that keep how `Uzi Prefab` and `Shotgun Prefab` behave today. Prefab edits (Step 5's
  HUD layout, Step 7's cooldown values, and the weapon prefabs later) go through the Unity CLI with the editor open.
- **Stop after each step** for review. Don't commit unless asked.
- Match the surrounding style: section-divider comments, guard clauses, tooltips on stats.
- After each step, check that it compiles (`unity command console status`).

## Where things stand

- `Assets/Scripts/Pawn/Weapon/Weapon.cs`: owner-only logic. `TryFire` is gated by `fireRate`
  (shots/sec) and `_currentAmmo`; `Fire` launches `projectilesPerShot` projectiles, each with random
  cone spread from `CalculateSpread` (`spreadAngle` is the full cone width, ±half).
- `Assets/Scripts/Pawn/Pawn.cs:176-187`: calls `TryFire` every frame while `Attack` /
  `SecondaryAttack` `IsPressed()`, so every weapon is automatic today. The weapon can't tell a new
  click from a held button.
- No ammo inventory exists. `MainView` has ammo labels for the five NT ammo types, but nothing feeds
  them.

## Design

### New / changed fields on `Weapon`

```csharp
public enum FireMode { SingleShot, Automatic, Burst, BurstAutomatic }

[Header("Weapon Stats")]
public FireMode fireMode = FireMode.Automatic;      // default keeps today's hold-to-fire behavior
[Tooltip("Seconds between trigger pulls. For bursts, measured from the burst's first shot.")]
public float cooldown = 0.2f;                       // replaces fireRate (see Step 7)
[Tooltip("0 = no clip: never reloads; ammo comes from the player's ammo of ammoType.")]
public int clipSize = 30;
[Tooltip("Ammo used per trigger pull. A whole burst counts as one pull.")]
public int ammoPerShot = 1;
[Tooltip("Which of the player's ammo counts a clipless weapon spends.")]
public AmmoType ammoType = AmmoType.Bullet;

[Header("Spread")]
[Tooltip("Random spread: full cone width in degrees, applied to every projectile.")]
public float spreadAngle = 5f;
[Tooltip("Fixed spread: total arc in degrees across which projectiles are spaced evenly. 0 = all on the aim line.")]
public float fixedSpreadAngle = 0f;
public int projectilesPerShot = 1;

[Header("Burst")]
public int burstCount = 1;
[Tooltip("Seconds between shots within a burst.")]
public float burstInterval = 0.067f;                // 2 frames at NT's 30 fps
```

### Words used below

- **Trigger pull:** the player starts a fire action (a click, or a refire while the button is held in
  automatic modes).
- **Shot:** one volley. Launches `projectilesPerShot` projectiles.
- **Burst:** `burstCount` shots, `burstInterval` apart, started by one trigger pull.

`ammoPerShot` is charged once per trigger pull, which is how the wiki counts "Ammo per shot" (per
click). The wiki value goes straight in: Assault Rifle 3, Triple Machinegun 3, Super Splinter Gun 2
for a 12-shot burst. Charging per burst shot couldn't express that last one.

### Input contract change

`Weapon` needs to know whether the button was pressed this frame. `Pawn` will call the weapon every
frame with both values instead of only while held:

```csharp
// Pawn.Update
primaryWeapon.TickTrigger(this, Attack.IsPressed(), Attack.WasPressedThisFrame());
secondaryWeapon.TickTrigger(this, SecondaryAttack.IsPressed(), SecondaryAttack.WasPressedThisFrame());
```

`TickTrigger` replaces the public `TryFire`. Calling it every frame, held or not, also gives the
weapon a place to advance a burst that's in progress after the button is released, so the weapon
doesn't need its own `Update`. Pawn's pause/reload `else if` chain needs to keep calling it, with
`held = false`, so a burst can finish while reloading is pressed or the game is paused. Decide in
review whether pausing should freeze a burst instead.

### Firing logic

```
TickTrigger(pawn, held, pressedThisFrame):
    if !IsOwner: return
    if bursting: advance burst (fire any shots now due); return
    wantsPull = fireMode is Automatic/BurstAutomatic ? held : pressedThisFrame
    if !wantsPull or Time.time < _nextPullAt or _isReloading: return
    if HasClip and _currentAmmo < ammoPerShot: TryReload; return
    if !TrySpendAmmo(pawn): return      // clip, or the player's ammo of ammoType when clipless
    start burst (shotsLeft = burstCount), fire first shot now
    _nextPullAt = Time.time + cooldown
```

- Each shot gets a fresh `TimeManager.GetPreciseTick(TickType.Tick)` so later burst shots carry their
  own spawn tick, not the click's.
- Ammo is paid up front, so a burst always finishes once started. Reload is blocked while bursting.
- `burstCount` only applies in the `Burst` / `BurstAutomatic` modes; the other modes fire one shot
  per pull whatever it is set to.
- If more than one burst shot is due in a frame (`burstInterval` shorter than a frame, e.g. Super
  Splinter Gun), they all fire that frame, so a burst lasts the same time at any frame rate.
- `SingleShot` vs `Burst` differ only in `burstCount`. The same goes for `Automatic` vs
  `BurstAutomatic`. The enum could shrink to a `bool automatic`. I kept four values so the inspector
  reads like the wiki, but either works.
- Clicks during cooldown are dropped (no input buffer). NT also drops them.

### Fixed spread

For projectile `i` of `n`, yaw offset = `fixedSpreadAngle * (i / (n - 1) - 0.5)` when `n > 1`,
else 0. Random spread is added on top, per projectile. The fan is horizontal (yaw) only. Random
spread stays a cone in pitch and yaw as it is today.

`CalculateSpread()` becomes `CalculateSpread(int index)`; `Fire` passes `i`.

### Clip size 0

`bool HasClip => clipSize > 0;`. When false, `TryReload` returns at once and each trigger pull
spends `ammoPerShot` from the controlling player's ammo of `ammoType` (see below). `RefillAmmo` is
only reached through the reload animation, which never plays.

### Player ammo counts

- `AmmoType` enum: `Bullet, Shell, Bolt, Explosive, Energy` (the five NT types, matching
  `MainView`'s ammo labels).
- Counts live in `NetworkPlayer`, one per type. They are **client-authoritative and not synced**:
  plain fields that only the player's owner reads and writes (bots are owned by the host, so the
  host keeps their counts). The server never sees them; pickups or a HUD can later go through the
  owner (e.g. a `TargetRpc` to add ammo).
- Per-type `max` and `starting` amounts are serialized on `NetworkPlayer` (defaults: NT's 255 bullets,
  55 of every other type; starting = max until pickups exist).
- API: `GetAmmo(type)`, `GetMaxAmmo(type)`, `TryConsumeAmmo(type, amount)` (all or nothing),
  `AddAmmo(type, amount)` (clamped to max), `ResetAmmo()`.
- The owner fills the counts to `starting` in `OnStartClient` and again each time it gets a new pawn
  (`ControlledPawn` changes to non-null), so a respawn starts with fresh ammo.
- `Weapon.TrySpendAmmo(pawn)` is the one place firing spends ammo: from the clip when `HasClip`, else
  `pawn.ControllingPlayer.Value.TryConsumeAmmo(ammoType, ammoPerShot)`. A pawn with no controlling
  player can't fire a clipless weapon.

### Reloading from the player's ammo

- `TryReload` only starts a reload if the controlling player has at least `clipSize` of `ammoType`.
  It remembers that player for the reload.
- `RefillAmmo` (the reload animation's event, owner only) takes `clipSize` from the player and fills
  the clip. If the player no longer has enough by then (another weapon spent the same type), the
  reload ends with the clip still empty.
- The clip is emptied when a reload starts, as today, so rounds left in it are lost. The first clip
  of a life is free.

### HUD ammo counts

- `NetworkUIEvents.OnLocalAmmoChanged(AmmoType, int)` fires whenever the local player's count of a
  type changes, including `ResetAmmo` (every type), and once for every type when the player becomes
  `LocalInstance`. Bots never fire it.
- `MainView` subscribes in `Show`, unsubscribes in `Hide`, and on `Show` reads every count from
  `NetworkPlayer.LocalInstance` so the texts are right even if it missed events while hidden.
- Text format: `Bullet: 255`.
- UIManager prefab: the existing `Ammo Texts` group under `Main View` (inactive, nested wrappers,
  already wired to `MainView`'s five text fields) becomes active and is anchored bottom right with
  a 40 px margin. The five texts become its direct children, stacked by a `VerticalLayoutGroup`, and
  right-aligned. The empty wrappers are deleted; the texts keep their identities, so the wiring stays.

## Steps

1. **Clip size 0.** Add `HasClip` and guard `TryFire`, `TryReload`, `Fire` and `OnStartClient`. No
   behavior change for clipSize > 0.
2. **Ammo per shot.** Add `ammoPerShot` (default 1). Gate firing on `_currentAmmo >= ammoPerShot` and
   subtract it once per trigger pull. The `_currentAmmo == 0` check becomes `< ammoPerShot`. Until
   Step 9 a pull is a single shot, so this behaves the same as per shot.
3. **Player ammo counts.** Add `AmmoType`, the ammo counts and API on `NetworkPlayer`, `ammoType`
   on `Weapon`, and `Weapon.TrySpendAmmo` so clipless weapons spend the player's ammo.
4. **Reloads draw from the player's ammo.** Gate `TryReload` on the player having `clipSize` of
   `ammoType`; take it in `RefillAmmo`.
5. **HUD ammo counts.** Add `OnLocalAmmoChanged`, raise it from `NetworkPlayer`, show the counts in
   `MainView`, and lay out the texts in the UIManager prefab via the Unity CLI.
6. **Fixed spread.** Add `fixedSpreadAngle` (default 0) and index-based `CalculateSpread`.
7. **Cooldown in seconds.** Replace `fireRate` with `cooldown`. The wiki lists reload time in
   seconds, so 28 weapons can be copied straight in. Use `_nextPullAt` scheduling instead of
   `_lastFiredAt`. Uzi and Shotgun prefabs need their value entered by hand after this step
   (`1 / old fireRate`), via the Unity CLI.
8. **Fire modes + input contract.** Add `FireMode` and `TickTrigger`, and update `Pawn.Update`.
   Default `Automatic` keeps current behavior.
9. **Burst.** Add `burstCount` / `burstInterval` and the burst state machine inside `TickTrigger`.

Step 6 is independent of the others. Steps 8 and 9 build on step 7's timing.

## Weapon configs (for the prefab phase)

From the wiki. Cooldown in seconds, spreads in degrees, both spreads are full widths. "Ammo" is
`ammoPerShot` (per trigger pull). All use `clipSize 0`.

| Weapon | Type | Mode | Cooldown | Burst | Proj. | Random | Fixed | Ammo |
|---|---|---|---|---|---|---|---|---|
| Machinegun | Bullet | auto | 0.17 | 1 | 1 | 6 | 0 | 1 |
| Assault Rifle | Bullet | burst | 0.37 | 3 | 1 | 2 | 0 | 3 |
| Shotgun | Shell | single | 0.57 | 1 | 7 | 20 | 0 | 1 |
| Slugger | Shell | single | 0.73 | 1 | 1 | 5 | 0 | 1 |
| Crossbow | Bolt | single | 0.87 | 1 | 1 | 0 | 0 | 1 |
| Grenade Launcher | Explosive | single | 0.67 | 1 | 1 | 3 | 0 | 1 |
| SMG | Bullet | auto | 0.10 | 1 | 1 | 16 | 0 | 1 |
| Triple Machinegun | Bullet | auto | 0.13 | 1 | 3 | 3 | 15 | 3 |
| Double Shotgun | Shell | single | 0.93 | 1 | 14 | 30 | 0 | 2 |
| Assault Slugger | Shell | burst | 1.17 | 3 | 1 | 4 | 0 | 3 |
| Heavy Crossbow | Bolt | single | 1.33 | 1 | 1 | 0 | 0 | 2 |
| Bazooka | Explosive | single | 1.00 | 1 | 1 | 2 | 0 | 1 |
| Minigun | Bullet | auto | 0.03 | 1 | 1 | 13 | 0 | 1 |
| Sawed-off Shotgun | Shell | single | 0.93 | 1 | 18 | 45 | 0 | 2 |
| Auto Shotgun | Shell | auto | 0.13 | 1 | 6 | 15 | 0 | 1 |
| Grenade Shotgun | Explosive | single | 0.53 | 1 | 4 | 17 | 0 | 1 |
| Splinter Gun | Bolt | single | 0.63 | 1 | 5 | 10 | 0 | 1 |
| Auto Crossbow | Bolt | auto | 0.27 | 1 | 1 | 5 | 0 | 1 |
| Super Crossbow | Bolt | single | 1.00 | 1 | 5 | 0 | 5 | 5 |
| Splinter Pistol | Bolt | single | 0.27 | 1 | 4 | 4 | 0 | 1 |
| Super Splinter Gun | Bolt | burst | 0.93 | 12 | 1 | 16 | 0 | 2 |
| Hyper Rifle | Bullet | burst | 0.10 | 5 | 1 | 2 | 0 | 5 |
| Heavy Revolver | Bullet | single | 0.17 | 1 | 1 | 1 | 0 | 2 |
| Gatling Slugger | Shell | auto | 0.23 | 1 | 1 | 6 | 0 | 1 |
| Super Slugger | Shell | single | 1.07 | 1 | 5 | 4 | 10 | 5 |
| Quadruple Machinegun | Bullet | auto | 0.13 | 1 | 4 | 3 | 12 | 4 |
| Double Minigun | Bullet | auto | 0.03 | 1 | 2 | 9.5 | 9.5 | 2 |
| Revolver | Bullet | single | 0.20 | 1 | 1 | 4 | 0 | 1 |

Super Splinter Gun is a short burst: give it a smaller `burstInterval` than the default (tune in
play, e.g. 0.017 s). Grenade Shotgun was listed twice in the request; it's one weapon.

## Decisions (answered 2026-10-20)

1. **Fixed spread** is the total arc, with projectiles evenly spaced edge to edge. The wiki's "Fixed"
   values are used as-is as that total arc (Triple Machinegun 15°). The 12° in the original request
   was a mistake.
2. **Random spread:** the wiki's value is the full cone width, same as `spreadAngle`. No conversion.
3. **Super Splinter Gun** is one short 12-splinter burst costing 2 ammo. This is why `ammoPerShot`
   is charged per trigger pull, not per burst shot.
4. **Rocket Launcher** means the Bazooka.
5. **Grenade Shotgun** appearing twice was a typo.
6. **Minigun frame-rate cap:** accepted, no catch-up firing.
7. **Clipless ammo** comes from the player's total ammo counts, which are client-authoritative and
   live in `NetworkPlayer`. Added as Step 3.
8. **Reloads** cost a full `clipSize` from the player's ammo and need that much to start. Added as
   Step 4.
9. **HUD:** every ammo count on `MainView`, bottom right with a margin, live. Added as Step 5.

## Open questions

1. **Hyper Rifle overlap:** in NT, fast clicking can start a new burst before the last one ends. This
   plan blocks firing during a burst, so the effective rate is set by burst length (5 × 0.067 s).
   Allowing overlapping bursts is more state; I'd skip it unless it feels wrong in play.
2. **Ammo on respawn:** Step 3 refills to `starting` on every new pawn, since there are no pickups
   yet. Drop that if ammo should persist across lives.
