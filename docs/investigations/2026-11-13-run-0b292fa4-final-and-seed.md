# Run 0b292fa4 final result and the cumulative seed for the next cut (2026-11-13)

Task cf82bfb0. The Windows Full run `0b292fa4bc3b40158e8950fb17804a21` on `rc/2026-11-18` (`56e96eabf698e9c1fcd462836ad32ed1fb259e83`, candidate `rc-2026-11-18-56e96eabf698`) ended by itself. This task read its final result and built the cumulative ordering seed for a relaunch on the current `origin/master`, `ab7af12c699039270af7e22a5d7fd794035bc325`.

The task only read things and launched nothing. It created no tag, published or promoted nothing, and created or moved no rc ref. It did not touch the AppHost, the production runner, Docker, any session or any run-folder file. The one thing it created is the folder `C:\Antiphon\releases\task-cf82bfb0\`.

`summary.json` is 889,834,487 bytes and was never loaded whole. A Newtonsoft `JsonTextReader` streamed it token by token, and each top-level property was capped at 4 MB of text. TRX files were read with a streaming `XmlReader`, and the per-chunk `.timing.json` files were read directly.

## 1. Verdict

| Item | Value |
|---|---|
| Outcome | **FAIL**: `outcome=TESTS`, `succeeded=false`, `testsPassed=false`, `coverageComplete=false`, `testExit=1`, `exitCode=1`, report delivered (`reportExit=0`). |
| Harness | `harnessHang=completed-but-hung` |
| Started / completed | summary: 2026-11-12T12:14:01.94Z to 2026-11-13T05:20:20.20Z (61,578 s, 17.10 h). State `completedAt` is 05:26:52Z. |
| Supervisor | pid 45428, `EXIT 1` at 05:26:58.25Z, then `SUPERVISOR-END`. Launched 12:13:39Z. Its log has only START, LAUNCHED, STATE-COPY, PROGRESS and EXIT lines: no restart and no kill. |
| Builds | all pass: `npm ci`, `npm run build`, `client lint`, `dotnet build Antiphon.sln` (exit 0, none timed out) |
| Required suites | 13: antiphon, session-runner, pty-host, agents-pty, messaging, client, scripts, e2e, markdown-pdf, observability, server-git, resilience, **git-in-process** (new on this cut, it ran and passed) |

### Chunks: 1,163 expected, 1,163 present

There are 1,163 `*.timing.json` files. That is 1,161 TRX chunks plus `client` and `scripts`, which have no TRX by design.

| Suite | Chunks | complete | timeout | partial | unstarted | Result |
|---|---:|---:|---:|---:|---:|---|
| antiphon | 885 | 884 | 1 | 0 | 0 | **FAIL**: g82 (1 failed row) and g118 (harness hang, TRX 146/146 passed) |
| session-runner | 101 | 100 | 1 | 0 | 0 | **FAIL**: g4 (harness hang, TRX 133/133 passed) |
| e2e | 76 | 76 | 0 | 0 | 0 | pass |
| pty-host | 53 | 53 | 0 | 0 | 0 | pass |
| agents-pty | 29 | 29 | 0 | 0 | 0 | pass (coverage reason only) |
| markdown-pdf | 12 | 12 | 0 | 0 | 0 | pass |
| messaging, observability, server-git, resilience, git-in-process | 1 each | 1 each | 0 | 0 | 0 | pass |
| client, scripts | 1 each | 1 each | 0 | 0 | 0 | pass |

The TRX counters show one chunk with `failed>0` (antiphon g82: 358 total, 357 passed, 1 failed). No TRX has an `error` or `timeout` counter. The 40 `NotExecuted` rows are platform skips: the 26 stale-roster rows and 14 `unexecuted-outcome` cases (section 3).

## 2. Every failed or timed-out row

| Chunk | Class.Method | Start (UTC) | Duration | First assertion / exception | Classification |
|---|---|---|---|---|---|
| antiphon `all-group-82` | `Antiphon.Tests.Application.CardFileGitFailureAcceptanceTests.Git_failures_are_sanitized_preserve_HEAD_and_retry_after_owned_fault_is_removed(cancel)` | 11-12 15:59:47 | 5.55 s | `ShouldAssertException: File.Exists(Path.Combine(root, "hook-wait-failed")) should be False but was True` at `AssertOwnedHookKilledAsync` (`CardFileGitFailureAcceptanceTests.cs:134`) | **KNOWN**: the g82 cancel hook. Fixed on master by the g82 job-object kill: `dc5d74fb9` red witness, `c00aad519` fix, merge batch 189. |
| antiphon `all-group-118` | chunk-level harness timeout, no failed test (146/146 passed; TRX 3m49s) | 11-12 17:06:31, timeout 17:11:22 | run 290.8 s | `result=FAIL timedOut=true`, `why=completed-but-hung`, `lastPhase=timeout` | **KNOWN**: the ae84478d g118 hang (sleep leak). Fixed on master by the batch 187 hang fixes, `d268c50c` series. |
| session-runner `all-group-4` | chunk-level harness timeout, no failed test (133/133 passed; TRX 8m35s) | 11-12 19:17:44, timeout 19:27:23 | run 578.4 s | the same | **KNOWN**: the ae84478d sr g4 hang (pty-host leak). Fixed on master by the same batch 187 series. |

**NEW: none.**

The nine ae84478d failure rows all re-ran and passed on this cut. They were in g80, g97, g126, g131 (2), g133, sr g1 (2) and profile-16.

The batch 187 hang fixes are on master after the cut and are not in `56e96eabf`. The series is `457741398`, `124235c4d` (red), `02f15f0cf` (fix: C24 kills the worker's whole descendant scope, reaching the msys `sleep`), `1484cc1f1` (red), `412a7bc4a` (fix: sweep every registered pty-host root), `ef475fb59` (red), `8f321c55e` (fix: `RunnerSessionGenerationTests` tears down each C502 pty-host in a finally), `3a984fae2`, `6626e8144` and `4a409cb32` (why-missed). `git merge-base --is-ancestor` confirms `c00aad519` and `4a409cb32` are ancestors of `ab7af12c6`.

## 3. Lane warnings

### Hangs: yes, both recurred, as expected on un-fixed code

The chunk ids are the same and the leaked holder is the same kind as in ae84478d:

| Chunk | Capture | Descendants | Alive at capture |
|---|---|---:|---|
| antiphon g118 | `antiphon-all-group-118-tests.log.hang.txt` | 3,257 (all exited but one) | **`sleep` pid 116496, 104 handles** |
| session-runner g4 | `session-runner-all-group-4-tests.log.hang.txt` | 14,177 (all exited but one) | **`Antiphon.PtyHost` pid 145516, 267 handles** |

In both chunks the root `testhost` exited (`root ... exited=true active=1`).

The g118 descendant images were git 2,034, conhost 1,011, pwsh 75, csc/cvtres/powershell 36 each, sh 21, `sleep` 2 and others. The class set is identical to ae84478d: `E2ECopyOwnedPtyHostAppHostContractTests`, `LandingAdmissionControlTests`, `LandingGitBatchingTests`, `LandRecoveryCheckoutTests`, `LandRecoveryIgnoredNestedRepositoryTests`, `RepositoryChildRecoveryTests`, `RepositoryMutationLeaseTests`. The fix targets `RepositoryMutationLeaseTests`' git-hook `sleep`.

sr g4 has 30 classes, among them `RunnerSessionGenerationTests` (the fix target), `TestSessionTeardownTests` and `HerdrPaneChildKillTests`.

This run used `56e96eabf`, which has none of the batch 187 fixes, so the recurrence is the expected result and not a new defect. A run on master or a later cut is the first proof of the fix.

### Other warnings

- **stale-roster**: 25 warnings, exactly the same 25 test ids as ae84478d. None was added and none dropped. They are `C0824RefOnlyCleanupTests` (1), `RemoteScriptContractTests.C1105` (1), `RollingVolumeRecycleScriptTests.C1008` (2), `VolumeRetirementApplyGateTests` (7), `RunnerChildClaudeTokenInheritanceTests` (1), `ClaudeDialogWatchdogCanaryTests.C680` (1), `C0824RefOnlySyncTests` (1), `CheckpointColdBuildTests.C1734` (2 ids, 3 rows), `LandGraphEvaluationTests.EvaluationBudgetRefusesSlowDotnet` (1) and `WorktreeNoFollowDeleteTests.C1717` (8).
- **unexecuted-outcome**: 3 reasons and 14 cases, all Linux-only, which execute nowhere on Windows. The set is the same as ae84478d:
  - `RcBridgeProbePlatformTests.Linux_probe_does_not_report_a_measured_zero` and 2 `ZombieCensusPlatformTests` cases (g120)
  - 4 `PwshPrivateCacheTests` cases (g128)
  - 7 `UnixPtyDeliveryEnvelopeTests` cases (agents-pty g1)
- **Idle budget**: no chunk hit it. The two timeouts are the hang detector, not an idle kill. The supervisor's highest `idleMinutes` was 9.77, at 20:01:29Z, with no kill.
- **timing-read-refused**: 21 chunks, a metrics gap only. The verdict is kept and the timing read budget was exceeded. They are antiphon g10, 11, 12, 14, 15, 18, 29, 32, 47, 51, 58, 59, 64, 88, 114 to 117 and profiles 411, 420, 1536.
- Noise: antiphon g8's log carries an Npgsql `EndOfStreamException` from a background delegation sweep at 19:33:00Z. The chunk passed.

## 4. Timing

Read from the finished timing root `...\logs\timing-0b292fa4bc3b40158e8950fb17804a21`.

| Metric | Value |
|---|---|
| Total wall | 17.10 h by the summary (12:14:01Z to 05:20:20Z). Chunks ran 12:24:33Z to 05:19:36Z, 16.92 h. Builds and discovery took about 10.5 min. |
| Planned | 51.42 h over 1,161 planned chunks |
| Sum of chunk run wall | 16.87 h. `nativeActiveMax=1`, so chunks ran serially. |
| **elapsed / planned** | **0.329** (wall/planned 0.328). ae84478d was 0.325. |
| Over 30 minutes | **none** |

The worst was antiphon g130 at 1,678.9 s (28.0 min), close to the line.

### 12 slowest chunks by run seconds

| Chunk | Run s | Planned s |
|---|---:|---:|
| antiphon all-group-130 | 1678.9 | 899.6 |
| antiphon all-group-19 | 1192.3 | 1277.0 |
| antiphon all-group-132 | 1096.0 | 91.0 |
| antiphon all-group-109 | 1088.8 | 899.7 |
| antiphon all-group-128 | 1009.3 | 899.5 |
| antiphon all-group-125 | 989.2 | 1420.0 |
| antiphon all-group-131 | 970.1 | 265.0 |
| antiphon all-profile-2186 | 848.5 | 257.8 |
| antiphon all-group-28 | 722.9 | 899.9 |
| antiphon all-group-45 | 653.3 | 900.0 |
| antiphon all-group-134 | 620.3 | 889.7 |
| antiphon all-group-129 | 599.6 | 900.0 |

These chunks ran more than twice their plan (plan of at least 60 s):

- antiphon g132: 12.0x (1,096 s against 91 s)
- session-runner g4: 8.6x (the hang)
- antiphon g131: 3.7x
- antiphon profile-2186: 3.3x
- session-runner g3: 3.1x
- antiphon g97: 2.0x

Per suite, run hours against planned hours:

| Suite | Run h | Planned h |
|---|---:|---:|
| antiphon | 12.44 | 33.47 |
| e2e | 3.29 | 17.41 |
| session-runner | 0.85 | 0.40 |

Everything else is under 0.15 h.

A Full relaunch runs every chunk, because seeds only reorder. Expect about 17 h again.

## 5. Seed for the next cut (current origin/master)

`git ls-remote origin refs/heads/master` = `ab7af12c699039270af7e22a5d7fd794035bc325`, the same as at dispatch. `56e96eabf` is an ancestor. The range `56e96eabf698..ab7af12c6` has 100 commits and 82 changed paths:

- tests/Antiphon.Tests: 17
- docs: 30
- scripts: 20, among them 7 `scripts/test-*.ps1`
- server/Application: 7
- server/Infrastructure: 2
- tests/Antiphon.SessionRunner.Tests: 4
- `tests/derived-impact-index.json` and `tests/linux-test-roster.json`

No `client/` path changed.

### Method

This is the same method as task 25e35cdf's `seed-cumulative.json`.

1. **Final export.** This command ran against the finished run directory:

   ```
   scripts/export-nightly-stopped-run-seed.ps1 -RunDirectory ...\2026-11-12-1213-0b292fa4 -TimingRoot ...\timing-0b292fa4... -SourceCandidateId rc-2026-11-18-56e96eabf698 -SourceSha 56e96eabf... -SourceRunId 0b292fa4...
   ```

   It read only a bounded prefix of `summary.json`. The output is `seed-0b292fa4-final-raw.json` (SHA-256 `90b0f456...93d2`): 1,161 chunks, all completed, and 1 failed row.
2. **Dirty set.** `scripts/impacted-classes.ps1 -Range 56e96eabf698..ab7af12c6 -Json` ran from master. It gave 157 classes, 157 derived classes and 4 owed paths (`docs/ops-http.md` 154, `SupervisionSettings.cs` 147, `SessionMessageQueueService.cs` 125, `PtyHostLeakSweep.cs` 7); `skippedByCap` was empty. The dirty set is the named classes plus derived classes plus each owed path's `first` sample. The unnamed owed remainder is not expanded, the same as before. The `scripts` suite counts as dirty because `scripts/test-*.ps1` changed. `client` does not.
3. **Builder.** `C:\Antiphon\releases\task-cf82bfb0\build-cumulative-seed.ps1` is 25e35cdf's with only the base and the paths changed. It ran with `-WorktreeRoot` set to this master worktree. Its rules:
   - A chunk that completed with no failed row and no dirty class is **passed** (credit `0b292fa4`).
   - A completed chunk with a failed row is **failed-first**.
   - A harness-hung chunk (g118, sr g4) gets **no credit**, and its classes do not count as re-run.
   - A prior failed row is kept only if its class did not re-run to completion.

### Result

| Count | Value |
|---|---:|
| Chunks in seed (0b29 plan) | 1,161 (plus `client` and `scripts`, which are never seeded) |
| **Seeded as passed** | **729**, all from 0b292fa4. Task 25e35cdf had 915. |
| **Newly invalidated by `56e96eabf..ab7af12c6`** | **429** (antiphon 411, session-runner 14, e2e 2, agents-pty 1, server-git 1) |
| Failed-first | **1 chunk, 1 row**: antiphon g82 `CardFileGitFailureAcceptanceTests.Git_failures_are_sanitized_preserve_HEAD_and_retry_after_owned_fault_is_removed`. Its fix is on master. |
| Harness-hung, no credit | 2: antiphon g118 and session-runner g4. Both fixes are on master. |
| Prior failed rows (25e35cdf seed) | 9; all dropped, because every class re-ran to completion; 0 kept |
| Seeded from the prior seed | 0. Every chunk completed in 0b29. |
| Rerun total (not seeded) | **432** = 429 invalidated + 1 failed-first + 2 hung, plus `client`, `scripts` and any chunk the master plan adds |

The antiphon 411 breaks down as 59 `all-group`, 339 `all-profile` and 13 `all-split` chunks. Most of it comes from a few per-test profile families whose class is dirty:

- `RemoteScriptContractTests`: 185 chunks
- `LandGraphEvaluationTests`: 72
- `AgentTaskDispatchBaseGuardTests`: 53
- `AgentTaskDispatchFailureTests`: 13
- `Server2ProductionServerContractTests`: 12

Only 3 chunks were invalidated by an owed sample alone.

The non-antiphon chunks invalidated are:

- session-runner g1, g2, g3, g5 and g6
- session-runner profiles 67 to 72 and 231 to 233
- e2e g1 and g8
- agents-pty g2
- server-git

Per suite (seeded / failed-first / invalidated / hung):

| Suite | Seeded | Failed-first | Invalidated | Hung |
|---|---:|---:|---:|---:|
| antiphon | 472 | 1 | 411 | 1 |
| session-runner | 86 | 0 | 14 | 1 |
| e2e | 74 | 0 | 2 | 0 |
| agents-pty | 28 | 0 | 1 | 0 |
| pty-host | 53 | 0 | 0 | 0 |
| markdown-pdf | 12 | 0 | 0 | 0 |
| messaging, observability, resilience, git-in-process | 1 each | 0 | 0 | 0 |
| server-git | 0 | 0 | 1 | 0 |

### Seed

`C:\Antiphon\releases\task-cf82bfb0\seed-cumulative.json` (727,161 bytes):

- whole-file digest (`-PriorFailuresDigest`): `64f66b7b215f2635b3cca3c82240c59d4e4c8cf90f29fa006b188d3985a3ce9d`
- `contentHash`: `be697840991567e063657ce4d288d4ed413aceb0c3d98b5633e671f24224f78d`
- `sourceCandidateId`: `rc-2026-11-18-56e96eabf698`
- `sourceRunId`: `0b292fa4bc3b40158e8950fb17804a21`
- `sourceSha`: `56e96eabf698...`
- master's `Read-NightlyStoppedRunSeed` accepted it with 1,161 chunks

The same folder holds:

- `seed-cumulative-report.json`: counts and the chunk lists
- `seeded-from-0b292fa4.txt`
- `impacted-classes.json` (SHA-256 `4a6767be...aa05`)
- `changed-files.txt`
- `seed-0b292fa4-final-raw.json`
- `build-cumulative-seed.ps1`

A launch on a new cut would pass:

```
-PriorFailuresDigest 64f66b7b215f2635b3cca3c82240c59d4e4c8cf90f29fa006b188d3985a3ce9d -AcceptCrossCandidateSeed -SeedSourceCandidateId rc-2026-11-18-56e96eabf698
```

It keeps `-SelectionMode Full`, so the seed only orders the run. Expect the failed-first g82 chunk first, then the hung g118 and sr g4 chunks and the invalidated chunks, before the 729 carried chunks.

## Authority

This was covered by the standing rule: "every rc run result is read by a delegate; failures get a Debug task". The operator's words on 2026-11-11 were "rc cut and rc fixes ... rerun on prior failures".

Nothing was launched. No Debug task is owed for this run, because every red row is in the known set and its fix is already on master.
