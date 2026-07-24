# ADR 0009 — Passives = shared-registry KINDS + component-coupled applicators + player-held references

- **Status:** Accepted (2026-07-20)
- **Date:** 2026-07-20

## Context

Passives were per-passive-per-player `NetworkObject` instances parented under the owner's pawn: membership was
derived from the live child instances, which forced a corpse-window `leaving:` exclusion into every fold; ownership
on the pawn meant a passive did not survive respawn and a foreign player could not read an enemy's loadout; stealing
meant despawn-then-spawn across two pawns. Acknowledged tech debt. H1 (ADR 0005) had already moved the *hit-side* to
presence-dispatch (handlers self-cloned onto the owner's spawned objects), so the remaining problem was the
ownership + grant model, not the hit exchange.

## Decision

A passive **KIND** is authored **once** as a child of a shared `PassiveRegistry` (one root `NetworkObject` per
match, wired in `MatchMediator`, observed on every peer). Owners hold **references** to the KIND, never copies:

- **`PlayerPassiveLoadout`** on `NetworkPlayer` holds a `SyncList<NetworkObject>` of registry-child refs (native
  FishNet resolution — no id→asset registry). Ownership on the **player**: it survives respawn (a
  `persistAcrossDeath` knob), and a foreign HUD resolves a ref → `PassiveInfo`. Grant = add a ref; remove = remove;
  **steal = move a ref** across two loadouts — a pure server-authoritative `SyncList` transfer, no spawn/despawn.
- **A passive is an APPLICATOR coupled to the ONE component it affects, not to the lifecycle event that triggers
  it:** `abstract Passive : MonoBehaviour` with `CanApplyTo`/`Apply`/`TryApply`; each concrete passive finds and
  configures one component (`MovementPassive`→`PawnMovement`, `SpeedPassive`→`NetworkedMotion`, …). A KIND = a
  registry child composing a `PassiveInfo` (identity) + one or more `Passive` components + any
  `IOwnedObjectContribution` create-passive controllers.
- **Application is target-driven and one-shot:** a pawn applies its owner's passives at spawn, a spawned object at
  spawn (`SpawnContributionReceiver`), a mid-life grant via the loadout's `OnChange` Add — all through
  `ApplyOwnedTo(target)`. `CanApplyTo` (component presence) decides which passives act on a target, so a pawn buff
  no-ops on a bullet with no target-type branching.
- **Naming carries no polarity** — a multiplier can be `<1`, so `SpeedPassive`, not `SpeedBuffPassive`.

## Alternatives rejected

- **One passive folding a `SpawnModifier` into every projectile seam:** coupled the passive to the *spawn event*,
  reached into four unrelated components, and made composition inexpressible.
- **Fold-then-set-once in `SpawnContributionReceiver`:** a second application mechanism beside `ApplyOwnedTo`;
  instead the per-spawn multiplier seams gained read-getters so passives compose through the one path
  (`FoldSpawnBuffs` deleted).
- **An identity-only kind SO for the pickup→KIND mapping:** registry children are runtime `NetworkObject`s an
  authored pickup cannot ref directly; name lookup (`PassiveRegistry.ResolveByName`) is the lightest stable key.
  Cheap to break — reintroduce a kind SO if a name proves too fragile.
- **Collapse `IOwnedObjectContribution` into `Passive`:** deferred — create-passives need the teardown tracking a
  void `Apply` cannot report; kept as a separate clone-with-teardown loop until a teardown-carrying applicator shape
  is designed.

## Consequences

- **Deleted:** `PassiveAbility`, `PawnPassiveAbilities`, the `PassiveAbilityType` SO, `AbilityGrant`'s passive use,
  the derive-from-children membership + corpse-window machinery, and the steal-by-spawn path. **Added:**
  `PassiveRegistry`, four projectile passives, four behaviour-inert read-getters. Part-count nets **down**.
- **Authoring (crosses the GUID boundary — worktree editor):** a production `PassiveRegistry` prefab carrying the
  passive KINDs, wired into `MatchMediator`; `NetworkPlayer` carries `PlayerPassiveLoadout`, the pawn
  `PawnPassiveLoadout`; `AbilityPickup`s name a KIND.
- **Verification:** 2-peer PlayMode — foreign loadout read, buff regression, server-authoritative steal, persistence
  across respawn.
- **Deferred (open questions, must graduate before the theme is "done"):** pooling teardown for create-passives
  applied as generic passives; removal cleanup (a stolen *pawn* buff lingers until respawn); a per-owner state home
  for stateful passives under a shared-KIND model; active-ability redesign.

**North-star rationale.** *Elegance:* one ownership mechanism (a `SyncList` of shared refs) and one application
mechanism (`ApplyOwnedTo`) replace per-player copies + derive-from-children + corpse hacks; composition, not new
types, expresses new KINDs. *Essence:* passives become first-class, foreign-readable, persistent, stealable
character state — with steal reduced to moving a reference.
