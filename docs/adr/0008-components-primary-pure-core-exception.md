# ADR 0008 — Components are the primary unit of logic; the pure `Arena.*.Core` processor is a targeted exception

- **Status:** Accepted (2026-07-17)
- **Date:** 2026-07-17

## Context

The project had **one architectural rule**: gameplay logic lives in a pure `Arena.<System>.Core` processor and the `Pawn<X>` NetworkBehaviour is Unity glue only — a functional-core / imperative-shell (FCIS) *mandate*, enforced by asmdef boundaries and governed by the `functional-core-imperative-shell` skill. It produced **18 `Arena.*.Core` asmdefs** and **28 EditMode tests**.

Einar wanted three things: (1) the **component** (Unity's MonoBehaviour composition) to be the primary unit of logic, as Unity intends; (2) the FCIS *mandate* gone; (3) two component-authoring guidelines documented — **generic-via-config** (a component's presence defines a capability; specifics live in serialized config, never in special-case code) and **flat hierarchy** (co-locate communicating components so `GetComponents` presence-dispatch resolves locally).

His initial inclination was *full removal* — inline every core, delete all 18 asmdefs and all 28 tests. A `unity-scout` evidence pass changed that decision (see Evidence).

## Decision

**Logic goes in the component by default. Extract a pure `Arena.<X>.Core` processor + EditMode test ONLY when the logic is determinism-critical (re-run on multiple peers / on reconcile) or heavily branchy (state machines, classification, geometry). Reuse is not a reason — test-isolation of hard-to-otherwise-test math is.** Plus the two authoring guidelines (generic-via-config; flat hierarchy).

The FCIS-as-mandate is retired: the `functional-core-imperative-shell` skill is replaced by `component-design` (component-primary, the two guidelines, and the pure-core exception). The determinism-critical and heavily-branchy cores — and their EditMode tests — are **kept**; only genuinely thin cosmetic cores (a clamp, two lerps) are inlined into their components.

## Alternatives rejected

- **Keep the full FCIS mandate (status quo).** Rejected: its stated **reuse** justification is dead (nearly every processor has exactly one shell caller), and mandating a pure core for cosmetic logic (a clamp, a lerp) is ceremony — a test that asserts a round-trip no one will break. Einar's skepticism that the split was over-sold is partly right.
- **Full demolition — inline everything, delete all 18 asmdefs and 28 tests.** Rejected on evidence: it would delete the **realized** benefit — EditMode test-isolation of determinism-critical math, whose regressions are **silent peer desyncs** a PlayMode host typically won't catch — and ~16 of 28 tests are HIGH-yield. This is unoffset quality debt (Lehman VII).

## Evidence

Baseline from a `unity-scout` measured pass (2026-07-17):

- **No reuse dividend:** nearly every processor has exactly **one** shell caller. The FCIS reuse justification is dead.
- **Real, realized benefit:** a cluster of cores (`MovementProcessor`, `WeaponProcessor`, `ServerCollisionProcessor`, `HitstunProcessor`, all `NetworkedMotion` strategies + `Deflect`/`Ricochet`/`SpawnFanMath`) is re-run on multiple peers; a regression is a **silent peer desync**. An EditMode test on the pure function catches it cheaply — PlayMode host fixtures typically don't.
- **Test yield:** ~**16 of 28** EditMode tests are HIGH-yield; only ~4 are LOW.
- **Guards, not ceremony:** two thin-looking cores hide a real decision — `UIStateMapper` enforces `switch` exhaustiveness in an engine-free assembly (Unity's compiler won't fail the build otherwise — the `unity-ignores-editorconfig` scar), and `DamageProcessor` is the deliberately-empty seam for the deferred damage-buff design (open question in [docs/plans/decisions/damage-buff-channel.md](../plans/decisions/damage-buff-channel.md)).

## Consequences

- **`component-design` replaces `functional-core-imperative-shell`** as the governing skill; CLAUDE.md's "one architectural rule" becomes "Where component logic goes" (default + exception). The workflow layer (engineering-canon lenses 1/6, presence-dispatch, cross-component-communication, arena-systems-map, game-programming-patterns, integration-testing, the skill-nudge hook, the `no-speculative-interfaces` memory) is repointed `[[functional-core-imperative-shell]]` → `[[component-design]]`; the pattern survives as an exception, not a struck rule.
- **Kept (do not inline), with tests** — DET: `MovementProcessor`, `WeaponProcessor`, `ServerCollisionProcessor`, `HitstunProcessor`, `NetworkedMotion/{Arc,Linear,Laser}Motion`, `SpawnFanMath`, `DeflectMath`, `RicochetMath`, `NetworkedMotionProcessor`. Branchy: `BotBrainLogic`, `BotStateMachine`, `BotInputMath`, `BotVisionMath`, `RespawnProcessor`, `MatchTimerProcessor`, `MatchCountdownProcessor`, `AbilityInventoryLogic`, `TemperatureProcessor`, `PawnColorProcessor`, `KnockbackHelper`. Guards/seams: `UIStateMapper`, `DamageProcessor`.
- **Inlined (Phase 2, small):** `HealthProcessor` (a clamp) → `Health`, preserving its divide-by-zero guard; `MovementEffectsProcessor` (two lerps) → its component. Their asmdefs + LOW-yield tests are removed. `HumanInputMath` is **borderline** — thin, but `InputMathTests` guards a real WebGL `/10` divisor + sign/zero quirk; flagged for Einar, recommend keep.
- **Cleanup debt (Lehman):** any thin core left un-inlined is named debt; the repayment **trigger** is the next time its owning component is edited for a feature — inline it then, don't schedule a sweep.
- **Cheap to break:** if the component-primary default ever proves wrong for a class of logic, the superseding path is one ADR widening the exception — the kept cores already demonstrate the pattern, so re-expanding it costs no new machinery.

**North-star rationale.** *Elegance:* removes a mandate and its forced abstractions (a mechanism collapsed, not added — engine-native composition beats an owned FCIS layer), while conserving the EditMode safety net that keeps determinism-critical math obviously correct. *Essence:* the two guidelines (generic-via-config, flat hierarchy) make composition — the way this game's few interacting systems are assembled on a pawn — more modular, not less.
