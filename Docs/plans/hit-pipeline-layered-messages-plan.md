> Status: PLANNED — design settled 2026-07-16, not yet implemented. This file is the editable source of truth for the slice; a future session executes it.

# Migrate the hit slice to merged controllers + typed messages over ExecuteEvents

## Context

Einar decided to restructure cross-component gameplay communication around per-datum **controllers** and Unity Messaging system. Fan-out contexts are ExecuteEvents messages, single-receiver operations are bounded direct calls. This creates a layered model that helps to define contexts of cross-component gameplay elements (such as a Hit). This plan executes **slice one**: the hit pipeline (knockback / damage / hitstun), at production quality, with the incumbent's guarantees re-proven by PlayMode tests. It includes **merged controllers** (KnockbackController/HitstunController own both deal and receive halves; Health stays the hit-point controller, Damage the outgoing-damage component — different data, no merge), **distributed exchange orchestration** (dealers respond to one Hit message as layer-2 handlers; HitResolver is deleted entirely — no dispatch class), **IHitHandler migrates now**, **no victim-side message types** (delivery = layer-2 direct calls into layer-3 operations).

## Binding design rules — THE LAYERED MODEL (Einar's, settled at plan review)

**Two idioms, one criterion:**
- **Fan-out context** (many arbitrary systems respond to one event, open responder set) → a **message** over ExecuteEvents. "Hit" is the archetype: 5 unrelated responders. Promotion criterion (R2): a context earns a message at **≥2 unrelated responders**; when an operation's responders multiply, promote it then.
- **Operation** (one component's capability, many callers) → **direct coupling: GetComponent + public method call, bounded by the 3-layer rule.** A message with a known singular receiver is a misuse of dispatch. Damage/knockback/hitstun *delivery to the victim* are operations, NOT messages.

**The 3-layer depth bound (per context):**
- Layer 1: the context (HitMessage), dispatched at the site.
- Layer 2: handlers (IHitMessageHandler implementors). Handlers MAY GetComponent (own GO or others, incl. GetComponentInParent on the victim) and call public methods.
- Layer 3: those public operations (ApplyImpulse, ApplyHitstun, TakeDamage) may mutate their own state and **contribute to data surfaces** (collections, wired refs used as data sinks — Family A) — they may NEVER discover-and-invoke another component's behaviour (R1: terminates in data, not just "no GetComponent"; serialized-ref behaviour chains are equally banned). Violation smell: a public method that is both handler-called AND GetComponents-and-calls → redesign (promote to a context or demote its callee to data).

**Carried rules:**
1. A message/operation input feeds a **recompute**, never an in-place edit of an effective value. Controllers keep (authored baseline, registries); removal = delete + recompute; `ResetState` clears registries, never baselines.
2. Consume-once data integrates at the **tick boundary** — `PawnMovement.PendingImpulses` stays a PUBLIC Family-A contribution surface (PawnMovement is now UNTOUCHED by this migration); `ApplyImpulse` ends in `movement.PendingImpulses.Add(...)`, a legal layer-3 data contribution.
3. `HitMessage` derives `BaseEventData`, `base(null)` (safe — verified in ugui 2.0.0 source), **pooled via static Get/Release on HitMessage itself** (only one pooled type — MessagePool<T> failed the inline rule), `Reset()` nulls every ref, Release immediately after dispatch; `HitMessage.Functor` statically cached with the channel guard baked in.
4. All gameplay mutation originates `[Server]`; the KnockbackController TargetRpc hop to the owning client (client-simulated movement) is the sanctioned delivery leg, identical to today.
5. Hit atomicity by construction: durability spent FIRST in `ServerCollision.DispatchHit`, then every handler runs unconditionally (even on a dead projectile); death gates only despawn/projectile-channel afterwards. Victim-side delivery is plain synchronous layer-2/3 calls — same proof status as today. ExecuteEvents' per-handler try/catch strengthens this.
6. Disabled component = silent (ExecuteEvents checks `isActiveAndEnabled`) — this IS the composability/strip hook; `ResetState` re-enables (`enabled = true`). NOTE: ExecuteHierarchy is no longer used anywhere (victim resolution = GetComponentInParent) — the reentrancy risk and its pins are DELETED from this plan.

## Verified ground truth

- ExecuteEvents (ugui 2.0.0, read from PackageCache): delivery = component order on the GO; disabled components SKIPPED (behavior change vs old harvest — audited, nothing ships disabled handlers); per-handler try/catch; `ExecuteHierarchy` walks up, stops at first GO with an enabled matching handler (== `GetComponentInParent` semantics for root receivers).
- Assembly-CSharp auto-references ugui — no asmdef changes except `Arena.PlayTests.asmdef` gains `UnityEngine.UI` for the pin tests. **No `Arena.*.Core` asmdef is touched; all Core processors/tests unchanged.**
- All six pawn prefab variants (Bot/Human/Debug/TestPawn/TestPawnTemperature/TestCombatant) inherit Pawn Root's receivers with zero overrides → surgery on **Bullet + Pawn Root only** propagates everywhere.
- FishNet refresh menu: `Tools/Fish-Networking/Utility/Refresh Default Prefabs`. Working tree has pre-existing noise — prefab-save git checks are per-path (`git status --short -- <prefab>`), differential vs Phase-0 snapshot.
- Only `ServerCollision` calls `HitResolver`; only `KnockbackReceiver` writes `PendingImpulses`; only `PawnMovement` reads/clears it. `PawnWeapon.hitstun` (serialized) reads `IsStunned` by pull — stays pull.

## New types (all Assembly-CSharp, new folder `Assets/Scripts/Combat/Messages/`)

- `HitMessage : BaseEventData` — fields mirror `HitContext` (Channel, Source, Instigator, Victim=struck collider GO, Point, Normal, Direction, SourceCenterPoint, Motion, SelfHealth, OtherMotion); `Init(...)`; `Reset()` nulls all; **static Stack pool (`Get`/`Release`) on the class**; **static `Functor` with the channel guard baked in** (`Supported & Enabled & Channel != 0`) — the guard lives ONLY here; handler bodies are unconditional.
- `IHitMessageHandler : IEventSystemHandler` — `SupportedChannels`/`EnabledChannels`/`OnHit(HitMessage)`. No abstract base. **No dispatch class, no victim-side message types** (the layered model: victim delivery is a layer-2 direct call). The hit sequence lives on ServerCollision (below).

## Merged controllers

**`KnockbackController : NetworkBehaviour, IHitMessageHandler`** (`Assets/Scripts/Pawn/Knockback/KnockbackController.cs`) — replaces KnockbackDealer + KnockbackReceiver:
- Serialized: `KnockbackConfig config` (baseline, never mutated), `PawnMovement movement` (receiver-side data sink; null on dealer-only bullets — old Awake hard-error dropped, delivery early-outs).
- Runtime registries: `OutgoingModifiers` (dealer; future damage-buff seam), `IncomingModifiers` (resistance).
- Dealer half (layer 2): `SupportedChannels = EnabledChannels = HitChannels.Player` (no serialized knob — nothing varies per kind); `OnHit` → `KnockbackHelper.ResolveImpulse(hit.Direction, in config, OutgoingModifiers)` → `hit.Victim.GetComponentInParent<KnockbackController>()?.ApplyImpulse(impulse)` — layer-2 direct call, exactly today's resolution walk.
- Receiver half (layer 3): `[Server] ApplyImpulse` folds `KnockbackHelper.ResolveIncoming` → `Owner.IsValid ? [TargetRpc] TargetApplyImpulse : AddImpulse` → both end in `movement.PendingImpulses.Add(resolved)` (verbatim from KnockbackReceiver — a legal layer-3 data contribution, R1).
- `ResetState`: clear both registries, `enabled = true`.

**`HitstunController : NetworkBehaviour, IHitMessageHandler`** (`Assets/Scripts/Pawn/Hitstun/HitstunController.cs`) — same shape: dealer half `OnHit` → `HitstunProcessor.ResolveDuration` → `GetComponentInParent<HitstunController>()?.ApplyHitstun(seconds)`; receiver half `[Server] ApplyHitstun` verbatim (ResolveIncoming → EndTick → ExtendTo never-shorten); SyncVar `StunnedUntilTick` (ServerOnly, conditional reset in OnStartServer to minimize dormant cost on bullets); `IsStunned` pull consumption unchanged.

**`Damage`** gains the dealer response (`IHitMessageHandler`, Player-only, stays a plain MonoBehaviour): `OnHit` → `DamageProcessor.Resolve(_amount, 1f)` → `DamageInfo` → `hit.Victim.GetComponentInParent<Health>()` (via the existing `IDamageable` walk) `?.TakeDamage(info)`. **`Health` is UNTOUCHED** (no new interface). `IDamageable` + contact-damage callers (HurtBox/TriggerHurt) untouched.

**`PawnMovement` is UNTOUCHED**: `PendingImpulses` stays the public Family-A contribution surface; `Modifiers` untouched.

**Reaction handlers** (`RicochetHandler`, `DeflectProjectileHandler`, `InstantiateOnHitHandler`, `PassiveStealOnHit`): interface swap `IHitHandler`→`IHitMessageHandler`, `OnHit(in HitContext)`→`OnHit(HitMessage)`; serialized `enabledChannels` fields keep names — prefab data survives.

**`ServerCollision`**: drop `_damage/_knockback/_hitstun` caches; Awake mask fold retypes to `IHitMessageHandler`; both internal channels (world-hit path, projectile-overlap path) converge on pooled `HitMessage` → **`[Server] public void DispatchHit(HitMessage hit)`** → Release. `DispatchHit` IS the atomic hit sequence: (1) durability spend first (`hit.SelfHealth?.ServerLose(1)` — presence stays the immortality switch), (2) `ExecuteEvents.Execute(hit.Source, hit, HitMessage.Functor)`, (3) `Execute` at each owner passive instance GO (`PawnPassiveAbilities.GetPassives`, IsSpawned-filtered; `CollectHitHandlers` deleted). Public so the test rig's synthetic-hit probe calls it directly. Guard/dedupe, `IsDead()` post-hit gating, pooled despawn unchanged. **Extraction trigger (recorded in the skill + ADR): a second hit source (explosion over N victims, a cast) EXTRACTS this sequence to a shared home — never duplicates it.**

## File inventory

**New** (4 source files): `Assets/Scripts/Combat/Messages/{HitMessage,IHitMessageHandler}.cs` (HitMessage carries its own pool + channel-guard functor; no dispatch class, no victim-side messages); `Assets/Scripts/Pawn/Knockback/KnockbackController.cs`; `Assets/Scripts/Pawn/Hitstun/HitstunController.cs`; `Docs/design/0005-hit-pipeline-layered-messages.md`; tests `Assets/Tests/PlayMode/Combat/{HitDispatchOrderTests,HitAtomicityTests,MutualHitTests}.cs`, `Assets/Tests/PlayMode/ExecuteEventsPinTests.cs`.

**Modified**: `ServerCollision.cs`, `Damage.cs`, `PawnWeapon.cs` (field type → HitstunController + rewire), `PawnPassiveAbilities.cs` (delete CollectHitHandlers, retype mask fold), 4 reaction handlers, `CombatTestRig.cs`, `CombatPlayModeTestFixture.cs` (new probe wrappers only), `Arena.PlayTests.asmdef` (+UnityEngine.UI), skills (`presence-dispatch`, `cross-component-communication`), `shared-understandings.md`, prefabs `Bullet.prefab` + `Pawn Root.prefab`, `DefaultPrefabObjects.asset` (regenerated). **NOT modified: `Health.cs`, `PawnMovement.cs`.**

**Deleted** (+.meta): `HitResolver.cs`, `HitContext.cs`, `IHitHandler.cs`, `KnockbackDealer.cs`, `HitstunDealer.cs`, `KnockbackReceiver.cs`, `HitstunReceiver.cs`.

**Untouched**: every `Arena.*.Core`, `HitChannels` values, `IDamageable`, SpawnBuffReceiver multiplier seams.

## Step sequence (compile-green each step; suite-green at checkpoints)

- **Phase 0**: worktree (EnterWorktree; headless validation per unity-worktree memory if needed — else validate via live editor). Baseline: run EditMode+PlayMode (delegate to unity-validator); snapshot `git status --short`.
- **Phase 1 (additive, zero behavior change)**: add the 4 new source files. Nothing references them. **Checkpoint: suites green.**
- **Phase 2 (atomic cutover)**: (2) reaction handlers interface swap → (3) Damage/PawnWeapon/PawnPassiveAbilities edits → (4) ServerCollision gains `DispatchHit` + cutover; delete HitResolver/HitContext/IHitHandler → (5) CombatTestRig updates → (6) **prefab surgery BEFORE source deletion** (below) → (7) delete the four old component files → (8) FishNet Refresh Default Prefabs (`unity_execute_menu_item`) + `unity_search_missing_references` sweep + console check for PrefabId/AssetPathHash errors. **Checkpoint: full suites green.**
- **Phase 3**: PlayTests asmdef + ExecuteEventsPinTests; rig probes (`SyntheticHitOutcome(reverseOrder)`, `NestedDispatchPoolProbe`) + fixture wrappers; the three new PlayMode test files. **Checkpoint: suites green incl. new.**
- **Phase 4**: ADR 0005, register line, skill updates. Commits per commit-conventions: gameplay slice / tests / docs+skills+.claude separated; NO Claude co-author; **no push — Einar pushes/merges himself (memory: never-push, merge-on-approval).**

### Prefab surgery (Unity MCP only)

**Bullet**: add KnockbackController (config.SpeedOnHit=7, MinimumAngleOnHit=35; movement null) + HitstunController (config.Seconds=0.2); remove KnockbackDealer, HitstunDealer; verify via `unity_component_get_properties` + `git status --short -- <path>` shows ` M` (SaveAsPrefabAsset silent-revert gate; on miss: re-run after reimport, or `PrefabUtility.SavePrefabAsset` + `SetDirty` via `unity_execute_code`).
**Pawn Root**: add KnockbackController on root, `unity_component_set_reference` movement → Movement child's PawnMovement; add HitstunController on root; rewire PawnWeapon.hitstun → root HitstunController (field retype nulls the old ref); remove KnockbackReceiver, HitstunReceiver; verify refs + git status; `unity_prefab_info` sweep over the six variants (expect clean inheritance).
Modifiers lists are runtime-only — nothing to migrate. Place controllers after Damage for readability; correctness must not depend on order (pinned).

## Test plan

**Keep green** (no body edits expected): EditMode Knockback/Hitstun/Weapon/Movement/Damage core tests + KnockbackMovementIntegrationTests (cores untouched); PlayMode HitstunKnockbackTests (6), BotShootsEnemyTests, HealthDeathEventTests, DamageVfxTests, PassiveStealOnHitTests, OwnerProjectileEffectTests (the live pin on owner-passive dispatch), ProjectileRicochetTests.

**Rig updates** (fixture reflection names stay stable): Knockback/Hitstun resolvers → controllers; Set*Resistance → IncomingModifiers; `StripHitstunDealersFromLiveProjectiles` keeps its name — now `FindObjectsByType<HitstunController>` filtered to projectiles (`GetComponent<NetworkedMotion>() != null` — controllers live on pawns too!), `enabled = false` (Destroy of a NetworkBehaviour is unsafe under FishNet); delete unused `PendingImpulseCount`.

**New tests**: (1) `HitDispatchOrderTests` — `SyntheticHitOutcome(false)` == `SyntheticHitOutcome(true)` field-for-field (permutes Bullet handler order via ComponentUtility; calls the Bullet's `ServerCollision.DispatchHit` on a built HitMessage). (2) `HitAtomicityTests` — durability-1 bullet with Damage+both controllers+Ricochet: damage AND stun AND displacement all land, bullet despawns (durability spent first ⇒ handlers ran on a dead projectile). (3) `MutualHitTests` — facing pair fire simultaneously; both damaged, stunned, displaced apart (HitMessage pool integrity under interleaved hits). (4) `ExecuteEventsPinTests` — disabled component silent / inactive GO silent / delivery order = component order / `NestedDispatchPoolProbe` clean (nested Hit dispatch — e.g. a reaction handler causing a synchronous second hit — never aliases a live pooled HitMessage). ExecuteHierarchy pins DELETED (no longer used).

## ADR 0005 (skeleton in Docs/design/0005-hit-pipeline-layered-messages.md)

**The decision IS the layered model**: (a) the two-idiom criterion — fan-out context (≥2 unrelated responders) → ExecuteEvents message; single-receiver operation → layer-2 direct call; (b) the 3-layer depth bound — context → handlers (may GetComponent + call public operations) → operations (terminate in own state or data surfaces; never discover-and-invoke behaviour); (c) merged per-datum controllers; (d) the hit sequence inline in `ServerCollision.DispatchHit` with the named extraction trigger (second hit source extracts, never duplicates). Alternatives rejected: incumbent kept (unnamed behaviour-call exception; hand-rolled handler harvest; every new exchange edits the resolver); messages-everywhere (victim-side single-receiver messages are dispatch theater — 4 message types + a generic pool deleted from the plan's own first draft); split components (no single home per datum). Accepted trades: dormant halves on pooled bullets (conditional SyncVar reset, no ticks); disabled=silent as the strip hook; ugui as gameplay dependency; contact damage still on IDamageable (future slice). **Pay-down receipt: HitResolver + HitContext + IHitHandler + four components deleted; the behaviour-coupling exception is now a named, bounded structure.**

**Register append** (supersession named inline): `2026-07-16 · Constantine/Parnas — cross-component gameplay coupling follows the LAYERED MODEL: a fan-out context (≥2 unrelated responders) is a pooled ExecuteEvents message (guard in the one cached functor, [Server]-originated); a single-receiver operation is a layer-2 direct call (handler may GetComponent+call a public operation); operations terminate in data (own state or Family-A surfaces), never discover-and-invoke behaviour. Supersedes "never call a sibling's behaviour" (now: legal at layer 2 only, depth one) and the "stamp/control drift triggers redesign" line's application here (accretion accepted knowingly on the Hit context); ADR 0005.`

**Skill updates**: presence-dispatch (IHitMessageHandler, guard in `HitMessage.Functor`, hit sequence in `ServerCollision.DispatchHit` + extraction trigger; disabled=silent strip hook; order-independence pinned; "we own no dispatch machinery" now literally true — one message type + one pool, zero dispatch classes); cross-component-communication (**the governing rule is replaced by the layered model**: Family A/B survive as the layer-3 data terminals and notification family; hit-exchange paragraph → controllers with two registries, layer-2 calls; the old `ApplyImpulse` unnamed exception is retired by naming — it IS a layer-2→3 call; add the promotion criterion R2 and the violation smell).

## Risks (top; full mitigations inline above)

FishNet NB-layout change on spawnable prefabs → refresh + sweep + full PlayMode; SaveAsPrefabAsset silent revert → per-path git gates; pooled HitMessage corruption under nested dispatch → Reset-nulls-all + release-at-site + NestedDispatchPoolProbe; PawnWeapon ref nulled by retype → explicit rewire + existing stun-gate test; mid-slice red window → contained to Phase 2, compile-green throughout. (ExecuteHierarchy reentrancy risk DELETED — no longer used.)

## Verification

unity-validator runs EditMode+PlayMode at each checkpoint (Phases 0/1/2/3); prefab git-status gates after surgery; `unity_search_missing_references` sweep; FishNet console check; final full-suite run before commits. Success = all pre-existing tests green unmodified (except rig internals), 4 new test files green, the deleted-files receipt matches the ADR's pay-down claim.
