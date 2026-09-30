# CARD-0802 C488 alias verification

## Baseline protocol

- Source SHA before test edits: `037b24a352a960a0ed35a10d171ea92f2814f4be`.
- Environment: Linux x86_64 (`4.15.0-213-generic`), .NET SDK `10.0.401`, runner mirror `/work/worktrees/task-6dfe4f27`.
- The task-owned census driver is `.antiphon/c802-census.ps1`, copied verbatim from the plan; SHA256 `8fd0ca720389f6959bd4a717b9db457b53196360dbab0830d1fe0bc8c7b3d7fe`.
- CP-1 builds `bin-c802-before/` and selects exactly the three named methods in the plan, preserving its fresh TRX and producer report. CP-2 discovers the unfiltered assembly from that retained output. Case durations will be read from outer `UnitTestResult` records joined by test ID to `TestDefinitions/UnitTest/TestMethod`.
- Baseline card evidence of 187.870 s belongs only to `C488_DetachedFollowUpPublishesReviewedFix` in an earlier Linux full-suite run; it is not a measurement of either alias here.

## Checkpoint results

Pending CP-1 through CP-5.
