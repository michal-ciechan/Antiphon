# CARD-1722 item 3: publish the receipt before build readiness

Original Code/landing owner: edbaee26-2ba5-4999-aa4d-17a5b60a7480.
Base: b322d430d8e1de9b8039526c70f7edd056b71b90. Restart: none.

S1 commits a deterministic regression and an internal asynchronous receipt-write gate,
with the original readiness ordering retained. S2 changes the ordering only: keep
readiness building through receipt writing/sealing, mark a refused receipt failed,
then publish ok. No timeout or assertion is relaxed. No project graph change.

## Verification design

V-1: Hold bin-b immediately before its real CheckpointBuildBinding.Write. Keep
bin-a's row running; an independent sentinel command proves that the scheduler
has scanned the pending bin-b row. At base that row attempts validation and
ends build_source_mismatch. After repair it waits, then runs green with a verified
receipt. Both owned gates release and the scheduler joins in finally.
R-1: Full RunSchedulerTests and CheckpointCallerBudgetTests preserve exclusive,
parallel row, one-build-lane, cancellation, confinement and deadline behavior.
R-2: Full classification policy/guard and slow registry/tripwire classes.
R-3: Whole Unit lane (Final profile). Unit omits delivery, landing, leases and
persistence. The changed production code is the standalone checkpoint scheduler;
R-1 is its full affected-class coverage. No full assembly run is planned.
R-4: Original builds_never_overlap_even_with_free_slots_and_width_two runs in R-1.
The brief requests twenty executions, but the stage repeat-proof ceiling forbids
that without a demonstrated Review flake. Investigation ff3ef62e is not Review.
Retain the request as an explicit unfulfilled repetition request; no extra repeats
after green. Mutation owns deliberate reorder/drop mutants and restore/green.

PC-1 pending SourceLanding Mutation: move ok before BeforeBuildReceiptWrite/Write;
exact rows_wait_for_the_build_source_receipt_before_readiness must fail with
build_source_mismatch. PC-2 pending: omit Write but publish ok; the same exact
method must fail. All variants remain pending; no Code mutant runs.

## Callers and readiness readers

| Reader | Role / consequence |
|---|---|
| RunScheduler.RunAsync readyExclusive | ok permits an exclusive row to claim the idle lane |
| RunScheduler.Pick | pending/building/failed prevent rows; ok admits concurrent rows even while BuildOneAsync is in flight |
| RunScheduler.RunAsync nextBuild / BuildFailed / SkipPendingBuilds | pending starts the build; failed terminalizes dependent rows; pending may become skipped |
| RunScheduler.BuildOneAsync | terminal state chooses source provenance and is later published |
| RunScheduler.RunRowAsync -> RowRunner.RunAsync | receives ok/reused label; strict binding.Check refuses absent/mismatched receipt before driver launch |
| CheckpointApp.BuildReport | snapshots BuildProgress into ReportBuild; reports timing/source/state |
| RunState.Heartbeat and checkpoint state publication | UI/diagnostic readers report building/pending and serialize state; do not admit work |

CheckpointBuildBinding.Write performs SafeWrite publication and OwnedOutputLedger.Seal.
Success of both must precede readiness. Readiness is an in-memory shared field; the
scheduler may scan it when another row completes, before the build task finishes.

## Performance budget

ff3ef62e measured full scheduler classes at 9.9-11.4 seconds, target max 0.64 seconds
under load. The new test uses existing 10-second gate safety guards, no sleeps.
Gate wait/body/teardown occur once per test with independent temp roots. No new
host/container setup. TRX records case timing; finer internal IO phases are unknown.
Cold Antiphon.Tests build provisional 15 minutes, named classes 5 minutes, Unit
35-minute collection limit (unmeasured here; CARD-1514 tracks slow-lane diagnosis).
Any selection over 30 minutes requires Debug; keep its outcome incomplete.
Build and admission waits are separate (broker default 45 minutes per acquisition).
Bootstrap checkpoint-tool build is an explicitly additional gated build per source
revision, native output, UseAppHost=false; tool manages each row/build slot itself.
Unit invariant: scheduler ordering repair preserves all existing Unit contracts;
unbounded-by-class selection is exactly Category=Unit, no full assembly.

OS audit: no OS-selected behavior changes. Existing _platform.IsWindows property
selection in BuildOneAsync is unchanged; receipt ordering is portable. No optional
configuration, production factory, startup paths or rc startup behavior changes.
No desktop execution or native Windows restart is authorized. Restart: none.

### Checkpoints

totalTimeoutMinutes: 90

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-card1722-receipt-red/` | receipt-witness | `/*/*/RunSchedulerTests*/rows_wait_for_the_build_source_receipt_before_readiness` | V-1 | all listed, 0 failed | 1 | 10 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-card1722-receipt-final/` | affected-and-guards | `/*/*/(RunSchedulerTests*)\|(CheckpointCallerBudgetTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)/*` | V-1, R-1, R-2, R-4 | all listed, 0 failed | 40 | 10 |
| CP-3 | S2 | CP-2 | unit | `/*/*/*/*[Category=Unit]` | R-3 | >= 1 executed, 0 failed | 1 | 20 |
