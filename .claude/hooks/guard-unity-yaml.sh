#!/usr/bin/env bash
# PreToolUse guard enforcing the `unity-mcp-vs-shell` skill mechanically.
#
# A skill description sits in Claude's context but cannot interrupt a tool call. This can.
# It blocks the two things that skill calls out as always-wrong:
#   1. Bash text tools (grep/rg/cat/sed/awk/head/tail/...) pointed at Unity serialized YAML.
#      Those files store script GUIDs, not names — text search reports confident nonsense.
#   2. Write/Edit against Unity serialized YAML. Hand-editing corrupts GUIDs/fileIDs/RefIds.
#
# Escape hatch: append `# unity-yaml-ok` to a Bash command to override. The point is to force
# a deliberate decision, not to make the operation impossible.
#
# Exit 2 = block, stderr is fed back to the model.
set -uo pipefail

payload="$(cat)"
tool="$(printf '%s' "$payload" | jq -r '.tool_name // ""')"

# Extension at END of a token, so `com.unity.render-pipelines` and `Assets/Prefabs` don't match.
EXT_RE='\.(prefab|unity|asset|meta)($|[[:space:]"'"'"';|)&])'

case "$tool" in
  Bash)
    cmd="$(printf '%s' "$payload" | jq -r '.tool_input.command // ""')"
    [[ "$cmd" == *"# unity-yaml-ok"* ]] && exit 0
    # Only text-inspection/mutation tools. `find`, `ls`, `git`, `mv` are fine.
    if printf '%s' "$cmd" | grep -Eq '(^|[|;&[:space:]])(grep|rg|egrep|fgrep|cat|sed|awk|head|tail|less|more|nl|strings|perl)([[:space:]]|$)' \
    && printf '%s' "$cmd" | grep -Eq "$EXT_RE"; then
      cat >&2 <<'EOF'
BLOCKED by unity-mcp-vs-shell: this reads/edits Unity serialized YAML with a text tool.

Unity .prefab/.unity/.asset/.meta store script GUIDs and fileIDs, not names. grep sees a raw
GUID it cannot resolve, and misses references living in YAML you didn't think to search — so it
returns confident, wrong answers.

Use the Unity MCP instead:
  - prefab/scene structure ....... unity_prefab_info, unity_scene_hierarchy
  - a component's serialized values .. unity_component_get_properties
  - "what references this?" ....... unity_search_missing_references, find_script_references
  - anything else / bulk queries ... unity_execute_code (load via AssetDatabase, query by TYPE)

If you genuinely need raw text (e.g. reading a .meta guid to wire an asmdef), append
`# unity-yaml-ok` to the command to override deliberately.
EOF
      exit 2
    fi
    ;;
  Write|Edit|MultiEdit|NotebookEdit)
    fp="$(printf '%s' "$payload" | jq -r '.tool_input.file_path // ""')"
    if printf '%s' "$fp" | grep -Eq '\.(prefab|unity|asset|meta)$'; then
      cat >&2 <<EOF
BLOCKED by unity-mcp-vs-shell: never hand-edit Unity serialized YAML ($fp).

Hand-editing corrupts GUIDs, fileIDs, and [SerializeReference] RefIds. Route the mutation through
the Unity MCP so the editor owns serialization:
  - components/refs ... unity_component_add, unity_component_set_reference, unity_component_set_property
  - prefabs/objects ... unity_asset_create_prefab, unity_gameobject_*
  - SerializeReference lists / anything bespoke ... unity_execute_code
      (PrefabUtility.LoadPrefabContents -> mutate -> SaveAsPrefabAsset -> UnloadPrefabContents)
  - new scripts ....... unity_script_create (never hand-write a .cs.meta)

Exception: a git worktree the editor isn't open on. Then hand-editing is sanctioned but narrow —
see the skill's worktree section.
EOF
      exit 2
    fi
    ;;
esac
exit 0
