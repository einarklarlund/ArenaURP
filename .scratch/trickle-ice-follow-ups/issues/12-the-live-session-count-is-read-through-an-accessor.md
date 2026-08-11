# 12 — The live session count is read through an accessor, not a log regex

_Repo: **ArenaInfra**. Source: [W8 of the trickle-ICE follow-ups spec — the largest item on the list](../../../docs/plans/trickle-ice-follow-ups-spec.md)._

**What to build:** A maintainer can improve a log message without breaking tests. The harness reads the live **signaling session** count by regex-matching a human-readable log line, which makes log wording a test contract for no behavioural reason. Expose the count as a test-only module export instead.

**This carries a structural consequence that must be planned for, not discovered.** The server module performs all of its setup as top-level side effects — it opens its listen socket and starts two intervals at load. A test that requires the module therefore acquires a listening socket and two live timers in the test process, and the test runner will not exit while those timers are alive. So this is **not a one-line export**: the server's startup and shutdown have to become callable, and the harness needs an in-process mode alongside the spawned-child mode it uses today.

**This is explicitly droppable.** If it proves larger than its value, abandon it and leave the log-scraping in place — and say so explicitly rather than half-landing it.

**Out of scope either way:** the nine other log-text assertions across routing, session, and trickle. They are defensible while the log is the only observable for a silently dropped message.

**Blocked by:** 02 — The answer names its own signaling session _(the harness's session-count mechanism should not change under the security-critical contract work)_.

**Status:** ABANDONED 2026-08-11, deliberately — the log-scraping is left intact and untouched.

## Why it was abandoned

The ticket allowed for this ("if it proves larger than its value"). It is larger, but that is not
the deciding argument. **The change cannot deliver its own stated benefit.**

The benefit is "a maintainer can improve a log message without breaking tests". Measured against
the suite as it stands on 2026-08-11:

- `liveSessionCount` is called at **16 sites**, in `lifecycle.test.js` (11) and `session.test.js` (5).
- `session.test.js` — one of the two files that would migrate — **also asserts on log text** at
  line 52: `/Session \w+ minted for host 1, client 2\./`. So after the migration, log wording is
  still a test contract *in the very file the migration was for*.
- Ten further log-text assertions across routing, session and trickle are **out of scope by the
  ticket's own terms**, and stay.

So the mechanism changes and the property does not.

Against that, the cost is not just the lifecycle refactor the ticket anticipated:

1. Startup/shutdown become callable — the part the ticket priced in.
2. **The Redis client must join that lifecycle too.** `new Redis(url)` connects and retries at
   require-time, not just the listen socket and the two intervals.
3. **A full module-state reset is needed on every in-process start.** `sessions`, `rooms`,
   `playerID` and the `currentID` counter are module-level and would leak between tests in one
   process. `session.test.js:51` asserts `clientPlayerID === 2` and `:52` matches `host 1, client 2`
   — both depend on a freshly-numbered process. That reset is test-only surface in production code.
4. **The harness's in-process mode must also capture `console.log`**, because `session.test.js`
   needs the accessor *and* `server.log()` in the same test.

Trading a test-only export plus a test-only state reset plus a dual-mode harness for a property
the suite does not end up having is a bad trade. Log text stays the observable until there is a
better one for silently-dropped messages — which is the same reason the other ten assertions were
already ruled out of scope.

**Not half-landed:** no part of this was started. `SignalServer.js` still starts at require-time,
the harness has one mode, and `liveSessionCount` still reads the log.

**If it is ever revived**, revive it with the ten sibling assertions, not without them — the
property only exists once the log stops being an observable anywhere in the suite.

## Original acceptance criteria (not met, by decision)

- [ ] The server's startup and shutdown are callable rather than top-level side effects
- [ ] Requiring the module does not open a socket or start timers on its own
- [ ] The harness gains an in-process mode beside its existing spawned-child mode, and the test runner exits cleanly
- [ ] The live session count is a test-only module export
- [ ] The tests that counted sessions read the accessor; the existing lifecycle and session assertions are the coverage and stay green through the change of mechanism
- [ ] The other nine log-text assertions are untouched
- [ ] Its own commit
- [ ] If abandoned: the log-scraping is left intact and the abandonment is recorded, not half-landed
