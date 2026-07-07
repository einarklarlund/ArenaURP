---
name: type-object-pattern
description: This project's house rules for the Nystrom / Gang-of-Four TYPE OBJECT pattern — the per-kind ScriptableObject (ProjectileType, AbilityType, WeaponType). Reach for this whenever you have, or expect, a FAMILY of many kinds of one conceptual thing (projectile/ability/enemy/item/spell types) that share a common runtime shape, and you want to add new kinds by AUTHORING AND COMPOSING an asset rather than writing a bespoke subclass per kind — and you're deciding how to structure that type. Kinds here MAY differ in behaviour, not just data: behaviour comes from the kind's instance prefab (any MonoBehaviour components) and from pluggable command/strategy SO assets on its config. A type is a ScriptableObject holding the type's shared CONFIGURATION (immutable-at-runtime per-kind data) AND whatever visual/resource data it needs directly (UI sprites, sounds), plus a prefab field that is the template for the in-game instance. Per-INSTANCE state never lives on the SO — it lives on the instantiated prefab (graphics, runtime state, thin behaviour components). Processor purity is preserved by feeding the pure core a plain config struct, NOT by stripping asset refs off the SO. Also covers an abstract ScriptableTypeObject base (instance prefab + a public Initialize method) self-triggered by ONE generic dumb composition-root MonoBehaviour on the instance prefab (holding the base-typed SO ref baked in, prefab-per-kind) so the instance initializes itself on every peer — which is what makes client-predicted spawn correct without putting config on the wire — and splitting a type's instance into reusable, type-agnostic components that the initializer configures by pushing config in (the leaf components never reference the type; only the root does), wired in code (UnityEvents only for the view layer). Biased for a SOLO, code-first Unity dev using the Processor + thin-MonoBehaviour pattern over FishNet. Use this BEFORE creating or extending a ScriptableObject "type", applying the Type Object pattern, modeling an in-game instance of one, or splitting a fat NetworkBehaviour into components. The general pattern catalogue is [[game-programming-patterns]]; this is the concrete house implementation of its Type Object entry. Pairs with [[scriptableobject-commands]] for authoring the pluggable behaviours themselves.
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

No resource catalogs / lookup tables. The type holds its `NetworkObject` instance prefab (plus impact
VFX, sprites, sounds) **directly**. We tried the catalog indirection once — `ProjectileWorldProvider`
mapped `ProjectileType → NetworkObject` because the type lived in a *pure-Core* assembly that couldn't
reference FishNet — and **deleted both the catalog and the provider**. The fix wasn't a catalog: it was to
put the **type itself in the shell assembly** (Assembly-CSharp), where it can hold engine/FishNet refs
freely. Purity is preserved by the *processors* taking a config struct (Rule 4), **never** by keeping the
SO pure. So: a type lives wherever it must to own its own resources — anything with a networked prefab
belongs in the shell, not a pure-Core assembly. (See [[scriptableobject-commands]] for the provider's
removal.)

## Rule 3 — the instance self-initializes from its type via ONE dumb composition root

The type still owns the composition logic; a single generic component on the instance — the **composition
root** — *triggers* it. The base is data + one contract method (no `Create`, no `NetworkManager`):

```csharp
public abstract class ScriptableTypeObject : ScriptableObject {
    [SerializeField] NetworkObject instancePrefab;          // the type owns its prefab (data)
    public NetworkObject InstancePrefab => instancePrefab;

    // the composition: push each component's config in. PUBLIC so the root can call it.
    public abstract void Initialize(GameObject instance);
}
```

The concrete type's `Initialize` **pushes** each component's config in — the type knows the composition:

```csharp
public override void Initialize(GameObject instance) {                 // ProjectileType
    if (instance.TryGetComponent<NetworkedMotion>(out var m)) m.Configure(Motion, Speed);                    // Motion = [SerializeReference] IMotionStrategy
    if (instance.TryGetComponent<ServerCollision>(out var c)) c.Configure(Radius, Lifetime, DefaultEffects); // bundles the [SerializeReference] OnSurface/OnPlayer/OnProjectile lists
    if (instance.TryGetComponent<Health>(out var h))          h.ConfigureMax(MaxHealth);
    if (instance.TryGetComponent<Damage>(out var d))          d.Amount = Damage;
}
```

The composition root is **one generic `MonoBehaviour`, reused across every type family** — *dumb about
which kind it is*. It holds a **base-typed** `ScriptableTypeObject` ref (baked into the prefab) and does
nothing but call `Initialize` on itself, once, in `Awake`:

```csharp
public sealed class TypeObjectRoot : MonoBehaviour {
    [SerializeField] ScriptableTypeObject type;   // baked per prefab-variant; the ONLY type ref on the instance
    bool _initialized;
    void Awake() {                                 // runs on EVERY peer, before SetupSpawn + OnStartNetwork
        if (_initialized || type == null) return;
        _initialized = true;
        type.Initialize(gameObject);
    }
}
```

**Why the instance self-inits instead of the spawner doing it — this is the load-bearing reason, not a
nicety.** Initialization must be driven by the spawned object on every peer, *not* by whoever spawned it,
because the spawner's code only runs on the spawning peer. With client-predicted spawns, a predicting
client's weapon configures its local copy; the server and the other observers instantiate the prefab and
**never run that code**. Self-init in the root — pulling config from the baked SO locally at `Awake` — is
the only thing that makes all peers agree *without putting the config on the wire*. (`Awake` runs
synchronously inside `GetPooledInstantiated` / FishNet's spawn instantiation, so config is set before the
per-spawn payload is applied and before any component's `OnStartNetwork`.)

**Two halves of the old rule — one survives, one inverts:**
- ✅ *survives*: the **leaf** components are still generic and type-agnostic (`NetworkedMotion` knows
  nothing about projectiles) and still get their config **pushed** by `Initialize`. They never reference
  the type. Centralizing in `Initialize` keeps them reusable across kinds and across whole families.
- 🔄 *inverts*: we deliberately reintroduce the `prefab → SO` reference we used to forbid — but confined to
  **exactly one** component (the root), base-typed. That one designated GameObject "is" the type object;
  every other component stays ignorant. This is the trade that buys correct predicted spawn.

**Pull at the boundary, push internally:** the root *pulls* one ref from the SO (resolved locally per
peer); `Initialize` *pushes* plain config into the leaves. Per-instance state still stays on the instance.

**This requires prefab-per-kind.** Baking a single `type` ref into the root only works if each kind has its
own prefab. **Use an independent prefab COPY per kind, NOT a prefab variant** — FishNet prefab *variants*
inherit the base's serialized `PrefabId`, so two kinds collide on one id and spawn resolution breaks (the
generator won't reassign a variant's id; an independent copy gets a fresh one). Duplicate `Bullet Root` →
`Types/<Kind>.prefab`, bake the kind's `type` on each copy's root, repoint each `ProjectileType.InstancePrefab`,
then run **Tools/Fish-Networking/Utility/Refresh Default Prefabs** so each copy lands at a distinct
collection index (its runtime PrefabId). A *shared* prefab across kinds can't bake one ref, so it falls back
to syncing a type **index** and resolving the SO in the root once the index arrives — keep that init
**idempotent** and re-runnable from the index's `OnChange`. Prefer prefab-per-kind; it's the only shape that
takes config fully off the wire.

**Consistency guard:** the ref is now bidirectional (`type.InstancePrefab` ⇄ the prefab's
`TypeObjectRoot.type`); a mismatch under prefab-per-kind silently configures the wrong kind. Add an
`OnValidate` on the root asserting `type.InstancePrefab` resolves back to itself.

**The spawner shrinks** to per-spawn concerns only — it pooled-instantiates `type.InstancePrefab`, stamps
ownership + the per-spawn anchor, and network-spawns; it does **not** initialize. Funnel every spawn
through one helper (`ProjectileSpawner`) so no per-spawn field is forgotten.

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
destructibles, projectiles all want `Health`). The projectile splits into **generic, type-agnostic**
components, each configured by `ProjectileType.Initialize` (Rule 3) — none holds a `ProjectileType`:

- **`NetworkedMotion`** — generic deterministic closed-form motion (`PositionAt(age)`); runs on every peer
  with nothing synced per tick. It holds an `IMotionStrategy` (a `[SerializeReference]` on the type carrying
  its own tuning), assigned via `Configure(strategy, speed)` by the root at `Awake` — **not** on the payload.
- **`ServerCollision`** — server-only. Casts each tick (the sweep comes polymorphically from the strategy's
  `CollisionSweep` — a beam casts forward, a mover sweeps prev→new), classifies the hit (owner-safe player/
  surface/projectile via a pure processor), and runs the matching **injected** typed `IEffect` list
  directly against a concrete context. Its radius + lifetime + operation lists are pushed in via
  `Configure(...)`; configuring it on a client is a harmless no-op because it only *acts* server-side.
- **`Health`** / **`Damage`** — data holders (`SyncVar<int> Hp` / `int Amount`), seeded via
  `ConfigureMax` / `Amount`. Reactions read/write them.

**The networking sub-rule — THREE data classes, each with a different home:**

1. **Per-kind type config** (radius, speed, damage, reaction lists) → the root reads it from the **baked SO
   locally on every peer** at `Awake`. **Nothing on the wire.**
2. **Per-spawn anchor** (spawn tick, origin, direction, owner id) → genuinely per-shot and not in the SO, so
   it **still rides the spawn payload**, written by the spawner before `Spawn`. The root can't conjure it.
3. **Server-only runtime** (collision casting) → configured everywhere but gated by its own `IsServer`
   behaviour, so no payload and no `IsServer` flag in `Initialize`.

So `NetworkedMotion` carries its `IMotionStrategy` as a local ref seeded from the SO (class 1), while its
*anchor* — spawn tick/pos/dir — stays on the payload (class 2). This is the migration's win: a generic
component + a baked SO ref beats every peer paying for the config on every spawn, **and** it's the only
shape that survives predicted spawn (where the observer's payload may be all it gets).

**Instance-lifetime config vs spawn-lifetime state — the pooling rule.** Because the root configures **once
at `Awake`** (and pooling does **not** re-run `Awake` on reuse), a component configured by `Initialize`
must treat that config as **instance-lifetime: set once, never wiped in `ResetState`/despawn.** Only
per-spawn and runtime state is reset. This is safe precisely because prefab-per-kind fixes a pooled
instance's kind forever (a fireball prefab is always a fireball). Concretely: `NetworkedMotion.ResetState`
resets the anchor + working state but **must not** clear `_strategy`/`_initialSpeed`.

**Two physically separate layers — baseline vs per-spawn buff (project-wide rule).** When a spawn-time
contributor mutates a config-ish field (an on-spawn passive buff scaling a projectile's speed/scale), do **not**
multiply onto the current value — pooling preserves the baseline, so a multiply-in-place buff *compounds* across
reuse by a different owner. Keep two layers that never share a field: the **baseline** (`InitialSpeed`, authored
prefab scale, authored `Damage.Amount`) is preserved in `ResetState`; the **per-spawn buff/multiplier layer** is
reset to identity in `ResetState` and re-derived every spawn as `effective = baseline × multiplier`. Rule of
thumb: *any field a spawn-time contributor mutates must be reset in `ResetState`; never multiply onto the last
spawn's value.* This is the pooling-safe form of the modifier fold. Corollary: a per-spawn **transform** mutation
(scale/tint) needs an owning component with its own `ResetState` (`SpawnScale`), never a raw `transform.localScale`
write — raw transform state has no reset owner and leaks across reuse. Live examples: the on-spawn-buff receiver
(`SpawnBuffReceiver` + `NetworkedMotion._speedMultiplier`) and `SpawnScale`; see [[onspawn-projectile-buffs]] and
[[pooled-per-spawn-state-reset]].

**Wire gameplay in code, NOT inspector UnityEvents.** Direct calls / injected lists / typed C# delegates
are compile-checked, refactor-safe, testable, and respect the server-authoritative boundary. **Reserve
inspector UnityEvents for the pure VIEW layer** (VFX/sound/animation) where none of those concerns apply.
Even cosmetics here went through code/data, not UnityEvents: impact VFX is a `SpawnVfx` operation in a
`[SerializeReference]` reaction list, not an `OnHit` UnityEvent (see [[scriptableobject-commands]]).

The split must respect the networking model: **motion runs on all peers; collision/damage runs
server-only.** For a *predicted* (Rigidbody/`[Replicate]`) entity the constraints are harder — see
[[fishnet-conventions]] and the data-seam section of [[scriptableobject-commands]].

## Quick checklist

1. A *family* of kinds sharing a runtime shape, added by authoring assets? → Type Object fits. One or two
   kinds, or wholly-bespoke behaviour? → just write the class/prefab.
2. Per-kind, immutable at runtime? → config field on the SO. Per-instance, mutable? → on the instance
   prefab or in a `State` struct, never the SO.
3. Needs visuals (sprite/sound)? → **directly on the SO**. Needs to exist in the world? → a **`NetworkObject`
   prefab field on the SO** (put the type in the shell so it can hold it), with a **`TypeObjectRoot`** on
   the prefab holding the SO ref baked in (**prefab-per-kind**). The root calls the SO's `Initialize` in
   `Awake`; the spawner pooled-instantiates `InstancePrefab` and never initializes. No catalogs, no provider,
   no `Create`.
4. Splitting the instance into components? → make them **generic and type-agnostic** and have the type's
   `Initialize` configure them (they never reference the type — only the one root holds it); wire gameplay in
   code, view via UnityEvents. Sort each datum into the three classes: **type config → local from SO**,
   **per-spawn anchor → payload**, **server-only → gated behaviour**. Treat pushed config as
   instance-lifetime — never wiped in `ResetState`.
5. Pure processor involved? → it gets a **config struct**, not the SO. Don't strip the SO to get purity.
