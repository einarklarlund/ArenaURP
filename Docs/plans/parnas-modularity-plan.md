# Modularity plan — Parnas's decomposition criteria applied to `Assets/Scripts/`

**Status:** plan (not executed — analysis only, per the 2026-07-15 request). **Source:** D.L. Parnas, *On the Criteria To Be Used in Decomposing Systems into Modules* (CACM 15:12, 1972). **Method:** the `engineering-canon` skill's Parnas + Constantine + Lehman lenses.

---

## Verdict up front (read this before the list)

**The core of this codebase is already a near-textbook Parnas decomposition. This paper validates the architecture; it does not indict it.** The 18 `Arena.*.Core` assemblies each hide exactly one algorithm behind `Process(state, config, input) → state`, asmdef-enforced so a Core physically cannot see gameplay glue. `MatchMediator` is a real mediator (wires, holds no logic). Contribution collections cap cross-system coupling at Constantine's best rung (data). That *is* Parnas's "second decomposition" — modules organised around hidden design decisions, not processing steps.

So "refactor the entire codebase to improve modularity" is the wrong shape of goal here — it is the Brooks **second-system trap** (embellishing a system that already works) and it fights the Elegance north star (fewest mechanisms). **The real work is a short, ranked list of genuine information-hiding leaks at the *edges* — none in the core.** Two of the five candidates resolve to *deletion*, not refactoring.

## Method — the only test that counts

Parnas, verbatim: *"begin with a list of difficult design decisions or design decisions which are likely to change. Each module is then designed to hide such a decision."* A seam is judged by **walking one plausible change through it**: a good seam confines the change to one module; a bad one ripples across many. Each item below is scored by that ripple × how much fixing it pays down entropy (Lehman) × blast radius. Anything that only *looks* untidy but confines its changes is left alone.

## Findings, ranked — with decisions taken 2026-07-15

| Item | Leak (file:line) | Design decision that leaks | Decision |
|---|---|---|---|
| **W1** | `Level Generation/` — 59 files / 2344 lines; `foreach(step in Steps)` flowchart pipeline (`LevelGenerator.cs:79`) + a parallel `Grid`/`GridNetworkPlayer` world; referenced **only by its own editor** | The most literal Parnas violation (KWIC Modularization-1) *and* the largest subsystem — but dormant, so it pays no flexibility dividend | **Quarantine → delete.** Dormant per roadmap. Biggest single entropy paydown (Lehman) at ~zero blast radius. |
| **W2** | `WeaponData`/`WeaponDatabase` SO duplicates `WeaponConfig`'s 8 firing fields, hand-copied every frame (`PawnWeapon.cs:38–48`); a *second* "kind" mechanism beside the `ProjectileType` prefab-per-kind doctrine | The firing-config **format** is known in two modules; change it → edit both. Two competing type-object authorities. | **Collapse into `ProjectileType`/`WeaponConfig`.** One authority owns the firing format. Restores the committed type-object doctrine. |
| **W3** | UI reaches **through** manager singletons into their internals: `MatchFlowManager.Instance.CountdownTime.Value`, `DeathmatchManager.Instance.leadPlayer.Value` (`NetworkUIController.cs:30`, `PostgameCountdownView.cs:13`) | How countdown/score is *stored* leaks to every view that reads it; a second comms regime beside the `NetworkUIEvents` bus | **Route UI through the bus** — scoped to the UI↔manager seam only. Not a crusade against all singletons. |
| **W4** | `Health` is two `SyncVar<int>`; Bot *and* UI both re-derive `(float)Health/MaxHealth` (`BotBrain.cs:64`, `NetworkUIController.cs:125`) | Health's int-pair representation leaks; the "fraction = a/b" assumption is duplicated per consumer | **Cheap win** — offer `HealthFraction` on the Health surface; delete the re-derivations. |
| **W5** | `MatchState` hand-mirrored across `UIMatchState` + `ToUIMatchState` + `MatchStateToViewKind` (`UIStateMapper.cs:18`, `UIManager.cs:127`) | Add a match state → touch 3 files | **Light touch.** This mirror is the *deliberate* price of keeping `Arena.UIFlow.Core` engine-free (Parnas's own "prune the upper levels" hierarchy point) — **do not share the enum.** Add a compile-time exhaustiveness check so a missing case *fails to build*. |

**Explicitly NOT a leak** (do not chase): the `damageMultiplier` / `HitContext` stub is the *deliberately deferred* damage-buff seam (DESIGN.md open questions), correctly a stub.

## Out of scope — the parts the paper endorses

The 18 `Arena.*.Core` assemblies, the `MatchMediator` wiring hub, the contribution-collection / notification mechanisms, the `Pawn`↔`Health` facade, the `PawnInput` three-implementation split. These are the *good* decomposition. Touching them to "improve modularity" would add mechanisms and violate both Parnas (fewest hidden decisions) and Elegance. Leave them.

## Work items — each with the change it hides and the proof

Ordered by ROI (entropy paid down ÷ blast radius). None executed yet.

### W1 — Quarantine and delete Level Generation
- **Change hidden:** removes a 2344-line flowchart subsystem and its parallel `Grid`/`GridNetworkPlayer` world that duplicates the live "player"/"world" concepts.
- **Scope:** confirm zero *runtime* references first (scout found only `Editor/LevelGeneratorEditor.cs`); delete the subsystem + its editor + orphaned assets; verify no missing-script components remain in scenes/prefabs (runtime null-component walk, per the `ProjectileWorldProvider was REMOVED` memory — MCP missing-ref scan false-negatives).
- **Proof:** project compiles; EditMode + PlayMode suites green; a scene load produces no missing-script warnings.
- **North star:** Elegance (delete a whole mechanism) + Lehman (pure entropy paydown).
- **ADR:** yes — "remove dormant level generation," rejected alternative = "keep the isolated island." One screen.

### W2 — Collapse `WeaponData` into the `ProjectileType`/`WeaponConfig` doctrine
- **Change hidden:** the firing-config format lives in *one* authority; changing a firing field touches one place, not two mechanisms.
- **Scope:** move the 8 firing fields to the type-object home; delete `WeaponData`/`WeaponDatabase` + the `WeaponDatabase.Instance` singleton; `PawnWeapon.Update` stops per-frame field-copying.
- **Proof:** a **PlayMode fire-cadence test** (fire rate / burst / spread unchanged) stays green — this touches the live fire path, so it earns a networked test per the floor.
- **North star:** Elegance (collapse two "kind" mechanisms to one) + restores the type-object doctrine.
- **ADR:** yes — rejected alternative = "keep a data-driven weapon database distinct from projectile types."

### W3 — Route UI through the event bus
- **Change hidden:** UI stops knowing manager internals; how match/score state is stored can change without touching views.
- **Scope:** the two offending views (`NetworkUIController` countdown/score, `PostgameCountdownView`) consume `NetworkUIEvents` like the rest of UI, instead of `<Manager>.Instance.<field>`. Singletons that are legitimate roots stay — this is scoped to the UI reach-in only.
- **Proof:** a PlayMode test that the countdown/score view updates from a bus event, not a singleton read.
- **North star:** Elegance (one comms regime, not two) + Constantine (drop from common/coupling-through-globals to data coupling).

### W4 — `HealthFraction` on the Health surface
- **Change hidden:** health's int-pair representation; consumers read a fraction, not two ints.
- **Scope:** add the derived surface to `Health`/`Pawn`; delete the two re-derivations.
- **Proof:** EditMode test on the fraction; Bot + UI read it.
- **North star:** Elegance (de-duplicate a derived assumption). Cheapest item on the board (~15 min).

### W5 — Compile-time exhaustiveness on the match-state mapping
- **Change hidden:** keeps `Arena.UIFlow.Core` engine-free (do **not** share the enum — that would couple the low-level module to Assembly-CSharp, breaking the acyclic layering Parnas's hierarchy section prizes), while making "add a state" fail to compile until every mapping site is updated.
- **Scope:** replace the hand-translation with an exhaustive switch that the compiler checks (no default fall-through), or a single mapping table with a completeness assertion.
- **Proof:** deleting a case fails compilation.
- **North star:** Elegance + Dijkstra (obvious-by-construction over tested-for correctness).

## Canon interview — lenses that bit

- **Parnas (lead):** applied as the ranking test above. Core passes; the five edge items are where a plausible change ripples. W1/W2 are the two clearest "one decision, many modules."
- **Constantine:** W3 is the coupling finding — global reach-in is worse than data coupling; the fix moves it back to the bus (data). W2 removes a stamp-coupled per-frame copy.
- **Lehman:** every item states its paydown; W1 is the largest (delete a subsystem). This plan **only removes and consolidates** — it adds no new mechanism, which is the point.
- **Brooks:** the second-system effect is the *reason* this plan is short — see the verdict. The core is not re-opened.
- **Conway / Dijkstra:** no new module seams introduced; W5 and W4 make correctness structural rather than tested.

## Sequencing

W4 (cheap, isolated) → W2 (live fire path, needs the cadence test) → W1 (deletion, needs the missing-script sweep) → W3 (UI seam) → W5 (compile-time guard). Each is an independent commit gated by its named test. W1 and W2 each get a one-screen ADR when executed (both have a rejected alternative worth remembering). Nothing here blocks the `spike-features-to-real-content` roadmap thread — this is anti-entropy work to schedule against it, not before it.
