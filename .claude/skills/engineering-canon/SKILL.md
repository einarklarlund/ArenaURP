---
name: engineering-canon
description: This project's design conscience — the Elegance value (fewest mechanisms that cover the real cases) plus seven seminal works (Parnas, Brooks, Lehman, Conway, Dijkstra, Constantine, Martin) distilled into design LENSES, and the shared-understandings register of settled positions. Supplementary design *context*, not a procedure: surfaces when designing or questioning a feature, refactor, or the workflow itself; when grilling a plan (`grill-me` / `grill-with-docs`); when questioning an existing design ("is X over-engineered / still right?"); or when recording a durable design agreement. The questioning *technique* is `grilling`'s job — this supplies what to ask and where the answers persist.
---

# The engineering canon — seven lenses a design must answer

**The one idea:** all seven works make the same discovery from different angles — the scarce resource in software is not construction effort but **intellectual control under change** — and each contributes one distinct failure mode to check a design against (Martin the newest and most local: the same control, applied at the line). A design is not "sound" because it lists plausible steps; it is sound when it has an answer under every lens that *bites*, and when Einar and Claude hold the *same* answers. These lenses are *what* a design is questioned against; running the questioning — one decision at a time, a recommendation carried into each question — is the `grilling` skill's job, not this one.

These lenses are the intellectual ancestry of the project's design values: **Elegance** ([[design-elegance]]) descends from Dijkstra and Brooks; the module doctrine from Parnas and Constantine; the line-level reader-cost discipline from Martin — subordinate to Elegance where the two collide (lens 7). (The other north star, the game's **essence**, is not a design value and lives in `CONTEXT.md`.) Most lenses are already embodied in a governing skill — the pointer under each says where. **The canonical doctrine statement stays in that skill; this one holds the source, the question it arms, and the delta the source adds.** A lens produces a *question*, not a verdict — never quote the canon as an appeal to authority to end an argument that evidence should settle.

## The lenses

### 1. Parnas — decompose by what will change (1972)

**Claim:** a module is a responsibility for a **design decision, hidden from all other modules**. Decompose by which decisions are likely to change, not by steps of processing — the flowchart decomposition looks natural and is almost always wrong.
**Ask:** *Which decisions in this design are volatile, and which single module hides each one? Walk one plausible change through the seams — how many modules does it touch?* More than one means the criterion was the flowchart.
**Embodied:** for the determinism/branchy cores we keep, the Core asmdef boundary and `Config`/`Input` structs hide "how the engine feeds us the world" ([[component-design]]); identity-only SOs + instance prefabs hide "what kinds exist" ([[type-object-pattern]]). (The boundary is now a *targeted* seam, not a universal mandate — ADR 0008.)
**Delta:** the *test* — a decomposition is judged by walking concrete changes through it, never by whether the boxes look natural. Parnas is also the honesty condition behind the rule of two ([[no-speculative-interfaces]]): you cannot hide a decision you have not yet seen vary.

### 2. Constantine — coupling and cohesion are measurable (Structured Design)

**Claim:** the cost of a system lives in the connections between its parts, and connection quality sits on two ladders — coupling from **data** (best) through stamp, control, common, to content (worst); cohesion from **functional** (best) down to coincidental. "Control coupling" — a parameter that tells the callee which behaviour to pick — is the caller making the callee's decision.
**Ask:** *For each new connection: what does the caller know about the callee's insides? Is any parameter a mode flag? Does any new module have more than one reason to exist ("manager", "utils", "helper" are the tells)?*
**Embodied:** contribution collections + notifications ([[cross-component-communication]]) deliberately cap this project at data coupling; presence discovery ([[presence-dispatch]]) removes the caller's knowledge of concrete receivers entirely.
**Delta:** the vocabulary. "This is control coupling" ends a debate that "this feels tangled" cannot. When a design adds a channel or a call, name its rung out loud.

### 3. Brooks — conceptual integrity, the second system, essence vs accident (The Mythical Man-Month)

**Claim:** conceptual integrity is *the* most important consideration in system design — a system reflecting one coherent mind beats a cleverer committee design. The most dangerous system is the **second** one, designed just after the first proved itself, because embellishment feels earned. Only work on **essential** complexity compounds; accidental complexity (our machinery) is cost. And Brooks's law: adding communicators to troubled work adds n(n−1)/2 communication paths before it adds any output.
**Ask:** *Does the design keep one mind's shape — can Einar recite why every part exists? Is this feature the second-system trap: embellishing what the last system proved rather than what the game needs? Which of its complexity is essential (upgrade-driven interaction, the Essence) and which accidental? And if work is being fanned out to agents — is it genuinely partitionable, or are we adding communication paths to a design problem?*
**Embodied:** the essence scope guard (`CONTEXT.md` preamble / `docs/plans/features.md`); spike-don't-polish via a throwaway `prototype` (mattpocock) — the output is the decision, not shipped code; "Workflows earn their cost only on genuinely wide work" (practices register).
**Delta:** the second-system effect, named at the exact moment it bites — ADR 0002/0003 just validated this project's first big system, so the *next* features are precisely where embellishment will feel earned. "Plan to throw one away" is the standing license behind spikes: the output is the decision, never the code.

### 4. Lehman — entropy is the default (laws of software evolution)

**Claim:** a system that is used must continually change (law I), and unless work is explicitly spent against it, its complexity strictly increases (II) and its quality declines (VII). Anti-entropy work is *conservation*, not a chore to schedule when convenient.
**Ask:** *What does this change pay down? A design that only adds is running up law-II debt — name the offset (a mechanism collapsed, a type deleted, two rules merged) or name the debt knowingly, with its trigger for repayment.*
**Embodied:** the `workflow-auditor`'s drift/sprawl charter is law II applied to the tooling; "when two rules say the same thing, merge them" ([[workflow-artifacts]]); [[design-elegance]]'s "prefer collapsing an existing mechanism."
**Delta:** the budget framing — refactoring is not an interruption of feature work; it is the conserved quantity that feature work spends. Every feature design states its offset or its debt. And for a refactor or a stated simplification, **re-derive from the principle** rather than editing the status-quo map — the current-state map is *migration cost*, not the target's shape, and editing the status quo anchors you to the parts you just studied — then state a **part-count delta** (mechanisms/types/components added vs removed): a "simplify" that nets *up* is a stop, not a footnote. (Encoded after a refactor plan silently preserved status-quo machinery against a stated "passives are data" principle; the `habit-check` hook's refactor/simplify branch nudges it too.)

### 5. Conway — the org chart ships (1968)

**Claim:** a system's structure copies the communication structure of the organization that produces it. Here the "organization" is Einar + the main thread + subagents + session/context boundaries — module seams will come to mirror context-window seams whether or not anyone chooses that.
**Ask:** *Do the design's module boundaries match how the work can be communicated about — can a subagent own one module from its skill + interface alone, without the whole design in context? Where a seam exists mainly because two pieces were built in different sessions, is it a real seam?*
**Embodied:** partially — the delegation rule ([[delegation]]) protects *attention*; this lens adds that delegation also *shapes the code*.
**Delta:** the inverse Conway maneuver, applied to AI: choosing what a scout, a validator, or a session owns is a **design act**, because those boundaries become the architecture. A module the scout cannot summarize in one report is a Conway smell as much as a Parnas one.

### 6. Dijkstra — the limited skull (The Humble Programmer, 1972)

**Claim:** "the competent programmer is fully aware of the strictly limited size of his own skull" — correctness comes only from keeping programs intellectually manageable: simplicity first, cleverness never. And "testing can show the presence of bugs, but never their absence" — structure for *obvious* correctness rather than tested-for correctness.
**Ask:** *Can this design be held in one head — and one context window? Where is it clever when it could be dull? What structure would make its correctness obvious rather than merely tested? And humility both ways: which of MY claims in this design are asserted from memory rather than verified?*
**Embodied:** Elegance itself ([[design-elegance]]), the pure-core exception for determinism/branchy logic ([[component-design]]), the "can this test fail?" gate ([[integration-testing]]), verify-don't-assert (practices register).
**Delta:** the skull limit now includes the model's context window and Einar's recall between sessions. A design neither party can hold will be built wrong regardless of its merit on paper — that is a rejection criterion, not a style preference.

### 7. Martin — code is read far more than written (Clean Code)

**Claim:** code is read an order of magnitude more often than it is written, so the reader's cost dominates and is paid down at the line: **names** reveal intent (no comment propping them up); a **function does one thing** at one level of abstraction; **duplication is the root evil** (DRY); a comment is an apology for code that failed to explain itself; and the **Boy Scout Rule** — leave every module a little cleaner than you found it. Tests are first-class code, kept as clean as production, one concept per test (FIRST).
**Ask:** *At the line level the other six lenses skip — does each name state intent without a comment to prop it? Does any function mix levels of abstraction, or need an "and" to describe it? Is this edit leaving the file cleaner than it found it, or only adding to it?*
**Embodied:** the kept cores' readability-through-testability ([[component-design]]); the describe-behaviour-not-implementation commit style ([[commit-conventions]]); the clean-test / "can this test fail?" gate ([[integration-testing]]).
**Delta:** the only lens *below* the module — it reads the code as written, not the design, so it bites at **implementation and review, not design-time**. Its Boy Scout Rule is Lehman's law-II conservation at edit-granularity: every touch pays a little down. **Subordinate to Elegance where they collide:** Clean Code's maximalism (extract-till-you-drop, hard length limits, comments-as-failure) *multiplies* tiny methods and indirection — the exact mechanism-proliferation the Elegance north star forbids. Take its **values** (intention-revealing names, single responsibility, DRY); reject its **quantitative dogma** when a longer, plainer function is the more manageable one (Dijkstra wins the tie). Its structural core (SRP, low coupling) is already Parnas + Constantine, more rigorously; the non-redundant contribution is the reader-cost lens at the line.

## Which lenses bite

A lens with nothing to say for the design at hand gets one dismissing clause, not a section — this is context to draw the sharp questions from, not a checklist to march through. Typical leads: **feature design** → Brooks, Parnas; **refactoring** → Constantine, Lehman, Parnas; **workflow/tooling change** → Conway, Brooks's law, Lehman. Martin bites at *implementation and code review*, not design-time — it rarely fires when questioning a plan.

## The shared-understandings register

[shared-understandings.md](shared-understandings.md) — the dated, append-only ledger of positions Einar and Claude have explicitly settled, one line each. **Check it before questioning a design** — never re-ask what is already settled; cite the entry instead. (This register is the memory of past design decisions — it loads only with this skill, so remembering costs no ambient context.) Append when a design decision settles something durable; supersede (don't silently edit) when a position changes. Plan-specific answers go in the plan or its ADR, never the register. Compaction rule: past ~40 live entries, merge overlapping lines and prune superseded ones — an unreadable register re-asks its own questions, which defeats it.
