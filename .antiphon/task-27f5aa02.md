# CARD-1798 Review (round 1, Final): transcript since-cursor hardening — CLEAN

Review task 27f5aa02 on `feat/card-task-27f5aa02` (runner mirror `/work/worktrees/task-27f5aa02`). Subject: Code task `afa927c5-c82c-4053-9ebe-9873227eb63c` (landing owner), branch `feat/card-task-afa927c5`, Grok Code author.

- Base (merge-base with master): `d2ff7e8d4d63d67cf611b2c567c2da97416bd8fa`; origin/master at review time `251dee88e11ee48da66c34aa636f57c4b3e3eb41`.
- Code tip (product + tests): `bde044b56858af8c48eca4bb6f83927824f1905b` (red `0d632572d5fb9d839fc61dec8d80f8091018a1e0`). Branch tip reviewed and tested: `9337e423397383f880b563674237ef85bd57b1db` = Code tip + `.antiphon/task-afa927c5.md` only.
- Range reviewed: `d2ff7e8d4d63d67cf611b2c567c2da97416bd8fa..9337e423397383f880b563674237ef85bd57b1db`. The diff is three files: `server/Infrastructure/Agents/SessionRunner/TranscriptReadHold.cs` (+94/-27 net), `SessionRunnerHttpClient.cs` (+9), `tests/Antiphon.Tests/Agents/TranscriptSinceClientTests.cs` (+226). It is a source change, not a docs/contract change: no DTO, schema, settings or doc moved.
- `git merge-tree --write-tree origin/master bde044b56`: clean, tree `21a885f0307e3ca1faf34dbbd44f66a8bf2ac18b`, exit 0.
- check-evidence-diff: `d2ff7e8d4..bde044b56` commits=2 entries=0 violations=0 exit 0; `d2ff7e8d4..9337e4233` commits=3 entries=1 violations=0 exit 0 (the entry is the committed Code report).
- `scripts/impacted-classes.ps1 -Range d2ff7e8d4..bde044b56 -Json`: no impacted row matches. The fixed high-radius subset is the Merge batch's obligation (docs/orchestration-loop.md:685), not this round's.

## Verdict

CLEAN. No blocker, no regression. Three advisories (A-5..A-7) go to cards and do not hold the land. PCs 1-13 stay pending for method-scoped SourceLanding Mutation.

## Adversarial checks (brief items 1-5)

1. **A-1 whole-stream test is exact.** `TranscriptSinceRead.Holdable` (TranscriptReadHold.cs:199) requires a non-empty stream, `body.SessionId == sessionId`, a non-null generation, every row with the session id and `Sequence == expected + 1` from `expected = 0` (rows exactly 1..n, contiguous), and `n == body.LastSequence`. A served cursor delta begins at `held.LastSequence + 1 >= 2` because `TranscriptSince.IsCursor` needs `since > 0`, so a partial body can never pass the row-1 check and is never held as a whole body. A stale-cursor empty answer (`LastSequence != 0`) fails `Join` (`expected != LastSequence`) and `Holdable` (`0 != LastSequence`) and falls to the full read. An empty answer with `LastSequence == 0` on a fresh stream is a genuinely empty whole stream; the next poll has no cursor (`IsCursor(0, ..)` is false). The refused answer is remembered under `answer.Stream` with its own generation (PC-2 shape), so the next cursor targets the runner's new stream (`Http_refused_cursor_keeps_the_runners_full_answer` asserts `?since=3&stream=t2`).

2. **A-2 forget and cap.** Forget sites: `KillAsync` after `EnsureRunnerSuccessAsync` (a failed kill keeps the hold, correct), `KillGenerationAsync` only when `Killed`, `StreamEventsAsync` on every parsed `Exited` before the yield. Race: a poll that did `TryGet` before a `Forget` and `Remember`s afterwards re-inserts the dead session's hold (TryGet and Remember are separate lock scopes, no epoch). Consequence is memory only: a relaunched `RunnerSession` has a fresh tailer Guid, so the runner refuses `since=N&stream=t_old` and answers its whole new stream; `Join` refuses on stream (and generation) and `Holdable` adopts the new stream's full body. A recycled id can never join against the stale body. Before this card the same stale hold existed with no Forget at all, so this is no regression (advisory A-5). Cap accounting: every read and write of `_held` and `_bytes` is inside `lock (_gate)`; `_bytes` is always the exact sum of stored `Bytes` (insert adds; the replace-prior, over-cap, eviction and Forget paths all subtract). `Weigh` is pure and runs outside the lock. Two concurrent polls for one session each replace the entry with their own body and weight, so the total stays consistent. Eviction only removes dictionary entries; a join in flight holds a reference to the immutable held record, so eviction cannot touch a mid-join body, and the join result is re-remembered. The eviction loop never evicts the just-inserted entry (highest `Touched`), so `MinBy` never runs on an empty dictionary. A body heavier than the cap is returned and not held, and its prior entry is dropped (`Hold_keeps_no_body_larger_than_its_byte_cap`, `Read_past_the_byte_cap_...`). Phone-home: `PhoneHomePtyReads.Transcript = new()` takes `DefaultMaxBytes` per connection (advisory A-7).

3. **A-3 shared rows.** `Join` returns `delta with { Entries = held.Entries }` on an empty gap-free delta: same reference, `LastSequence` equal by construction, `TerminalComplete`/`Readiness`/`CodexTokenCountAt` from the fresh answer. `Entries` is `IReadOnlyList<SessionRunnerTranscriptEvent>` of immutable records; the hold never mutates a held list (a non-empty join allocates a new array). Grep of `server/` for casts of transcript `Entries` to `List<>`/array, `CollectionsMarshal`, `Add/Insert/Remove/Sort/Clear/Reverse`: none; consumers enumerate (`Any`, `Select`, `LastOrDefault`, `foreach`, `PersistTranscriptAsync`). Since CARD-1764 the first full-read body was already shared between the hold and its caller; A-3 only extends the same sharing to later callers.

4. **A-4 direct generation witness.** `Join_refuses_a_gap_free_delta_from_another_generation` calls `Join` directly: same session, same stream `t1`, row 3 after held `LastSequence` 2, so every other refusal condition is satisfied and the only path to `null` for the `+1 s` and `null` cases is `!SessionGeneration.Equal(...)`. Deleting that check makes both `ShouldBeNull()` assertions fail; the same-generation control pins that the join itself works. The mutant was not run in this round (read-only, each build lease waited about 10 minutes on this host); PC-12 stays pending for method-scoped SourceLanding Mutation.

5. **Whole transcript to every consumer, bytes unchanged, old peers.** `ReadAsync` returns one of: the join (held 1..N plus delta N+1..M), the refused-cursor answer only when `Holdable` proved rows 1..LastSequence, the held rows unchanged when the delta is empty and gap-free, or the fresh full body. Every path hands the caller rows 1..LastSequence; the UserPrompt delivery proof, catch-up and idle detection paths (`AgentSessionRuntime` :971/:1026/:1069/:1254, receipt readiness, recovery pump) are untouched by the diff. The diff is three files (two server internals, one test class): no DTO, contract, JSON or settings change, and `RunnerReadJsonClientTests` (CARD-1762 byte pin) is in CP-1. Old runner without a `Stream`: `Holdable` is false, the hold is forgotten, and every read stays the plain `sessions/{id}/transcript` GET. Old server: nothing in this diff reaches the wire.

## Re-runs at the reviewed tip 9337e4233 (= bde044b56 + the Code report only)

Plan extracts (review-only, ignored): `/work/worktrees/task-27f5aa02/.antiphon/review-27f5aa02-checkpoints.md` (run 1), `...-checkpoints-b.md` (run 2, same rows, `--serial`), `...-guards.md` (not run through the tool; see below). TEMP/TMP=/tmp, UseAppHost=false confined by the tool, one lease per build and per row.

### Run 1, `20261109-020458-6679` (RED on two overlap timeouts; validate exit 2 `report_failed`)

Wall 34m56s, of which 14m15s was build-slot admission (4/4 slots busy, queue position 2) and 16m46s the cold build under maxcpucount 6; host load 17-28 on 24 cores with two other worktrees' checkpoint runs active. Rows CP-1 and CP-2 overlapped (two at a time on Linux).

| Row | Selection | Executed | Failed | Skipped | TRX classes (fresh) |
|---|---|---:|---:|---:|---|
| CP-1 | 17 transcript/phone-home classes (Code's first group) | 261 | 2 | 0 | TranscriptSinceClientTests=24 (all 10 CARD-1798 methods by name), PtyConditionalReadClientTests=8, RunnerReadJsonClientTests=1, Card1761RunnerReadPollTests=20, RunnerContractMapperTranscriptEventTests=7, PhoneHomeTranscriptIncidentTests=5, WorkingTranscriptRecoveryTests=54, StaleWorkingTranscriptWatchdogTests=53, StandingResumeReceiptReadinessTests=6, SessionRunnerGenerationWireTests=9, SessionRunnerHttpClientHerdrWireTests=16, PhoneHomeSessionRoutingTests=5, PhoneHomeConnectionTests=29, PhoneHomeDirectoryTests=8, PhoneHomeEventPumpTests=8, SessionRunnerCapabilityGateTests=2, CompactionContinuationWireTests=6 |
| CP-2 | 7 kill / kill-generation / SessionExited consumer classes (Code's second group) | 287 | 0 | 0 | SessionRunnerHeldEventDeliveryTests=131, SessionRunnerHeldEventExitMatrixTests=50, SessionGenerationExitTests=9, SessionRunnerStreamReadinessTests=23, Card1619ExitConsumerTests=11, TerminalRunnerSeatReleaseTests=41, SessionRunnerAbsenceEvidenceClientTests=22 |

The two CP-1 failures are `PhoneHomeConnectionTests.Held_launch_does_not_block_heartbeat_or_reads` and `.Old_epoch_reply_cannot_complete_current_request`, both `TimeoutException` on a 3 s `WaitAsync` (lines 302 and 217). Neither the class nor `PhoneHomeLiveConnection` is in the diff. Re-run alone against the same verified kept output (`bin-r1798a/`, direct test-host run under its own lease, `review-27f5aa02-phonehomeconnection`): PhoneHomeConnectionTests 29/29 passed in 9.3 s, TRX `<scratch>/phc/phc.trx` with 29 `PhoneHomeConnectionTests` results. Overlap/load flake in an untouched Integration class, not a regression of this diff; the caller may want a Debug card for the 3 s bound (same shape as the recorded C0824/C1514 load flakes).

### Guard batch (own process, `review-27f5aa02-guards` lease, same verified kept output)

`/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/*`: total 77, failed 9, passed 68, skipped 0, 2m05s. TRX `<scratch>/guards/guards.trx`: TestClassificationPolicyTests=41, MechanicalPcContractTests=11, C1729LayeringGuardTests=9, SlowTestRegistryMergeTests=5, TaskPlatformGuidanceTests=5, NativeLinuxPolicyWitnessTests=3, SlowTestTripwireTests=2, TestClassificationGuardTests=1. The 9 failures are exactly Code's list (`Registry_matches_compiled_metadata`, `C487_G068`, `C1383_Rebase_preserves_distinct_appended_pairs` x2, `C1383_Layout_opt_in_is_scoped(non-repository)`, `C1383_Valid_reason_pairs_are_accepted` x4) and every failure message is `lane-xor Antiphon.IO.WriteBehind.Tests.WriteBehindFileSystemTests` (10 mentions): the inherited master red that Debug b73aeb1d is fixing. No other failure.

Reason these two runs were not checkpoint rows: the tool's `dotnet run --no-build --property:OutputPath=bin-r1798a/` rerun recipe refuses this project ("OutputType is 'Library'"), and a second cold build would have cost another admission wait plus about 15 minutes; the kept output is the one run 1 built and stamped `buildSource=verified` at 9337e4233. The test host was started from the output directory, so cwd matched `dotnet run`.

### Run 2, `20261109-024715-7ea2` (GREEN, `--serial`, validate exit 0)

Same two rows on a fresh cold build (`bin-r1798b/`, deleted by the tool), rows one at a time. Build lease granted at once; build 10m40s; wall 17m27s.

| Row | Executed | Failed | Skipped | TRX |
|---|---:|---:|---:|---|
| CP-1 | 261 | 0 | 0 | same 17 classes and counts as run 1, TranscriptSinceClientTests=24 with all 10 CARD-1798 methods present by name |
| CP-2 | 287 | 0 | 0 | same 7 classes and counts as run 1 |

`validate --evidence .../20261109-024715-7ea2/report.json --expected-source-sha 9337e4233...`: `CHECKPOINT SOURCE VALID source=9337e423397383f880b563674237ef85bd57b1db rows=2`, exit 0.

## Against the Code report (`.antiphon/task-afa927c5.md`)

- Code's touched run: 24 classes, 548/548 at bde044b5 → here 261 + 287 = 548 across CP-1/CP-2 at 9337e4233, same classes, same per-class counts, zero skipped.
- Code's guards: 77 with the 9 inherited → here 77 with the same 9, all `lane-xor WriteBehindFileSystemTests`. Code also ran the batch at base d2ff7e8d4 with the same 9 (detached worktree). Not re-run at base here; the failure text names a class and csproj this diff does not touch.
- Code's red at 0d632572: 8 of the 10 new tests failed on assertions (listed); the two that passed at red carry a reason (A-4 adds only a test; `Weigh` exists at red but the cap is enforced by the fix). Not re-run here.
- No plan exists for this card, so there is no `### Checkpoints` table and the checkpoint tool was not required of Code; the brief set the verification. Code's unlisted builds (five) each carry a reason. All Code runs were leased.
- Whole Unit lane: not run in this round. docs/testing-and-build.md "Per-task Final" (line 286) assigns it to the nightly backstop and the rc qualification; the CARD-1764 round applied the same reading. The profile's "every named full affected integration class" is CP-1/CP-2 plus the guard batch above; there is no ordinary V-n/R-n or manual acceptance for this card.
- No OS-selected production behaviour changed (no `check-os-witnesses` obligation); no migration; nothing ran on the desktop; `tests/test-execution-policy.json` untouched.
- Performance: both rows are under 2m10s of test time; the slow classes the tool flags (WorkingTranscriptRecoveryTests 704 s summed test time over 54 tests) are untouched neighbours.

## Advisories (cards, never hold the land)

- **A-5 (memory, low):** `TryGet` and `Remember` are separate lock scopes, so a poll that read the hold before a `Forget` (kill, kill-generation, SessionExited) re-inserts the ended session's body afterwards, and the ordinary post-exit catch-up read re-holds it anyway (full read, `Holdable`, `Remember`). Correctness is untouched (fresh stream Guid plus generation refuse any join by a relaunched id) and the byte cap bounds it, but the session-end Forget frees memory only when no read follows the end. A forget epoch (Remember refuses when a Forget for that id happened after its TryGet) or a short tombstone would make the Forget effective.
- **A-6 (perf, low):** the only per-body bound is the whole cap, so one transcript weighing up to 64 MiB is held and evicts every other session's hold (LRU), and one heavier than the cap turns each of its polls back into a full read (the pre-CARD-1764 cost) while also dropping its prior hold. A per-session cap well under the total (a few MiB) would stop one long session from thrashing the others.
- **A-7 (doc, minor):** `PhoneHomePtyReads.Transcript = new()` applies `DefaultMaxBytes` per phone-home connection, so the fleet-wide bound is 64 MiB times the connected runners, while the HTTP client has one hold. Fine for today's runner count; worth one line in the class summary so the cap is not read as global.

## PCs pending for SourceLanding Mutation (method-scoped)

PC-1 … PC-13 as listed in `.antiphon/task-afa927c5.md`; none discharged here. PC-12 (delete the `SessionGeneration.Equal` check in `Join`) is the A-4 witness; by inspection it is the only refusal condition the `+1 s` and `null` cases exercise.

## Residue left for manual removal

The red run 1 kept its verified output under the brief's no-`rm -rf` rule: `/work/worktrees/task-27f5aa02/tests/Antiphon.Tests/bin-r1798a/` (1.2 GB, gitignored, tree stays clean) and `/work/worktrees/task-27f5aa02/.antiphon/checkpoints/20261109-020458-6679/builds/bin-r1798a`. The direct test-host reruns wrote their TRX only to the session scratchpad. The mirror retires with the task.

## Receipt provenance (unedited)

### Run 2 (validated)
commit: 9337e423397383f880b563674237ef85bd57b1db  branch: feat/card-task-27f5aa02  worktree: /work/worktrees/task-27f5aa02  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 9337e423397383f880b563674237ef85bd57b1db state=clean buildSource=verified
CHECKPOINT CP-1 commit=9337e423397383f880b563674237ef85bd57b1db build=ok filter=/*/*/(TranscriptSinceClientTests*)|(PtyConditionalReadClientTests*)|(RunnerReadJsonClientTests*)|(Card1761RunnerReadPollTests*)|(RunnerContractMapperTranscriptEventTests*)|(PhoneHomeTranscriptIncidentTests*)|(WorkingTranscriptRecoveryTests*)|(StaleWorkingTranscriptWatchdogTests*)|(StandingResumeReceiptReadinessTests*)|(SessionRunnerGenerationWireTests*)|(SessionRunnerHttpClientHerdrWireTests*)|(PhoneHomeSessionRoutingTests*)|(PhoneHomeConnectionTests*)|(PhoneHomeDirectoryTests*)|(PhoneHomeEventPumpTests*)|(SessionRunnerCapabilityGateTests*)|(CompactionContinuationWireTests*)/* executed=261 passed=261 failed=0 skipped=0 trx=/work/worktrees/task-27f5aa02/.antiphon/checkpoints/20261109-024715-7ea2/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=9337e423397383f880b563674237ef85bd57b1db sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=9337e423397383f880b563674237ef85bd57b1db build=reused filter=/*/*/(SessionRunnerHeldEventDeliveryTests*)|(SessionRunnerHeldEventExitMatrixTests*)|(SessionGenerationExitTests*)|(SessionRunnerStreamReadinessTests*)|(Card1619ExitConsumerTests*)|(TerminalRunnerSeatReleaseTests*)|(SessionRunnerAbsenceEvidenceClientTests*)/* executed=287 passed=287 failed=0 skipped=0 trx=/work/worktrees/task-27f5aa02/.antiphon/checkpoints/20261109-024715-7ea2/rows/CP-2/run.trx slot=granted waited=30s dirty=0 source=9337e423397383f880b563674237ef85bd57b1db sourceState=clean buildSource=verified

validate: CHECKPOINT SOURCE VALID source=9337e423397383f880b563674237ef85bd57b1db rows=2 (exit 0)

### Run 1 (red on the two overlap timeouts; validate exit 2 report_failed)
commit: 9337e423397383f880b563674237ef85bd57b1db  branch: feat/card-task-27f5aa02  worktree: /work/worktrees/task-27f5aa02  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 9337e423397383f880b563674237ef85bd57b1db state=clean buildSource=verified
CHECKPOINT CP-1 commit=9337e423397383f880b563674237ef85bd57b1db build=ok filter=/*/*/(TranscriptSinceClientTests*)|(PtyConditionalReadClientTests*)|(RunnerReadJsonClientTests*)|(Card1761RunnerReadPollTests*)|(RunnerContractMapperTranscriptEventTests*)|(PhoneHomeTranscriptIncidentTests*)|(WorkingTranscriptRecoveryTests*)|(StaleWorkingTranscriptWatchdogTests*)|(StandingResumeReceiptReadinessTests*)|(SessionRunnerGenerationWireTests*)|(SessionRunnerHttpClientHerdrWireTests*)|(PhoneHomeSessionRoutingTests*)|(PhoneHomeConnectionTests*)|(PhoneHomeDirectoryTests*)|(PhoneHomeEventPumpTests*)|(SessionRunnerCapabilityGateTests*)|(CompactionContinuationWireTests*)/* executed=261 passed=259 failed=2 skipped=0 trx=/work/worktrees/task-27f5aa02/.antiphon/checkpoints/20261109-020458-6679/rows/CP-1/run.trx slot=granted waited=255s dirty=0 source=9337e423397383f880b563674237ef85bd57b1db sourceState=clean buildSource=verified
FAILED Antiphon.Tests.Agents.PhoneHomeConnectionTests.Held_launch_does_not_block_heartbeat_or_reads (CP-1) TimeoutException: The operation has timed out. -> rows/CP-1/failures.md
FAILED Antiphon.Tests.Agents.PhoneHomeConnectionTests.Old_epoch_reply_cannot_complete_current_request (CP-1) TimeoutException: The operation has timed out. -> rows/CP-1/failures.md
CHECKPOINT CP-2 commit=9337e423397383f880b563674237ef85bd57b1db build=reused filter=/*/*/(SessionRunnerHeldEventDeliveryTests*)|(SessionRunnerHeldEventExitMatrixTests*)|(SessionGenerationExitTests*)|(SessionRunnerStreamReadinessTests*)|(Card1619ExitConsumerTests*)|(TerminalRunnerSeatReleaseTests*)|(SessionRunnerAbsenceEvidenceClientTests*)/* executed=287 passed=287 failed=0 skipped=0 trx=/work/worktrees/task-27f5aa02/.antiphon/checkpoints/20261109-020458-6679/rows/CP-2/run.trx slot=granted waited=345s dirty=0 source=9337e423397383f880b563674237ef85bd57b1db sourceState=clean buildSource=verified

--- review evidence ---
subjectTaskId: afa927c5-c82c-4053-9ebe-9873227eb63c
reviewedSourceSha: 9337e423397383f880b563674237ef85bd57b1db
reviewedSourceClean: true
ordinaryScopeCompleted: Full
reviewRound: 1
reviewedRange: d2ff7e8d4d63d67cf611b2c567c2da97416bd8fa..9337e423397383f880b563674237ef85bd57b1db
