# Plan — spike features → real content

_Drafted 2026-07-15 from the DESIGN.md roadmap thread of the same name. Canon-interviewed; **not final until the two open questions below are settled.** Delete this file once the sessions have shipped and DESIGN.md reflects the outcomes (the same rule the roadmap applies to finished threads)._

Governing doctrine: [DESIGN.md](../../DESIGN.md) (essence, elegance, open questions), [ADR 0002](../design/0002-ieffect-appropriateness-verdict.md), [ADR 0003](../design/0003-hit-reactions-as-handler-components.md), and the `presence-dispatch` / `type-object-pattern` / `fishnet-conventions` skills.

## Premise corrections (scouted 2026-07-15)

Two facts the roadmap text predates:

1. **A passive-steal already shipped on main** as an on-hit handler — `PassiveStealOnHit` (`Assets/Scripts/Ability/PassiveStealOnHit.cs`) with two PlayMode tests. `PawnPassiveAbilities.RemovePassive`, kind-before-despawn resolution, and double-remove guards all exist. Roadmap item 2 is therefore only the **active, capsule-cast variant**.
2. **The spike worktrees are gone.** The `agent-*` worktrees named in the roadmap ("REFERENCE ONLY") no longer exist on disk or in any git ref; their learnings survive only in ADR 0002 and DESIGN.md prose.

Both belong in a DESIGN.md housekeeping edit (below).

---

## Session 1 — Proximity explosion (roadmap item 1)

First real consumer of the `HitResolver.ResolveExchanges` fan-out and the per-target `damageMultiplier` slot (`HitResolver.cs:111` — currently no caller passes a non-default value).

**Shape (standing doctrine, no new decisions):**

- A new **`ExplodeOnHit` handler component** implementing `IHitHandler` (ADR 0003 shape), authored on a **new projectile kind**: an independent prefab **copy** of `Bullet.prefab` (never a variant — FishNet PrefabId collision) + identity-only `ProjectileType` SO + `TypeObjectRoot` back-ref + Refresh Default Prefabs.
- `OnHit` runs server-side already (handlers fire from `ServerCollision`'s server tick — fishnet Rule 2 satisfied). It does `Physics.OverlapSphereNonAlloc` at `context.Point` against the Players mask, maps struck colliders to entity roots via the same `GetComponentInParent` walk the receivers use, **dedupes on resolved root**, and calls `HitResolver.ResolveExchanges` once per victim.
- The handler harvests **the projectile's own dealers** off `context.Source` — one `Damage` value is both direct-hit and blast-center damage; carrying the dealers means blast knockback/hitstun work for free, radial direction = center→victim.
- Per-victim `damageMultiplier` = **falloff**, computed by a pure Core helper (`BlastFalloffMath`: distance, radius → multiplier). **v1 is linear with a min clamp — no `AnimationCurve`, no wall occlusion, no per-channel radii.** That pin is deliberate (Brooks, below).

**Constraints:**

- The handler calls **only `ResolveExchanges`** — never `RunHandlers`/`ResolvePlayerHit`; HitResolver's static scratch list is not reentrant (`HitResolver.cs:67-69`). Deliberate consequence: blast hits fire exchanges but not other hit reactions (no recursive ricochet-on-blast).
- Blast knockback/hitstun apply **unscaled** (the multiplier scales only damage). Scalable/resistible knockback is explicitly the damage-buff channel's job — session 3, not here.
- `DamageType.Bullet` stays hardcoded in `ResolveExchanges` (`HitResolver.cs:126`). Named debt; trigger = the first consumer that distinguishes blast from bullet damage.

**Proof:**

- EditMode: `BlastFalloffMath` (distance/radius/clamp edges).
- PlayMode (combat harness, modeled on `HitstunKnockbackTests.cs`): shooter + two `SpawnCombatant` victims at different distances → both damaged; nearer takes more (`LastDamageAmount` per victim); **exactly one damage event per victim** (dedupe); direct-hit victim's total matches the double-dip decision (open question 1).

**Also in this session's commit — DESIGN.md housekeeping:** rescope item 2 to the active variant, delete the stale worktree-reference note, delete item 1 once shipped.

## Session 2 — Passive-steal active ability (item 2, corrected scope)

The steal mechanics exist and are tested; this session builds only the delivery. Two pieces:

1. **`IUseHandler` — the earned interface.** `ActiveAbility.ServerUse` is hard-typed to `GetComponents<SpawnNetworkObjects>` (`ActiveAbility.cs:80-82`), with a rule-of-two note anticipating exactly this moment. The steal is the second use-site behaviour, so the interface is now earned: `IUseHandler.OnUse(origin, direction, instigator)`, implemented by `SpawnNetworkObjects` and the new `StealPassiveOnUse`, discovered by the same `GetComponents` presence dispatch as hit handlers. **This is the session's Lehman offset** — a bespoke hard-typed dispatch collapses into the one mechanism the project already runs.
2. **`StealPassiveOnUse`:** server-side capsule-cast from the already-plumbed `aimSource` (`PawnActiveAbilities.cs:134-136`) against Players; resolve the victim `Pawn` via `GetComponentInParent` (the `AbilityPickup` idiom); run the steal core **extracted from `PassiveStealOnHit` into a shared method both variants call** — the guarantee the two steals can't drift. Authoring: new ability prefab copy + `AbilityType` SO, modeled on ScatterBurst.

Targeting mechanism is open question 2 (plain cast vs. routing through HitResolver).

**Tunable (decided, not interview-worthy):** a use is consumed on activation whether or not the cast lands — consistent with ScatterBurst's semantics.

**Proof:** PlayMode, modeled on `PassiveStealOnHitTests.cs` (already the two-pawn roster-mutation shape) + `AbilityInventoryPlayModeTests` for grant/use: caster facing victim-with-passive → use → passive counts swap sides and the modifier fold follows; miss case → rosters unchanged, use consumed.

## Session 3 — the damage-buff channel design session (item 3)

**Not an implementation session** — DESIGN.md is explicit: do not bolt it onto another feature. Deliverable is **ADR 0004**. Its agenda, as inherited from sessions 1–2:

- The per-target `damageMultiplier` will have a real consumer (falloff) — the contributor story must fit around it.
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

### 2. Does the steal cast route through HitResolver? (session 2 — Parnas lens, module ownership)

The `presence-dispatch` skill says target discovery at apply time ("an explosion over N victims, a cast") resolves through HitResolver. But the steal fires **no exchange and no hit reaction** — HitResolver would contribute nothing but its scratch-list hazards, and `AbilityPickup` already establishes that non-hit target discovery (overlap → `Pawn`) lives outside it.

**Recommendation: plain server capsule-cast + shared steal core**, reading the skill's sentence as governing *hit-dealing* discovery. **The real question underneath:** should the steal BE a hit — trigger the victim's hit reactions, stack with damage/knockback later? If yes, the HitResolver route becomes right, and the steal active turns into a dealer-carrying cast.

Either way, sharpen the skill's sentence afterward (proactive-skill-maintenance norm).

---

## Canon interview record (register checked 2026-07-15)

- **Parnas** — "change the falloff shape" touches one Core file; "change what an explosion is" touches one component. Holds.
- **Constantine** — `damageMultiplier` is data coupling (a folded scalar, not a mode flag); `IUseHandler` removes the one control-coupling smell in the path.
- **Brooks** (the register's standing second-system question for this phase) — both features are upgrade-driven system interactions from the roadmap, not embellishments; the embellishment risk concentrates in falloff generality, which v1 pins to linear/no-occlusion.
- **Lehman** — session 1 pays down DESIGN.md drift and converts the speculative `damageMultiplier` slot to earned; named debt: hardcoded `DamageType`. Session 2 deletes a bespoke dispatch (`IUseHandler` collapse) and de-duplicates the steal core.
- **Dijkstra / Conway** — each session is one head-sized module a validator can verify blind; falloff math is pure Core + EditMode.
