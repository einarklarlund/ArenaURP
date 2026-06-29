---
name: unity-mcp-vs-shell
description: When to reach for the Unity MCP (unity_* tools) versus plain shell/file tools (grep, find, sed, Read, Edit, Write) for the same operation in this project. The two overlap heavily for reading and searching but diverge sharply for writes and for anything that crosses a GUID/serialization boundary. Use this whenever you are about to inspect, search, read, or mutate a Unity asset/scene/prefab/script, or run a build/test, and you have a choice between an editor round-trip and a file-system operation. Consult this BEFORE hand-editing .unity/.asset/.prefab/.meta YAML, and BEFORE spinning up an MCP call for something a grep would answer instantly.
---

# Unity MCP vs. shell: picking the right tool for the operation

This project keeps a Unity Editor instance connected over the Unity MCP (`unity_*` tools, bridge "AB-UMCP"). Almost every operation you'd do through the MCP has a shell/file-tool equivalent (`grep`, `find`, `sed`, `Read`, `Edit`, `Write`) and vice-versa. They are **not** interchangeable. This skill is the decision procedure.

The standing project rule (from memory `[[use-unity-mcp-for-editor-actions]]` and CLAUDE.md) is: **use the MCP for actions inside the editor.** This skill refines that — it says *which* operations are genuinely "editor actions" that must go through the MCP, and which are plain text/file work where the shell is faster, cheaper, and works even when Unity is closed.

## The three questions that decide it

Ask these in order. The first one that applies wins.

1. **Does the operation MUTATE serialized Unity state?** (an `.asset`, `.prefab`, `.unity`, `.meta`, a component reference, a `[SerializeReference]` list, import settings) → **Unity MCP, always.** Hand-editing YAML corrupts GUIDs, fileIDs, and SerializeReference RefIds. The editor API is the only thing that keeps meta/serialization consistent. The sole exception is the *body* of an existing `.cs` file — that's just text (see below).

2. **Does answering require resolving a GUID, fileID, or cross-asset reference?** ("what references this script?", "what's wired into this prefab slot?", "what materials use this shader?", "any missing/broken refs?") → **Unity MCP.** grep sees the raw GUID string but can't tell you which human-named asset it belongs to, and misses references that live in prefab/scene YAML you didn't think to search. The MCP resolves the graph.

3. **Otherwise it's plain text or a file listing** → **shell/file tools.** Reading a script, grepping for a symbol, listing files, find-by-name, a sed survey — these are faster, free, don't queue behind the editor, and work with Unity closed. Don't pay an MCP round-trip for them.

## Quick reference

| Operation | Prefer | Why |
|---|---|---|
| Read a `.cs` script body | `Read` | Plain text. `unity_script_read` only wins if Unity holds unsaved buffer state. |
| Edit a `.cs` script body | `Edit`/`Write` | Just text. Save, then `AssetDatabase.Refresh()` via MCP so Unity owns the import. |
| Create a NEW `.cs` file | `unity_script_create` | Writes the script AND generates the `.meta`/GUID. Never hand-write `.cs.meta`. |
| Grep for a symbol / string in scripts | `grep`/`Grep` | Instant, no editor queue, works offline. |
| Survey many files (counts, patterns) | `find`/`grep`/`sed` | Bulk text work; MCP has no advantage. |
| "What references this script/asset?" | **MCP** (`find_script_references`/`find_references`/`unity_search_missing_references`) | Resolves GUIDs + prefab/scene refs grep can't. |
| List typed Unity assets (prefabs, SOs, scenes, materials) | **MCP** (`unity_asset_list`, advanced `list_prefabs`/`list_scriptable_objects`/`list_all_scenes`) | Knows asset *types*; more reliable than `find -name "*.prefab"` but find is an OK fallback. |
| Inspect a scene/prefab structure | **MCP** (`unity_scene_hierarchy`, `unity_scene_info`, `unity_prefab_info`, advanced `get_*_summary`) | Parses YAML into a real hierarchy; reading raw `.unity` YAML is error-prone. |
| Read a component's serialized property values | **MCP** (`unity_component_get_properties`) | Resolves fileID refs to names. |
| Create/modify a prefab, GameObject, component, material, scene | **MCP** (`unity_gameobject_*`, `unity_component_*`, `unity_asset_create_prefab`, `unity_material_create`, `unity_renderer_set_material`, `unity_scene_*`) | Mutates serialized state — rule #1. |
| Author/edit a ScriptableObject or `[SerializeReference]` list | **MCP** (`unity_execute_code`: load → mutate → `SetDirty` → `SaveAssets`) | SerializeReference RefIds are unsafe to hand-edit. |
| Wire component references between objects | **MCP** (`unity_component_set_reference`, `unity_component_batch_wire`, `unity_set_object_reference`) | fileID/GUID wiring. |
| Build the game | **MCP** `unity_build`, or CLI `Unity -executeMethod BuildScripts.*` | MCP if editor is up; CLI for headless/CI (see CLAUDE.md Build & Run). |
| Run EditMode/PlayMode tests | **MCP** if the editor is open on THIS checkout; CLI `Unity -batchmode -runTests` if it isn't (e.g. a worktree — see below) | MCP can't attach to a path the editor isn't open on. |
| Run/menu/play-mode/code in editor | **MCP** (`unity_execute_menu_item`, `unity_play_mode`, `unity_execute_code`) | Only the editor can do these. |

## Tie-breakers when both would work

- **Is Unity even running?** The shell works regardless; the MCP needs a connected, responsive editor. For read/search work where the shell is adequate, prefer it and don't block on the editor. (If a *mutation* is needed and the MCP is disconnected, ask the user to reconnect — do **not** fall back to hand-editing YAML.)
- **Cost / latency.** MCP calls queue behind the editor's main thread and the multi-agent queue. A `grep` is instant and free. For anything text-addressable, the shell wins on speed.
- **Precision of the answer.** If the question is "does this string appear," grep. If it's "what is the meaning of this GUID / what depends on this," MCP. grep can *find* a GUID; only the MCP can *name* it.
- **Bulk vs. single.** Large surveys (count usages across 1000 scripts, pattern-match across files) → shell. One targeted reference-resolution or one asset mutation → MCP.

## Working in a git worktree (the editor isn't attached to it)

The Unity MCP binds to **one running editor at one project path** (normally the main checkout). A git
worktree under `.claude/worktrees/<name>/` is a **different project path with no editor attached**, so
*every MCP tool targets the main checkout, not your worktree* — `unity_list_instances` will show the editor
on `/…/ArenaURP`, never on the worktree. This breaks the skill's normal assumption that "the editor is on
the project I'm editing." Inside a worktree the decision procedure shifts:

- **Running tests → batchmode CLI against the worktree path** (the MCP can't reach it):
  ```
  Unity -batchmode -nographics -projectPath <worktree> \
        -runTests -testPlatform <editmode|playmode> \
        -testFilter "Arena\.PlayTests\.(Owner|Ability|Projectile)"   # optional, scopes the run \
        -testResults <out>/results.xml -logFile <out>/run.log
  ```
  Exit code 0 = all passed; non-zero = failures or compile errors. Parse `results.xml`
  (`test-run total=/passed=/failed=`); the `-testResults` path you pass is authoritative — do NOT look in
  the editor's `~/Library/Application Support/.../TestResults.xml` (that's the in-editor RunFiltered path and
  belongs to the main checkout). PlayMode runs headless under `-nographics` for this project's
  FishNet-host fixtures.

- **This is SAFE to run while the main editor is open** — and it's the key reason worktrees work at all: the
  worktree is a separate project path with its **own `Library/`**, so there's no AssetDatabase / import lock
  contention with the editor's checkout. The two instances don't share state. (Verified: a batchmode run on
  the worktree completed cleanly with the editor live on main.)
  - **Licensing caveat:** a second concurrent Unity instance needs a license seat. It resolved fine here
    (the log shows `Successfully updated license`), but if a run dies early with a licensing error, the seat
    is the cause — close the editor for the run, or run the editor's own checkout instead.
  - **First run is a COLD import** (no `Library/` yet — minutes, churns CPU alongside the editor). The
    worktree's `Library/` then persists, so later runs are warm/fast. **Don't delete it between runs.**

- **Creating a `.cs` in a worktree:** `unity_script_create` is unavailable, so hand-write the script AND a
  **minimal** `.meta` — `fileFormatVersion: 2` + a fresh 32-hex `guid` (`uuidgen | tr -d - | tr A-F a-f`).
  This is the **sanctioned exception** to "never fabricate `.cs.meta`": it is exactly what Unity generates
  for a plain script, and the real editor validates/normalizes it on the next import. Only for plain scripts
  (no custom ScriptedImporter).

- **Mutating an asset in a worktree:** the MCP write path is unavailable, so a hand-edit of `.asset`/`.prefab`
  YAML is sometimes unavoidable — but keep it the **narrow exception**, not the habit:
  - Prefer code changes that let Unity do the work: remove a serialized field from the class and a batchmode
    reimport **drops** it from the YAML; add a field with a C# initializer and the reimport **fills** the
    default. Often you only need to delete now-dead lines and let defaults flow.
  - For unavoidable edits, change only scalar values / `{fileID,guid}` refs, **verify by reading back**, then
    run a batchmode pass so Unity **reserializes the asset canonically**.
  - **Never** hand-edit `[SerializeReference]` `RefIds` or fileID component wiring in a worktree — those are
    not safely hand-authorable. Defer that work until the branch is merged into a checkout the editor can
    open, then do it through the MCP.

- **When the worktree merges back to main**, the open editor reimports the changed scripts/assets — the
  same `AssetDatabase.Refresh()` you'd trigger after any out-of-band write, just driven by the merge.

## Hard rules (do not violate)

- **Never** hand-edit `.unity`, `.asset`, `.prefab`, or `.meta` YAML to change Unity state **when the editor is on this checkout** — route through the MCP (rule #1). Reading them for orientation is always fine. The *only* place hand-editing is sanctioned is a **worktree the editor can't reach** (see "Working in a git worktree"), and even there keep it to scalar/ref edits, verify by reading back, let a batchmode reimport reserialize, and never touch SerializeReference RefIds or fileID wiring.
- **Never** fabricate a `.cs.meta` file with `printf`/`echo`/`Write` when the editor is on this checkout — create scripts via `unity_script_create` so Unity owns the GUID. The sole exception is a **worktree** (no MCP reachable): hand-write a minimal `fileFormatVersion: 2` + fresh `guid` meta, which the editor validates on next import.
- After any out-of-band file write that Unity needs to know about (e.g. editing a `.cs` body with `Edit`), run `AssetDatabase.Refresh()` via the MCP so the editor re-imports.
- Discover advanced/graph tools with `unity_list_advanced_tools` rather than guessing tool names; the reference-resolution helpers (`find_script_references`, `get_prefab_dependencies`, `list_materials_using_shader`, etc.) live there.

## One-line heuristic

**Text in, text out → shell. GUIDs, the asset graph, or any write to serialized state → Unity MCP.**
