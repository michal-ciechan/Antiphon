# CARD-1082 S7 Final Review (task 15b1cc9c)

Verdict: clean. S7 is tests only (no production file changed, no existing assertion weakened or deleted), and every new or extended test goes red under a method-scoped mutation of the guard it names. The complete ordinary scope for the chain's last slice, CP-11, CP-12, the registry guard and the plan's final After=all rows CP-13, CP-14 and CP-15, reran green at the reviewed SHA as one checkpoint-tool run: 6 rows, 245 results, 0 failed, 0 skipped, one build, every slot granted, source clean, build provenance verified. CARD-1082 is complete against its acceptance text with one plan-chosen residual (D-4, CARD-1109) and the activation restart still owed after land. No new card: the two disclosures below already live on CARD-1132 and CARD-0850.

## Identity

- subjectTaskId (Code owner): `8cddfda9-6d36-44d2-ba37-3971d53d984b`, ref `refs/heads/feat/card-task-8cddfda9`
- Reviewed SHA: `23af749d651aeb528a7416f1db7efa1905f81421` (remote ref equals it; my branch `feat/card-task-15b1cc9c` started at the same SHA)
- Base `80db50749fcead4278c06f1147ef8e0811416bb6` = merge-base = `origin/master` at review time. Range `80db50749..23af749d6`: 3 commits (`da0ab6af3`, `62ae2c679`, `23af749d6`), 5 files, +152/-3: four test files plus the plan's PC-10 expected-red cell. `git diff --stat` confirms no `server/`, `scripts/` or `client/` file.
- Merge onto origin/master: `git merge-base --is-ancestor origin/master HEAD` true (fast-forward); `git merge-tree --write-tree --name-only origin/master HEAD` exit 0, tree `08ecb5d89caef3b61ff41f4dfde507e3c114664b`, zero conflicting hunks.
- Platform: `GET /api/runner-defaults` globalRunnerId=server2; `GET /api/session-runners` desktop (windows), server2 (linux), server2-temp (linux), all available. Ran on the Linux mirror; no `-Runner`/`-Platform` pin needed for the next stage.
- Evidence guard: `scripts/check-evidence-diff.ps1 -BaseRef 80db50749 -HeadRef 23af749d6` -> `EVIDENCE result commits=3 entries=0 violations=0`.

## Checkpoint rerun (one checkpoint-tool run, hand-made six-row manifest, `--serial --keep-outputs`)

Run `20261007-034413-f66b`; driver built via `scripts/build-slot.ps1` into `bin-c1082-s7drv/` (lease fba1429b, 9 s). Manifest: scratchpad `c1082-s7-review-checkpoints.md`. CP-1/CP-2 are the plan's CP-11/CP-12, CP-3 the registry guard, CP-4/CP-5/CP-6 the plan's CP-13/CP-14/CP-15 (identical filters, Min and Expect). `validate --evidence report.json --expected-source-sha 23af749d6...` -> `CHECKPOINT SOURCE VALID source=23af749d651aeb528a7416f1db7efa1905f81421 rows=6`. Wall 14m26s, build 263 s.

```
CHECKPOINT CP-1 commit=23af749d651aeb528a7416f1db7efa1905f81421 build=ok filter=/*/*/(ReviewEvidenceResettlementTests*)|(SettlementSyncDebtLandingTests*)/* executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-15b1cc9c/.antiphon/checkpoints/20261007-034413-f66b/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=23af749d651aeb528a7416f1db7efa1905f81421 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=23af749d651aeb528a7416f1db7efa1905f81421 build=reused filter=/*/*/ReviewEvidenceRecoveryTests/* executed=21 passed=21 failed=0 skipped=0 trx=/work/worktrees/task-15b1cc9c/.antiphon/checkpoints/20261007-034413-f66b/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=23af749d651aeb528a7416f1db7efa1905f81421 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=23af749d651aeb528a7416f1db7efa1905f81421 build=reused filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-15b1cc9c/.antiphon/checkpoints/20261007-034413-f66b/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=23af749d651aeb528a7416f1db7efa1905f81421 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=23af749d651aeb528a7416f1db7efa1905f81421 build=reused filter=/*/*/(RunnerTaskSettlementTests*)|(ReviewEvidenceResettlementTests*)|(SettlementSyncDebtLandingTests*)|(ReviewEvidenceRecoveryTests*)/* executed=63 passed=63 failed=0 skipped=0 trx=/work/worktrees/task-15b1cc9c/.antiphon/checkpoints/20261007-034413-f66b/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=23af749d651aeb528a7416f1db7efa1905f81421 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=23af749d651aeb528a7416f1db7efa1905f81421 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(BlockedTaskParkDeliveryTests*)|(BlockedTaskSyncRecoveryTests*)|(SettlementSyncRecoveryTests*)/* executed=55 passed=55 failed=0 skipped=0 trx=/work/worktrees/task-15b1cc9c/.antiphon/checkpoints/20261007-034413-f66b/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=23af749d651aeb528a7416f1db7efa1905f81421 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=23af749d651aeb528a7416f1db7efa1905f81421 build=reused filter=/*/*/(SettlementSyncDebtPolicyTests*)|(DelegationLeaseSettingsTests*)|(RunnerSettlementSyncTests*)|(TaskProgressGitTests*)|(RunnerCompletionProgressTests*)|(SettlementSyncDebtSchemaTests*)|(RunnerBranchContractDocumentationTests*)/* executed=92 passed=92 failed=0 skipped=0 trx=/work/worktrees/task-15b1cc9c/.antiphon/checkpoints/20261007-034413-f66b/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=23af749d651aeb528a7416f1db7efa1905f81421 sourceState=clean buildSource=verified
unlisted: none (the tool ran no other build or test command)
verdict: GREEN exit=0
```

TRX rosters equal the plan's expected sets exactly: CP-1 ReviewEvidenceResettlementTests 10 + SettlementSyncDebtLandingTests 1; CP-2 ReviewEvidenceRecoveryTests 21; CP-3 TestClassificationGuardTests 1 + SlowTestTripwireTests 2; CP-4 31 + 10 + 1 + 21 = 63 (59 distinct methods); CP-5 TerminalRunnerSeatReleaseTests 39 + BlockedTaskParkDeliveryTests 4 + BlockedTaskSyncRecoveryTests 2 + SettlementSyncRecoveryTests 10 = 55; CP-6 SettlementSyncDebtPolicyTests 21 + DelegationLeaseSettingsTests 14 + RunnerSettlementSyncTests 29 + TaskProgressGitTests 11 + RunnerCompletionProgressTests 11 + SettlementSyncDebtSchemaTests 1 + RunnerBranchContractDocumentationTests 5 = 92. The five S7-relevant methods (V-27, V-28, V-29, V-20, V-21) are present and Passed in their rows. Tool advisories `SLOW CLASS TerminalRunnerSeatReleaseTests 1369s` and `BlockedTaskParkDeliveryTests 81s` are cumulative per-test time (CP-5 hostWall 194 s); pre-existing, not S7.

## Code report audit (owner 8cddfda9)

- CP-11 11/11 and CP-12 21/21 on run `20261007-032520-e545` (`run --plan ... --rows CP-11,CP-12 --expected-source-sha 23af749d6`) match the plan rows (filter, Expect, Min, Covers V-27/V-29/R-3 and V-28); `unlisted: none`; slot granted on build and both rows; `validate --evidence <its report.json> --expected-source-sha 23af749d6` -> `SOURCE VALID rows=2` reproduced here. The run folder and the report file survive at `/work/worktrees/task-8cddfda9/.antiphon/`; the report file is uncommitted (as in S5/S6) and the API `.result` carries the summary.
- Every driver was leased: the session transcript shows 8 `build-slot.ps1 -Label` strings (proof-build x2, v27-green x3, green-rest, mutation-red, registry-guard, checkpoint-bootstrap) and the red/green/mutation loops are PowerShell scripts run INSIDE one lease (`dotnet build ... ; foreach ... dotnet run`), not shell-chained after it; the transcript's only "unleased" mention is "No unleased drivers remain". The registry guard is an unlisted leased build+run with its reason stated (new Integration class, not Slow, not allowlisted); CP-3 re-executes it here.
- Checks (c): no production file in the range; no pre-existing assertion weakened or deleted. The `C1043_StoredSyncRequired` change adds a loop arm; the six existing arms still go through `RefusedAsync`, and the Pending arm asserts the EXACT code plus `UnchangedAsync` (stricter than `RefusedAsync`). V-20/V-21 gain assertions only. V-27 and V-29 are new methods.
- New tests can go red: proved below, not by reading.
- Tool note (CARD-0850, existing): the Code's checkpoint report says `builds: 9` and `outputs: deleted bin-c1082-cp1/ ... bin-c1082-final/` under `--rows CP-11,CP-12`; PHASES shows one executed build (CP-11 109 s, CP-12 0 s). Cosmetic, the tool counts manifest builds.

## Mutation probes (kept build `bin-c1082-s7rev/`, four files batched into one 97 s incremental build, one method-scoped leased detector each, all reverted)

| Probe | Mutation (production, reverted) | Detector | Result |
|---|---|---|---|
| M1 (G-15/PC-15) | `ReviewEvidenceBindingService.PrepareRepairAsync`: a Pending result whose `RemoteSha` equals the reviewed SHA is rewritten to Synchronized with that SHA as `DesktopAfterSha` before the sync-witness block | `ReviewEvidenceResettlementTests.C1082_PendingContinuationRefusesStrictRebind` (lease fad40479) | RED 1/1: `w.Task.NextStage` should be Decide (the bound row replaced the unbound one and the handoff became land) |
| M2 (G-16/PC-16) | `ReviewEvidenceRecoveryService`: stored-state disjunction gains `or Pending` | `ReviewEvidenceRecoveryTests.C1043_StoredSyncRequired` (lease 8d7d0656) | RED 1/1 on the Pending arm's `.Code` assertion (recovery no longer refuses `stored_sync_unconfirmed`; the refusal moves to the binding service's code) |
| M3 (G-12/PC-12) | `LandApproval.LoadUsableEvidenceAsync`: throw `pending_sync_refused` when the subject's stored `RemoteSync.State` is Pending | `SettlementSyncDebtLandingTests.C1082_PendingSyncOwnerLandsOnPushedBranch` (lease 37ab2dc5) | RED 1/1: `ConflictException: Desktop sync is pending.` at `LandApproval.cs:line 82`, thrown from the land request before any queue claim, so the land path is hit and the current code's `Landed` is the required result |
| M4 (G-10/PC-10, CARD-1132) | `AgentTaskReplyService.MergeBackAsync`: the "left for review" early return also requires `State != Pending`, so Pending falls through to `TryMergeBackAsync` | `RunnerTaskSettlementTests.C1082_CodeLeaseBusyWithLocalObjectsSettlesSucceededPending` (lease 942eca90) | RED 1/1 on `pendingEvents` (a Failed "Merge-back failed: repository_busy" event appears) |
| M4 alone | same | `RunnerTaskSettlementTests.C1082_ReviewLeaseBusySettlesSucceededWithPendingSyncDebt` (lease b42b62cd) | GREEN 1/1: a Review with `S == b` evaluates NoAttributedProgress, `AllowsAutomaticWorkspaceMutation` is false, so settlement never calls `MergeBackAsync`; V-20's pin cannot see the PC-10 defect |
| M4 + M5 | M5: `TaskCompletionProgressService.AllowsAutomaticWorkspaceMutation` returns true for a non-ProgressObserved assessment (second 102 s build) | the same V-20 detector (lease cdbde597) | RED 1/1 on `pendingEvents`: the pin is a real predicate over the live event list and fails as soon as the Review path reaches the merge gate |

Green half: the main run at the clean SHA before the mutations. After `git checkout -- server/`: `git status --short --untracked-files=all` empty, `git diff` empty, HEAD `23af749d6`.

## Hard checks (a)-(e)

(a) Done above: V-27, V-28, V-29 and V-21 each go red under the mutation of the exact guard they name; V-20 goes red once its path reaches the merge gate.

(b) V-29's accepted arm is a genuine land: `LandingSafetyHarness` seeds a Succeeded Worktree owner, `AddSourceAsync` commits a real feature on the source branch and the test pushes it to the fixture origin; the owner's `CompletionProgressEvidenceJson` carries `RemoteSync{State=Pending, ObservedSha=reviewed, ConfirmedSha=null, Reason=runner_sync_lease_busy}`; a Clean Final/Full Review `StageOutcome` with `ReviewedSourceClean=true`, `ReviewedSourceSha=reviewed`, `ReviewedSourceRef=refs/heads/<owner branch>` and `SubjectTaskId=owner` is inserted; `RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId)` returns `queued`, `RunQueuedAsync` returns `Complete`, and the assertions require `Publication=Landed` (set only by `AgentTaskLandingProtocol` after `PushExitCode == 0`), the terminal `Landed` event, `ReviewEvidenceId` and `OriginalSourceSha` on the operation, and the stored sync still Pending with `ConfirmedSha` null. `LandApproval.cs` (read in full) never reads `RemoteSync` or the evidence JSON: approval is the Clean/clean-source/Final-Full/subject/SHA/ref/repository/not-superseded chain on the StageOutcome, so Pending adds no approval and no refusal (D-6, G-12). Refused arms refuse for the stated code: V-28's Pending arm keeps `ConfirmedSha`, `MirrorDirty=false` and the matching `FullRef` so only the state predicate can fail, and the assertion is the exact `review_evidence_rebind_stored_sync_unconfirmed`; V-27's handoff prefix is `review_evidence_sync_unconfirmed`, the state/DesktopAfterSha predicate at `ReviewEvidenceBindingService.cs:94-98`, and M1 shows the later mirror/ref predicates pass for that result (the handoff became land, not another refusal).

(c) No production change; no assertion weakened (above). The plan edit is the PC-10 expected-red cell only; table counts, filters and After tokens unchanged (`import --plan` not rerun; the tool parsed the table in the Code's run and mine uses the same filters).

(d) The V-20/V-21 event assertion is `pendingEvents.ShouldNotContain(e => e.Type == Failed || e.Type == Merged)` plus `ShouldNotContain(e => e.Detail.StartsWith("Merge-back failed"))` over `world.EventsAsync()`; V-21 red under M4 and V-20 red under M4+M5 prove it fails when such an event is emitted. V-22 (Failed Code settlement) needs no pin: the merge block runs only for `Status == Succeeded`.

(e) CARD-1082 acceptance, item by item:
- `progressEvidence.remoteSync = Pending` with the observed/pushed SHA and the lease reason; settle on the verdict; seat released through the ordinary Succeeded path: met (S3/S5; V-20, V-21, V-22; green in CP-4).
- The reconcile job (or the next land) retries the desktop fast-forward and `-Land` is not refused by a pending sync: met (S4b sweep V-14..V-19, V-23 in CP-4/CP-5; V-29 in CP-1/CP-4 with M3 proving the land path is exercised).
- Completion note says "synced later", never "repair it, then reply"; no Reply owed: met (V-5, V-20 warning, V-21 note `ShouldNotContain("then reply")`, V-26 docs; green in CP-4/CP-6).
- Keep Blocked only when the runner cannot prove the branch is pushed: met for dirty/diverged/sequencer/identity refusals (R-1) and for every Review, Plan, no-push and fetched-Code case. Residual: a Code task whose pushed objects were never fetched during the budget still ends Blocked by plan decision D-4 (Indeterminate progress, not a push-proof failure). That is fail-closed, measured after activation per the plan, and tracked as CARD-1109. Not a safety gap.
- Open cards are not blockers: CARD-1113 (policy input pins; hardening 3 met by S5 call-site guards, 1-2 unreachable fail-opens per the S5 review), CARD-1115 (baseline remote pin gating, fail-closed), CARD-1119 (claim-rule dedupe, reason fidelity), CARD-1125 (untested `RemoveMirrorAsync` Pending fallback; the fallback publishes a server-observed origin tip, benign either way), CARD-1132 remainder (item 3 below), CARD-1133 (Blocked lease-busy evidence labelled Pending, verdicts unchanged), CARD-1136 (Held attention never clears; Code-report process). None is a reachable fail-open shown here.
- Activation: the behaviour goes live only with the AppHost restart that picks up S5-S7 (plan Activation); every Code report in the chain says `Restart: none`. After landing 8cddfda9 the orchestrator must restart the AppHost and confirm `GET /api/version`, then settle or land any task currently Blocked on `runner_sync_lease_busy` through today's reply path first.

## Disclosures (existing cards, no new card)

1. Merge-path Pending workspace-note composition (CARD-1132 item 3) stays open and is a disclosure, not a defect: for a Pending result `MergeBackAsync` returns "branch X left for review" at its first gate before any merge command, so S5's note override replaces that same text with "branch X left for review; source S (desktop-sync=pending)" and hides nothing today. The event pin (V-21) now catches the only way a merge outcome could be hidden. V-20's pin is an invariant on the Review path, not a PC-10 detector; the plan's PC-10 row names V-21 only, which is correct.
2. Checkpoint tool `builds:`/`outputs: deleted` lines count manifest builds under `--rows` (CARD-0850, existing).

## Pending outside this round

PC-1..PC-16 stay pending for method-scoped SourceLanding Mutation (these review probes do not discharge them). No S8. Land 8cddfda9 at `23af749d651aeb528a7416f1db7efa1905f81421` with this evidence, then restart the AppHost (activation), then Mutation.

## Cleanup

`clean --run 20261007-034413-f66b` deleted every kept `bin-c1082-s7rev/`; the driver `tools/Antiphon.Checkpoints/bin-c1082-s7drv` removed by a root-confined rm; `find -name 'bin-c1082*'` empty; all five mutations reverted; tree clean at 23af749d6 before this report commit. Evidence (TRX, report.md/json, executor.log) stays ignored under `.antiphon/checkpoints/20261007-034413-f66b/`; probe TRX under the scratchpad `mut/`.

--- review evidence ---
subjectTaskId: 8cddfda9-6d36-44d2-ba37-3971d53d984b
reviewedSourceSha: 23af749d651aeb528a7416f1db7efa1905f81421
reviewedSourceClean: true
ordinaryScopeCompleted: Full
