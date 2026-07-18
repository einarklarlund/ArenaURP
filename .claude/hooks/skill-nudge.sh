#!/usr/bin/env bash
# PostToolUse nudge: re-surface a skill at the moment code matching its trigger is written.
#
# Companion to guard-unity-yaml.sh. That one BLOCKS mechanically-wrong operations. This one only
# NUDGES: it detects code shapes whose governing skill is easy to forget mid-task and injects the
# reminder back into context, right after the write, while the change is still cheap to fix.
#
# It is a heuristic, not a gate — it cannot know whether an interface has one concretion or three.
# It just asks the question the skill would have asked. Never blocks; always exit 0.
set -uo pipefail

payload="$(cat)"
fp="$(printf '%s' "$payload" | jq -r '.tool_input.file_path // ""')"
[[ "$fp" == *.cs ]] || exit 0

# Drop a sentinel for the Stop-time compile gate (cs-compile-gate.sh): Unity must re-import edited C#
# before anyone trusts a `unity_execute_code` result or a green test run.
# Skip worktree paths: the editor never imports .claude/worktrees/, so a spike agent's edits there
# false-positived the gate on the main session (observed 2026-07-13 during the IEffect review).
if [[ "$fp" == *"/Assets/"* && "$fp" != *"/.claude/worktrees/"* ]]; then
  printf '%s\n' "${fp#*/Assets/}" >> "${CLAUDE_PROJECT_DIR:-.}/.claude/.cs-dirty" 2>/dev/null || true
fi

# Write -> .content, Edit -> .new_string
body="$(printf '%s' "$payload" | jq -r '.tool_input.content // .tool_input.new_string // ""')"

notes=()

if printf '%s' "$body" | grep -Eq '(SyncVar<|SyncList<|\[ServerRpc\]|\[ObserversRpc\]|\[TargetRpc\])'; then
  notes+=("fishnet-conventions: you just wrote a SyncVar/SyncList/RPC. Rule 1 — can the receiver DERIVE this from an id it already has? Rule 4 — ServerOnly write permission, mutated only in a [Server] method. Rule 5 — this change now REQUIRES a PlayMode test.")
fi

if printf '%s' "$body" | grep -Eq '^[[:space:]]*(public|internal)[[:space:]]+interface[[:space:]]+I[A-Z]'; then
  notes+=("no-speculative-interfaces / component-design: you just declared an interface. How many REAL concretions does it have? If one, delete it and use the concrete type — an interface earns its keep only at the SECOND real case (rule of two). When it does, it is a capability ON the component that carries the behaviour, discovered by presence (the IHitHandler shape), not a parallel abstraction.")
fi

if [[ "$fp" == *"/Tests/PlayMode/"* ]]; then
  notes+=("integration-testing: PlayMode test written. (a) Could this live in EditMode instead? (b) CAN THIS TEST FAIL? A test asserting the ABSENCE of an effect (not stunned / did not move / no damage) passes when the feature is entirely missing — pair it with a positive control in the same fixture. (c) Assert observable behaviour, not internal structure.")
fi

[[ ${#notes[@]} -eq 0 ]] && exit 0

ctx="SKILL REMINDER (triggered by what you just wrote to ${fp##*/}):"
for n in "${notes[@]}"; do ctx+=$'\n  - '"$n"; done

jq -n --arg c "$ctx" \
  '{hookSpecificOutput: {hookEventName: "PostToolUse", additionalContext: $c}}'
exit 0
