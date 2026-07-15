---
name: engineering-canon
description: The six seminal works this project treats as its design conscience — Parnas's "On the Criteria to Be Used in Decomposing Systems into Modules", Brooks's The Mythical Man-Month, Lehman's laws of software evolution, Conway's law, Dijkstra's "The Humble Programmer", and Constantine's Structured Design (coupling/cohesion) — distilled into interview LENSES for plans. Use BEFORE finalizing any plan (new feature, refactor, or change to the AI-workflow layer), when entering or about to exit plan mode, when a design discussion is converging on fuzzy agreement ("looks good", "makes sense"), when auditing the AI workflow itself, and whenever a durable design agreement is reached and should be recorded. The interview protocol and the shared-understandings register live here.
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
**Embodied:** the Essence scope guard (`DESIGN.md`), spike-don't-polish and "Workflows earn their cost only on genuinely wide work" ([[ai-workflow]], practices register).
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

Runs before any plan is finalized — feature, refactor, or workflow-layer change; in plan mode, before `ExitPlanMode`.

1. **Read [shared-understandings.md](shared-understandings.md) first.** Never re-ask what is already settled; cite the entry instead. (This register is the memory of the interviews — it loads only with this skill, so remembering costs no ambient context.)
2. **Pick the lenses that bite.** Feature design: Brooks and Parnas lead. Refactoring: Constantine, Lehman, Parnas. Workflow/tooling changes: Conway, Brooks's law, Lehman. A lens with nothing to say gets one dismissing clause, not a section.
3. **Answer from the codebase before asking Einar.** If a lens's question is answerable by exploring the code — which modules a change would touch (Parnas), what a caller actually knows about a callee (Constantine), what an addition could pay down (Lehman) — explore (delegate to `unity-scout` per the delegation rule) and bring the *finding* to the interview. Einar's time is for questions only he can answer: intent, priorities, trade-offs, what the game needs.
4. **Ask 2–4 sharp questions per round**, each naming its lens (plan-mode prose or `AskUserQuestion`). *Relentless* means never settling for fuzzy agreement — "looks good" is not an answer to a named question — not exhaustive checklisting; a question the plan already answers is noise.
5. **Stop condition, per lens:** an answer now in the plan, or an explicitly recorded trade-off. Genuine *disagreement* does not loop — it converts to evidence: a spike or `design-critic`, per the questioning-a-design method in [[ai-workflow]].
6. **Record what will recur.** A durable position (an interpretation of a lens, a standing answer) becomes one dated line in the register. Plan-specific answers go in the plan or its ADR, never the register.

## The shared-understandings register

[shared-understandings.md](shared-understandings.md) — the dated, append-only ledger of positions Einar and Claude have explicitly settled, one line each. Check it before interviewing; append when an interview settles something durable; supersede (don't silently edit) when a position changes. Compaction rule: past ~40 live entries, merge overlapping lines and prune superseded ones — an unreadable register re-asks its own questions, which defeats it.
