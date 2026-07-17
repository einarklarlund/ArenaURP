---
name: engineering-canon
description: The six seminal works this project treats as its design conscience (Parnas, Brooks, Lehman, Conway, Dijkstra, Constantine), distilled into interview LENSES for plans. Use BEFORE finalizing any plan or exiting plan mode, when a design discussion converges on fuzzy agreement, when questioning an EXISTING design ("is X over-engineered / still right?"), when auditing the AI workflow itself, and whenever a durable design agreement should be recorded. The interview protocol, the evidence method, and the shared-understandings register live here.
---

# The engineering canon — six lenses that interrogate every plan

**The one idea:** all six works make the same discovery from different angles — the scarce resource in software is not construction effort but **intellectual control under change** — and each contributes one distinct failure mode to check a plan against. A plan is not "understood" because it lists plausible steps; it is understood when it has an answer under every lens that bites, and when Einar and Claude hold the *same* answers. Getting there is an **interview, not a review**: ask, don't assert, and persist until each biting lens ends in an answer or an explicitly recorded trade-off.

These lenses are the intellectual ancestry of the two north stars (`DESIGN.md`): Elegance descends from Dijkstra and Brooks; the module doctrine descends from Parnas and Constantine. Most lenses are therefore already embodied in a governing skill — the pointer below says where. **The canonical doctrine statement stays in that skill; this one holds the source, the question it arms, and the delta the source adds.** Never quote the canon as an appeal to authority to win an argument that evidence (a spike, a baseline) should win — a lens produces a *question*, not a verdict.

## The lenses

### 1. Parnas — decompose by what will change (1972)

**Claim:** a module is a responsibility for a **design decision, hidden from all other modules**. Decompose by which decisions are likely to change, not by steps of processing — the flowchart decomposition looks natural and is almost always wrong.
**Ask:** *Which decisions in this plan are volatile, and which single module hides each one? Walk one plausible change through the seams — how many modules does it touch?* More than one means the criterion was the flowchart.
**Embodied:** the Core asmdef boundary and `Config`/`Input` structs hide "how the engine feeds us the world" ([[functional-core-imperative-shell]]); identity-only SOs + instance prefabs hide "what kinds exist" ([[type-object-pattern]]).
**Delta:** the *test* — a decomposition is judged by walking concrete changes through it, never by whether the boxes look natural. Parnas is also the honesty condition behind the rule of two ([[no-speculative-interfaces]]): you cannot hide a decision you have not yet seen vary.

### 2. Constantine — coupling and cohesion are measurable (Structured Design)

**Claim:** the cost of a system lives in the connections between its parts, and connection quality sits on two ladders — coupling from **data** (best) through stamp, control, common, to content (worst); cohesion from **functional** (best) down to coincidental. "Control coupling" — a parameter that tells the callee which behaviour to pick — is the caller making the callee's decision.
**Ask:** *For each new connection: what does the caller know about the callee's insides? Is any parameter a mode flag? Does any new module have more than one reason to exist ("manager", "utils", "helper" are the tells)?*
**Embodied:** contribution collections + notifications ([[cross-component-communication]]) deliberately cap this project at data coupling; presence discovery ([[presence-dispatch]]) removes the caller's knowledge of concrete receivers entirely.
**Delta:** the vocabulary. "This is control coupling" ends a debate that "this feels tangled" cannot. When a plan adds a channel or a call, name its rung out loud.

### 3. Brooks — conceptual integrity, the second system, essence vs accident (The Mythical Man-Month)

**Claim:** conceptual integrity is *the* most important consideration in system design — a system reflecting one coherent mind beats a cleverer committee design. The most dangerous system is the **second** one, designed just after the first proved itself, because embellishment feels earned. Only work on **essential** complexity compounds; accidental complexity (our machinery) is cost. And Brooks's law: adding communicators to troubled work adds n(n−1)/2 communication paths before it adds any output.
**Ask:** *Does the plan keep one mind's shape — can Einar recite why every part exists? Is this feature the second-system trap: embellishing what the last system proved rather than what the game needs? Which of its complexity is essential (upgrade-driven interaction, the Essence) and which accidental? And if work is being fanned out to agents — is it genuinely partitionable, or are we adding communication paths to a design problem?*
**Embodied:** the Essence scope guard (`DESIGN.md`), spike-don't-polish (the evidence method below), "Workflows earn their cost only on genuinely wide work" (practices register).
**Delta:** the second-system effect, named at the exact moment it bites — ADR 0002/0003 just validated this project's first big system, so the *next* features are precisely where embellishment will feel earned. "Plan to throw one away" is the standing license behind spikes: the output is the decision, never the code.

### 4. Lehman — entropy is the default (laws of software evolution)

**Claim:** a system that is used must continually change (law I), and unless work is explicitly spent against it, its complexity strictly increases (II) and its quality declines (VII). Anti-entropy work is *conservation*, not a chore to schedule when convenient.
**Ask:** *What does this change pay down? A plan that only adds is running up law-II debt — name the offset (a mechanism collapsed, a type deleted, two rules merged) or name the debt knowingly, with its trigger for repayment.*
**Embodied:** the `workflow-auditor`'s drift/sprawl charter is law II applied to the tooling; "when two rules say the same thing, merge them" ([[ai-workflow]]); DESIGN.md's "prefer collapsing an existing mechanism."
**Delta:** the budget framing — refactoring is not an interruption of feature work; it is the conserved quantity that feature work spends. Every feature plan states its offset or its debt.

### 5. Conway — the org chart ships (1968)

**Claim:** a system's structure copies the communication structure of the organization that produces it. Here the "organization" is Einar + the main thread + subagents + session/context boundaries — module seams will come to mirror context-window seams whether or not anyone chooses that.
**Ask:** *Do the plan's module boundaries match how the work can be communicated about — can a subagent own one module from its skill + interface alone, without the whole design in context? Where a seam exists mainly because two pieces were built in different sessions, is it a real seam?*
**Embodied:** partially — the delegation rule ([[ai-workflow]]) protects *attention*; this lens adds that delegation also *shapes the code*.
**Delta:** the inverse Conway maneuver, applied to AI: choosing what a scout, a validator, or a session owns is a **design act**, because those boundaries become the architecture. A module the scout cannot summarize in one report is a Conway smell as much as a Parnas one.

### 6. Dijkstra — the limited skull (The Humble Programmer, 1972)

**Claim:** "the competent programmer is fully aware of the strictly limited size of his own skull" — correctness comes only from keeping programs intellectually manageable: simplicity first, cleverness never. And "testing can show the presence of bugs, but never their absence" — structure for *obvious* correctness rather than tested-for correctness.
**Ask:** *Can this plan be held in one head — and one context window? Where is it clever when it could be dull? What structure would make its correctness obvious rather than merely tested? And humility both ways: which of MY claims in this plan are asserted from memory rather than verified?*
**Embodied:** Elegance itself (`DESIGN.md`), the pure-core doctrine ([[functional-core-imperative-shell]]), the "can this test fail?" gate ([[integration-testing]]), verify-don't-assert (practices register).
**Delta:** the skull limit now includes the model's context window and Einar's recall between sessions. A plan neither party can hold will be built wrong regardless of its merit on paper — that is a rejection criterion, not a style preference.

## The interview protocol

Runs before any plan is finalized — feature, refactor, or workflow-layer change; in plan mode, before `ExitPlanMode`. Scope caveat: genuinely leaf/dev-only work has no plan to finalize and takes the fast-path — it never enters this interview. [[ai-workflow]] ("Match process weight to coupling") owns *when* the interview is skipped; this skill owns *how* it runs once you're in it.

1. **Read [shared-understandings.md](shared-understandings.md) first.** Never re-ask what is already settled; cite the entry instead. (This register is the memory of the interviews — it loads only with this skill, so remembering costs no ambient context.)
2. **Pick the lenses that bite.** Feature design: Brooks and Parnas lead. Refactoring: Constantine, Lehman, Parnas. Workflow/tooling changes: Conway, Brooks's law, Lehman. A lens with nothing to say gets one dismissing clause, not a section.
3. **Answer from the codebase before asking Einar.** If a lens's question is answerable by exploring the code — which modules a change would touch (Parnas), what a caller actually knows about a callee (Constantine), what an addition could pay down (Lehman) — explore (delegate to `unity-scout` per the delegation rule) and bring the *finding* to the interview. Einar's time is for questions only he can answer: intent, priorities, trade-offs, what the game needs.
4. **Ask 2–4 sharp questions per round**, each naming its lens (plan-mode prose or `AskUserQuestion`). *Relentless* means never settling for fuzzy agreement — "looks good" is not an answer to a named question — not exhaustive checklisting; a question the plan already answers is noise.
5. **Stop condition, per lens:** an answer now in the plan, or an explicitly recorded trade-off. Genuine *disagreement* does not loop — it converts to evidence, per the evidence method below.
6. **Record what will recur.** A durable position (an interpretation of a lens, a standing answer) becomes one dated line in the register. Plan-specific answers go in the plan or its ADR, never the register.

## The evidence method — questioning an existing design ("is X over-engineered / still right?")

For when a lens ends in genuine disagreement, or the question arrives outside any plan. A pure-argument session is inconclusive and anchored — especially when the main thread helped build X. Run it as a decision, not a debate:

1. **Judge against the two north stars** (`DESIGN.md`): the **Essence** (serves an upgrade-driven system interaction?) and **Elegance** (the fewest parts that cover the cases?). Both first-class, neither a tiebreaker. "Good/clean/flexible" is unmeasurable; these two are. A piece serving neither is suspect however clean.
2. **Name ≥1 concrete ALTERNATIVE.** "Over-engineered" is meaningless without "versus what?". An ADR that only critiques the status quo is half an argument.
3. **SPIKE the misfit case first** — build the thing that does NOT fit (throwaway worktree, `isolation: worktree`). Where the pattern breaks is the real design input. Decide from evidence, not vibes; this is the single highest-value prep. **Do not polish a spike into a feature** — the output is the decision, not shipped code; that slip turns a one-day assessment into a three-week build.
4. **Argue from measured BASELINES**, not adjectives — capture the numbers (types-per-behaviour, files-to-add-a-system, inspector-locations, runtime-tunable fraction) before the session and re-run them after any change.
5. **Bias-separate.** Spawn `design-critic`; for a central decision, spawn one advocate to *keep* X and one to *replace* it, then judge between them — the main thread is usually too invested to hold both honestly. Scar: the first `design-critic` run's fast counts (23 types / ~4 shapes) were verified to 29 / 5 by an independent scout — critic findings are leads; the measured baseline is the evidence (ADR 0002).
6. **Output an ADR** (keep / refactor / replace) with alternatives-rejected. The session's job is that decision, not a feeling.

## The shared-understandings register

[shared-understandings.md](shared-understandings.md) — the dated, append-only ledger of positions Einar and Claude have explicitly settled, one line each. Check it before interviewing; append when an interview settles something durable; supersede (don't silently edit) when a position changes. Compaction rule: past ~40 live entries, merge overlapping lines and prune superseded ones — an unreadable register re-asks its own questions, which defeats it.
