# CARD-1305 Windows E2E land-contract fixture repairs (master-only)

Plan owner: `18b06751`. Branch: `feat/card-task-18b06751`.
Base: `19499afdf20fbe23d93c5a8d43c458fa5080f65e`, which is the Investigate commit itself, so
`docs/investigations/2026-10-19-e2e-land-contract-reds.md` is already on this branch and lands
with this plan. Input report: that file (task `463014fb`). Round: Final. Restart: none.
Publication is only to the assigned task ref; landing rebases.

Scope: tests and fixtures only. No product file changes. Every repaired test is a Windows E2E
case; the edits themselves are platform-free. C540 is a separate slice, commit and checkpoint
from the two land-approval signatures.

## Platform

Read on 2026-10-19 from the configured API (`GET /api/runner-defaults`,
`GET /api/session-runners`):

| Fact | Value |
|---|---|
| Global default runner | `server2` (operator reason: desktop only for truly Windows work) |
| `desktop` | platform `windows`, capacity 2 delegated tasks, 0 occupied, accepting |
| `server2` | platform `linux`, draining, not accepting new work |
| `server2-temp` | platform `linux`, capacity 10 sessions, 5 occupied, accepting |

Every checkpoint row below runs `tests/Antiphon.E2E` through `LandDeliveryFixture`, which
asserts `OperatingSystem.IsWindows()`, a located ConPTY redistributable and a staged
`fakegrok.exe` before anything else (`tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs:59-61`).
So the Code dispatch needs `-Platform Windows`; omit `-Runner` (the platform pin resolves to the
desktop without naming a host). Nothing else in this plan needs Windows: authoring the edits,
Review, and land are platform-free. The card itself stays `platform Any`.

## Ground truth

| The brief or report assumes | What the code does | Verdict |
|---|---|---|
| C488 land POSTs fail because the synthetic Review omits `reviewedSourceClean`. | `LandDeliveryFixture.Review.cs:41-52` writes subject, SHA and `ordinaryScopeCompleted: Full` only. `ReviewEvidence.cs:83` maps an absent key to `null`; `ReviewEvidenceBindingService.cs:63` carries it; `AgentTaskReplyService.cs:5173` stores it. `LandApproval.cs:77-78` throws `review_evidence_source_not_clean` for anything but `true`. | Confirmed. |
| The stale-A row cannot reach `review_evidence_sha_mismatch`. | The clean-source check at `LandApproval.cs:77` precedes the subject check at `:107` and the SHA check at `:109`. | Confirmed. S1 makes the SHA check reachable. |
| A `reviewedSourceClean: true` line in the fixture report is accepted by settlement. | `ReviewEvidenceParserTests.cs:230` pins the parser accepting the line; `RunnerTaskSettlementTests.cs:200` settles a report with it. No server-side receipt check gates the value at settlement; the receipt rule in `docs/orchestration-loop.md:2282` is a Review-agent obligation. | Confirmed. The fixture is the Review agent here and its source is a committed, pushed, clean tip. |
| Lost-flush delivery succeeds but the test needs an obsolete `completion-scan` observation. | `completion-scan` is emitted only by the 15-minute durable sweep (`CompletionNoteWorkHostedService.cs:78`) and SourceLanding recovery (`:121`). `DropWakeup("completion")` is consulted at `AgentTaskLandNotificationService.cs:97` (scan reconcile) and `:161` (enqueue). `SessionMessageQueueService.cs:887-890` schedules queue recovery for the committed Delegation row regardless, and `CompletionNoteWorkHostedService.cs:30-31` turns it into a flush with no observation. | Confirmed. The test's liveness contract holds; its observation is obsolete. |
| The fixture could make the durable sweep fire in-test. | The hosted service takes the DI `TimeProvider`; `LandDeliveryOptions.cs:68-73` injects the fixture `LandClock` into five scoped services only. `CompletionNoteRecoveryQueue` is `internal` and has no boundary seam. | Rejected without a product seam (follow-up F-1). |
| C540 blocks on Auto base selection before any warning intent exists. | `LandDeliveryFixture.Dispatch.cs:56-60` inserts the queued task without `RequestedWorktreeBaseMode` (default `Auto`, `AgentTaskEnums.cs:512`). `AgentTaskDispatcher.cs:5733-5760` resolves, gets `Ambiguous` for the divergent tips, calls `BlockAsync` and returns before the drafts at `:6036`. | Confirmed. |
| Explicit `Target` mode keeps the two reduced sibling warnings. | With `Target` and no preview JSON the dispatcher skips the resolver (`:5733-5735`); `siblingObservation` is nulled only when a card base was selected (`:5773`); `BuildDispatchWarningDrafts` (`:6234`) drafts one intent per observed sibling. `-FreshWorktree` maps to `Target` at `AgentTaskService.cs:1510`. Unit precedent: `AgentTaskDispatchBaseGuardTests.C508_GuardRefMismatchWarnedOnce` seeds `Target` (`:1617`) and asserts one sibling intent per divergent sibling (`:1413-1421`). | Confirmed. |
| The C540 quiet timeout is the only failure signal. | `WaitForDispatchIntentsAsync` polls intents through `UntilProtocolAsync`; `ProgressAwareWait.UntilAsync` propagates a predicate exception immediately (`tests/Shared/ProgressAwareWait.cs:27`). | A Blocked check inside the predicate fails fast and names the reason. |
| The Investigate report must be committed onto this branch. | It is the base commit `19499afdf`. | Already satisfied. |
| The report classifies all four signatures as fixture drift, not product defects. | Each refusal implements a documented contract: CARD-0835 (`docs/orchestration-loop.md:2282`), CARD-0442 (`docs/orchestration-loop.md:219`, `docs/session-runtime-invariants.md:1100`), CARD-0467 recovery (`a16d82d72`). | Agreed. No product slice. |
| A single `[Arguments]` row can be a checkpoint filter. | Repo convention selects an argument row by its method (`docs/testing-and-build.md:555`); the checkpoint `Filter` column is a treenode filter. | The matrix row runs all six cases. |
| The checkpoint tool accepts E2E rows and method-level OR filters. | Prior executed plans: `2026-09-27-card-0494-plan.md` CP-4 (`tests/Antiphon.E2E ->`), `2026-10-17-card-1315-fixture-repair.md` CP-2 (`/*/*/Class/(A*)\|(B*)`). `AfterSelector` accepts `S0` and `S1-S3`. | Used below. |

## Decisions

- **D-1 Tests and fixtures only.** The report proves no product defect, and this plan found none; every refusal is an owner-documented contract. No product slice exists.
- **D-2 Verification design is in this plan; next stage is Code.** The three fixes are one-line fixture edits plus three witness assertions; a separate TestDesign stage would cost more than the Code budget (30 to 60 minutes). Rejected: `next: test-design`.
- **D-3 C488 fix is the declaration, not the gate.** S1 adds `reviewedSourceClean: true` to the synthetic Review report. The witness is a new `outcome.ReviewedSourceClean.ShouldBe(true)` assertion in `WaitForReviewReceiptAsync`, red before S1 for every Review-driven test, green after. Rejected: loosening `LandApproval.cs:77` (contract), or passing `-ReviewedSourceClean` only on the stale-A arm (leaves the six matrix rows red).
- **D-4 Lost-flush test asserts the cut fired and delivery still happened.** S2 makes `FileBoundary.DropWakeup` write a `wakeup-dropped-<boundary>-<guid>.observation.json` record carrying the dropped identity; V28 requires a dropped-completion record whose identity is the delivered note and keeps its existing receipt, remote and one-prompt assertions. The obsolete `completion-scan` requirement is removed. Rejected: (a) forcing the durable sweep (no fixture-controlled clock or seam; needs a product edit, F-1); (b) replacing `CompletionNoteFlushQueue` with a recording subclass (the 60-second stranded-queue sweep can also deliver, so a `TryEnqueue` record does not uniquely attribute the delivery and the extra seam buys nothing decisive); (c) deleting V28 and its alias (drops the lost-wakeup liveness contract).
- **D-5 C540 fix is the explicit fresh-worktree intent, kept separate from land approval.** S3 sets `RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Target` on the C540 queued task, which is what `-FreshWorktree` records and what the unit guard fixtures already use. S3 also adds a Blocked fast-fail inside `WaitForDispatchIntentsAsync` so a dispatch refusal names its reason instead of spending a 60-second quiet deadline. Rejected: `Task` mode on `c540-containing` or deleting `c540-divergent` (a selected card base nulls the sibling observation at `AgentTaskDispatcher.cs:5773`, so the two expected intents would never exist); letting Auto continue on divergent tips (CARD-0442 contract). S3 is its own commit and its own checkpoint rows, and belongs to the CARD-0540/CARD-0442 follow-up, not the reviewed-source-clean gate.
- **D-6 One red commit, three fix commits, two builds.** S0 commits all three witnesses before any fix (git-common: failing test before its fix). CP-1 to CP-3 run red from one build; S1, S2, S3 are separate commits; CP-4 to CP-7 run green from a second build.
- **D-7 Green coverage for C540 is a two-row sample.** The report's representative (`C540_PreEnqueueCrashRecoversWarnings`) plus the idle baseline (`C540_CollapsedWarningsReachIdleCaller`). The other eight C540 rows take about 30 more minutes and are deferred to the rc Windows lane (R-deferred below). The caller may widen CP-7 to `/*/*/DispatchBaseWarningDeliveryE2ETests/C540_*` at the cost of that time; that is their call, not Code's.
- **D-8 Dispatch and prerequisites.** Code runs with `-Platform Windows`, no `-Runner`, in its own worktree on the desktop. A fresh worktree has no `client/dist`; the fixture serves it (`UsePrebuiltFrontend = true`) and `EnsureClientBundleIsCurrent` hard-fails without it. The client bundle build is the one pre-authorized unlisted build (see Verification design).
- **D-9 Every checkpoint row is `Serial: true`.** Each row boots real servers, an isolated runner, Postgres and native FakeGrok under a per-assembly process limiter; two rows in flight would share the host and the limiter would not cap the second process.
- **D-10 Not rerun, with reason.** `C488_ReviewDeliveryCrashMatrix` (9 rows), `C488_ReviewEvidenceCrashRecovers` and `C494_ReviewReceiptRejectsFalseEvidence` (8 rows) share `StartReviewDelegateAsync` but never admit a land; the added line changes only the nullable flag binding that `ReviewEvidenceParserTests` already pins. They stay in the rc Windows lane.

Checklist for sessions that wait for input: not applicable. No slice adds or changes a session that waits for input; the FakeGrok caller sessions are fixture-owned and disposed with the fixture.

## Slices

### S0 — red witnesses (one commit, before any fix)

1. `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.Review.cs`, in `WaitForReviewReceiptAsync`
   after `outcome.ReviewedSourceSha.ShouldBe(SourceSha);` (line 140):
   `outcome.ReviewedSourceClean.ShouldBe(true, "CARD-0835: evidence-backed land admission requires the clean-source assertion");`
   Red today at the receipt stage for `C488_ApprovalOutcomeReceiptMatrix(stale-A)` and every
   `C488_ReviewToLandReceiptMatrix` row (the outcome stores `null`).
2. `tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs:154`, in `C467_V28_LostFlushWakeupRecoversOnIdleCaller`:
   replace the `completion-scan-*.observation.json` requirement with
   `wakeup-dropped-completion-*.observation.json` files, at least one of whose JSON `identity`
   equals `note.Id`. Keep the preceding `ReceiptAsync`, `AssertRemoteAsync` and
   `AssertOnePromptAsync` calls unchanged. Add a comment: the direct enqueue-time and
   scan-time wakeups are dropped by the cut; delivery proves the remaining recovery sources
   (queue-side scheduled flush, stranded-queue sweep) keep an idle caller's outcome live; the
   15-minute durable sweep is not exercised (F-1). `C488_ApprovalLostFlushRecovers` aliases this
   method and needs no edit. Red today: the fixture writes no such file.
3. `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.Dispatch.cs`, `WaitForDispatchIntentsAsync`:
   inside the `UntilProtocolAsync` predicate, load the owned task row (`TaskId`) with
   `AsNoTracking` and assert `Status != AgentTaskStatus.Blocked` with a message that includes
   `FailureReason`. `ProgressAwareWait` propagates the assertion, so the wait ends at the first
   poll after the refusal. Red today for every C540 test: fails within seconds naming
   `worktree_base_ambiguous` instead of a 60-second quiet timeout.

### S1 — C488 clean-source declaration (one commit)

`tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.Review.cs:47`: add
`+ "reviewedSourceClean: true\n"` beside `ordinaryScopeCompleted: Full`. The declaration is
shared by the raw, Apply and spill forms. The fixture's source is the committed, pushed
`c467-source` tip at `SourceSha`, so the declaration is truthful for this Review.
Green after S1: `C488_ApprovalOutcomeReceiptMatrix(stale-A)` reaches
`review_evidence_sha_mismatch` (`LandReviewedAsync` / `RefuseStaleReviewAsync` unchanged);
all six `C488_ReviewToLandReceiptMatrix` rows land with the evidence ID.

### S2 — lost-flush drop observation (one commit)

`tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs:121-123`, `FileBoundary.DropWakeup`:
compute the existing drop predicate, and when it is true write
`wakeup-dropped-{boundary}-{Guid:N}.observation.json` under `options.Root` with
`{ boundary, identity, cut, at, pid }` (synchronous `File.WriteAllText`; the method is not
async). Return the predicate unchanged. The `lost-request` and `idle-lost-wakeup` cuts also
record drops; nothing asserts on them. Green after S2: V28 and its C488 alias.

### S3 — C540 explicit fresh-worktree intent (one commit, separate from S1/S2)

`tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.Dispatch.cs:56-60`: add
`RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Target` to the queued C540 task with a
comment: the fixture's four same-card branches are intentionally divergent so the dispatcher
observes and reduces them into sibling warnings; CARD-0442 refuses automatic selection on
divergent tips, so the task records the `-FreshWorktree` intent (`AgentTaskService.cs:1510`).
Leave `StartMismatchDispatchAsync` (C508) unchanged: it seeds no siblings and resolves to the
target already. Green after S3: the two reduced intents (`c540-containing` covering 2,
`c540-divergent` covering 0) are committed and delivered as before.

### Follow-ups, not slices

- **F-1** Qualify the 15-minute durable completion sweep after losing every current wakeup.
  Needs a product seam (drop the queue-side `ScheduleFlush` signal or inject the hosted
  service's clock). File under the CARD-0467/CARD-0488 completion-recovery follow-up.
- **F-2** The eight C540 rows not in CP-7 and the Review crash/C494 rows in D-10 run in the
  next rc Windows profile execution of `tests/Antiphon.E2E`.

## Verification design

| ID | Required outcome / coverage |
|---|---|
| V-1 | A fixture Review that declares a clean source settles with `ReviewedSourceClean == true`; the stale-A refusal is `review_evidence_sha_mismatch`; all six matrix rows land with the evidence ID bound on request and operation. Red first at CP-1, green at CP-4 and CP-5. |
| V-2 | Under the `lost-flush` cut the direct completion wakeup for the delivered note is recorded as dropped and the idle caller still receives exactly one confirmed outcome prompt with the remote containing `SourceSha`. Red first at CP-2, green at CP-6. |
| V-3 | The C540 queued task dispatches under explicit `Target` intent with exactly two reduced sibling intents whose details name the expected branches, tips and `also covers 2` text, delivered once each; a dispatch refusal fails the wait immediately with its reason. Red first at CP-3, green at CP-7. |
| R-1 | The Apply and spill forms and the busy recipient of the matrix keep their rendering, spill and single-prompt assertions (CP-5). |
| R-2 | `C488_ApprovalLostFlushRecovers` stays an exact alias of V28 (CP-6). |
| R-3 | The pre-enqueue crash cut still recovers both warnings through the restarted host with original intent identities (CP-7). |
| R-deferred | Eight further C540 rows, nine `C488_ReviewDeliveryCrashMatrix` rows, `C488_ReviewEvidenceCrashRecovers`, eight C494 rows: rc Windows lane (D-7, D-10, F-2). |

Red rows are expected failures; Code reports them as red with the named assertion and does not
"fix" them. A red row that passes before its fix means the witness is a stub (rule 4) and must
be reworked before continuing. CP-3's red must be the fast Blocked assertion naming
`worktree_base_ambiguous`, not the historical quiet timeout.

Prerequisites on the desktop worktree `C:\Antiphon\worktrees\card-task-18b06751`, before CP-1:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1305-client-ci -- npm --prefix client ci
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1305-client-build -- npm --prefix client run build
```

That pair is the one pre-authorized unlisted build (D-8); report it with its slot lines.
Follow the Investigate run's safe environment policy (inherited `PhoneHome__*` cleared,
phone-home disabled, provider and parallel opt-ins cleared); the exact helper it used is
`C:\src\Antiphon\.antiphon\investigations\e2e-land-contract-463014fb\run-case.ps1` on the
desktop. Do not restart AppHost or the production runner; the fixtures own random-port
servers and an isolated runner (`LandDeliveryFixture.cs:89` asserts the port is not 17204).

Running the table:

```powershell
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-19-card-1305-e2e-land-contract-fixtures-plan.md --rows CP-1,CP-2,CP-3 --expected-source-sha <S0 sha>
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-19-card-1305-e2e-land-contract-fixtures-plan.md --rows CP-4,CP-5,CP-6,CP-7 --expected-source-sha <S3 sha>
```

Call `wait` again while it returns 75. Commit S0 before the first run and S1, S2, S3 before
the second; do not edit source while a run is in flight. Delete every `bin-c1305-red` and
`bin-c1305-green` directory (about one per project) before finishing. Run
`scripts/check-evidence-diff.ps1 -BaseRef 19499afdf20fbe23d93c5a8d43c458fa5080f65e -HeadRef <pushed sha>`
and the `git fetch origin master` plus `git merge-tree --write-tree origin/master HEAD`
landable check; report both.

Manual acceptance: inspect each fresh TRX identity and count against the `Expect` column; for
CP-4 confirm `stale-refusal.txt` in the fixture root names `review_evidence_sha_mismatch`; for
CP-6 confirm the dropped-wakeup record's `identity` equals the confirmed note ID; for CP-7
confirm `dispatch-graph.json` plus the two intents' details. No UI or deployment work.

### Mutation handoff

Every control remains pending for method-scoped SourceLanding Mutation after Review and land.
Candidate location `docs/superpowers/mutations/` (unbound).

| PC | Method / variant pending |
|---|---|
| PC-1 | `C488_ApprovalOutcomeReceiptMatrix`: remove the `reviewedSourceClean: true` line from the synthetic report; must fail at the receipt-stage clean assertion; restored must pass. |
| PC-2 | `C467_V28_LostFlushWakeupRecoversOnIdleCaller`: make `DropWakeup` return false for `completion` under `lost-flush`; must fail on the missing dropped-wakeup record; restored must pass. |
| PC-3 | `C540_CollapsedWarningsReachIdleCaller`: restore `Auto` on the C540 queued task; must fail fast naming `worktree_base_ambiguous`; restored must pass. |

### Cost

Ordinary floor is the `EstimatedMinutes` sum: **49 minutes** for seven rows including two
isolated E2E builds (about 4 minutes each, after the Investigate build's 36-warning clean
result). Add the client bundle prerequisite (about 4 minutes) and authoring (about 8 minutes):
ask `-ExpectAbout 60`. Measured single-case walls from the Investigate run: review rows
178 to 181 seconds, lost-flush 81 seconds, C540 pre-enqueue 119 seconds under the refusal and
a similar bound once it dispatches. CP-5 is six review rows at roughly 3 to 4 minutes each;
the importer warns that its derived 60-minute row timeout exceeds the 45-minute advisory, which
is accepted: the six argument rows share one method and cannot be split by treenode filter, and
the timeout is not capped. The table was validated on this branch with
`tools/Antiphon.Checkpoints -- import --plan <this file>` (exit 0, that one warning).
Additional runs require an actual failure or a missing execution.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S0 | `tests/Antiphon.E2E -> bin-c1305-red/` | red-review-clean | `/*/*/AgentTaskLandDeliveryE2ETests/C488_ApprovalOutcomeReceiptMatrix` | V-1 red | expected red: 1 executed, 1 failed at the new ReviewedSourceClean assertion (null, not true), 0 skipped | 1 | 7 | true |
| CP-2 | S0 | CP-1 | red-lost-flush | `/*/*/AgentTaskLandDeliveryE2ETests/C467_V28_LostFlushWakeupRecoversOnIdleCaller` | V-2 red | expected red: 1 executed, 1 failed at the missing wakeup-dropped-completion record after a confirmed receipt, 0 skipped | 1 | 2 | true |
| CP-3 | S0 | CP-1 | red-dispatch-blocked | `/*/*/DispatchBaseWarningDeliveryE2ETests/C540_CollapsedWarningsReachIdleCaller` | V-3 red | expected red: 1 executed, 1 failed fast at the Blocked assertion naming worktree_base_ambiguous, no quiet timeout, 0 skipped | 1 | 3 | true |
| CP-4 | S1-S3 | `tests/Antiphon.E2E -> bin-c1305-green/` | stale-review | `/*/*/AgentTaskLandDeliveryE2ETests/C488_ApprovalOutcomeReceiptMatrix` | V-1 | 1 executed, 1 passed, 0 failed/skipped; refusal text names review_evidence_sha_mismatch | 1 | 7 | true |
| CP-5 | S1-S3 | CP-4 | review-land-matrix | `/*/*/AgentTaskLandDeliveryE2ETests/C488_ReviewToLandReceiptMatrix` | V-1, R-1 | 6 executed (idle and busy by raw, Apply, spill), 6 passed, 0 failed/skipped | 6 | 20 | true |
| CP-6 | S1-S3 | CP-4 | lost-flush | `/*/*/AgentTaskLandDeliveryE2ETests/(C467_V28_LostFlushWakeupRecoversOnIdleCaller*)\|(C488_ApprovalLostFlushRecovers*)` | V-2, R-2 | 2 executed, 2 passed, 0 failed/skipped | 2 | 4 | true |
| CP-7 | S1-S3 | CP-4 | dispatch-warnings | `/*/*/DispatchBaseWarningDeliveryE2ETests/(C540_CollapsedWarningsReachIdleCaller*)\|(C540_PreEnqueueCrashRecoversWarnings*)` | V-3, R-3 | 2 executed, 2 passed, 0 failed/skipped | 2 | 6 | true |
