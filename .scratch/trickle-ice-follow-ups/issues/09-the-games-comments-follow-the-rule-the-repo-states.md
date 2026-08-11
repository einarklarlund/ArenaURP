# 09 — The game's comments follow the rule the repo states

_Repo: **ArenaURP**. Source: [W4 of the trickle-ICE follow-ups spec, game half](../../../docs/plans/trickle-ice-follow-ups-spec.md)._

**What to build:** An agent or a new reader who imitates the surrounding code produces compliant code rather than violations. The rule — **no comments except where the code is not self-explanatory** — is recorded in `CLAUDE.md`, and the commit-conventions skill that formerly cited "doc comments" as justification has already been corrected. Both were done in the grill session. What remains is the sweep of code written before the rule was written down.

**Modules to sweep:** the local UI event definitions, the room manager, the UI manager, and the loading view.

**The judging line:** a comment survives only where the knowledge has **no other home in the file** and cannot be expressed in code — a retired wire byte that must never be reused, a timing bound, a buffer that must be sliced exactly. Anything restating what the code does, goes. *Why* belongs in an ADR or a skill, where it cannot rot away from the code.

Where a comment is carrying knowledge that could be code, prefer making the code say it over keeping the comment.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [ ] The four modules are swept against the stated line
- [ ] Every surviving comment carries knowledge with no other home in the file that cannot be expressed in code
- [ ] Knowledge that belongs in an ADR or skill is moved there or already lives there, not paraphrased in the file
- [ ] The end-to-end join fixture is green — a comment sweep that changes behaviour has removed something that was not a comment
