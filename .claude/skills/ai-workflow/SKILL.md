---
name: ai-workflow
description: Picking the right workflow artifact — hook, skill, subagent, command, or memory — plus the delegation rule and how the workflow layer evolves. Use when adding or editing anything under .claude/, when a process feels repetitive or error-prone, when a guideline was violated, or when starting a task big enough to need delegation or plan mode.
---

# The AI development workflow for this project

Treating "how we use Claude" as part of the codebase — versioned, reviewed, improved when it fails — and under the same **Elegance** value as the code (`DESIGN.md`): the fewest parts that cover the cases. Prefer collapsing two artifacts into one over adding a third; when two rules say the same thing, merge them.

The standing mandates (educator, anti-sycophancy) are *always-on norms*, so by this skill's own table they live as a memory — [[ai-workflow-coaching]], summarized into every session by the `session-start` hook — not here.

## The five artifacts — pick by what kind of thing you're encoding

The single most common mistake is writing a *rule* where a *gate* was needed. A rule you must remember is not a rule; it is a hope.

| You want to… | Use | Why | Lives in |
|---|---|---|---|
| Make a wrong action **impossible** | **Hook** | The harness runs it. Compliance is 100% and constant. A skill in context cannot interrupt a tool call. | `.claude/settings.json` + `.claude/hooks/*.sh` |
| Explain **judgment** (when, why, trade-offs) | **Skill** | Loads on demand by trigger; holds prose a regex can't. | `.claude/skills/<name>/SKILL.md` |
| Keep a **noisy search or run** out of the main context | **Subagent** | Its context is separate; only its conclusion returns. | `.claude/agents/<name>.md` |
| Replay a **multi-step procedure** you keep re-deriving | **Command** | User-invoked, argument-taking, one shot. | `.claude/commands/<name>.md` |
| Persist a **fact or norm** across sessions | **Memory** | Indexed in `MEMORY.md`, loaded every session. | the auto-memory dir |

**Litmus:** if the failure is mechanically detectable (a tool × a file extension, a code shape) → hook. If it needs judgment → skill. If it costs *attention* → subagent. If it costs *typing* → command.

## The delegation rule (the highest-leverage habit here)

**Main-thread context is for the design. Searching and validating are somebody else's job.**

Skill drift is not a memory failure, it is an attention failure: after fifteen file reads and a prefab dump, the skill you loaded at turn 2 is no longer psychologically present. So:

- More than ~3 files to answer a question, or the question crosses a Unity GUID boundary → **`unity-scout`**.
- Running tests, compile-checking, parsing test XML → **`unity-validator`**.
- A genuinely open design question with several viable shapes → **plan mode** (`/plan`, or Shift+Tab) before writing any code — and before any plan is finalized, the [[engineering-canon]] interview.

A subagent returns a conclusion. If it returns file dumps, its prompt is wrong — fix the agent, not the habit.

## The inventory IS the directory

This skill keeps no inventory of hooks, agents, skills, or memories — a copy drifts, and every change would have to be recorded twice; the auditor would then be *detecting* rot this structure simply prevents. The single sources: hooks in `.claude/hooks/` + `.claude/settings.json` (each hook's header comment states what it gates); subagents self-describe in the session's agent list; skills self-index via their frontmatter descriptions (loaded into every session; `CLAUDE.md` points at the load-bearing ones); `MEMORY.md` indexes memories; design rationale in `DESIGN.md` + `Docs/design/*.md` ADRs; the practices ledger in [practices-register.md](practices-register.md). `@agent-workflow-auditor` audits the whole layer for drift and sprawl.

Cross-cutting gotcha with no other home (2026-07-15): a subagent added or a `.claude/settings.json` change made mid-session is **not** picked up until the next session.

## Questioning an existing design ("is X over-engineered / still right?")

Lives in [[engineering-canon]] as the **evidence method** — north stars → named alternative → spike the misfit → measured baselines → bias-separated critic → ADR. One owner for design-decision methodology: the interview raises the questions; the evidence method settles the disputed ones.

## Session habits worth having

- **`/context`** — see what's eating the window before blaming the model.
- **`/rewind`** — file checkpoints. Reach for it when an edit (or a Unity asset save) goes wrong, instead of hand-reconstructing.
- **Plan mode first** for design work. A design settled in prose is far cheaper than one settled in fifteen files of code.
- **`--worktree` / `isolation: worktree`** for anything risky or parallel. The editor stays on the main checkout; validate the worktree with batchmode (see the `unity-worktree-headless-validation` memory).
- **`/loop`, `/schedule`, `/batch`, Workflows** — all multiply cost; for genuinely wide work only (practices register, "Workflows" row).
- **`@agent-unity-scout`** mentions guarantee the agent is used rather than merely offered.

## Anti-patterns

- Adding an artifact "in case it's useful." Same rule as [[no-speculative-interfaces]]: every artifact is one more thing future-you must recognize and debug. **Earn it with a real failure or a real repetition**, then write the failure into it as the cautionary example. A rule with a scar is remembered.
- Writing a skill to fix something a hook could gate.
- Exploring in the main thread because spawning an agent "feels like overhead." The overhead is the point: it buys back attention.
- Letting a skill go stale. A skill naming a class that was renamed is worse than no skill — it is confidently wrong.
- Writing a skill as a **rule-pile** — a mechanism dump, or a checklist that restates the prose above it. A skill is *judgment*: lead with the ONE idea and its *why*, then only the non-obvious specifics; if a reader who has the principle wouldn't need a line, cut it. Scar: the first `effect-commands`→`presence-dispatch` rewrite reproduced the deleted command system's shape as a rules index — the user rejected it (2026-07-14).

## Two kinds of improvement — do BOTH, not just the first

1. **Failure-patching (reactive).** Something went wrong or was repeated → codify the fix in the right artifact, with the failure written in as its example.
2. **Frontier-scanning (proactive).** Practices you don't yet feel the pain of missing are invisible to failure-patching — you adopt them deliberately or never. Scar: for a while only observed failures got patched; ADRs and eval harnesses went unproposed until the user asked why.

Both feed the **practices register** ([practices-register.md](practices-register.md)) — the dated Adopted/Watching/Rejected ledger, including its every-~20-prompts re-evaluation ritual (nudged by the `habit-check` hook). Design rationale (why we chose X over Y) goes in `DESIGN.md` + ADRs, not here.

## Cadence

- **End of any substantial task:** name **one** workflow improvement. Under five minutes → do it now; else offer it.
- **When a guideline is violated:** repairing the guardrail is part of the fix, never follow-up ([[proactive-skill-maintenance]]).
- **When a guardrail repeatedly blocks doctrine-compliant work:** the doctrine is wrong, not the hook — treat enforcement collisions as design *evidence* and feed them into a design review; don't work around them, don't soften the hook. Scar: the one-concretion nudge killed the `effect-commands` "leanest capability" route in two independent spikes (2026-07-13); that collision, not argument, decided ADR 0002.
- **After any rename or architectural change:** `@agent-workflow-auditor` before trusting the docs.
- **Task finished AND output approved:** merge per [[merge-to-main-on-approval]]; never push ([[never-push-to-origin]]).
