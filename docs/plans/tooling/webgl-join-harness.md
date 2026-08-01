# WebGL join harness — drive a served build from headless Chrome

_Grill-ready brief. Origin: trickle-ICE follow-up **F9**, agreed 2026-07-31 as the first entry on the tooling backlog. Sized as a `/prototype` first — one question below decides whether the rest is worth designing._

## The recurring cost

**WebGL is the shipping configuration.** CI builds it and deploys to `gh-pages` on every push to `main`; the jslib is what actual players run. Its total runtime evidence is one afternoon — the two manual gates (punch list T6/T7) run by hand on 2026-07-31.

The coverage is inverted. The standalone transport, which ships to nobody, is defended by `WebRtcJoinTests` launching a real signal server and driving a real handshake on every run. The jslib — the half the trickle plan requires to stay *behaviourally identical* to standalone, and which already carries three recorded divergences (see `docs/known-issues.md`, F6) — is defended by a compile, a `node --check`, and a Node harness over the extracted `Connection` class. Every future jslib edit silently returns this path to unverified until someone volunteers an afternoon with two browser windows.

## The opening

A Unity WebGL build exposes **`unityInstance.SendMessage(gameObject, method, arg)`** to page JavaScript. That is a real control surface into a running build from outside it: a headless-Chrome driver (Playwright or Puppeteer) can load the served build in two pages, tell one to host and the other to join, and read the outcome back off the console. No clicking into a canvas, no pixel matching, no image diffing — the fragile parts of browser automation are simply not involved.

`SendMessage` takes a GameObject **name** (not a path), a method name on a component attached to it, and at most one string/number argument. It is fire-and-forget: there is no return value, so results come back the other way — `Debug.Log` surfaces in the browser console, which the driver reads via `page.on('console')`. That asymmetry shapes the design: commands go in by `SendMessage`, observations come out as structured log lines.

The same driver could assert the specific things a human is asked to eyeball today:

- exactly **one** empty end-of-candidates trickle per peer connection per side — browsers signal gathering-complete twice, so this is precisely the kind of once-only condition an eye slides past;
- the host's first candidates reaching the server only **after** its offer — a mis-ordering shows up as a slow or failed join, never as an error;
- no `Failed to add ICE candidate` spam;
- **join duration**, which extends the 0.5s ceiling agreed for `WebRtcJoinTests` (follow-up F8) to the transport that currently has no timing evidence at all. The standalone baseline is 0.05s against an 8.05s bundled floor; the browser number is unknown, and establishing it is itself worth the prototype.

## What has to be answered before this is a ticket

**1. Does headless Chrome gather usable candidates? — the kill question.** Data channels need no media, so none of the fake-device flags apply, but mDNS candidates may be absent headless. The Local and WebGL-dev bootstrap paths configure **zero ICE servers**, so a loopback join should have host candidates to work with. This needs proving, not assuming, and it is the single most likely reason the idea fails. **Nothing else gets designed until this is answered.**

**2. Is there a `SendMessage`-reachable entry point?** `RoomManager`'s host and join paths are driven by UI today, so there is no named GameObject with a one-argument method to call. This likely needs a small dev-only bridge component on a known GameObject, present only in development builds. That is a design question, not a scripting one: *what is the smallest surface that does not become a second, drifting way to start a match?* A bridge that reimplements the join sequence is a liability — it would pass while the real UI path is broken. The bridge should call the same entry points the buttons call, and nothing else.

**3. T7 is the harder half.** A browser client against an *editor* host means driving the editor too. A standalone player build as the host is probably the tractable substitute, at the cost of no longer testing the editor path specifically — which is the path a developer actually uses. Worth deciding whether the cross-transport gate is browser↔browser (cheap, and covers the jslib) or browser↔standalone (covers the mixed-transport case that T7 was written for).

**4. Where does it run?** Locally as a `deploy_local.sh` companion is the cheap version. CI needs a WebGL build per run, which is minutes, so it likely belongs on a pre-merge or nightly job rather than on every push. A harness that only ever runs locally is still worth building — it converts an afternoon into a command — but it will rot unless something runs it.

## Why it is worth grilling rather than just doing

If it works, it converts the feature's least-defended surface from "ask Einar" into something an agent can run unattended — the same move that took the signal server from unverifiable to 34 tests. If it does not, an afternoon closes F9 permanently with a stated reason, which is worth more than leaving it open.

The prototype's output is **the decision**, not shipped code.
