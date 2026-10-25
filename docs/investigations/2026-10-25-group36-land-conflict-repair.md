# Group 36 fixture compatibility repair — 5b5af094

Combined group 36's guarded spill delivery repair with the landed Codex fixture behavior. The complete selected Linux classes pass: 47 passed, zero failed or skipped. The fetched-master merge-tree check is clean. No landing or activation is claimed.

## Source and ownership

- Code/landing owner: `5b5af094-4ca7-47e7-b57f-44505a478560`; predecessor Debug owner: `afef8c76`.
- Assigned branch: `feat/card-task-5b5af094`.
- Worktree: `/work/worktrees/task-5b5af094`.
- Task base: `9182f666e698b4ed7fd891008ccd89b87e65d6e1`.
- Committed, pushed and actually built/tested source: `9ca36b0e60760325dbaf32738612b5627fa7c392`.
- Repair brief: `.antiphon/inbox/21892545-423c-40d0-996a-9c1233e9021a.md`.
- Historical group 36 report: `docs/investigations/2026-10-25-group36-debug.md`.

The initial fetched master was `a0b51f75fc93b768c6b3bfce883e3378660c53c9`. The starting tip already produced a clean merge-tree, `37f4db2bb4257e5206371900ef1ce1874cc1364e`; its earlier store-binding alignment had removed the tip-level overlap. This does not establish that landing's replay of intermediate commits is conflict-free.

The standing Code contract prohibits merging the target into the task branch. Therefore no target merge, rebase, reset, amend or force push was performed. Instead, one normal compatibility commit imports only the relevant already-landed behavior from the combined tree. Actual landing remains the caller's operation; this report does not claim the previously refused rebase was executed successfully.

## Combined behavior

Four files changed in the repair commit:

- `tests/Antiphon.Tests/Agents/FakeAgentProtocolAdapter.cs`: retain group 36's fake behavior and add the landed opt-in Codex composer/footer rendering.
- `tests/Antiphon.Tests/Application/CodexCliObservationTests.cs`: use the landed state-aware transcript writes and `MarkIdleAsync`, including model/runner and warm-session observation cases.
- `tests/Antiphon.Tests/Application/CodexCliRemoteDeliveryFixture.cs`: prefer the existing `World.DispatchAsync` helper, enable the shared Codex chrome and retain all four pre-input refund metadata assertions. Group 36's composer snapshot, conditional Claude transport, clock and durable-recovery behavior remain intact.
- `server/Application/Services/SessionMessageQueueService.cs`: carry the already-landed, narrowly guarded first-attempt Codex pre-input spill-refusal refund needed by those assertions. This file equals fetched master at verification; it is not a new production design.

`CodexCliDeliveryWorld.cs` already includes both sides' store binding and required group 36 clock/conditional snapshot behavior, so it needs no further edit. Group 36's two production guarded-spill files are unchanged from the task base. No assertion was removed or loosened; no timeout was changed. No new test method or mutant was authored.

Symbol census used `rg -l --no-ignore 'CodexCliRemoteDeliveryFixture' tests/Antiphon.Tests -g '*.cs'`: the actual test-class consumers are `CodexCliObservationTests` and `CodexCliObservationGapTests`; the other hits are the two partial fixture files. There is no additional group 38 class using this fixture. The two added safety selections below protect the retained transport fix and the imported refund guard.

## Verification

The repair brief explicitly commissions one slot-gated build and direct `--no-build` execution, excluding the Unit lane. The test DLL is 68,547,072 bytes, exceeding the checkpoint inventory reader's 64 MiB limit. Accordingly no checkpoint tool/`run-checkpoint.ps1` execution, validated checkpoint receipt or CHECKPOINT line is claimed. There is no repair-specific CP-n/V-n/R-n manifest; the source plans below are historical context, not a claim that all their qualification rows ran.

| Full class | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| CodexCliObservationGapTests | 7 | 7 | 0 | 0 |
| CodexCliObservationTests | 8 | 8 | 0 | 0 |
| PhoneHomeSpillTransportTests | 5 | 5 | 0 | 0 |
| SessionMessageQueueCodexPointerTests | 27 | 27 | 0 | 0 |

Fresh `results/verify.trx` was inspected through its TestDefinitions and UnitTestResult identities. The inspector checks every attributed source test method in all four classes, exact per-class counts, freshness, all 47 outcomes and zero non-passing results. Parameterized methods retain their separate case rows. `case-roster.csv` and `trx-summary.json` contain the full executed roster. No red row remains and no repetition was needed.

This covers the existing observation class's V-21 delivery, V-22 retry/cancellation and V-23/24/25/26 failure/stale/unknown/old-version cases, plus the seven C1029 gap methods. Their actual ordinary outcomes are passing. Broader proposed C1029 vectors, other V/R rows and native qualification are not newly qualified by this repair. No additional manual acceptance was commissioned.

Exact commands:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 5b5af094-combined-build -SlotWaitMinutes 5 -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-5b5af094/ --property:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 5b5af094-combined-verify -SlotWaitMinutes 5 -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-5b5af094/ --property:UseAppHost=false -- --treenode-filter '/*/*/(CodexCliObservationGapTests*)|(CodexCliObservationTests*)|(PhoneHomeSpillTransportTests*)|(SessionMessageQueueCodexPointerTests*)/*' --report-trx --report-trx-filename verify.trx --results-directory .antiphon/evidence-5b5af094/results
```

The ignored verification wrapper checks expected SHA `9ca36b0e60760325dbaf32738612b5627fa7c392`, tracked cleanliness and DLL identity before/after execution. These remain unchanged. Build: exit 0, zero errors, 1,197 warnings, MSBuild elapsed 3m13.79s. Test: exit 0, TUnit elapsed 11m22.883s. Test-driver wall including admission was 15m12.641s. There were no other build/test invocations, no full assembly run and no Unit lane.

Unedited slot receipt lines:

```text
BUILD SLOT granted lease=14b0d111-7bf6-4fe2-80f0-ebeb4fdd9a9e waited=30s maxcpucount=6
BUILD SLOT released lease=14b0d111-7bf6-4fe2-80f0-ebeb4fdd9a9e held=194s
BUILD SLOT granted lease=f0d3645d-b6ea-4f82-bc72-188756ec0ee9 waited=225s maxcpucount=6
BUILD SLOT released lease=f0d3645d-b6ea-4f82-bc72-188756ec0ee9 held=687s
```

Thus build `slot=14b0d111-7bf6-4fe2-80f0-ebeb4fdd9a9e waited=30s`; test `slot=f0d3645d-b6ea-4f82-bc72-188756ec0ee9 waited=225s`. Both acquisitions stayed within five minutes. No unleased execution occurred.

Evidence root: `/work/worktrees/task-5b5af094/.antiphon/evidence-5b5af094/`. Generated JSON/TRX/logs/phase records stay ignored. The build's attempted Tee-Object did not capture inherited native/host output; `build-summary.txt` explicitly preserves only copied receipt/summary lines, not a fabricated complete build log. Build start/end, exit and source are in `build.json`; test metadata and complete console output are in `verify.json` and `verify.log`.

SHA-256 provenance:

- Test DLL before/after: `201665fb6a3e0296a2f18d60459be3b05b4f4ab4188f06adefccd5917cff0919`.
- Fresh TRX: `849a6532ee81ee9f7b63241a790e22b5a81425888af96f749ad1a9829bb48063`.

Phase recording was enabled for run `5b5af094-final`, group `combined-codex-fixture`, attempt 1. Assembly database create/migrate spans are 6.305s/32.927s. Per-case records retain clone and body phases where emitted. Uninstrumented Docker/readiness, individual filesystem/network/CLI and teardown costs remain unknown/remainder, not zero. This is one provisional shared-host sample, not measured p95. No selection exceeded 30 minutes. The 33 inventoried producer-owned `bin-5b5af094` outputs were removed after the driver exited; no owned build/test remains running.

## Pending mutation and qualification

All inherited PC IDs and every ledger variant remain pending for method-scoped SourceLanding Mutation. None is discharged by these ordinary green results. The inherited set is `PC-1..14,16..84,89..109,111..120,122..127,153..159,184..188,190..192,194,199..203,205..255`, plus separate `PC-256` (193 IDs). In particular the affected PC-191/192/218/219/220/246 variants remain pending. Deliberate red/restore/green and missing-control discovery belong to Mutation; no bound mutation pack was created here.

Source plan/evidence references:

- `docs/superpowers/plans/2026-10-03-card-0959-runner-codex-version-plan.md`.
- `docs/superpowers/plans/2026-10-04-card-1029-inert-observation-verification-gaps-plan.md`.
- `docs/superpowers/plans/2026-10-04-card-1029-control-qualification.md` (full PC/variant ledger).
- `docs/investigations/2026-10-25-group36-debug.md` (historical independent red/green witness for guarded spill delivery).

The native rerun obligations in the predecessor report remain pending. Linux green is not Windows qualification. Runner defaults and session runner platform/eligibility were read from their API routes; no runner/platform pin or fleet location was embedded. Activation of the inherited guarded-spill production fix needs server and runner binaries; no restart or deployment was performed.

## Publication and next stage

At tested source `9ca36b0e60760325dbaf32738612b5627fa7c392`, a fresh fetch resolved master to `d35b37a01c4f1f3027ee53cc3f2b7d1106fcc322`; merge-tree exited 0 with tree `7e6931603bc177bfdeece44be4876f4f0461f54b`. The initial full task-range evidence check reports one commit and zero violations. The final report-only commit receives a fresh full-range history check, fetch, merge-tree and remote-tip check; their exact final values belong in the completion report and ignored final receipt.

The standing Code contract requires ordinary Review after implementation and ordinary verification, including zero-new-PC repairs; it therefore takes precedence over the brief's requested `next: land`. Review this task's exact pushed tip, then the caller lands this original Code owner and commissions SourceLanding Mutation. A successful future rebase/publication is not established by a clean merge-tree alone.

--- next stage ---
next: review
handoff: Review the combined fixture source and 47-case Linux green evidence on the exact pushed tip; preserve all inherited PCs/native obligations. Then land original Code owner 5b5af094 and commission method-scoped SourceLanding Mutation. No target merge or landing was performed by Code.
artifact: docs/investigations/2026-10-25-group36-land-conflict-repair.md
