# ADR 0005 — The hit pipeline is a layered model: one typed fan-out message + per-datum controllers

- **Status:** Accepted (2026-07-19)
- **Date:** 2026-07-19 (runtime proof 2026-07-18; design settled + canon-interviewed 2026-07-19)

## Context

The hit pipeline ran through `HitResolver` — a hand-rolled harvest with its own channel guard and comment-enforced non-reentrancy; every new exchange edited the resolver, and each datum's deal/receive halves lived in two components (`KnockbackDealer`/`KnockbackReceiver`, `HitstunDealer`/`HitstunReceiver`) plus a per-datum `Arena.*.Core` processor. Einar directed a restructure around a **layered model** with merged per-datum **controllers**, proven by re-passing the incumbent's PlayMode guarantees. This ADR records the *validated* design, written against the shipped code.

## Decision

**(a) Two idioms, one criterion.** A fan-out context earns a pooled, typed `ExecuteEvents` message **only at all four clauses: ≥2 unrelated responders, on the dispatch target's own GameObject, on the same peer, served synchronously.** A single-receiver operation is a direct `GetComponent` + method call — a message with a known singular receiver is dispatch theater. Cross-peer fan-out rides a replicated `SyncVar.OnChange` (ADR 0004), never a message; off-hierarchy fan-out stays a C# event or a `MatchMediator` wire. "Hit" is the one qualifying context: `HitMessage` (one pooled type, one channel-guard functor, no dispatch class), dispatched by `HitMessage.Dispatch` to every `IHitMessageHandler` **on the source**.

**(b) Layers are vocabulary, not a depth budget.** Layer 1 = the context at its site; layer 2 = handlers (may `GetComponent` and call public methods); layer 3 = the operations they call (`ApplyKnockback`, `ApplyHitstun`, `TakeDamage`). Any layer may dispatch a further context. Analyzability comes from the *typed message* — grep every site and implementor — not from depth arithmetic.

**(c) Per-datum `<X>Controller`s — a binding, predictive rule.** A `<X>Controller` **(1) owns state and (2) exposes an API to modify it**. `KnockbackController`/`HitstunController` each merge their old dealer+receiver pair and inline their old core arithmetic; `Health` → `HealthController`. The rule earns its keep by **excluding**: `Pawn<X>` simulation shells keep the `Pawn<X>`/`<X>Processor` pairing (per-tick sim state, not dispatch-model state-owners), and `NetworkUIController` is thereby known-misnamed (rename pending, out of slice scope).

**(d) The atomic hit sequence lives inline in `HitMessage.Dispatch`:** durability spent first (a live `HealthController` on the source is the switch), then every handler runs unconditionally in component order, channel-filtered by the functor — authored order can never change a hit's outcome, and a mid-hit projectile death cannot retract a handler. **Extraction trigger:** a second hit source (explosion, cast) *extracts* this sequence to a shared home, never duplicates it.

## Alternatives rejected

- **Keep the incumbent `HitResolver` harvest** — every new exchange edits the resolver; the pay-down here is deleting it.
- **Messages everywhere** (victim-side single-receiver message types) — a known singular receiver is a direct call; 4 message types died in the plan's own first draft.
- **Split dealer/receiver components per datum** — no single home per datum; the merge gives one.
- **A damage/DamageTaken fan-out context** (declined on evidence) — `CurrentHealth.OnChange` is already a cross-peer, engine-native dispatcher at the correct site (ADR 0004); wrapping it reaches 0-of-4 cosmetic responders (they sit on child GOs, while `ExecuteEvents` is GameObject-scoped) for zero offset. **General lesson: `SyncVar.OnChange` IS an engine-native cross-peer dispatcher — you cannot beat it by wrapping it.**
- **A blanket `XController` rename** — (c) is a definition with exclusions, not a suffix applied on sight.
- **A runtime re-entrancy depth-guard, now** — deferred, not adopted; see Consequences.

## Evidence

- **Pay-down receipt (measured):** `HitResolver`, `HitContext`, `IHitHandler`, the four dealer/receiver components, `InstantiableObjects`, and the Knockback/Hitstun/Damage `Arena.*.Core` asmdefs + processors + EditMode tests deleted — net ≈ 940 lines; the model itself also shrank at review (the depth bound and R1 were deleted, not added).
- **The four-clause criterion is calibrated, not asserted:** across all 12 multi-responder contexts, the one-clause form disagreed with the honest one on 4 of the 5 it admits. Decisive case: `PawnManager.OnPawnKilled` satisfies it exactly, yet promoting it silently breaks the client-side ragdoll (`PawnBodySpawner` subscribes in `OnStartClient`, the event is raised in a `[Server]` method — peer-disjoint). The same-peer and same-GameObject clauses catch exactly that.
- **Guarantees pinned by PlayMode:** the incumbent's suite passes unmodified against the new pipeline (EditMode 240/240, PlayMode 55/55), plus two new pins: `HitDispatchOrderTests` (permuted handler order → identical outcome) and `HitAtomicityTests` (durability-1 bullet: damage + stun + knockback all land and the bullet despawns).

## Consequences

- **`HitMessage` carries only the two context GameObjects (`Source`/`Victim`), the `Instigator`, and hit geometry** — every component a handler needs it looks up itself, so no pooled component ref can go stale (the plan's component fields were removed, dissolving the fields-null-on-a-blast concern).
- **Ricochet + Deflect are consolidated into `RedirectionController`** — one component branching on whether the victim resolves to a `NetworkedMotion` (mover → adopt its heading; surface → reflect across the normal). No per-channel config struct (nothing varies yet).
- **Owner-contributed on-hit behaviour is a spawn-time self-clone, not a hit-time harvest** (`IOwnedObjectContribution` via `SpawnContributionReceiver`), so `HitMessage.Dispatch` never resolves the ability system. Dispatching to the owner instead would fire the pawn root's dealer-capable controllers as double-dealers; filtering re-couples dispatch to `PawnPassiveAbilities`.
- **The re-entrancy cycle-guard is deferred debt, not shipped** — no handler dispatches a nested hit today, so the guard is machinery for an unexercised path (rule-of-two). **Repayment trigger:** the first feature that dispatches a nested hit ships the depth-guard and its test with it.
- **Corrects ADR 0008's kept-cores list.** ADR 0008 listed `HitstunProcessor`, `KnockbackHelper`, `DeflectMath`, `RicochetMath`, and `DamageProcessor` as *kept* determinism-critical cores; H1 inlined them because they are **not** re-run on reconcile — the arithmetic resolves server-side and ships only the result (a TargetRpc'd impulse, a `StunnedUntilTick` SyncVar), unlike the genuinely-reconciled `MovementProcessor`. *Supersedes ADR 0008's kept-list for those five cores only; the component-primary default and the determinism-critical/branchy exception stand.* *Correction (2026-07-23): only `HitstunProcessor`/`KnockbackHelper`/`DamageProcessor` were inlined — `DeflectMath` and `RicochetMath` remain live motion cores in `Assets/Scripts/NetworkedMotion/Core/`, used by `RedirectionController` and pinned by `MotionMathTests`.*
- **Named pay-down target left unaddressed:** by this ADR's own criterion, `LocalUIEvents` is 14 channels of single-receiver dispatch (13 with one subscriber, 1 dead) — the largest target the criterion names, not touched here.

**North-star rationale.** *Elegance:* a hand-rolled harvest + two components per datum + three Core asmdefs collapse into one engine-maintained dispatch + one controller per datum; the doctrine shrank at review. *Essence:* the controllers' modifier registries are the seam every future upgrade-interaction (damage buffs, resistances) folds into, without new hit plumbing.
