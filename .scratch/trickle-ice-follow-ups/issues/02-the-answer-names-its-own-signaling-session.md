# 02 — The answer names its own signaling session

_Repos: **ArenaURP + ArenaInfra, in lockstep**. Source: [W1 of the trickle-ICE follow-ups spec](../../../docs/plans/trickle-ice-follow-ups-spec.md)._

**What to build:** A player joining a room has their answer reach the exact handshake their offer came from. A second join attempt against the same host can no longer silently hijack the first, and a peer that is not a member of a handshake cannot relay signalling into it.

Today the answer carries no **session token**, so the server *guesses* which **signaling session** it belongs to by scanning for the (client, host) pair, newest first. The client already holds the token — it was stored on its own trickle route when the session was created — so the fix costs one length-prefixed field.

The scan is also doing authorization work by construction: it can only ever find a session this client owns. A direct lookup drops that for free, so the replacement must **state the ownership check as a line** — the session's client player id must equal the sending socket's. That is the gain, not a cost: an implicit property of a loop becomes an auditable assertion.

The wire change is **client → server only**:

```
client → server   0x05  int32 targetPlayerId  [tok]  sdp…      ← [tok] is new
server → host     0x05  int32 connectionId           sdp…      ← unchanged
```

`[tok]` uses the same one-byte-length + UTF-8 framing the offer already carries, written through the existing session-token helper rather than by hand. Because the server→host direction is untouched, the game's inbound parser is not modified.

**This is the only cross-repo item.** A wire mismatch is a hard failure at handshake time, not a degraded experience — the two halves land together or not at all. It must be **its own commit**, containing no code-shape changes: the relay is security-critical and must not be reshaped in the same diff that changes the contract it enforces.

**Blocked by:** 01 — One name for the player id in the signal server _(it renames the same session fields; interleaving the two is the thing to avoid)_.

**Status:** ready-for-agent

- [ ] The client appends the session token it already holds to the answer, through the existing framing helper
- [ ] The server reads the token and resolves the session directly — no scan
- [ ] The server explicitly asserts the sending socket owns the session it named, and drops the message silently otherwise
- [ ] The scan function is **deleted**, not fixed
- [ ] New harness test: an answer naming a live session routes to the host with the session's connection id stamped
- [ ] New harness test: an answer naming an unknown token is dropped silently
- [ ] New harness test: an answer naming a session the sender does not own is dropped silently — **this is the test that must exist**, it is the ownership property the deleted scan used to provide implicitly
- [ ] The signal server's suite is green: 33 existing plus the new tests
- [ ] The end-to-end join fixture is green **with both repos changed** — this is where "the two repos agree" becomes a fact the suite establishes rather than a claim. The wire change is not done until it passes
- [ ] The wire change is one commit carrying no refactors
