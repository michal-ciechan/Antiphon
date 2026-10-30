# Run 28bbb58e: the apparent 20-second post-test exit delay

Investigated 2026-10-30, task afedcf4e, native Windows, task base
`0e7dd390520a43ab330396a72e9085a8691ac332`.

**The reported 114.2 minutes are real elapsed time, but predominantly harness
bookkeeping after process exit, not processes lingering for 20 seconds.** The
original analysis subtracted the MTP session-end timestamp from `tests.log`'s
last-write timestamp. It missed the harness's later rewrite of that file.

## Evidence roots and measurement

All historical files were read only. Define:

- `L` = `C:\Antiphon\releases\candidates\rc-2026-10-27-4563e52529b2\logs\2026-10-28-1013-28bbb58e`.
- `T` = its sibling `timing-28bbb58edca04b0ebb7afc914b74d386`.
- `R` = `C:\Antiphon\releases\checkout-5906dbf2`, source
  `4563e52529b23052856cb0f1e0d01b383cf7590f`.
- Local derived evidence = `C:\Antiphon\worktrees\card-task-afedcf4e\.antiphon\afedcf4e`.

The preceding report is
`origin/feat/card-task-2714a65f:docs/investigations/2026-10-29-rc-run-28bbb58e-time-breakdown.md`,
section "Chunk wall decomposed". Its interval is explicitly `ended` to
`tests.log` written; it is neither TRX finish to root exit nor an observed job-exit interval.

Reconstructed from all 384 chunks with TRX and completed MTP sessions:

| Interval | Total seconds | Minutes | Median seconds | Range seconds |
|---|---:|---:|---:|---:|
| MTP session end to log last write (original metric) | 6852.776 | 114.213 | 19.604 | 0.551–64.418 |
| MTP session end to log creation | 184.335 | 3.072 | 0.267 | 0.158–41.564 |
| Log creation to later log rewrite | 6668.441 | 111.141 | 19.337 | 0.175–32.988 |
| `runWallSeconds - hostWallSeconds` | 6570.297 | 109.505 | 19.303 | 0.189–32.976 |
| Log last write to chunk `completedAt` | 122.698 | 2.045 | 0.118 | 0.016–3.497 |

These are overlapping measurements, not additive phases. `hostWallSeconds` is
stored per `(suite, chunk)` in `T\groups-executed.json`; `runWallSeconds` is in
`L\summary.json:timingChunks[]` and each timing sidecar. The difference includes
the manifest call plus surrounding preparation/PowerShell work, not exclusively
one function. The source stops the host stopwatch before that call but the run
stopwatch afterwards. Log creation is an upper bound on job exit, not an exact
exit timestamp. Historical root/descendant exit stamps were not persisted.

## Mechanism reconstructed from source

Line references in this section are to the run source `R` at the SHA above:

1. `scripts/lib/nightly-owned-process.cs:170` waits until root exit and job count
   zero; `:188` confirms cleanup; `:200` schedules the final log writer, and
   `:206` creates the log only after both output readers reach physical EOF.
2. `scripts/lib/nightly-tests-impl.ps1:1211` calls the owner, `:1218` stops
   `$phaseWatch`, then `:1221` calls `Write-TestPhaseExecutedManifest` with its
   elapsed time as `HostWallSeconds`. Only `:1226` stops `$runWatch`.
3. `scripts/lib/test-phase-evidence.ps1:1361` reloads the entire cumulative
   `groups-executed.json`; `:1368` reconstructs every preceding chunk and its
   outcomes; `:1384` serializes and replaces the whole document for every chunk.
   `ConvertTo-TestPhaseJsonText` (`:106`) recursively visits all values;
   `ConvertTo-TestPhaseJsonString` (`:88`) escapes strings one character at a time
   in PowerShell. This work grows with preceding results, not just the current test count.
4. `scripts/lib/nightly-tests-impl.ps1:1235` reads the completed log and `:1239`
   rewrites it with `C487_INVOCATION=...` prepended. **This is the last-write time
   used by the original report.** Coverage verdict and timing updates follow.

Thus 97.31% of the original measured interval is after the log has already been
created by the exited-and-drained owner. The 20-second process-linger conclusion
does not follow from that timestamp. The historical aggregate has a separate
small upper-bound remainder, including a few genuine long tails, that this
finding does not erase.

No 20-second sleep/drain/grace appears in the native launch/wait path. Its output
drain cap is **15,000 ms** (`nightly-owned-process.cs:61`, `:220`) and marks a
timeout/error; it is not an unconditional wait. Job cleanup also has a 15-second
failure bound (`:190`). Ordinary polls sleep 10 ms. The native qualification
invocation prefers the apphost `.exe`, falling back to `dotnet exec` (`nightly-tests-impl.ps1:527`);
it does not use `dotnet test`.

## Ten historical chunks

Times are UTC, seconds after midnight on the indicated day rendered as clock
times. `create` is the owner's log creation; `rewrite` is the subsequent stamp
rewrite. The later timeline appendix retains exact diagnostic line references.

| Chunk | Tests | Day | Session ended | Log create | Log rewrite | Run minus host seconds |
|---|---:|---|---|---|---|---:|
| antiphon all-group-1 | 94 | Oct 28 | 10:26:15.279 | 10:26:15.655 | 10:26:15.830 | 0.189 |
| antiphon all-group-103 | 47 | Oct 28 | 20:53:23.879 | 20:53:24.149 | 20:53:39.373 | 14.884 |
| antiphon all-profile-46 | 1 | Oct 29 | 03:23:48.949 | 03:23:49.139 | 03:24:08.233 | 19.056 |
| session-runner all-group-1 | 1179 | Oct 29 | 04:30:23.981 | 04:30:24.269 | 04:30:46.288 | 20.953 |
| pty-host all-group-1 | 73 | Oct 29 | 04:49:25.402 | 04:49:29.975 | 04:49:49.732 | 19.726 |
| pty-host all-profile-1 | 1 | Oct 29 | 04:50:12.490 | 04:50:12.653 | 04:50:34.242 | 21.574 |
| agents-pty all-group-1 | 572 | Oct 29 | 05:09:21.975 | 05:09:22.238 | 05:09:44.659 | 22.393 |
| e2e all-split-64 | 2 | Oct 29 | 09:04:13.468 | 09:04:13.707 | 09:04:40.628 | 26.899 |
| markdown-pdf all-profile-1 | 1 | Oct 29 | 09:04:43.194 | 09:04:43.370 | 09:05:10.229 | 26.846 |
| observability all | 12 | Oct 29 | 09:11:00.649 | 09:11:00.889 | 09:11:29.341 | 28.440 |

This is neither constant nor proportional to the current chunk size: the first
94-test chunk pays 0.189 seconds outside the owner; a late one-test chunk pays
26.846 seconds. The executed manifest accumulates across suites.

## Investigation status

The measurement error and location after the owner are reconstructed from
stored evidence. Native replay and isolation of the cumulative serialization
cost are in progress; no production fix or test change has been made.

Generated evidence is ignored: `analyze.py`, `chunks.csv`, `chunks.json`,
`totals.json`, `samples.json`, `sample-timelines.txt`, and `input-hashes.json`.
The analyzer reads historical files and writes only to its own directory;
`python .antiphon/afedcf4e/analyze.py` regenerates these observations.
