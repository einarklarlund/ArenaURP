---
name: type-object-pattern
description: This project's house rules for the Nystrom / Gang-of-Four TYPE OBJECT pattern — the per-kind ScriptableObject (ProjectileType, AbilityType, WeaponType). Reach for this whenever you have, or expect, a FAMILY of many kinds of one conceptual thing (projectile/ability/enemy/item/spell types) that share a common runtime shape, and you want to add new kinds by AUTHORING AND COMPOSING an asset rather than writing a bespoke subclass per kind — and you're deciding how to structure that type. Kinds here MAY differ in behaviour, not just data: behaviour comes from the kind's instance prefab (any MonoBehaviour components) and from pluggable command/strategy SO assets on its config. A type is a ScriptableObject holding the type's shared CONFIGURATION (immutable-at-runtime per-kind data) AND whatever visual/resource data it needs directly (UI sprites, sounds), plus a prefab field that is the template for the in-game instance. Per-INSTANCE state never lives on the SO — it lives on the instantiated prefab (graphics, runtime state, thin behaviour components). Processor purity is preserved by feeding the pure core a plain config struct, NOT by stripping asset refs off the SO. Also covers splitting a type's instance into reusable thin components that read their slice of the type SO and communicate via typed C# events (UnityEvents only for the view layer). Biased for a SOLO, code-first Unity dev using the Processor + thin-MonoBehaviour pattern over FishNet. Use this BEFORE creating or extending a ScriptableObject "type", applying the Type Object pattern, modeling an in-game instance of one, or splitting a fat NetworkBehaviour into components. The general pattern catalogue is [[game-programming-patterns]]; this is the concrete house implementation of its Type Object entry. Pairs with [[scriptableobject-commands]] for authoring the pluggable behaviours themselves.
---

# The Type Object pattern, the house way: SO config + prefab instances (curated for a solo, code-first Unity dev)

This is the concrete, project-specific implementation of the **Type Object** pattern catalogued in
[[game-programming-patterns]]. Read that entry for the *what* and *why* in the abstract; read this for
*how we build one here*. When the general skill — or your own judgement — says "this is a Type Object,"
this skill is the house standard for structuring it.

## When the Type Object pattern fits

You have, or expect, a **family of many kinds** of one conceptual thing — projectile types, ability types,
enemy types, item types — that share a **common runtime shape** (a generic instance/consumer reads the
type and runs it). You want to add a new kind by **authoring and composing an asset** — config values +
resource refs + an instance prefab + pluggable behaviour assets — instead of writing a bespoke subclass
per kind.

**Kinds may differ in behaviour, not just data.** The classic "Type Object differs by data, not behaviour"
restriction does **not** apply to this house version, because behaviour is supplied two ways: (1) a kind's
**instance prefab** can carry any `MonoBehaviour` components, and (2) a kind's config can hold **pluggable
command/strategy SO assets** (different reactions, a different movement strategy — see
[[scriptableobject-commands]]). The pattern's real job here is to avoid a **class-per-kind explosion**:
each kind is a *composed asset built from reusable parts*, not a one-off subclass.

**When it doesn't fit:** only one or two kinds (no family to parameterize — just write the class/prefab),
or a kind whose behaviour is wholly bespoke and shares nothing reusable with its siblings (a unique
scripted boss may just be its own prefab + script). Reach for Type Object when the family is open-ended and
assembled from shared, reusable building blocks.

Unity's strength is that a ScriptableObject or prefab **consolidates** configuration, graphics, audio,
and behaviour into one authorable, previewable unit, so the house default is to **go with that grain**.
This skill covers the macro structure of a type; [[scriptableobject-commands]] covers authoring the
pluggable behaviours it composes. It builds on [[functional-core-imperative-shell]] (keep the *processor*
pure — distinct from keeping the *SO* pure) and [[fishnet-conventions]] (networked instances,
server-authoritative collision, syncing a type as a database index).

## The default shape

A "type" is split across two Unity assets and one pure struct:

1. **The type ScriptableObject = shared CONFIGURATION + intrinsic resources.** Per-kind data that is
   immutable at runtime (radius, damage, speed, multipliers) **plus** the resources that belong to this
   kind: a UI `Icon`, a sound, etc. — held directly on the SO — **plus** a **prefab field** that is the
   template for spawning an instance. One self-contained asset per kind.
2. **The prefab = the INSTANCE template.** When you want a live instance, you instantiate the type's
   prefab. The prefab carries the graphics, the **per-instance runtime state**, and thin behaviour
   `MonoBehaviour`s. Instances are GameObjects; the SO is the flyweight they share.
3. **A plain config struct = what the pure processor consumes.** The processor never receives the SO as a
   Unity object — the shell reads the SO's logical fields into a config struct and passes that in.

`AbilityType`/`PassiveAbilityType`/`ActiveAbilityType` and `ProjectileType` are the live examples.

## Rule 1 — the SO holds CONFIG, never per-instance state

The type SO is a **Flyweight**: shared by every instance, read-only at runtime. Never put a field on it
that an individual instance mutates. "This projectile's current health," "this turret's remaining ammo"
are **per-instance** — they live on the spawned prefab instance (a `MonoBehaviour` field) or in the
processor's `State` struct, never on the SO. If you catch yourself writing to an SO field at runtime,
you've put instance state in the flyweight; move it to the instance.

## Rule 2 — resources and instances live on the SO

The SO holds **whatever visual/resource data it needs directly** — UI sprites, icons, sounds — alongside a
**prefab field** for the in-game instance. This is the consolidation win: a type asset is a complete,
self-contained, previewable unit. To spawn the thing, instantiate its `InstancePrefab`; the prefab is
where graphics, colliders, per-instance state, and behaviour components live.

We are **not** introducing resource catalogs / lookup tables right now — don't add that indirection
preemptively. The one situation that can force it is a hard assembly constraint: a pure-Core SO can't hold
an engine/FishNet reference (e.g. `ProjectileType` in `Arena.Projectile.Core` can't reference a
`NetworkObject` prefab — which is why `ProjectileWorldProvider` maps that one today). Solve that case if
and when you hit it; everything else keeps its sprites/prefab on the SO.

## Rule 3 — initialize instance scripts FROM the config

A spawned instance's scripts read shared parameters from the type's config. Hand the SO (or a config
struct built from it) to the instance right after instantiation:

```csharp
var instance = Instantiate(type.InstancePrefab, pos, rot);
instance.GetComponent<TurretBehaviour>().Initialize(type);   // reads type.FireRate, type.Range, ...
```

The instance holds the config for its lifetime; per-instance state stays on the instance.

## Rule 4 — processor purity ≠ SO purity

[[functional-core-imperative-shell]] requires the **processor** to be pure — you feed it a plain config
struct, not the SO. It does **not** require the SO to be free of asset references. So you get both: a
rich, Unity-native SO bundling config + icon + prefab + sound, **and** a pure processor that only sees the
logical subset as a struct. Never strip asset refs off the SO "for purity" — that's not where purity
lives. (Networking is the same: a type that syncs as a database **index** can still hold `Sprite`/prefab
refs — they resolve locally per peer and are never serialized; see [[fishnet-conventions]].)

## Splitting a type's instance into behaviours — the projectile example

A type's instance prefab can be **one** thin shell or **several** focused components — and giving different
kinds different components is exactly how kinds differ in behaviour. Decompose when you want heterogeneous
prefabs (a laser has no `Health`; a grenade adds a `Timer`) or component reuse across archetypes (pawns,
destructibles, projectiles all want `Health`). The projectile breaks into thin components that each read
**their slice of the type SO**:

- **`NetworkedMotion`** — reads `type.Movement` (a strategy; see [[scriptableobject-commands]]). Motion is
  **deterministic / closed-form** (`PositionAt(age)`), so it runs on every peer with nothing synced per
  tick.
- **`ServerCollision`** — server-only. Casts each tick, classifies the hit category, and raises **typed
  C# events**: `OnEnemyHit` / `OnSurfaceHit` / `OnProjectileHit`. On a hit it iterates the matching
  reaction list from the SO and runs each reaction (no separate "reactions" dispatcher component).
- **`Health`** — reads `type.MaxHealth`; holds the per-instance health (`SyncVar`); subscribes to the
  projectile-hit event.

**Wire the gameplay/networked layer with typed C# events, NOT inspector UnityEvents.** Code wiring is
compile-checked, refactor-safe, testable, and respects the server-authoritative boundary (the component
raising the event gates on `IsServerInitialized`). UnityEvents have none of those guarantees and no notion
of "server-only" or "don't re-run during reconcile." **Reserve inspector UnityEvents for the VIEW layer**
— VFX/sound/animation subscribing to an `OnHit` — where none of those concerns apply and Unity's
composition genuinely is better. (The repo's own predicted-movement layer, `MovementController`, wires
behaviour with SO strategy objects in code, not UnityEvents — follow that precedent for logic.)

The split must respect the existing networking model: **motion runs on all peers; collision/damage runs
server-only.** For a *predicted* (Rigidbody/`[Replicate]`) entity the constraints are harder — see
[[fishnet-conventions]] and the data-seam section of [[scriptableobject-commands]].

## Quick checklist

1. A *family* of kinds sharing a runtime shape, added by authoring assets? → Type Object fits. One or two
   kinds, or wholly-bespoke behaviour? → just write the class/prefab.
2. Per-kind, immutable at runtime? → config field on the SO. Per-instance, mutable? → on the instance
   prefab or in a `State` struct, never the SO.
3. Needs visuals (sprite/sound)? → **directly on the SO**. Needs to exist in the world? → a **prefab field
   on the SO**; instantiate it; `Initialize` its scripts from the config. No catalogs yet.
4. Splitting the instance into components? → each reads its slice of the SO; wire gameplay with **typed C#
   events**, view with UnityEvents; keep the server/everyone boundary the current code draws.
5. Pure processor involved? → it gets a **config struct**, not the SO. Don't strip the SO to get purity.
