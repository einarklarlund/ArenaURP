---
name: design-elegance
description: Elegance — this project's root design value: the fewest principles, mechanisms, and abstractions that cover the real cases. Use BEFORE adding a mechanism, channel, abstraction, interface, manager, ADR, or workflow artifact; when choosing between two designs that both work; when judging whether something is over-engineered or should be collapsed; and whenever a plan only adds without naming what it removes.
---

# Elegance

**The fewest principles, mechanisms, and abstractions that cover the real cases.** A simple mental model of *why and how* beats a complete one.

This is the value the project's design rules serve, not one rule among them. *No speculative interfaces*, *fewest cross-component mechanisms*, *earn every workflow artifact* — all one value wearing different hats. Cite the value, not a growing list of rules.

The other north star is the **essence** — what makes the game worth playing, in [CONTEXT.md](../../../CONTEXT.md). Essence decides *what* is worth building; elegance decides *how* it gets built.

## The budget

Every mechanism costs against a budget, and the budget is what you can hold in your head. So spending is a decision, not a default:

- **Prefer reusing or collapsing an existing mechanism over adding a new one.** Justify a new one against what already exists, or don't add it. When two things overlap, merge them.
- **Engine-native beats owned.** A mechanism Unity provides and maintains — components as the unit of composition, `GetComponents` dispatch, plain serialization, prefab authoring — is nearly free against the budget. One we own — custom drawers, `[SerializeReference]` machinery, bespoke registries — costs full price. When both cover the case, take the engine's.
- **Generality is earned by the second real case** (rule of two), never defaulted to. You cannot hide a decision you have not yet seen vary.

## State the delta

A design that only adds is running up debt. Name the offset — a mechanism collapsed, a type deleted, two rules merged — or name the debt knowingly, with the trigger that repays it.

For anything billed as a refactor or simplification, this hardens into a **part-count delta**: mechanisms, types, and components added versus removed. **A "simplify" that nets up is a stop, not a footnote.** And re-derive the target *from the stated goal* rather than editing the current-state map — a map of what exists is migration cost, not the target's shape, and studying it deeply anchors you to its parts.

## Deciding between designs that both work

- **SOLID is a diagnostic, not a ranking.** A violation — a closed enumeration at a hot extension point, a class accreting unrelated jobs — flags inelegance worth fixing. But two competing designs frequently *both* conform, and conformance cannot pick between them. Decide on mechanism count and coverage. (Receipt: the 2026-07-14 hit-handler session — authored command lists and component discovery were both fully SOLID-open; engine-nativeness decided it.)
- **Composition and inheritance have assigned layers** ([ADR 0002](../../../docs/adr/0002-ieffect-appropriateness-verdict.md)). *Which systems interact* is expressed by composing participants — components an entity carries, modifiers in a fold — never by a bespoke type per interaction. Inheritance appears only as the shallow taxonomy of sites and events that gates *where* behaviour can be authored, and never carries behaviour itself.

The scar behind that last rule: "composition" at the TYPE level — the deleted capability lattice — is where speculative generality hides. Expressive type machinery is earned by a second real case that doesn't fit the tree.

## It binds the workflow too

Skills, hooks, subagents, ADRs, and memories are under the same discipline as the code — see [[workflow-artifacts]]. The knowledge layer should trend toward *fewer, sharper* statements. When in doubt, remove.

## Where the reasoning behind this lives

The seven seminal works this value descends from — and the sharp questions they arm — are [[engineering-canon]]. Reach for that when *grilling* a design; reach for this when *deciding whether a part earns its place*.
