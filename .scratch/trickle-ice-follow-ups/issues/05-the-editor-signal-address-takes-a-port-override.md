# 05 — The editor's signal address takes a port override

_Repo: **ArenaURP**. Source: [W2 of the trickle-ICE follow-ups spec](../../../docs/plans/trickle-ice-follow-ups-spec.md)._

**What to build:** A developer can run two join harnesses at once, and either of them beside their own running signal server, without stopping anything by hand first. Today the game hard-codes its editor signal address including the port, so a harness cannot be moved off the port the developer's Docker container is already holding — and the "stop the container first" step is a thing to remember and a confusing failure when forgotten.

The server side already honours a listen-port override. This is the matching half.

The harness's port and the game's editor address must derive from **one** source rather than two that agree by convention. The coupling is currently documented in two places — a constant's doc comment and the join fixture's class summary — and both should stop needing to say it.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [ ] The editor's signal address is overridable rather than a fixed host:port
- [ ] The harness port and the game's editor address come from one source; changing the port is one edit
- [ ] The two places that document the coupling in prose no longer need to
- [ ] The end-to-end join fixture passes against a **non-default** port — that is the proof, not a passing default
- [ ] Two harness runs can proceed concurrently, and neither collides with a signal server already bound to the default port
