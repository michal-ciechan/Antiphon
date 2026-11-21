# Why missed: C475 attempt-before-write (rc run 3528e6b1 group-71)

Windows Full run 3528e6b1 on rc/2026-11-30 tip `bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0` failed one test in chunk `antiphon-all-group-71.trx`:

`SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery(attempt-before-write)` (18.2s)

```
ShouldAssertException: Task attempt should throw System.InvalidOperationException but did not
```

Debug task f1382575. The live rc run was not touched.

## What the assertion means

`attempt-before-write` blocks the forwarding client, starts a WhenIdle enqueue, waits up to 15s for `WriteReached`, then arms `CutBeforeWrite` and `FailRevert` and requires the enqueue task to throw `InvalidOperationException` (`SessionQueueReceiptPlumbingTests.cs` lines 71-92). Shouldly's "but did not" means the task completed without throwing. A different exception would name that type. The `finally` replaces any earlier failure, including a `WriteReached` timeout, so the reported text does not say whether the write barrier fired.

The cut throws only after `ForwardingClient.WriteAsync` is entered (`CutBeforeWrite`, line 855). `DeliverNextLockedAsync` then reverts the Sent row, and `InsertFault.FailRevert` throws again on that save (lines 877-879 and 3030-3040). Several returns never reach that cut and still complete the enqueue:

- WhenIdle leaves the row Pending when the session is not idle (`EnqueueCoreAsync`, around line 972).
- Claude `SendInputAsync` observes the dialog and `ProviderDialogObservationService.Request` throws `ProviderDialogInputRefusedException` before any client write (2s read bound, `ProviderDialogObservationService.cs` line 17). `DeliverAsync` turns that into `ModalBlocked` (around line 4400) and `HandleDeliveryFailureAsync` records a verdict and returns.
- `InputDeliveryUncertainException.Is` (`5b80144d3cc7dcdcdb50c85ee03cd737e1508b41`, CARD-1892, 2026-11-13) treats `TimeoutException`, `IOException`, `HttpRequestException`, cancellation, and two phone-home codes as uncertain. That catch returns `FlushResult.Failed` without `FailRevert` (`SessionMessageQueueService.cs` lines 3010-3028).

Sibling cuts arm the fault before enqueue. Only this arm waits and then demands the exception, so a pre-write return is reported as "did not throw". 18.2s matches fixture startup plus that 15s wait.

## Not an rc-only or 2026-11-21 pointer change

These blobs are identical on `bd34a7ff6` and `origin/master` `2afaf4beb` (2026-11-21):

- `server/Application/Services/SessionMessageQueueService.cs`
- `server/Application/Services/SessionMessageQueueService.CodexPointers.cs`
- `server/Application/Services/AgentSessionRuntime.cs`
- `server/Application/Services/ProviderDialogObservationService.cs`
- `server/Application/Exceptions/InputDeliveryUncertainException.cs`
- `tests/Antiphon.Tests/Application/SessionQueueReceiptPlumbingTests.cs`

`SessionMessageQueueService.cs` last changed at `5eb2bc29a` (2026-11-15, CARD-1943). `CodexPointers.cs` last changed at `d535c750d` (2026-10-23). The test last changed at `cad310b24965f3e45132836ce45581e7c276e93f` (2026-10-27). The 2026-11-21 Codex 0.170 pointer commits (`9adbf3ba1`, `b963a3e8b`) do not change these files. A second build at master would not separate an rc-only edit from inherited code.

## Linux reproduction at the rc tip

One leased build, load 4.82, `ANTIPHON_LINUX_COMPILER_POLICY=0`, fixture database only, `--timeout 570s`:

```
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c475-group71-debug -- dotnet run --project tests/Antiphon.Tests -p:UseAppHost=false -- --treenode-filter '/*/*/SessionQueueReceiptPlumbingTests/*' --timeout 570s
```

Lease `68e5e47b-efd3-4b03-a7ac-5fc2affe0335`, waited 0s, held 237s. Class result: 22 total, 6 passed, 16 skipped, 0 failed, 30.3s. `attempt-before-write` is `NotExecuted` with `ConPTY only on Windows` (line 38). Passed: all five `C475_PumpPersistsCompleteLinesOnce` arms and `C1022_Partial_receipt_turn_parks_interrupted_attempt`. The same skip is why a Linux run cannot show the Windows red.

## Why merge and earlier runs did not catch it

The class is OS-gated (`tests/derived-impact-index-known-os-gated.txt`). Every native method throws `SkipTestException` off Windows. Merge verification is short (2026-11-18): one build, the subject's narrow rows, land-graph, pins, the derived index, and evidence-diff. It does not run this ConPTY class. The fixed merge subset is the timing, model, contract, checkpoint, and land-notification rows, not group-71.

No investigation since 2026-11-15 names this assertion. `docs/investigations/2026-11-16-cards-1912-1913-reconciliation.md` already lists the class as NEEDS-WINDOWS. A 2026-10-25 Linux slice (`docs/investigations/2026-10-25-db2c2088-windows-pty-argv-debug.md`) recorded 6 passed / 16 skipped here and told the Windows owner to separate a pre-write guard hold from a lost body. The 2026-10-26 timing sample for run 8f055f87 notes nine early failures in the class and does not name this assertion. The guarded-input behavior the test depends on landed with CARD-0680 (`4de01c9a50ceb5874294b70f79deaa718b57b908`, 2026-10-23), before the test's last edit.

## Classification

Windows-only. The product and the test on this rc tip are the same bytes as master. Linux cannot execute the failing arm, so there is no red witness to repair here. A Windows proof is owed before any change.

## Windows proof the repair needs

Rerun only `C475_QueueCommitAndTransportRecovery(attempt-before-write)` on Windows and keep `PTY_DIAGNOSTIC`, whether `WriteReached` completed, and the row's status, attempts, baseline, and verdict at the assertion. If the row is still Pending, or the verdict is `ModalBlocked` / observation-unknown, the enqueue returned before the cut: arm the block only after the dialog is Clear, and fail on the `WriteReached` timeout itself. If the row is Sent and the task faulted with a type `InputDeliveryUncertainException.Is` accepts, CARD-1892 swallowed it; the injected `InvalidOperationException` still has to escape through `FailRevert`. `C1022_Incomplete_or_stale_receipts_do_not_confirm` uses the same finally and should be read with it. Do not attribute this to the 2026-11-21 Codex pointer commits.
