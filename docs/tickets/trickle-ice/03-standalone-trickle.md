# 03 — Standalone trickle: instant SDP, streamed candidates, sub-second join

**What to build:** the whole trickle path for the **Unity.WebRTC / standalone** transport (editor
and desktop builds). An editor host and client join through the signal server and reach
data-channels-open in well under a second instead of the ~8s two-stall floor, with ICE still
promoting to the better path afterwards as candidates keep arriving.

End to end: offer and answer go out the instant the local description is set, advertising
`a=ice-options:trickle` and carrying **no** candidates; every candidate the agent gathers is
relayed as it appears, through the server-owned signaling session addressed by its token; each
side buffers inbound candidates until its remote description exists, then flushes them in order;
gathering-complete sends the empty end-of-candidates marker.

**Blocked by:** 01 (the harness that verifies it), 02 (the contract and the queue).
**Also gated externally by ArenaInfra S1–S5** — the session table and the 0x08/0x09 relay routes.
Nothing connects until that ships; the two repos land together.

**Status:** ready-for-agent

**Notes**

- The **signal manager stays a dumb pipe**: it routes a trickled candidate to the right peer-
  connection wrapper and never inspects remote-description state. The wrapper owns the queue
  (ADR 0011).
- The host associates each connection with its session token when it creates and sends the offer,
  so it can address host→client trickles.
- **Fold in the candidate-filter consolidation (plan T5).** The `filterLocalConnections` diagnostic
  moves to a single emit/drop predicate at the gather site, beside the existing UDP-only check,
  replacing the SDP-string scrub that ran over the bundled array. Keep the flag off by default.
- **Leave the WebGL surface alone.** The bundled-candidate field on the offer/answer struct, the
  collect-duration setting and the jslib all stay as they are — ticket 04 deletes them together, so
  both tickets stay green and WebGL ↔ WebGL keeps working. A **cross-transport** join is expected
  to be broken between 03 and 04; see the README's green-gap note.
- **Verify the configured STUN host still resolves.** A dead STUN server now delays
  *gathering-complete* — the end-of-candidates trigger — rather than just wasting a bundle slot.
  Replace it if it is dead.

- [ ] Offer and answer are sent as soon as the local description is set — the fixed gather delay is
      gone from both the offer and the answer path
- [ ] Offers and answers advertise `a=ice-options:trickle` and carry no candidates
- [ ] Each gathered UDP candidate is relayed as it appears, in the correct direction, addressed by
      the session token
- [ ] Gathering-complete emits the empty-candidate marker exactly once per connection
- [ ] Candidates arriving before the remote description is set are buffered and flushed in arrival
      order; later ones apply immediately
- [ ] One filter predicate per transport at the gather site; the SDP-string candidate scrub is gone
- [ ] Ticket 01's PlayMode test still passes, and the logged join is **well under a second**
      against the baseline it recorded
- [ ] The configured STUN host is confirmed reachable (or replaced)
