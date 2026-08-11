# 04 — The candidate relay's direction branches become one role table

_Repo: **ArenaInfra**. Source: [W1 ride-along of the trickle-ICE follow-ups spec](../../../docs/plans/trickle-ice-follow-ups-spec.md)._

**What to build:** A reviewer auditing the relay can confirm at a glance that sender, recipient, and done-marker are paired correctly for both opcodes. Today that lives in four direction branches that must be read against each other.

This function **is** the anti-spoof check — the membership test is the only thing standing between a prober and another pair's routing state — so "can a reader confirm it pairs correctly in one look" is load-bearing rather than cosmetic.

Collapse the branches into one role table keyed by opcode, carrying sender, recipient, done-marker and label per role.

**Blocked by:** 03 — Length-prefixed framing becomes one named pair of helpers _(the spec fixes this order: wire change, then framing, then role table)_.

**Status:** ready-for-agent

- [ ] The relay's direction branches are one table keyed by opcode
- [ ] Sender, recipient, done-marker and label are visible per role in one place
- [ ] The membership check is still the single gate, and reads as one
- [ ] The signal server's suite is green
- [ ] Landed as its own commit, separate from the framing helpers
