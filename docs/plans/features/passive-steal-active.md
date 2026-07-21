# Feature — passive-steal active ability

**Blocking decision:** none hard (rides the shipped passive-loadout ref-move). **Ship gate:** none. **Buildable now.**

## What it is

An **active** ability: cast a capsule in front of you and, if it hits an enemy, **steal one of their passives** onto yourself. The *on-hit* version of passive-steal already ships and is tested — this feature builds only the **active, cast-delivered** variant.

## Essence gate

(a) Drastically changes both players' builds mid-match — you lose a passive, the caster gains it. (b) Interacts with passives, actives, and (open question) the hit pipeline. Passes strongly — it's an upgrade×upgrade interaction by construction.

## What already exists (don't re-litigate)

- The steal **mechanics** ship as `PassiveStealController` (an `IHitMessageHandler`), with PlayMode tests. Post-ADR-0009 the steal is a `PlayerPassiveLoadout` ref-move (`Remove` + `Grant`, no spawn/despawn).
- `ActiveAbility.ServerUse` is hard-typed to `GetComponents<SpawnNetworkObjects>` today, with a rule-of-two note anticipating exactly this second use-site.
- `PawnActiveAbilities` already plumbs an `aimSource`.

## Shape

1. **`IUseHandler` — the now-earned interface.** The steal is the *second* use-site behaviour, so the interface earns existence (rule of two): `IUseHandler.OnUse(origin, direction, instigator)`, implemented by `SpawnNetworkObjects` and the new `StealPassiveOnUse`, discovered by the same `GetComponents` presence dispatch as hit handlers. This *collapses* a bespoke hard-typed dispatch into the one mechanism the project already runs — a net simplification.
2. **`StealPassiveOnUse`** — server-side capsule-cast from `aimSource` against Players; resolve the victim `Pawn` via `GetComponentInParent` (the `AbilityPickup` idiom); run the steal core **extracted from `PassiveStealController` into a shared method both variants call** — so the on-hit and active steals can't drift. Authoring: new ability prefab copy + `AbilityType` SO, modeled on ScatterBurst.

## Open questions to grill

1. **Does the cast dispatch a `HitMessage`?** The *architecture* is settled by ADR 0005: if the steal is a hit, it's a `HitMessage` from the cast site (the "second hit source" the ADR names). What's left is a **pure game-design** question: *should stealing trigger the victim's hit reactions* (and later stack with damage/knockback)? **Recommend: no — plain server capsule-cast + shared steal core.** The steal fires no exchange and no reaction, so it isn't a hit. If the answer flips to yes, it becomes a dealer-carrying cast. **Watch:** if it lands on "yes" *and* [proximity explosion](proximity-explosion.md) ships first, the extraction already happened and the steal is the third hit source, not the second.
2. **Use consumed on miss?** **Recommend: yes** — a use is spent on activation whether or not the cast lands, consistent with ScatterBurst.

## Proof

PlayMode, modeled on the shipped on-hit steal tests + `AbilityInventoryPlayModeTests`: caster facing a victim-with-passive → use → passive counts swap sides and the modifier fold follows; miss → rosters unchanged, use consumed.
