# Phase: Passive-ability refactor — shared registry, applicator passives, player-held references

**Status:** storage-core scaffolding landed **inert on main** (`09e9c68d`/`33ff569a`) — the new shapes compile and `NetworkPlayer.PassiveLoadout` is wired, but nothing grants into them and `PassiveRegistry` does not exist yet; the **old** per-player-`NetworkObject` system is still the live one. **Re-scoped 2026-07-20 after H1 landed (ADR 0005): the remaining work is a grant-path CUTOVER + delete-old, not a build-the-hit-side job — H1 already shipped the projectile-side owner-handler plumbing this plan's old step 2 set out to build.** · **ADR:** pending (registry + applicator model), owed before "done". · **Sequenced in:** [master-execution-dag.md](master-execution-dag.md) — the **PR** thread.

## Goal

Retire the per-passive-per-player `NetworkObject` model (derive-from-children membership + corpse-window hacks; no foreign read). A passive KIND is authored **once** as a child of a shared registry; every owner holds a **reference** to it. Ownership on the player (persistent, foreign-HUD-readable, per-game persistence); execution on the pawn/projectile.

## Design

- **`PassiveRegistry`** — a root `NetworkObject`, spawned once per match (wired in `MatchMediator`), globally observed. Its children are the passive kinds.
- **A passive is an APPLICATOR, not the behaviour.** `abstract Passive : MonoBehaviour` with `Apply(GameObject target)` (create/configure a logic-component on the target — the passive knows which components in the target's hierarchy to touch), `CanApplyTo(GameObject)`, and `TryApply`. A KIND = a registry child composed of one or more `Passive` components; identity (name/icon) is a `PassiveInfo` component. The installed logic-component may be an `IHitHandler`; the passive itself is not one.
- **`PlayerPassiveLoadout`** (on `NetworkPlayer`) — `SyncList<NetworkObject>` of registry-child references (native FishNet resolution, no id-registry). Grant = add ref; remove = remove; **steal = move a ref**. `persistAcrossDeath` knob = the single per-game/per-life home. Foreign HUD resolves a ref → `PassiveInfo`.
- **Application is target-driven and one-shot** (no clear/reapply/teardown — pooling deferred): a target applies its owner's owned passives at spawn (pawn → `PawnPassiveLoadout`; projectile → `SpawnContributionReceiver`, the shipped seam); a mid-life grant applies the new kind to the owner's current pawn (via the loadout's `OnChange` Add).
- **Configure vs create:** buff passives CONFIGURE (movement/weapon modifier lists; projectile idempotent per-spawn seams). Behaviour passives (steal, instantiate) CREATE (`AddComponent` a logic-`IHitHandler` onto the projectile). *(2026-07-20: the create-side already SHIPS post-H1 — `IOwnedObjectContribution` implementors (`PassiveStealController`, `RedirectionController`, `InstantiationController`) self-clone onto the spawned projectile via `SpawnContributionReceiver`, found thereafter by ordinary `IHitMessageHandler` presence dispatch. What remains is re-expressing them as the shared-registry `Passive` applicator shape, not building the hit-side.)*
- **Doctrine:** logic stays in presence-dispatch components (ADR 0002/0003 upheld). The `Passive` applicator base uses inheritance in the accepted `PawnInput`-style polymorphic sense (tiny surface), not a moment-accreting god-base. The registry + applicator decisions get an ADR, canon-interviewed.

## Done (verified)

- **Storage core, compiling on the worktree editor (0 errors):** `Passive`(+`TryApply`), `PassiveInfo`, `PlayerPassiveLoadout` (`SyncList<NetworkObject>` + grant-time apply), `NetworkPlayer.PassiveLoadout` field, pawn-side `PawnPassiveLoadout` (applies owned at spawn), `MovementBuffPassive`, `WeaponBuffPassive`. Coexists with the old system (still compiles).
- **NetworkPlayer prefab wired** — `PlayerPassiveLoadout` on the root + the reference, verified against a fresh load and on disk.
- **Committed:** refactor guardrail `beee6c9a`; `unity-worktree-editor` agent `3cf496bc`.

## Remaining — a grant-path CUTOVER + delete-old (H1 already did the hit-side)

The new shapes are **inert scaffolding**; the job is to make them the live path and retire the old per-player-`NetworkObject` model. `PassiveRegistry` is the linchpin — nothing can reference registry children until it exists.

1. **Author the shared `PassiveRegistry` prefab** (Unity MCP, on the worktree editor) — a root `NetworkObject`, its children the passive KINDs (each = `PassiveInfo` + one or more `Passive` applicators). Wire it in `MatchMediator`, spawn once per match. Until this exists, `PlayerPassiveLoadout.Owned` (a `SyncList<NetworkObject>` of registry children) has nothing to point at.
2. **Cut the grant path to the new model:** `AbilityPickup` → `PlayerPassiveLoadout.Grant(registryChild)` (a ref add), not `PawnPassiveAbilities.AddPassive` → `AbilityGrant.Spawn`. Projectile CONFIGURE passives (`SpawnBuffPassive`) apply via `ownerLoadout.ApplyOwnedTo(projectile)` on spawn (the `SpawnContributionReceiver` seam).
3. **Cut steal to a ref-MOVE:** `PassiveStealController` already ships as a create-passive `IHitMessageHandler` (H1) — but it still steals via the OLD model (despawn the victim's `PassiveAbility` instance, spawn a fresh one under the attacker). Repoint it at `PlayerPassiveLoadout.Remove(kind)` + `.Grant(kind)` — a pure `SyncList` ref move, server-authoritative, no spawn.
4. **Delete the old shapes:** `PassiveAbility`, `PawnPassiveAbilities`, the `PassiveAbilityType` SO, and `AbilityGrant`'s passive path (keep it for actives). Rename the stale test `PassiveStealOnHitTests`.
5. **Update `CombatTestRig` / `AbilityTestRig`** grant helpers to reference registry children via the loadout.

**Struck 2026-07-20 — was step 2, "`HitResolver` drops its owner-walk (`CollectOwnerHandlers`); reconcile `ServerCollision`'s `OwnerHitChannelMask` gate":** H1 (ADR 0005) already DELETED `HitResolver`, `CollectOwnerHandlers`, and the `OwnerHitChannelMask` gate. Owner handlers now live on the projectile (self-cloned via `IOwnedObjectContribution` / `SpawnContributionReceiver`) and are reached by ordinary `IHitMessageHandler` presence dispatch — exactly the end-state this step wanted. Nothing to do.

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
