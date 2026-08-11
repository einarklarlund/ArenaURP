# 13 — The hook config resolves the project directory in one place

_Repo: **ArenaURP** (tooling layer). Source: [W8 of the trickle-ICE follow-ups spec](../../../docs/plans/trickle-ice-follow-ups-spec.md)._

**What to build:** An agent editing the hook configuration makes the edit once instead of six times. The same roughly-200-character project-directory resolver is inlined six times in the Claude settings file. One dispatcher script the hooks call collapses it.

Out of feature, cheap while the file is open.

**No test seam — stated rather than hidden.** This has no test in either suite. Its only verification is that the hooks still fire. That is acceptable for a tooling change of this size, but it must not be described as *verified* when it is merely *exercised*.

**Blocked by:** None — can start immediately.

**Status:** LANDED 2026-08-11, **partially** — one criterion is not met and cannot be, see below.

## What the ticket asked for that is impossible

"No inlined copy remains" cannot be satisfied in full. **A dispatcher has to be located before it
can dispatch**, and locating it is the very thing being deduplicated. Every hook entry still runs
the three-candidate search — for `dispatch.sh` alone now, not for its own hook.

What did move into `dispatch.sh`: the hook-name path, the executability test, the exec, the
not-found note, and the explanation of why `$CLAUDE_PROJECT_DIR` cannot be trusted on its own.
What each entry still carries is one uniform lookup that differs only in the hook name.

Counted honestly, this is five entries (the spec said six) whose bodies shrank, not five entries
that became one line. **A change to the resolution policy is still a five-place edit.** If that is
worth closing, the shape that would do it is an absolute path or a wrapper the harness resolves —
neither exists today.

- [x] One dispatcher script resolves the project directory
- [ ] ~~All six hook entries call it; no inlined copy remains~~ — all five call it; the dispatcher
      lookup is necessarily still inlined. See above.
- [x] The hooks still fire — EXERCISED, not verified. Each of the five was driven through
      `dispatch.sh`, and `session-start.sh` and `habit-check.sh` additionally through the
      settings.json bootstrap verbatim; the unresolvable case prints to stderr and no-ops.
