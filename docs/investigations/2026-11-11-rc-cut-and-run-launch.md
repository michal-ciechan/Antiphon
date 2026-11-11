# rc/2026-11-16 cut and seeded Windows launch (2026-11-11)

Task 500bb290. Cut `rc/2026-11-16` from current `origin/master` and started one detached Windows Full qualification. No tag was created. Nothing was published or promoted. AppHost, the production runner, Docker Desktop, containers, and volumes were not changed.

Observed through 2026-11-11T12:04:04Z. Leave the supervisor running.

## Verdict

`rc/2026-11-16` is `6f8741d872d63507827014f972ccf71196722d4a`, the same SHA as `origin/master` at the cut. `rc/2026-11-15` is still `ac6d00022e940ee3b90584c7d313567b4491c498`.

Run `d88de419025b4d368fb75108e4de0c36` is a Windows Full rc qualification at concurrency 1, ordered by the run `6591e378` seed. The supervisor is alive and its heartbeat is advancing. The failed-method band started in schedule order: `session-runner-all-group-2` at 2026-11-11T11:56:00Z, then `agents-pty-all-group-1`, then `antiphon-all-group-75` (in flight at 12:04Z). The trx files were not scored.

## Cut

| Item | Value |
|---|---|
| Ref | `rc/2026-11-16` |
| SHA | `6f8741d872d63507827014f972ccf71196722d4a` |
| Candidate | `rc-2026-11-16-6f8741d872d6` |
| Previous rc | `rc/2026-11-15` at `ac6d00022e940ee3b90584c7d313567b4491c498` |
| Commits since that rc | 213 (`ac6d00022..6f8741d872d6`) |
| Journal | `C:\Antiphon\releases\candidates\rc-2026-11-16-6f8741d872d6\candidate.json` (`pushed: false`; the script did not push) |
| Branch push | `git push origin 6f8741d872d63507827014f972ccf71196722d4a:refs/heads/rc/2026-11-16` (new ref only) |

The branch was created only because `rc/2026-11-16` was absent. No existing `rc/*` ref was updated. No tag was created.

### In the cut, versus `rc/2026-11-15`

These ancestors of the cut SHA are the named post-cut fixes:

| SHA | What |
|---|---|
| `a4ea62dd3bfaa20b20ab8609c9352825b96c9974` | Windows runner gc fixtures (group 2, C1817/C1818) |
| `d8a4012c9d32a451b05caee95c1f261b2d0c9dea` | agents-pty group 1 composer probe clock |
| `a565aebfda95d760334d636c53eaf9759334983d` | phone-home byte window (group 75) |
| `b6bbfa26be15fad1fb6a6d23d0ba75fd2b1bd2dc` | Claude startup frame wait (group 74) |
| `fc25314f2963384e7c52dd3ebc8eca03d4ddf703` | CARD-1453 land status child (group 120) |
| `01b93066ae49a3ab2b57730bce4622fe72e7858c` | card-1784 startedAt / read-only git objects (group 130) |
| `4b19c7bd6e6d4fff2416423a7c8fb0e22a460a52` | enqueue-errors throws stay on the named task |
| `f549310659c3494e90d97b6147f08b090ea9ec00` | C1584 roster records the two enqueue-error probes |
| `c3e00c00bc3f5d99bc9558c762fb5bef64a8c0e9` | group 7 late-start probe |
| `13deffd2792a9705aec31e8b593fc6500ab61fda` | group 56 ref-only receipt budget is 170 seconds |
| `bfbcadc2f` | slash palette is Clear only when the highlight matches the draft |
| `1622c8cab` | read `/remote-control` through Claude's slash autocomplete |
| `d4499af2d` | red witness for slash autocomplete covering the composer |
| `1557475b6` | CARD-1224 Unix fake apphosts |
| `acaa9d9b1` | CARD-1832 class platform label from methods |

The tip commit is `6f8741d872d6` (`perf(scripts): select-release answers baseline attempts`). Its parent is `13deffd27`.

### Not in the cut

- Commit `d3c8e48690cc08af5026e39fec3cfebdb445e271` is not an ancestor. The same group-56 budget change is in the cut as `13deffd27`. Group 56 is still in the failed-method band. This launch does not claim it passed.
- `git log ac6d00022..6f8741d872d6 --grep=1750` is empty. CARD-1750 is not shown in this range.
- Every older `rc/*` line, including `rc/2026-11-15`, was left where it was.
- No tag, and no publish or promote.

## Seed

One export of finished run `6591e378c04d47bc898cf0927352b01b` (candidate `rc-2026-11-15-ac6d00022e94`, SHA `ac6d00022e940ee3b90584c7d313567b4491c498`). Older files under `C:\Users\lndco\antiphon-seeds` were not merged. The run had already closed every chunk, and merging those files would have put passed identities back into `failedTests`.

Launch seed: `C:\Antiphon\releases\task-500bb290\seed-6591e378.json`.

Whole-file digest passed as `-PriorFailuresDigest`, and recorded on the live schedule as `historyDigest`: `312363d286bc4bcabe7fdef8c840f064296794e78114aff026eea171c4ea872e`.

The adjuster's earlier digest `204cdddf81d392af01df8843d9dcb127e3a0583a511cb9fb1c8005fe92a6078e` is stale. A JSON round-trip had rewritten timestamps as culture strings. They were written back as ISO-8601 and the file was rehashed before launch. `Read-NightlyStoppedRunSeed` accepted the repaired file.

| Count | Value |
|---|---|
| Appendix B names | 1,143 |
| Carried as passed (`completed=true`, sources clean) | 473 |
| Marked `source-changed` (`completed=false`) | 668 |
| Appendix names absent from the export | 2 (`client-all`, `scripts-all`) |
| Newly invalidated by source changes | 670 (668 marked + those 2) |
| Failed-first chunks kept in `failedTests` | 13 |
| Docker-outage chunks also marked `unproven-by-environment` | 4 (old groups 115, 116, 117, 118) |
| Failed-test rows in the seed | 699 across those 13 chunks |
| Exported chunks | 1,154 (the export omits client and scripts; they have no TRX) |

The 699 rows are the exporter's planned-class matches, not every raw TRX failure from the Docker outage. The 13 chunks still each have at least one failed row, so they lead by method.

Invalidation rule: `scripts/impacted-classes.ps1` on `ac6d00022e940ee3b90584c7d313567b4491c498..6f8741d872d63507827014f972ccf71196722d4a` emitted 608 classes, 580 derived classes, and 22 owed paths. The dirty set is the named set only: `classes` union `derivedClasses` union each owed path's `first` sample (669 bare names). The unnamed owed remainder was not expanded. `skippedByCap` is `Antiphon.sln`. `client` and `scripts` were treated as source-dirty because those trees changed and the selector does not name their tests as C# classes. A chunk was invalidated when any class last-segment was in that named set. The seed reorders a Full run. It does not skip execution, and `select-rc-project-batches.ps1` was not used as a skip list.

## Launch

Repository set: `scripts/qualification/start-supervisor.ps1` from this worktree, which is the cut SHA. `launch-rc.ps1` was copied byte-for-byte into `C:\Antiphon\releases\task-500bb290\launch-rc.ps1` (SHA-256 `db7a6ddff77ec57da00dbbe47de05ab543f1c6d1db110a02138aa17121f65664`). The starter was not copied. It was started with `C:\Program Files\PowerShell\7\pwsh.exe` (7.6.9) through `Win32_Process.Create`. The WindowsApps `pwsh` alias returns create code 8 and was not used.

`C:\Antiphon\releases\checkout` was fetched and detached at the cut SHA before start. The two ownership markers stayed. Nightly was not passed `-NoSync`, so it fetched `rc/2026-11-16` and reset onto that SHA itself. Launch-rc saw HEAD equal the expected SHA. `-NoReport` was not passed.

Workspace cold-reset, before launch: `scripts/workspace-slots.ps1 cold-reset -AllRunners -Reason "rc qualification rc/2026-11-16 6f8741d872d6"` exited 0 with `no runner advertises workspaceSlotV1; nothing to cold-reset`. No request ids.

Preflight immediately before create: Docker server 29.5.3, `antiphon-postgres` Up (healthy), no `native-run.lock`, no nightly `run.lock`, no state `run.lock`, no supervisor identity, no qualification process.

| Process | Pid | State at 12:02Z |
|---|---|---|
| `start-supervisor.ps1` (WMI starter) | 205092 | exited after writing identity |
| `supervise-rc.ps1` | 50568 | alive since 2026-11-11T11:41:40.8122184Z |
| `launch-rc.ps1` | 410128 | alive |
| `nightly-run.ps1` (`run.lock`) | 449568 | alive, parent 466372 |

`supervisor-identity.json` records `detach=new-console`, `consoleProcessIds=[50568]`. That list does not contain starter pid 205092. `sessionId` is null.

Nightly arguments include `-Profile rc -Trigger rc -SelectionMode Full -RunId d88de419025b4d368fb75108e4de0c36 -Ref rc/2026-11-16 -ExpectedSha 6f8741d872d63507827014f972ccf71196722d4a -CandidateId rc-2026-11-16-6f8741d872d6 -AcceptCrossCandidateSeed -SeedSourceCandidateId rc-2026-11-15-ac6d00022e94`. State root, log root, and coordination root are private to this candidate. Build slot `e4b136f2-9055-4913-badb-da964b760f75` was granted at once (`maxcpucount=4`). Concurrency is the default 1. There is no scheduling knob. `nativeActiveMax` above 1 would fail coverage.

### Heartbeat

`C:\Antiphon\releases\task-500bb290\supervisor-heartbeat.json`

| observedAtUtc | cpuSeconds | trx | newest | hangSuspected |
|---|---|---|---|---|
| 2026-11-11T11:42:14.2923536Z | 27.5 | 0 | last-run.json | false |
| 2026-11-11T11:43:15Z | 100.3 | 0 | npm-ci.log.stderr.tmp | false |
| 2026-11-11T11:56:28Z | 116.9 | 0 | log_261111115600894.diag | false |
| 2026-11-11T12:02:02Z | 179.6 | 0 | log_261111115600894.diag | false |
| 2026-11-11T12:04:04Z | 147.3 | 2 | log_261111120404296.diag | false |

The file's last write at the check was 2026-11-11T12:04:04.7048755Z. Cadence is about 30 seconds. `hangSuspected` stayed false. Cpu seconds are the live process tree, so the total can fall when a chunk process exits.

### First chunk

`dispatch-schedule.json` `orderingReason` is `operator-seed-order` and its `historyDigest` is the launch digest above. Chunk ids are not binding: a failed method follows whatever chunk the new plan uses. The first 13 schedule slots are that failed-method band. The old 6591 chunk numbers shifted where the plan regrouped:

| Order | New chunk | Holds the failed methods from |
|---|---|---|
| 0 | `session-runner-all-group-2` | the same chunk (12 rows) |
| 1 | `agents-pty-all-group-1` | the same chunk |
| 2 | `antiphon-all-group-75` | old group 74 (herdr startup; theory arguments) |
| 3 | `antiphon-all-group-116` | old group 115 |
| 4 | `antiphon-all-group-117` | old group 116 |
| 5 | `antiphon-all-group-118` | old group 117 |
| 6 | `antiphon-all-group-119` | old group 118 |
| 7 | `antiphon-all-group-121` | old group 120 |
| 8 | `antiphon-all-group-131` | old group 130 |
| 9 | `e2e-all-split-7` | the same chunk (`C488_ReviewDeliveryCrashMatrix_Batch2`) |
| 10 | `antiphon-all-group-7` | the same chunk |
| 11 | `antiphon-all-group-53` | old group 56 |
| 12 | `antiphon-all-group-73` | old group 75 |

The old ids `antiphon-all-group-115`, `56`, `74`, `120`, and `130` are later in the schedule because those ids now name other classes. The outage methods themselves are in new groups 116, 117, 118, and 119, which are inside the leading band. The seed's `unproven-by-environment` mark stays on the old 115-118 rows.

`session-runner-all-group-2-execution` was created at 2026-11-11T11:56:00.8956113Z. Its trx was written at 2026-11-11T12:02:36Z. `agents-pty-all-group-1-execution` followed at 12:02:39Z and its trx was written at 12:04:03Z. `antiphon-all-group-75-execution` was created at 12:04:04Z and was the in-flight chunk at the last check. Those trx files were not scored. Phase on `last-run.json` is `tests`. SHA on that state is the cut SHA.

The schedule's `priorFailure` flag is still the history member-list bit. It is true on 48 entries. Thirteen of those lead. The other 35 sit at dispatch ordinals 656-690 (the sibling band), after the resume band.

## Watch paths

| What | Path |
|---|---|
| Log directory | `C:\Antiphon\releases\candidates\rc-2026-11-16-6f8741d872d6\logs\2026-11-11-1142-d88de419` |
| Log root | `C:\Antiphon\releases\candidates\rc-2026-11-16-6f8741d872d6\logs` |
| State | `C:\Antiphon\releases\state-500bb290\last-run.json` |
| Heartbeat | `C:\Antiphon\releases\task-500bb290\supervisor-heartbeat.json` |
| Identity | `C:\Antiphon\releases\task-500bb290\supervisor-identity.json` |
| Schedule | `C:\Antiphon\releases\candidates\rc-2026-11-16-6f8741d872d6\logs\2026-11-11-1142-d88de419\dispatch-schedule.json` |

`observe.ps1` credits only run id `d88de419025b4d368fb75108e4de0c36`. The end signal is `completedAt` for that run id. Do not stop the supervisor.

Docker after launch, at 12:04Z: server 29.5.3, `antiphon-postgres` Up (healthy). Supervisor pid 50568 and nightly pid 449568 were still alive.

## Authority

Operator standing authority of 2026-11-11 covers this cut and this rerun. Tags, publish, and promote stayed untouched.
