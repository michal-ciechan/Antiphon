# CARD-1432: desktop canonical repository maintenance (2026-10-21)

Repository: `C:\src\Antiphon` (desktop canonical checkout, git 2.50.1.windows.1). Background:
[2026-10-21-server2-remote-prep-latency.md](2026-10-21-server2-remote-prep-latency.md). Operator authority
(2026-10-21): "fine to prune and cleanup and pack, and delete old refs don't ask me, just go for it, better to
delete more than less."

## Outcome

| Item | Before | After |
|---|---|---|
| `refs/antiphon/land` | 13,859 | 2,640 |
| `refs/antiphon/progress` | 8,150 | 373 |
| All refs (`for-each-ref`) | 28,979 | 9,983 after the sweep; 10,024 at 00:02 with new task refs. `for-each-ref` took 0.2 s, against 0.49 s after pack-refs and 10.4 s originally. |
| Registered worktrees | 1,158 | 1,153 (4 stale `locked initializing` registrations unlocked and pruned) |
| Loose objects | 72,661 (443.8 MiB) | 268 (1.5 MiB), all written during the run |
| Packs | 118 (151.2 MiB) | 2 (81.7 MiB: a 76.9 MB main pack and a 4.95 MB cruft pack) |
| `.git/objects` on disk | 596.8 MiB | 84.6 MiB |
| Commit-graph | 2026-09-24 | rewritten 2026-10-22 00:02:41 |
| `git gc` wall time | | about 58.5 min (23:04:10 to 00:02:41 +01:00) |

Auto gc had stopped because a gc was killed on **2026-09-25 12:17:15 +01:00**. It left a zero-byte
`.git/objects/maintenance.lock` and a `.git/gc.pid` (PID 64836, `DESKTOP-KTLKPIF`, no longer running). Since
git 2.29, porcelain commands run `git maintenance run --auto`. When the maintenance lock exists, that command
**exits 0 without doing anything** if `--auto` is set (`builtin/gc.c`, `maintenance_run_tasks`). So every
automatic gc after that kill was a silent no-op. A stale `gc.pid` alone would not have blocked gc, because git
ignores one older than 12 hours. Both files were removed before the manual gc. The oldest loose objects and the
newest commit-graph (2026-09-24) agree with that date.

The ref sweep, the worktree prune and the start of gc ran while this task held the server's two OS locks. The
locks were released at 23:45:05, mid-gc, on the orchestrator's refinement; see §4. The locks are `.git/antiphon/landing.lock` (the
repository mutation lease, `RepositoryMutationLease.cs:54`, opened with `FileShare.None`) and
`.git/antiphon/land-execution.lock` (`LandExecutionLease.cs:11`). It opened the same files exclusively, so the
server saw a busy lease and deferred. There were no Queued, Held or Running land requests at either start. No git
process this task did not start was touched. No branch, tag, `refs/rescue` or `refs/cleanup-kept` ref was changed.

## 1. Why a land runs about 230 git processes

### Measurement

The server already records a per-land profile (`AgentTaskLandService.cs:512`, `Land git profile`, counters in
`LandingDtos.cs:98-145`, recorded per process in `LandingGit.cs:58-66`). The last ten successful lands on
2026-10-20 logged these numbers:

| | processes | `worktree list` | remote round trips | inspections | canonicalHits | gitSeconds | push s | cleanup s |
|---|---|---|---|---|---|---|---|---|
| range | 232-250 | 7 | 19 | 2 | 3,103-3,136 | 164-331 | 39-117 | 60-134 |

A refused land (no publication or cleanup) ran 141. A land that resumed after a refusal ran 300, with 9 lists and
28 remote round trips. No land ran during this session, so there is no per-subcommand process trace. The profile
counts processes but not which subcommand each one ran (see recommendation R4).

### Breakdown by phase (from code, checked against the profile totals)

The figures are counted from code paths for one fresh land that publishes and cleans up. They add up to about
230, matching the measured 232-250.

| Phase | About | Main call sites | What runs |
|---|---|---|---|
| A. Admission and source resolution | 45-55 | `AgentTaskLandService.cs` (common dir ×4, index-lock probe `LandingGit.cs:922`), `AgentTaskLandSourceResolver.cs` (11 direct `RunAsync`, 7 `RecheckSourceRemoteAsync`, 3 `ObserveSourceAsync`, 3 `PinAsync`, 6 `InspectAsync` sites), `LandOperationFactory.cs` (6 `RunAsync`, `ObserveAsync`, `DestinationAsync`) | rev-parse, `show-ref` of the branch, `ls-remote` and `fetch` of the source into `source-observed`/`source-recheck` pins, two worktree inspections (`LandingGit.cs:440-600`: diff-tree, 3-4 ls-files, 3 `config` reads, cat-file --batch) |
| B. Per-boundary rechecks in the protocol | about 70 | `AgentTaskLandingProtocol.cs:219-343`: `RecheckSourceAsync` **10×** (`:782-812`), `RecheckRemoteSourceAsync` 2× (`:755`), `PinAsync` 3× (`:841`), `IsAncestorAsync`/`CommitAsync` (`:869-881`), `DestinationAsync` | Each `RecheckSourceAsync` runs one `show-ref --verify` **per recovery pin (0-3)**, then `ReadBranchAsync` (2 `show-ref`). That is about 23 pin reads and 20 branch reads per land. Each `PinAsync` runs 4 processes: `check-ref-format`, `show-ref --verify`, `show-ref --exists`, `update-ref` (`LandingGit.cs:962-981`). |
| C. Land worktree and rebase | about 15 | `PrepareLandWorktreeAsync` (`:553`, workspace `EnsureAsync`), rebase (`:254`), rev-parse after the rebase (`LandingGit.cs:132`), index-lock probes (`:914-925`) | reset/checkout/status of the land worktree, rebase, rev-parse |
| D. Verification ticket | about 15 | `LandVerificationPolicy` (2), `VerifiedLandWorktreeAsync` (`:568-575`), `CaptureTicketAsync` (`:580-594`, **live** `worktree list`), `ValidateTicketAsync` (`:596-637`) | rev-parse, status, sequencer probe, `worktree list`. The test run itself is not git. |
| E. Publication | about 20 | `ObserveAsync` 2× (`:836`, before and after the push), push (`LandingGit.cs:990`), canonical advance (`:475-550`: sequencer check, status, `update-ref` or `merge --ff-only`) | Each observation runs `remote get-url`, `check-ref-format`, `ls-remote`, `fetch` into `remote-observed/<id>`, `merge-base --is-ancestor` and `remote get-url` again: about 6 processes, 2 of them remote. |
| F. Cleanup | 45-50 | `CardWorktreeCleanupExecutor.cs:62` (`cleanup-observed` observation), `GuardedWorktreeRemoval.cs:286-412` (12 `RunAsync`, 2 live and 1 cached `worktree list`, inspection, unregister), `CardWorktreeMirrorCleanupService.cs:76` (`mirror-observed` observation plus destination) | symbolic-ref, rev-parse, status, merge-base, 3 pin `show-ref`, `update-ref -d` of the branch, 2 `show-ref --exists`, `worktree list`, `worktree remove`/prune, 2 more observations |

`worktree list --porcelain -z` runs 7 times. With 1,150 registrations, each run parses about 1,150 entries. The
`canonicalHits≈3,100` counter shows the cost: every cached registration path is checked again with
`Directory.Exists` (`LandingGit.cs:150-160`). That is in-process filesystem work, not extra processes, but it
grows with the worktree count. Live reads (`LiveRegistrationsAsync`) bypass the cache.

### Redundant or batchable calls

| # | Today | Proposed | Saving per land |
|---|---|---|---|
| G1 | `RecheckSourceAsync` ×10: 1 process per pin plus 2 per branch read | One `git for-each-ref --format="%(refname) %(objectname)" <prefix>/ <sourceRef>` per recheck, or `show-ref --verify <pins...> <branch>` | about 33 |
| G2 | `PinAsync`: 4 processes per pin, 3 pins written at different phases | Validate the ref name in process. Write with `update-ref --stdin` `create <ref> <sha>`, which refuses if the ref exists. Read back only on refusal. | about 9 |
| G3 | Every `check-ref-format` (about 10 per land) | A pure C# validator. The ref names come from GUIDs and fixed suffixes. | about 10 |
| G4 | `remote get-url --push --all origin` before and after every observation or recheck (about 15) | Read once per leased land and bind the fingerprint. The config cannot change under the lease without a writer the lease already fences. | about 12 |
| G5 | `ls-remote` followed by `fetch` of the same ref | Run `fetch` alone into the pin and read the pin. It is one round trip, and the object is usually present already. | about 6 processes, 6 round trips |
| G6 | Inspection: separate `config --bool core.sparseCheckout`, `config --get core.autocrlf`, `config --bool core.filemode`, `ls-files -v`, `ls-files --stage` | One `config --get-regexp "^core\.(sparsecheckout\|autocrlf\|filemode)$"` and one `ls-files --stage -v` | about 6 (2 inspections) |
| G7 | Paired `merge-base --is-ancestor a b` / `b a` (`AgentTaskLandSourceResolver.cs:614-617`) | One `rev-list --left-right --count a...b` | about 1-2 |
| G8 | 7 full `worktree list` runs over 1,150 entries | Read `<common>/worktrees/<name>/gitdir` for the one registration involved. Prune terminal worktrees (CARD-1263). | CPU plus 4-5 processes |

Together these would take a land from about 235 processes to about 120-140, with no change to the safety
checks. The checks would run in fewer processes, not less often. The per-process cost also falls with the
repository cleanup below: fewer refs, packed objects, a fresh commit-graph.

## 2. `refs/antiphon/*`: what each namespace is for, and the deletion rule

| Namespace | Writer | Reader | Needed after |
|---|---|---|---|
| `refs/antiphon/land/<task>/<landing>/{source,target-before,prepared}` | `LandOperationFactory.cs:45` prefix; `AgentTaskLandingProtocol.cs:841` `PinAsync` | `RecheckSourceIdentityAndPinsAsync` (`:790-812`) during the land; resume/cleanup retry (`GuardedWorktreeRemoval.cs:396`) | That landing reaches Complete/Refused and its cleanup completes |
| `.../{remote,cleanup,mirror}-observed/<id>`, `.../source-observed/<id>`, `.../source-recheck/<id>`, `.../recover-*-observed/<id>` | fetch-into-pin observations (`LandingGit.cs:800-875`, `AgentTaskLandSourceResolver.cs:115,257,268`, `CardWorktreeCleanupExecutor.cs:62`, `CardWorktreeMirrorCleanupService.cs:76`) | Only the observing call itself; the pin keeps the fetched object alive while it is compared | Immediately after the observation |
| `.../adopt/{source,local-before,remote-before}` | `AgentTaskLandSourceResolver.cs:316,417,585` | `LandRecoveryWitness.cs:46` for an interrupted adopt (`SourceResolutionState=AdvanceStarted`, `RecoveryAdoptedAt` null) on the same worktree | Adoption is resolved |
| `refs/antiphon/progress/<task>/{baseline-*,observe-*}` | `TaskProgressGit.cs:144,226` (dispatch baseline capture and progress observation) | `ListProgressPinsAsync` (`TaskProgressGit.cs:245`) for that task | The task is terminal |
| `refs/antiphon/progress/<task>/settlement-sync` | `RemoteWorkspaceService.cs:674,867` | Settlement sync-debt replay for that task | Its `AgentTaskSyncDebts` row is Superseded |
| `refs/antiphon/start-ref-probe`, `refs/antiphon/retirement/<id>/` | `StartRefAvailability.cs:45`, `LandingGit.cs:1058` | Their own operations | Transient; none were present |

Nothing deletes any of these refs today, so they grow by about 12 land refs and about 3 progress refs per task.

**Rule applied.** Delete every `refs/antiphon/{land,progress}/<task>/...` ref unless `<task>` is in the
protected set, which is any of:

- `AgentTasks.Status` in Queued/Dispatched/Working/Blocked;
- the task or `RecoverySourceTaskId` of a land request in Queued/Held/Running/NeedsResolution;
- the task of a land request with `SourceResolutionState=AdvanceStarted` and `RecoveryAdoptedAt` null (an adopt
  witness);
- the task or `RecoverySourceTaskId` of an `Active` landing whose `Phase` is not Complete or Refused (interrupted
  publication, or a refused cleanup that can be retried; 150 rows);
- the task of an `AgentTaskSyncDebts` row in Pending/Ready/Held;
- the task of an active `TaskWorktreeRetirements` row in Released/Claimed/CommandStarted/Partial.

Everything else was deleted: tasks that are terminal (Succeeded, Failed or Canceled) and tasks that are no longer
in the database. That deletes more than the 3-day age clause would, and stays inside the rule. The protected set
was 288 tasks. The deletion ran as one `git update-ref --stdin` transaction with old-value checks:
`delete <ref> <oid>` × 18,996, in 41.5 s. A first attempt piped the batch through PowerShell, which prepended a
BOM. Git rejected that transaction (`whitespace before command`) and applied none of it. The rerun used a file
redirect.

| Namespace | Before | Deleted | After |
|---|---|---|---|
| `refs/antiphon/land` | 13,859 | 11,219 | 2,640 |
| `refs/antiphon/progress` | 8,150 | 7,777 | 373 |
| `refs/heads` / `refs/remotes` / `refs/tags` | 3,617 / 3,303 / 2 | 0 | unchanged |
| `refs/cleanup-kept` / `refs/rescue` / `refs/stash` | 37 / 10 / 1 | 0 | unchanged. These are manual rescue refs outside `refs/antiphon`, so they were not covered by this rule. |

## 3. Worktrees

- `git worktree prune`: 0 at first. Every unlocked registration still had its directory.
- Four registrations were `locked initializing` with no directory: `card-task-047193d4` (2026-09-25 12:17, the
  same minute as the killed gc), `273b3a0d` (09-25 12:55), `6fea402b` (09-30) and `4c2309e1` (10-01). These
  are interrupted `git worktree add` runs, and `prune` skips locked entries. With no git process running and
  the lease held, they were unlocked and pruned. That is 4 registrations removed.
- **1,134 registered worktrees whose task is terminal still have a directory**: Succeeded 1,048, Failed 46,
  Canceled 40, completed 2026-09-06 to 2026-10-21. They were **not** removed, because CARD-1263 owns that.
  Another 11 registrations match no task row: evidence base checkouts, `*-base-proof`, `c849-retired-*`, and
  nested `.antiphon/c1310-*` checkouts inside task worktrees. They are listed for CARD-1263 too.

## 4. Why auto gc stopped, and the gc run

- Repository config has no `gc.*` or `maintenance.*` keys, and no maintenance schedule is registered. The global
  config sets `gc.autodetach=false`, so an automatic gc runs in the foreground of whichever git command triggered
  it.
- **Cause:** the stale `objects/maintenance.lock` from 2026-09-25 12:17:15. See Outcome. Likely mechanism (not
  proven): `gc.autodetach=false` made a commit or fetch run gc in the foreground, and that process was killed
  (a timeout or a Job Object teardown during an AppHost restart). The interrupted `worktree add` registered in
  the same minute is consistent with a process-tree kill.
- Antiphon sets `gc.auto=0` and `maintenance.auto=false` only for the runner's read-only inspection git calls
  (`RunnerWorkspaceService.cs:986-990`). Nothing in the server disables it.
- `git gc` ran with defaults (`gc.pruneExpire=2.weeks.ago`, cruft packs, `--cruft-expiration=2.weeks.ago`). It
  did not use `--prune=now`, because worktrees are written concurrently. The 4,768 already-packed loose
  duplicates were removed. Unreachable objects younger than two weeks went into a cruft pack, not deleted.
  About 8.5 minutes passed before `repack` started: pack-refs, reflog expiry across about 1,150 worktree HEAD
  reflogs, rerere and worktree prune. Then came `repack -d -l --cruft` (delta search about 1,250 CPU-s at
  1 GB working set; writing began at 23:35), then `prune --expire 2.weeks.ago` (about 23:50 to 00:00), then the
  commit-graph.
- **Lease cost.** This task held both locks from 23:03:45 until 23:45:05, about 41 min. That blocked every land
  and new worktree cut, and one land sat Held. On the orchestrator's refinement, the lock-holding script process
  was stopped. gc kept running and finished on its own, and the Held land started at 23:45:45. With the default
  prune grace, gc does not need the lease. **Future gc runs should not hold the repository mutation lease**: hold
  it only for the ref sweep (41.5 s) and the worktree prune, and keep gc out of it. A full gc on this repository
  takes about an hour while the 1,150 worktrees exist, mostly reflog expiry and reachability over their indexes,
  so it should also not run inline in a land or a cut.

## Recommendation: scheduled maintenance (for the Plan stage)

R1. **Hangfire recurring job `RepositoryMaintenance`** on the main instance. Hangfire is the scheduler for work
inside Antiphon. It runs nightly, plus on demand from an ops route, for each canonical repository. It takes the
repository mutation lease with `TryAcquireAsync` **only for steps 1-3**, which take about a minute. If the lease
is busy it skips and retries hourly, and it always yields to queued lands and dispatch waiters. Step 4 runs
outside the lease, because default prune grace makes it safe alongside concurrent writers. It runs:
  1. Stale-lock repair: `objects/maintenance.lock`, `gc.pid` or `gc.log` older than 12 h whose PID is not alive
     on this host is removed and recorded as an attention item.
  2. Terminal-ref sweep, using the rule in §2, as one `update-ref --stdin` transaction.
  3. `git worktree prune`, plus unlock-and-prune of `locked initializing` registrations older than 24 h with no
     directory and no in-flight cut.
  4. `git maintenance run --task=loose-objects --task=incremental-repack --task=commit-graph --task=pack-refs`
     nightly, and `git gc` (default prune) weekly.
  5. A metrics line, with attention past thresholds: loose objects > 20,000, packs > 50, refs > 15,000, loose ref
     files > 1,000, worktree registrations > 300, maintenance lock older than 12 h.
R2. **Delete refs at the source.** When a landing reaches Complete or Refused with cleanup Complete, delete its
`refs/antiphon/land/<task>/<landing>/` subtree. Delete each `*-observed/<id>` and `source-recheck/<id>`
observation pin as soon as its comparison is done. When a task is terminal and has no open sync debt, delete
`refs/antiphon/progress/<task>/`. The nightly sweep then becomes only a backstop.
R3. **Keep auto gc out of leased server operations.** Pass `-c gc.auto=0 -c maintenance.auto=false` on server
git invocations, as the runner inspection path already does, so a land never runs a foreground gc while holding
the lease. With R1 in place, the global `gc.autodetach=false` no longer matters for Antiphon.
R4. **Add a per-subcommand histogram** to `LandingGitProfile.Record`, keyed by subcommand plus the first
sub-argument, and log it on the `Land git profile` line. That gives exact numbers to check G1-G8 against.
R5. Implement G1-G6 (§1) in the protocol and `LandingGit`, keeping one check per boundary. This roughly halves
the processes per land.
