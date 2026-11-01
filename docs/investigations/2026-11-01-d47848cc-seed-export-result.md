# d47848cc stopped-run seed export on real data (2026-11-01)

Task 78641295. Worktree at rc/2026-10-27 tip `6e4f208f60c9247c75a87f205b9b48b3e4ba54fb` (top-up 52).
Source run: `d47848cce896471db1cc8ca346707cad`, candidate `rc-2026-10-27-cde8def41638`,
source sha `cde8def4163829aa5b867df6ec625433be2522cb`. The finished run directories were read only.
No test, run, runner, AppHost or process was started or stopped.

## Outcome

- Export worked on Windows. It took 100.95 s against the 60 s synthetic target, and it exited 0.
- The seed is outside `C:\Antiphon`, in the task scratchpad:
  `...\scratchpad\seed\d47848cc.json`, 521,406 bytes,
  SHA256 `bb3c427c280930fba3c372fed4bf8a8416b0726e1c23e33c63717e40fee0b72e`.
- The read-only baseline was identical before the export, after it, and after the ordering probes.
- On the real 841-chunk plan the ordering reason was `operator-seed-order`. The schedule is a permutation: 841 planned, 841 scheduled, 841 unique.
- A one-byte corrupted copy fell back to the exact canonical order (`history-digest-mismatch` or `seed-hash-mismatch`).
- Finding for a decision: failed-first works at class level. The 25 chunks that really failed become 235 P0 chunks, about 12.1 h of serial planned cost. The last truly failed chunk is at position 234 (see below).

## Read-only baseline

Recursive count, all files and directories, under the two finished-run directories.
The brief's 3,265 and 1,699 are smaller counts and were not a recursive count.

| Directory | Files | Dirs | Bytes | Newest file (UTC) | Fingerprint (sha256 of sorted path+len+mtime) |
|---|---:|---:|---:|---|---|
| `logs\2026-10-30-0824-d47848cc` | 4,960 | 3,372 | 768,919,630 | 2026-10-31T10:28:10.1166947Z `summary.json` | `b534e8ec8c0001218c589158b4c4de851fa5a611c478d8097ba78015efa7397a` |
| `logs\timing-d47848cce896471db1cc8ca346707cad` | 3,167 | 684 | 320,072,418 | 2026-10-31T10:28:05.9177179Z `summary.md` | `35ab97f1ee418d20181721e6fe364d21509c63d70fe4e9092fa2cb461826e853` |

The before, after-export and final rows had the same counts, bytes, newest time, root mtime and fingerprint. Verdict: IDENTICAL.

## Export

```
pwsh -NoProfile -File scripts\export-nightly-stopped-run-seed.ps1 `
  -RunDirectory 'C:\Antiphon\releases\candidates\rc-2026-10-27-cde8def41638\logs\2026-10-30-0824-d47848cc' `
  -OutputPath <scratchpad>\seed\d47848cc.json `
  -SourceCandidateId 'rc-2026-10-27-cde8def41638' -SourceSha 'cde8def4163829aa5b867df6ec625433be2522cb'
```

The timing sibling was found through `summary.json` `timingRoot`. No `-TimingRoot` was passed. Output: `EXPORT_EXIT:0`, `EXPORT_SECONDS:100.953`.
The Windows file-index path (A-6) ran without a refusal.

## Seed contents

- format `antiphon-nightly-stopped-seed-v1`, seedKind `operator-seed-from-stopped-run`
- sourceRunId `d47848cce896471db1cc8ca346707cad`, sourceCandidateId `rc-2026-10-27-cde8def41638`, sourceSha `cde8def41638...22cb`
- createdAtUtc 2026-11-01T02:04:52Z, sourceObservedAtUtc 2026-10-31T10:24:14.18Z
- The ingest window is 14 days on both stamps, so the seed goes stale at about 2026-11-14T10:24Z.
- contentHash `81b51045ac03b36aff92206a4c4b0482ee2265b108733cee4d962709adb7b761`
- 841 chunks, all with `membershipSource=plan`. 48 failedTests in 24 distinct suite/class pairs, all with outcome `Failed`. 1,690 evidence digests.

Class per chunk. "failed" means any failed row. Otherwise: never-run if not started, unfinished if not completed, passed otherwise.

| Lane | Total | Failed | Never-run | Unfinished | Passed |
|---|---:|---:|---:|---:|---:|
| antiphon | 638 | 13 | 0 | 8 | 617 |
| e2e | 75 | 7 | 0 | 0 | 68 |
| agents-pty | 9 | 1 | 0 | 1 | 7 |
| markdown-pdf | 11 | 0 | 0 | 0 | 11 |
| session-runner | 53 | 2 | 0 | 0 | 51 |
| pty-host | 53 | 2 | 0 | 0 | 51 |
| messaging | 1 | 0 | 0 | 0 | 1 |
| observability | 1 | 0 | 0 | 0 | 1 |
| **All** | **841** | **25** | **0** | **9** | **807** |

### Reconciliation with the summary (independent TRX scan)

841 top-level TRX files, none open: 22,780 results, 58 Failed, 63 NotExecuted. This matches the summary.

- **58 failed rows map to 48 seed failedTests.** The seed keys failures by chunk+class+method, so parameterised cases collapse. For example, `antiphon/all-group-80` `AgentReportStoreBudgetTests` has 20 TRX rows and 10 methods. The 58 rows dedupe to exactly 48 file+class+method keys. All 25 chunks with a failed TRX row are the seed's 25 failed chunks.
- **The 63 NotExecuted rows sit in 14 chunks.** 43 are in the 9 unfinished chunks and 20 are in 5 failed chunks, which are therefore also `completed=false`.
  - Unfinished chunks: antiphon `all-group-103` (35), `all-group-3`, `all-group-29`, `all-group-123`, `all-profile-767`, `all-profile-1616`, `all-profile-1803`, `all-profile-1804` (1 each), and agents-pty `all-group-1` (1).
  - Failed chunks with NotExecuted rows: antiphon `all-group-104` (11), `all-group-111` (4), `all-group-124` (1), `all-group-80` (1), and pty-host `all-group-1` (3).
  - None of the unfinished chunks carries an `unfinishedReason`. They are unfinished only because of NotExecuted rows (A-10).
- **Failed chunks:**
  - antiphon: `all-group-48`, 79, 80, 104, 105, 110, 111, 120, 124, 126, 128, and `all-profile-1716`, `all-profile-1762`
  - e2e: `all-split-1`, 2, 3, 4, 32, 33 (`AgentTaskLandDeliveryE2ETests`) and `all-split-63` (`WorktreeRetirementDeliveryE2ETests`)
  - pty-host: `all-group-1`, `all-group-2`
  - session-runner: `all-group-4`, `all-group-6`
  - agents-pty: `all-group-3`

## Ordering proof on real data

There is no plan-only mode. `nightly-run.ps1 -WhatIf` takes the run lock, writes run state under StateRoot and syncs the clone, then returns before the dispatch schedule is built.

The probe therefore called the production functions directly:
1. `Initialize-NightlyDispatchHistory` with `-AcceptCrossCandidateSeed`, `-SeedSourceCandidateId rc-2026-10-27-cde8def41638` and the seed digest. No owned copy was made.
2. `Resolve-NightlyPriorFailures ... Full`.
3. `New-NightlyDispatchSchedule` with the candidate sha `6e4f208f6...`.

Prepared suites came from the run's real `groups-planned-*.json`, in nightly suite order. Nodes came from `plannedTestIds`. This is d47848cc's own plan, so the class sets equal the seed's. A new candidate's discovered plan may regroup, and class mapping is what covers that.

Probe artefact: `messaging/all` and `observability/all` have no planned classes or ids. With no discovery nodes in the probe they sort as untimed P1 (positions 236-237). In a real run, discovery nodes would map them to their seed classes, which puts them in P2.

Result:
- orderingReason `operator-seed-order: unknown timestamps retain original order; known timestamps oldest first`
- 24 resolved priority members, 0 unresolved
- 841/841, unique, permutation=True
- Wall time 62 s

| Band | Positions | Chunks | Holds |
|---|---|---:|---|
| P0 | 1-235 | 235 | all 25 truly failed chunks, plus every other chunk that holds a failed class (by lane: antiphon 192, e2e 38, pty-host 2, session-runner 2, agents-pty 1) |
| P1 | 236-331 | 96 | 8 non-P0 unfinished chunks, every chunk of their split classes (`LandGraphEvaluationTests` 64 chunks, `RollingVolumeRecycleScriptTests` 25), and the 2 probe-artefact lanes |
| P2 | 332-841 | 510 | fully passed classes, oldest first |

The bands are monotone.

### Finding: P0 is class-wide, so failures past the first 11 wait behind 210 passed chunks

The failed class `RemoteScriptContractTests` is split into 181 per-method `all-profile-*` chunks. Only 2 of them failed (1716, 1762). `AgentTaskLandDeliveryE2ETests` spans 36 e2e split chunks, 31,800 planned s, and 6 of them failed. `WorktreeRetirementDeliveryE2ETests` spans 2.

P0 therefore holds 235 chunks with 43,466 planned s (12.1 h of 49.7 h). P0 sorts oldest-first across all lanes. The truly failed chunks land at positions:

`1-11` (antiphon groups), `121` (profile-1716), `167` (profile-1762), `193-197` (session-runner, pty-host, agents-pty), `198-201, 229, 230, 234` (e2e).

There are 40,966 serial-equivalent planned seconds before position 234. Lanes run concurrently, so wall time is lower.

This is the designed rule: the round-3 review's "a split failed class: all chunks P0", A-9. The seed numbers are right. Whether failures should lead at method or chunk level instead of class level is a decision for the operator.

### First 40 of the ordered schedule

```
  1. antiphon all-group-48       failed      P0  last 2026-10-30 15:51:26
  2. antiphon all-group-79       failed      P0  2026-10-30 18:35:31
  3. antiphon all-group-80       failed      P0  2026-10-30 18:38:49
  4. antiphon all-group-104      failed      P0  2026-10-30 19:45:14
  5. antiphon all-group-105      failed      P0  2026-10-30 19:47:13
  6. antiphon all-group-110      failed      P0  2026-10-30 20:44:15
  7. antiphon all-group-111      failed      P0  2026-10-30 20:48:44
  8. antiphon all-group-120      failed      P0  2026-10-30 21:59:57
  9. antiphon all-group-124      failed      P0  2026-10-30 22:48:40
 10. antiphon all-group-126      failed      P0  2026-10-30 22:54:35
 11. antiphon all-group-128      failed      P0  2026-10-30 22:55:21
 12. antiphon all-profile-1607   passed      P0  2026-10-31 02:37:32  (RemoteScriptContractTests)
 13-20. antiphon all-profile-1608..1615  passed  P0  02:37:52..02:40:07
 21. antiphon all-profile-1616   unfinished  P0  2026-10-31 02:40:26
 22-40. antiphon all-profile-1617..1635  passed  P0  02:40:47..02:51:50
```

Full per-position CSV: scratchpad `order-real.csv`.

### Corrupted copy (one byte)

A one-byte flip at offset 379 changed the `:14` seconds digit of `sourceObservedAtUtc` inside the payload. The copy's sha256 is `50378b64...b395`.

| Case | Fallback reason | Order |
|---|---|---|
| Corrupt copy with the original digest (operator `-PriorFailuresDigest`) | `historyFallbackReason=history-digest-mismatch` | identical to canonical, 841/841, 0 priorFailure |
| Corrupt copy with its own digest | `historyFallbackReason=seed-hash-mismatch` | identical to canonical |
| Valid ingest, then an in-memory one-byte edit of `sourceSha` (to `G...`) | `orderingReason=seed-candidate-malformed` | identical to canonical |

Corrupted bytes are refused at ingest, so in production the reason appears as the schedule's `historyFallbackReason`. `orderingReason` carries the reason only when a structurally bad record gets past ingest.
