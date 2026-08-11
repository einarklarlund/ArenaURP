# 08 — The outbound candidate buffer is a tested pure type

_Repo: **ArenaURP**. Source: [W6 of the trickle-ICE follow-ups spec, as revised](../../../docs/plans/trickle-ice-follow-ups-spec.md)._

**What to build:** The ordering guarantee on the outbound trickle path is proven rather than assumed. The signal manager hand-rolls a small machine — hold **candidates** gathered before the route can be resolved, release them in arrival order once it resolves, pass through from then on — with no test, while an equivalent fully-tested machine exists for the inbound direction.

Extract it as **its own small pure type**, a sibling of the inbound queue and in the same assembly, so the existing EditMode test assembly reaches it with no new assembly reference. Its surface is the machine and nothing else: offer a candidate, open the route, release what was held in arrival order, reset.

**It is not the inbound queue and must not become it.** The original plan called for folding this into the inbound queue; that was overridden during verification. The inbound queue has exactly one consumer, its entire API is named for the inbound direction, and its end-of-candidates rule — collapse repeats, force the marker last — is inbound-specific reordering the outbound path never needs, because the local ICE agent already emits the marker last by construction. Rule of two is **not** satisfied here: this is a second *case*, not a second concretion of one abstraction.

**Stays where it is:** the end-of-candidates marker retiring the route (which keeps a host's route table from growing an entry per join) is routing lifecycle, not buffering. Leave it in the signal manager.

**Stays duplicated:** the WebGL jslib's third copy of this machine. It is another language; that duplication is forced.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [ ] A pure, named type holds the buffering machine, in the same assembly as the inbound queue
- [ ] Its surface is offer / open / reset and nothing more
- [ ] The signal manager uses it instead of hand-rolling the machine
- [ ] Route retirement on the end-of-candidates marker stays in the signal manager
- [ ] EditMode fixture pins: candidates offered before the route opens are held, not sent
- [ ] EditMode fixture pins: opening releases them in **arrival order**
- [ ] EditMode fixture pins: after opening, candidates pass straight through
- [ ] EditMode fixture pins: opening a second time releases nothing
- [ ] EditMode fixture pins: reset returns to holding with nothing held
- [ ] It does **not** inherit the inbound queue's marker-collapse or marker-last tests — their absence is part of the decision
- [ ] The end-to-end join fixture stays green
