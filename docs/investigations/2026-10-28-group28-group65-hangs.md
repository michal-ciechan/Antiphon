# Groups 28 and 65 of run 8f055f87: budget overruns, not hangs

Task 21993964, 2026-10-28, Investigate (read-only, native Windows desktop). I ran no test, build, dotnet or
git-heavy command. I only parsed stored files with light Python and grep. The live run (supervisor PID 145140),
`C:\Antiphon\releases` and `native-run.lock` were not touched.

## Verdict

**Confirmed: neither chunk hung.** Both ran tests until the 3,600 s watchdog killed them. Each had exactly one
test in flight at the kill, and that test had started in the last 10 s. Both chunks need about 3,700-4,000 s of
mostly **serial** work. The planner priced each at **900 s** (`pricingBasis: model`): none of their heavy classes
has a measured row in `scripts/lib/nightly-class-costs.json`, so the model charges 0.5-1.5 s per node, but these
tests take 17-365 s each. `ParallelLimiter<ProcessSpawnLimit>` (Limit = 1) then runs most of the classes one test
at a time.

The prior read (`docs/investigations/2026-10-28-failed-class-map.md`, e5360e7a) described "447 passed in 17 s,
then 11 never finished, 59 min with no progress" for g28 and "24 git tests stuck, a 39.5-minute stretch with no
progress" for g65. **The stored evidence does not support either.** For g65 the cause is known: the diag file
contains 10 NUL bytes from offset 2,001,749 (19:13:00 UTC), so a plain `grep` treats it as binary and stops
reading there. That is where the "stuck from 19:08-19:13" picture came from. For g28 the diag has no NUL bytes
and I could not reproduce the claim. A full parse shows continuous progress.

The live run 79b742de will repeat both overruns unless something changes. The live plan has the same classes
(g28: 17 classes, 704 tests, planned 900 s, model; g65: 28 classes, 975 tests, planned 900 s, model). Between
105792f7 and f84228f2, no test file in these classes, neither spawn limiter, and no row for these classes in
`nightly-class-costs.json` changed (`git diff --stat` touches only the costs file, 6 lines, and
`nightly-tests-impl.ps1`). The watchdog is still 3,600,000 ms.

## Evidence sources

Run dir `C:\Antiphon\releases\candidates\rc-2026-10-25-105792f7d22f\logs\2026-10-26-0946-8f055f87\`:
- `timing\cases-239920-ac1b3db4d0fa429d9c2c95ac1ab391a1.jsonl` (header `groupId: all-group-28`, pid 239920,
  epoch 13:24:59.68Z), `timing\cases-269272-6d44809b271d4f07935b60d58442bb4f.jsonl` (`all-group-65`, pid
  269272, epoch 19:06:48.77Z). These are per-test start/complete records with phases and process counts.
- `antiphon-all-group-28-execution\log_261026132457749.diag` (1,975,432 B) and
  `antiphon-all-group-65-execution\log_261026190647795.diag` (3,585,111 B), MTP Trace.
- `timing\groups-executed.json`: g28 `hostWallSeconds 3606.53, trxMissing true, exitCode 1`; g65 `3607.91`, same.
- The console logs hold only the header (g28) or the skipped/failed lines (g65). There is no
  "Test host process crashed" line and no hang-dump line in either log, because no dump is ever requested (Q4).
- Baseline: run a779085c (`rc-2026-10-24-a97194ee7a4a\logs\2026-10-24-0720-a779085c\antiphon-all-group-{28,65}.trx`),
  the same chunks with complete TRX.

The journal and the diag agree exactly:

| chunk | diag InProgress / terminal messages | journal start / complete | outcomes | in flight at kill |
|---|---|---|---|---|
| g28 | 541 / 540 Passed | 541 / 540 | 540 Passed | `ChannelBridgeTests.A_mid_turn_launch_note_does_not_steal_the_channel_reply`, started 3,595.8 s (14:24:55.50) |
| g65 | 961 / 946 Passed + 11 Skipped + 3 Error | 961 / 960 | 946 P, 11 S, 3 F | `WorktreeManagerGitIntegrationTests.WorktreeManager_raw_remove_preserves_unregistered_leftover_directory`, started 3,596.4 s (20:06:45.08) |

The last diag line in g28 is `14:24:55.4975623 ... TestNode ... ChannelBridgeTests ... A_mid_turn_launch_note_does_not_`
(InProgress). The one before it is `14:24:55.4974799`, `A_queued_channel_prompt_still_routes_the_reply` Passed.
Diag activity continued in every interval from 13:24 to 14:24.

## Q1: Group 28

**The "11 tests that never finished" do not exist.** Every OutputDistillationGate (103), HerdrLabelFollow (33)
and AgentTaskServiceIntegration (120) test completed by 56 s. After 56 s the journal shows **exactly one test in
flight** at every 120 s sample until the kill.

| class (limiter) | start s | end s | tests done | test-seconds | a779085c test-s (outcome) |
|---|---|---|---|---|---|
| HerdrLabelFollow, AgentCreateRace, OutputDistillationGate, AgentTaskServiceIntegration, TaskInputAttentionCost | 10 | 79 | 295 | 166 | 229 |
| DelegateBranchDelivery (ProcessSpawn=1) | 10 | 344 | 7 | 334 | 41 (7 Failed fast) |
| BlockedTaskParkDelivery (ProcessSpawn=1, NotInParallel MessageQueue) | 344 | 2,633 | 6 | 575 | 104 (3 Failed fast) |
| PostLandMutationCustody (ProcessSpawn=1) | 371 | 1,598 | 56 | 1,110 | 1,298 |
| RepairSourceDispatch (GitProcessSpawn) | 837 | 951 | 41 | 302 | 314 |
| RepairSourceSettlement (ProcessSpawn=1) | 1,598 | 2,105 | 36 | 491 | 447 |
| SessionRuntimeActionRouting (ProcessSpawn=1) | 2,105 | 2,252 | 35 | 112 | 69 (29 Failed fast) |
| BlockedTaskParkReclaim (ProcessSpawn=1, MessageQueue) | 2,633 | 3,540 | 38 | 907 | 981 |
| ChannelBridge (MessageQueue) | 3,540 | kill | 27 of 40 | 66 | 9 (22 Failed fast) |
| ChannelInboundRecovery, CodexStartupDelivery, LandEvidenceWarningDelivery, ApiErrorRecoveryService | never started | | 0 of 150 | | 192 |

Not started at the kill: 163 tests (13 ChannelBridge plus the 150 above). a779085c finished the whole chunk in
3,142 s, but only because 89 tests failed fast. The tests that grew are tests that were fixed since then and now
pass at full length:

| test | a779085c | 8f055f87 |
|---|---|---|
| BlockedTaskParkDeliveryTests.C1065_FullAnswerRequiresNewSessionUserPrompt | 16 s Failed | 365 s Passed: 21 internal iterations, one `db-create clone`/`db-cleanup drop` about every 17 s |
| DelegateBranchDeliveryTests.C1213_QueuedBranchRecovery | 6 s Failed | 235 s: 24 DB clones, 7-12 s apart |
| BlockedTaskParkDeliveryTests.C1065_ParkedReviewReplyBindsFreshEvidence | 6 s Failed | 117 s: 5 clones, 24 s apart |
| DelegateBranchDeliveryTests.C1213_QueuedBranchesReachRecipient | 6 s Failed | 76 s |

Each of these loops over cases inside one test (`foreach (var answer in ...)` at
`tests/Antiphon.Tests/Application/BlockedTaskParkDeliveryTests.cs:38`; `foreach (var modern in ...)` at
`DelegateBranchDeliveryTests.cs:153`). Every iteration boots a fresh DB clone and host. The phases show steady
progress inside each long test, so the longest completion gap (365 s) is one test working, not a stall.

Estimated serial need for g28 on this desktop: 3,606 s + about 13 × 2.4 s (ChannelBridge) + about 192 s (the 4
unstarted classes, a779085c timing) ≈ **3,830 s**, with no headroom against the 3,600 s watchdog.

**Sign-in lock fix (119311065):** it does not explain g28. **Refuted** for this failure: there was no blocked
test, every test that started finished except the one still in flight at the kill, and per-test durations match
a779085c (PostLandMutationCustody 1,110 vs 1,298 s; BlockedTaskParkReclaim 907 vs 981 s). Journal phase totals
for g28 are db 152 s, process 1,000 s and remainder 2,898 s. No phase is a lock wait of minutes.

## Q2: Group 65

**No git process was stuck.** The journal records 34,545 `process-wait` phases (all `exe: git`): median 95 ms,
p95 304 ms, max 2,166 ms (at 1,476 s). The median per 10-minute window stays between 91 and 99 ms from start to
kill, so no contention or degradation built up over the hour. In-flight tests were 4-5 during the first 6
minutes (GitProcessSpawnLimit classes), then **1** from 480 s to the kill (ProcessSpawnLimit classes). The longest
completion gap is 47 s, one `WorktreeGuardedCleanupTests.C443_TwoGitSlotsMaximum` run.

| class (limiter) | start s | end s | tests | git spawns | process s | wall test-s | a779085c test-s |
|---|---|---|---|---|---|---|---|
| CompletedCardWorktreeRemoval (GitProcessSpawn 2-4) | 10 | 260 | 37 | 6,234 | 761 | 942 | 308 |
| LandRecoveryCheckout / LandingGit / LandRecoveryIgnoredNested (GitProcessSpawn) | 238 | 412 | 107 | 3,315 | 576 | 643 | 630 |
| LandingRemovalControl (ProcessSpawn=1) | 375 | 1,136 | 34 | 6,592 | 687 | 761 | 644 |
| LandingSourceFreshness, LandingSourceBoundary, RepositoryChildRecovery | 1,114 | 1,290 | 66 | 370 | 97 | 271 | 226 |
| SettledWorktreeRemoval (ProcessSpawn=1) | 1,290 | 1,958 | 78 | 5,288 | 637 | 668 | 560 |
| WorktreeGuardedCleanup (ProcessSpawn=1) | 1,958 | 3,551 | 49 | 13,188 | 1,474 | 1,593 | 1,387 |
| WorktreeManagerGitIntegration (ProcessSpawn=1) | 3,551 | kill | 16 of about 29 | 0 recorded | | 56 | 80 |

The other 17 classes took 143 test-seconds together: LandingAdmissionControl 96 s, GitService 13 s, and the
rest (Docker*, HostCleanup*, Landing{GitProfile,Identity,RemovalPolicy}, FileUseCapture, GrokRunnerImage,
WorktreeDeleteAccessProbe, WorktreeLockDiagnostics) 34 s.

Two questions from the brief have no evidence to support them:
- One shared repository or temp root: the journal does not record paths, and I found no evidence of a shared
  root. Each test runs its own git sequence of about 270 spawns (WorktreeGuardedCleanup: 13,188 / 49).
- index.lock, gc, fsmonitor, Defender, long path, credential prompt or hooks: none appears. No git wait exceeded
  2.2 s, so nothing waited on a prompt or a lock.

**The g65 crawl is volume, not load or a hang.** About 34.5k Windows git spawns at about 95-130 ms each come to
4,232 s of process time. Most of it runs behind a one-at-a-time limiter. Remaining at the kill: about 13
WorktreeManagerGitIntegration tests (about 50-80 s). The serial need is about **3,670 s**, a miss of roughly
1-2 minutes. a779085c fit in 2,998 s because 97 tests failed fast.

g65's three failures, unrelated to the timeout:
- `LandingGitTests.C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal` (32 s, `ShouldAssertException: watch.Elapsed`)
- `RepositoryChildRecoveryTests.C452_R2_AliasRecoveryUsesCanonicalCommonDirectory`
- `WorktreeManagerGitIntegrationTests.C1502_CompleteCheckoutStillMarkedIncompleteIsHealed`

## Q3: Reproduction recipe and smallest mitigation

The overrun is reproducible on Windows (git spawns there cost about 95 ms each). On Linux the same tests may fit,
so a Linux run cannot prove or disprove the Windows overrun. Native Windows recipe, through the build-slot gate,
from an rc f84228f2 checkout:

```
pwsh -NoProfile -File scripts/build-slot.ps1 -Label g28-heavy -- dotnet run --project tests/Antiphon.Tests -- --treenode-filter "/*/Antiphon.Tests.Application/BlockedTaskParkReclaimTests/*"
pwsh -NoProfile -File scripts/build-slot.ps1 -Label g65-heavy -- dotnet run --project tests/Antiphon.Tests -- --treenode-filter "/*/Antiphon.Tests.Infrastructure/WorktreeGuardedCleanupTests/*"
```

Expected: all tests pass, in about 900 s and about 1,500 s respectively, against model prices of roughly 60 s
and 25 s. The symptom is wall time, not failure. Single-method witnesses take 4-6 minutes each:
`/*/Antiphon.Tests.Application/BlockedTaskParkDeliveryTests/C1065_FullAnswerRequiresNewSessionUserPrompt`
(about 365 s) and `/*/Antiphon.Tests.Application/DelegateBranchDeliveryTests/C1213_QueuedBranchRecovery`
(about 235 s). No reproduction needs a hang dump.

Smallest fix I believe in: give the planner measured costs for these classes. Measured seconds per node:

| class | s/node |
|---|---|
| PostLandMutationCustody | 1,110 / 56 |
| BlockedTaskParkReclaim | 907 / 38 |
| BlockedTaskParkDelivery | 575 / 6 |
| RepairSourceSettlement | 491 / 36 |
| DelegateBranchDelivery | 334 / 7 |
| RepairSourceDispatch | 302 / 41 |
| WorktreeGuardedCleanup | 1,593 / 49 |
| CompletedCardWorktreeRemoval | 942 / 37 |
| LandingRemovalControl | 761 / 34 |
| SettledWorktreeRemoval | 668 / 78 |

With these rows the 900 s packer splits g28 and g65 into about four to five launches each, every one well under
the 3,600 s watchdog. No timeout needs raising, and nothing hangs, so a per-test timeout or process-tree kill
would not help.

## Q4: Live run 79b742de recorder and supervisor

- Inputs are already recorded. `...\rc-2026-10-27-f84228f285dd\logs\timing-79b742de44354190bb55fd35914496f3\groups-planned.json`
  (22:26:45) holds g28 and g65 with full `classes`, `filter` and `plannedTestIds` in shared manifest order. Per-chunk
  `antiphon-all-group-N.timing.json` files are written at start and at completion.
- The per-test journal `cases-<pid>-<nonce>.jsonl` is written continuously into the same timing root. There were
  27 of them at 00:41Z. Together with the MTP Trace diag (`--diagnostic`), it is enough to tell a crawl from a
  wedge after a kill: compare the last `complete` time with the kill time, and the in-flight count.
- **The supervisor captures no dump and no process list on timeout.** `Get-NightlyNativeExecutionArguments`
  passes no `--hangdump` or `--crashdump` (`scripts/lib/nightly-tests-impl.ps1:607-615`). On timeout
  `NativeProcessOwner` sets `TimedOut` and calls `TerminateJobObject` immediately (rc
  `scripts/lib/nightly-owned-process.cs:178-187`). The timing JSON records `lastPhase: timeout`. If g28 or g65
  overruns again, the journal and diag survive, but no stack or handle state will exist.

## Not done, noted

- Fix idea: add measured rows for the classes above to `scripts/lib/nightly-class-costs.json`, and add an
  optional `--hangdump --hangdump-timeout` below the watchdog so a real wedge would leave a stack.
- For the live run, before g28 starts (about 02:00Z), expect `timeout all-group-28` near 3,600 s. Run the 4
  unstarted classes and the ChannelBridge tail as a split rerun, not as a hang investigation.
- I did not establish why the prior read saw a 59-minute gap in the g28 diag. The full file has no gap and no NUL.
