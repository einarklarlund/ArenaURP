# 03 — Length-prefixed framing becomes one named pair of helpers

_Repo: **ArenaInfra**. Source: [W1 ride-along of the trickle-ICE follow-ups spec](../../../docs/plans/trickle-ice-follow-ups-spec.md)._

**What to build:** A maintainer adding a length-prefixed field to the signal server writes one call instead of a hand-computed offset. Framing is inlined four times today with the offsets worked out by hand, and the session-token work adds a fifth. This feature has already shipped one offset bug — that is the argument.

The test harness already has both helpers; the server it tests does not. Give the server the same named pair, and route every existing site through it.

Pure shape change: no behaviour moves, no wire byte changes.

**Blocked by:** 02 — The answer names its own signaling session _(the contract change lands and goes green before the code that enforces it is reshaped)_.

**Status:** ready-for-agent

- [ ] One named write helper and one named read helper exist in the server
- [ ] Every hand-inlined framing site, including the one the answer change added, goes through them
- [ ] No hand-computed length offsets remain in the server
- [ ] The signal server's suite is green — the baseline plus the tests ticket 02 added
- [ ] Landed as its own commit, separate from the wire change
