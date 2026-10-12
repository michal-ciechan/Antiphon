# CARD-0955 redacted land-failure frames

## Decision

`LandFailureDiagnostic.RedactedFrames` is already on this branch, landed in `e52179de0206c99e3a6ff67dd010651fa63e2354`. `AgentTaskLandService.FailRequestAsync` and every `AgentTaskLandHostedService` warning pass that string as `{Frames}` on the same log call as `{DiagnosticId}`. The exception object is not an argument.

The helper keeps at most 16 frames from the throw site. Each frame is `BoundIdentifier(method name, 100)` plus `:{line}` when `GetFileLineNumber()` is positive. `BoundIdentifier` keeps only ASCII letters, digits, and underscores. An empty walk or any thrown reader returns `unavailable`. It does not call `Exception.ToString`, `StackTrace.ToString`, or `GetFileName()`.

Inner-exception stacks are not walked. A caller past frame 16 is omitted. CARD-0972 owns that cap. This slice does not change production: a new assertion of the landed contract is green on this HEAD, and a test that cannot go red is a stub.

## Files

No production or test edit. This note is the slice.

## Test list

| Case | Mutation | Red |
|---|---|---|
| C955 frame text contains `ThrowSensitiveLandFailure`, shares `FailureDiagnosticId`, and excludes `C:\private\source.cs`, `/private/source.cs`, `secret_token_12345`, and `@password=secret_sql_parameter` | Drop the `{Frames}` value (parent of `e52179de0`, test introduced by `64115bfd9`) | `D.FramesEmitted` |
| Hosted drain logs share that diagnostic id, emit frames, and exclude the synthetic marker | Log a new diagnostic id on the hosted warning | `D.HostedFrameDiagnosticId` |

Those guards are already in the tree. This task does not revert them to manufacture a red; that cycle belongs to SourceLanding Mutation.

## Verification design

V-1: `AgentTaskLandFailureDiagnosticTests.C955_RedactedFramesKeepDiagnosticAndExcludeExceptionContent` is the frame and planted-secret guard.

R-1: `AgentTaskLandFailureDiagnosticTests.C498_HostedDrainClassifiesExecutionException` is the hosted diagnostic-id and frame guard.

R-2: `LandFailureDiagnosticTests.C498_ExceptionTypeNameIsBoundedIdentifier` is the identifier-alphabet guard used by frame names.

PC-1: returning `unavailable` from the frame helper is killed by `AgentTaskLandFailureDiagnosticTests.C955_RedactedFramesKeepDiagnosticAndExcludeExceptionContent`.

PC-2: copying the exception message into the frame text is killed by `AgentTaskLandFailureDiagnosticTests.C955_RedactedFramesKeepDiagnosticAndExcludeExceptionContent`.

PC-3: giving the hosted success warning a new diagnostic id is killed by `AgentTaskLandFailureDiagnosticTests.C498_HostedDrainClassifiesExecutionException`.

Post-land Mutation was not run. PC-1, PC-2, and PC-3 stay pending.

### Checkpoints

Closed list. One isolated build. No whole-Unit lane. Filters use wildcards.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c0955/` | c0955-unit | `/*/*/LandFailureDiagnosticTests/*` | R-2 | all 12 listed results, 0 failed/skipped | 12 | 15 | true |
| CP-2 | S1 | `CP-1` | c0955-land-frames | `/*/*/AgentTaskLandFailureDiagnosticTests/*` | V-1, R-1 | all 26 listed results, 0 failed/skipped | 26 | 15 | true |
