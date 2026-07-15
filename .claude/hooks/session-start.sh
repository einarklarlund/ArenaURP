#!/usr/bin/env bash
# SessionStart: thirty seconds of orientation, injected into context before the first turn.
#
# Answers the questions that, unanswered, cause expensive mistakes:
#   - Is the Unity editor actually reachable? (unity_* MCP tools silently target nothing if not)
#   - Are there uncommitted Unity assets? (a prefab that is clean-vs-HEAD when it shouldn't be means a save reverted)
#   - What did the last test run say, and when? (so a stale green isn't mistaken for a fresh one)
#
# It also injects a SUMMARY of the standing mandates (canonical text: the ai-workflow-coaching memory),
# because a memory can be skimmed past and a SessionStart injection cannot. Summary + pointer, never a full
# restatement — the memory is the single source (see the engineering-canon shared-understandings register).
# Matchers cover startup/resume/clear/compact — including after a compaction has evicted the memory.
#
# Must never fail and never be slow. Always exit 0.
set -uo pipefail
cd "${CLAUDE_PROJECT_DIR:-.}" 2>/dev/null || exit 0

# --- Unity editor reachability -------------------------------------------------------------------
if pgrep -x Unity >/dev/null 2>&1; then
  unity="RUNNING (unity_* MCP tools should work)"
else
  unity="NOT RUNNING — every unity_* MCP tool will fail. Ask the user to open the project, or validate via batchmode (see the unity-worktree-headless-validation memory)."
fi

branch="$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo '?')"
assets="$(git status --porcelain -- '*.prefab' '*.unity' '*.asset' 2>/dev/null | wc -l | tr -d ' ')"

# --- last test verdicts (heredoc'd python: no nested quoting) ------------------------------------
verdicts="$(python3 - <<'PY' 2>/dev/null
import json, os, datetime, xml.etree.ElementTree as ET

def when(p):
    return datetime.datetime.fromtimestamp(os.path.getmtime(p)).strftime("%b %d %H:%M")

p = "Temp/edit-mode-test-results.json"
if os.path.exists(p):
    d = json.load(open(p))
    print(f'  Last EditMode: {d["passed"]} passed / {d["failed"]} failed (as of {when(p)})')
else:
    print("  Last EditMode: none recorded")

# Unity's TestResults.xml is overwritten by WHICHEVER runner ran last (EditMode or PlayMode), so the
# mode must be detected, never assumed. Arena's PlayMode fixtures live in the Arena.PlayTests namespace.
x = os.path.expanduser("~/Library/Application Support/DefaultCompany/ArenaURP/TestResults.xml")
if os.path.exists(x):
    r = ET.parse(x).getroot()
    names = (tc.get("fullname") or "" for tc in r.iter("test-case"))
    mode = "PlayMode" if any(n.startswith("Arena.PlayTests") for n in names) else "EditMode"
    print(f'  Last TestRunner XML: {mode} — {r.get("passed")} passed / {r.get("failed")} failed '
          f'(as of {when(x)}). STALE unless the file was deleted immediately before that run.')
else:
    print("  Last TestRunner XML: none recorded")
PY
)"
[[ -n "$verdicts" ]] || verdicts="  Test verdicts: unavailable"

read -r -d '' ctx <<EOF || true
ARENA SESSION ORIENTATION
  Branch: $branch   |   Uncommitted Unity assets: $assets
  Unity editor: $unity
$verdicts

DELEGATION (see the \`ai-workflow\` skill)
  - >3 files to answer, or any question crossing a prefab/scene/GUID boundary -> spawn \`unity-scout\`.
  - Running or parsing tests, or compile-checking -> spawn \`unity-validator\`. Never poll test XML in this thread.
  - Open design question -> plan mode BEFORE code.
  Main-thread context is for the design. Skill drift is an attention failure, not a memory failure.

STANDING MANDATES (canonical statement: the ai-workflow-coaching memory — read it before acting on these)
  - Educator: teaching Einar to use AI effectively is an explicit project goal. Surface the AI-effective
    way and unused capabilities unprompted; end substantial tasks naming ONE workflow improvement.
  - Anti-sycophancy: owe him the strongest counterargument before agreeing; turn fuzzy goals into
    measurable tests; when invested in the thing under review, delegate the critique to design-critic.
EOF

jq -n --arg c "$ctx" '{hookSpecificOutput:{hookEventName:"SessionStart", additionalContext:$c}}' 2>/dev/null || true
exit 0
