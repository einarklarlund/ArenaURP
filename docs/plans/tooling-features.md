# Tooling features — the grill-ready backlog

_Forward-looking index of everything not yet built **in the tools**: the harnesses, the build and deploy plumbing, the AI-development layer. Same shape as [gameplay-features.md](gameplay-features.md) — each entry is a small, self-contained brief written to be fed to a **`/grill-with-docs`** session one at a time, with the open decisions surfaced rather than pre-answered. The briefs live in [tooling/](tooling/)._

**Why this is a separate backlog.** The gameplay backlog is filtered by the **essence gate** — does this drastically change how someone plays, and does it interact with an existing system? Tooling neither passes nor fails that question; the gate simply does not apply. Mixing the two either smuggles tooling past a gate it was never meant to answer, or dilutes the gate that keeps the game from sprawling.

## The tooling gate (every brief answers this)

An entry earns a place here only if it:

**(a) is about how the game is built, tested, deployed or developed — never about how it plays.** Anything a player could notice is a gameplay feature and answers to the essence gate instead. This is the boundary, and it is the whole reason this file is separate.

**(b) removes a recurring cost.** One of: a manual step someone must repeat, a class of defect that can currently ship unnoticed, or knowledge that survives only in someone's head. A tool that is merely tidier than what exists does not earn a place — that is a refactor, and it rides along with whatever work next opens the file.

## Open tooling features

| Feature | One line | Recurring cost it removes | Blocking question |
|---|---|---|---|
| [WebGL join harness](tooling/webgl-join-harness.md) | Drive a served WebGL build from headless Chrome and assert a real two-peer join | The shipping transport's only runtime evidence is a person opening two browser windows | Does headless Chrome gather usable candidates? — prototype first |

## Suggested order

- **WebGL join harness** — sized as a `/prototype` first. The candidate-gathering question decides whether the rest is worth designing at all.
