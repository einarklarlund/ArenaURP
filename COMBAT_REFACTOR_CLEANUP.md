# Combat / Weapon Refactor — Cleanup Plan

Addresses the verified findings from the high-effort code review of the combat/weapon refactor
(damage moved off `Pawn.ServerTakeDamage` onto the `Health` sibling; weapon spread reworked).

**Six correctness bugs + one dead-API cleanup.** Grouped by call site and risk. The floor from
`CLAUDE.md`: **every networked change gets a PlayMode test** — each fix below names the test that proves it.

Refuted candidates (`DeathmatchManager.cs:19` field-initializer, `Health.cs:29` 1-HP baseline) are
**not** in scope — verification cleared them.

---

## Priority order (recommended)

| # | Fix | Files | Risk | Depends on design? |
|---|-----|-------|------|--------------------|
| A | Shotgun spread: pellet index + modifier spread | `PawnWeapon.cs` | Low, isolated | No |
| B | `TriggerHurt` server guard | `TriggerHurt.cs` | Low, one-line | No |
| C | Host double-fire `OnDeath` | `Health.cs` | Low | No |
| D | Null `Health` sibling fails loudly | `Pawn.cs` | Low | No |
| E | Dead `Resolve(int)` overload | `DamageProcessor.cs` + test | Trivial | No |
| F | Lost cross-peer hit/death VFX broadcast | `Health.cs` (+ possibly `ServerCollision`) | **Higher** | **Yes** |

Do **A–E** first — mechanical, unit/PlayMode-testable, no open questions. **F is a design
decision** (how predictive damage and cross-peer VFX coordinate) and should go through the
`fishnet-conventions` + `engineering-canon` lenses before code.

---

## A. Shotgun spread — pellet index dropped **and** modifier spread ignored

**Findings 5 + 6.** `Assets/Scripts/Pawn/Weapon/PawnWeapon.cs:82`

Two bugs at one call site:

1. **Pellet index is a literal `0`** — every pellet of a multi-projectile shot gets the same
   angle (`-spreadAngle/2`, the leftmost slot), so `SpreadType.Even` fans nothing. Should be the
   loop variable `i`.
2. **`effectiveSpread` is computed but never used** — `ExecuteShot(currentWeapon, effectiveSpread)`
   receives the modifier-adjusted angle, but the body calls the 4-arg
   `CalculateSpread(currentWeapon, …)` overload, which reads the **raw** `weaponData.SpreadAngle`
   and ignores the parameter. Any `WeaponModifier.SpreadMultiplier` (overheat widening, accuracy
   buff tightening) silently does nothing.

**Fix.** `WeaponHelper` already exposes a 6-arg overload that takes an explicit spread angle
(`WeaponHelper.cs:8`). Call it with `spreadAngle` (the passed-in `effectiveSpread`) and `i`:

```csharp
// PawnWeapon.ExecuteShot, line 82
var fireDir = WeaponHelper.CalculateSpread(
    currentWeapon.SpreadType, currentWeapon.ProjectilesPerShot,
    spreadAngle,            // was: read from currentWeapon.SpreadAngle inside the 4-arg overload
    firePoint, i, count);   // was: index 0, count currentWeapon.ProjectilesPerShot
```

- Rename the `spreadAngle` parameter of `ExecuteShot` to make intent obvious (it is the *effective*
  angle, post-modifier).
- Consider deleting the now-unused 4-arg `CalculateSpread(WeaponData, …)` overload if this was its
  only caller (check first — it may be a second dead API, same class as finding E).

**Proof.** EditMode test on the pure spread math (`Arena.*.Core` if the geometry lives there, else a
`WeaponHelper` EditMode test): assert that for `ProjectilesPerShot = 3, SpreadType.Even`, the three
returned directions are **distinct** and symmetric about `firePoint.forward`; and that a
`SpreadMultiplier = 2` widens the fan vs. multiplier `1`. The Even branch has no `Random`, so it is
deterministic and belongs in EditMode.

---

## B. `TriggerHurt` runs on every peer — server guard dropped

**Finding 2.** `Assets/Scripts/Misc/TriggerHurt.cs:10`

`OnTriggerEnter` lost its `if (!IsServerStarted) return;` guard, so hazard damage (lava/spikes) now
runs on every peer's local physics and hits the all-peer predictive `Health.TakeDamage`:

- **On a client:** any pawn clipping a trigger volume plays a spurious hit-flash / predicted-death
  VFX for a pawn the server keeps alive — uncoordinated across peers.
- **On a host:** a lethal hazard hit runs the death/despawn+respawn chain twice.

**Fix.** Restore the guard at the top of `OnTriggerEnter`:

```csharp
private void OnTriggerEnter(Collider other)
{
    if (!IsServerStarted) return;   // hazard damage is server-authoritative; clients react via replication
    ...
}
```

This is the `fishnet-conventions` rule: gameplay mutation runs inside/from a `[Server]` context.

**Proof.** PlayMode test (host fixture): spawn a pawn, move it into a `TriggerHurt` volume, assert
HP drops exactly once and death fires once. A pure host fixture won't catch the client-side spurious
path directly, but the double-fire on host is the observable symptom and is enough to lock the
regression. (If the combat rig supports a second client peer, assert the client sees no local
predicted death from the hazard.)

---

## C. Host double-fires `Health.OnDeath`

**Finding 1.** `Assets/Scripts/Combat/Health.cs:117`

On a host/listen-server, `IsClientInitialized` **and** `IsServerInitialized` are both true, so one
lethal hit raises `OnDeath` twice:

- line 117–118: client-predictive `OnDeath` (fires because `IsClientInitialized`)
- line 128: authoritative server `OnDeath`

Subscribers that don't self-unsubscribe run twice — `PawnDamageVfx` double-bursts the death
particle. Only `PawnManager` escapes (its handler removes itself on first fire).

**Fix.** The client-predictive death is *only* meaningful on a peer that is **not** the server (a
pure client predicting a hit it can't yet confirm). On the host the authoritative branch already
covers it. Gate the predictive branch on client-only:

```csharp
// Client-side predictive death (subject to rollback) — NOT on the host, whose server branch fires authoritatively.
if (IsClientInitialized && !IsServerInitialized && CurrentHealth.Value - info.Amount <= 0)
    OnDeath?.Invoke(info);
```

Apply the same `!IsServerInitialized` reasoning to the predictive `OnDamageTaken` at line 114 **only
if** review shows it also double-fires visibly on host — verify before touching; the review flagged
`OnDeath` specifically. Keep the change minimal.

**Proof.** PlayMode host fixture: subscribe a counter to `pawn.OnDeath`, deal a lethal hit, assert
the counter increments **exactly once**. This test fails today (counts 2) — the ideal repro.

---

## D. Null `Health` sibling should fail loudly once, not spam NREs

**Finding 4.** `Assets/Scripts/Pawn/Pawn.cs:44`

`Awake` does `_health = GetComponent<Health>()` and, when null, only logs an error and `return`s —
it does not disable the pawn. Every facade reader (`Health`/`MaxHealth`/`TakeDamage`, and downstream
`DamageIndicator`, `PawnColor`, `NetworkUIController`, `BotBrain.BuildSnapshot`, `MatchTestRig`,
HurtBox/KillTimer wiring) then throws `NullReferenceException` — one logged error at Awake followed
by per-frame NRE spam.

**Fix (two layers).**

1. **Prevent** — add the attribute so the editor keeps the sibling attached and can't strip it:
   ```csharp
   [RequireComponent(typeof(Health))]
   public class Pawn : NetworkBehaviour
   ```
   (Consistent with `TriggerHurt`'s `[RequireComponent(typeof(Collider))]`.)
2. **Fail loudly if it still happens** — a lost sibling on a live prefab should be one loud failure,
   not silent forwarding into NREs. In `Awake`, after logging, disable the component so nothing reads
   a null facade:
   ```csharp
   if (_health == null)
   {
       Debug.LogError($"{nameof(Pawn)} requires a sibling Health component.", this);
       enabled = false;
       return;
   }
   ```
   Optionally destroy/deactivate the GameObject instead if a pawn with no Health is never valid — but
   `enabled = false` is the least surprising and stops the facade from being called.

**Proof.** This is a defensive/authoring guard, not networked behaviour — an EditMode test that adds
a bare `Pawn` to a GameObject with no `Health`, runs `Awake` (or the test harness equivalent), and
asserts `pawn.enabled == false` and no exception on a subsequent facade read. Low ceremony; verifies
the "fail once" contract.

---

## E. Dead `DamageProcessor.Resolve(int)` overload

**Finding 7 (cleanup).** `Assets/Scripts/Damage/Core/DamageProcessor.cs:15`

The single-arg `Resolve(int)` is only exercised by `DamageProcessorTests`; the live caller
(`HitResolver`) uses `Resolve(int, float)`. `Resolve(x) == Resolve(x, 1f)` — a redundant second way
to do the same thing.

**Fix.** Delete the single-arg overload; update `DamageProcessorTests` to call `Resolve(x, 1f)` (or
delete the now-redundant test case if it only asserted the identity). Confirm no other caller first
(`grep -rn "Resolve(" Assets/Scripts`).

**Proof.** Compile + existing EditMode `DamageProcessorTests` pass after the test update. No new test.

---

## F. Lost cross-peer hit/death VFX broadcast — **design decision, do last**

**Finding 3.** `Assets/Scripts/Combat/Health.cs:114`

The old `Pawn.ServerTakeDamage` broadcast damage to all clients via an `ObserversTakeDamage`
ObserversRpc. The new `Health` path only invokes `OnDamageTaken`/`OnDeath` **in-process, where
`TakeDamage` is called** — and the standard projectile-hit path
(`ServerCollision.ServerTick` → `HitResolver` → `damageable.TakeDamage`) runs **server-only**. So:

- **Dedicated Linux server:** no client ever sees hit-spark or death-burst particles.
- **Host:** only the host sees them; remote players see fatal hits with no impact/death VFX (only
  the SyncVar-driven `PawnColor` flash survives).

The class doc claims `OnDamageTaken` "fires on EVERY peer" — but that only holds if `TakeDamage` is
*invoked* on every peer. For the server-only projectile path it isn't, so the claim is currently
aspirational. **The doc and the code disagree; F reconciles them.**

**This is the deep one — resolve the design before coding.** Candidate directions (interview through
`fishnet-conventions` + `engineering-canon` first):

1. **Replicate the damage event via an ObserversRpc inside `Health.TakeDamage`** — a server-side
   `TakeDamage` fan-outs an RPC that raises `OnDamageTaken`/`OnDeath` on observers, so client VFX
   subscribers fire. Closest to the old behaviour; re-establishes the "every peer" contract. Watch
   for double-fire on the host (the predictive local invoke + the RPC) — reuse the C-style
   `IsServerInitialized`/owner gating.
2. **Make collision predictive on clients** — clients run the hit path locally too, so `TakeDamage`
   is genuinely invoked on every peer as the doc claims. Larger change; interacts with FishNet
   prediction and the `which-peer-simulates` rule.
3. **Drive VFX off SyncVar `OnChange`** — subscribe VFX to `CurrentHealth.OnChange` instead of the
   C# events, so replication carries them. Simplest, but loses the *predictive* (before-SyncVar)
   timing the events exist to provide, and can't distinguish damage cause/attacker.

**Recommendation:** option 1 (ObserversRpc in `Health`) most directly restores the lost behaviour and
keeps predictive timing, but it re-introduces the double-fire-on-host hazard that C fixes — do C
first, then design F on top of it. Do **not** improvise; put it through the canon interview and record
the outcome as an ADR if it settles a networking pattern (`DESIGN.md` ADR index).

**Proof.** PlayMode test with a client peer (if the combat rig supports one): deal a server-side
lethal hit, assert the client's `pawn.OnDamageTaken` / `OnDeath` (or the VFX spawn) fires. Without a
second peer, at minimum assert the host still fires each once (guards against a regression from the
RPC wiring). Every networked change gets a PlayMode test — this one especially.

---

## Sequencing & verification

1. **A, B, E** — independent, mechanical. Land together or separately.
2. **C** then **D** — both `Health`/`Pawn` lifecycle; C's host double-fire test also exercises the
   path F will build on.
3. **F last**, after its design interview — depends on C being in place.

Run the full EditMode + PlayMode suite via **`unity-validator`** after each group (never poll the
test XML from the main thread). The C, F (and ideally B) tests should be written to **fail first**
against current `main`, proving they catch the regression before the fix lands.
