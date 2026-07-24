# Working in a git worktree (the editor isn't attached to it)

Companion to `SKILL.md` in this directory — read it whenever you are working from a git worktree, BEFORE any test run or any write to serialized state there. It replaces the parent skill's normal assumption that the editor is on the project you're editing.

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
