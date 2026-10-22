# Windows rc groups 1-11: five failures and an undersized group-10 budget

Debug `79bfbadb`, 2026-10-22. Group 10 exhausted its 60-minute process watchdog while still making progress; the evidence does **not** show a repeated set of hung tests. Groups 1-11 contain four TRX failures **plus one completed group-10 assertion failure** that the missing TRX hides. Three fixture defects are now Backlog cards CARD-1510/1511/1512. The two PTY failures have plausible timing mechanisms but unproven underlying causes.

Full evidence is preserved in C:\Antiphon\worktrees\card-task-79bfbadb\.antiphon\task-79bfbadb.md. This was file-based investigation, with the explicitly requested board searches/writes. No test/build/process probe or source repair was run. The live qualification was not touched. Only reports, ignored card descriptions and the requested cards were written; no commit/push under the READ-ONLY brief.

## Evidence coordinates

- Candidate/source: `0714a9a89e4bff65aae7e0b4b94098694aeb931c`; verified release-checkout HEAD matches. Source references below are relative to `C:\Antiphon\releases\checkout`, not this task's newer HEAD `3f54023d6b715fe2653014dc82187d358c0193b6`.
- **R** = `C:\Antiphon\releases\candidates\rc-2026-10-20-0714a9a89e4b\logs\2026-10-22-1606-d2777e21` (run `d2777e21ef4c4a3e9b1fb315ff8f5602`). **D10** = `R\antiphon-all-group-10-execution\log_261022164232613.diag`.
- **P** = `C:\Antiphon\releases\candidates\rc-2026-10-20-ceb6e3d6b784\logs\2026-10-22-0157-27a1b370`. Previous census read directly from `feat/card-task-77c15d47:docs/investigations/2026-10-22-rc-windows-qualification-interim-reds.md`; its G and H/row 11 recur here.
- Times use the diagnostic line's UTC clock. Windows civil time was UTC+1. No inference about an await stack is possible from a test-state event alone.

## Census through group 11

Every completed group's counters come from `R\antiphon-all-group-N.trx` at the line listed. No skips in these ten TRX files.

| Group | Total in TRX | Passed | Failed | Counter line |
|---|---:|---:|---:|---:|
| 1 | 76 | 76 | 0 | 2829 |
| 2 | 414 | 414 | 0 | 6540 |
| 3 | 76 | 74 | 2 | 1585 |
| 4 | 22 | 22 | 0 | 354 |
| 5 | 270 | 269 | 1 | 4428 |
| 6 | 44 | 44 | 0 | 3631 |
| 7 | 136 | 136 | 0 | 2281 |
| 8 | 16 | 16 | 0 | 371 |
| 9 | 123 | 123 | 0 | 2518 |
| 10 | **No TRX; 158 discovered/selected** | **141 diagnostic passes** | **1 completed failure** | D10:792-821,1033-1036 |
| 11 | 120 | 119 | 1 | 2533 |

TRX totals: **1,297 executed, 1,293 passed, 4 failed**. Group 10 separately has 142 completed results, one in progress and 15 unstarted nodes. Its diagnostic passes are observations, not complete qualification coverage. Group 9's six previous-census reds are absent; the three previous group-11 boundary reds are absent. Later groups are outside this census.

### Every failure and attribution

1. **Group 3 - DirectSessionRunnerClientDisposalTests.Dispose_is_idempotent**, 8.203 s. Recursive fixture deletion gets UnauthorizedAccessException for OpenConsole.exe (R/antiphon-all-group-3.trx:71,107). Idempotency and host/child/console exit assertions already passed; console exit is witnessed at :104. The fixture immediately releases handles and deletes its root (tests/Antiphon.Tests/Agents/DirectSessionRunnerClientDisposalTests.cs:127-141,363-367). **New occurrence relative to P; a transient image/file-handle release or external access under load is plausible, unproven.** No blocking owner or ACL cause is recorded. P passed the unchanged test in 3.037 s. CARD-1020 owns the original cleanup contract. Instrument an exact-method recurrence before choosing a repair; do not silently add retries.


2. **Group 3 - ClaudeAdapterEffortPromptTests.Local_adapter_keeps_requested_effort_and_probes_after_clearance(xhigh)**, 8.198 s. WaitForReadyAsync returns false after effort selection succeeded; the screen shows xhigh and a repeated/partial token still in the composer (R/antiphon-all-group-3.trx:448 and following error; test :85-86). **New occurrence, plausibly PTY scheduling/fixture throughput under load; exact probe outcome unrecorded.** Settings use 7,000 ms ready/effort budgets, 1,000 ms retype and 500 ms clear (RunnerClaudeAdapterEffortPromptTests.cs:17-21); the peer writes JSON and repaints per key (Agents/Fixtures/claude-effort-dialog.ps1:20-39). Probe appearance/clear logic is src/Antiphon.Agents.Pty/ComposerInputProbe.cs:125-164. P passed this unchanged variant in 4.012 s; the high sibling passes here. CARD-0791 covers related, different runner methods. Preserve probe outcome/peer trace on an exact-method rerun; a budget increase is not yet justified.

3. **Group 5 - RunnerProcessProbeTests.Inherited_path_over_the_ceiling_is_bounded_on_an_entry_boundary_and_the_probe_runs**, 0.583 s. Probe succeeds; exact PATH comparison fails with an additional leading WindowsApps/Microsoft.PowerShell_8wekyb3d8bbwe entry, followed by the expected entries (R/antiphon-all-group-5.trx:221). Test tests/Antiphon.Tests/AgentTui/RunnerProcessProbeTests.cs:124-130 discounts only an exact PSHOME prefix. **New environmental fixture failure, not memory-induced or CARD-1414's earlier product refusal.** P passed the unchanged test in 0.493 s. Launcher/alias mutation fits; executable identity was not logged. Use a fixture-owned environment reporter, or narrowly account for proven launcher mutation while preserving exact bounds/order. **Filed CARD-1510.**

4. **Group 10 — `AgentTaskLandPublicationTests.C448_V04_IgnoredFilesSurviveConfirmedPublication(.antiphon/inbox/photo.png)`**, 14.277 s, completed 17:28:10Z. Expected `ignored_content_preserved`, got `evidence_retention_unavailable` (`R\antiphon-all-group-10-tests.log:4-9`; D10:792-821). **New deterministic test-contract mismatch on this rc**, independently of the watchdog. Between P and this candidate, CARD-1446 commit `d366b0f5fe8101bb01bdc95e6c2d6b311073eb4d` added `**/.antiphon/**` retained evidence (`server/Application/Settings/WorktreeCleanupSettings.cs:20-27`). The image is now evidence; `.antiphon/report.md` and `.claude/settings.local.json` remain protected. The harness registers `RefusingEvidenceRetention` (`tests/Antiphon.Tests/TestHelpers/DelegationTestServices.cs:92`), whose nonempty-evidence result is precisely the observed reason (`server/Application/Interfaces/IWorktreeEvidenceRetention.cs:29-34`). Production registers the real retention implementation (`server/Program.cs:462`). The stale uniform assertion is at `AgentTaskLandPublicationTests.cs:420-437`. Split protected and retained cases, using fixture-owned real retention and byte verification for the image. **Filed CARD-1512.** This is not evidence that production lost the file.


5. **Group 11 — `AgentTaskLandRemovalMatrixTests.C448_V18_LowLevelRemovalRechecksAtBothContentBoundaries(2, submodule)`**, 23.659 s. `fired` is false at test line 101 (`R\antiphon-all-group-11.trx:84-101`). **Known census H/row 11, now explained as a deterministic fixture mismatch rather than the earlier timing conjecture.** The test pre-creates the submodule (`tests/Antiphon.Tests/Application/AgentTaskLandRemovalMatrixTests.cs:48-51`) and waits for the second source status call (`:69`). `GuardedWorktreeRemoval.Classify` adds existing nested repositories to Protected (`server/Infrastructure/Git/GuardedWorktreeRemoval.cs:423-431`); `WorktreeNestedRepositories.cs:20-23` recognizes `nested/.git`. Consequently the first reading refuses at `GuardedWorktreeRemoval.cs:113-115`, before the second reading at `:129`. The first-reading variant can fire; the second cannot. All other 23 variants passed here. Preserve the early-refusal assertion; test a newly introduced repository at boundary two if that race must be covered. Do not remove nested-repository protection. **Filed CARD-1511.**

## Why group 10 exceeds the watchdog

D10:10 identifies exactly three classes: `AgentTaskLandCheckpointMatrixTests` (43 nodes), `AgentTaskLandPersistenceFailureTests` (52), and `AgentTaskLandPublicationTests` (63), matching `R\antiphon-profile.discovery.json`. These real-git integration tests take the one-at-a-time process-spawn lane. The process begins at 16:42:32Z (D10:1); group 11 starts at 17:42:39Z. The configured per-process cap is 3,600,000 ms (`tests/test-execution-policy.json:805`, passed by `scripts/lib/nightly-tests-impl.ps1:1069`). The cap interrupts the remaining run before TRX generation.

Only **`AgentTaskLandPublicationTests.C688_Repair_AlreadyCanonicalRecordsLocalIdentity(True)`** remains InProgress without a terminal result (D10:1036, started 17:42:09.532Z), about 29 seconds before the process ends. Immediately before it, `C688_LocalMasterBehindOriginRebasesOntoOrigin` passed in 36.193 s (D10:1033). The longest completed node took 47.631 s (D10:300). Completed counts in successive ten-minute bins are **24, 28, 21, 21, 31, 17**: no hour-long silent interval.

The unfinished method initializes a private git/DB harness, commits, merges/pushes to a private local remote, optionally detaches, then awaits the land protocol (`AgentTaskLandPublicationTests.cs:754-764`). Its exact instruction/await at the kill is **not recorded**. It has no PTY and does not run a real verifier/build: `LandingSafetyHarness.cs:70,432-443` injects `ControlledVerifier`, with no barrier set in this method. There is no evidence it waited for the host build slot, a lock, or a deadlocked await. The 15 unstarted nodes had not been admitted behind the serial lane. A per-test timeout is not the demonstrated remedy.

The policy estimates this entire group at exactly **900 s**: 43 Slow Land nodes x 8 s + 52 non-Slow Land nodes x 1 s + 63 Slow Land nodes x 8 s. These classes have no measured overrides in `scripts/lib/nightly-class-costs.json`. The fallback weights and 900 s packing cap are at `nightly-tests-impl.ps1:123-138,169-178,218-223`.

| Class | Policy seconds | Completed here: nodes / summed seconds | Previous P: nodes / summed seconds |
|---|---:|---:|---:|
| CheckpointMatrix | 344 | 43 / 933.34 | 43 / 1549.84 |
| PersistenceFailure | 52 | 52 / 1473.78 | 52 / 1938.05 |
| Publication | 504 | 47 / 1144.27 (partial) | 2 / 68.54 (partial) |

The first two classes alone used about 40.1 minutes here and 58.1 minutes previously. Publication has 16 nodes without a completed result here. A naive equal-cost extrapolation gives roughly 66 minutes for this group here; it is an estimate, not a finish guarantee. The missing Slow category makes PersistenceFailure particularly underpriced, but adding that category alone still underestimates it materially.

**Smallest fix: policy/cost data, under CARD-1404 (Review) and CARD-1410 (Done).** Add conservative measured Windows costs for these three classes; keep the 60-minute cap and let the existing packer put them in separate launches, using method splits if a measured class approaches its 2,400 s threshold. Current complete-class measurements and P's slower measurements already support this correction; a partial Publication sample must be labelled partial, never presented as a complete TRX. Final calibration should use a complete separately bounded Publication run. Do not raise all watchdogs or add timeouts to the test that happened to be current at minute 60.

## Earlier gates: no repeated unfinished set

I parsed complete multiline TestNodeUpdateMessage records, keyed by UID and retaining the final state, including `ErrorTestNodeStateProperty` (the assertion-failure state). This parser reproduces current group 3's TRX census exactly (74 passed, 2 failed). Looking only at lines containing UID loses terminal states following multiline captured stdout.

Full historical paths, counts and line references are in the expanded report at C:\Antiphon\worktrees\card-task-79bfbadb\.antiphon\task-79bfbadb.md. Complete multiline parsing gives these final pass/error/skip/InProgress counts and sole unfinished nodes:

| Run | Counts | Sole unfinished node |
|---|---|---|
| Oct 18 42d2c784 | 2672/72/4/1 | C494_GuardsAreScopedToTheirOwnOperation(valid-first) |
| Oct 18 d081f742 | 2697/69/4/1 | C1211_NestedTrackedHeadKeepsExistingAdmission(True) |
| Oct 20 66f84ee4 | 2210/63/4/1 | C753_SupersededConflictedOperationGetsFreshReviewedPublication |
| Oct 21 0f43c3e5 group 10, different filter | 348/0/0/1 | C448_V18_CleanupRetryPreservesChangedWork(ignored) |
| P, same group-10 classes | 97/0/0/1 | C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance(directory, False) |

The old watchdog-sizing report's persistent Antiphon.Tests set is contradicted by terminal states after multiline stdout. In the second gate's antiphon-all-execution/log_261018233306240.diag: effort-dialog An_effort_dialog_that_never_clears_fails_within_its_budget passes in 0.523 s (3096-3098); Dispose_is_idempotent passes in 4.099 s (6306-6370); KeepSix a_second_tick_while_that_message_is_pending_enqueues_nothing passes in 3.291 s (25301-25549). All six disposal and eleven KeepSixReminder methods pass in both Oct 18 logs. The old report's session-runner/E2E interpretation was not re-audited.

## Memory and handoff

The supplied approximately 2.4 GB free / 5 GB VBCSCompiler snapshot plausibly amplifies process/filesystem/PTY latency, but is not timestamped causal proof. Group 10 kept progressing (C:\Antiphon\releases\task-2a34ba8c\supervisor-console.log:7-12, idleMin=0) and completed 142 nodes versus P's 97. Memory does not explain the three deterministic fixture failures or prove a deadlock. CARD-1461 covers real-verifier memory-floor starvation; this group uses a controlled verifier.
Board searches used card.ps1 search with -Board Antiphon -All for exact methods/classes and related symptoms; candidate descriptions were read in full. Filed CARD-1510 (PATH fixture), CARD-1511 (submodule boundary), CARD-1512 (evidence-policy fixture), all Backlog. CARD-0838 is an older broad load census; CARD-0971/1211 own nested protection, not the stale test.

The next work is a policy/cost correction under CARD-1404/1410, the three test repairs, and narrowly instrumented follow-up of the two group-3 methods. Keep this run's missing-TRX/failed-coverage result intact. No green gate, exact internal await, memory causation, or quiet-host rerun is claimed.

--- next stage ---
next: plan
handoff: Use CARD-1404/1410 for measured Windows group costs and CARD-1510/1511/1512 for the fixture repairs. Commission exact-method PTY diagnosis separately; do not interrupt the live qualification or infer a repeated hang from the older single-line census.
artifact: C:\Antiphon\worktrees\card-task-79bfbadb\docs\investigations\2026-10-22-rc-group-10-timeout-debug.md
