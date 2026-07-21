# Feature — combo fire-rate passive

**Blocking decision:** none. **Ship gate:** none. **Buildable now.**

## What it is

A passive: landing consecutive hits (a combo) ramps your fire-rate, decaying when the combo breaks. Rewards sustained accuracy.

## Essence gate

(a) Changes how the owner plays — accuracy compounds into DPS. (b) Interacts with weapons (firing cadence) and kills/hits (the combo source). Passes; it's an upgrade on the weapon system driven by the hit-interaction source.

## What already exists

`PawnWeapon.Update` drives firing cadence. The weapon config now lives on the component (ADR 0007). This feature slots a `ComboProcessor` into that loop — a pure Core is warranted here (fire-rate math is small but re-run and worth an EditMode test; confirm against the `component-design` rule during the grill — it may be thin enough to inline).

## Open questions to grill

1. **What counts as a combo hit** — any hit, or only hits within a time window? **Recommend:** a rolling window (combo decays if you miss/pause), so it rewards *sustained* fire, not a lifetime counter.
2. **Ramp curve** — linear vs. stepped tiers? **Recommend:** start linear with a cap; tiers are a tuning embellishment to defer.
3. **Does it stack with hitstun/other weapon passives** — any interaction to design now? **Recommend:** treat as an independent fold on `PawnWeapon.Modifiers`; no special-casing.

## Proof

- EditMode (if a `ComboProcessor` core survives the inline test): combo count → fire-rate multiplier, window expiry, cap.
- PlayMode: fire a sustained burst → cadence increases; break the combo → cadence resets.
