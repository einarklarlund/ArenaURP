# 06 — A slow join fails the suite

_Repo: **ArenaURP**. Source: [W3 of the trickle-ICE follow-ups spec](../../../docs/plans/trickle-ice-follow-ups-spec.md)._

**What to build:** A regression that makes joining slow again fails the build instead of passing it. Today the join fixture measures the duration and only **logs** it; its one real bound is a 30-second liveness timeout, which sits far above the ~8-second floor of the bundled-candidate path trickle ICE replaced. A full regression back to that path would go green.

Keep the existing timeout as the **liveness** bound — "did it connect at all" — and add a **second, explicit ceiling** on the measured stopwatch.

- **Ceiling: elapsed < 0.5s.** This is a loopback join between a host and a client in one process on one machine. The measured baseline is 0.05s and the path this replaced had an ~8s floor — an order of magnitude above the baseline, an order of magnitude below the regression it must catch.
- **The two failures must read differently.** The timeout says *never connected*. The ceiling says *connected, but slowly*.
- The slow-join message quotes **both** the ceiling and the recorded 0.05s baseline, so a future decision to raise it is informed rather than reflexive.

This is not an ICE-timing assert — the plan's "no timing asserts" position stands for ICE. This is a floor-versus-ceiling regression detector with 10× margin on both sides.

The stopwatch already starts at the join attempt, after server-up and room-created, so no setup cost sits inside the measurement. That does not change.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [ ] The existing liveness timeout is unchanged and still says *never connected*
- [ ] A second assertion bounds the measured elapsed time at 0.5s
- [ ] The slow-join failure message names both the ceiling and the 0.05s baseline
- [ ] The two failure messages are distinguishable without reading the test source
- [ ] The measurement window still excludes server-up and room-created
- [ ] Deliberately verify the test **can fail** — the gate is "can this test fail?", so confirm an artificially slowed join trips the ceiling rather than the timeout
