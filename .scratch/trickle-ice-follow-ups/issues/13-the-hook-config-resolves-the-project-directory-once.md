# 13 — The hook config resolves the project directory in one place

_Repo: **ArenaURP** (tooling layer). Source: [W8 of the trickle-ICE follow-ups spec](../../../docs/plans/trickle-ice-follow-ups-spec.md)._

**What to build:** An agent editing the hook configuration makes the edit once instead of six times. The same roughly-200-character project-directory resolver is inlined six times in the Claude settings file. One dispatcher script the hooks call collapses it.

Out of feature, cheap while the file is open.

**No test seam — stated rather than hidden.** This has no test in either suite. Its only verification is that the hooks still fire. That is acceptable for a tooling change of this size, but it must not be described as *verified* when it is merely *exercised*.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [ ] One dispatcher script resolves the project directory
- [ ] All six hook entries call it; no inlined copy remains
- [ ] The hooks still fire — confirmed by exercising them, and reported as exercised rather than verified
