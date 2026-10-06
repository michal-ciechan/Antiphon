# CARD-1073 Final Review (task a36c6c6e)

Outcome: no defect in CARD-1073. The projected first-match receipt scan is equivalent to the old full scan, the projection is real, ordering is the same first-by-Sequence rule the old code used, and nothing outside the receipt read changed. Every red result in the rerun is INHERITED at the base commit and is now on a Backlog card.

- subjectTaskId: 5ff9ee20-17e0-442b-82b7-6e27f00d6e12 (Code owner; carried as the landing owner)
- reviewed: refs/heads/feat/card-task-5ff9ee20 at cc03bb73b38d0cccd67091768f10f8ce9992fb77, base def23ff2c5c17dbd9f7aaf83d5c8c49d5268488a (merge-base with origin/master confirmed)
- review worktree: /work/worktrees/task-a36c6c6e on feat/card-task-a36c6c6e (same tree as cc03bb73b; no source edits; this report is the only commit)
- platform: GET /api/runner-defaults globalRunnerId=server2; GET /api/session-runners lists one windows and two linux runners; no -Runner or -Platform pin needed.

## Checkpoint compliance of the Code report

The Code task had no plan and no `### Checkpoints` table (its report states "No plan file. No V-n/R-n table. No PC rows."), so the plan-table comparison is vacuous; the Code brief's required filter stands in. Against that filter the Code report is complete: red (12 total, 11 passed, 1 failed on the projection assertion) then green (12/12) for the C1073 methods, one required-filter run (100 executed, 99 passed, 1 failed) with per-class counts, one explained extra run (the base diagnostic for the inherited red), slot leases on every build and run, evidence guard commits=4 entries=0 violations=0. The new test can go red: `C1073_LowFloorScanProjectsSequenceAndTextAndStopsAtTheFirstMatch` failed on the old shape because the SELECT still carried `ToolInput`, and `C1073_ReceiptDecisionMatchesTheInMemoryScan` asserts hard-coded expected sequences per shape, not a self-compare. PCs: none exist; nothing to discharge.

## Rerun (one checkpoint-tool run, one build, serial)

Run `.antiphon/checkpoints/20261006-204403-bbb6`, manifest `/tmp/.../scratchpad/c1073-review-manifest.md` (outside the tree), `start --plan ... --serial --expected-source-sha cc03bb73b... --baseline def23ff2c...`. `source: cc03bb73b38d0cccd67091768f10f8ce9992fb77 state=clean buildSource=verified`; build 104.5 s into `bin-c1073rv/`, `UseAppHost=false`, every row `slot=granted`, wall 27m30s. `validate --rows CP-1,CP-3..CP-12,CP-14` -> `CHECKPOINT SOURCE VALID source=cc03bb73b... rows=12`.

| CP | Filter | executed | passed | failed | why selected |
|---|---|---:|---:|---:|---|
| CP-1 | `(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)` | 3 | 3 | 0 | registry guard (required) |
| CP-2 | `AgentTaskLandReceiptTests*` | 42 | 41 | 1 | required; all 12 C1073 results executed and passed; red = C467_V13(confirmed) INHERITED |
| CP-3 | `AgentTaskLandNotificationPersistenceTests*` | 12 | 12 | 0 | required |
| CP-4 | `AgentTaskLandNotificationRecoveryTests*` | 43 | 43 | 0 | required; hosted service |
| CP-5 | `AgentTaskLandQueuedReceiptTests*` | 22 | 22 | 0 | two-kind set (a) |
| CP-6 | `AgentTaskLandQueueAgingTests*` | 9 | 9 | 0 | aging (d) |
| CP-7 | `DataRetentionServiceTests*` | 73 | 73 | 0 | retention (d, e) |
| CP-8 | `ChannelOutboundRetentionTests*` | 6 | 6 | 0 | CARD-0519 S10 policy witnesses (e) |
| CP-9 | `ExpectationNoteDebtTests*` | 7 | 7 | 0 | other LandNoteReceipt consumer |
| CP-10 | `ReceiptFailureDeliveryTests*` | 21 | 21 | 0 | outcome notes (d) |
| CP-11 | `LegacyCheckNotePublicationTests*` | 17 | 17 | 0 | legacy one-kind set (a) |
| CP-12 | `AgentTaskLandMonitoringTests*` | 25 | 25 | 0 | land monitoring (d) |
| CP-13 | `PostLandMutationDeliveryTests*` | 67 | 51 | 16 | TaskCompletion rendering path; all 16 INHERITED |
| CP-14 | `VerificationRoundDeliveryTests*` | 20 | 20 | 0 | completion notes end to end (d) |
| CP-15 | `DispatchBaseNotificationTests*` | 42 | 38 | 4 | DispatchBase one-kind set; all 4 INHERITED |

Totals: 409 executed, 388 passed, 21 failed, 0 skipped; 12 rows green, 3 red. Baseline stage: `git worktree add` at def23ff2c..., one `baseline build`, one method-scoped `baseline run` per red row, `git worktree remove`, each with its own `BUILD SLOT granted` line; base results CP-2 5/4/1, CP-13 21/5/16, CP-15 9/5/4 with identical messages (`baseline/classification.txt`). Not selected and why: `CheckCompactionReceiptTests` (its full-entity copy of shape 1 in CheckCompactionContinuationService is untouched), the remaining Slow delivery classes (ReviewEvidence/WorktreeLandingCleanupRetry/AgentTaskWorktreeLockOutcome exercise the same `ReconcileAsync` entry already covered by CP-13/CP-14), and no whole-Unit lane per the brief.

Other runs: tool bootstrap build (`bin-c1073rvtool/`, slot b9aa1573, 9 s); `scripts/check-evidence-diff.ps1 -BaseRef def23ff2c... -HeadRef HEAD` -> commits=4 entries=0 violations=0 exit 0 (no build). Cleanup: 28 `bin-c1073rv`/`bin-c1073rvtool` directories removed with a root-confined loop; the tool removed its baseline worktree; the run folder stays (gitignored).

## Findings

**(a) Equivalence: exact.** Old (`AgentTaskLandNotificationService.cs` before): `prompts.OrderBy(p => p.Sequence).ToListAsync(ct)` then `.FirstOrDefault(p => LandNoteReceipt.IsReceipt(expected, p.Text!))`. New (`LandNoteReceipt.FirstReceiptAsync`, lines 58-72): same `prompts` IQueryable (unchanged `Prompts()`: session, `Text != null`, kind set by `AcceptsQueuedPrompt`, `Sequence > floor` strictly or the timestamp floor), same `OrderBy(Sequence)`, `Select(Sequence, Text)`, `AsAsyncEnumerable`, return at the first `IsReceipt`. Both read `db.TranscriptEntries.AsNoTracking()`, so no identity-resolution difference. Null Text is excluded by `Prompts()` in both and re-guarded in the new loop. Empty-string Text is not a receipt in either (strong arm rejects an empty record). Weak arm (normalized body under 12 chars) confirms the first post-floor row in both. Duplicates: both return the lowest Sequence. Cancellation and a mid-read provider error surface the same way (OperationCanceledException propagates; other exceptions hit the existing catch and record `notification_reconcile_failed`). The spill-pointer SHA check and the `ConfirmingPromptSequence` write are unchanged apart from the nullable struct. No input exists where the new path confirms and the old would not, or misses one the old would find. Witnesses: the 11 differential shapes (below-floor, kind sets for legacy and non-legacy, null text, duplicate, match-first/last) plus CP-5/CP-10/CP-11/CP-14/CP-15 green through the real queue.

**(b) Projection and seam: real and reliable enough.** The interceptor records the SELECT list and asserts `"Sequence"` and `"Text"` present and `"ToolInput"`, `"ApiErrorTimeZoneId"`, `"ModelCalls"` absent; the counting reader saw one `ReadAsync` for 48 candidates. WHERE and ORDER BY are byte-identical to the old statement, so the plan the investigation observed (bitmap index scan on `IX_TranscriptEntries_AgentSessionId_Sequence`, unique on `(AgentSessionId, Sequence)` at `AppDbContext.cs:1504`) is unchanged in kind; a narrower SELECT cannot remove index eligibility and `Text` was never covered. The probe keys on `"TranscriptEntries"` + `ORDER BY` + `"Sequence"` + not INSERT and `ShouldHaveSingleItem` would catch a second matching statement during the reconcile; it went red on the old shape (Code report).

**(c) First versus any: the old code used FIRST by Sequence too.** `FirstOrDefault` after `OrderBy(Sequence)` on the materialized list; `ConfirmingPromptSequence` records that row. Ordering is load-bearing for the recorded sequence (the `duplicate` shape asserts 11, not 15), though not for the Confirmed verdict itself.

**(d) No behaviour change beyond cost.** The server diff is two files: the new helper and the one call site. `AgentTaskLandNotificationHostedService` (5 s tick, 128-row pages) is untouched; aging, outcome, retention, monitoring and completion classes are green (CP-6..CP-12, CP-14).

**(e) Inherited red classified: stale test versus an intentional CARD-0519 S10 retention change.** `C467_V13_RetentionCancellationAndSupersession(confirmed)` fails at `AgentTaskLandReceiptTests.cs:120` with `should be False but was True` at base and head. Cause: CARD-0519 S10 commit 3e53e6583 (2026-10-05, "preserve recovery evidence") added `&& !outboundSources.Any(s => s.Id == m.Id)` to `DataRetentionService.PruneQueuedMessagesAsync` (lines 343, 359); `ChannelOutboundEvidence.DiscoverySources` protects every Sent row of Origin Delegation/Check/System/Scheduled while `ChannelReplySettledAt`, `ChannelOutboundDeliveryId` and `ChannelReplyDiscoveryClosedAt` are null; land notes enqueue as Delegation (`AgentTaskLandNotificationService.cs:131-132`). S10 updated its sibling `DataRetentionServiceTests.C544_CompletionObligationRetention` in 29c241a33 (the pruned row now seeds `discoveryClosed: true`; a confirmed-open row is asserted kept) but never selected `AgentTaskLandReceiptTests` (its rows were CP-11/12/14/58), leaving CARD-0467 V-13 ("after receipt ordinary pruning may remove queue history") contradicted by CARD-0519 G-81/PC-81. Not CARD-0544: that card's confirmed-open retention is the test S10 edited, not the predicate that changed. Not an unconditional leak: `ChannelReplyDispatcher.cs:264-294` closes a machine source once its prompt's turn has a TurnEnd and a later turn opened or the session reached its terminal generation, when the session has no earlier Channel context or policy silence is conclusive; the hold is open-ended only for a turn that never ends in a session that never stops (and in fixtures without a TurnEnd). Same cause for `C478_CompletionReplayAfterRetention` and `C478_CompletionReplayAfterPartialEnqueueRetention` in CP-13. Filed **CARD-1120** (Backlog) with both fix options and the bound question.

**Cost efficacy (observation, not a defect): the measured driver is only partly addressed.** The investigation attributes 10.3 s/68 s to 20 notes that stay AwaitingReceipt and re-read about 1,500 (two-kind) or 850 (one-kind) prompt rows every 7.8 s from floors near the session start. Those notes never match, so the new enumeration still reads every post-floor row and Postgres still scans, sorts and returns `Text` for all of them; the dropped columns are mostly null on UserPrompt rows. The real saving is entity materialization and change-tracker work on the server, plus the early stop for notes that do match (which then leave the scan). Even for a match Npgsql drains the remaining rows on reader close. Expect a smaller pg_stat_statements drop than the card implies; re-measure after land. Filed **CARD-1121** (Backlog) proposing a per-note scanned-through watermark or a backoff, keeping the UserPrompt verdict and retention unchanged.

**Other inherited reds (not CARD-1073):** 14 `PostLandMutationDeliveryTests` results (13 x `UserPrompt count should be 1 but was 2` after a receipt that did confirm at sequence 11; 1 x `File.Exists(ready)` in publication-commit) and 4 `DispatchBaseNotificationTests` C508 intent-capture results, all reproduced at def23ff2 by the baseline stage and absent from the CARD-1102 inherited-reds note. Filed **CARD-1122** (Backlog) with names, messages and first places to look (CARD-0519 S4/S9/S11 harness migrations; CARD-1087 S3/S4 and CARD-1065 S5 for the dispatch-base claims).

## Verdict

CARD-1073 is correct and equivalent; land it. The inherited reds are on CARD-1120, CARD-1121 and CARD-1122. PCs: none in this task; nothing pending for Mutation beyond the standard post-land pass.

--- review evidence ---
subjectTaskId: 5ff9ee20-17e0-442b-82b7-6e27f00d6e12
reviewedSourceSha: cc03bb73b38d0cccd67091768f10f8ce9992fb77
reviewedSourceClean: true
ordinaryScopeCompleted: Full
