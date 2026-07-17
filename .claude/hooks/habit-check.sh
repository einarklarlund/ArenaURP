#!/usr/bin/env bash
# UserPromptSubmit: nudge Einar (and me) toward the good AI-dev habits on EVERY substantive request,
# and drive the frontier-scan cadence. This is the mechanical form of "every time I ask you to do
# something, ensure I'm following the habits" — a memory can't fire per-prompt; a hook can.
#
# Design constraints, because this runs on every prompt:
#   - CHEAP (pure shell, no LLM, no network) and FAST.
#   - TERSE and INTENT-SPECIFIC, or it becomes wallpaper and gets tuned out. Lead with the ONE habit
#     that matches the request; don't dump the whole checklist every time.
#   - SILENT on trivial/conversational turns ("yes", "thanks", "continue").
# It never blocks — a nudge, not a gate. Always exit 0.
set -uo pipefail
cd "${CLAUDE_PROJECT_DIR:-.}" 2>/dev/null || exit 0

payload="$(cat)"
# The documented input field is `prompt` (code.claude.com/docs hooks reference); older builds used
# other names — kept as fallbacks only.
p="$(printf '%s' "$payload" | jq -r '.prompt // .user_input // .user_prompt // .message // ""' 2>/dev/null)"
[[ -n "$p" ]] || exit 0

lc="$(printf '%s' "$p" | tr '[:upper:]' '[:lower:]')"
words="$(printf '%s' "$p" | wc -w | tr -d ' ')"

# Skip pure acknowledgements / continuations — no work to nudge.
if [[ "$words" -le 6 ]] && printf '%s' "$lc" | grep -Eq '^(y|yes|yep|yeah|no|n|nope|ok|okay|k|thanks|thank you|ty|continue|go|go on|go ahead|proceed|do it|sure|great|nice|perfect|cool|stop|wait|hmm|right|correct)\b'; then
  exit 0
fi

nudges=()
add() { nudges+=("$1"); }

# ESSENCE check — fires on any design/feature/scope prompt. The overarching design lens: does this serve
# the game's essence, and does it grow scope past the handful of systems? (Full text: DESIGN.md.)
if printf '%s' "$lc" | grep -Eq '\b(design|implement|add|build|create|feature|system|ability|abilities|upgrade|pickup|projectile|effect|mechanic|enemy|weapon|mode|redesign|should we)\b'; then
  # Inline only the STABLE claim; POINT to the volatile system list (it lives in DESIGN.md and changes —
  # enumerating it here drifted within two turns, which is why this rule exists).
  add "ESSENCE: the game is character-BUILDING via a handful of INTERACTING systems. Test against it: does this deepen an upgrade-driven interaction, or is it a one-off that composes into nothing? Does it add a system beyond the current list (see DESIGN.md — read it, do not assume)? If either, challenge it before building, and guard the essence's size."
fi

# Intent classification — most work-heavy match leads. A prompt can trip several; keep to ~2.
if printf '%s' "$lc" | grep -Eq '\b(design|architect|approach|options|trade-?offs?|should (we|i)|how should|figure out|decide|redesign)\b'; then
  add "This reads as DESIGN. Prefer plan mode (/plan or Shift+Tab) BEFORE any code — settle it in prose. Before the plan is FINAL, run the engineering-canon interview (six lenses; check its shared-understandings register before re-asking). If a real decision gets made, it wants an ADR in Docs/design/ (see DESIGN.md)."
fi
if printf '%s' "$lc" | grep -Eq '\b(implement|add|build|create|write|wire|codify|refactor|rewrite|change|introduce|hook up|make (it|a|the))\b'; then
  add "This reads as BUILD. First: is it leaf/dev-only (tooling, converter, one-off)? Then fast-path it — try+verify, skip the interview (ai-workflow 'two speeds'). If it touches networked/core code (SyncVar/RPC/[Server]/the hit-exchange web), it's the gauntlet: say the AI-effective plan first (what delegates to unity-scout, what wants plan mode), name the test that will PROVE it before writing it, gameplay logic -> Core processor not the MonoBehaviour."
fi
if printf '%s' "$lc" | grep -Eq '\b(where|how does|how do|find|locate|which file|trace|investigate|understand|look into|search for|what references|what uses)\b'; then
  add "This reads as INVESTIGATE. Delegate it to @agent-unity-scout rather than reading files in the main thread — main-thread context is for the design. Anything crossing a prefab/scene boundary MUST use the Unity MCP, not grep."
fi
if printf '%s' "$lc" | grep -Eq '\b(fix|bug|broken|crash|failing|regress|wrong|not working|doesn.?t work|nre|exception)\b'; then
  add "This reads as FIX. Reproduce first (a failing test is the ideal repro), then fix, then re-run. Delegate the run to @agent-unity-validator."
fi
if printf '%s' "$lc" | grep -Eq '\b(test|verify|run the|check that|make sure it|confirm it)\b'; then
  add "Delegate test/verify to @agent-unity-validator — it knows the stale-XML and stale-domain traps and returns only the verdict."
fi
if printf '%s' "$lc" | grep -Eq '\b(commit|stage|push|pr\b|pull request)\b'; then
  add "Follow the commit-conventions skill: features/behaviour not implementation; scope the commit; separate the .claude/ tooling from the gameplay change; NO Claude co-author."
fi
if printf '%s' "$lc" | grep -Eq '\b(assess|evaluate|is (this|that|it|the) .*(good|right|worth|working)|should (we|i) keep|skeptical|does (this|it) (work|deliver|accomplish)|critique|reconsider|worth it|too complex|over-?engineer)\b'; then
  # Dispatch to the canonical method; do NOT restate it here (one copy, in the skill).
  add "This reads as ASSESS. Load the \`engineering-canon\` skill and follow its evidence method ('Questioning an existing design') — do not improvise one. The imperative it is easiest to skip: do NOT just agree; delegate the critique to @agent-design-critic, which does not share my sunk cost."
fi

# Frontier-scan cadence: every Nth substantive prompt, prompt a re-evaluation. This operationalizes
# "continually evolve and re-evaluate" so it happens on a schedule, not only when the user asks.
COUNT_FILE=".claude/.habit-count"
n="$(cat "$COUNT_FILE" 2>/dev/null || echo 0)"; n=$((n + 1)); printf '%s' "$n" > "$COUNT_FILE" 2>/dev/null || true
if (( n % 20 == 0 )); then
  add "FRONTIER (every 20 tasks): re-evaluate ONE adopted practice and scan for ONE new one against current best practice. Update .claude/skills/ai-workflow/practices-register.md with the verdict + date."
fi

[[ ${#nudges[@]} -eq 0 ]] && exit 0

ctx="WORKFLOW HABITS (habit-check hook — nudge, not a gate; see the ai-workflow skill):"
for x in "${nudges[@]}"; do ctx+=$'\n  - '"$x"; done
ctx+=$'\nState the plan and what you will delegate BEFORE acting. If a habit does not apply here, say so in one line and move on.'

jq -n --arg c "$ctx" '{hookSpecificOutput:{hookEventName:"UserPromptSubmit", additionalContext:$c}}' 2>/dev/null || true
exit 0
