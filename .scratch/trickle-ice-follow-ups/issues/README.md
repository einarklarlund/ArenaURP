# Trickle ICE follow-ups — tickets

Produced by `/to-tickets` on 2026-08-11 from [the spec](../../../docs/plans/trickle-ice-follow-ups-spec.md). Numbered in dependency order — blockers first. Filed here rather than on GitHub because `gh` is not installed on this machine; they can be lifted into issues unchanged.

**Two repos.** `ArenaURP` and `ArenaInfra` ship in lockstep. Ticket 02 is the only one that crosses the wire contract, and its two halves land together.

| # | Ticket | Repo | Blocked by |
|---|---|---|---|
| 01 | One name for the player id in the signal server | ArenaInfra | — |
| 02 | The answer names its own signaling session | **both** | 01 |
| 03 | Length-prefixed framing becomes one named pair of helpers | ArenaInfra | 02 |
| 04 | The candidate relay's direction branches become one role table | ArenaInfra | 03 |
| 05 | The editor's signal address takes a port override | ArenaURP | — |
| 06 | A slow join fails the suite | ArenaURP | — |
| 07 | Every `-1` says which concept it is | ArenaURP | — |
| 08 | The outbound candidate buffer is a tested pure type | ArenaURP | — |
| 09 | The game's comments follow the rule the repo states | ArenaURP | — |
| 10 | The signal server's comments point at the ADR | ArenaInfra | 04 |
| 11 | A fresh clone can run `npm test` | ArenaInfra | — |
| 12 | ~~The live session count is read through an accessor~~ **ABANDONED** | ArenaInfra | 02 |
| 13 | The hook config resolves the project directory once | ArenaURP | — |

**The frontier at the start:** 01, 05, 06, 07, 08, 09, 11, 13 can all start immediately. 02→03→04 is the only long chain, and it is long on purpose: the relay is security-critical and must not be reshaped in the same diff that changes the contract it enforces.

**Baselines that must stay green.** The signal server's suite measured **34 tests** when the work started on 2026-08-11 — so the plan's "34" was right and this file's "33" was the miscount. It is **37** now: ticket 02 added three. The end-to-end join fixture must be green at every landing.

## Outcome — 2026-08-11

Twelve of thirteen landed. Every ticket is its own commit, on `worktree-trickle-ice-followup-grill`
(ArenaURP) and `trickle-ice-follow-ups` (ArenaInfra); neither branch is pushed.

**Ticket 12 was abandoned, deliberately and in full** — see [its own file](12-the-live-session-count-is-read-through-an-accessor.md) for the argument. In short: `session.test.js` would still
assert on log text after the migration, so the change cannot buy the property it exists for. The
log-scraping is untouched; nothing was half-landed.

**Dropped from the plan, deliberately.** `CancelJoin`'s double transport walk — it was already fixed when join cancellation shipped, and the plan recorded it as outstanding. There is no ticket for it. _(Third stale trickle finding on re-check; see the `unactioned-findings-need-evidence` memory.)_
