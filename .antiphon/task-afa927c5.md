# CARD-1798 Code report (task afa927c5): transcript since-cursor hardening

Outcome: all four CARD-1764 round-1 advisories are fixed test-first. The touched and neighbour classes ran green (548/548). The guard batch gave 68/77; the 9 failures are inherited and the base run shows the same 9.

- Branch `feat/card-task-afa927c5`, runner mirror `/work/worktrees/task-afa927c5`, original Code task `afa927c5-c82c-4053-9ebe-9873227eb63c` (landing owner).
- Base / origin/master `d2ff7e8d4d63d67cf611b2c567c2da97416bd8fa` (unchanged at the end of the run).
- Red `0d632572d5fb9d839fc61dec8d80f8091018a1e0`. Fix = tested `bde044b56858af8c48eca4bb6f83927824f1905b`. This report is committed on top of it, with no source change.
- Plan: none exists for this card. The brief set the verification, so there is no `### Checkpoints` table and the checkpoint tool did not run. Every build and run used `scripts/build-slot.ps1`, one lease per command, with native default output, `-p:UseAppHost=false` and TEMP=/tmp.
- No prior implementation exists: nothing matched `git log --all --grep=CARD-1798`, and the card search returned only CARD-1798.

## Changes

`server/Infrastructure/Agents/SessionRunner/TranscriptReadHold.cs`
- **A-1:** a refused cursor is not read a second time when its answer is `Holdable`, meaning rows 1..LastSequence with no gap on a named stream and a non-null generation. That answer is held under the answer's stream and returned. Any other refused answer is read again in full, as before.
- **A-2 (byte cap):** the hold tracks an estimated byte weight for each body. `Weigh` charges 256 bytes per row plus 2 bytes per character of every string field. The total held across sessions is capped at `DefaultMaxBytes` = 256 sessions × 256 KiB (the CARD-1763 body cap), which is 64 MiB. Sessions are evicted least-recently-read first. A body larger than the cap is still returned but is not held, and any earlier body for that session is dropped. A join's weight is the held weight plus `Weigh(delta)`, so an empty delta does no O(n) work. The phone-home `PhoneHomePtyReads.Transcript` gets the same cap.
- **A-3:** `Join` of an empty, gap-free delta returns `delta with { Entries = held.Entries }`, so the held rows are reused, not copied.

`server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs`
- **A-2 (session end):** the HTTP client calls `_transcriptHold.Forget` in three places: after a successful `KillAsync`, after a `KillGenerationAsync` that returns `Killed=true`, and on every parsed `SessionExited` event in `StreamEventsAsync` (before the event is yielded).

`tests/Antiphon.Tests/Agents/TranscriptSinceClientTests.cs`
- **A-4:** new direct witness `Join_refuses_a_gap_free_delta_from_another_generation`. It has a same-generation control, a +1 s generation case and a null generation case.
- The scripted handler now also answers `/kill`, `/kill-generation` and `/events` with an SSE `SessionExited`. Non-transcript paths are recorded in `Other`.

The delivery proof is unchanged: every caller still receives rows 1..LastSequence.

## Red / green

| Run | SHA | Selection | Total | Failed | Passed | Skipped |
|---|---|---|---:|---:|---:|---:|
| red | 0d632572 | `/*/*/TranscriptSinceClientTests/*` | 24 | 8 | 16 | 0 |
| touched | bde044b5 | 24 classes (below) | 548 | 0 | 548 | 0 |
| guards | bde044b5 | guard batch | 77 | 9 | 68 | 0 |
| guards at base | d2ff7e8d4 (detached worktree, now removed) | same guard batch | 77 | 9 | 68 | 0 |

The 8 red tests at 0d632572 all failed on assertions:
- `Http_refused_cursor_keeps_the_runners_full_answer`
- `Http_empty_delta_at_the_cursor_returns_the_held_rows` (ShouldBeSameAs)
- `Http_session_end_drops_the_held_transcript(kill|kill-generation|exited)` (Queries were `["", "?since=2&stream=t1", ""]`)
- `Hold_drops_the_least_recently_used_sessions_past_its_byte_cap`
- `Hold_keeps_no_body_larger_than_its_byte_cap`
- `Read_past_the_byte_cap_answers_the_whole_transcript_and_holds_nothing`

Two tests passed at red, as expected:
- `Join_refuses_…generation`: A-4 adds only a test, so the generation check it guards already exists at base and its red can only come from a mutant.
- `Weigh_counts_the_text_each_row_carries`: the red commit adds the weighing API, but the cap is not enforced until the fix.

A first red run happened before one spare Enqueue was added to the A-1 test, and it failed there with `Queue empty`. Its replacement, the run at the committed red SHA above, fails on the assertion.

Touched run classes, all present in a fresh TRX with nonzero counts:
- TranscriptSinceClientTests 24, PtyConditionalReadClientTests 8, RunnerReadJsonClientTests 1, Card1761RunnerReadPollTests 20, RunnerContractMapperTranscriptEventTests 7, PhoneHomeTranscriptIncidentTests 5
- WorkingTranscriptRecoveryTests 54, StaleWorkingTranscriptWatchdogTests 53, StandingResumeReceiptReadinessTests 6
- SessionRunnerGenerationWireTests 9, SessionRunnerHttpClientHerdrWireTests 16
- PhoneHomeSessionRoutingTests 5, PhoneHomeConnectionTests 29, PhoneHomeDirectoryTests 8, PhoneHomeEventPumpTests 8
- SessionRunnerCapabilityGateTests 2, CompactionContinuationWireTests 6
- SessionRunnerHeldEventDeliveryTests 131, SessionRunnerHeldEventExitMatrixTests 50, SessionGenerationExitTests 9, SessionRunnerStreamReadinessTests 23
- Card1619ExitConsumerTests 11, TerminalRunnerSeatReleaseTests 41, SessionRunnerAbsenceEvidenceClientTests 22

Selection reason: the CARD-1764 Review CP-3 server set, plus every class that drives the real `SessionRunnerHttpClient` through kill, kill-generation or the event stream. `DesktopCapabilitiesSnapshotTests` and `CodexCliObservation*` from CP-3 were left out because they do not use the transcript hold.

Guard filter: `/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContract*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/*`. The 9 inherited failures:
- Registry_matches_compiled_metadata, C487_G068
- C1383_Rebase_preserves_distinct_appended_pairs ×2, C1383_Layout_opt_in_is_scoped(non-repository), C1383_Valid_reason_pairs_are_accepted ×4

All 9 report the same thing: `lane-xor Antiphon.IO.WriteBehind.Tests.WriteBehindFileSystemTests`. That class has no Unit/Integration category and is linked from Antiphon.Tests.csproj on master. This diff touches neither the class nor the csproj, and the same 9 fail at base d2ff7e8d4.

Unlisted builds and runs: five Antiphon.Tests builds — red, red after the spare-Enqueue edit, fix, and the base worktree. Every run is the one listed above. Slot waits were 0–45 s. All leases were granted, none timed out.

Whole Unit lane: not run here. docs/testing-and-build.md "Per-task Final" (line 286) assigns it to the nightly backstop and rc qualification.

Other checks:
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef d2ff7e8d4 -HeadRef bde044b5`: commits=2, entries=0, violations=0, exit 0.
- `git merge-tree --write-tree origin/master bde044b5`: clean, tree `6649e65f023d1faa6eea4557fb36200312998aa4`, exit 0.
- Not touched: `tests/test-execution-policy.json` (so no policyHash or PinnedInputDigest refresh), migrations, runner code. Nothing ran on the desktop and nothing was deployed. There is no OS-selected behaviour.

## PCs pending for method-scoped SourceLanding Mutation (none discharged)

| PC | Mutant | Witness expected red |
|---|---|---|
| PC-1 | delete the `Holdable(answer)` branch in `TranscriptSinceRead.ReadAsync` | `Http_refused_cursor_keeps_the_runners_full_answer` |
| PC-2 | that branch remembers under the held `stream`, not `answer.Stream` (or does not remember) | `Http_refused_cursor_keeps_the_runners_full_answer` (third query) |
| PC-3 | delete the Forget in `SessionRunnerHttpClient.KillAsync` | `Http_session_end_drops_the_held_transcript(kill)` |
| PC-4 | delete the Forget in `KillGenerationAsync` | `…(kill-generation)` |
| PC-5 | delete the Forget on `parsed.Exited` in `StreamEventsAsync` | `…(exited)` |
| PC-6 | delete `if (bytes > _maxBytes) return;` in `Remember` | `Hold_keeps_no_body_larger_than_its_byte_cap`, `Read_past_the_byte_cap_…` |
| PC-7 | eviction loop drops `\|\| _bytes > _maxBytes` | `Hold_drops_the_least_recently_used_sessions_past_its_byte_cap` |
| PC-8 | `Forget` does not subtract the bytes | `Hold_drops_…byte_cap` (Bytes after Forget) |
| PC-9 | `Remember` does not subtract a replaced body's bytes | `Hold_keeps_no_body_larger_than_its_byte_cap` (Bytes) |
| PC-10 | the join weight omits `Weigh(delta.Entries)` | `Read_past_the_byte_cap_…` |
| PC-11 | delete the empty-delta early return in `Join` | `Http_empty_delta_at_the_cursor_returns_the_held_rows` |
| PC-12 | delete the `SessionGeneration.Equal` check in `Join` (A-4) | `Join_refuses_a_gap_free_delta_from_another_generation` |
| PC-13 | `Weigh` drops `Text` or `ToolInput` | `Weigh_counts_the_text_each_row_carries` |

## Owed

- SourceLanding Mutation: PC-1 to PC-13.
- Review: Claude Frontier, ordinary round 1.
