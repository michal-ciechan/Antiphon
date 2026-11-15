# Why missed: CARD-1935 C699 recovery and caller queue-lock contention

## Outcome and attribution

Test-fixture defect, inherited on current master `08cc3dcac230658c9bb7f580d32ad91906665fbd`.
The targeted error really requests an immediate fallback sweep. No product repair, assertion
removal, timeout increase, clock advance or change to the fifteen-minute interval is needed.

The C699 witness allowed the caller to be idle while testing recovery of two completion notes
for that same caller. Its hosted service runs recovery and delivery concurrently. Delivering the
first recovered note can hold the caller queue lock beyond the recovery pass's one-second wait.
CARD-1709 deliberately leaves the second task unchanged and schedules a five-second retry.
The fixture freezes its RecoveryClock, so that retry cannot run, even while its ten-second
wall-clock poll keeps waiting. This is delivery interference with a recovery/stamping witness,
not a missing error-triggered sweep and not SQL fault-predicate drift.

The repair publishes Working through StoreAwareTranscript/SessionStateStore before starting
the worker, matching C699_Startup_pages_past_a_full_batch_without_skipping_owed_tasks in the
same file. Delivery then stays at the caller's working boundary. All existing assertions and
ten-second bounds remain. The poll additionally covers the failure count and both exact queue
counts before returning, so the later assertions cannot race unfinished work. Delivery and busy
retry behavior have their separate witnesses; this test still requires both tasks' stamps and
one row each without advancing fake time.

## Introducing commit/card

The witness was introduced by `a16d82d72d083ba3e62960b8f9f3e7b036af07e9` (CARD-0699).
The mechanism exposing its idle-caller assumption is
`e5b680ff059d1fa6d87b22fe45231926c048eaff` (CARD-1709, cherry-picked from
`7fdf784b8299dfa73c0d0f18f6eb8786037cd90c`): one-second per-caller queue-lock wait,
unchanged busy work and a five-second retry. This is source-mechanism attribution, not a
bisected claim that this commit was the first bad revision. The successful historical G1
below is preserved; the race depends on delivery scheduling.

## Approving reviews

Review `459edfef`, round 1 of CARD-1709, reviewed
`7fdf784b8299dfa73c0d0f18f6eb8786037cd90c`. It explicitly reported
`ordinaryScopeCompleted: Interim` and `reviewedSourceClean: false`, because it used direct
leased runs without checkpoint-tool receipts. It did not claim a Full gate.
Its retained report is `.antiphon/task-459edfef.md` at
`86ccf5602e9122d45185cacadfbfa1e49bc74bad`.

Actual named selections/results: C1709_* 4/4; G1 13 service classes 365/365; scanner A
10 classes 251/251; scanner B 9 classes 157/157. Code e056f0ff's retained report at
`ffa1546a23b3a0ca2a176cd3d35dda5657a71d1b` enumerates G1:
TaskSupervisionReceiptTests, PostLandMutationDeliveryTests, CompletionNoteWorkQueueTests,
CompletionNoteDurableSweepTests, OutputDistillationDeliveryTests,
OutputDistillationProducerTests, OutputDistillationCleanupTests, RunnerAlarmDeliveryTests,
StaleWorkingTranscriptWatchdogTests, DataRetentionServiceTests, CheckCompactionCrashTests,
CheckCompactionWorkerLifetimeTests and CheckCompactionFixtureTests.
The historical report does not retain literal G1 tree-filter text, so none is invented here.
The original CARD-0699 approving Review was not identified in the evidence inspected.

## Outside the selection

Integration lane/category; path
`tests/Antiphon.Tests/Application/PostLandMutationDeliveryTests.CompletionRecovery.cs`.
This caller was **inside** CARD-1709's reported G1 class selection and passed historically;
its uncontrolled idle delivery boundary allowed the scheduling-dependent miss. A selected
class is insufficient when the fixture leaves a competing clock/lock lifecycle uncontrolled.

The later CARD-1892 explicit checkpoint caller census omitted this broader completion-recovery
family, as measurement task 15406ecc records in
`docs/investigations/2026-11-14-card-1892-stale-test-sweep.md` on
`origin/feat/card-task-15406ecc`. That task observed B043 7/8 and isolated R007 0/1; it did
not establish a CARD-1892 causal relationship. Triage 593c280c correctly kept attribution open.

## Cheapest pre-land check

Proposed CARD-1782 impacted-class mapping (this note does not change selector inputs):

`server/Infrastructure/Orchestration/CompletionNoteWorkHostedService.cs` ->
`/*/*/PostLandMutationDeliveryTests/*`

For a first diagnostic, the cheapest exact selection is
`/*/*/PostLandMutationDeliveryTests*/C699_Recovery_error_sweeps_other_missed_events_without_waiting_fifteen_minutes*`.
Run the whole class once after changing its fixture. A passing delivery or busy-retry neighbor
alone does not establish this caller's recovery/stamp boundary.

## Evidence

All runs are serial on server2/Linux, leased through scripts/build-slot.ps1, using native default
outputs with UseAppHost=false. Raw logs, TRX, load samples and run metadata stay ignored in
`/work/worktrees/task-2b6dde13/.antiphon/c1935/`. No live Windows run, service restart or host
setting was touched. The brief's standing authority covered this test-only repair; no approval
pause was needed.

| Row | Committed source | Outcome | Wall / slot wait / held seconds | /proc/loadavg start | /proc/loadavg end |
|---|---|---|---|---|---|
| base-build | 08cc3dcac230658c9bb7f580d32ad91906665fbd | build exit 0, 0 errors | 476.433 / 0 / 475 | 6.27 3.52 3.20 10/4202 7864 | 31.02 24.54 13.67 36/4389 9688 |
| base-red | same unchanged master | 1 executed, 0 passed, 1 failed, 0 skipped | 370.545 / 300 / 68 | 18.73 24.09 15.20 14/4230 15544 | 18.35 24.94 19.06 3/3999 14699 |
| diag-build | 211f7eb4e5b9e46aa691ad6f2d609b07ed74c444 | build exit 0, 0 errors | 307.439 / 0 / 306 | 11.84 21.81 18.34 5/3963 30270 | 47.40 34.40 24.42 34/4346 12692 |
| diag-red | same diagnostic commit | 1 executed, 0 passed, 1 failed, 0 skipped | 133.520 / 60 / 72 | 44.95 34.31 24.50 7/4416 13061 | 21.85 30.13 24.30 10/4223 15866 |

Both reds used the exact diagnostic selection above and failed with
`TimeoutException: one targeted failure must sweep both the requested task and an unrelated missed event`.
Baseline MTP duration was 63.636 seconds; diagnostic MTP duration was 64.931 seconds.
Diagnostics were committed and pushed before their build/run; the final fixture delta removes
that temporary instrumentation. The diagnostic commit's retained output proves:

```text
C1935 faultHits=1 armed=False timers=00:15:00,00:15:00,00:00:05
C1935 task=requested status=Succeeded stamped=True rows=1 completionObligations=0
C1935 task=missed status=Succeeded stamped=False rows=0 completionObligations=0
C1935 worker=Completion note recovery failed; requesting a backstop sweep exception=owned completion recovery read failure
```

The captured SQL includes the targeted eligible-task SELECT with `AND a."Id" = @__id_0`, then
the same bounded eligible-task SELECT without that ID predicate (the immediate fallback sweep),
then a targeted reread. The SourceLandingOperationId IS NOT NULL fault predicate still matches.
The five-second timer is specifically selected by the busy-session branch, not the generic
error backoff. Neither task had a completion obligation: duplicate-obligation suppression is
excluded. The requested task remains Succeeded and stamped; the unrelated task remains
Succeeded/eligible and unqueued. No second injected or worker-reported recovery failure occurred.

The baseline phase recorder lacked its required Attempt field, so its phase breakdown is unknown;
its TRX and load/slot metadata remain valid. The diagnostic run records per-test phases under
c1935/phases. Database cache readiness was 8.890 seconds in baseline and 6.217 seconds in the
diagnostic. Uninstrumented phases and any unattributed remainder stay unknown. These are
contended small samples, not a measured p95 or justification to change any timeout.

An initial logging-wrapper attempt was stopped before it had a gate child/lease or test process;
it supplied zero test evidence. Literal ProcessStartInfo.ArgumentList forwarding replaced that
wrapper. It is ignored local evidence infrastructure, not a repository repair.

Mutation checks are operator-waived (2026-11-12); the separate proof obligations remain pending.

### Completed post-fix verification

All rows below bind the repaired source
`454c77abb7f08d3151dcfe669cb7331d373a33ce`. Subsequent changes only complete this note.
No production file changed. The impact resolver selected the two owned-child
PostLandMutationDeliveryWorkerTests in addition to the caller class and lane guard;
that is the reason for including those two cases in the class pack.

| Row | Outcome | Wall / slot wait / held seconds | /proc/loadavg start | /proc/loadavg end |
|---|---|---|---|---|
| fix-build | build exit 0, 0 errors | 398.079 / 120 / 276 | 17.40 26.01 23.35 19/4386 5005 | 39.27 32.02 26.86 34/4199 14504 |
| class-green | 56 executed, 56 passed, 0 failed, 0 skipped | 158.490 / 30 / 128 | 31.46 30.64 26.49 7/4210 15289 | 19.30 26.18 25.44 33/3973 14072 |
| guards | 114 executed, 113 passed, 1 failed, 1 skipped | 328.301 / 60 / 266 | 16.72 25.40 25.19 10/4062 14673 | 11.23 17.34 21.68 5/3939 1906 |
| driver-build | standalone driver build exit 0, 0 warnings/errors | 23.771 / 15 / 7 | 21.73 19.25 22.11 8/3924 3917 | 19.24 18.86 21.92 10/4009 5544 |
| guard-driver-green | 1 executed, 1 passed, 0 failed, 0 skipped | 45.554 / 30 / 14 | 16.55 18.27 21.68 8/4029 6952 | 15.54 17.89 21.40 11/4061 10926 |

The class pack was `/*/*/(PostLandMutationDeliveryTests*)|(PostLandMutationDeliveryWorkerTests*)/*`:
54/54 caller cases and 2/2 worker cases. MTP duration was 124.926 seconds. The exact repaired
C699 method passed in 1.4899276 seconds within that pack, retaining the ten-second bound.

The full guard union was:

```text
/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(DerivedImpactIndexTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(WindowsUnitPlatformAdmissionTests*)/*
```

The initial guard failure was my omitted build prerequisite, not a product or inherited source
defect: DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged invokes the
standalone driver with -NoBuild, and the driver DLL did not exist. Antiphon.Tests' project build
does not build that standalone solution project. The other 26 derived-index cases passed,
including the committed-index byte comparison. After a separately leased native build of
`scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj`, only the failed
method was rerun using
`/*/*/DerivedImpactIndexTests*/regenerate_script_reports_changed_then_unchanged`.
It passed both scratch-fixture regenerations, including the second `unchanged` assertion.
No test, product code or committed index bytes were changed to repair that prerequisite.
MTP durations were 263.390 seconds for the union and 10.368 seconds for the exact follow-up.
The original failing guard receipt remains failed; there was no second full-union run.

| Guard class | Final distinct passed | Failed after the exact prerequisite repair | Skipped |
|---|---:|---:|---:|
| TestClassificationPolicyTests | 42 | 0 | 0 |
| TestClassificationGuardTests | 1 | 0 | 0 |
| SlowTestRegistryMergeTests | 5 | 0 | 0 |
| SlowTestTripwireTests | 2 | 0 | 0 |
| NativeLinuxPolicyWitnessTests | 3 | 0 | 0 |
| DerivedImpactIndexTests | 27 | 0 | 0 |
| MechanicalPcContractTests | 11 | 0 | 0 |
| TaskPlatformGuidanceTests | 5 | 0 | 0 |
| C1729LayeringGuardTests | 11 | 0 | 0 |
| TestLaneCategoryGuardTests | 1 | 0 | 0 |
| WindowsUnitPlatformAdmissionTests | 6 | 0 | 1 |

Layering printed `VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3`.
The skip is C1210_Windows_linux_only_outcomes_are_exact: its real Windows child-roster audit
is not portable evidence and remains owed to Windows qualification. No Windows run was touched.

The pins were verified unchanged: policyHash
`bd6689bbc7ea752f13a69f872ea86a171fc8afc94e90ef9ed0c8d53aab00fbaa`;
scripts/test-live-lane-run.ps1 -PrintInputDigest returned
`070479b6da9902cc6cbd2b791baf855f01333599f6041cc017c45a8fe0cf9c75`.
No protected input changed, so no re-pin was needed.

Phase detail is partial, not a fabricated complete wall-time allocation. The diagnostic test
envelope was 31.049 seconds: exclusive recorded DB 0.028 seconds, process 5.155 seconds,
unknown remainder 25.866 seconds. Its shared database-create span was 4.439 seconds and
runtime-model setup was 4.612 seconds. The green pack recorded 56 complete cases with summed
envelopes 437.807 seconds (parallel and distinct from 124.926-second MTP wall), exclusive DB
4.584 seconds, process 110.164 seconds and remainder 323.059 seconds. Shared database creation
was 5.714 seconds and runtime-model setup 10.729 seconds. Two owned worker headers contain
zero TUnit cases and add no execution credit. The guard journal has 115 terminal records,
including its one skip, with 401.262 seconds of summed envelopes and 398.256 seconds of
unattributed remainder. The follow-up case envelope was 5.865 seconds, entirely unattributed.
Do not treat these small, contended samples or parallel sums as per-method p95 measurements.

TRX SHA-256 receipts (raw files stay ignored):

| Row | SHA-256 |
|---|---|
| base-red | 1561170ade9418e461dd0713b6f0b22bc539035a1bf9d4241d138746116a46a2 |
| diag-red | de0bcadcb1901bdb23998f65109446ef5af4761ce34aa771c3ae1bc2def69155 |
| class-green | b50f0a7a415ce90d1a988d91ecc5724ad9b2e5979fe607c3462b5a8fad2d7159 |
| guards | 70310e222fbe3e87575dab05e0a24c74e974d77b9f2bcefe159db4e9d89dadfb |
| guard-driver-green | 56e799ddecf5d93790a8f6e216e04f4fac492e4a39c6c0f181dd221e1eb3bc9f |

## Exemption

None. First note for this queue-lock/frozen-clock caller-fixture cause.
