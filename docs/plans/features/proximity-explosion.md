# Feature — proximity explosion

**Blocking decision:** none hard (coordinates with the [damage-buff channel](../decisions/damage-buff-channel.md) for the *wide* version of falloff). **Ship gate:** none. **Buildable now.**

## What it is

A new projectile kind that, on contact, detonates and deals a hit to every entity in a radius — knockback + damage falling off with distance. Includes **knock-up + AoE** (the "pepper" variant) as pure authored config on the same explosion, no new code.

## Essence gate

(a) Changes how both players play — area denial, splash pressure, rocket-jump-adjacent movement. (b) Interacts with projectiles, health, knockback, and (via self-resist) passives. Passes.

## What already exists (don't re-litigate)

The hit pipeline (ADR 0005) is done. A hit is one typed `HitMessage` dispatched to `IHitMessageHandler` components; each datum has a per-datum `*Controller` that owns both deal and receive halves and looks its own controllers up from `Source`/`Victim`. **Nested dispatch is legal** — a handler may fire a further `HitMessage`. So an explosion is not new infrastructure; it's a new hit *source*.

## Shape (standing doctrine, no new decisions)

- A new **`ExplosionController`** (an `IHitMessageHandler`) authored on an independent prefab **copy** of `Bullet.prefab` (never a variant — FishNet PrefabId collision) + identity-only `ProjectileType` SO + `TypeObjectRoot` back-ref + Refresh Default Prefabs.
- On the hit, it does `Physics.OverlapSphereNonAlloc` at the hit point against the Players mask, resolves struck colliders to entity roots via the same `GetComponentInParent` walk the controllers use, **dedupes on resolved root**, and calls `HitMessage.Dispatch` **once per victim** (per-victim radial `Direction`) — N dispatches of one message each, not one message to N victims.
- Re-dispatching from the source means the source's own `DamageController`/`KnockbackController`/`HitstunController` **deal for free** for each victim. No dealer harvest, no component refs on the message.
- Per-victim **falloff** is a pure Core helper (`BlastFalloffMath`: distance, radius → multiplier; **v1 linear with a min clamp — no `AnimationCurve`, no wall occlusion, no per-channel radii**, a deliberate pin). It ships a **minimal local per-target seam** (scale `DamageController.Amount` at the dealing handler for the dispatch). The [damage-buff channel](../decisions/damage-buff-channel.md) later decides whether that seam generalizes wide — don't build wide now.
- Blast knockback/hitstun apply **unscaled** (falloff scales damage only). Scalable/resistible knockback is the damage-buff channel's job, not here.

## Owed debt this feature pays

Nested dispatch re-opens a cycle risk (`X→Y→X`) that the old pipeline foreclosed by comment. ADR 0005 **defers the re-entrancy cycle-guard** and names *this feature* as its repayment trigger: ship the **runtime depth-guard + `ReentrancyGuardTests`** in the same change that first makes cycles possible.

## Open questions to grill

1. **Direct-hit double-dip.** Should the directly-struck victim *also* take blast damage? **Recommend: no** — exclude the direct victim's root from the fan-out; the direct exchange *is* the 1.0 hit. Keeps "one hit event per victim" and the dedupe test simple.
2. **Self-exclusion.** Exclude the instigator's own pawn from the blast in v1? **Recommend: yes.** Revisit only if a rocket-jump movement upgrade ever wants self-knockback — that would be an essence-positive interaction (and it re-opens here; see [bigger blast + self-resist](bigger-blast-self-resist.md)).

## Proof

- EditMode: `BlastFalloffMath` (distance/radius/clamp edges).
- PlayMode (combat harness): shooter + two victims at different distances → both damaged, nearer takes more (`LastDamageAmount` per victim); **exactly one damage event per victim** (dedupe); direct-hit total matches the double-dip decision.
