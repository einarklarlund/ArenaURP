---
name: type-object-pattern
description: How a KIND of thing (projectile type, ability type) is structured here — an IDENTITY-ONLY ScriptableObject plus an independent prefab copy carrying ALL gameplay config; instances self-initialize on every peer. Use BEFORE creating or extending a type SO, adding a new kind (projectile/ability), deciding where a kind's config or a new per-kind value lives, spawning anything a player can own, granting/removing abilities, or touching pooled-instance state/ResetState.
---

# Type objects: identity-only SO + prefab-per-kind

A **kind** (a projectile type, an ability type) is two assets:

1. **The type SO — identity only.** `ProjectileType` is an empty `ScriptableTypeObject` (just `InstancePrefab`); `AbilityType` adds `DisplayName` + `Icon`. It exists so pickups, granting, and the HUD can name a kind and find its prefab — nothing else. `Initialize(GameObject)` is a no-op virtual hook kept for the rare type that must compose at runtime; by default it does nothing.
2. **The prefab — all the gameplay config**, authored on generic, type-agnostic components: a bullet's speed on `NetworkedMotion.InitialSpeed`, its motion strategy on `NetworkedMotion.Motion`, radius/lifetime/hit-effect lists on `ServerCollision`, durability on `Health.maxHealth`, damage on `Damage`. None of these components knows what kind it belongs to; heterogeneous kinds differ by which components (and which authored [[presence-dispatch]] lists) their prefab carries.

**Why identity-only won** (this reversed an earlier design where the SO pushed config into components): the prefab is knowledge every peer already has. Config authored on prefab components is replicated for free, previewable as one unit, runtime-tunable per instance, and pooling-safe — and it removed the SO→component `Configure()` plumbing entirely. The one back-ref is `TypeObjectRoot` on the prefab holding its SO (validated bidirectionally in `OnValidate`), so a spawned instance can resolve its own identity locally on any peer — which is why there is **no ability/projectile database and no index sync anywhere** — weapons now follow the same prefab-copy shape too (ADR 0007).

**Prefab-per-kind means independent prefab COPIES, never prefab variants.** FishNet variants inherit the base's `PrefabId` and collide; the Refresh Default Prefabs generator won't reassign a variant's id. Duplicate the prefab, then run Tools/Fish-Networking/Utility/Refresh Default Prefabs.

## The three data classes (the networking sub-rule)

1. **Per-kind config** (speed, radius, effect lists) → authored on the prefab; every peer reads it locally. **Nothing on the wire.**
2. **Per-spawn anchor** (spawn tick/origin/direction, owner id) → genuinely per-shot, so it rides the spawn payload: `NetworkedMotion.WritePayload` carries the anchor (self-derived in `OnStartNetwork` on the initiating peer), `NetPlayerOwnership.WritePayload` carries only `SpawnOwnerId`.
3. **Server-only runtime** (collision casting) → not sent, not flagged; the component gates itself (`OnStartServer` tick subscription).

Instances self-initialize on every peer from local data — the spawner configures nothing. That's what makes client-predicted spawn correct: the predicting client, the server, and every observer each build the same instance from the same prefab + payload.

## Spawning owned objects — the inline three-step

There is deliberately **no spawn factory**. Every spawn site does exactly:

```csharp
var nob = NetworkManager.GetPooledInstantiated(prefab, pos, rot, asServer);
nob.GetComponent<NetPlayerOwnership>().SpawnOwnerId = ownerPlayer.ObjectId;  // BEFORE Spawn — rides the payload
ServerManager.Spawn(nob /*, ownerConn for predicted */);
```

`SpawnOwnerId` must be stamped by the spawner — it can't be self-derived (bots have no owning connection, so `NetworkObject.Owner` won't stand in). Current sites: `PawnWeapon.ExecuteShot` (client-predicted: the firer's own NetworkManager, `asServer:false`) and the `SpawnNetworkObjects` effect (server, `asServer:true`). Keep new sites to that exact shape.

## Abilities: granting is spawning a child

Granting an ability = instantiate its kind's prefab, `NetworkObject.SetParent(pawn's NOB)` (never raw `transform.SetParent` — only SetParent replicates the hierarchy), then Spawn (`AbilityGrant.Spawn`). Gameplay state lives on the instance's `ActiveAbility`/`PassiveAbility` components (`UsesRemaining` is a SyncVar; effect lists are authored per kind on the prefab).

- **Actives** keep a `SyncList<ActiveAbility>` roster (slot order + HUD hooks). Ordering is load-bearing: **Spawn before Add, RemoveAt before Despawn** — an unspawned NetworkBehaviour ref serializes as permanent null, and removal-first keeps deltas off dying objects. Mutate the roster only through the facade's single add/remove paths.
- **Passives** have no roster — no individual-removal path, so membership derives from the replicated child instances; `PawnPassiveAbilities` re-derives modifiers and owner contributions on every change.

## Pooling: baseline vs per-spawn layer

Pooling never re-runs `Awake`, and `ResetState` must preserve the authored baseline while resetting everything a spawn-time contributor touched:

- **Baseline** (authored `InitialSpeed`, prefab scale, `maxHealth`, effect lists): never wiped in `ResetState`. Safe because prefab-per-kind fixes a pooled instance's kind forever.
- **Per-spawn layer** (buff multipliers, anchors, owner id, scratch buffers): reset to identity every despawn — `NetworkedMotion._speedMultiplier`, `Health._maxMultiplier`, `SpawnScale`, `SpawnBuffReceiver`, `NetPlayerOwnership.SpawnOwnerId`.

Rule of thumb: *any field a spawn-time contributor mutates gets reset in `ResetState`; never multiply onto the last spawn's value* (`effective = baseline × multiplier`, idempotent setters). A per-spawn transform mutation needs an owning component with its own `ResetState` (`SpawnScale`) — a raw `transform.localScale` write has no reset owner and leaks across reuse.

## Checklist

1. New kind → duplicate a sibling prefab (COPY, not variant), tune its components, create the identity SO, point `InstancePrefab` at it, bake the root's back-ref, Refresh Default Prefabs.
2. New per-kind value → a field on the right generic prefab component. Per-instance/mutable → instance state, never the SO.
3. New spawn site → the inline three-step; stamp `SpawnOwnerId` before `Spawn`.
4. New spawn-time mutation → per-spawn layer + `ResetState` reset; baseline untouched.
5. Ability roster mutation → only via the facade paths; Spawn-before-Add, RemoveAt-before-Despawn.
