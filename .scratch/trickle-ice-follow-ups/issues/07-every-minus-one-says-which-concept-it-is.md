# 07 — Every `-1` says which concept it is

_Repo: **ArenaURP**. Source: [W5 of the trickle-ICE follow-ups spec](../../../docs/plans/trickle-ice-follow-ups-spec.md)._

**What to build:** A game-side maintainer reading a transport branch can tell which concept is under test without guessing. Two constants of equal value carry unrelated meanings — *"this wire form stamps no connection id"* and *"this route is our own client connection"* — and the five sites that actually branch on the sentinel spell the bare literal instead of naming either. A wrong guess there is invisible to the compiler.

Name it at all five sites, choosing the correct one of the two constants at each.

**In scope:** naming, at the five branch sites.

**Not in scope:** collapsing the repeated comparison into a named predicate. It is the same question asked five times and does want a predicate, but that touches the send path and waits for a reason.

**Noted, deliberately not scheduled:** the trickle payload struct now carries two disjoint wire forms — token XOR connection id — behind one type with two factories and two serializer pairs. That is arguably two types wearing one name, and the sentinel exists only to mark which one you hold. If it is ever split, the sentinel disappears with it. Do not split it here.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [ ] All five branch sites name a constant rather than the bare literal
- [ ] Each site names the constant whose *meaning* applies there, not whichever is in scope
- [ ] The repeated comparison is left as-is; no predicate is introduced
- [ ] The end-to-end join fixture is green — it exercises both the host route and the client route in one process, so a wrong choice breaks the join rather than passing silently. That is the reason this is safe to do at all
