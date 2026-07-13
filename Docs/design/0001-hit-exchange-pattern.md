# ADR 0001 — Hit exchanges are dealer component + receiver component + courier effect

- **Status:** Accepted
- **Date:** 2026-07-10
- **Supersedes:** —
- **Superseded in part by [0002](0002-ieffect-appropriateness-verdict.md)** — the courier effects are replaced by presence-dispatch at the site; the dealer/receiver components, both-end modifier folds, and receiver-owned cross-peer application stand.

## Context

Damage already worked as an `IEffect` (`DealDamage` reads a `Damage` component on the projectile and calls `TakeDamage` on the struck `IDamageable`). We wanted knockback and hitstun to work "the same way as damage," each carrying its own tunable magnitude, with modifiers on both the dealing and receiving ends. Knockback previously lived in `PawnKnockback`, which subscribed to the pawn's damage event and computed an impulse from config baked into the *receiver* — so every damage source knocked back identically and knockback was not per-weapon tunable. Hitstun did not exist.

Constraint: this is a FishNet server-authoritative game. Hit effects run server-only, but a human pawn's movement and firing are simulated on the *owning client*; a bot's on the server.

## Decision

A "deal X on hit" mechanic is three parts:

1. A **dealer** component on the dealing entity (the projectile): authored baseline + a public modifier list + an `Effective*()` fold. `Damage`, `KnockbackDealer`, `HitstunDealer`.
2. A **receiver** component on the struck entity's ROOT (beside `Health`): its own resistance modifier list + fold. `KnockbackReceiver`, `HitstunReceiver`.
3. A **courier** `IEffect` typed to a capability composite (`IKnockbackExchangeContext`, `IHitstunExchangeContext`) — no config, no arithmetic. It reads the dealer's effective value and hands it to the receiver.

`ServerCollision` resolves both ends once (dealers cached in `Awake`; receivers via `GetComponentInParent` on the struck object) and passes them through the context. Each end folds only its own modifiers.

The receiver owns cross-peer application: `KnockbackReceiver.ApplyImpulse` is `[Server]` and target-RPCs the owner when one exists (else applies locally, for bots); `HitstunReceiver` publishes a ServerOnly `SyncVar<uint>` expiry *tick*, which the owning client's `WeaponProcessor` reads to gate its own predicted firing.

## Alternatives rejected

- **Config as `[SerializeField]` on the effect** (the literal reading of "duration saved on a `DealHitstun` effect"). Rejected: an effect authored on a *passive* prefab is contributed to projectiles it has never seen (owner contributions), so a serialized value describes the wrong entity. Also can't carry a modifier list ("both ends") without becoming a component anyway.
- **Effect does `GetComponent` on `ctx.Source`.** Fixes the wrong-entity bug but reaches the *source* (the projectile is itself), not the struck target — and the context deliberately carries no handle to the struck GameObject, only resolved system handles. Reintroduces the null-gamble the capability design removed (a mis-authored effect becomes a silent no-op instead of an authoring impossibility).
- **Receiver interfaces (`IKnockbackReceiver`, `IHitstunReceiver`).** Built, then deleted: one concretion each. The capability interface belongs on the *context*, not the component ([[no-speculative-interfaces]]). Concrete `KnockbackReceiver` is handed over directly, exactly as `IMotionContext` hands over a concrete `NetworkedMotion`.
- **Keep knockback on the damage event.** Rejected: couples knockback to damage (a zero-damage shove is impossible), bakes magnitude into the receiver (not per-weapon tunable), and — the latent bug it was hiding — never actually applied for human pawns on a dedicated server, because the server-side impulse landed on a peer whose `Update` early-outs.

## Consequences

- **Easy:** a new "deal X on hit" copies the shape; the three exchanges compose freely (shove without damage, stun without shove); balance lives on the prefab.
- **Hard / deferred:** the *dealing-end* modifier lists exist but have no contributors — the damage-buff channel is still an open question (see DESIGN.md). Adding a receiver now requires deciding which peer simulates it first (documented as `fishnet-conventions` Rule 4b).
- **Doctrine produced:** `effect-commands` skill (the pattern), `fishnet-conventions` Rule 4b (which-peer-simulates), `integration-testing` gate 5 (positive controls — a test asserting *absence* of an effect passed over a prefab with the feature entirely absent).
