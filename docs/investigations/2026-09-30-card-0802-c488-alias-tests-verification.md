# CARD-0802 C488 alias verification

## Baseline protocol

- Source SHA before test edits: `037b24a352a960a0ed35a10d171ea92f2814f4be`.
- Environment: Linux x86_64 (`4.15.0-213-generic`), .NET SDK `10.0.401`, runner mirror `/work/worktrees/task-6dfe4f27`.
- The task-owned census driver is `.antiphon/c802-census.ps1`, copied verbatim from the plan; SHA256 `8fd0ca720389f6959bd4a717b9db457b53196360dbab0830d1fe0bc8c7b3d7fe`.
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

Pending successful CP-1 through CP-5.
