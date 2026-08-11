# 10 — The signal server's comments point at the ADR instead of restating it

_Repo: **ArenaInfra**. Source: [W4 of the trickle-ICE follow-ups spec, server half](../../../docs/plans/trickle-ice-follow-ups-spec.md)._

**What to build:** Knowledge that already lives in an ADR is **referenced** rather than paraphrased in a docblock, so the two cannot drift apart. The signal server's session-sweep docblock and session-table comment restate an ADR and the repo glossary nearly verbatim — the same knowledge in three files that will diverge. Point at the ADR.

The rest of the server's newly-added commentary gets the same sweep as the game half: a comment survives only where the knowledge has no other home in the file and cannot be expressed in code.

**Blocked by:** 04 — The candidate relay's direction branches become one role table _(sweep the final shape, so the sweep isn't done twice)_.

**Status:** ready-for-agent

- [ ] The session-sweep docblock and session-table comment reference the ADR rather than restating it
- [ ] The rest of the server's added commentary is swept against the same line as the game half
- [ ] No comment paraphrases the glossary
- [ ] The signal server's suite is green
