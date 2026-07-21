# ADR 0006 — Remove the dormant Level Generation subsystem

- **Status:** Accepted (2026-07-15)
- **Date:** 2026-07-15

## Context

`Assets/Scripts/Level Generation/` was a 2344-line, 59-file procedural-level subsystem: a `foreach (step in Steps)` flowchart pipeline (`LevelGenerator.cs`) driving a `Walker`/`WalkerManager` grid-carving engine over a parallel `Grid`/`GridSpace`/`GridNetworkPlayer` world, plus boundary shapes, room-library stamping, and hallway generation. It is the modularity plan's **W1** item — Parnas's most literal violation (modules organised around processing *steps*, the KWIC Modularization-1 antipattern) *and* the single largest subsystem.

It is **dormant**: nothing in the live game runs it. A static sweep of every `.cs` in `Assets/` for all 60 public type names in the folder found **zero** references outside the subsystem and its one editor (`Assets/Scripts/Editor/LevelGeneratorEditor.cs`). It duplicates the game's live "player"/"world" concepts (`GridNetworkPlayer` beside the real `NetworkPlayer`) with no shared code and no roadmap slot — pure accumulated entropy (Lehman) paying no flexibility dividend.

## Decision

**Delete the subsystem, its editor, and its orphaned configuration assets.** Removed from source control:

1. `Assets/Scripts/Level Generation/` — the whole code island (59 `.cs` + metas + folder metas).
2. `Assets/Scripts/Editor/LevelGeneratorEditor.cs` (+ meta) — its only referrer.
3. The 20 orphaned ScriptableObject config assets under `Assets/Prefabs/Level/Test Level/Generation/` (Steps, Walker Actions/Conditions, boundary, walker profile, room-library entries), each **positively confirmed** to be an instance of a deleted script GUID, plus the four folder metas left empty by their removal.

**Left in place, flagged for editor-side validation** (a `git`-only agent cannot resolve GUID references or strip scene components safely): the level-gen *art* prefabs under `Test Level/` (Buildings, Floors, Hallways, ProceduralRooms) carry **no** deleted script and may be reusable geometry; and `Assets/Scenes/Online.unity` holds one dormant `LevelGenerator` and one `WalkerManager` component instance that will read as missing-script after this deletion and must be stripped with the editor (per the [[projectile-world-provider-per-scene]] memory — a runtime null-component walk, since `unity_search_missing_references` false-negatives).

## Alternatives rejected

- **Keep the isolated island.** The tidiest argument: it compiles, it is self-contained, and procedural levels *might* be wanted someday. Rejected: "might be wanted someday" is the speculative-generality trap the Essence scope-guard exists to stop — an unbuilt subsystem earns nothing and costs full maintenance/attention price against the Elegance budget every time someone reads `Assets/Scripts/`. If procedural generation is ever greenlit it will be designed against the *current* pawn/world model, not resurrected from a parallel `Grid`/`GridNetworkPlayer` world it no longer matches. Git history preserves it at zero carrying cost, which is the correct place for a dormant island.
- **Quarantine only (move to an `~Excluded` / Editor-only folder).** Halves the visible-code entropy without committing to deletion. Rejected: it keeps the second "player/world" vocabulary alive as a live-looking option and still costs a reader the "what is this, is it load-bearing?" tax — a half-measure that spends the decision without banking the paydown.

## Evidence

- **Zero external runtime references.** Word-boundary grep of every `.cs` under `Assets/` for all 60 subsystem type names (`LevelGenerator`, `WalkerManager`, `Grid`, `GridNetworkPlayer`, every Step/Action/Condition/Boundary/Data type), excluding the folder itself and `LevelGeneratorEditor.cs`, returned nothing. The grep mechanism was sanity-checked positive (a `Pawn` control found live hits; a `Grid` control found internal hits).
- **Asset references are self-contained.** A scan of every tracked file in `Assets/` for the 60 deleted script GUIDs found matches only inside the level-gen island — the 20 config SOs (each an instance of a deleted type) — plus `Online.unity`'s two dormant component instances. No live/test scene or shared prefab depends on the deleted code.
- **The art prefabs carry no deleted script**, so they are not orphaned by the code removal and were left for a reference-check rather than deleted blind.

## Consequences

- ~2344 lines and the entire flowchart-pipeline mechanism leave the codebase; `Assets/Scripts/` no longer presents a second, dead "player/world" model to every reader.
- **Follow-up owed to editor-side validation** (this change was made git-only, no editor): strip the missing `LevelGenerator` + `WalkerManager` components from `Online.unity`; decide whether the `Test Level/` art prefabs (and `Snow Town Level.prefab`) are referenced by any live scene before removing them.
- **Cheap to reverse:** the subsystem is one `git revert` away in history if procedural levels are ever greenlit — though the expectation is a fresh design against the current model, not a resurrection.

**North-star rationale.** *Elegance:* deletes a whole mechanism and a duplicated vocabulary rather than maintaining a dormant one — the largest single entropy paydown on the modularity board at near-zero blast radius. *Essence:* procedural level generation is not a current system and earns nothing until a feature needs it; carrying it speculatively violates the scope guard.
