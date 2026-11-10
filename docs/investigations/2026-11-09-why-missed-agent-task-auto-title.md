# Why missed: auto-title diagnose-row poll

One committed note for the rc/2026-11-14 group-86 failure of `AgentTaskAutoTitleTests.a_timeout_cancels_a_queued_run_and_ledgers_DegradedTimeout` (run dce78ee0). CARD-1781.
Owner: [why-missed write-up](../orchestration-loop.md#why-missed-write-up-card-1781).

Run dce78ee0, chunk `antiphon-all-group-86.trx`, checkout `C:\Antiphon\releases\checkout-42be26dd` at rc/2026-11-14 = `7bee43614495293afbae1b0f4add019d11dacb9c`. Host DESKTOP-KTLKPIF, Windows 10.0.19045, win-x64, .NET 9.0.22, TUnit 1.44. Windows read-out: `docs/investigations/2026-11-09-run-231f573c-agent-task-auto-title-windows-failure.md` on `feat/card-task-16acb7aa` at `65c12e8328830f1e0186603453d5ab95ca17d49d` (the file keeps the 231f573c name; the extract is dce78ee0 and does not quote the 231f573c TRX).

## Failure

`Antiphon.Tests.Application.AgentTaskAutoTitleTests.a_timeout_cancels_a_queued_run_and_ledgers_DegradedTimeout`. Duration 7.825s (`00:00:07.8251964`), start `2026-11-10T01:08:54.5853297+00:00`, end `2026-11-10T01:09:02.4105261+00:00`. executionId `da121393-49fa-4c66-acf4-56fc151fedba`.

```
TimeoutException: The diagnose worker never created a run.
```

Thrown at `WaitForDiagnoseRunAsync` (`tests/Antiphon.Tests/Application/AgentTaskAutoTitleTests.cs`, then line 546), called from the test at line 178. Chunk counters: total/executed 636, passed 635, failed 1, timeout platform counter 0, exit code 2. Peak concurrency 28; 58 other results overlapped the window. The class is `[NotInParallel("DiagnoseTitle")]`. The other 21 methods passed, including the same helper immediately before and after (0.126s and 0.137s). `the_unavailable_incident_dedups_per_minute` uses the same `DiagnoseWaitSeconds = 4` and passed at 8.961s. No git, pwsh, or process-spawn text in the failure. `processSpawnSeconds` 0.036.

The helper polled 200 times with `Task.Delay(25)` (about 5.0s of delays, plus query time, 7.825s wall) for a Queued Diagnose row. Product `DiagnoseWaitSeconds = 4` is fake-clock time inside `WaitForRunAsync` and starts only after that insert. The title, Canceled status, and `DegradedTimeout` assertions never ran.

`AgentTaskAutoTitleTests.cs`, `DiagnoseService.cs`, `AgentTaskService.cs`, `Diagnosis.cs`, `DiagnoseProvisioner.cs`, and `SpecialistTaskRunner.cs` are identical at rc/2026-11-13 `653f1145de5b72c2617a018cd606e0e8ba3cfe6e`, rc/2026-11-14 `7bee43614495293afbae1b0f4add019d11dacb9c`, and master `da4f168de5a771adf5dab22336826d19b4ec613b`. rc14 is an ancestor of that master; rc13 is an ancestor of rc14. Run 231f573c's dispatched failures were groups 2, 4, 71, 96, and 117. This class was not one of them. The 231f573c TRX itself was not read from this runner.

Linux at master `da4f168de5a771adf5dab22336826d19b4ec613b`, filter `/*/*/AgentTaskAutoTitleTests/*`, `UseAppHost=false`, native output: one run 22/22 in 30s 632ms, then 20 `--no-build` repeats 22/22 (`REPEAT_DONE ok=20 fail=0`), then one run under eight shell CPU burners 22/22 in 41s 240ms (lease `b3e2d516-dada-46be-afa7-2cde426598b7`, waited 0s). The Windows TRX is the red witness. This is a test timing budget under chunk load, not a product defect and not a commit between the two rc tips.

## Introducing commit/card

introducing commit/card: `9bd7e2e4f05ad289966cf023b328ddcbfa23272e` (CARD-0352)

CARD-0352 is `076e01f6-d7bf-4868-bae1-4317d0290141`, status Done. Commit message `feat(delegation): CARD-0352 S3 auto-title job and Diagnoses ledger` (Mike Ciechan, 2026-09-03). `git blame` still attributes `WaitForDiagnoseRunAsync` lines 530-546 to that commit. The message records the author's selection: `AgentTaskAutoTitleTests 21/21`, `DelegateScriptTitleTests 10/10`, `ProductionRunnerIsolationTests 3/3`. The plan checkpoint at `docs/superpowers/plans/2026-09-03-card-0352-diagnose-agent-plan.md` line 253 is `/*/*/AgentTaskAutoTitleTests/*` with `OutputPath=bin-card0352/`. Line 258 ("Never widen a wait") is the S1 warning against papering over a product `RequestCount == 0` bug. This repair widens only the real-time insert poll. `DiagnoseWaitSeconds` stays 4.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- No stored review. `GET /api/stage-outcomes?cardId=076e01f6-d7bf-4868-bae1-4317d0290141&stage=Review` returned `{"rows":[],"summary":[]}` on 2026-11-10. There is no Full or Interim review task, and no selection, for CARD-0352 in that ledger. The only recorded selection is the S3 commit message and plan line 253 above.
- Commits between `653f1145d` and `7bee43614` do not touch this class, `DiagnoseService`, or `SpecialistTaskRunner`. Their reviews had no reason to select `AgentTaskAutoTitleTests`.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration lane, category `Integration`, path `tests/Antiphon.Tests/Application/AgentTaskAutoTitleTests.cs`.

A Unit filter does not match `[Category("Integration")]`. `tests/impacted-classes.json` has no row naming `AgentTaskAutoTitleTests`, `DiagnoseService`, or the diagnose poll. The author's isolated run was the class at S3 time, quiet, and green (21/21; the class is now 22). A quiet Linux class run still passes (1 + 20), and one CPU-loaded Linux run passed 22/22, so a pre-land Linux filter of this class does not catch the chunk-load stall the Windows rc run saw. The stall is the insert poll expiring before `RunTitleAsync` inserts the Queued row, under a chunk with peak concurrency 28.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/Application/AgentTaskAutoTitleTests.cs` -> `/*/*/AgentTaskAutoTitleTests/*`

Also map `server/Application/Services/SpecialistTaskRunner.cs` and `server/Application/Services/DiagnoseService.cs` to that class filter. The method that failed is `/*/*/AgentTaskAutoTitleTests/a_timeout_cancels_a_queued_run_and_ledgers_DegradedTimeout*`. The helper is shared, so the class is the narrow set.

Proposed CARD-1801 row (not applied; `ImpactedClassesSelectorTests` pins `tests/impacted-classes.json`):

```json
{
  "id": "auto-title-diagnose-row",
  "globs": [
    "tests/Antiphon.Tests/Application/AgentTaskAutoTitleTests.cs",
    "server/Application/Services/SpecialistTaskRunner.cs",
    "server/Application/Services/DiagnoseService.cs"
  ],
  "classes": [
    "AgentTaskAutoTitleTests"
  ]
}
```

No `"windows": "required"`. The cause is load, which a loaded Linux host can in principle hit. This runner's quiet and CPU-loaded runs stayed green; the Windows chunk is what observed the 7.825s expiry. A quiet Linux pre-land run of the filter would not have failed.

## Exemption

same cause as an already written note: none

Distinct from the pty-host wait (`docs/investigations/2026-11-09-why-missed-pty-host-fixture.md`), the C1514 class cap (`docs/investigations/2026-11-08-why-missed-c1514-task-load-window.md`), and the C1584 roster (`docs/investigations/2026-11-09-why-missed-landing-split-budget.md`).
