# CARD-1616 round 2

Fixed the admitted-build deadline receipt regression. The deadline witness went red for the exact reported state mismatch, then passed with null counts, run/row exit 5 and validate exit 2. Required narrow Linux verification is complete; native Windows timing remains owed.

## Change and cancellation finding

`tools/Antiphon.Checkpoints/Execution/RunScheduler.cs` now gives a non-null terminal reason precedence over slot classification, except for `slot-admission-deadline-exceeded`, which retains `slot-timeout`. The production diff is three inserted/two removed lines. Admission timeout remains explicitly nonexecuted with zero counts, covered by the existing C1616 witness. No definition files, timeout, retry, cleanup decision or launch-reduction logic changed.

`CheckpointAdmissionContractTests` adds two witnesses through the real `CheckpointApp.ExecuteAsync`, receipt reader and CLI validate. The build-deadline case asserts `build-deadline-exceeded`, null execution/pass/fail/skip counts, exit 5, readable RED receipt, `report_failed` and validate exit 2. Both witnesses assert all four `FINALIZATION phase=` lines.

The cancellation catch does set `AdmissionExitCode=5` after admission, as the reviewer observed. A real execution-budget cancellation, however, reaches the scheduler's earlier cancellation branch before failed-build classification: its row is `skipped`, with null counts; the build retains `execution-budget-exceeded`. The second witness expires the manual clock after the build driver starts, reaches that catch, verifies the build reason, drains the driver and validates refusal. It does not claim that this normal cancellation path previously produced `slot-refused`.

## Evidence and outcomes

All build/test drivers used `scripts/build-slot.ps1`, `TEMP=/tmp`, `OutputPath=bin-c1616-r2/`, and `UseAppHost=false`. All owned drivers exited and released their leases. Raw logs, fresh TRX/HTML, per-test outcome/duration JSON and expanded class counts remain ignored under `/work/worktrees/task-7b6b8ccb/.antiphon/evidence-7b6b8ccb/`.

| Source SHA | Selection | Result |
|---|---|---|
| `4a691c93d7288244a93185cd6a94601281086826` | `/*/*/CheckpointAdmissionContractTests/C1616_admitted_build_deadline_preserves_terminal_receipt` | 1 failed, 0 skipped: expected `build-deadline-exceeded`, actual `slot-refused`; host exit 2; 3.401s |
| `409931c6debaf481733aecd95adf197ddfa1220e` | Ten classes below | 125 total: 119 passed, 2 failed, 4 existing Windows-only skips; 427.788s |
| `b575d4099af323adb402d045f8573d2493606c34` | `/*/*/CheckpointAdmissionContractTests/(C1616_admitted_build_deadline_preserves_terminal_receipt*)|(C1616_admitted_build_budget_cancellation_is_not_slot_refusal*)` | 2 passed, 0 failed/skipped; host exit 0; 4.495s |

The two failures in the ten-class batch were introduced witness mistakes after the scheduler assertions had passed: validate writes directly to stderr, not `Runtime.Output`, and default red-output retention supplies `cleanup_refused` before `report_failed`. The final witness correction keeps its scratch outputs explicitly, asserts the production validator reason directly, and still invokes CLI validate and requires exit 2. No production assertion was loosened. After this test-only correction, only the two affected methods were rerun. Combined final coverage is 121 passed and four existing skips across 125 distinct cases, not a claim of one all-green ten-class invocation.

Exact combined class filter:

```text
/*/*/(CheckpointAdmissionContractTests*)|(CheckpointAdmissionBudgetTests*)|(CheckpointSlotExecutorTests*)|(CheckpointSelectedOutputTests*)|(CheckpointReceiptSafetyTests*)|(OutputCleanupTests*)|(ReportWriterTests*)|(RunSchedulerTests*)|(CheckpointSourceExecutionTests*)|(HostCleanupCheckpointExtractionTests*)/*
```

The fresh TRX contains exactly those ten classes. Expanded counts: admission contract 7 (5 passed, 2 corrected above), admission budget 9, slot executor 13, selected output 3, receipt safety 8, output cleanup 49 (45 passed, 4 skipped), report writer 6, scheduler 18, source execution 9, host-cleanup extraction 3. All non-contract failures are zero. The skips are the same CARD-1310/1318 Windows custody tests as the review.

Build elapsed times were 269.83s (red), 671.28s (production fix), and 330.59s (test-only correction using `--no-dependencies --no-restore`). All builds exited 0 with zero errors. Slot waits were respectively 30s, 0s, 255s; test waits were 0s, 90s, 90s. Per-test durations are retained in TRX/JSON; unavailable fixture/external-call span breakdown remains unknown. This work exceeded the requested 40-minute target because of queue waits, the slower second build and the additional witness correction; it was supervised through completion rather than left running.

## Graph, integration and remaining limits

The real gated command `scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin-c1616-r2/Antiphon.LandGraphAudit.dll` exited 0 at `409931c6debaf481733aecd95adf197ddfa1220e`: digest `7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99`, count 68, both evaluation passes and canary approved. Admission 361s, held 46s. No `-Update`; all definition files remain unchanged in the later test-only correction.

Fetched master is `698bf1b73e8abf449d5569ea74ac8c82e80f92a1`; rc is `e4c9547e10e3da59fd20684b49c029cdc6063fa8`. `git merge-tree --write-tree origin/rc/2026-10-27 HEAD` is clean. The master merge has twelve conflicts; comparing the untouched starting tip reproduces the identical twelve conflict messages. No merge was performed. Carry the small round-2 delta over the already reviewed change to rc through the caller's integration path.

The full Unit lane was not commissioned or run; this does not upgrade the earlier Interim review to Full or claim a source-clean checkpoint receipt. Native Windows wall-time attribution of the historical 814 seconds stays **OWED**, using the unchanged command/manifest in `.antiphon/task-185d0094.md`, section “Owed native Windows row”. No native tests, tags, publication, rc rebase or force push occurred.

Removed all 35 inventoried `bin-c1616-r2` directories by individually specified literal paths after the last driver exited. No outputs outside this checkout were deleted. The force-removal command was automatically rejected; non-forced removal of the same inspected literal paths succeeded. Inventory remains in `output-inventory.txt`. Evidence history and whitespace checks pass; the final report-only commit is checked again at settlement.

## Pushed slices

- `72927139d1dc20d327057b4c867523bcd0b7409d`: deadline and cancellation witnesses.
- `4a691c93d7288244a93185cd6a94601281086826`: correct emitted finalization phase name, before the red run.
- `409931c6debaf481733aecd95adf197ddfa1220e`: minimal scheduler fix.
- `b575d4099af323adb402d045f8573d2493606c34`: correct witness validation setup; final two-method green.

--- next stage ---
next: merge to rc
handoff: Tiny scheduler diff over the reviewed CARD-1616 change; narrow Linux verification complete, Windows timing owed; master conflicts inherited.
artifact: /work/worktrees/task-7b6b8ccb/.antiphon/task-7b6b8ccb.md
