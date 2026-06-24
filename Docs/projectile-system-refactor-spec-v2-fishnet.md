# Projectile System Refactor Spec v2 — FishNet-aware (ArenaURP)

> Audience: an AI coding agent (Claude Code) refactoring the EXISTING projectile system in
> ArenaURP — a Unity 6 / URP arena shooter, server-authoritative over **FishNet** with
> **PredictionV2** (`[Replicate]`/`[Reconcile]`), transports Tugboat (standalone) and
> CanoeWebRTC (WebGL).
> This supersedes the generic v1 spec. The v1 "single global `ProjectileSimulation` singleton
> that ticks every bullet" is **rejected** for this codebase because it fights FishNet's
> per-`NetworkObject` prediction loop. See "Why no central sim" below.
>
> All existing projectile scripts under `Assets/Scripts/Projectile/` are to be **deprecated and
> replaced** by this architecture. Do not try to preserve their internals — only their observable
> gameplay behaviors (ricochet, deflect, damage, surface impact, homing, laser, arc, spawn-on-death,
> Devil-Daggers self-health).

---

## Governing rules (constraints on every edit)

1. **Earn the pattern.** Only add an abstraction that removes a coupling that actually exists here.
2. **Logic out of MonoBehaviour/NetworkBehaviour.** All projectile *decision* logic lives in plain,
   deterministic C# in an `Arena.Projectile.Core` assembly — edit-mode testable, no `UnityEngine`
   beyond math structs, no FishNet. `NetworkBehaviour`s are thin adapters that forward the FishNet
   tick lifecycle and apply results to Unity components. This mirrors the existing
   `Arena.<X>.Core` ↔ `Pawn<X>` convention (Movement, Weapon, Temperature, Knockback).
3. **Deterministic Core.** The Core step is `Process(state, in config, in input, dt) -> newState`
   with every dependency in the signature — no `Time.*`, no `Random`, no statics, no engine calls.
   This is what lets server and predicting clients reach the same state, and what makes FishNet
   reconcile correct.
4. **Two-tier side effects (server-authoritative vs predicted-cosmetic).** Movement is predicted on
   clients. Side effects split into two classes:
   - **Authoritative** (damage, despawn, spawning child projectiles, debuffs) — decided on the
     **server** only, never on clients, never during reconcile replays.
   - **Cosmetic** (impact VFX, local hit feedback) — may run **predicted on the owning client** (and
     on the server) for snappier feel, but still never during reconcile replays, and never in a way
     that affects gameplay state.
   Neither class may re-fire during a reconcile replay. (See the two-flag gating in the tick contract.)

---

## Ownership model (NetworkPlayer)

- A projectile's owner is the **`NetworkPlayer`** that fired it (the `ControllingPlayer.Value` of the
  firing pawn). This matches today's `ProjectileState.Firer` (`Assets/Scripts/Projectile/ProjectileState.cs:21`)
  and works for both humans and bots (`NetworkPlayer.IsBot`) — do not assume a client connection.
- The **Unity adapter and the seam work in `NetworkPlayer` terms** (damage attribution, friendly-fire
  checks, ownership assignment). The engine-free **Core works in a stable `int OwnerId`** derived from
  the owner (`NetworkPlayer.ObjectId`) purely so owner-equality/same-owner-filtering can run in pure
  code. The adapter maps `NetworkPlayer -> OwnerId` on spawn; the seam maps `OwnerId/targetPlayerId ->
  NetworkPlayer` when applying damage/debuffs.
- Ownership rules (enforced in Core, attributed via NetworkPlayer in the seam):
  - **Owner safety:** a world-cast hit whose hit player == the projectile's owner is ignored (also
    fixes muzzle-overlap on the spawn frame — replaces today's `Physics.IgnoreCollision` call in
    `PawnWeapon.ExecuteShot`).
  - **Projectile vs projectile:** resolve only across *different* owners; dedupe each pair by id.

---

## Why no central sim (the key v1 change)

The v1 spec assumed one plain `ProjectileSimulation.Tick(dt)` owning a global active list. In this
project each projectile is already a FishNet `NetworkObject` that predicts its own motion via
`[Replicate]`/`[Reconcile]` and `PredictionRigidbody` (`Assets/Scripts/Projectile/ProjectileMovement.cs`).
FishNet drives prediction, reconciliation, and rollback **per NetworkObject**. A global sim would
have to re-implement reconciliation by hand and would not interoperate with FishNet's predicted
spawning. So:

- **Keep one `NetworkBehaviour` per projectile** as the FishNet adapter (the prediction unit).
- **Move all per-tick decision logic into the pure Core** that the adapter calls from inside
  `[Replicate]`. "Centralization" becomes *shared pure code*, not a shared runtime object.
- The projectile-vs-projectile channel still needs cross-bullet queries, but those go through the
  engine broadphase via the seam (`OverlapProjectiles`), called from each projectile's own Replicate —
  not from a global owner object.

---

## Target architecture in one paragraph

Each projectile is a FishNet `NetworkObject` (prefab with `PredictedSpawn` enabled) carrying a thin
`PawnProjectile`-style adapter (`NetworkBehaviour`). Per-instance mutable data lives in a plain
`ProjectileState` struct; shared immutable data lives in a `ProjectileType` ScriptableObject
(Type Object + Flyweight). Each tick, the adapter's `[Replicate]` builds a `ProjectileStepInput`,
calls the pure `ProjectileStepProcessor.Step(...)` to advance movement + run collision queries +
route hits to composed `IHitReaction` lists, then applies the returned state to the transform and
performs any **server-authoritative, non-replay** side effects through one injected `IProjectileWorld`
seam (cast, overlap, spawn, despawn, damage, VFX, debuff). `[Reconcile]` restores the full
predicted state. No global singleton; no per-bullet rigidbody *dynamics* (the rigidbody is kinematic
and used only as a queryable broadphase entry).

---

## Pattern decisions (apply; don't relitigate)

- **Type Object + Flyweight** — `ProjectileType` ScriptableObject per kind; instances hold only
  mutable per-bullet state. Replaces today's per-prefab `ProjectileData` component + prefab variants.
- **Composition for reactions** — per-`HitCategory` lists of `IHitReaction`, NOT a subclass tree.
  Replaces today's `ProjectileDamageableImpact` / `ProjectileBulletImpact` / `ProjectileSurfaceImpact`
  / `DevilDaggersBulletImpact` MonoBehaviours.
- **Strategy for movement** (`IProjectileMovement`), one per type. Replaces the
  `ProjectileMovement` / `ProjectileMovementArc` / `HomingMovement` / `LaserMovement` subclass tree.
  Promote to State only if a single projectile changes movement *mode* mid-flight (not required;
  ricochet is a velocity reflect).
- **Seam (DI)** — `IProjectileWorld` passed into the Core step; production `UnityProjectileWorld`,
  test `FakeWorld`. This is the project's `ISpawner`/fake pattern.
- **FishNet PredictionV2** — keep `[Replicate]`/`[Reconcile]`; the adapter is the prediction unit.
- **FishNet Predicted Spawning** — bullet prefabs use the `PredictedSpawn` component so the owning
  client can spawn locally and have FishNet reconcile with the server's authoritative object.
- **Object Pool (Tier 2)** — keep using FishNet's `GetPooledInstantiated` (already in
  `PawnWeapon.ExecuteShot`). Keep view-reset logic in a plain method for testability. No new pool.
- **Rejected:** global `ProjectileSimulation` singleton; Command pattern; custom spatial partition;
  custom game loop; DOTS/Data-Locality.

---

## Core types (engine-free — `Arena.Projectile.Core`)

> `Vector3` from `UnityEngine` is allowed in Core (math struct only), consistent with the other
> `Arena.*.Core` assemblies. No FishNet, no `MonoBehaviour`, no `Physics`, no `Time`.

```csharp
public enum HitCategory { Surface, Enemy, Projectile }
public enum MovementKind { Linear, Arc, Homing, Laser }

public struct ProjectileState {        // per-instance, mutable, value type (reconcile-friendly)
    public int     OwnerId;            // NetworkPlayer.ObjectId of the firer
    public int     Id;                 // resolves a cast/overlap hit back to this projectile
    public Vector3 Position;
    public Vector3 Velocity;
    public Vector3 Forward;            // facing, for arc/homing math + view rotation
    public int     Health;
    public bool    Alive;
    public float   Age;                // seconds alive; for lifetime culling (fed dt each step)
    // homing/target scratch as needed (kept minimal to preserve the flyweight win)
}

[CreateAssetMenu(menuName = "Projectiles/Type")]
public sealed class ProjectileType : ScriptableObject {   // shared, immutable at runtime
    public float        Radius = 0.05f;
    public int          MaxHealth = 1;
    public float        Speed = 50f;
    public float        Acceleration = 0f;
    public float        Lifetime = 5f;
    public MovementKind Movement;
    [SerializeReference] public List<IHitReaction> OnSurface;
    [SerializeReference] public List<IHitReaction> OnEnemy;
    [SerializeReference] public List<IHitReaction> OnProjectile;
}

public interface IProjectileMovement { void Advance(ref ProjectileState s, in ProjectileType t, float dt, IProjectileWorld world); }
public interface IHitReaction        { void Apply(in HitInfo hit, ref ProjectileState self, IProjectileWorld world); }
```

> Note: `ProjectileState` is a **struct** (not the v1 class) so it round-trips cleanly through
> FishNet reconcile data. `ProjectileType` referenced by index/id in reconcile (see below), never
> serialized per tick.

---

## The seam (`IProjectileWorld`) — every engine + networked side effect goes through here

```csharp
public interface IProjectileWorld {
    // Queries (safe to call during prediction/replay — read-only):
    bool CastWorld(Vector3 from, Vector3 to, float radius, out WorldHit hit);  // Stage|Players mask
    int  OverlapProjectiles(Vector3 center, float radius, int[] buffer);       // Projectiles mask -> ids
    bool TryGetProjectile(int id, out ProjectileState state);                  // resolve overlap ids

    // AUTHORITATIVE side effects (server only; skipped on clients AND during reconcile replay):
    int  Spawn(ProjectileType type, Vector3 pos, Vector3 dir, int ownerId);    // child projectiles
    void Despawn(int id);
    void ApplyDamage(int targetPlayerId, int amount, int attackerPlayerId, Vector3 point, Vector3 dir);
    void ApplyDebuff(int targetPlayerId, DebuffType debuff);

    // COSMETIC side effects (server + owning client predicted; skipped during reconcile replay):
    void SpawnImpactVfx(ProjectileType type, Vector3 pos, Vector3 normal);

    // Ownership resolution kept on the Unity side:
    int  OwnerSafetyPlayerId { get; }   // helper if needed; mapping NetworkPlayer<->id lives in impl
}
```

- `UnityProjectileWorld` (in `Arena.Projectile.Unity`): wraps `Physics.SphereCastNonAlloc` /
  `Physics.OverlapSphereNonAlloc` with reused buffers (zero per-frame GC); maps `OwnerId`/target ids
  to `NetworkPlayer` (via the player manager / `NetworkPlayerOwnership`); routes `ApplyDamage` to the
  existing `IDamageable.TakeDamage(DamageInfo)` path with `DamageInfo.Attacker = <NetworkPlayer>`,
  `Type = DamageType.Bullet`; routes `Spawn` through `ServerManager.Spawn` + `GetPooledInstantiated`;
  routes `Despawn` through the lifecycle despawn.
- `FakeWorld` (in tests): records spawns/damage/despawns/debuffs/vfx and returns scripted cast +
  overlap results. Lets every reaction and the whole step be edit-mode tested with no engine.

---

## Reactions (port existing behaviors onto these)

Each is one plain class, one `Apply`, no engine calls except via `world`:

- `Ricochet` — `Velocity = Vector3.Reflect(Velocity, hit.Normal)`; update `Forward`. (today: implicit)
- `DeflectProjectile` — on `OnProjectile`, set velocity along the other bullet's direction
  (ports `ProjectileBulletImpact`).
- `LoseHealth { Amount }` — `Health -= Amount; if (Health <= 0) Alive = false;`
  (ports `ProjectileSurfaceImpact` / `DevilDaggersBulletImpact` self-damage).
- `DealDamage { Amount }` — `if (hit.OtherPlayerId >= 0 && hit.OtherPlayerId != self.OwnerId)
  world.ApplyDamage(hit.OtherPlayerId, Amount, self.OwnerId, hit.Point, dir);` then usually `Alive=false`
  (ports `ProjectileDamageableImpact`).
- `SpawnProjectiles { Type, Count }` — `world.Spawn(...)` N times (ports `spawnOnDeath`).
- `SpawnImpactVfx` — `world.SpawnImpactVfx(...)` (ports `impactVfx`/`spawnOnAnyImpact`).
  **Cosmetic** — gated on `input.AllowCosmeticEffects` (runs on owning client + server, not replays).
- `ApplyDebuff { Debuff }` — `world.ApplyDebuff(hit.OtherPlayerId, Debuff)` when target is a player.

New behaviors = new reaction class, no changes elsewhere. Power-ups (later) = compose a new
`ProjectileType` at pickup (concat reaction lists, swap `MovementKind`) and cache it.

---

## Movement strategies (port existing behaviors onto these)

- `LinearMovement` — `Position += Velocity * dt` (+ optional `Acceleration` along `Forward`).
- `ArcMovement` — adds gravity to velocity each step (ports `ProjectileMovementArc`).
- `HomingMovement` — steers `Velocity` toward nearest non-owner player; needs target lookup, so it
  reads candidates through the seam (`world`), not via `FindAnyObjectByType` (ports `HomingMovement`).
- `LaserMovement` — stationary; `Velocity = 0`, position fixed; the hitscan is expressed as a
  `CastWorld` over the beam each step (ports `LaserMovement`/`LaserHitbox` manual cast).

`MovementKind` on the type selects the strategy (a small pure factory/switch in Core, no DI).

---

## Tick contract — `ProjectileStepProcessor.Step(...)` called from `[Replicate]`

```csharp
public static ProjectileState Step(
    ProjectileState s, ProjectileType type, in ProjectileStepInput input, float dt, IProjectileWorld world);
```

`ProjectileStepInput` carries the per-tick flags the Core can't read itself — **two gates**:

- `bool AllowAuthoritativeEffects` — TRUE only on the **server**, on a non-replay tick. Gates
  `Spawn`/`Despawn`/`ApplyDamage`/`ApplyDebuff`. FALSE on every client and during all reconcile replays.
- `bool AllowCosmeticEffects` — TRUE on the **server OR the owning client**, on a non-replay tick.
  Gates `SpawnImpactVfx` (and any future local hit feedback). FALSE during reconcile replays and on
  non-owner clients.
- Movement and health math **always** run (deterministic + reconciled); only world-mutations are
  gated. Reactions check the matching flag before calling a mutating seam method. This two-flag split
  is THE FishNet-correctness rule the v1 spec missed — it's what lets cosmetics be predicted while
  damage/despawn stay server-authoritative, with neither double-firing on rollback.

The adapter sets the flags: `AllowAuthoritativeEffects = IsServerInitialized && rs.IsTickedCreated()`;
`AllowCosmeticEffects = (IsServerInitialized || IsOwner) && rs.IsTickedCreated()`.

Order inside `Step` (fixed `dt`):

1. **Lifetime:** `s.Age += dt; if (s.Age >= type.Lifetime) s.Alive = false;`
2. **Move:** `movement.Advance(ref s, in type, dt, world)` (pure; may read-only query via world).
3. **World channel:** `world.CastWorld(from, to, type.Radius, out hit)`. If `hit.PlayerId ==
   s.OwnerId` → ignore (owner safety). Else snap `s.Position` to hit point and run the matching
   reaction list — `OnEnemy` if a player was hit, else `OnSurface`.
4. **Projectile channel:** `world.OverlapProjectiles(s.Position, type.Radius, buffer)`; for each
   returned id, resolve via `world.TryGetProjectile`, skip same-owner, dedupe by resolving only when
   `s.Id < other.Id`, then run `OnProjectile` reactions. (Effects still gated by `AllowSideEffects`.)
5. Return `s`. Despawn of `!s.Alive` is performed by the adapter after the step (server-authoritative),
   not inside Core.

Children spawned this tick can't recursively collide this tick (they enter the active set next tick),
preserved by the fact that spawns go through the seam/`ServerManager.Spawn` and are distinct
NetworkObjects.

---

## FishNet adapter — `PawnProjectile : NetworkBehaviour` (glue only)

Replaces `ProjectileMovement` + `ProjectileHitbox` + `ProjectileLifecycle`. Pattern mirrors the
existing `ProjectileMovement` (`OnPostTick` → `RunReplicate` → `CreateReconcile`):

```csharp
public class PawnProjectile : NetworkBehaviour {
    [SerializeField] private ProjectileType _type;   // shared flyweight
    private ProjectileState _state;                  // mutable per-bullet
    private IProjectileWorld _world;                 // resolved from a scene-level provider

    struct ReplicateData : IReplicateData { /* empty: motion is deterministic from _state+type */ }
    struct ReconcileData : IReconcileData {           // full predicted state to roll back to
        public Vector3 Position, Velocity, Forward;
        public int Health; public bool Alive; public float Age;
        // tick boilerplate
    }

    void OnStartNetwork() { TimeManager.OnPostTick += PostTick; _world = ProjectileWorldProvider.Get(); }
    void OnStopNetwork()  { TimeManager.OnPostTick -= PostTick; }

    void PostTick() { RunReplicate(default); if (IsServerInitialized) CreateReconcile(); }

    [Replicate]
    void RunReplicate(ReplicateData d, ReplicateState rs = ReplicateState.Invalid, Channel c = Channel.Unreliable) {
        bool fresh = rs.IsTickedCreated();                 // authoritative, non-replay tick
        var input = new ProjectileStepInput {
            AllowAuthoritativeEffects = IsServerInitialized && fresh,           // damage/despawn/spawn/debuff
            AllowCosmeticEffects      = (IsServerInitialized || IsOwner) && fresh, // impact VFX (predicted)
        };
        _state = ProjectileStepProcessor.Step(_state, _type, in input, (float)TimeManager.TickDelta, _world);
        ApplyToTransform();                          // position + LookRotation(Forward)
        if (IsServerInitialized && !_state.Alive) Despawn();   // authoritative cull
    }

    public override void CreateReconcile() => RunReconcile(Snapshot(_state), Channel.Unreliable);

    [Reconcile]
    void RunReconcile(ReconcileData rd, Channel c) { _state = Restore(rd); ApplyToTransform(); }
}
```

> Verify `ReplicateState.IsTickedCreated()` (or the equivalent "is authoritative non-replay tick"
> query) against the installed FishNet version before relying on the exact name — the intent is
> "run mutating side effects exactly once, on the authoritative pass, never during reconcile replay."

`ProjectileView` (optional, if separating visuals): pooled, mirrors `_state` → transform in
`LateUpdate`; carries the kinematic trigger collider + rigidbody for the projectile channel.

---

## Collision design (cheapest viable — keep)

- **No rigidbody dynamics, no solver.** Movement is by transform inside the strategy. Today's
  `ContinuousDynamic` rigidbody + `NetworkCollision`/`NetworkTrigger` event routing
  (`ProjectileMovement.cs:50`, `ProjectileHitbox.cs`) is removed in favor of explicit casts.
- **Two channels = two LayerMasks:**
  - *World channel:* `SphereCast` last→new position vs `Stage | Players` (continuous, no tunneling
    for fast bullets). The projectile needs NO collider here.
  - *Projectile channel:* `OverlapSphere` vs `Projectiles` layer. To be *found*, each bullet carries
    ONE queryable trigger `SphereCollider` on the `Projectiles` layer + a KINEMATIC rigidbody (so
    moving it doesn't thrash the static tree). This is the only per-bullet physics object and it
    never enters the solver.
- **Ownership in code, not layers** — see the Ownership model section.
- **Cost controls:** `*NonAlloc` + reused buffers; shared `ProjectileType` flyweights keep instance
  state tiny; FishNet pooled instantiation. If profiling later shows collider/rigidbody overhead
  dominating with bounded counts, brute-force pairwise over the active list is an allowed swap for
  the projectile channel — only with that profiling reason. **`log` any such cap; never silently
  truncate coverage.**

---

## Predicted spawning (FishNet `PredictedSpawn`)

- Bullet prefabs get a FishNet **`PredictedSpawn`** component with predicted spawning enabled on the
  `NetworkObject`.
- Spawn data (`OwnerId` from `NetworkPlayer.ObjectId`, `StartPosition`, `StartDirection`,
  `PreciseTick`, `Health`) is sent via the `PredictedSpawn`/payload write so the **server can validate**
  (rate, ammo, owner) and **all peers reconstruct deterministically**. Today's
  `ProjectileState.WritePayload/ReadPayload` already serializes exactly this set (plus `Firer` as a
  `NetworkBehaviour`); fold it into the predicted-spawn payload and keep `Firer` (`NetworkPlayer`).
- Firing flow becomes: the owning client (in `PawnWeapon.ExecuteShot`, or a successor) **predict-spawns**
  the bullet locally for instant feedback; the server authoritatively spawns and FishNet reconciles
  the predicted object id. The server remains the authority for hit-reg/damage (gated via
  `AllowAuthoritativeEffects`), while impact VFX/local hit feedback are predicted on the owning client
  (gated via `AllowCosmeticEffects`). Reconcile the bullet's predicted *motion* via `[Reconcile]` above.
- Keep predicted-spawn behavior behind the same Bootstrap stage awareness; verify it works under both
  Tugboat (standalone/editor) and CanoeWebRTC (WebGL) — do not assume one transport.

> The currently-commented `Networking/BulletPredictedSpawn.cs` and the commented `ProjectileState.OnStartClient`
> predicted block are the seeds of this; replace them with the `PredictedSpawn`-component approach
> rather than reviving the manual RPC version.

---

## Refactor sequence — Core/reactions/seam/tests FIRST, PredictionV2 SECOND

### Phase 0 — Scaffolding (no behavior change)
0.1 Create `Arena.Projectile.Core` asmdef (refs: none beyond `UnityEngine` for math; NO FishNet,
    NO other gameplay asmdefs). Create `Arena.Projectile.Unity` asmdef (refs FishNet + Core +
    existing gameplay). Add `Arena.Projectile.Core` to `Arena.Tests` references.
0.2 Confirm it compiles empty/in isolation.

### Phase 1 — Core extraction + reactions + seam + tests (NO networking yet)  ← must complete before Phase 3
1.1 Add Core types: `ProjectileState` (struct), `ProjectileType` (SO), `HitCategory`, `MovementKind`,
    `HitInfo`/`WorldHit`, `IProjectileMovement`, `IHitReaction`, `IProjectileWorld`,
    `ProjectileStepInput`.
1.2 Implement movement strategies (`Linear`, `Arc`, `Homing`, `Laser`) + the pure selector.
1.3 Implement reactions (`Ricochet`, `DeflectProjectile`, `LoseHealth`, `DealDamage`,
    `SpawnProjectiles`, `SpawnImpactVfx`, `ApplyDebuff`) — porting the behaviors of today's impact
    classes. Reactions gate mutating seam calls on `input.AllowSideEffects` (threaded in via the step).
1.4 Implement `ProjectileStepProcessor.Step(...)` per the tick contract.
1.5 Implement `FakeWorld` in `Arena.Projectile.Tests.EditMode`.
1.6 Write edit-mode tests (the regression net) — see Invariants below. **Phase 1 is "done" only when
    these pass.** No `Pawn*`/FishNet code has been touched yet.

### Phase 2 — Unity seam impl (still no prediction change)
2.1 Implement `UnityProjectileWorld` (NonAlloc casts/overlaps, buffer reuse, `NetworkPlayer` mapping,
    routes `ApplyDamage` to existing `IDamageable`/`DamageInfo`, `Spawn`/`Despawn` to FishNet).
2.2 Add a scene-level `ProjectileWorldProvider` (plain, injectable; NOT a Singleton — a serialized
    reference or a resettable provider so tests/edit-mode stay clean).
2.3 Map each existing bullet prefab variant → a `ProjectileType` asset (Linear bullet, Devil Daggers,
    laser, arc, homing). Author reaction lists to match current behavior.

### Phase 3 — FishNet PredictionV2 adapter (replaces the old movement/hitbox/lifecycle)
3.1 Implement `PawnProjectile : NetworkBehaviour` with `[Replicate]`/`[Reconcile]` per the adapter
    section; `ReconcileData` carries full predicted state; `AllowSideEffects` gated by
    server + non-replay.
3.2 Wire collision: kinematic trigger `SphereCollider` on `Projectiles` layer; world channel via
    `CastWorld`; remove `ContinuousDynamic` rigidbody dynamics, `NetworkCollision`/`NetworkTrigger`,
    and the `PriorityQueue` hit routing.
3.3 Authoritative cull in the adapter (`!Alive` → `Despawn`), not in Core.

### Phase 4 — Predicted spawning + firing integration
4.1 Add `PredictedSpawn` component to bullet prefabs; move spawn payload (incl. `NetworkPlayer Firer`)
    into the predicted-spawn path.
4.2 Update `PawnWeapon.ExecuteShot` to predict-spawn from the owning client and let the server
    validate/authoritatively spawn; drop the `Physics.IgnoreCollision` muzzle hack (replaced by
    owner-safety in Core). Keep working under both transports.

### Phase 5 — Deprecate & delete old system
5.1 Delete `Assets/Scripts/Projectile/` legacy scripts (`ProjectileMovement*`, `ProjectileHitbox`/
    `LaserHitbox`, `ProjectileLifecycle`, `Projectile*Impact*`, `ProjectileData`, `ProjectileState`,
    `ProjectileSpawnOnDeath`, `HomingMovement`, `LaserMovement`) once prefabs reference the new
    components. Remove dead `BulletPredictedSpawn.cs`.
5.2 Run all edit-mode tests + a play-mode smoke (ParrelSync two-instance) to confirm hit-reg,
    ricochet, deflect, spawn-on-death, homing, laser still behave.

### Phase 6 — Pooling/perf (only if profiler shows hitches)
6.1 Keep FishNet pooled instantiation; if GC/instantiation hitches appear, add explicit view reset
    (plain, tested) — do not pre-emptively add a new pool.

---

## Invariants to assert in tests / preserve during refactor

- A projectile never reacts to a collision with its own owner (world or projectile channel), by
  `OwnerId`.
- Projectile↔projectile collisions occur only across different owners and resolve once per pair
  (dedupe by `Id`).
- Reaction logic, health changes, and movement contain NO direct engine/FishNet calls — only via
  `IProjectileWorld`.
- **Two-tier gating holds:** authoritative effects (`Spawn`/`Despawn`/`ApplyDamage`/`ApplyDebuff`)
  fire only when `AllowAuthoritativeEffects` is true; cosmetic effects (`SpawnImpactVfx`) fire only
  when `AllowCosmeticEffects` is true; neither fires during a reconcile replay. Add `FakeWorld` tests:
  (a) both flags false → zero recorded mutations while movement/health still advance;
  (b) cosmetic-only true (owning-client predict) → VFX recorded, but zero damage/despawn/spawn/debuff.
- Spawns created during a tick are not collided again until the next tick.
- `ProjectileType` is shared/immutable at runtime; mutating per-bullet `ProjectileState` never mutates
  the type.
- Movement is deterministic: same `ProjectileState` + `ProjectileType` + `dt` ⇒ same next state
  (required for FishNet reconcile correctness).
- Ownership is attributed to a `NetworkPlayer` end-to-end: damage `DamageInfo.Attacker` resolves back
  to the firing `NetworkPlayer` (human or bot).

---

## Assembly / file layout

- `Arena.Projectile.Core` (asmdef; only `UnityEngine` math): `ProjectileState`, `ProjectileType`,
  `HitCategory`, `MovementKind`, `HitInfo`, `WorldHit`, `IProjectileMovement`, `IHitReaction`,
  `IProjectileWorld`, `ProjectileStepInput`, movements, reactions, `ProjectileStepProcessor`.
- `Arena.Projectile.Unity`: `PawnProjectile`, `UnityProjectileWorld`, `ProjectileWorldProvider`,
  `ProjectileView`, layer/collider setup.
- `Arena.Projectile.Tests.EditMode` (under `Arena.Tests`): `FakeWorld` + reaction/movement/step tests.

## Do NOT build
Global `ProjectileSimulation` singleton; custom game loop; custom spatial partition (use engine
broadphase); Command pattern; hand-rolled reconciliation (use FishNet PredictionV2); DOTS/Data-Locality.
