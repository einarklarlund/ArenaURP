# 01 — One name for the player id in the signal server

_Repo: **ArenaInfra**. Source: [W7 of the trickle-ICE follow-ups spec](../../../docs/plans/trickle-ice-follow-ups-spec.md)._

**What to build:** A maintainer reading the signal server's anti-spoof check sees one concept under one name. The per-socket identifier is called a **player id** everywhere — in the glossary, on the socket, on the **signaling session**'s two members, and in the player sweep — so nobody has to first establish that two identifiers hold the same value before they can audit whether a peer is allowed to relay into a session.

Today the session fields are named for a term the glossary never defines, sitting directly beside the table that holds exactly the same value, and the membership comparison reads as two concepts being equated.

This lands **first** deliberately: it renames the exact fields the session-token work rewrites, and doing it after would mean a rename interleaved with a security-critical contract change.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [ ] The session's host and client members, and the per-player sweep, use the glossary's **player id** term
- [ ] No identifier in the server introduces a synonym for player id
- [ ] The membership check reads as one concept compared against itself, not two names
- [ ] The signal server's suite is green at its full 33-test baseline — the rename adds no tests, and all 33 staying green **is** the assertion
