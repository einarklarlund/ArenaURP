# ADR 0009 — Passives = shared-registry KINDS + component-coupled applicators + player-held references

- **Status:** Accepted (2026-07-20)
- **Date:** 2026-07-20

## Context

Passive abilities were per-passive-per-player `NetworkObject` instances: granting spawned a `PassiveAbility` prefab
copy and parented it under the owner's pawn (`AbilityGrant.Spawn`); membership was *derived from the live child
instances* (`PawnPassiveAbilities.GetPassives` filtered on `IsSpawned`), which forced a corpse-window hack (a
just-despawned instance stays enumerable for the rest of the frame, so every fold had to pass a `leaving:` exclusion).
Ownership lived on the pawn, so a passive did not survive respawn without extra machinery and a foreign player could
not read an enemy's loadout (no shared identity to resolve). Stealing meant despawn-then-spawn across two pawns. The
system was acknowledged tech debt ([[passives-convoluted-refactor-later]]).

H1 (ADR 0005) had already moved the *hit-side* of a passive to presence-dispatch: on-hit reactions are
`IHitMessageHandler` components self-cloned onto the owner's projectiles via `SpawnContributionReceiver` /
`IOwnedObjectContribution`. So the remaining problem was the *ownership + grant* model, not the hit exchange.

## Decision

A passive **KIND** is authored **once** as a child of a shared `PassiveRegistry` (a root `NetworkObject` spawned
once per match, wired in `MatchMediator`, observed on every peer). Every owner holds a **reference** to the KIND,
not a copy:

- **`PlayerPassiveLoadout`** on `NetworkPlayer` holds a `SyncList<NetworkObject>` of registry-child refs (native
  FishNet resolution — no id→asset registry). Ownership on the **player**: it survives respawn (a `persistAcrossDeath`
  knob is the single per-game/per-life home) and a foreign HUD resolves a ref → `PassiveInfo`. Grant = add a ref;
  remove = remove; **steal = move a ref** across two loadouts (a pure `SyncList` transfer, server-authoritative — no
  spawn/despawn).
- **A passive is an APPLICATOR coupled to the ONE component it affects, not to the lifecycle event that triggers it.**
  `abstract Passive : MonoBehaviour` with `CanApplyTo(GameObject)` / `Apply(GameObject)` / `TryApply`. Each concrete
  passive finds and configures **one** component: `MovementPassive`→`PawnMovement`, `WeaponPassive`→`PawnWeapon`,
  `SpeedPassive`→`NetworkedMotion`, `ScalePassive`→`SpawnScale`, `CollisionRadiusPassive`→`ServerCollision`,
  `MaxHealthPassive`→`HealthController`. A KIND = a registry child composed of a `PassiveInfo` (identity) + one or more
  `Passive` components (a "fast, big bullet" composes `SpeedPassive` + `ScalePassive` + `CollisionRadiusPassive`), and
  any `IOwnedObjectContribution` create-passive controllers.
- **Application is target-driven and one-shot** (pooling teardown deferred, below): a pawn applies its owner's owned
  passives at spawn (`PawnPassiveLoadout` → `ApplyOwnedTo(pawn)`); a spawned object applies them at spawn
  (`SpawnContributionReceiver` → `ApplyOwnedTo(projectile)`); a mid-life grant applies the new KIND to the current
  pawn via the loadout's `OnChange` Add. Which passives act on a given target is decided by `CanApplyTo` (component
  presence) — so a pawn buff no-ops on a bullet and vice-versa, with no target-type branching.
- **Naming carries no polarity.** A `Passive`'s multiplier can be `<1`, so the type is `SpeedPassive`, not
  `SpeedBuffPassive` — the same shape expresses buffs and debuffs.

## Alternatives rejected

- **One passive that folds a `SpawnModifier` into every projectile seam (the first `SpawnBuffPassive` draft).**
  Rejected: it coupled the passive to the *spawn event* and reached into four unrelated components. It broke the
  established `MovementPassive`/`WeaponPassive` shape (one passive ↔ one affected component) and made composition
  ("just the collision radius, not the visual") inexpressible. Coupling to the component, not the lifecycle, keeps
  each passive tiny and composable — the presence-dispatch discipline applied to applicators.
- **Route projectile on-spawn buffs through a fold-then-set-once in `SpawnContributionReceiver`** (mirroring the old
  `FoldSpawnBuffs`). Rejected: it keeps a second application mechanism beside `ApplyOwnedTo`. Instead the per-spawn
  multiplier seams gained a read-getter (`SpeedMultiplier`, `ScaleMultiplier`, `RadiusMultiplier`,
  `MaxHealthMultiplier`) so a passive **composes** — `SetSpeedMultiplier(SpeedMultiplier * m)` — and several owned
  speed passives multiply through the *same* `ApplyOwnedTo` path the pawn buffs use. The setter stays last-write-wins;
  composition lives in the applicator. One apply mechanism, `FoldSpawnBuffs` deleted.
- **An identity-only kind SO (à la `AbilityType`) for the pickup→KIND mapping.** Considered (matches the type-object
  doctrine) but rejected for **naive child-name lookup** (`PassiveRegistry.ResolveByName`, quiet warning on a miss):
  registry children are runtime `NetworkObject`s an authored pickup cannot ref directly, and a name is the lightest
  stable key that needs no parallel asset. (Cheap to break — reintroduce a kind SO if a name proves too fragile.)
- **Collapse `IOwnedObjectContribution` into `Passive`.** Deferred: a create-passive `AddComponent`s a handler onto a
  pooled projectile and must be torn down on despawn; the receiver tracks exactly the components it added
  (`_contributed`), which a void `Passive.Apply` cannot report. Kept as a separate clone-with-teardown loop, its
  source swapped from `PawnPassiveAbilities` to the loadout's owned children. Revisit when a teardown-carrying
  applicator shape is designed.

## Consequences

- **Deleted:** `PassiveAbility`, `PawnPassiveAbilities`, the `PassiveAbilityType` SO, and `AbilityGrant`'s passive use
  (kept for actives) — with them the derive-from-children membership + corpse-window `leaving:` machinery and the
  steal-by-spawn path. Part-count nets **down**.
- **Added:** `PassiveRegistry`, the four projectile passives, and four read-getters on existing seams (behaviour-inert).
- **Prefab/content authoring (crosses the GUID boundary — worktree editor):** a production `PassiveRegistry` prefab
  whose children are the passive KINDs (Swift / Big Bullets / Bouncy Ball), wired into `MatchMediator`; the
  `NetworkPlayer` prefab carries `PlayerPassiveLoadout`, the pawn prefab `PawnPassiveLoadout`; `AbilityPickup`s name a
  KIND. The PlayMode passive suite (steal, on-spawn buff, inventory) re-authors its test KINDs under a test registry.
- **Verification:** PlayMode 2-peer — foreign read (peer B resolves A's loadout refs → identity), buff regression
  (movement/weapon intact, no per-player passive spawns), steal (an owner projectile moves a ref victim→attacker,
  server-authoritative), persistence (loadout survives respawn, per-game).
- **Deferred (open questions, must graduate before the theme is "done"):** pooling teardown for create-passives
  applied as generic passives; removal cleanup (a stolen *pawn* buff lingers until respawn — no clear/reapply);
  stateful passives ("every Nth hit") have no per-owner state home under a shared-KIND model; active-ability redesign
  is its own thread.

**North-star rationale.** *Elegance:* one ownership mechanism (a `SyncList` of shared refs) replaces per-player
`NetworkObject` copies + derive-from-children + corpse hacks; one application mechanism (`ApplyOwnedTo`) covers pawn
and projectile buffs; each passive couples to one component, so composition — not new types — expresses new KINDs.
*Essence:* passives become first-class, foreign-readable, persistent, stealable character state — the ability↔ability
interaction the game is built on — with steal reduced to moving a reference.
