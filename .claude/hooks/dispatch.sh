#!/bin/sh
# Runs one of this repo's hooks by name, from whichever checkout the session is really in.
#
# $CLAUDE_PROJECT_DIR is the session's LAUNCH dir, not this repo: start claude in the multi-repo
# parent (~/Projects) and it points outside ArenaURP, so every hook dies with "No such file or
# directory". Candidate order below: git toplevel (covers worktrees), $CLAUDE_PROJECT_DIR, then
# $CLAUDE_PROJECT_DIR/ArenaURP. Unresolvable is a silent no-op with a stderr note - a hook that
# cannot be found must never take the session down with it.
#
# Every entry in settings.json calls this; the roots live here so the next hook edit is one edit.

hook=".claude/hooks/$1"
shift

for root in "$(git rev-parse --show-toplevel 2>/dev/null)" "$CLAUDE_PROJECT_DIR" "$CLAUDE_PROJECT_DIR/ArenaURP"; do
    [ -n "$root" ] && [ -x "$root/$hook" ] && exec "$root/$hook" "$@"
done

echo "hook not found: $hook" >&2
