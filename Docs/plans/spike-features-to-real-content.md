# Plan — spike features → real content

_Drafted 2026-07-15 from the DESIGN.md roadmap thread of the same name. Canon-interviewed; **not final until the two open questions below are settled.** Delete this file once the sessions have shipped and DESIGN.md reflects the outcomes (the same rule the roadmap applies to finished threads)._

Governing doctrine: [DESIGN.md](../../DESIGN.md) (essence, elegance, open questions), [ADR 0002](../design/0002-ieffect-appropriateness-verdict.md), [ADR 0003](../design/0003-hit-reactions-as-handler-components.md) (**superseded in part by 0005**), [ADR 0005](../design/0005-hit-pipeline-layered-messages.md) (the layered hit model this plan now dispatches through), and the `presence-dispatch` / `type-object-pattern` / `fishnet-conventions` skills.

> **H1 reconciliation (2026-07-20).** This plan was drafted against the pre-H1 pipeline (`HitResolver.ResolveExchanges`, `IHitHandler`, the per-target `damageMultiplier` slot). **H1 landed as [ADR 0005](../design/0005-hit-pipeline-layered-messages.md)** and deleted all three. The hit-dispatch references below are reconciled to the shipped seams (`HitMessage.Dispatch`, `IHitMessageHandler`, the per-datum `*Controller`s). Two of the changes are **material, not cosmetic**, and are flagged `**[H1]**` inline where they land: (a) the per-target multiplier **slot is gone** — Session 1's falloff has no waiting field and must **re-establish** a per-target seam, which entangles it with Session 3 (see the [DAG](master-execution-dag.md)'s falsifiable check 5); (b) the old static-scratch non-reentrancy comment is now the **deferred cycle-guard** — ADR 0005 names the explosion (this session) as its repayment trigger, so **Session 1 ships the depth-guard + `ReentrancyGuardTests`** with it.

## Premise corrections (scouted 2026-07-15)

Two facts the roadmap text predates:

1. **A passive-steal already shipped on main** as an on-hit handler — `PassiveStealOnHit` (`Assets/Scripts/Ability/PassiveStealOnHit.cs`) with two PlayMode tests. `PawnPassiveAbilities.RemovePassive`, kind-before-despawn resolution, and double-remove guards all exist. Roadmap item 2 is therefore only the **active, capsule-cast variant**.
2. **The spike worktrees are gone.** The `agent-*` worktrees named in the roadmap ("REFERENCE ONLY") no longer exist on disk or in any git ref; their learnings survive only in ADR 0002 and DESIGN.md prose.

Both belong in a DESIGN.md housekeeping edit (below).

---

## Session 1 — Proximity explosion (roadmap item 1)

The named **extraction trigger** from [ADR 0005](../design/0005-hit-pipeline-layered-messages.md): the explosion is the *second hit source*, so it extracts the atomic hit sequence from `HitMessage.Dispatch` to a shared home rather than duplicating it. It is a **layer-2 dispatch** — a controller that fires a fresh `HitMessage` per victim.

**Shape (standing doctrine, no new decisions):**

- A new **`ExplosionController`** (an `IHitMessageHandler`, ADR 0005 shape; the `*Controller` suffix follows the hit pipeline's naming rule, as `RedirectionController` does), authored on a **new projectile kind**: an independent prefab **copy** of `Bullet.prefab` (never a variant — FishNet PrefabId collision) + identity-only `ProjectileType` SO + `TypeObjectRoot` back-ref + Refresh Default Prefabs.
- It runs server-side already (the source's handlers fire from the server tick — fishnet Rule 2 satisfied). It does `Physics.OverlapSphereNonAlloc` at the hit point against the Players mask, maps struck colliders to entity roots via the same `GetComponentInParent` walk the controllers use, **dedupes on resolved root**, and calls **`HitMessage.Dispatch` once per victim** — an explosion is **N dispatches of one message each** (per-victim radial `Direction`), not one message to N victims.
- Re-dispatching from the projectile source means **the source's own per-datum controllers deal for free** — the same `DamageController`/`KnockbackController`/`HitstunController` handlers that fire on a direct hit fire again for each blast victim, radial direction = center→victim. No dealer harvest and no component refs are carried on the message: `HitMessage` holds only `Source`/`Victim`/`Instigator` + hit geometry, and each handler looks its controllers up itself (ADR 0005 Consequences).
- **[H1]** Per-victim **falloff** is still a pure Core helper (`BlastFalloffMath`: distance, radius → multiplier; **v1 linear with a min clamp — no `AnimationCurve`, no wall occlusion, no per-channel radii**, a deliberate Brooks pin). **But there is no longer a `damageMultiplier` slot to feed** — the pre-H1 per-target multiplier died with `HitResolver`. So Session 1 must **re-establish a per-target seam** — the narrowest one that makes falloff work: have the dealing handler scale `DamageController.Amount` at layer 2 for the duration of the dispatch (a per-target field on `HitMessage` is the wider alternative). **Session 1 is NOT blocked on Session 3** — the DAG's ordering is Session 1 *first*, precisely because its falloff is the live consumer that becomes Session 3's evidence. Build the minimal local seam here; **Session 3 decides whether it generalizes into the damage-buff channel** (DAG check 5 counts the missing field as `(b) ≥ 1`, and Session 3 re-establishes it *wide* if the channel wants it). This is the doctrine — narrow existing seam now, don't unify prematurely.

**Constraints:**

- **[H1]** Nested dispatch is now legal (ADR 0005: *any layer may dispatch a further context*), so the blast can fire full `HitMessage`s — other hit reactions on a blast victim *do* run, unlike the old scratch-list carve-out. That re-opens the cycle risk (`X→Y→X`) the old static scratch list foreclosed by comment. ADR 0005 **defers the re-entrancy cycle-guard** and names *this session* as its repayment trigger: **Session 1 ships the runtime depth-guard + `ReentrancyGuardTests` in the same change that first makes cycles possible.**
- Blast knockback/hitstun apply **unscaled** (falloff scales only damage). Scalable/resistible knockback is explicitly the damage-buff channel's job — session 3, not here.
- `DamageType.Bullet` is now inlined in `DamageController.OnHit` (the arithmetic moved off `HitResolver`). Named debt; trigger = the first consumer that distinguishes blast from bullet damage.

**Proof:**

- EditMode: `BlastFalloffMath` (distance/radius/clamp edges).
- PlayMode (combat harness, modeled on `HitstunKnockbackTests.cs`): shooter + two `SpawnCombatant` victims at different distances → both damaged; nearer takes more (`LastDamageAmount` per victim); **exactly one damage event per victim** (dedupe); direct-hit victim's total matches the double-dip decision (open question 1).

**Also in this session's commit — DESIGN.md housekeeping:** rescope item 2 to the active variant, delete the stale worktree-reference note, delete item 1 once shipped.

## Session 2 — Passive-steal active ability (item 2, corrected scope)

The steal mechanics exist and are tested; this session builds only the delivery. Two pieces:

1. **`IUseHandler` — the earned interface.** `ActiveAbility.ServerUse` is hard-typed to `GetComponents<SpawnNetworkObjects>` (`ActiveAbility.cs:80-82`), with a rule-of-two note anticipating exactly this moment. The steal is the second use-site behaviour, so the interface is now earned: `IUseHandler.OnUse(origin, direction, instigator)`, implemented by `SpawnNetworkObjects` and the new `StealPassiveOnUse`, discovered by the same `GetComponents` presence dispatch as hit handlers. **This is the session's Lehman offset** — a bespoke hard-typed dispatch collapses into the one mechanism the project already runs.
2. **`StealPassiveOnUse`:** server-side capsule-cast from the already-plumbed `aimSource` (`PawnActiveAbilities.cs:134-136`) against Players; resolve the victim `Pawn` via `GetComponentInParent` (the `AbilityPickup` idiom); run the steal core **extracted from `PassiveStealOnHit` into a shared method both variants call** — the guarantee the two steals can't drift. Authoring: new ability prefab copy + `AbilityType` SO, modeled on ScatterBurst.

Targeting mechanism is open question 2 (plain cast vs. dispatching a `HitMessage` from the cast site).

**Tunable (decided, not interview-worthy):** a use is consumed on activation whether or not the cast lands — consistent with ScatterBurst's semantics.

**Proof:** PlayMode, modeled on `PassiveStealOnHitTests.cs` (already the two-pawn roster-mutation shape) + `AbilityInventoryPlayModeTests` for grant/use: caster facing victim-with-passive → use → passive counts swap sides and the modifier fold follows; miss case → rosters unchanged, use consumed.

## Session 3 — the damage-buff channel design session (item 3)

**Not an implementation session** — DESIGN.md is explicit: do not bolt it onto another feature. Deliverable is **the damage-buff channel ADR** (named by title; it claims the next free number when the file is written — 0005 went to H1, and numbers are no longer reserved in advance). Its agenda, as inherited from sessions 1–2:

- **[H1]** The per-target multiplier now has to be **re-established, not filled** — the `damageMultiplier` slot died with `HitResolver` (ADR 0005). Session 1's falloff is the real consumer that forces the shape (a per-target field on `HitMessage` vs. a layer-2 scale of `DamageController.Amount`); the contributor story must land that seam, not fit around a waiting one.
- The channel owes **owner-adds-flat-damage** (removed by the refactor, now inexpressible by design) and **spike 4's scalable/resistible knockback** (the dealing-end contributor IS this channel).
- Candidates on the table (DESIGN.md open question): an on-spawn damage multiplier folded like the other on-spawn buffs vs. a standing dealing-end contribution.
- Must also resolve the `presence-dispatch` skill's OPEN note — **buffs-as-data vs. presence-dispatch**: this channel is the named trigger for deciding whether the data/behaviour split collapses into one mechanism.

## Item 4 — movement-stealing projectile: stays last

Unchanged from DESIGN.md: it forces the timed-modifier lifetime and the wire-payload class, both gated on session 3's ADR. No plan here until then.

---

## Open questions (settle before the affected session; recommendations included)

### 1. Direct-hit double-dip (session 1 — Brooks lens, game feel)

Should the direct-hit victim **also** take blast damage?

**Recommendation: no** — exclude the direct victim's root from the fan-out; the direct exchange IS the multiplier-1.0 hit. Point-blank double damage is a balance oddity, and "one hit event per victim" keeps correctness obvious (and the dedupe test simple).

Same lens, smaller: **exclude the instigator's own pawn from the blast in v1.** Revisit if a rocket-jump-style movement upgrade ever wants self-knockback — that would be an essence-positive interaction.

### 2. Does the steal cast dispatch a `HitMessage`? (session 2 — game-design question, its architecture now settled)

**[H1] The architectural half is settled by ADR 0005's two idioms; only a game-design question remains.** If the steal is *not* a hit, it is plain non-hit target discovery (overlap → `Pawn`), the `AbilityPickup` idiom, outside the hit pipeline entirely. If the steal *is* a hit, it dispatches a `HitMessage` from the cast site — which is exactly the *second hit source* (a cast) that ADR 0005 names as the extraction trigger, so there is no module-ownership question left to interview: the idiom says where the code goes and what it costs.

**Recommendation: plain server capsule-cast + shared steal core** — the steal fires no exchange and no reaction, so it is not a hit. **The real question underneath is pure game design:** should stealing trigger the victim's hit reactions (and later stack with damage/knockback)? If yes, the `HitMessage` route becomes right and the steal active turns into a dealer-carrying cast. **Watch (from the DAG):** if this lands on "yes" *and* Session 1 ships first, the extraction already happened and the steal is the *third* hit source, not the second.

Either way, sharpen the `presence-dispatch` skill's sentence afterward (proactive-skill-maintenance norm).

---

## Canon interview record (register checked 2026-07-15)

- **Parnas** — "change the falloff shape" touches one Core file; "change what an explosion is" touches one component (`ExplosionController`). Holds.
- **Constantine** — the per-target falloff multiplier is data coupling (a folded scalar, not a mode flag); `IUseHandler` removes the one control-coupling smell in the path. **[H1]** post-H1 the multiplier has no field yet — Session 3 re-establishes it as data, preserving this reading.
- **Brooks** (the register's standing second-system question for this phase) — both features are upgrade-driven system interactions from the roadmap, not embellishments; the embellishment risk concentrates in falloff generality, which v1 pins to linear/no-occlusion.
- **Lehman** — **[H1]** session 1 no longer "converts a speculative `damageMultiplier` slot to earned" (that slot was deleted with `HitResolver`); it now *pays* to re-establish the per-target seam and ships the deferred re-entrancy cycle-guard the extraction makes reachable. Named debt: hardcoded `DamageType` (now in `DamageController.OnHit`). Session 2 deletes a bespoke dispatch (`IUseHandler` collapse) and de-duplicates the steal core.
- **Dijkstra / Conway** — each session is one head-sized module a validator can verify blind; falloff math is pure Core + EditMode.
