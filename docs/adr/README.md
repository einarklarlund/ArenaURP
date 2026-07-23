# ADR index

Architectural Decision Records — the **why** behind a contested, load-bearing choice: what we decided, what we rejected, and the evidence. One line each below; each file holds context + decision + alternatives + consequences. Conventions for writing one are in [../agents/domain.md](../agents/domain.md).

| # | Decision | Status | Date |
|---|---|---|---|
| [0001](0001-hit-exchange-pattern.md) | Hit exchanges = dealer component + receiver component (courier layer superseded by 0002) | Accepted | 2026-07-10 |
| [0002](0002-ieffect-appropriateness-verdict.md) | IEffect verdict: keep command core; delete lattice/couriers/sink-buffs; add fan-out site | Superseded in part by 0003 | 2026-07-13 |
| [0003](0003-hit-reactions-as-handler-components.md) | Hit reactions = presence-discovered IHitHandler components; channel = call site; command core retires | Superseded in part by 0005 | 2026-07-14 |
| [0004](0004-cross-peer-damage-cosmetics-ride-replication.md) | Cross-peer damage cosmetics ride the CurrentHealth SyncVar OnChange (PawnColor pattern), not an ObserversRpc; C# damage events narrow to server-side | Accepted | 2026-07-15 |
| [0005](0005-hit-pipeline-layered-messages.md) | Hit pipeline = the layered model: one typed HitMessage (four-clause fan-out criterion) + per-datum controllers; HitResolver/dealer/receiver deleted; cycle-guard deferred; corrects 0008's kept-cores list | Accepted | 2026-07-19 |
| [0006](0006-remove-dormant-level-generation.md) | Remove the dormant Level Generation subsystem (delete the isolated island; art prefabs + Online scene components flagged for editor-side sweep) | Accepted | 2026-07-15 |
| [0007](0007-weapon-config-on-component.md) | Weapon firing config lives on the PawnWeapon component (serialized WeaponConfig + ProjectileType ref); WeaponData/WeaponDatabase deleted (one type-object authority) | Accepted | 2026-07-15 |
| [0008](0008-components-primary-pure-core-exception.md) | Components are the primary unit of logic; FCIS mandate retired; pure Arena.*.Core + EditMode test kept only for determinism-critical/branchy logic; generic-via-config + flat-hierarchy guidelines | Accepted | 2026-07-17 |
| [0009](0009-passive-shared-registry-applicator.md) | Passives = shared-registry KINDs (nested-NObj children of a spawned PassiveRegistry) + component-coupled Passive applicators (one per affected component) + player-held SyncList references (PlayerPassiveLoadout); steal = ref move; PassiveAbility/PawnPassiveAbilities/PassiveAbilityType deleted | Accepted | 2026-07-21 |

## What is *not* yet decided

Deliberately deferred decisions — so they aren't improvised under pressure — live as grill-ready briefs in [../plans/decisions/](../plans/decisions/), indexed with the features waiting on each in [../plans/features.md](../plans/features.md). Feed a brief to a `/grill-with-docs` session to settle it into an ADR here.
