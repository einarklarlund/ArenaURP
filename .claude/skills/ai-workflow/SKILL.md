---
name: ai-workflow
description: Picking the right workflow artifact — hook, skill, subagent, or memory — plus the delegation rule and how the workflow layer evolves. Use when adding or editing anything under .claude/, when a process feels repetitive or error-prone, when a guideline was violated, or when starting a task big enough to need delegation or plan mode.
---

# The AI development workflow for this project

Treating "how we use Claude" as part of the codebase — versioned, reviewed, improved when it fails — and under the same **Elegance** value as the code (`DESIGN.md`): the fewest parts that cover the cases. Prefer collapsing two artifacts into one over adding a third; when two rules say the same thing, merge them.

The standing mandates (educator, anti-sycophancy) are *always-on norms*, so by this skill's own table they live as a memory — [[ai-workflow-coaching]], summarized into every session by the `session-start` hook — not here.

## The four artifacts — pick by what kind of thing you're encoding

The single most common mistake is writing a *rule* where a *gate* was needed. A rule you must remember is not a rule; it is a hope.

| You want to… | Use | Why | Lives in |
|---|---|---|---|
| Make a wrong action **impossible** | **Hook** | The harness runs it. Compliance is 100% and constant. A skill in context cannot interrupt a tool call. | script in `.claude/hooks/`, **registered** in the `hooks` block of the committed `.claude/settings.json` — an unregistered script never fires |
| Explain **judgment** (when, why, trade-offs) — or replay a **procedure** you keep re-deriving | **Skill** | Loads on demand by trigger; holds prose a regex can't. A user-invoked one-shot procedure is also a skill (`/name`) — slash commands are merged into skills; add `disable-model-invocation: true` if only the user should trigger it. | `.claude/skills/<name>/SKILL.md` |
| Keep a **noisy search or run** out of the main context | **Subagent** | Its context is separate; only its conclusion returns. Can preload a skill's full text via `skills:` frontmatter — single-source doctrine instead of duplicating it in the agent prompt. | `.claude/agents/<name>.md` |
| Persist a **fact or norm** across sessions | **Memory** | Indexed in `MEMORY.md` (the index loads every session; bodies load on demand). | the auto-memory dir |

**Litmus:** if the failure is mechanically detectable (a tool × a file extension, a code shape) → hook. If it needs judgment → skill. If it costs *attention* → subagent. If it costs *typing* → a manual-only skill.

(Watching, not adopted: **path-scoped rules** — `.claude/rules/*.md` with a `paths:` glob, always-on only for matching files. See the practices register before reaching for one.)

## The delegation rule (the highest-leverage habit here)

**Main-thread context is for the design. Searching and validating are somebody else's job.**

Skill drift is not a memory failure, it is an attention failure: after fifteen file reads and a prefab dump, the skill you loaded at turn 2 is no longer psychologically present. So:

- More than ~3 files to answer a question, or the question crosses a Unity GUID boundary → **`unity-scout`**.
- Running tests, compile-checking, parsing test XML → **`unity-validator`**.
- A genuinely open design question with several viable shapes → **plan mode** (`/plan`, or Shift+Tab) before writing any code — and before any plan is finalized, the [[engineering-canon]] interview.

In a **multi-step autonomous run**, validate *between* steps, not only at the end: small per-step error rates compound (four 95%-correct steps ≈ 81%; Gaul), and a gate that fires only at the finish can't localize which step drifted.

A subagent returns a conclusion. If it returns file dumps, its prompt is wrong — fix the agent, not the habit.

## Match process weight to coupling — two speeds

Not every change earns the full gauntlet (plan mode → [[engineering-canon]] interview → ADR). Process weight tracks **coupling and permanence**, not habit. The default has silently been one speed — the slow one; naming the fast-path is permission to stop over-processing the leaves.

- **Fast-path** — leaf code: dev-only tooling, editor scripts, throwaway converters, one-off diagnostics. Low coupling, no invariants to satisfy, cheap to regenerate. *Just try it empirically and verify* (the game runs; a test goes green). Skip the interview. Gaul's economics hold here: regeneration from a description is cheaper than preservation, so the maintenance question ("worth building AND keeping?") stops gating — describe it clearly, let it be built.
- **Gauntlet** — core code: anything networked, server-authoritative, or wired into the interacting-mechanisms web (`Pawn`/`Health`/projectiles/abilities/match flow). "Correct" here means satisfying non-local invariants no short description carries — our skills and ADRs *are* that description. Regeneration is NOT cheap; the design work is the point, and the gauntlet earns its cost.

The tell: **could a competent stranger regenerate this from one paragraph and be right?** Yes → fast-path. No — the correctness lives in constraints spread across the codebase → gauntlet. A change is never fast-path merely for being *small*: a five-line edit that touches a `SyncVar`/RPC/`[Server]` method or the hit-exchange web is coupled, so it takes the gauntlet.

## The inventory IS the directory

This skill keeps no inventory of hooks, agents, skills, or memories — a copy drifts, and every change would have to be recorded twice; the auditor would then be *detecting* rot this structure simply prevents. The single sources: hooks are the `hooks` block of the committed `.claude/settings.json` — that block IS the registry; the scripts in `.claude/hooks/` are its payload (each header comment states what it gates), and a script without a registration entry is dead code. Subagents self-describe in the session's agent list; skills self-index via their frontmatter descriptions (loaded into every session — keep each description a TRIGGER, not a summary; the content belongs in the body, which loads on use); `MEMORY.md` indexes memories; design rationale in `DESIGN.md` + `Docs/design/*.md` ADRs; the practices ledger in [practices-register.md](practices-register.md). `@agent-workflow-auditor` audits the whole layer for drift and sprawl — including that every hook script is registered and every registration points at a live script.

Scar (2026-07-15): all five hooks silently died for ~2 days because their registration lived only in the unversioned `settings.local.json` and a settings rewrite dropped it — the scripts sat in `.claude/hooks/` looking alive while nothing fired, and nothing detected it until a docs audit. Registration is part of the artifact; it goes in the **committed** settings file, always.

Cross-cutting gotcha with no other home (2026-07-15): a subagent added or a `.claude/settings.json` change made mid-session is **not** picked up until the next session.

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
- **Task finished AND output approved:** merge per [[merge-to-main-on-approval]] — fast-forward local main, then tear down the worktree if one was used — never push ([[never-push-to-origin]]).
