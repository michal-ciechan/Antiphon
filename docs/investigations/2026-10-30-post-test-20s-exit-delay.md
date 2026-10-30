# Run 28bbb58e: the apparent 20-second post-test exit delay

Investigated 2026-10-30, task afedcf4e, native Windows, task base
`0e7dd390520a43ab330396a72e9085a8691ac332`.

**Confirmed: the reported 114.2 minutes are real elapsed time, but predominantly harness
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

There is no manifest-publication phase column. Fixture setup/teardown,
distillation and build-slot wait are null throughout the 387 timing rows.
Process spawn/wait are populated for 23 rows, but represent per-test spans,
not outer job exit/drain (antiphon group 60 has 8.714/189.957 seconds).
The final `T\tests.csv` has zero in all db/host/process/fs/network/fixture
columns; its incomplete aggregation cannot supply outer-phase attribution.

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

## Native reproduction

Four serial launches used the **historical retained binary and historical
launcher code**, alternating an empty timing root with one preseeded from the
first 240 entries of the historical executed manifest. That is the prefix
immediately before `pty-host/all-profile-1`: 21,512 prior outcomes. The final
manifest has 385 chunks and is 1,712,308 bytes. Only the copied prefix was
changed; the original run remained read only.

Each launch selected the same historical one-test chunk:

```text
/*/Antiphon.PtyHost.Tests/Card0490NativeExecutionTests/Cancellation_cannot_accept_even_complete_frames
```

Each executed **1 Passed, 0 Failed, 0 NotExecuted**: **4 passed total**. Every
owner returned exit 0, `CleanupComplete=true`, exited root/job and drained output.

| Arm | Owner wall s | Session end to owner return s | Manifest publication s | Session end to stamped log s |
|---|---:|---:|---:|---:|
| empty1 (cold) | 5.640 | 0.571 | 0.415 | 1.021 |
| grown1 (240 prior chunks) | 2.839 | 0.221 | 31.239 | 31.470 |
| empty2 | 2.025 | 0.247 | 0.020 | 0.270 |
| grown2 (240 prior chunks) | 1.507 | 0.188 | 24.147 | 24.339 |

These are owner-return measurements, not exact root-exit timestamps. Owner
return follows root/job exit and output drain. The populated arms reproduce
the large **post-owner** delay without a process lingering through it. Recorded
output drain was 20.5–32.3 ms across the four launches.

The retained `native-probe.ps1` uses the real `Resolve-NightlyTestHost`,
`Get-NightlyNativeExecutionArguments`, `Get-NightlySafeChildEnvironment`,
`Enable-TestPhaseExecutionEnvironment`, `Invoke-NightlyOwnedProcess`, and
`Write-TestPhaseExecutedManifest`, followed by the invocation-stamp rewrite.
The historical owner uses its real suspended launch, inherited stdout/stderr
pipes and Windows Job Object; no launch seam is installed. This is native
apphost execution, not `dotnet test`. Each arm's `invocation.json` retains
executable/argv/working directory. The common arguments are:

```text
--no-progress --no-ansi --report-trx --report-trx-filename <name>
--results-directory <arm> --diagnostic --diagnostic-output-directory <arm>/execution
--treenode-filter <above>
```

Differences from the original qualification: worktree-local binary/evidence
paths and working directory, a 120-second diagnostic watchdog, current inherited
safe environment and host load, and four isolated launches. The prior manifest
prefix was reconstructed from retained records. No project build or canonical
checkout execution was needed. All 218 copied binary-directory files were
hash-compared against the retained originals.

Host: DESKTOP-KTLKPIF, Windows 10.0.19045, 8 logical CPUs, PowerShell 7.6.9 /
driver .NET 10.0.14; test runtime .NET 9.0.22, TUnit 1.44.0, MTP 2.2.2.
`TESTINGPLATFORM_TELEMETRY_OPTOUT` remained absent. Background load was
uncontrolled; these two publication samples establish the mechanism, not its
p95 or a precise future saving.

Unedited `native-console.log` evidence:

```text
BUILD SLOT granted lease=bfd85613-606d-4992-ae68-d3251c2b2e01 waited=1s maxcpucount=4
PROBE empty1 owner=5.64s postSessionOwner=0.571s manifest=0.415s exit=0
PROBE grown1 owner=2.839s postSessionOwner=0.221s manifest=31.239s exit=0
PROBE empty2 owner=2.025s postSessionOwner=0.247s manifest=0.02s exit=0
PROBE grown2 owner=1.507s postSessionOwner=0.188s manifest=24.147s exit=0
BUILD SLOT released lease=bfd85613-606d-4992-ae68-d3251c2b2e01 held=83s
```

The optional `serialization-probe.ps1` breakdown of parse, normalization,
serialization CPU and atomic write **did not run**. The gate remained occupied
2/2 and timed out after five minutes; zero cases executed and no unleased retry
followed. Its console ends `BUILD SLOT timeout after 5m position=1`; the
enclosing shell reported exit 1. The precise division of the 24–31 seconds
between internal publication operations remains unmeasured. The whole call,
its input-size dependency, and its location after owner return are confirmed.
The character-by-character serializer is a source-identified contributor, not
an exclusively measured CPU attribution.

## Why the previous verifies did not reproduce it

The missing factor was the **accumulated manifest**, not apphost versus
`dotnet exec`. The b9561ea9 driver creates a fresh timing root at
`C:\Antiphon\worktrees\card-task-b9561ea9\.antiphon\b9561ea9\measure-chunk.ps1:10`
and publishes one chunk at `:35`. The a82885c3 driver creates fresh output at
`C:\Antiphon\worktrees\card-task-a82885c3\.antiphon\a82885c3\measure-chunk.ps1:6`,
sets its fresh timing root at `:16`, and publishes one chunk at `:41`.
Its 117-test cases were larger current chunks, not 240 prior accumulated
chunks. Both previous reports' short root-exit measurements remain valid.

The requested b9561ea9 `.antiphon/task-b9561ea9.md` is absent on its remote
branch. Its complete local report was read; the branch's committed report is
`docs/investigations/2026-10-29-rc-speedups-native-b9561ea9.md`. The a82885c3
report was read from its named remote branch. A missing path was not treated
as proof of a run.

## Remaining uncertainty and scope

- 379/384 historical session-end-to-log-creation intervals are under 0.5 s.
  The five larger bounds are session-runner group 4 (41.564 s), antiphon group
  78 (30.868 s), pty-host group 1 (4.573 s), antiphon split 14 (3.117 s), and
  e2e group 10 (3.060 s). Root exit, descendant exit, final drain and log-create
  latency cannot be separated historically. These are distinct tails.
- `runWallSeconds - hostWallSeconds` identifies approximately **109.5 minutes**
  of bookkeeping, including minor surrounding preparation. It is an
  optimization opportunity, not a demonstrated 114.2-minute saving. The
  3.072-minute pre-log-creation bound must not be credited to a manifest repair.
- Historical antivirus, disk contention, diagnostic-flush CPU and per-operation
  publication CPU are not logged. The replay does not prove these costs zero.
- The task base still has the cumulative publication mechanism at
  `scripts/lib/test-phase-evidence.ps1:1324`, `:1370`, `:1393`, with serializer
  functions at `:97`/`:115`. Historical launcher and owner match the base.
  This is not established as already fixed.
- No full qualification, product-code/policy/telemetry change, AppHost/runner
  change, tag or publication occurred. All launched children were joined.
  Historical files and diagnostic evidence were retained. Automatic approval
  review rejected removal of the local `native-repo` binary copy with only
  "blocked by policy", including a verified literal-path attempt. That ignored
  54,384,810-byte copy remains; no deletion workaround was attempted.

## Not done, noted

Candidate repair: avoid rebuilding/re-escaping the entire cumulative manifest per chunk at `scripts/lib/test-phase-evidence.ps1:1370`–`:1393` (task-base lines); target approximately 109.5 minutes of bookkeeping rather than promise all 114.2 minutes; red witness idea: preseed 240 historical chunks and run the same one-test apphost, measuring publication separately from owner exit against an empty-root control; no fix was designed or implemented.

## Provenance and rerun

The historical source files matched Git blobs at the run SHA:

| File under `scripts/lib` | Git blob |
|---|---|
| `nightly-tests-impl.ps1` | `195e7e8b0129114af2009822e94b1f0ab6ac6aa6` |
| `nightly-owned-process.cs` | `544d991af9e4bcce2f5bb9819631dced4742945a` |
| `test-phase-evidence.ps1` | `b54bac2b0cfbd9979fad53495cb85d2ed5c02a1f` |

Essential SHA-256 identities:

- `L\summary.json`: `53713781aa0e1554a50c730cb6fa35009ad07e5b31109bfd00f81b4c33c0c046`.
- `T\groups-executed.json`: `9db1b29578a3dadfef614b5a1cb6b5ffc5e315a126cf39cfcd1d11fb4271e932`.
- `Antiphon.PtyHost.Tests.dll`: `a1054be12c6b58fb670448df21df2677e1dc097dce1e4899e8eac49db09aa98c`.
- `Antiphon.PtyHost.Tests.exe`: `8880c2e50a18113e949bd72bf3a2065fa9d8c9e900ac190bf93f7200e0efc36e`.

All 32 initially hashed historical inputs (summary, manifest, ten each of
diag/TRX/log) were rehashed after reproduction and unchanged. The copied
binary directory matched all 218 original hashes. Native replay ran with
task report commit `50540e69233201ac8156ceda7e4fb1c14901438c` checked out,
explicitly importing the run-SHA launcher and retained binary. It did not
build the task tip or establish new release-qualification credit.

Ignored evidence includes `analyze.py`, `chunks.csv/json`, `totals.json`,
`samples.json`, `sample-timelines.txt`, `input-hashes.json`, `prepare.py`,
`native-probe.ps1`, the four arm directories with TRX/diag/owner/measurement/
invocation records, `native-summary.json`, `native-binary-hashes.json`,
`serialization-probe.ps1`, both console logs, `finalize-evidence.py`,
`validation.json`, and `timeline-appendix.md`.

The read-only historical reconstruction is rerun with
`python .antiphon/afedcf4e/analyze.py`. For another native replay, use a fresh
copy of the diagnostic directory (existing arm directories intentionally
refuse), run `prepare.py` to copy the retained binary and reconstruct the
240-entry prefix, then:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label afedcf4e-native-replay -SlotWaitMinutes 5 -- pwsh -NoProfile -File <fresh-diagnostic-root>/native-probe.ps1
```

No additional permission was needed under the caller's standing timing
authority. Next: Plan, based on the confirmed cumulative-publication mechanism.

## Last-minute timeline detail for the ten samples

Each diagnostic path below is relative to `L`; line numbers are one-based. All
listed tail events occur within the last 60 seconds before that chunk's
`completedAt`. TRX `Times.finish` is report-generation time, not process exit.
The last terminal test message can follow its TRX test end because delivery is
asynchronous. The shared HTML report path is overwritten by later chunks, so
its present mtime is not used. The diagnostic artifact announcement is used.
No later diagnostic event is recorded after the exit telemetry announcement.
The intervening manifest work has no timestamped console phase marker.

### antiphon-all-group-1

`antiphon-all-group-1-execution\log_261028102426928.diag`; date `2026-10-28`.

Last terminal test: `Concurrent_contexts_share_a_disposable_test_schema` (diag line 3700).

| Event | UTC | Evidence |
|---|---|---|
| Latest test end | 10:26:14.212 | `antiphon-all-group-1.trx`, maximum result `endTime` |
| Last terminal test message | 10:26:14.877 | diag:3699 timestamp, :3700 message |
| HTML artifact | 10:26:15.168 | diag:3709 timestamp, :3710 artifact |
| TRX artifact | 10:26:15.233 | diag:3712 timestamp, :3713 artifact |
| TRX finish field | 10:26:15.178 | `antiphon-all-group-1.trx:3` |
| Session ended | 10:26:15.279 | diag:3715 |
| Exit telemetry (last timestamped diagnostic event) | 10:26:15.286 | diag:3716 |
| Owner log created | 10:26:15.654 | `antiphon-all-group-1-tests.log` NTFS creation time |
| Stamped log last write | 10:26:15.829 | same file NTFS last-write time; invocation stamp on line 1 |
| Chunk completed | 10:26:16.398 | `antiphon-all-group-1.timing.json:1` `completedAt` |

### antiphon-all-group-103

`antiphon-all-group-103-execution\log_261028202124016.diag`; date `2026-10-28`.

Last terminal test: `repeat_two_then_default_then_three_rebuilds_the_same_project` (diag line 452).

| Event | UTC | Evidence |
|---|---|---|
| Latest test end | 20:53:23.592 | `antiphon-all-group-103.trx`, maximum result `endTime` |
| Last terminal test message | 20:53:23.633 | diag:451 timestamp, :452 message |
| HTML artifact | 20:53:23.770 | diag:699 timestamp, :700 artifact |
| TRX artifact | 20:53:23.832 | diag:702 timestamp, :703 artifact |
| TRX finish field | 20:53:23.780 | `antiphon-all-group-103.trx:3` |
| Session ended | 20:53:23.879 | diag:705 |
| Exit telemetry (last timestamped diagnostic event) | 20:53:23.887 | diag:706 |
| Owner log created | 20:53:24.149 | `antiphon-all-group-103-tests.log` NTFS creation time |
| Stamped log last write | 20:53:39.373 | same file NTFS last-write time; invocation stamp on line 1 |
| Chunk completed | 20:53:39.877 | `antiphon-all-group-103.timing.json:1` `completedAt` |

### antiphon-all-profile-46

`antiphon-all-profile-46-execution\log_261029032345271.diag`; date `2026-10-29`.

Last terminal test: `C1011_windows_backends_reach_ready_and_complete_prompt` (diag line 62).

| Event | UTC | Evidence |
|---|---|---|
| Latest test end | 03:23:48.692 | `antiphon-all-profile-46.trx`, maximum result `endTime` |
| Last terminal test message | 03:23:48.753 | diag:61 timestamp, :62 message |
| HTML artifact | 03:23:48.873 | diag:91 timestamp, :92 artifact |
| TRX artifact | 03:23:48.929 | diag:94 timestamp, :95 artifact |
| TRX finish field | 03:23:48.887 | `antiphon-all-profile-46.trx:3` |
| Session ended | 03:23:48.949 | diag:97 |
| Exit telemetry (last timestamped diagnostic event) | 03:23:48.956 | diag:98 |
| Owner log created | 03:23:49.139 | `antiphon-all-profile-46-tests.log` NTFS creation time |
| Stamped log last write | 03:24:08.232 | same file NTFS last-write time; invocation stamp on line 1 |
| Chunk completed | 03:24:08.267 | `antiphon-all-profile-46.timing.json:1` `completedAt` |

### session-runner-all-group-1

`session-runner-all-group-1-execution\log_261029041730971.diag`; date `2026-10-29`.

Last terminal test: `Each_key_dimension_invalidates_independently` (diag line 7138).

| Event | UTC | Evidence |
|---|---|---|
| Latest test end | 04:30:23.412 | `session-runner-all-group-1.trx`, maximum result `endTime` |
| Last terminal test message | 04:30:23.455 | diag:7137 timestamp, :7138 message |
| HTML artifact | 04:30:23.730 | diag:7145 timestamp, :7146 artifact |
| TRX artifact | 04:30:23.963 | diag:7148 timestamp, :7149 artifact |
| TRX finish field | 04:30:23.745 | `session-runner-all-group-1.trx:3` |
| Session ended | 04:30:23.981 | diag:7151 |
| Exit telemetry (last timestamped diagnostic event) | 04:30:23.988 | diag:7152 |
| Owner log created | 04:30:24.268 | `session-runner-all-group-1-tests.log` NTFS creation time |
| Stamped log last write | 04:30:46.288 | same file NTFS last-write time; invocation stamp on line 1 |
| Chunk completed | 04:30:49.785 | `session-runner-all-group-1.timing.json:1` `completedAt` |

### pty-host-all-group-1

`pty-host-all-group-1-execution\log_261029044901913.diag`; date `2026-10-29`.

Last terminal test: `A_host_whose_exit_manifest_cannot_be_saved_still_exits_on_its_linger_TTL` (diag line 494).

| Event | UTC | Evidence |
|---|---|---|
| Latest test end | 04:49:25.258 | `pty-host-all-group-1.trx`, maximum result `endTime` |
| Last terminal test message | 04:49:25.262 | diag:493 timestamp, :494 message |
| HTML artifact | 04:49:25.346 | diag:499 timestamp, :500 artifact |
| TRX artifact | 04:49:25.393 | diag:502 timestamp, :503 artifact |
| TRX finish field | 04:49:25.354 | `pty-host-all-group-1.trx:3` |
| Session ended | 04:49:25.402 | diag:505 |
| Exit telemetry (last timestamped diagnostic event) | 04:49:25.406 | diag:506 |
| Owner log created | 04:49:29.975 | `pty-host-all-group-1-tests.log` NTFS creation time |
| Stamped log last write | 04:49:49.731 | same file NTFS last-write time; invocation stamp on line 1 |
| Chunk completed | 04:49:49.939 | `pty-host-all-group-1.timing.json:1` `completedAt` |

### pty-host-all-profile-1

`pty-host-all-profile-1-execution\log_261029045011927.diag`; date `2026-10-29`.

Last terminal test: `Cancellation_cannot_accept_even_complete_frames` (diag line 62).

| Event | UTC | Evidence |
|---|---|---|
| Latest test end | 04:50:12.336 | `pty-host-all-profile-1.trx`, maximum result `endTime` |
| Last terminal test message | 04:50:12.369 | diag:61 timestamp, :62 message |
| HTML artifact | 04:50:12.447 | diag:67 timestamp, :68 artifact |
| TRX artifact | 04:50:12.480 | diag:70 timestamp, :71 artifact |
| TRX finish field | 04:50:12.455 | `pty-host-all-profile-1.trx:3` |
| Session ended | 04:50:12.490 | diag:73 |
| Exit telemetry (last timestamped diagnostic event) | 04:50:12.493 | diag:74 |
| Owner log created | 04:50:12.652 | `pty-host-all-profile-1-tests.log` NTFS creation time |
| Stamped log last write | 04:50:34.241 | same file NTFS last-write time; invocation stamp on line 1 |
| Chunk completed | 04:50:34.261 | `pty-host-all-profile-1.timing.json:1` `completedAt` |

### agents-pty-all-group-1

`agents-pty-all-group-1-execution\log_261029050754808.diag`; date `2026-10-29`.

Last terminal test: `A_body_that_mangled_live_reaches_a_js_runtime_peer_whole` (diag line 3688).

| Event | UTC | Evidence |
|---|---|---|
| Latest test end | 05:09:21.647 | `agents-pty-all-group-1.trx`, maximum result `endTime` |
| Last terminal test message | 05:09:21.653 | diag:3687 timestamp, :3688 message |
| HTML artifact | 05:09:21.832 | diag:3693 timestamp, :3694 artifact |
| TRX artifact | 05:09:21.962 | diag:3696 timestamp, :3697 artifact |
| TRX finish field | 05:09:21.846 | `agents-pty-all-group-1.trx:3` |
| Session ended | 05:09:21.975 | diag:3699 |
| Exit telemetry (last timestamped diagnostic event) | 05:09:21.980 | diag:3700 |
| Owner log created | 05:09:22.237 | `agents-pty-all-group-1-tests.log` NTFS creation time |
| Stamped log last write | 05:09:44.658 | same file NTFS last-write time; invocation stamp on line 1 |
| Chunk completed | 05:09:46.801 | `agents-pty-all-group-1.timing.json:1` `completedAt` |

### e2e-all-split-64

`e2e-all-split-64-execution\log_261029090302090.diag`; date `2026-10-29`.

Last terminal test: `C459_LaunchReservationRecoveryRetainsOwnership` (diag line 5246).

| Event | UTC | Evidence |
|---|---|---|
| Latest test end | 09:04:13.303 | `e2e-all-split-64.trx`, maximum result `endTime` |
| Last terminal test message | 09:04:13.321 | diag:5245 timestamp, :5246 message |
| HTML artifact | 09:04:13.417 | diag:9979 timestamp, :9980 artifact |
| TRX artifact | 09:04:13.455 | diag:9982 timestamp, :9983 artifact |
| TRX finish field | 09:04:13.423 | `e2e-all-split-64.trx:3` |
| Session ended | 09:04:13.468 | diag:9985 |
| Exit telemetry (last timestamped diagnostic event) | 09:04:13.472 | diag:9986 |
| Owner log created | 09:04:13.706 | `e2e-all-split-64-tests.log` NTFS creation time |
| Stamped log last write | 09:04:40.628 | same file NTFS last-write time; invocation stamp on line 1 |
| Chunk completed | 09:04:40.918 | `e2e-all-split-64.timing.json:1` `completedAt` |

### markdown-pdf-all-profile-1

`markdown-pdf-all-profile-1-execution\log_261029090442470.diag`; date `2026-10-29`.

Last terminal test: `Invalid_manifest_is_rejected_without_creating_output` (diag line 62).

| Event | UTC | Evidence |
|---|---|---|
| Latest test end | 09:04:43.008 | `markdown-pdf-all-profile-1.trx`, maximum result `endTime` |
| Last terminal test message | 09:04:43.031 | diag:61 timestamp, :62 message |
| HTML artifact | 09:04:43.106 | diag:67 timestamp, :68 artifact |
| TRX artifact | 09:04:43.139 | diag:70 timestamp, :71 artifact |
| TRX finish field | 09:04:43.113 | `markdown-pdf-all-profile-1.trx:3` |
| Session ended | 09:04:43.194 | diag:73 |
| Exit telemetry (last timestamped diagnostic event) | 09:04:43.200 | diag:74 |
| Owner log created | 09:04:43.370 | `markdown-pdf-all-profile-1-tests.log` NTFS creation time |
| Stamped log last write | 09:05:10.229 | same file NTFS last-write time; invocation stamp on line 1 |
| Chunk completed | 09:05:10.253 | `markdown-pdf-all-profile-1.timing.json:1` `completedAt` |

### observability-all

`observability-all-execution\log_261029091019369.diag`; date `2026-10-29`.

Last terminal test: `V10_Powershell_preserves_values_and_failures_without_logging_payloads` (diag line 340).

| Event | UTC | Evidence |
|---|---|---|
| Latest test end | 09:11:00.440 | `observability-all.trx`, maximum result `endTime` |
| Last terminal test message | 09:11:00.445 | diag:339 timestamp, :340 message |
| HTML artifact | 09:11:00.565 | diag:349 timestamp, :350 artifact |
| TRX artifact | 09:11:00.636 | diag:352 timestamp, :353 artifact |
| TRX finish field | 09:11:00.573 | `observability-all.trx:3` |
| Session ended | 09:11:00.648 | diag:355 |
| Exit telemetry (last timestamped diagnostic event) | 09:11:00.654 | diag:356 |
| Owner log created | 09:11:00.889 | `observability-all-tests.log` NTFS creation time |
| Stamped log last write | 09:11:29.341 | same file NTFS last-write time; invocation stamp on line 1 |
| Chunk completed | 09:11:30.626 | `observability-all.timing.json:1` `completedAt` |
