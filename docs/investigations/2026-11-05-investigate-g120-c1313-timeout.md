# g120 C1313_BaselineAuthority timeout investigation

The Windows root cause remains **unconfirmed**. `root=True` can describe a canceled wait whose process has not exited, and all 14 PASS lines precede fixture cleanup. A portable replay confirms that ambiguity.

10 Linux direct-case runs completed: 5 rc and 5 master, each with 14/14 assertions and exit 0. Min/median/max seconds: rc **246.882 / 263.002 / 294.207**; master **252.270 / 261.139 / 290.098**. No exit/EOF hang was reproduced. **30/40 requested repetitions remain unrun** under the 45-minute budget; these are not Windows/TUnit qualification results.

The server build used for concurrent load succeeded with 0 errors and 24 warnings. The relevant harness/case/helper bytes are identical on rc and the sampled master. No fix was designed or implemented.

The original g120 C487 tail/timestamps and native root/task/pipe state are still needed to distinguish load-sensitive execution from a native wait/handle defect. Full source citations, sample timings, process observations and measurement limitations are in the [CARD-1514 evidence report](2026-11-05-card-1514-investigate-and-speed-up-the-slowest-test-classes-agenttasklandpersistencefailuretests-24-32-min-checkpointmatrix-26-min-publication.md).

--- next stage ---
next: investigate
handoff: Retrieve the original g120 TRX/console timestamps and C487 tail, then native pre-cleanup root/task/pipe evidence when the live run permits; complete the disclosed repetition shortfall.
artifact: docs/investigations/2026-11-05-card-1514-investigate-and-speed-up-the-slowest-test-classes-agenttasklandpersistencefailuretests-24-32-min-checkpointmatrix-26-min-publication.md
