# 11 — A fresh clone can run `npm test`

_Repo: **ArenaInfra**. Source: [W8 of the trickle-ICE follow-ups spec](../../../docs/plans/trickle-ice-follow-ups-spec.md)._

**What to build:** A maintainer who clones the signal server can run its test script without editing the manifest first. The manifest's `engines` range permits a Node older than the package's own test script requires. The upper bound was narrowed for the WebSocket library's ABI; the lower bound never moved with it.

Raise the lower bound to the Node the test script actually needs.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [ ] The manifest's `engines` lower bound permits only Node versions the test script can run on
- [ ] The upper bound is unchanged — it exists for the WebSocket library's ABI
- [ ] The test script runs on a fresh clone with no manifest edit
