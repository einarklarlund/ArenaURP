# Projectile Refactor — Editor Wiring Checklist (Phase 4) + Verification + Phase 5 deletion

The code and the `ProjectileType` data assets are DONE and compile clean; all EditMode tests pass
(27 projectile pure-function tests). The legacy projectile system is **fully intact** — nothing below
deletes anything until you've verified the new path in play mode.

> **Architecture note:** the system follows *functional core / imperative shell* (see the
> `functional-core-imperative-shell` skill). There is **no `IProjectileWorld`, no `FakeWorld`, no
> `EffectGate`, and no `[Replicate]`/`[Reconcile]` prediction loop**. Motion is **deterministic
> closed-form** from the spawn tick + trajectory segment, so every peer computes position locally with
> nothing synced per tick. Collisions are **server-authoritative**: only the server ticks the
> cast/overlap, runs the pure core, performs the returned effects directly, and publishes the rare
> authoritative changes (health, a ricochet's new segment, death) via SyncVars + despawn. The pure core
> (`Arena.Projectile`) takes query results in as data and returns `ProjectileEffect`s out; `PawnProjectile`
> (the shell) does all the I/O; `ProjectileWorldProvider` is a concrete scene service it calls.
> Homing is intentionally not implemented.
>
> Tradeoff to expect when verifying: client hit feedback (despawn/VFX) lands ~½-RTT after the server
> resolves it, and a ricochet briefly mispredicts on clients until the segment SyncVar arrives. Normal
> server-authoritative behavior.

New code (all non-destructive additions):
- `Assets/Scripts/Projectile/Core/` — `Arena.Projectile.Core` asmdef (pure functions, tested).
- `Assets/Scripts/Projectile/PawnProjectile.cs` (shell) + `ProjectileWorldProvider.cs` (concrete scene service), Assembly-CSharp.
- `Assets/Data/Projectiles/{Bullet,Bolt,Pellet,DevilDaggers,Grenade}.asset` (`ProjectileType` flyweights).

Layers in this project (relevant): `Player`=7, `Bullet`=9, `Bullet-To-Bullet`=11, `Default`=0.

---

## Step 1 — Build a new bullet prefab (easiest: duplicate an existing one)

1. Duplicate `Assets/Prefabs/Projectiles/Bullet.prefab` → `Bullet (PawnProjectile).prefab`.
2. On the root, REMOVE the legacy components: `ProjectileMovement`, `ProjectileHitbox` (and Laser/`ProjectileBulletImpact`/`ProjectileSurfaceImpact`/`ProjectileDamageableImpact`), `ProjectileLifecycle`, `ProjectileState`, `ProjectileData`, `ProjectileSpawnOnDeath`, the dynamic `Rigidbody`, and any `NetworkCollision`/`NetworkTrigger`. KEEP the `NetworkObject` and the Visuals child.
3. ADD component `PawnProjectile`. Set its **Type** field to `Assets/Data/Projectiles/Bullet.asset`.
4. Collider for the projectile channel (so other bullets can FIND this one):
   - Add a `Rigidbody`: **Is Kinematic = true**, Use Gravity = false (kinematic so moving it doesn't thrash the static tree).
   - Add a `SphereCollider`: **Is Trigger = true**, Radius ≈ the type's `Radius` (0.2 for Bullet).
   - Set the **GameObject layer = `Bullet-To-Bullet`** (this is the layer `OverlapProjectiles` queries).
5. The root needs NO collider for the world channel — world hits are done by `SphereCast` in code.
6. **No `NetworkTransform`** — position is computed deterministically on every peer from the spawn tick,
   so you must NOT add a NetworkTransform (it would fight the computed transform). Remove one if the
   duplicated prefab had it.
7. `NetworkObject`: a standard NetworkObject is fine (there's no prediction loop). For **predicted
   spawning** (client-initiated, server-validated — the intended model for snappy fire feel), add
   FishNet's `PredictedSpawn` component and enable predicted spawning on the `NetworkObject`. For a first
   server-authoritative smoke test it isn't strictly required, but it's the intended setup.

Repeat (or make prefab variants) for Bolt/Pellet/DevilDaggers/Grenade later; one is enough to verify.

---

## Step 2 — Put a `ProjectileWorldProvider` in the scene

In the scene you test (e.g. `Debug.unity` or `Online.unity`), create an empty GameObject
`ProjectileWorldProvider` and add the `ProjectileWorldProvider` component:
- **World Mask**: include `Default` + `Player` (+ any other stage geometry layers you use). This is
  what bullets collide with for damage/surface.
- **Projectile Mask**: `Bullet-To-Bullet` only.
- **Player Manager**: drag your scene's `NetworkPlayerManager` (or leave empty — it falls back to
  `FindAnyObjectByType` on first use).
- **Catalog**: add one entry per `ProjectileType` you use: `Type` = the asset, `Prefab` = the matching
  new bullet prefab, `Impact Vfx` = optional cosmetic prefab. The catalog is used for child spawns
  (`SpawnProjectiles`) and impact VFX; a type you only fire directly still benefits from being listed.

> The provider is a soft singleton (`Instance` set in `OnEnable`). One per active scene.

---

## Step 3 — Fire the new prefab (minimal, non-destructive change to PawnWeapon.ExecuteShot)

In `Assets/Scripts/Pawn/Weapon/PawnWeapon.cs`, inside `ExecuteShot`'s loop, right AFTER
`GetPooledInstantiated(...)` returns `nob`, add a branch BEFORE the legacy `ProjectileData`/`ProjectileState`
block:

```csharp
if (nob.TryGetComponent<PawnProjectile>(out var pp))
{
    var fireDir = WeaponHelper.CalculateSpread(currentWeapon, firePoint, 0, currentWeapon.ProjectilesPerShot);
    pp.SpawnOwnerId  = pawn.ControllingPlayer.Value.ObjectId;
    pp.SpawnTick     = InstanceFinder.TimeManager.GetPreciseTick(TickType.Tick); // deterministic age basis
    pp.SpawnPosition = firePoint.TransformPoint(currentWeapon.SpawnPositionOffset);
    pp.SpawnForward  = fireDir;
    pp.SpawnHealth   = 0; // 0 -> falls back to ProjectileType.MaxHealth
    InstanceFinder.ServerManager.Spawn(nob, Owner);
    continue; // skip the legacy ProjectileData/State path
}
```

(`SpawnTick` is the basis every peer uses to compute the projectile's age — and therefore its
deterministic position — so it must be set. `TickType` is in `FishNet.Managing.Timing`.)

Then point a weapon at the new prefab: duplicate a `WeaponData` asset (e.g.
`Assets/Prefabs/Weapons/Data/Pistol.asset`) and set its **Bullet Prefab** to your new
`Bullet (PawnProjectile)` prefab's `NetworkObject`. Equip that weapon in your test scene. Legacy
weapons keep using legacy prefabs through the unchanged path — fully A/B-able.

(The `SpawnOwnerId/SpawnPosition/SpawnForward/SpawnHealth` fields are public on `PawnProjectile` and
serialize through its `WritePayload`/`ReadPayload` so all peers reconstruct the spawn deterministically.)

---

## Step 4 — Verify in play mode / ParrelSync (this is the gate before deletion)

Run two instances (ParrelSync) and confirm against the legacy weapon:
- [ ] Bullet travels straight, despawns at lifetime.
- [ ] Hit-reg: hitting an enemy pawn applies damage with correct attribution (the firer's NetworkPlayer); does NOT damage the owner (muzzle overlap is fine — owner-safety is in Core).
- [ ] Surface hit despawns the bullet.
- [ ] Bullet-vs-bullet across different owners (DevilDaggers `OnProjectile = LoseHealth`) resolves once on contact, not every tick.
- [ ] Damage/despawn happen exactly once (server-authoritative; no client double-apply under lag).
- [ ] Arc (Grenade) lobs and falls; Laser if you wire that type.
- [ ] Client motion is smooth (each peer computes position from the spawn tick — no NetworkTransform).

If something misbehaves, the logic is unit-tested in `Assets/Tests/EditMode/Projectile/` — re-run via
the Test Runner; the bug is almost certainly in the Unity wiring (masks, layer, collider, catalog,
provider presence), not the Core.

---

## Known gaps / faithfulness notes to confirm while verifying

1. **Spawn-on-lifetime-expiry (Grenade) is NOT yet covered.** Reactions only run on HITS; lifetime
   expiry just despawns. Legacy `spawnOnDeath` spawned the explosion on expiry too. If you want that,
   it's a small Core addition: a `List<IHitReaction> OnExpire` on `ProjectileType` that the processor
   runs when `Alive` flips false via lifetime (then it can hold a `SpawnProjectiles{Type=Explosion}`).
   Ask me and I'll add it with a test. The Grenade asset currently just dies on surface contact.
2. **Bullet reaction list is my best reconstruction.** Legacy `Bullet.prefab` had `ProjectileBulletImpact`
   (deflect) + `ProjectileSurfaceImpact` (lose health) but no `DamageableImpact` — so strictly it may
   not have damaged players. I authored `OnEnemy = DealDamage(3)` because a bullet logically should.
   Adjust the `.asset` reaction lists in the inspector if you want exact legacy parity.
3. **Laser** type isn't authored (no `Laser` asset yet). `LaserMovement` + the processor's forward-beam
   cast are implemented; author a `ProjectileType` with `Movement=Laser`, `MaxRange`, and
   `OnEnemy=[DealDamage{DespawnAfter=false}]` when you get to it.
4. **Impact VFX** is cosmetic: the server broadcasts an `ObserversRpc` on impact and each peer does a
   plain (non-networked) `Instantiate` of the catalog's `ImpactVfx` for the projectile's own type. A
   custom vfx type set on a `SpawnImpactVfx` reaction is not carried over the RPC (defaults to self type).
   Feedback lands ~½-RTT after the hit (no client-side prediction); acceptable and simple.
5. **Homing is intentionally not implemented** — `MovementKind` is `Linear`/`Arc`/`Laser` only.

---

## Phase 5 — Deletion (ONLY after Step 4 passes)

Once verified, tell me and I'll delete the legacy system + remove the temporary `ExecuteShot` branch
in favor of the new path being the default. Legacy files to remove (per the spec):
`Assets/Scripts/Projectile/ProjectileMovement*.cs`, `ProjectileHitbox.cs`, `Laser/LaserHitbox.cs`,
`Laser/LaserMovement.cs`, `ProjectileLifecycle.cs`, `Projectile*Impact*.cs`,
`Devil Daggers/DevilDaggersBulletImpact.cs`, `ProjectileData.cs`, `ProjectileState.cs`,
`ProjectileSpawnOnDeath.cs`, `HomingMovement.cs`, legacy `HitInfo.cs`, and the dead
`Networking/BulletPredictedSpawn.cs` — plus retiring the old bullet prefabs. After deletion the
`Arena.Projectile` namespace's `ProjectileState`/`HitInfo` no longer need aliasing.
