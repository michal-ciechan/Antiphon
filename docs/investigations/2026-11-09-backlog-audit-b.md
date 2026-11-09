# Backlog audit B — 2026-11-09

Read-only audit of 26 Antiphon-board cards against master `e983d6fe263af623a3da186a2260575f60cc4c5f` (this worktree, `feat/card-task-1dc84f1c`). Each card was read with `scripts/card.ps1 get -Board Antiphon`. No card was closed, moved, edited, or commented. Verdicts are about the code at that master tip. A board column of Review does not change the code verdict.

Counts: **A 3, B 3, C 20, D 0.**

| Card | Board column | Verdict | Size | Windows-pinned | Needs operator | Fixing SHA or master location |
|---|---|---|---|---|---|---|
| CARD-1292 | Backlog | A | — | no | no | `1839210555a5e6cf84e11295e5e226b0749e7960`, `0633527c62be63a8acedc35cb7bb6a967d06afdf`; witness `ce6dd79df05e5813758d2545ccb8fe9901fb66a0` |
| CARD-1293 | Backlog | C | M | no | no | `PipelineHandoffActionService.cs:92`, `:199-210`; endpoints `:60-61` |
| CARD-1294 | Backlog | C | M | no | no | `PublishedCardCompletion.cs:42-47`, `:94-97`; `CardService.cs:1162-1185` |
| CARD-1295 | Backlog | A | — | no | no | `7e95cd20ae1e2b853b86bf0e59fff81aae4935b1`, `ce6dd79df05e5813758d2545ccb8fe9901fb66a0`, `1b4bc3dab78c1233fbb5428bf602a4e806a6dbab`, `b2a612807e9541258cc4f396123541958884fe03` |
| CARD-1300 | Backlog | C | S | no | yes (check order) | `AgentTaskService.cs:1646-1648` before `:1745`; admission test arguments `:110-117` |
| CARD-1309 | Backlog | B | L | yes | yes (Windows window for N5) | N4 `36dfcedf37f80b3437309070d96238f628a2c2f4`; N2 expectation `7cba89090d4b36b6430fa0ddd484299a4a158592` |
| CARD-1429 | Backlog | C | M | no | no | `AgentTaskLandService.cs:538-546` |
| CARD-1431 | Backlog | C | M | no | no | `DispatchHoldDetails.cs:150`, `:185-218`; pipeline `:624-641` |
| CARD-1437 | Backlog | C | S | yes (mklink check) | no | `AgentTaskLandActiveSourceClaimTests.cs:22` |
| CARD-1438 | Backlog | B | S | no | no | tool lane `e51d4375a4bc57297bc052ef3734d0d3ee824347` (CARD-1747); not the git lane |
| CARD-1439 | Backlog | C | S | no | no | `LandingAdmissionControlTests.cs:8`, `OutputDistillationApplyRaceTests.cs:18` |
| CARD-1441 | Review | C | S | no | no | `RunnerWorkspaceService.cs:1012-1048` |
| CARD-1442 | Backlog | C | M | no | no | no `RepositoryMaintenance`; refs still created `LandingGit.cs:1979`, probe `StartRefAvailability.cs:45` |
| CARD-1443 | Backlog | C | M | no | no | `AgentTaskLandSourceResolver.cs:629-632`; `LandingGit.cs:766` |
| CARD-1444 | Backlog | C | S | no | yes (live land week) | histogram logged `AgentTaskLandService.cs:559-566`; no week record |
| CARD-1445 | Backlog | C | M | yes | no | `ReportValidator.cs:34-35`; `OutputCleanup.cs:96` |
| CARD-1450 | Backlog | C | M | no | no | S2 `LandRefRetention` exists; no S3 sweep class |
| CARD-1451 | Backlog | C | S | no | no | amendment file still separate; `LandingGit.cs:695-705`; `LandingGitTests.cs:1030-1041` |
| CARD-1453 | Review | A | — | no | no | `1d4a76c9fbcabcc8cf5eaa9414484dfbc33c29d9`; witness `cfaf42afe8d67ef6968d6ec9982c27a8fff3e1ee` |
| CARD-1454 | Backlog | C | S | no | no | `ChannelReplyDispatcher.cs:65`, `:254`, `:1512` |
| CARD-1455 | Backlog | C | S | no | yes (ADV-2) | `ChannelReplyDispatcher.cs:275-276`, `:301-311` |
| CARD-1546 | Backlog | C | L | no | requested, not blocking a plan | `LandingGit.cs` still in the server project |
| CARD-1568 | Backlog | C | S | no (W-1..W-7 stay on the desktop lane, outside this card) | no | `AuditGitFixture.cs:30-32` |
| CARD-1588 | Backlog | B | S remaining | PATH follow-up is Windows restart scripts | no | composer `eb284614b92204eefcbdea93aa28ac396f6524fc`, `4c4ad5126fe576320661517dbb785b65bafb470b` |
| CARD-1592 | Backlog | C | L | no | no | no watcher type, `/api/watchers`, or `scripts/watch.ps1` |

All named SHAs above are ancestors of `e983d6fe263af623a3da186a2260575f60cc4c5f`.

## A — already fixed

### CARD-1292

The handoff writer is recipient-bound, and the operator bypass is explicit.

- `PipelineHandoffActionEndpoints.AuthorizeAsync` returns `"operator"` when the operator header matches (`PipelineHandoffActionEndpoints.cs:56`), then refuses any other caller whose session is not `predecessor.ParentSessionId` (`:80-81`, code `handoff_recipient_denied`). Introduced by `1839210555a5e6cf84e11295e5e226b0749e7960` (2026-10-15).
- The child-request path checks `RecipientSessionId` in `PipelineHandoffActionService.cs:123-124` (`0633527c62be63a8acedc35cb7bb6a967d06afdf`).
- Witness `PipelineSpeedIntegrationTests.C1281_B2Authorization` (`PipelineSpeedIntegrationTests.Wiring.cs:37-48`) posts as `other-recipient`, expects 403 `handoff_recipient_denied`, and expects zero action and observation rows. Commit `ce6dd79df05e5813758d2545ccb8fe9901fb66a0`.

The card was filed at 2026-10-15T13:52:16Z, before those commits, and is still Backlog.

### CARD-1295

All four advisory items are on master.

1. CP-25 is committed in `docs/superpowers/evidence/2026-10-15-card-1281-b1-receipt-correction.md` (checkpoint line at that file). Commit `b2a612807e9541258cc4f396123541958884fe03`.
2. Refusal codes are bound, not only HTTP 409. `AdvisoryChangePolicyTests` `bad-number` expects `review_evidence_correction_receipt_invalid` and `wrong-receipt` expects `review_evidence_correction_receipt_coordinates_mismatch` (`AdvisoryChangePolicyTests.Correction.cs:166-175`, `ce6dd79df05e5813758d2545ccb8fe9901fb66a0`).
3. `PipelineSpeedIntegrationTests.C1281_B1Authorization` (`PipelineSpeedIntegrationTests.Correction.cs:25-35`, `1b4bc3dab78c1233fbb5428bf602a4e806a6dbab`) allows task, session, capability, and operator, and denies anonymous, invalid, revoked, worker, project, repository, board, card, and session-card.
4. A bad receipt number is a 409. `CheckpointReceiptValidator.ImportAsync` catches `JsonException` and `OverflowException` and refuses `receipt_invalid` (`CheckpointReceiptValidator.cs:27-30`). The catch is `7e95cd20ae1e2b853b86bf0e59fff81aae4935b1`; `OverflowException` was added by `1839210555a5e6cf84e11295e5e226b0749e7960`. The HTTP witness is the `bad-number` row above.

### CARD-1453

Post-window and other land git starts set `GIT_OPTIONAL_LOCKS=0` after `ConfigureProcess`, so a configure step that clears the variable does not win.

- `LandingGit.cs:331-332` and `:1302` call `ServerGitProcess.SuppressOptionalLocks` after configuration. The helper is `ServerGitProcess.cs:33-34`.
- Fix `1d4a76c9fbcabcc8cf5eaa9414484dfbc33c29d9` (`fix: CARD-1453 land git reads suppress optional locks after configuration`, 2026-11-09).
- Witness `cfaf42afe8d67ef6968d6ec9982c27a8fff3e1ee` (`test: CARD-1453 post-window source status must not take index.lock`).
- The board column is Review (updated 2026-11-09). The fixing commits are already on this master tip.

## B — partly fixed

### CARD-1309

Native Windows E2E debt, sections N1-N5. No Windows green run is in this checkout. Do not treat the fixture edits as a pass.

- N4, fixed in the fixture: `LandDeliveryFixture.Review.cs:48-51` sets `reviewedSourceClean: true` (comment CARD-1305 / CARD-0835). Commit `36dfcedf37f80b3437309070d96238f628a2c2f4`.
- N2, expectation changed, not witnessed green: `AgentTaskLandDeliveryE2ETests.cs:150-162` now requires `wakeup-dropped-completion-*.observation.json`. The string entered in `7cba89090d4b36b6430fa0ddd484299a4a158592` (`test(card-1305): S0 red witnesses`). The card's missing `completion-scan-*.observation.json` is no longer what this test asserts.
- N1, still the same tests: `DispatchBaseWarningDeliveryE2ETests.C540_CollapsedWarningsReachIdleCaller` at `DispatchBaseWarningDeliveryE2ETests.cs:18`. `CARD-1359` comments at `:90-91` renew the 60-second quiet window on dispatch-side markers. That is not a Windows pass record.
- N3, still open as a script-surface check: `LandDeliveryFixture.Review.cs:411` requires `review_evidence_sha_mismatch` in the script text. The product throws that code at `LandApproval.cs:110`. `scripts/delegate.ps1:659` still uses `Write-Error` for the general failure path. CARD-1655's unwrapped line is only the bind path (`:649-652`) and `PlainFailure` (`:654-657`).
- N5, still open: this audit found no record that the 27 unrun landing cases were scheduled and finished.

Remaining: a Windows run of N1, N2, N3, and N4, plus the N5 schedule. Size L. Windows-pinned. N5 needs a Windows window.

### CARD-1438

The card asked to trace what the Checkpoints classes spawn and to move any git-only class onto `GitProcessSpawnLimit`. Plan F-2 names three candidates (`docs/superpowers/plans/2026-10-21-card-1421-git-spawn-limit-plan.md:44` and `:163`): `CheckpointConfinementLedgerTests`, `CheckpointOwnedOutputTests`, `CheckpointSelectedOutputTests`. Appendix row also names `CheckpointTrackedBaselineTests`.

Those classes, and `BuildSlotClientTests`, now carry `[ParallelLimiter<CheckpointToolSpawnLimit>]` (`CheckpointConfinementLedgerTests.cs:14`, `CheckpointOwnedOutputTests.cs:10`, `CheckpointSelectedOutputTests.cs:11`, `CheckpointTrackedBaselineTests.cs:12`, `BuildSlotClientTests.cs:11`). The move is `e51d4375a4bc57297bc052ef3734d0d3ee824347` (2026-11-08, CARD-1747: checkpoint-tool lane, width 2). None of them is on `GitProcessSpawnLimit`. Several other Checkpoints classes remain on `ProcessSpawnLimit`, including `RerunFilterHostTests.cs:10`, `CheckpointRepeatFixture.cs:11`, `PhaseJournalKillDiagnosticTests.cs:14`, `CheckpointSourceExecutionTests.cs:13`, `CheckpointTerminalReceiptTests.cs:14`, `CheckpointRepeatBuildIsolationTests.cs:10`, `CheckpointStatePublicationTests.cs:12`, and `CheckpointRepeatHostTests.cs:10`.

Remaining: a trace that either places a git-only Checkpoints class on the git lane or records that none of the named classes is git-only. The tool-lane move is not that trace. Size S.

### CARD-1588

The composer-hint failure is on master. The hotfix SHAs named on the card (`3aac0cdd4`, `98aa524aa`, `f06b5f40d` on `fix/composer-wrapped-rc-hint`) are not the master commits.

- `eb284614b92204eefcbdea93aa28ac396f6524fc` accepts the wrapped `/rc` hint badge.
- `4c4ad5126fe576320661517dbb785b65bafb470b` recognises the weekly-usage and paste-expand rows.
- `ClaudeScreen.cs:127-168`: `MaxHintRowsBelowComposer = 4`; `youveused`+`limit`; `pasteagaintoexpand`; `IsWrappedHintCommandRow` treats a row whose first token is `/rc` or `/effort`.
- Witness `ClaudeDialogObservationTests.cs:37-44` expects `ClaudeDialogState.Clear` for the wrapped `/rc` badge, the weekly usage banner, the pasted-draft hint, and `/rc connecting…`.

Remaining: restart scripts still do not resolve `claude.exe` when the caller PATH lacks the per-user install. A search of `scripts/**/*restart*` found no `claude.exe`. The Torquay Leander `NativeSessionMissing` note on the card (`fe1eec9e`, resume `8c1d3da6`) is an incident record; this audit did not find a separate code defect for it. Size of the remainder S. The PATH follow-up is Windows restart scripts.

## C — still open

### CARD-1293 (M)

(a) `EligibleAt ??= now` at `PipelineHandoffActionService.cs:92` keeps the first eligibility. The unexplained flag (`:208-209`) is only `child is null && ReasonCode == "eligible" && EligibleAt` older than 15 minutes. An overdue `NextObservationAt` is not that flag. (b) This audit did not find a new session-token authorization test or HTTP-level receipt-refusal coverage added for this card. (c) `docs/superpowers/evidence/2026-10-15-card-1281-b2-caller-actions.md` still uses the CP-24 label the card asked to rename. (d) A missing predecessor is `NotFoundException` at `PipelineHandoffActionEndpoints.cs:60-61`, before the recipient check. `GetAsync` throws `child_mismatch` for one bad child (`PipelineHandoffActionService.cs:199-201`) and fails the whole read.

### CARD-1294 (M)

1. `PublishedCardCompletion.ValidateAsync` refuses only Queued, Dispatched, Working, and Blocked (`PublishedCardCompletion.cs:94-97`). A second Code task that succeeded and never landed does not block the close.
2. `CardService.CompletePublishedAsync` marks the workflow run Completed (`CardService.cs:1181-1185`) and does not clear `ActiveWorkflowRunId` (no production assignment clears it). Shadow and disabled return at `:1162-1165` with a null receipt.
3. Shadow qualification reads `PublishedCloseShadowObservations` from settings (`PublishedCardCompletion.cs:42-47`). That is not a durable auto-collected cohort.
4. The same method requires a non-blank adjudicator. It does not compare the adjudicator to the acceptance actor.
5. The published close path above does not call the tracker push that an ordinary column move uses.
6. True lock-contention tests were not added for this card.

### CARD-1300 (S, operator)

`AgentTaskService` runs `PreviewCanCreateAsync` (`:1646`) and `PrepareWorktreeBaseAsync` (`:1648`) before `card_implementation_closed` (`:1745`). The card's first item is an explicit decision: whether closed and admission-changed should be checked first. `C1289_ConcurrentCardEdit` arguments are title, description, owner, token, state, archived, platform, and policy (`PipelineSpeedIntegrationTests.Admission.cs:110-117`). The policy case sets only `CodeVerificationPolicy` (`:140`). Board, identifier, workflow run, and `ReviewVerificationPolicy` are not arguments, though the compared SQL includes both policy columns (`:488-489`).

### CARD-1429 (M)

`AgentTaskLandService` holds `LandRepositoryLeaseScope` across `RunLeasedAsync` and `ReleaseLandRefsAsync` (`AgentTaskLandService.cs:538-546`). The profile log in that same leased region includes push and cleanup seconds (`:559-566`). `git log --grep CARD-1429` on this master hits the CARD-1462 plan commit `bb43ec3529b9d8ad2a799f81080b9e34d89fa9c9` and no land-path commit that moves cleanup outside the lease.

### CARD-1431 (M)

`DispatchHoldDetails.Escalation` still sets `reason={lastHeldDetail}` and appends the ledger afterwards (`DispatchHoldDetails.cs:149-150`). `ClassOf` (`:185-218`) has lease and remote-prep prefixes and no inspection class, so `SiblingInspectionPending` (`:15`) falls through to `DispatchHoldClass.Other` (`:218`). The pipeline maps only Lease and RemotePrep onto `repositoryLease` and `remotePrep`; any other class leaves `awaitingDispatch` (`AgentTaskPipelineStatusService.cs:624-641`). Sibling inspection timing is still `LogDebug`, including `elapsedMs` (`CardSiblingInspectionService.cs:105` and `:124`). The CARD-0672 ledger is the `reason=` plus totals shape the investigation already called insufficient. `git log --grep CARD-1431` hits only the same CARD-1462 plan commit.

### CARD-1437 (S, Windows-pinned)

`AgentTaskLandActiveSourceClaimTests` is still `[ParallelLimiter<ProcessSpawnLimit>]` (`AgentTaskLandActiveSourceClaimTests.cs:22`). The card's gate is a Windows concurrent `mklink` check before the git-lane move. That check is not recorded on this master tip.

### CARD-1439 (S)

The plan's F-3 examples were not dropped. `LandingAdmissionControlTests.cs:8` and `OutputDistillationApplyRaceTests.cs:18` are still `ProcessSpawnLimit`. `BuildSlotClientTests` moved to `CheckpointToolSpawnLimit` under the CARD-1747 commit above; that is a different limiter, not a drop. This audit did not find a pass that removed `ParallelLimiter` from the census files that spawn nothing.

### CARD-1441 (S)

The board column is Review (updated 2026-11-09). On this master tip the runner still sets `gc.auto=0` and `maintenance.auto=false` only inside `if (inspection)` (`RunnerWorkspaceService.cs:1012-1048`). Non-inspection git still sets `GIT_TERMINAL_PROMPT` and `GIT_OPTIONAL_LOCKS` at `:1054-1055` and does not set those two maintenance keys. `git log --grep CARD-1441` on this master is empty.

### CARD-1442 (M)

No `RepositoryMaintenance` type (`git log -S 'class RepositoryMaintenance'` empty). Retirement refs are still created at `LandingGit.cs:1979` (`refs/antiphon/retirement/{id}/`). The probe namespace is still `StartRefAvailability.cs:45` (`refs/antiphon/start-ref-probe`). `LandRefRetention` deletes per land; it is not a report of stale retirement or probe refs. The namespaces still exist, so this is not obsolete.

### CARD-1443 (M)

G7 is still two `merge-base --is-ancestor` calls (`AgentTaskLandSourceResolver.cs:629` and `:632`), not one `rev-list --left-right --count`. G8 still lists worktrees with `["worktree", "list", "--porcelain", "-z"]` (`LandingGit.cs:766`). No `rev-list --left-right` in the land-source resolver.

### CARD-1444 (S, operator)

The per-subcommand histogram is logged (`AgentTaskLandService.cs:559-566`, `histogram={Histogram}`). The card asks for a week of counts after S4 and S5, compared with the ~230 baseline and the 120-140 target. No such record is on this master tip. S5's G7 and G8 are CARD-1443 and are not landed, so the "after S5 deploy" precondition is unmet. The measurement itself is still the card. It needs live land profiles.

### CARD-1445 (M, Windows-pinned)

`ReportValidator` still returns `cleanup_refused` when `CleanupRefusals.Count > 0` (`ReportValidator.cs:34-35`). The delete path still logs `left in place` and continues (`OutputCleanup.cs:96`). No `staticwebassets`, `endpoints.json`, CARD-1445, or CARD-1520 commit subject was found with `git log --grep` on this master. The sharing violation was reported on the Windows node; it was not reproduced in this Linux checkout. The cleanup code still exists, so this is not obsolete.

### CARD-1450 (M)

The card requires an S3 sweep that keeps the latest landing and every Refused pin the way `LandRefRetention` does, and that runs `update-ref --stdin` under `RepositoryChildJournal`. `LandRefRetention` exists (`LandRefRetention.cs:19`) and is the per-land S2 path. No `RepositoryMaintenance` sweep class exists, and `git log --grep CARD-1450` on this master is empty. The journal used by the land batch is not that sweep.

### CARD-1451 (S)

All five nits are still in tree.

1. `docs/superpowers/plans/2026-10-22-card-1432-s2-repair-amendment.md` is still a separate file. The main plan's checkpoints were not extended with the amendment's CP-12..CP-18.
2. V-9 still cites `AgentTaskLandPublicationTests.C448_V26_OriginalAndPreparedPinsSurviveCleanupAndGarbageCollection` and `AgentTaskLandRefusedRetryTests.RR_V5_SettledRefusalIsNotAutomaticallyQueued` (`amendment:39`).
3. CP-15 Expect is still `43 passed` (`amendment:49`).
4. The doc comments are still swapped: `CreatedNoChild`'s summary is closed, then `IsMutating`'s summary sits on `IsMutating` (`LandingGit.cs:695-705`). `CreatedNoChild` at `:705` has no comment of its own. The card's old line numbers 138-145 have drifted; the methods are these lines now.
5. `LandingGitTests.C1432_UpdateRefsBatchRunsUnderTheChildJournal` still polls with `poll.CancelAfter(TimeSpan.FromSeconds(8))` and `await Task.Delay(50, poll.Token)` (`LandingGitTests.cs:1030-1041`). A missing record cancels before `record.ShouldNotBeNull` at `:1045`.

### CARD-1454 (S)

`ChannelReplyDebtState` is only `Attempts`, `CreatedAt`, and `FailureReason` (`ChannelReplyDispatcher.cs:65`). An unmatched turn keys the route with `NewestConversationKeyAsync` at `:254`. Machine-turn publish calls `NewestConversationKeyAsync` again at `:1512`. The route is not stored on the debt row. No two-chat witness (resolved against chat A, published to chat B) was found.

### CARD-1455 (S, operator for ADV-2)

`TryAbandonExhaustedDebtAsync` gives up on attempts, or on `minutes > 0 && Attempts > 0 && now - CreatedAt >= minutes` (`ChannelReplyDispatcher.cs:275-276`). The comment at `:265-267` documents the one-attempt-after-waiting behaviour. Tests that mention `ReplyDebtGiveUpMinutes` only check setting bounds (`ChannelMachineTurnTextTests.cs:524-530`); none ages a debt row past the minute bound. An already-settled abandon logs `LogInformation` at `:301-303` and `:309-311`, not Warning. ADV-2 (count from creation versus from becoming head) is still an undecided product choice.

### CARD-1546 (L)

`LandingGit.cs` is still under `server/Infrastructure/Git/`. The solution's separate git project is `src/Antiphon.GitInProcess/Antiphon.GitInProcess.csproj`, referenced by the server and the session runner. There is no operations csproj for the landing git handler. This is the operator request of 2026-10-23. It is a master-only refactor; the card itself says it must not land on an rc line casually. Planning it does not need a new operator decision. The extraction has not started.

### CARD-1568 (S)

`AuditGitFixture.DeleteOwnedRoot` still requires the parent to equal the temp directory and the name to start with `antiphon-audit-` (`AuditGitFixture.cs:30-32`). Link refusal runs after that check (`RefuseLink` at `:42-46`). Existing cleanup tests cover links. No test was found for a prefix sibling (`root-evil` next to `root`) or for a non-link path outside temp, so removing the parent-equality check would leave the link tests green. An absent root still reaches `File.GetAttributes` inside `RefuseLink` with no documented no-op versus throw. Parent `3c072ab97` landed the helper; the witness did not.

### CARD-1592 (L)

No watcher record type, no `MapPost("/api/watchers")`, and no `scripts/watch.ps1` exist in this tree. Commits whose subjects mention a watcher are rc-qualification and exit-watcher work, not this feature. The related cards named on the body are not a substitute. Size L. It does not need the operator to start.

## D — obsolete

None. The retirement and probe ref namespaces, checkpoint cleanup, `LandingGit`, and the audit-fixture helper all still exist.

## Not done, noted

No fix was designed or implemented. Board columns were left as they were.
