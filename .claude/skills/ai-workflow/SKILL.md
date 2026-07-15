---
name: ai-workflow
description: How this project uses Claude Code itself — the five artifact types (hooks, skills, subagents, commands, memories), which one to reach for, and the habits that keep long sessions from drifting. Use this WHENEVER a workflow feels repetitive or error-prone, when about to add or edit anything under .claude/, when starting a task big enough to need delegation or plan mode, when a guideline was violated, or when the user asks how to work with Claude more effectively. Also the place to record newly-learned Claude Code capabilities. Consult BEFORE hand-rolling a process that an artifact should own.
---

# The AI development workflow for this project

Treating "how we use Claude" as part of the codebase — versioned, reviewed, improved when it fails. The user has explicitly made teaching this an objective of the project, so **proactively surface improvements; don't wait to be asked** ([[proactive-skill-maintenance]]).

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

## Questioning an existing design ("is X over-engineered / still right?")

A pure-argument session is inconclusive and anchored — especially when the main thread helped build X. Run it as a decision, not a debate:

1. **Judge against the two north stars** (`DESIGN.md`): the **Essence** (serves an upgrade-driven system interaction?) and **Elegance** (the fewest parts that cover the cases?). Both first-class, neither a tiebreaker. "Good/clean/flexible" is unmeasurable; these two are. A piece serving neither is suspect however clean.
2. **Name ≥1 concrete ALTERNATIVE.** "Over-engineered" is meaningless without "versus what?". An ADR that only critiques the status quo is half an argument.
3. **SPIKE the misfit case first** — build the thing that does NOT fit (throwaway worktree, `isolation: worktree`). Where the pattern breaks is the real design input. Decide from evidence, not vibes; this is the single highest-value prep. **Do not polish a spike into a feature** — the output is the decision, not shipped code; that slip turns a one-day assessment into a three-week build.
4. **Argue from measured BASELINES**, not adjectives — capture the numbers (types-per-behaviour, files-to-add-a-system, inspector-locations, runtime-tunable fraction) before the session and re-run them after any change.
5. **Bias-separate.** Spawn `design-critic`; for a central decision, spawn one advocate to *keep* X and one to *replace* it, then judge between them — I am usually too invested to hold both honestly.
6. **Output an ADR** (keep / refactor / replace) with alternatives-rejected. The session's job is that decision, not a feeling.

## The delegation rule (the highest-leverage habit here)

**Main-thread context is for the design. Searching and validating are somebody else's job.**

Skill drift is not a memory failure, it is an attention failure: after fifteen file reads and a prefab dump, the skill you loaded at turn 2 is no longer psychologically present. So:

- More than ~3 files to answer a question, or the question crosses a Unity GUID boundary → **`unity-scout`**.
- Running tests, compile-checking, parsing test XML → **`unity-validator`**.
- A genuinely open design question with several viable shapes → **plan mode** (`/plan`, or Shift+Tab) before writing any code.

A subagent returns a conclusion. If it returns file dumps, its prompt is wrong — fix the agent, not the habit.

## Be critical — the anti-sycophancy mandate

The user has explicitly asked to be challenged on his **goals and methodology**, not just helped to execute them. Agreeing when you should push back is the worse failure: he can correct a wrong critique, but he cannot correct silence. So:

- When he states a goal or you propose a design, **owe him the strongest counterargument before agreeing.** Steelman it, then attack it. Name where goals conflict with each other and where "it works" is really one shape validated once (N=1).
- **Turn fuzzy goals into measurable tests.** "Easy / clean / flexible" cannot be assessed, only rationalized. Replace each with a number you can re-run (files touched to add a system, C# classes per new behaviour, inspector locations to tune one value). This is the same discipline as the "can this test fail?" gate — motivated reasoning is the enemy in both.
- **When you are invested in the thing under review, delegate the critique** to `design-critic` (bias-separated) rather than grading your own work. First use: assessing the `IEffect` system against its four goals surfaced a scaffolding-to-behaviour ratio the main thread, having helped build it, was anchored against (its fast counts — 23 types / ~4 shapes — were later verified to 29 / 5 by an independent scout, which is why the method's step 4 says *measure before arguing*; verdict in ADR 0002).
- Readability is part of being critical: a critique he has to re-read is a critique he won't act on. Short paragraphs, lead with the verdict.

## The educator mandate

Einar is deliberately learning to use AI well; that is a stated goal of this project, alongside shipping the game. He does not yet know what he doesn't know, so **the burden is on you to surface it, unprompted and specifically.** Never a feature tour — always a capability paired with the exact pain it removes, plus the mechanism ("this is a hook, not a skill, because a skill cannot interrupt a tool call").

Before implementing anything substantial, say **how it should be done with AI tooling**, not just what the code will be: what gets delegated to a subagent, whether it wants plan mode first, which artifact should own the repetitive part. Explain the reasoning; he will ask for it otherwise, and he is right to.

## Current inventory

**Hooks** (`.claude/hooks/`, wired in `.claude/settings.json`)
- `guard-unity-yaml.sh` — PreToolUse. **Blocks** text tools and `Write`/`Edit` against `.prefab`/`.unity`/`.asset`/`.meta`. Override a Bash command with a trailing `# unity-yaml-ok`.
- `skill-nudge.sh` — PostToolUse. Re-surfaces the governing skill when a SyncVar/RPC, an interface declaration, or a PlayMode test is written. Also drops the `.claude/.cs-dirty` sentinel.
- `cs-compile-gate.sh` — Stop. If C# was edited, **blocks once** (it consumes the sentinel first, so it cannot loop) until Unity re-imports and the console is checked for compile errors.
- `habit-check.sh` — UserPromptSubmit. Fires on **every** substantive prompt, classifies the work (design/build/investigate/fix/verify/commit), and injects the matching habit nudge; silent on trivial turns. Also drives the frontier-scan cadence (every 20 tasks). This is the mechanical form of "nudge me to the habits every time."
- `session-start.sh` — SessionStart. Injects orientation (editor reachable? uncommitted assets? last test verdict, and *which runner* wrote it) plus the delegation rules and this mandate — surviving compaction, which a memory does not always.

**Design & practice docs** — `DESIGN.md` (root: design log, roadmap, open questions) + `Docs/design/*.md` (ADRs: why we chose X over Y). [practices-register.md](practices-register.md) (this skill's dir: the Adopted/Watching/Rejected ledger). Consult `DESIGN.md` at the start of any design task; write an ADR when a real decision with alternatives gets made.

**Subagents** (`.claude/agents/`)
- `unity-scout` — read-only, MCP-first investigation. Returns conclusions, not file dumps.
- `unity-validator` — test runs and compile checks. Knows the stale-XML and stale-domain traps.
- `workflow-auditor` — audits the WHOLE workflow layer (skills, CLAUDE.md, DESIGN.md/ADRs, memories, hooks, agents, commands) for drift AND sprawl: is each artifact correct, needed, non-redundant? Run after any rename or before a consolidation pass.
- `design-critic` — bias-separated adversarial reviewer of a design or a set of goals. Spawn it before committing to a design, whenever the main thread is invested, or when asked "is X worth it?" — it does not share the sunk cost.

Note: a subagent added mid-session is **not** picked up until the next session (the directory watcher only watches directories that existed at startup). Same caveat as `.claude/settings.json`.

**Skills** — `arena-systems-map` is the entry point when a task spans systems. Then the governing skill per area: `presence-dispatch`, `type-object-pattern`, `cross-component-communication`, `functional-core-imperative-shell`, `fishnet-conventions`, `integration-testing`, `unity-mcp-vs-shell`, `game-programming-patterns`, `commit-conventions`.

**Memories** — `MEMORY.md` indexes them. Verify a memory's claims against current code before acting: they are point-in-time, and this codebase renames things.

## Session habits worth having

- **`/context`** — see what's eating the window before blaming the model.
- **`/rewind`** — file checkpoints. Reach for it when an edit (or a Unity asset save) goes wrong, instead of hand-reconstructing.
- **Plan mode first** for design work. A design settled in prose is far cheaper than one settled in fifteen files of code.
- **`--worktree` / `isolation: worktree`** for anything risky or parallel. The editor stays on the main checkout; validate the worktree with batchmode (see the `unity-worktree-headless-validation` memory).
- **`/loop`, `/schedule`, `/batch`, Workflows** — situational, and all of them multiply cost. Reach for them when the work is genuinely wide (a sweeping migration, a multi-source audit), not because a task is merely long.
- **`@agent-unity-scout`** mentions guarantee the agent is used rather than merely offered.

## Anti-patterns

These are all one value — **Elegance** (`DESIGN.md`): the fewest parts that cover the cases. Prefer collapsing two artifacts into one over adding a third; the workflow itself is under this discipline, and when two rules say the same thing, merge them.

- Adding an artifact "in case it's useful." Same rule as [[no-speculative-interfaces]]: every artifact is one more thing future-you must recognize and debug. **Earn it with a real failure or a real repetition**, then write the failure into it as the cautionary example. A rule with a scar is remembered.
- Writing a skill to fix something a hook could gate.
- Exploring in the main thread because spawning an agent "feels like overhead." The overhead is the point: it buys back attention.
- Letting a skill go stale. A skill naming a class that was renamed is worse than no skill — it is confidently wrong.
- Writing a skill as a **rule-pile** — a mechanism dump, or a checklist that restates the prose above it. A skill is *judgment*: lead with the ONE idea and its *why* (how it serves the north stars), then only the non-obvious specifics; if a reader who has the principle wouldn't need a line, cut it. The skill is under the same Elegance discipline as the code it governs. Scar: the first `effect-commands`→`presence-dispatch` rewrite reproduced the deleted command system's shape as a rules index — the user rejected it (2026-07-14).

## Two kinds of improvement — do BOTH, not just the first

1. **Failure-patching (reactive).** Something went wrong or was repeated → codify the fix in the right artifact, with the failure written in as its example. This is most of what happens and it's good.
2. **Frontier-scanning (proactive).** Practices you don't yet feel the pain of missing are invisible to failure-patching — you adopt them deliberately or never. So *also* scan the frontier for what a thoughtful team is doing that we aren't. The blind spot this fixes was real: for a while I only ever patched observed failures and never proposed a practice unprompted (ADRs, eval harnesses) until the user asked why.

Both feed the **practices register** ([practices-register.md](practices-register.md)) — the dated ledger of every practice Adopted / Watching / Rejected, so evolution is auditable and rejected ideas aren't re-litigated. Design rationale (why we chose X over Y) goes in **`DESIGN.md`** + `Docs/design/*.md` ADRs, not here and not in CLAUDE.md.

## Cadence

- **End of any substantial task:** name **one** workflow improvement — an artifact to add, a skill gone stale, a repetition worth a command. Under five minutes → do it now; else offer it.
- **Every ~20 substantive prompts** (the `habit-check` hook nudges this): run the re-evaluation ritual in the practices register — re-judge one Adopted practice, advance one Watching one, scan for one new one. Verify capabilities with the `claude-code-guide` agent; don't assert from memory (it has been wrong).
- **When a guideline is violated:** repairing the guardrail is part of the fix, never follow-up.
- **When a guardrail repeatedly blocks doctrine-compliant work:** the doctrine is wrong, not the hook. Treat enforcement collisions as design *evidence* and feed them into a design review — don't work around them, and don't soften the hook. Scar: the one-concretion nudge killed the `effect-commands` "leanest capability" route in two independent spikes (2026-07-13); that collision, not argument, decided ADR 0002.
- **After any rename or architectural change:** run `@agent-workflow-auditor` before trusting the docs — and periodically for a sprawl/consolidation pass, since the workflow itself is under the Elegance value.
