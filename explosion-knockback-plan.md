# Explosion knockback plan

## Intent

Projectiles that hit a surface can trigger an explosion that damages nearby pawns and knocks them back.
The explosion is not a networked object. Every peer already runs `Projectile` collision locally
(`Projectile.FixedUpdate`), so each peer resolves the explosion itself:

- **Server:** applies damage. It does not move pawns, because movement is client-authoritative.
- **Victim's owner** (including the host for bots): adds the knockback to its own movement, so the
  victim feels it right away and the client-authoritative NetworkTransform carries the result to everyone.
- **Observers:** predict the knockback on the pawn's visuals only, then blend to the networked
  position as the real data arrives, so knockback looks snappy instead of arriving a hop and an
  interpolation delay late.

Out of scope: fixing mispredictions, explosion IDs/RPCs, lasting networked explosion state, and
server checks that the owner applied the knockback.

## Ground rules for the session

- **No prefab, scene or asset changes.** Don't edit `.prefab`, `.unity`, `.asset` or `.mat` files.
  New serialized fields must have code defaults that keep today's behavior. The user wires the
  existing `Assets/Prefabs/Effects/Explosion.prefab` (a visual-only particle/GIF prefab, currently
  unreferenced) into projectile prefabs in the editor themselves.
- **Stop after each step** for the user to review. Don't start the next step until they say so.
  Don't commit unless asked.
- Match the style of the surrounding code (section-divider comments, XML doc comments on public API,
  guard clauses).
- After each step, check that it compiles (Unity console via the Unity MCP tools).

## Step 1: explosion resolution and owner knockback

**New file: `Assets/Scripts/Projectile/Explosion.cs`** (Assembly-CSharp, next to `Projectile.cs`)

- `[System.Serializable] public class ExplosionSettings`: `GameObject VfxPrefab` (null = no explosion),
  `float Radius`, `int Damage`, `float KnockbackSpeed` (a velocity change in m/s, because
  `MovementProcessor` adds impulses straight to velocity), `float UpwardBias` (extra vertical
  component so ground hits launch players), `LayerMask PawnLayers`.
- `public static class Explosion` with a pure static `Resolve(ExplosionSettings, Vector3 point,
  Vector3 normal, Pawn owningPawn, NetworkManager/flags as needed)`:
  - On peers with a client running, `Instantiate` the VFX prefab at `point` (same guard as
    `Projectile.PlayDamageVfx`).
  - `Physics.OverlapSphere(point, Radius, PawnLayers, QueryTriggerInteraction.Collide)`; dedupe
    to unique `Pawn`s (one pawn can have several colliders).
  - For each pawn, compute the impulse with a pure helper: direction from `point` to the pawn's
    center (fall back to `normal` when they overlap), linear falloff by `distance / Radius`, plus
    `UpwardBias`.
  - Damage: call `pawn.TakeDamage(...)` with the falloff-scaled damage on every peer, the same way
    `Projectile` does now (`TakeDamage` already limits the health change to the server). Default:
    don't damage `owningPawn`.
  - Knockback: call `pawn.ApplyKnockback(impulse)` on every peer, including `owningPawn` (rocket jumps).

**`Projectile.cs`**

- Add `[SerializeField] ExplosionSettings explosion` under a new `[Header("Explosion")]`.
- Explode on surface contact (the `TakeCollisionDamage` path), only when `explosion.VfxPrefab != null`.
- **Guard against double resolution:** `HandleCollisions` can call `TakeCollisionDamage` several
  times in one `FixedUpdate` (one call per hit). Only explode on the call where the projectile goes
  from alive to dead. Lifetime expiry (`Update` → `Die` + smoke) does not explode.
- Projectile-vs-projectile kills also go through `TakeCollisionDamage`. Default: they explode too.
  Call this out in the review notes.

**`Pawn.cs`**

- Add `private readonly List<Vector3> _pendingImpulses = new();`.
- Add `public void ApplyKnockback(Vector3 impulse)`: if `IsOwner`, add the impulse to `_pendingImpulses`.
  Leave the observer branch as a clearly marked no-op for step 2.
- In `Move()`, pass `Impulses = _pendingImpulses` in `MovementInput`, then clear the list after
  `MovementProcessor.Process`.

**`MovementProcessor.cs`**: knockback doesn't survive the current rules, so these need changing:

- **Horizontal:** with movement input held, `CalculateXZVelocity` clamps to `effectiveMaxSpeed`
  every frame, which kills horizontal knockback the frame after it's applied. Change the clamp so
  speed above the max isn't cut off instantly. Suggested: clamp to
  `Mathf.Max(effectiveMaxSpeed, previousVelocityXZ.magnitude)` while airborne, so the excess carries
  until landing, where the existing grounded rules take over. Write down the chosen rule for review.
- **Vertical:** `GetGroundedYVelocity` returns 0, so an upward impulse on a grounded pawn only lasts
  if the next `controller.Move` lifts it off the ground. Check this in play mode. If it doesn't hold,
  treat a positive-Y impulse as leaving the ground for that frame.
- Keep `Process` pure (no new statics/state). `Arena.Movement.Core` has no references, so nothing
  in it can depend on `Pawn` or FishNet.

**Review checkpoint:** stop. Summarize the diff, the falloff/clamp rules you chose, and anything
you couldn't verify.

## Step 2: observer knockback visuals (inside `Pawn`, using `MovementProcessor`)

No new class. The prediction state and logic go in `Pawn`. The trajectory comes from
`MovementProcessor` so observers and owners share one gravity/air model.

**`MovementProcessor.cs`**

- If needed, add a small pure helper (e.g. `SimulateKnockback(in MovementConfig, Vector3
  startVelocity, float elapsed)` returning the displacement), built on `Process` with zero input and
  `IsGrounded = false`. No collision, no `Physics` calls. Note that `GetSlopeSnappedVelocity`
  raycasts when grounded, so the helper must keep `IsGrounded = false`.

**`Pawn.cs`**

- New serialized field `float knockbackBlendTime` with a code default (~0.15s, roughly one-way
  latency plus 2 ticks of interpolation at 50 Hz). It's a serialized field, so no prefab edit is
  needed; it uses the default.
- Prediction state: start position (the displayed root position when the explosion happens),
  start velocity (impulse plus the pawn's estimated current velocity from frame-to-frame root
  movement, or zero if that's unreliable), elapsed time, and active flag. A new explosion restarts
  the prediction from the current displayed position.
- `ApplyKnockback` observer branch: `else if (IsClientInitialized)` → start the prediction.
  A dedicated server does nothing.
- **What gets moved (no prefab changes):** at `OnStartClient`, for non-owners, cache the root's
  direct children that have no `NetworkTransform` (currently `Body Mesh` and `VFX`) along with
  their original `localPosition`. Don't move the root: it carries the NetworkTransform and the
  observer hitbox (`CapsuleCollider` added in `OnStartClient`).
- In `LateUpdate`, while active: `predicted = start + displacement(elapsed)`,
  `α = elapsed / knockbackBlendTime`, `shown = Lerp(predicted, rootPosition, α)`, then offset the
  cached children by `shown - rootPosition`, converted to the root's local space. When `α >= 1`,
  restore their original `localPosition` and deactivate. Also restore in `ResetState`/`OnStopClient`,
  since pawns are pooled.
- **Known limitation to flag:** `Camera` (which holds `Primary Weapon`/`Secondary Weapon`) has its
  own NetworkTransform, so it's excluded and the gun visuals won't follow the predicted knockback.
  Check how that NetworkTransform writes the camera's transform on observers and report options;
  don't work around it without asking.

**Review checkpoint:** stop. Summarize the diff and what was checked in play mode (ideally two
ParrelSync clients: one firing at a pawn the other owns, watching from a third view if possible).
