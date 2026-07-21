# Plan — Retire the FCIS *mandate*, make components the primary unit, keep pure cores as a targeted exception

**Status:** proposed (not implemented). Drafted 2026-07-17.

## Context

Einar wants three things: (1) components (Unity's MonoBehaviour composition) to be the **primary unit of our logic**, as Unity intends; (2) the `functional-core-imperative-shell` (FCIS) skill — which *mandates* that gameplay logic live in a pure `Arena.*.Core` processor and the MonoBehaviour be glue only — **gone**; (3) two new component-authoring guidelines documented:
- **Generic-via-config:** make components as generic as possible; a component's presence must **not** encode a specific object or situation — configuration on the component defines specifics. (Modularity-via-composition default; breakable in specific cases.)
- **Flat hierarchy:** keep the GameObject hierarchy as flat as possible; co-locate components that communicate on the same GameObject so presence-dispatch (`GetComponents`) works well.

He was initially inclined to *full removal* (inline all cores, delete the 18 `Arena.*.Core` asmdefs and all 28 EditMode tests). A `unity-scout` evidence pass changed that decision:

- The FCIS **reuse** justification is dead — nearly every processor has exactly **one** shell caller. His skepticism that the split is over-sold is *partly right*.
- But the **realized** benefit is real: **test-isolation of determinism-critical math.** A cluster of cores (`MovementProcessor`, `WeaponProcessor`, `ServerCollisionProcessor`, `HitstunProcessor`, all `NetworkedMotion` strategies + `Deflect/Ricochet/SpawnFanMath`) is re-run on multiple peers; a regression is a **silent peer desync** a PlayMode host typically won't catch — an EditMode test on the pure function catches it cheaply.
- ~16 of 28 EditMode tests are HIGH-yield; only ~4 are LOW.
- Two "thin-looking" cores are **guards, not ceremony**: `UIStateMapper` enforces switch-exhaustiveness in an engine-free assembly (the Unity compiler won't fail the build otherwise — the `[[unity-ignores-editorconfig]]` scar), and `DamageProcessor` is the deliberately-empty seam for the deferred damage-buff design.

**Einar chose the reframe:** components become the default unit and the FCIS-as-mandate goes, but we **keep** the determinism/branchy cores and their tests, and inline only the genuinely thin cosmetic ones. This delivers ~80% of the ask (components primary, ceremony cut, FCIS-as-worded gone, the two new rules in) without deleting the silent-desync safety net.

**Outcome:** a coherent new build doctrine — *"logic goes in the component by default; extract a pure `Arena.*.Core` + EditMode test only when the logic is determinism-critical or heavily branchy"* — recorded in an ADR, embodied in a replacement skill, and threaded through the workflow layer so docs and code agree.

## Canon interview (recorded — the lenses on this plan)

> Note: the canon was expanded to a **seventh lens (Martin — SOLID/dependency-direction as diagnostic)** on 2026-07-17, after this plan was drafted. Run the Martin lens before this plan is acted on; it is expected to *support* the reframe (dependency direction stays inward-as-data for the kept cores; inlining thin cosmetic cores introduces no new dependency inversion). Confirm, don't assume.

- **Parnas (decompose by what changes):** FCIS's Core boundary hides "how the engine feeds us the world." Retiring the *mandate* is fine because that decision is only genuinely volatile for the DET/branchy cores — which we keep. The thin cosmetic cores hide no volatile decision → inlining them is correct by Parnas, not against it.
- **Constantine (coupling):** shell→core is pure data coupling (best rung). Inlining a thin core removes a module without adding coupling; keeping the DET cores preserves data coupling. No control coupling introduced either way. Neutral-positive.
- **Brooks (conceptual integrity / second-system):** the risk is a *half-migrated* codebase (some FCIS, some inline) with no statable line. Mitigation: the new doctrine draws a **crisp, evidence-backed line** (determinism-critical or heavily-branchy → pure core; else → component). This is a *simplification* (removing a mandate), not second-system embellishment.
- **Lehman (pay-down):** the doc change pays down (one skill replaces one skill; a mandate becomes a targeted exception; fewer forced abstractions). Deleting HIGH-yield tests would have been unoffset quality debt — the reframe avoids it. The ~2–3 thin cores left un-inlined are named cleanup debt with a trigger (below).
- **Conway:** the Core asmdef boundary doubles as a delegation boundary (a validator can own a pure core from its interface). We keep it exactly where it's load-bearing.
- **Dijkstra (obvious correctness / humility):** the DET cores ARE "structure for obvious correctness"; keeping them + their tests honors the lens. The humility clause was satisfied by delegating the keep-vs-inline judgment to a scout with measured yields rather than asserting from memory.

**Register:** append one dated line under Constantine/Dijkstra recording the settled position (below). No existing register entry is contradicted; the charter lines that *embody* FCIS in Parnas #1 and Dijkstra #6 get reworded, not struck (the pattern survives as an exception).

## The new doctrine (one sentence)

> **Logic goes in the component by default. Extract a pure `Arena.<X>.Core` processor + EditMode test ONLY when the logic is determinism-critical (re-run on multiple peers / reconcile) or heavily branchy (state machines, classification, geometry). Reuse is not a reason — test-isolation of hard-to-otherwise-test math is.**

Plus the two authoring guidelines (generic-via-config; flat hierarchy).

## Changes

### 1. Replace the skill (the core deliverable)
- **Delete** `.claude/skills/functional-core-imperative-shell/SKILL.md`.
- **Create** `.claude/skills/component-design/SKILL.md` — the home for *how we build components*:
  - **Default:** components are the primary unit of logic (engine-native composition — an instance of the Elegance north star's "engine-native beats owned").
  - **Guideline A — generic-via-config:** a component is agnostic to concretions; its *presence* defines a capability, never a specific object/situation; specifics come from serialized **configuration**. Breakable, but the default for modularity-via-composition. Cross-link `[[presence-dispatch]]` (varies-in-behaviour = component; varies-in-data = authored config) and `[[type-object-pattern]]` (config-on-the-prefab-copy).
  - **Guideline B — flat hierarchy:** keep the GameObject hierarchy flat; co-locate communicating components on one GameObject so `GetComponents` presence-dispatch resolves locally. Generalizes the existing root-placement fact (`Health`/`KnockbackReceiver`/`HitstunReceiver` on the entity root for the `GetComponentInParent` walk).
  - **Exception — the pure core:** the determinism/branchy rule above, with the reference case (the projectile: `ServerCollision` shell casts/overlaps; `ServerCollisionProcessor` classifies). Keep the **shell-hygiene / fail-loud guard** guidance from the old skill (`Debug.LogError` naming the field + `this` context, then bail) — it's component-authoring advice, not FCIS-specific.
  - **Keep the anti-pattern note** ("an interface whose only second implementation is a test double") — it's the `[[no-speculative-interfaces]]` rule of two applied to cores; still true.
  - Description/trigger rewritten: *"how we build components — components primary, generic-via-config, flat hierarchy, and when to extract a pure core."*

### 2. ADR 0008 (load-bearing + contested → earns an ADR)
- `docs/adr/0008-components-primary-pure-core-exception.md`, following the DESIGN.md ADR shape:
  - **Context / Decision / Alternatives rejected** (full FCIS mandate; full demolition) / **Evidence** (the scout baseline: 1-caller-each ⇒ no reuse; ~16/28 HIGH-yield tests; DET cluster desync risk; guard cores) / **Consequences** / one-line north-star rationale.
- Add the row to the **DESIGN.md ADR index** and, if the "one architectural rule" is referenced as doctrine anywhere in DESIGN.md, point it at the ADR.

### 3. CLAUDE.md
- Rewrite **"## The one architectural rule"** → *"## Where component logic goes"*: state the new default (component-primary) + the pure-core exception + a pointer to `component-design`. Keep the asmdef-boundary note but reframe it as *"the boundary that keeps the determinism/branchy cores pure,"* not a universal law.
- Update the bot-AI line (it currently cites FCIS as the split for `BotBrainLogic` etc.) — bots stay pure cores (they're heavily branchy), so this line survives with the new framing.

### 4. Thread the workflow layer (link + wording updates, `[[functional-core-imperative-shell]]` → `[[component-design]]`)
Live files that reference FCIS (from grep; worktree copies ignored):
- `.claude/skills/engineering-canon/SKILL.md` — reword lens **#1 (Parnas)** and **#6 (Dijkstra)** "Embodied" lines: the Core boundary now embodies the lens *for determinism/branchy cores*, not universally.
- `.claude/skills/engineering-canon/shared-understandings.md` — append the settled line (§ Register below).
- `.claude/skills/presence-dispatch/SKILL.md`, `.claude/skills/cross-component-communication/SKILL.md`, `.claude/skills/arena-systems-map/SKILL.md`, `.claude/skills/game-programming-patterns/SKILL.md` — repoint the link + adjust any sentence that presumes the FCIS mandate.
- `.claude/hooks/skill-nudge.sh` — update the nudge string that currently says "gameplay logic → Core processor, not the MonoBehaviour" to the new default ("logic in the component; pure core only for determinism/branchy").
- `.claude/skills/integration-testing/SKILL.md` — reframe the EditMode framing: EditMode is *for the pure cores we keep*, not a universal first stop.

### 5. Code cleanup (small — the real cleanup is tiny)
Inline ONLY the genuinely thin cosmetic cores where a test is not the point; delete their asmdef + LOW-yield test:
- `HealthProcessor` (a clamp) → fold into `Health`; drop `Projectile/HealthProcessorTests` (LOW-MED, clamp round-trips) — **verify the divide-by-zero guard is preserved inline.**
- `MovementEffectsProcessor` (two lerps) → fold into its component; drop `MovementEffectsProcessorTests` (LOW-MED).
- `HumanInputMath` — **borderline, flag for Einar at execution:** thin, but `InputMathTests` guards a real WebGL `/10` divisor + sign/zero quirk. Recommend **keep** unless he wants it inlined; if inlined, keep the test by moving the quirk assertion to the shell's PlayMode or an EditMode test on a retained helper.

**Explicitly KEEP (do not inline), with tests:**
- DET: `MovementProcessor`, `WeaponProcessor`, `ServerCollisionProcessor`, `HitstunProcessor`, `NetworkedMotion/{Arc,Linear,Laser}Motion`, `SpawnFanMath`, `DeflectMath`, `RicochetMath`, `NetworkedMotionProcessor` (DET dispatch seam).
- Branchy: `BotBrainLogic`, `BotStateMachine`, `BotInputMath`, `BotVisionMath`, `RespawnProcessor`, `MatchTimerProcessor`, `MatchCountdownProcessor`, `AbilityInventoryLogic`, `TemperatureProcessor`, `PawnColorProcessor`, `KnockbackHelper`.
- Guards/seams: `UIStateMapper` (exhaustiveness), `DamageProcessor` (deferred-buff seam).

**Cleanup debt (Lehman):** any thin core left un-inlined is named debt; trigger = next time its owning component is edited for a feature. Recorded in the ADR's Consequences.

### 6. Memory updates
- Update/prune any memory that asserts the FCIS mandate. Add a short `project` note only if a durable fact changed; the doctrine itself lives in the skill + ADR, not memory. Update `MEMORY.md` pointer lines if a file changes.

## Phasing / delegation
- **Phase 1 (committed):** items 1–4, 6 + the ADR — pure doc/knowledge-layer work. No code risk. This *is* the answer to "how do we include the guidelines in our documentation."
- **Phase 2 (small code cleanup):** item 5 — inline the 2 safe cores; `HumanInputMath` decided with Einar. Each change verified by **`unity-validator`** (EditMode compile + the retained suites stay green). Because the new doctrine legitimizes the kept cores, there is **no doc/code drift** from doing Phase 2 later — the only debt is the 2–3 named thin cores.

## Verification
- **Docs:** re-read the new `component-design` SKILL + CLAUDE.md section end-to-end; grep the live tree for stale `functional-core-imperative-shell` link targets (expect zero outside worktrees); confirm the ADR index row + `shared-understandings` line landed.
- **Code (Phase 2):** delegate to `unity-validator` — EditMode suite compiles and passes after each inline; confirm the removed asmdefs don't orphan references. No PlayMode change expected (nothing networked moved).
- **Consistency gate:** optionally run `workflow-auditor` after Phase 1 to confirm no dangling FCIS references or contradictory doctrine remain.
