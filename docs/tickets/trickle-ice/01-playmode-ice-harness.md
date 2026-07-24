# 01 — PlayMode ICE harness against a locally-launched signal server

**What to build:** a PlayMode test that stands up a real `CanoeWebRTC` host and client in the
editor, talking to a signal server the fixture launches and reaps itself, and asserts both data
channels open. Built **against today's bundled-candidate path**, where it should already pass —
so the harness is proven before the protocol changes underneath it, and it records the join time
we are trying to beat.

This is the prefactor that makes the trickle change easy: ticket 03 inherits a working
end-to-end assertion instead of debugging a new fixture and a new wire protocol at once.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

**Notes**

- Assert on **channels-open with a generous timeout**. No timing assertions — real async ICE is a
  known flakiness source (see the combat-harness fixture for the house pattern).
- The join time is captured as a **logged baseline**, not an assertion. Expect roughly the ~8s
  floor (two serial ~4s gather stalls).
- The signal server lives in the sibling **ArenaInfra** repo and already listens on 9001. If the
  fixture needs port isolation, that repo threads a port env var through — coordinate rather than
  hard-coding a second port here.
- Reap the server process on teardown including on test failure, so a crashed run does not leave a
  listener holding the port for the next one.

- [ ] A PlayMode test in `Arena.PlayTests` launches a signal server, hosts, joins, and asserts both
      peers reach data-channels-open
- [ ] The fixture reaps the server on teardown, on both the pass and the fail path
- [ ] The test passes repeatedly against the current (pre-trickle) code — run it enough times to
      show it is not flaky
- [ ] The observed join duration is logged so 03 can be compared against it
- [ ] If the harness proves irreducibly flaky in-editor, say so in the ticket and stop — do not
      absorb the flake into the suite; the manual two-browser gate carries verification instead
