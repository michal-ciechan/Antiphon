# CARD-1065 S5 Code: implementation pushed; ordinary verification incomplete

S5 is not ready for Review. Final CP-5 executed 3 methods: V-13 and V-14 passed,
V-12 failed at G-102. The brief's two repair rounds are exhausted. Next is Code,
not landing, Review, Mutation or activation.

Original Code task / landing owner: `a1c361f4-0e52-47fb-9ba2-b074b594159c`.
Branch: `feat/card-task-a1c361f4`.
Worktree: `/work/worktrees/task-a1c361f4`.
Desktop checkout is inaccessible from this runner.
Task base: `88e81f4748539329ac127e9a70ae5b1b58fe8f21`.
Actual final tested source: `f6f91aabe5cc457d792ba81a5c7212ea8746ddbb`.
The report-only closing commit and full-range evidence-guard outcome are in the
completion message; they do not replace the actual tested SHA.

Plan: `docs/superpowers/plans/2026-10-05-card-1065-blocked-task-parking-plan.md`.
Evidence: `.antiphon/checkpoints/20261006-040850-e9cb/` (report.json, report.md,
rows/CP-5/run.trx, failures.md, console.log and coverage.log).
Classification evidence: `.antiphon/checkpoints/CP-5-registry-20261006-041257-80c0/`.
Generated payloads remain ignored. This Markdown is the individually committed report.

## Changes and commits

- `801f56909` cherry-picks the requested test-only `9d72aea30` from
  `feat/card-task-d180d288`. The predecessor blocker was read in full. Its original
  red evidence establishes the three initial missing guards; no deliberate mutant
  was introduced in this task.
- `b7170f90960ceb26185591b8c909ca34f4d15c72` composes registration, source preparation,
  publication-required reservation/send, park action identity, version-2 wire evidence,
  source checks during ambiguous-release recovery, stopped retained identity, and
  pool/retirement/deletion reservations. Nonreport blocks digest existing transcript
  bytes through a fixed stored sequence without creating Result or CompletedAt.
- `427a7311966802115f6de4e66da92567d77f2abd` corrects two new assertion type errors
  found by the first CP-5 build. Repair round 1.
- `f6f91aabe5cc457d792ba81a5c7212ea8746ddbb` adds the missing Blocked caller obligation
  before release, a receipt-generation refusal for TaskCompletion, and corrects the
  test HTTP factory that let capability reads dispose its shared client. It restores
  existing publication reason strings and adds receipt-negative tests. Repair round 2.

Each commit was pushed immediately, fast-forward only. No rebase, reset, amend or force push.

Production changes are in AgentTaskReplyService, BlockedTaskParkingService,
TaskParkPublicationService, TerminalRunnerSeatReleaseService/Policy,
PoolDelegateRelease, AgentTaskDispatcher, AgentTaskService and Program's DI wiring.
AgentTaskLandNotificationService also changes because V-12 requires completion
receipts to retain their delivery generation. That additional file is directly on
the planned caller-delivery path, not an unrelated change. Documentation is in
docs/session-runtime-invariants.md. Tests use BlockedTaskParkReleaseTests,
BlockedTaskParkReleaseFixture and RunnerSeatReleaseFixture, with a reasoned Slow
allowlist entry. No runner production implementation was changed.

## Fresh results and remaining ordinary work

| ID | Actual result at final tested source |
|---|---|
| V-12 | Failed, 17.326s: G-102 expected ConfirmedAt null, got 2026-10-05T00:00:00.6800000Z. |
| V-13 | Passed, 17.574s: nonreport question/bind/unmarked/prerequisite/quota/wall/cost/merge/land causes, Working/Unknown vetoes, sessionless create/routing/kind holds, unknown/foreign/sequencer source holds. |
| V-14 | Passed, 7.726s: retained stopped identity, null warm fields, pinned pool admission and retirement/deletion refusal, independent standing/AlwaysOn/board/specialist vetoes, failed-stop retention, cancellation preserving another reservation. |

Fresh TRX was inspected for all three exact class/method identities, nonzero counts,
and no skips. The classification TRX was separately inspected: Registry_matches_compiled_metadata
and both SlowTestTripwireTests methods passed (3/3, zero failed/skipped).

V-12 stops in its negative receipt loop. Its G-95 direct/settlement refusals and
prefix/destination/kind negatives completed before G-102. Its generation negative,
H0-H6 x busy/eligible caller matrix, real runner/child capstone, and
reserve/send/response/lost-reply recovery matrix are present but **not reached** in
the final run. They are pending ordinary verification, not passed. The V-14 method
does not yet include a separate continuation reservation-clear variant or unpinned
warm-selection challenge; add those to finish its full plan matrix.

The G-102 setup creates an eligible caller with no transcript history, then changes
the actually submitted prompt's Sequence to -1. Source inspection shows
CaptureTranscriptBaselineAsync makes an empty history unobservable, and
LandNoteReceipt then correctly uses deliveryStartedAt rather than a sequence floor.
The prompt retains its current timestamp. This is a likely fixture mistake, not
established evidence of a production floor bypass. A follow-up should seed genuine
prior caller history and assert the captured nonnull floor before the negative, or
explicitly exercise the timestamp-fallback negative with an old timestamp. Preserve
the assertion and production deadlines. Re-run CP-5 and inspect any later failures;
the unexecuted matrices may expose additional work.

Source inspection also found adjacent legacy expectations that still permit
publication-free Blocked release: TerminalRunnerSeatReleaseTests.
Blocked_report_with_running_runner_frees_the_seat and the Blocked arm of
RunnerSeatOrphanSweepTests.Existing_job_discovers_debt_without_settlement_callback.
They were not run because the brief explicitly closes verification to CP-5 plus
the registry guard. The continuation must reconcile those expectations with the
new mandatory publication barrier and its authorized verification scope.

No inherited-red claim is made; no baseline run was performed. No assertion or
timeout was loosened, and no automatic retry or loaded repetition was added.

The explicit S5 closed list overrides the generic whole-Unit profile text. No Unit,
namespace or assembly sweep ran. R-1, R-2, R-3, R-4, R-5 and R-6 are not run here;
the plan assigns them to S11. V-1-V-11, V-15-V-31 and CP-1-CP-4c/CP-6-CP-13 are
outside this S5 dispatch and are not claimed passed by it. S11 integrated final
verification and isolated activation/manual acceptance remain pending. No live
provider or fleet release test was performed; the feature remains dormant.

## Unedited checkpoint evidence

```text
CHECKPOINT CP-5 commit=b7170f90960ceb26185591b8c909ca34f4d15c72 build=failed filter=/*/*/BlockedTaskParkReleaseTests/C1065_* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=b7170f90960ceb26185591b8c909ca34f4d15c72 sourceState=clean buildSource=unknown
CHECKPOINT CP-5 commit=427a7311966802115f6de4e66da92567d77f2abd build=ok filter=/*/*/BlockedTaskParkReleaseTests/C1065_* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-a1c361f4/.antiphon/checkpoints/20261006-040302-ecc2/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=427a7311966802115f6de4e66da92567d77f2abd sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=f6f91aabe5cc457d792ba81a5c7212ea8746ddbb build=ok filter=/*/*/BlockedTaskParkReleaseTests/C1065_* executed=3 passed=2 failed=1 skipped=0 trx=/work/worktrees/task-a1c361f4/.antiphon/checkpoints/20261006-040850-e9cb/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=f6f91aabe5cc457d792ba81a5c7212ea8746ddbb sourceState=clean buildSource=verified
CHECKPOINT CP-5-registry commit=f6f91aabe5cc457d792ba81a5c7212ea8746ddbb build=reused filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-a1c361f4/.antiphon/checkpoints/CP-5-registry-20261006-041257-80c0/run.trx slot=granted waited=0s dirty=0 source=f6f91aabe5cc457d792ba81a5c7212ea8746ddbb sourceState=clean buildSource=verified
```

Initial compile failure: SourceEventId is a nonnullable Guid (bad ShouldNotBeNull),
and List<Guid>/array selected no valid ShouldBe overload. No tests ran.
First executable run: V-12 had no durable completion obligation; V-13/V-14 remained
ReleasePending because the fixture capability client disposed the primary client.

Every build/test driver used the host slot gate. Final CP-5 build slot=granted,
waited=0s, build=106.494s; test slot=granted, waited=0s. Registry reused the same
SHA-bound build, with slot=granted waited=0s. The report's manifest-wide "builds: 15"
is not fifteen executed builds: only selected bin-c1065-cp5 has a started build.

The only extra bootstrap build was the checkpoint tool itself, through
scripts/build-slot.ps1, because the required tool was not prebuilt. It took 4.69s
MSBuild time (5s lease), slot=granted waited=0s, zero errors and the existing CS8602
TaskOwnerGuard warning. The registry run is explicitly required by the brief;
there were no other test selections. Static coverage/receipt validation are
read-only tool commands, not builds/tests.

Receipt validation with the actual tested SHA returns exit 2:
`CHECKPOINT SOURCE INVALID reason=row_failed`. The source snapshots themselves
are clean and build provenance verified, but this is **not** a green qualification
certificate. Static coverage exits 2, INPUT_INVALID unresolved selected class,
with reachability=unproven; future selected classes are absent from this slice.

Evidence-policy guard over base..f6f91aabe exits 0, commits=4, entries=0,
violations=0. The full base..closing-report-HEAD guard is also required and its
actual result is supplied in the completion message. All generated payloads stay
ignored; no evidence directory or runtime-owned reports directory was added.

CP-5 outputs were removed with checkpoint `clean --run 20261006-040850-e9cb`
after inspecting its dry run; 28 producer-owned bin-c1065-cp5 directories were
removed. The bootstrap bin-c1065-driver directory is removed before settlement.
All runs were awaited to terminal exit; no checkpoint executor is left running.

## Pending Mutation controls

No PC has been discharged. SourceLanding Mutation owns deliberate red/restore/green
cycles and missing-control discovery after ordinary Code, Review and landing.

| PC | Pending control / variants |
|---|---|
| PC-94 | Committed report and exact caller obligation before release. |
| PC-95 | Direct coordinator and settlement publication barrier. |
| PC-96 | Durable park/action reservation before wire. |
| PC-97 | Caller obligation recovery, H0-H6 x busy/eligible. |
| PC-98 | Queue ACK without transcript. |
| PC-99 | Prefix-only caller prompt. |
| PC-100 | Whole text at a different destination. |
| PC-101 | QueuedUserPrompt rather than UserPrompt. |
| PC-102 | Old sequence floor / timestamp fallback; ordinary fixture repair pending. |
| PC-103 | Replacement caller generation; ordinary case not reached. |
| PC-104 | Lost enqueue ACK and deduplicated queue/receipt. |
| PC-105 | Busy-at-enqueue caller, zero premature writes. |
| PC-106 | Question, bind refusal, unmarked, prerequisite, quota, wall/cost, merge/land holds. |
| PC-107 | Sessionless create/routing/kind-exhaustion holds. |
| PC-108 | Unknown/foreign writer and sequencer holds. |
| PC-109 | Agent retained after release/deletion sweep. |
| PC-110 | PoolIdleSince and PoolReservedForRootTaskId remain null. |
| PC-111 | Pool admission reservation; pinned and unpinned variants. |
| PC-112 | Retirement reservation. |
| PC-113 | Standing owner. |
| PC-114 | AlwaysOn owner. |
| PC-115 | Board owner. |
| PC-116 | Specialist owner. |
| PC-117 | Episode-specific cancellation and continuation clearing. |

All other plan controls PC-1-PC-93 and PC-118-PC-226 also remain pending Mutation;
this task makes no predecessor or future-slice PC-clean claim.

## Continuation command and ownership

After the bounded Code continuation is committed, bootstrap the checkpoint tool
through scripts/build-slot.ps1, then run:

```sh
dotnet tools/Antiphon.Checkpoints/bin-c1065-driver/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-05-card-1065-blocked-task-parking-plan.md --rows CP-5 --expected-source-sha "$(git rev-parse HEAD)"
```

Await the run to non-75 exit, inspect its exact fresh TRX, and run the brief's narrow
classification guard if its source changes. Never substitute a whole-Unit sweep,
weaken G-102, widen a timeout, or call a red row qualified.

GET /api/runner-defaults and GET /api/session-runners were read using the session's
configured API and token. No host/platform was pinned or fleet setting changed.
Restart: **none**. Caller/orchestrator owns later activation after qualification.
