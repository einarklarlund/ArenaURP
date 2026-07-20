# Phase: Passive-ability refactor — shared registry, applicator passives, player-held references

**Branch:** `worktree-passive-loadout-refactor` · **Status:** storage core built + compiling; big rewire pass remaining · **ADR:** pending (registry + applicator model)

## Goal

Retire the per-passive-per-player `NetworkObject` model (derive-from-children membership + corpse-window hacks; no foreign read). A passive KIND is authored **once** as a child of a shared registry; every owner holds a **reference** to it. Ownership on the player (persistent, foreign-HUD-readable, per-game persistence); execution on the pawn/projectile.

## Design

- **`PassiveRegistry`** — a root `NetworkObject`, spawned once per match (wired in `MatchMediator`), globally observed. Its children are the passive kinds.
- **A passive is an APPLICATOR, not the behaviour.** `abstract Passive : MonoBehaviour` with `Apply(GameObject target)` (create/configure a logic-component on the target — the passive knows which components in the target's hierarchy to touch), `CanApplyTo(GameObject)`, and `TryApply`. A KIND = a registry child composed of one or more `Passive` components; identity (name/icon) is a `PassiveInfo` component. The installed logic-component may be an `IHitHandler`; the passive itself is not one.
- **`PlayerPassiveLoadout`** (on `NetworkPlayer`) — `SyncList<NetworkObject>` of registry-child references (native FishNet resolution, no id-registry). Grant = add ref; remove = remove; **steal = move a ref**. `persistAcrossDeath` knob = the single per-game/per-life home. Foreign HUD resolves a ref → `PassiveInfo`.
- **Application is target-driven and one-shot** (no clear/reapply/teardown — pooling deferred): a target applies its owner's owned passives at spawn (pawn → `PawnPassiveLoadout`; projectile → reworked `SpawnBuffReceiver`); a mid-life grant applies the new kind to the owner's current pawn (via the loadout's `OnChange` Add).
- **Configure vs create:** buff passives CONFIGURE (movement/weapon modifier lists; projectile idempotent per-spawn seams). Behaviour passives (steal, instantiate) CREATE (`AddComponent` a logic-`IHitHandler` onto the projectile).
- **Doctrine:** logic stays in presence-dispatch components (ADR 0002/0003 upheld). The `Passive` applicator base uses inheritance in the accepted `PawnInput`-style polymorphic sense (tiny surface), not a moment-accreting god-base. The registry + applicator decisions get an ADR, canon-interviewed.

## Done (verified)

- **Storage core, compiling on the worktree editor (0 errors):** `Passive`(+`TryApply`), `PassiveInfo`, `PlayerPassiveLoadout` (`SyncList<NetworkObject>` + grant-time apply), `NetworkPlayer.PassiveLoadout` field, pawn-side `PawnPassiveLoadout` (applies owned at spawn), `MovementBuffPassive`, `WeaponBuffPassive`. Coexists with the old system (still compiles).
- **NetworkPlayer prefab wired** — `PlayerPassiveLoadout` on the root + the reference, verified against a fresh load and on disk.
- **Committed:** refactor guardrail `beee6c9a`; `unity-worktree-editor` agent `3cf496bc`.

## Remaining (one pass — won't compile until whole)

1. `SpawnBuffPassive` (projectile seams) + projectile-side apply: rework `SpawnBuffReceiver` → `ownerLoadout.ApplyOwnedTo(projectile)` on spawn.
2. Behaviour passives: **steal** + **instantiate** as create-passives (`AddComponent` the `IHitHandler`; NO teardown per the pooling deferral). `HitResolver` then **drops its owner-walk** (`CollectOwnerHandlers`) — the handlers are on the projectile, found by the existing presence dispatch. Reconcile `ServerCollision`'s `OwnerHitChannelMask` gate.
3. Author the shared **`PassiveRegistry`** prefab (Unity MCP, on the worktree editor) + the grant-side kind→child resolution (`AbilityPickup`, test rigs reference registry children).
4. Delete old shapes: `PassiveAbility`, `PawnPassiveAbilities`, `PassiveStealOnHit`, the `PassiveAbilityType` SO; drop `AbilityGrant`'s passive path (keep for actives).
5. Update `CombatTestRig` / `AbilityTestRig` grant helpers.

## Verification

PlayMode, 2-peer (→ `unity-validator`): **foreign read** (peer B resolves A's loadout refs → child identity); **buff regression** (movement/weapon intact, no per-player passive spawns); **steal** (an owner projectile hitting a pawn moves a ref victim→attacker, server-authoritative); **persistence** (loadout survives respawn, per-game). Then the ADR + reconcile the `presence-dispatch` / `type-object-pattern` skills.

## Deferred (must graduate to ADR / DESIGN.md open questions before "done")

- **Pooling teardown for create-passives** — `AddComponent` leaks across pooled projectile reuse; no teardown now.
- **Removal cleanup** — a stolen *pawn* buff lingers until respawn (no clear/reapply).
- **Stateful passives** ("every Nth hit") — no per-owner state home under any shared-kind model.
- **Active-ability redesign** — its own thread; actives carry live `UsesRemaining` (not a field move).

## Workflow artifacts settled alongside (already committed)

- **Refactor guardrail** (re-derive-don't-patch + measure the part-count at plan-finalization) — `habit-check` hook branch + `engineering-canon` protocol step 7 + register.
- **`unity-worktree-editor` agent + memory norm** — the interactive MCP/editor targets the MAIN checkout, so asset writes from a worktree silently hit the wrong code; prompt before worktree editor work, use the agent to open one on the worktree.
