# Review CARD-1756 round 1 (task bc7a9fdd): bound tailer poll backoff

Subject: `origin/feat/card-task-67112dd1` at `b8fe734030a7a2d08f4a45d090326ccd576b9fe7` (Code owner `67112dd1-da27-4a22-bcf3-71c34c24de0b`, Grok).
Range reviewed: `6d5531762633dbb9077739cb451386c6c2131255..b8fe734030a7a2d08f4a45d090326ccd576b9fe7` (2 commits; red `ca0c666b07bb96b0adb8ab8a5a5563c566bb7eb7`, fix `b8fe734030a7a2d08f4a45d090326ccd576b9fe7`). Master at brief: `0e7aef0379a073c206e8398dcaed539bc33eaf9b`.

**Verdict: one blocker, B-1 (test adequacy). Product behaviour verified correct by execution; the repair is test-only.**

## Findings

### B-1 The 4 s interval identity recheck (the documented 6 s same-stat staleness) has no committed witness

- Where: `src/Antiphon.SessionRunner/TranscriptTailer.cs` `ResolveBoundIdentity` (`intervalDue` branch, `IdentityRecheckInterval` 4 s, `IdentityStaleness` 6 s); `tests/Antiphon.SessionRunner.Tests/TranscriptTailerBoundPollTests.cs`.
- Failure: `Replaced_bound_file_is_detected_within_the_identity_bound` replaces the inode with `File.WriteAllBytes`, which changes mtime, so it is caught by the stat-change branch on the very next poll (measured 409 ms). Nothing in the branch exercises a replacement that keeps length and mtime. A mutant that drops `intervalDue` (never rechecks an unchanged stat) survives all three witnesses, so the only new guarantee the card introduced (bounded staleness instead of a per-poll check) is unproven by the suite and uncovered by PC-1..PC-3.
- Why it matters: the card's constraint is "file-identity change detection stays correct, with a documented maximum staleness"; this is the branch that bound exists for (same-second mtime on coarse filesystems, `cp -p`/restore replacements). The product does satisfy it (scratch probe A below: detected after 3988 ms through exactly one interval open), so only the witness is missing.
- Fix: add one method to `TranscriptTailerBoundPollTests`: wait until `CurrentBoundPollDelay == QuietPollCap`, wait for one interval open (`IdentityHandleOpens` increments), then move the file aside, write the same bytes, `File.SetLastWriteTimeUtc(path, oldMtime)`, assert the `FileInfo` length and mtime are unchanged, then assert `BoundIdentityDrift` within `IdentityStaleness` and exactly one more `IdentityHandleOpens`. Declare it PC-4 (mutant: `intervalDue = false`). About 8 s wall. No product change.

### Advisories (D-2: no Review rerun for these alone)

- A-1 Docs: the new bounds (300 ms to 2 s quiet backoff, identity handle only on stat change or every 4 s, 6 s same-stat staleness, exited child fast until completion settles) live only in a code comment. `docs/session-runtime-invariants.md` owns transcript invariants and still describes the tailer without them.
- A-2 `_boundIdentityDrift` latches forever (never cleared on a fork or claim-revoked rebind) and `_boundPollDelay` is not reset to `PollInterval` on a rebind, so the first quiet wait on a new file inherits up to 2 s. Both are inside the cap and self-correct on growth; the drift flag is test-only today but reads like product state.
- A-3 Load sensitivity: `Unchanged_bound_file_opens_identity_handle_only_on_the_first_poll` needs poll 4 to run before the 4 s interval is due (nominal 2.1 s; measured 2.19 s at load average 20). `Quiet_bound_poll_backs_off_and_growth_restores_the_fast_poll` needs the restored gap under 900 ms (nominal 300 ms). Both passed here; a stalled host could still fail them.
- A-4 `TerminalTranscriptCompletion.Observe` now receives the stat taken before the read, so bytes appended between that stat and the read can let one poll publish `TerminalComplete=true` before they are parsed; the old code had the symmetric window (stat after read). Only reachable when a child writes more than 3 s after exit, corrected by the next poll. Not a regression.

## Review items from the brief

1. Terminal observation correctness. The per-poll identity result in the Claude tailer feeds only `_completion.Observe`, which returns early until child exit plus 3 s settle; `TerminalSeatReleaseObservation.ObserveAsync` (seat release) and `Snapshot().Readiness` (`IsCurrentFile` per snapshot) are unchanged and still open their own identity handle. On a check poll `MatchBoundFile` yields the same booleans as `IsCurrentFile` did (missing file, null bound identity, IO fault, changed inode all false; current inode true); between checks the cached value is returned, bounded by stat change (next poll, at most 2 s) or the 4 s interval. Probes: same-stat replacement detected 3988 ms; existence loss detected 1999 ms with readiness `Unknown/file_identity_changed`; path change is a stat-key change (`_boundStatPath`) and a fork/claim rebind calls `TerminalObservation.Bind` first (TranscriptAdoptionSafetyTests 45 green).
2. Delays. Worst case for any new byte, input or exit is one in-flight quiet delay, 2 s. Measured: input restored the 300 ms poll after 1996 ms and the following prompt was ingested 288 ms after append; after `NotifyChildExited` the poll was fast 2004 ms later and `TerminalComplete` arrived 3207 ms after exit (settle 3 s), then the cap applied. Server windows: `TranscriptConfirmTimeoutSeconds` 30, `EvidenceTimeoutSeconds` 15. Seat release reads the file itself. Input cannot be missed: the file is append-only and read from the stored offset.
3. Thread safety and leaks. New state is written only on the single bound loop; test observables use `Volatile`/`Interlocked`; `SessionInputLog.Version` is lock-protected; no timers, only `Task.Delay`. The bound delay now takes the injected `TimeProvider` (production passes none at the three `new TranscriptTailer` sites, so `TimeProvider.System`); a frozen fake clock would now stall bound polls as the locate loop already did. No existing test depends on that (SessionRunnerCpuTests 11 green).
4. Grok and Codex tailers are not in the diff; they still call `IsCurrentFile` per poll and `Observe` without the probed stat (old `FileInfo` path). CodexTranscriptTailerTests 19 and GrokTranscriptTailerTests 32 green. The CARD-1624 locate scan is outside the changed hunks (bound loop only); SessionRunnerCpuTests 11 green.
5. Witnesses. Red commit read: at `ca0c666b0` the loop sets `_boundPollDelay = PollInterval` unconditionally and calls `IsCurrentFile` every poll, so the opens and backoff assertions necessarily fail (Code reported 2 failed / 1 passed: 4 opens expected 2; 601 ms expected over 1.2 s). Not re-executed by this Review. The replacement witness was green at base by construction and can go red (Code's never-recheck mutant); see B-1 for the branch it does not reach.
6. Rest of the diff: one `FileInfo` per poll, `grew` only on bytes actually read, `IOException` during the read leaves the delay unchanged, `Observe` keeps the `FileInfo` path when no stat is probed. No migration. `check-evidence-diff` exit 0 (commits=2, violations=0). `git merge-tree --write-tree origin/master HEAD` clean, tree `22f59c668d995662292f52b338a8fd390283c698`.

## Verification

Code report (task API): no plan and no `### Checkpoints` table (Grok Code without a Plan stage); its runs were listed with leases (touched 64/64, guards 75/75, red 2 failed/1 passed), unlisted runs none. Code did not run the Codex/Grok tailer, compaction-stop, latency-probe, park-wire or adoption classes; this round did.

Checkpoint run `20261107-150139-8f8d` (one-row ignored plan `.antiphon/review-bc7a9fdd-plan.md`, cold build `bin-r1756-touched/`, deleted after green):

```
CHECKPOINT CP-1 commit=b8fe734030a7a2d08f4a45d090326ccd576b9fe7 build=ok filter=/*/Antiphon.SessionRunner.Tests/(TranscriptTailerBoundPollTests*)|(TranscriptTailerObservationTests*)|(TerminalSeatReleaseTests*)|(TranscriptTailerCompactionTests*)|(ResumeReceiptReadinessTests*)|(SessionRunnerCpuTests*)|(CodexTranscriptTailerTests*)|(GrokTranscriptTailerTests*)|(CompactionContinuationStopTests*)|(TranscriptIngestionLatencyProbeTests*)|(BlockedParkWireTests*)|(TranscriptAdoptionSafetyTests*)/* executed=173 passed=173 failed=0 skipped=0 trx=/work/worktrees/task-bc7a9fdd/.antiphon/checkpoints/20261107-150139-8f8d/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=b8fe734030a7a2d08f4a45d090326ccd576b9fe7 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=227.181s wall=227.225s
PHASES CP-1 slotWait=165s build=184.1438975s startup=3.1062344s testsWall=222.4404945s teardown=0.6991087s hostWall=226.2457459s
CHECKPOINT SOURCE VALID source=b8fe734030a7a2d08f4a45d090326ccd576b9fe7 rows=1
```

| Class | Executed | Passed |
|---|---:|---:|
| TranscriptTailerBoundPollTests | 3 | 3 |
| TranscriptTailerObservationTests | 5 | 5 |
| TerminalSeatReleaseTests | 38 | 38 |
| TranscriptTailerCompactionTests | 4 | 4 |
| ResumeReceiptReadinessTests | 3 | 3 |
| SessionRunnerCpuTests | 11 | 11 |
| CodexTranscriptTailerTests | 19 | 19 |
| GrokTranscriptTailerTests | 32 | 32 |
| CompactionContinuationStopTests | 6 | 6 |
| TranscriptIngestionLatencyProbeTests | 4 | 4 |
| BlockedParkWireTests | 3 | 3 |
| TranscriptAdoptionSafetyTests | 45 | 45 |

Witness timings: opens-once 2.19 s, backoff 2.50 s, replacement 0.41 s. Slowest: cold-tail latency probe (96 MiB) 32.2 s. Host: server2, load average about 20, slot wait 165 s.

Scratch probes (git-archive export of HEAD in the session scratchpad with one extra test file, built and run through `build-slot.ps1`, never committed): 4 executed, 4 passed, 23 s.

| Probe | Result |
|---|---|
| A same-length, same-mtime inode replacement after backoff | drift after 3988 ms, opens 2 to 3 to 4 (one interval recheck) |
| B file deleted after backoff | drift after 1999 ms, readiness `Unknown/file_identity_changed` |
| C `SessionInputLog.Append` after backoff | 300 ms poll back after 1996 ms; appended prompt ingested 288 ms later |
| D `NotifyChildExited` after backoff | fast poll after 2004 ms, `TerminalComplete` 3207 ms after exit, then 2 s cap |

Not run: the whole Unit lane (brief commissioned the touched classes in one leased run; 60-minute budget); the red witness at `ca0c666b0` was read, not re-executed. PCs stay pending for method-scoped SourceLanding Mutation: PC-1, PC-2, PC-3 as declared by Code, plus the PC-4 B-1 asks for.

--- review evidence ---
subjectTaskId: 67112dd1-da27-4a22-bcf3-71c34c24de0b
reviewedSourceSha: b8fe734030a7a2d08f4a45d090326ccd576b9fe7
reviewedSourceClean: true
ordinaryScopeCompleted: Full
reviewRound: 1
reviewedRange: 6d5531762633dbb9077739cb451386c6c2131255..b8fe734030a7a2d08f4a45d090326ccd576b9fe7
