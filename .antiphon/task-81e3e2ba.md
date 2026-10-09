# CARD-0889 group C Final Review (task 81e3e2ba)

Subject: Code task `331c52a0-c7d0-436f-980e-b6fffb45502d`, branch `feat/card-task-331c52a0` at
`a18cf764a2ff9f40ae97552b0490b4a7e6ed6c37` (two commits over merge base `9e98286fb82a5b46697e6254e518a01f12f153d7`
with `origin/master` `2e7e7e58a01a926fe1f8a7dd4ceb080c5c731d34`). Read-only: the subject branch was not
rebased, amended or touched. Contract: `docs/superpowers/plans/2026-10-09-card-0889-test-design.md`
(`### Checkpoints` CP-8..CP-12, V-1..V-4, R-1..R-3, PC-15..32, PC-55..68, PC-73, PC-74).

Verdict: **clean**. No regression of existing behaviour, no new reachable fail-open, no stub or
vacuous test, no weakened assertion, no false doc or comment sentence. Test-only: the 8 changed files
are all under `tests/Antiphon.Tests/` (`git diff --name-status 9e98286fb HEAD`), 741+/96-.

## Runs (both bound; `ANTIPHON_TASK_TOKEN` was present, never printed; `request.json` ownerTaskId = this task)

Scratch table in the scratchpad: the plan's CP-8..CP-12 rows copied verbatim (diff against the repo
plan rows: identical) plus one stated-reason row CP-17 for check (b), sharing the one isolated build.
Reason for the scratch table: one run, one build, one source certificate for the plan rows and the
caller row together.

Run 1 `20261009-194702-d5fe` (`start --plan <scratch> --after C --serial --expected-source-sha a18cf764a...`),
one build `bin-c889-c/` (129 s), wall 14m30s:

| Row | Filter | Result | testsWall |
|---|---|---|---:|
| CP-8 | `/*/*/AgentTaskLandRecoveryTests*/C448_V15_RealWorkerDeathRecoversDurableBoundaries` | 7 passed, 0 failed, 0 skipped | 200 s |
| CP-9 | `/*/*/PostLandMutationDeliveryTests*/C478_V09a_LandCrashMatrix` | 12 passed, 0 failed, 0 skipped | 77 s |
| CP-10 | `/*/*/WorktreeResidueRecoveryTests*/C459_WorkerDeathAtEveryRetirementHandoff` | 13 passed, 0 failed, 0 skipped | 277 s |
| CP-11 | `/*/*/TestDbFixtureLifecycleTests*/Worker_mode_list_names_every_owned_child_worker` | 1 passed, 0 failed, 0 skipped | 1 s |
| CP-12 | `/*/*/TestDbFixtureLazyInitializationTests*/A_worker_child_exits_before_the_shared_store_warmup` | 8 passed, 0 failed, 0 skipped | 24 s |
| CP-17 | method OR with bare names | executed 0 (my filter defect, see below) | n/a |

Every row: `slot=granted waited=0s dirty=0 source=a18cf764a... sourceState=clean buildSource=verified`;
`unlisted: none`. Run verdict RED exit 3 solely because CP-17 selected zero tests: a method-segment OR
needs a trailing `*` on each name (the plan's own CP-5 note). `validate --evidence <run 1 report.json>
--expected-source-sha a18cf764a... --rows CP-8,CP-9,CP-10,CP-11,CP-12` -> `CHECKPOINT SOURCE VALID rows=5`.
Kept outputs deleted with `clean --run 20261009-194702-d5fe`.

Run 2 `20261009-200234-425a` (`start --plan <scratch> --rows CP-17 --serial --expected-source-sha a18cf764a...`),
build 47 s, wall 2m20s, GREEN exit 0, outputs deleted by the run:

| Row | Filter | Result | testsWall |
|---|---|---|---:|
| CP-17 | `/*/*/PostLandMutationDeliveryTests/(C478_V09a_LandProducerToCaller*)\|(C478_G134_LandAtomic*)\|(C478_G135_LandEnqueue*)\|(C478_G136_LandQueueKey*)\|(C478_G137_LandWakeup*)\|(C478_G138_LandBusy*)\|(C478_G139_LandReceipt*)\|(C478_G144_LandReceiptSave*)` | 8 passed, 0 failed, 0 skipped | 41 s |

`validate ... --rows CP-17` -> `CHECKPOINT SOURCE VALID rows=1`. Unlisted builds: the checkpoint driver
bootstrap only (`scripts/build-slot.ps1 -Label c889-review-tool -- dotnet build tools/Antiphon.Checkpoints
--property:OutputPath=bin-rv889drv/`, slot granted, 6 s), deleted after confinement. No whole-Unit run.
No `bin-*` directory remains under the worktree. Evidence under `.antiphon/checkpoints/<run>/` (gitignored).

## (b) ConfirmLandReceiptAsync callers and results

Grep across both partial files (`PostLandMutationDeliveryTests.cs`, `PostLandMutationDeliveryTests.CompletionRecovery.cs`;
the second has no caller):

- `C478_V09a_LandProducerToCaller` (direct call, busy false): run 2, passed.
- `LandCrashAsync` (five call sites) reached by `C478_V09a_LandCrashMatrix` (12 cases, CP-9, passed) and by
  `C478_G134_LandAtomic`, `C478_G135_LandEnqueue`, `C478_G136_LandQueueKey`, `C478_G137_LandWakeup`,
  `C478_G138_LandBusy`, `C478_G139_LandReceipt`, `C478_G144_LandReceiptSave` (run 2, all passed).
- `PublicationCommitCrashAsync` (one call site) reached from the matrix `publication-commit` cut (CP-9, passed).

No caller broke under the new `QueueMessageId` requirement.

## (a) Changed assertions (merge base -> HEAD)

- C448 `interrupted.Phase.ShouldBe(expected)` -> same value with label `worker-durable-phase`. Same.
- C448 `resumedWorker.ExitCode.ShouldBe(0, stderr)` -> `ShouldBe(0, "worker-resume-exit: " + stderr)`. Same.
- C448 `OperationAsync().ShouldNotBeNull()` -> labelled `worker-parent-fixture-survives`, plus a new root-exists assertion. Same or stricter.
- C459 `DirectoryRemoved.ShouldBe(true)` for three cuts -> `directory-result` labelled `retirement-durable-handoff`, the other two unlabelled, all still `true`. Same.
- `ConfirmLandReceiptAsync`: the old `if (QueueMessageId is null) { State.ShouldBe(RetryPending); return; }` early return skipped every delivery assertion; now `ShouldNotBeNull("land-queue-row-required")` for every caller. Stricter (removes a fail-open in the test), proven non-breaking by CP-9 + CP-17.
- `h.Adapter.Inputs.ShouldBeEmpty()` -> labelled `land-busy-does-not-submit`. Same.
- `h.Adapter.Inputs.Count.ShouldBe(typed)` -> labelled `land-recovery-does-not-retype`. Same.
- Census `.ShouldBe(declared)` -> `.ToArray().ShouldBe(declared, "all-owned-worker-markers-registered")`: ordered sequence equality on arrays. Same.

## (c) stdout flush and child cleanup

`CrashWorkerProcess.WriteStdoutSentinel` writes UTF-8 bytes to `Console.OpenStandardOutput()` (the raw
fd-1 stream, below the testing host's `Console.Out` replacement) and flushes synchronously before the
worker body runs; the static field keeps the stream alive. The parent asserts the sentinel only after
`WaitForExitAsync` and `Task.WhenAll(Stdout, Stderr)` (pipe EOF), so there is no sleep and no race with
the host. Cleanup: `JoinOwnedAsync` kills and joins the witness and every owned child on the success
path; `finally` kills any survivor (witness re-resolved from the `.witness` file) and `DrainAsync`
joins the pipes; `Running.DisposeAsync` is idempotent. The witness is `pwsh Start-Sleep 180` with no
children, so a parent death leaves at most a 180 s self-expiring process. Census after both runs: no
`Antiphon.Tests.dll` child, no `Start-Sleep`, no `/tmp/antiphon-c448-*` directory.

## (d) warmup count 6 -> 8

`TestWorkerModes.All` has 8 entries; CP-12 is `[MethodDataSource(WorkerMarkers)]` over `All`, so it
executes exactly `All.Count` results (8 observed). The number 8 lives only in the plan row (Min/Expect),
and the tool treats Min as a floor (`RowRunner.cs:287`, `Executed < MinExecuted`). Adding a ninth mode
makes CP-12 report 9 without a red row; what goes red is CP-11 `all-owned-worker-markers-registered`,
which compares `All` to a reflection census of every `*Worker` type with a literal `Marker` string, in
both directions (a class missing from `All`, or an `All` entry whose class has no literal marker; PC-22/63).
Disclosed as R3 below.

## (e) delivery evidence

Producer -> real `SessionMessageQueueService` (`h.Queue.FlushIfIdleAsync`) -> adapter `SubmittedBodies`
contains the note body -> the runner transcript carries `UserPrompt` sequence 11 -> `ReconcileAsync`
persists and confirms (`ConfirmingPromptSequence == 11`) -> final verdict is a fresh `AppDbContext` read
of the whole-body `UserPrompt` at sequence 11 in the intended session with
`PromptSubmissionMatch.IsCompleteIn` (`land-complete-userprompt-required`). Busy recipients: a Working
session gets no input (`land-busy-does-not-submit`), then idle -> flush -> submit. Queue row / Sent are
never the receipt: the test marks the row Sent itself before the receipt, and three finite rejects
(wrong session, head+tail splice, complete body at the floor sequence 10) each leave `ConfirmedAt`
null against `LandNoteReceipt.Prompts` (session predicate line 39, `Sequence > floor` line 44) and
`IsReceipt` (`IsCompleteIn`, line 77). `queue-inserted` recovery pins exactly one keyed
`SessionQueuedMessages` row equal to `QueueMessageId` (`land-keyed-recovery-single-row`). Substitute:
the runner transcript is the inherited S1/S5 fake.

## (f) red reachability and the seam-commit failure

Run `20261009-190631-a479` at `8d5f024f3` (author's worktree evidence): CP-8 7 of 7 failed at
`worker-stdout-drained`, `should contain "c889-ready-diagnostic" but was actually ""`, 0 passed; CP-9..12
green. The testing host replaces `Console.Out` unconditionally before the assembly hook, so every
`Console.WriteLine` sentinel went to the replaced writer: a deterministic fixture defect with no timing
component, fixed by `a18cf764a` (raw stdout stream) and green 7 of 7 in the author's run and in run 1.
Each new label has a finite red: cut/PID identity (`Decide` accepts with an error, the labelled
`ShouldBe` on Cut/Pid fails), early-exit (`pwsh exit 3` + absent file + completed cycle -> `early-exit`
code 3), ready-recheck (pre-written file + completed cycle -> `accepted`; removing the immediate read
yields `not-ready`), one-selected-marker (every marker seeded then cleared), connection-env-only
(sentinel absent from argv/JSON, present in env), typed-resume (serialized `Cut == "resume"`),
crash-kills-root-only (witness alive after root kill), owned-children-joined (`HasExited` after join),
admission (`Rejection` null for the owned request first, non-null per single-field mutation, including
the land-only cut `C03` rejected by the retirement whitelist), stderr-drained (rejected probe exits 1
with the stderr sentinel), and the receipt rejects above.

## (g) deterministic time

No `Task.Delay`/`Thread.Sleep` added; the two `Task.Delay(100)` poll loops in C448/C459 are replaced by
a `FileSystemWatcher` plus `Process.Exited` with an immediate post-subscribe read. `Start-Sleep` occurs
only inside held child processes (30 s sleeper probe, 180 s witness), never awaited, killed in `finally`.
The 2-minute `CancellationTokenSource` budgets are pre-existing deadlines. The unchanged
`PublicationCommitCrashAsync` keeps its pre-existing 100 ms poll (outside this change).

## Merge-tree and evidence diff

`git merge-tree --write-tree origin/master HEAD` exit 0, tree `3ebfa63837135530e3630a62d2cf8f9fa5b2a938`
(matches the author at `2e7e7e58a`). `scripts/check-evidence-diff.ps1 -BaseRef 9e98286fb... -HeadRef HEAD`:
commits=2 entries=0 violations=0.

## R2/R3 disclosures (no `found`)

- R3: the CP-12 count is a plan floor, not a code pin (see (d)); the structural guard is CP-11.
- R3: the DB-level head+tail reject in `AssertRejectedReceiptsLeaveTheNoteOpenAsync` runs only when the
  note body exceeds 300 chars (not measured here); the unconditional `IsReceipt(synthetic, spliced)`
  pair carries `land-receipt-complete-body`, so PC-30 keeps a finite red either way.
- R3: C459 asserts `accepted` but not cut/PID identity labels; the plan maps no PC there (PC-26, 59..62 covered).
- R3: my run 1 CP-17 row selected zero tests (bare names in a method OR); corrected and rerun as run 2.
- R3: the importer warns "names more than four land classes" on CP-17; a token heuristic, advisory only.

Post-land Mutation was NOT run: PC-15..32, PC-55..68, PC-73, PC-74 stay pending for method-scoped
SourceLanding Mutation. No restart is needed (test-only change).
