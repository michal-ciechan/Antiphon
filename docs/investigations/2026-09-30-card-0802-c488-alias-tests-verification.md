# CARD-0802 C488 alias verification

## Baseline protocol

- Source SHA before test edits: `037b24a352a960a0ed35a10d171ea92f2814f4be`.
- Environment: Linux x86_64 (`4.15.0-213-generic`), .NET SDK `10.0.401`, runner mirror `/work/worktrees/task-6dfe4f27`.
- The task-owned census driver is `.antiphon/c802-census.ps1`. Its initial plan-verbatim SHA256 was `8fd0ca720389f6959bd4a717b9db457b53196360dbab0830d1fe0bc8c7b3d7fe`. After the first CP-2 attempt exposed multiline diagnostic records, the driver gained a loss-checked line-folding adapter; its final SHA256 is `ea9e6240e537e74448d9d3f13a88f8324e58ecdd01e8dc37d40ca321815ab918`. CP-2 and CP-5 use these identical final bytes.
- CP-1 builds `bin-c802-before/` and selects exactly the three named methods in the plan, preserving its fresh TRX and producer report. CP-2 discovers the unfiltered assembly from that retained output. Case durations will be read from outer `UnitTestResult` records joined by test ID to `TestDefinitions/UnitTest/TestMethod`.
- Baseline card evidence of 187.870 s belongs only to `C488_DetachedFollowUpPublishesReviewedFix` in an earlier Linux full-suite run; it is not a measurement of either alias here.

## Checkpoint results

The first CP-1 attempt at `b57343396a14b26093c865e3cecb140a69fd4385` is retained at
`.antiphon/checkpoints/20260930-091428-142a/`: 3 executed, 0 passed, 3 failed,
0 skipped. Each failure was the fixture's nested verifier build. A separate,
explicitly diagnostic build of the same miniature solution reproduced SDK
`10.0.401` error `MSB3030`, missing `obj/Antiphon.Tests/debug/apphost`; the same
build passed with process environment `UseAppHost=false`. The diagnostic used
the host build slot. The baseline checkpoint will be rerun with that Linux
environment setting inherited by the nested verifier build. No test body was
changed for this recovery.

CP-1 rerun at `8c42e138dae1ab896d86018976c846932ee1be7c` is green:
3 executed, 3 passed, 0 failed, 0 skipped. Filter:
`/*/*/AgentTaskLandSourceFreshnessTests/(C488_DetachedFollowUpPublishesReviewedFix*)|(C488_BehindSelectsRemote*)|(C488_DetachedFollowUpRequiresFetch*)`.
Fresh TRX: `.antiphon/checkpoints/20260930-092454-d66b/rows/CP-1/run.trx`.
Complete producer report copied verbatim to `.antiphon/c802-before-producer.json`
(SHA256 `bd7fc85bbc66117ea8007be0799b75f6334635ee9587dda084370e3bd87198e4`).
Host: Debian 12 x86_64, SDK 10.0.401; `UseAppHost=false` inherited by the
miniature verifier. Build slot: unleased after 60 s broker wait. CP-1 wall:
325 s; row wall: 175.826 s. Outer TRX durations (all passed): main
`17.2107442` s, Behind alias `22.9444085` s, RequiresFetch alias
`24.8679250` s. Combined three `65.0230777` s; aliases `47.8123335` s.
Each identity invokes the main body, which calls the real landing verifier
once and the independent target verifier once: 6 real invocations total.

Pending CP-2 through CP-5.

The first CP-2 attempt at `f3386cc04707d2628a3d1ba87c9535827c2d2915`
is retained at `.antiphon/checkpoints/20260930-093158-f7a6/`, with its
phase directory archived as `.antiphon/c802-census/before-attempt1/`. It
failed before export because 13,118 raw lines contained the discovered-state
marker while the nightly one-line parser returned 13,092 nodes. Inspection
showed 28 records split across lines by embedded newline display arguments.
The task-local adapter folds each such record, checks that no state marker was
lost, then applies the same pinned strict parser. This changes census input
framing only; raw diagnostics remain retained. The rerun uses the existing
CP-1 producer output and performs no test execution.
