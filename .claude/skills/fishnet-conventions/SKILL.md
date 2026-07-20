---
name: fishnet-conventions
description: FishNet conventions for this server-authoritative arena shooter — when to send data over the wire and (more importantly) when NOT to, and which peer simulates what. Use BEFORE adding a SyncVar/SyncList/RPC, extending a spawn payload, syncing an asset reference, writing collision/pickup/contact detection, or any server-authoritative gameplay mutation. The floor: every networked change gets a PlayMode test.
---

# FishNet conventions

Server-authoritative: the **server is the single source of truth**; clients send intent and observe synced results. The cheapest networked system is the one that sends the least.

## Rule 1 — Don't send what the receiver can derive (payload avoidance)

Before adding a field to a spawn payload, SyncVar, or RPC: **can the receiver compute or look this up from state it already has?** Send a small stable id; resolve the rich state where it's consumed.

The decisive example is **projectile owner effects**: a bullet's payload carries only `SpawnOwnerId` + the per-spawn motion anchor. The owner's ability contributions are never sent — they resolve from replicated state: at SPAWN, `SpawnContributionReceiver` reads the owner's passives (owner id → `NetPlayerResolver` → `ControlledPawn` → `PawnPassiveAbilities`) and self-clones each `IOwnedObjectContribution`'s hit handler onto the bullet, so by hit time they are ordinary handlers on the source and dispatch stays ignorant of the ability system. The loadout already lives on the server; shipping it per bullet would be bytes spent on state the authoritative peer reads for free. Per-kind config (speed, radius, hit-reaction components) is even cheaper: it's authored on the prefab, so every peer has it locally — see [[type-object-pattern]]'s three data classes (per-kind → prefab, per-spawn anchor → payload, server-only → gated behaviour).

Smell: widening a `WritePayload`/RPC signature with gameplay config the owning player's components already hold.

## Rule 2 — Contact detection is a server tick + cast, never trigger callbacks

`OnTriggerEnter`/`OnCollisionEnter` fire on every peer off client physics and aren't authoritative. Resolve "did X touch Y" on the **server tick** with an explicit query, mapped to entities via `GetComponentInParent`:

- `ServerCollision.ServerTick` (subscribed in `OnStartServer`): SphereCast against the world along the strategy's sweep + OverlapSphere against projectiles.
- `AbilityPickup`: server-side `Physics.OverlapSphereNonAlloc` on `TimeManager.OnTick`.

Motion is closed-form/deterministic on every peer, so only the server ticks collision — no `[Replicate]`/`[Reconcile]` needed. Mirror this shape for anything new that detects contact.

## Rule 3 — You can't sync an asset reference; make identity travel as the instance

FishNet serializes NetworkObject/NetworkBehaviour refs and primitives — not ScriptableObject refs. So a synced asset identity never travels as the SO reference: **it travels as the spawned instance itself.** A type's prefab carries a `TypeObjectRoot` back-ref to its SO, which resolves locally on every peer — projectile and ability types work exactly this way, and since ADR 0007 so does weapon config (authored on the `PawnWeapon` prefab component). Spawn the thing and let its instance carry its identity; there is no type database or synced index anywhere in the codebase, and a new one would be a fresh design decision, not a default.

## Rule 4 — Server writes; clients send intent

- Synced state: `SyncVar<T>`/`SyncList<T>` with `WritePermission.ServerOnly`, mutated only in `[Server]` methods.
- Clients request via `[ServerRpc]`; the server validates and writes; clients react via `.OnChange`.
- Extract an RPC's body into a plain `[Server]` method when a test/rig needs to call the real logic.
- Initial-value timing (payload before `OnStartNetwork`, SyncVars applied then OnChange flushed after Initialize) is verified in the [[fishnet-initial-syncvar-onchange-timing]] memory — the foundation for "seed on server, self-heal on clients" designs.

## Rule 4b — Know WHICH PEER simulates the thing you are mutating

Server-authoritative does not mean server-simulated. Every `Pawn<X>.Update` early-outs on `!input.IsActive`, and:

- **Human pawn** → `HumanPawnInput.IsActive => IsOwner`, so movement and firing are simulated on the **owning client** (the root NetworkTransform is `clientAuthoritative`). The server never runs them.
- **Bot pawn** → `BotPawnInput.IsActive => IsServerInitialized`, so the **server** simulates.
- **Scripted test pawn** → same as a bot.

Hit effects, however, run **server-only** (`ServerCollision`). So a server-side write into a pawn's per-tick simulation can land on a peer that never ticks it. This was a real, silent bug: knockback pushed an impulse into `PawnMovement.PendingImpulses` on the server, where a human pawn's `Update` early-outs — the impulse was never consumed *and never cleared*. Knockback simply did not exist for human players on a dedicated server.

**A controller's receiver half owns which peer applies its effect.** `KnockbackController.ApplyKnockback` is `[Server]`: it target-RPCs the owner when `Owner.IsValid`, and applies locally otherwise (bots). `HitstunController` publishes a ServerOnly `SyncVar<uint>` **expiry tick** rather than a countdown, precisely because a countdown would have to be ticked in two different places; a tick is a value both peers evaluate against their own `TimeManager.Tick` and agree on. Without it, a stunned client keeps predicting shots the server rejects and the player watches bullets evaporate.

Before adding any receiver or server→pawn write: **name the peer that simulates it.**

## Rule 5 — Every networked change gets a PlayMode test

Anything touching SyncVar/SyncList/RPC, payloads, server ticks, collision, or ownership can only break on a live FishNet host. Cover it via `NetworkedPlayModeTestFixture` (single host; two-peer fixture for replication/divergence), with the deterministic decision logic still in a pure Core processor under EditMode. See [[integration-testing]].

## Checklist

- New payload/SyncVar/RPC field? → derivable from an id? (1)
- Contact? → server tick + cast. (2)
- Asset over the wire? → instance identity; there is no type database. (3)
- Gameplay mutation? → `[Server]`; client intent via `[ServerRpc]`. (4)
- Changed any of it? → PlayMode test. (5)
