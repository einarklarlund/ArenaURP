# 12 — The live session count is read through an accessor, not a log regex

_Repo: **ArenaInfra**. Source: [W8 of the trickle-ICE follow-ups spec — the largest item on the list](../../../docs/plans/trickle-ice-follow-ups-spec.md)._

**What to build:** A maintainer can improve a log message without breaking tests. The harness reads the live **signaling session** count by regex-matching a human-readable log line, which makes log wording a test contract for no behavioural reason. Expose the count as a test-only module export instead.

**This carries a structural consequence that must be planned for, not discovered.** The server module performs all of its setup as top-level side effects — it opens its listen socket and starts two intervals at load. A test that requires the module therefore acquires a listening socket and two live timers in the test process, and the test runner will not exit while those timers are alive. So this is **not a one-line export**: the server's startup and shutdown have to become callable, and the harness needs an in-process mode alongside the spawned-child mode it uses today.

**This is explicitly droppable.** If it proves larger than its value, abandon it and leave the log-scraping in place — and say so explicitly rather than half-landing it.

**Out of scope either way:** the nine other log-text assertions across routing, session, and trickle. They are defensible while the log is the only observable for a silently dropped message.

**Blocked by:** 02 — The answer names its own signaling session _(the harness's session-count mechanism should not change under the security-critical contract work)_.

**Status:** ready-for-agent

- [ ] The server's startup and shutdown are callable rather than top-level side effects
- [ ] Requiring the module does not open a socket or start timers on its own
- [ ] The harness gains an in-process mode beside its existing spawned-child mode, and the test runner exits cleanly
- [ ] The live session count is a test-only module export
- [ ] The tests that counted sessions read the accessor; the existing lifecycle and session assertions are the coverage and stay green through the change of mechanism
- [ ] The other nine log-text assertions are untouched
- [ ] Its own commit
- [ ] If abandoned: the log-scraping is left intact and the abandonment is recorded, not half-landed
