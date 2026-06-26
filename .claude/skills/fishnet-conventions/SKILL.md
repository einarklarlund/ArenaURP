---
name: fishnet-conventions
description: FishNet networking conventions for this server-authoritative Unity arena shooter — when to send data over the wire and (more importantly) when NOT to. Covers the payload-avoidance rule (derive state on the server instead of shipping it; the owner-lookup pattern), server-authoritative cast-based collision, syncing ScriptableObject identity as a database index via a custom serializer, and the rule that every networked change gets a PlayMode test. Biased for a SOLO dev on FishNet (Tugboat/CanoeWebRTC) using the Processor + thin-MonoBehaviour pattern. Use this BEFORE adding a SyncVar/SyncList/RPC, extending a spawn payload, syncing an asset reference, writing collision/pickup/projectile detection, or any server-authoritative gameplay mutation.
---

# FishNet conventions (curated for a solo, server-authoritative Unity dev)

Server-authoritative means the **server is the single source of truth** for gameplay state; clients send
intent and observe synced results. The cheapest networked system is the one that sends the least. These
rules keep this codebase fast, deterministic, and debuggable.

## Rule 1 — Don't send what the server can already derive (payload avoidance)

**Before adding a field to a spawn payload, a SyncVar, or an RPC argument, ask: can the receiver compute
or look this up from state it already has?** If yes, don't send it.

The decisive case in this codebase is **projectile effects**. A projectile carries the bare minimum on
its spawn payload — owner id (on `NetPlayerOwnership.WritePayload`), spawn tick + origin pose + motion
config (on `NetworkedMotion.WritePayload`). It does **not** carry the effects/modifiers its owner's
abilities contribute. Instead, collisions are resolved **server-side**, and there `ServerCollision`
**looks up its owner** (owner id → player via `NetworkPlayerManager` → `ControlledPawn` →
`IProjectileEffectProvider.GetReactions()`) and runs the owner's reactions alongside the type's. The
owner's loadout already lives on the server; shipping it on every bullet would be bytes spent to send
state the authoritative peer can read for free. (This used to route through `ProjectileWorldProvider`,
since deleted — `ServerCollision` resolves the owner itself now.)

The pattern, generalized:
- **Identity in, state looked-up.** Send a small stable id (owner id, a database index). Resolve the rich
  state from it on the server.
- This only works because the consuming code runs in ONE authoritative place (the server). If you were
  tempted to send the data "so clients can do X," first check whether X needs to happen on clients at all
  — server-authoritative effects (damage/spawn/debuff) don't.

Smell: you're widening a `WritePayload`/RPC signature to carry gameplay config that the owning player's
components already hold. Stop — resolve it server-side from an id instead.

(Closed-form motion config — speed/accel/gravity/etc. that every peer needs to render the trajectory — is
a deliberate, eyes-open **exception**. It currently rides the `NetworkedMotion` spawn payload (a handful
of floats per spawn), set by the type's `Initialize`, because we chose to make `NetworkedMotion`
**type-agnostic** (no `ProjectileType` ref) and all kinds share one prefab — so it can't resolve the
config locally. The cheaper alternative is to bake the config on a **per-type prefab** (then every peer
has it for free and it drops off the payload); take that the day kinds get distinct prefabs. The point
stands: prefer resolving locally; if you must send, know why — here it bought a fully-generic component.)

## Rule 2 — Collision/detection is server-authoritative and cast-based, not trigger callbacks

Resolve "did X touch Y" on the **server tick** with an explicit physics query, not `OnTriggerEnter`/
`OnCollisionEnter` (which fire on every peer off client-side physics and aren't authoritative).

- Projectiles: `ServerCollision.ServerTick` runs only on the server, sweeps a SphereCast against the world
  and an OverlapSphere against other projectiles (the physics is inline now that `ProjectileWorldProvider`
  is gone), maps colliders to entities via `GetComponentInParent<Pawn>`/`<NetworkedMotion>`, then runs the
  hit reactions. No `[Replicate]`/`[Reconcile]` — motion is closed-form/deterministic so only the server
  needs to tick collisions.
- Pickups follow the same shape: `AbilityPickup` subscribes `TimeManager.OnTick` in `OnStartServer`, does
  a server-side `Physics.OverlapSphereNonAlloc`, and grants on overlap. It deliberately does NOT use
  `OnTriggerEnter`.

When you add anything that detects contact, mirror this: server tick + overlap/cast + `GetComponentInParent`.

## Rule 3 — You cannot sync a ScriptableObject reference; sync a database index

FishNet serializes NetworkBehaviour/NetworkObject references (by ObjectId) and primitives/enums — NOT
arbitrary `UnityEngine.Object`/ScriptableObject references. To sync "which AbilityType / WeaponData":

1. Put the assets in a registry SO indexed by list position (`AbilityDatabase`, `WeaponDatabase`), loaded
   from `Resources` so every peer has the same instance and indices line up.
2. Write a custom FishNet serializer that writes the index and resolves it back:
   `AbilityTypeSerializer.WriteAbilityType`/`ReadAbilityType`. FishNet's codegen auto-discovers
   `Write*`/`Read*` extension methods on `Writer`/`Reader` and uses them for `SyncList<T>`, struct fields,
   and RPC args. Then you can keep an ergonomic `SyncList<AbilityType>`.
3. Adding a new asset means adding it to the database (or the index won't resolve on other peers).

## Rule 4 — SyncVar/SyncList write on the server; clients send intent via [ServerRpc]

- Declare synced state `public readonly SyncVar<T> X = new(default, new SyncTypeSettings(WritePermission.ServerOnly))`
  / `SyncList<T>`. Mutate only inside `[Server]` methods.
- Clients request changes through `[ServerRpc]` (e.g. `PawnAbilities.ServerCycleSelected`/`ServerUseSelected`);
  the server validates and writes the SyncVar; clients react via `.OnChange`.
- `SyncList.OnChange` signature here: `(SyncListOperation op, int index, T oldItem, T newItem, bool asServer)`.
- Extract the body of a `[ServerRpc]` into a plain `[Server]` method when a test/rig needs to invoke the
  real logic (calling an RPC directly is awkward) — see `PawnAbilities.ServerUse`.

## Rule 5 — Every networked change gets a PlayMode test

Networked behaviour (anything touching SyncVar/SyncList/RPC, spawn payloads, server ticks, collision,
ownership) cannot be proven by EditMode alone — the bug lives in the live FishNet host. **Whenever you
change networked behaviour, cover it with a PlayMode test** via the `NetworkedPlayModeTestFixture`
(single-host) + an editor-only `*TestRig` reached by reflection (Assembly-CSharp is unreferenceable from
the test asmdef). Keep the deterministic decision logic in a pure Core processor (EditMode-tested) and let
the PlayMode test cover only the live-engine seam. See the [[integration-testing]] skill for the
EditMode-vs-PlayMode split and how to keep these cheap; this rule is the non-negotiable floor for
networked code.

## Quick checklist

- Adding a payload/SyncVar/RPC field? → Can the server derive it from an id? (Rule 1)
- Detecting contact? → server tick + cast, not a trigger. (Rule 2)
- Syncing an asset? → database index + custom serializer. (Rule 3)
- Mutating gameplay state? → `[Server]`, with clients sending `[ServerRpc]` intent. (Rule 4)
- Changed any of the above? → write the PlayMode test. (Rule 5)
