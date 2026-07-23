---
name: workflow-artifacts
description: Picking the right artifact — hook, skill, subagent, or memory — to encode a piece of AI-development tooling, and evolving that layer under the Elegance discipline. Use when adding or editing anything under .claude/, when a process feels repetitive or error-prone, or when a violated guideline needs a guardrail.
---

# Workflow artifacts

The `.claude/` layer is code: versioned, reviewed, improved when it fails, under the same [[design-elegance]] discipline as the game — the fewest parts that cover the cases. Collapse two artifacts into one before adding a third.

The *process* of shipping a feature is the mattpocock flow — `grill-with-docs` → `to-spec` → `to-tickets` → `implement` → `code-review`, plus `diagnosing-bugs`, `prototype`, `research`, `wayfinder`; ask `ask-matt` when unsure which fits. This skill is the project-specific layer beneath that flow. Authoring a skill well — invocation, leading words, progressive disclosure — is the mattpocock `writing-great-skills` reference.

## Pick by what you're encoding

A rule you must *remember* is a hope, not a rule. Encode it where the harness enforces it:

| To… | Use | Because | Lives in |
|---|---|---|---|
| make a wrong action **impossible** | **Hook** | the harness runs it — 100% compliance; a skill in context can't interrupt a tool call | script in `.claude/hooks/`, registered in the committed `.claude/settings.json` `hooks` block |
| explain **judgment**, or replay a **procedure** you keep re-deriving | **Skill** | loads on trigger; holds prose a regex can't. A manual one-shot is a skill with `disable-model-invocation: true` | `.claude/skills/<name>/SKILL.md` |
| keep a **noisy search or run** out of the main context | **Subagent** | its context is separate; only the conclusion returns. `skills:` frontmatter preloads doctrine instead of copying it | `.claude/agents/<name>.md` |
| persist a **fact or norm** across sessions | **Memory** | indexed in `MEMORY.md`; bodies load on demand | the auto-memory dir |

**Litmus:** mechanically detectable (a tool × a file extension, a code shape) → hook. Needs judgment → skill. Costs *attention* → subagent. Costs *typing* → a manual-only skill.

## The inventory IS the directory

Keep no separate list of what exists — a copy drifts, and the auditor would then be detecting rot this structure prevents. Each artifact self-indexes: the `.claude/settings.json` `hooks` block is the hook registry, and a script without an entry there is dead code (all five hooks once died silently for two days when their registration lived only in the un-committed `settings.local.json` — it goes in the *committed* file); subagents appear in the session's agent list; skills self-index via their frontmatter descriptions; `MEMORY.md` indexes memories. `@agent-workflow-auditor` audits the whole layer for drift and sprawl.

A subagent added, or a `settings.json` change made, mid-session is picked up only next session.

## Evolving the layer

- **Earn each artifact with a real failure or repetition**, and write that failure into it as its example — a rule with a scar is remembered. An artifact added "in case it's useful" is the [[no-speculative-interfaces]] rule wearing a workflow hat.
- **Improve both ways.** *Failure-patching* codifies what just went wrong; *frontier-scanning* adopts practices you don't yet feel the pain of missing — invisible to failure-patching, so you adopt them deliberately or never. Both feed the practices register ([practices-register.md](practices-register.md)), the dated Adopted / Watching / Rejected ledger; its ~20-prompt re-evaluation is nudged by `habit-check`.
- **A guardrail that repeatedly blocks correct work is evidence the doctrine is wrong** — feed the collision into a design review rather than softening the hook. (The one-concretion nudge killing the `effect-commands` route in two independent spikes decided ADR 0002.)
- **Repairing a violated guardrail is part of the fix** ([[proactive-skill-maintenance]]). After any rename or architectural change, run `@agent-workflow-auditor` before trusting the docs.
