#!/usr/bin/env bash
# Stop hook: refuse to end a turn that edited C# without letting Unity re-import and checking the console.
#
# The failure this closes: `unity_execute_code` compiles against the CURRENTLY-LOADED domain. Right after
# a .cs edit it reports `type not found` for brand-new types that in fact compile perfectly. Worse, real
# compile errors sit silently in the Unity console while the model concludes "everything's fine."
# Both happened in one session.
#
# Mechanics: skill-nudge.sh drops a sentinel whenever a .cs under Assets/ is written. This hook consumes
# the sentinel (removing it FIRST, so a second Stop cannot block again -> no loop) and blocks once, telling
# the model to do the refresh + console check through the Unity MCP. The hook cannot call MCP itself, and
# must never hit the Unity HTTP bridge directly.
set -uo pipefail
cd "${CLAUDE_PROJECT_DIR:-.}" 2>/dev/null || exit 0

SENTINEL=".claude/.cs-dirty"
[[ -f "$SENTINEL" ]] || exit 0

files="$(cat "$SENTINEL" 2>/dev/null | sort -u | head -8)"
rm -f "$SENTINEL"          # consume FIRST: this hook can block at most once per edit batch

# If the editor isn't running there is nothing to refresh; don't block on an impossible instruction.
pgrep -x Unity >/dev/null 2>&1 || exit 0

reason="You edited C# this turn but never let Unity re-import it:
$(printf '%s\n' "$files" | sed 's/^/  - /')

Before finishing, via the Unity MCP:
  1. unity_execute_code: UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
  2. unity_console_log with logType=Error  -> confirm there are no compile errors.
  3. Verify NEW types by reflection over AppDomain.CurrentDomain.GetAssemblies(), NOT typeof(NewType):
     unity_execute_code compiles against the stale domain and will report 'type not found' for code
     that compiles fine.

If a test suite should have run, delegate it to the unity-validator subagent rather than polling here.
(This gate fires once per edit batch; it will not block you again.)"

jq -n --arg r "$reason" '{decision:"block", reason:$r}' 2>/dev/null || exit 0
exit 0
