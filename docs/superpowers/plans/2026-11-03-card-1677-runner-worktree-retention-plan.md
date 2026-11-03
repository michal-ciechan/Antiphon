# CARD-1677: runner-side retention of finished task worktrees (plan)

Plan task b2d063aa, 2026-11-03, on the server2 runner mirror (Linux) at base
`c4e7aa06e9f8c119f7dd59f52e38ed413293c9f2`. Read-only: no code, no builds, nothing deleted.

## Summary

server2's `/work/worktrees` grows by about 80 GiB a day. 566 task mirrors reached 397 GiB in five
days, and Custom task e937b0c3 reclaimed 193 GiB by hand on 2026-11-03. No code path on the runner
retires the mirror of a finished task. The two existing owners do not reach it in practice:

- CARD-0459's `antiphon:worktree-residue` job runs inside the **server** process against the
  server's own filesystem (the desktop). Its settled-task lane removes a tree only when a caller has
  filed a `TaskWorktreeRetirements` release (there are none). It reaches the runner only as a
  best-effort follow-up after it retires the desktop worktree. It also ships with `Execute=false`.
- CARD-0824 S1 (workspace slots) bounds disk usage by construction for **new** tasks, but only after
  `PhoneHome:WorkspaceSlots:Enabled` is turned on. It never touches the legacy `task-*` mirrors,
  which its own S4 slice was meant to retire.

**Recommendation (D-2): the server decides and the runner verifies.** A server recurring job reads
the task table. That table is the only place where "terminal for more than N hours, no land pending,
no sync debt, no live owner" is known. For each candidate mirror on an allow-listed Linux
phone-home runner, the job sends one runner operation with that task's identity, one candidate at a
time. The runner rechecks every filesystem and Git fact itself, immediately before it acts: clean,
nothing unpushed, branch tip present on live origin and containing HEAD, no live process inside,
not a slot, not locked. It then removes the tree with `git worktree remove`, never `--force`. This
is the e937b0c3 manual procedure, which built its candidate list from the server API and rechecked
on the runner, moved into code with its exact checks. Dry-run is the default and the job is off
until an operator enables it.

Three slices: **S1** retention engine and inventory (the 17 unmatched folders are reported, never
deleted). **S2** a `/tmp` sweep limited to registered name families with a pinned producer file.
**S3** a `WorkspaceDiskLow` attention row at 85 percent used on the worktree volume, from the
runner's existing host-stats disk sample. It uses the existing attention feed, not a new sink.

This engine **is** the runner half of CARD-0824 S4 (legacy migration). S4 adopts it rather than
building a second deleter (D-10).

## 1. Measured facts (server2, 2026-11-03)

| Fact | Value | Source |
|---|---|---|
| Disk | One LV backs `/`, `/work`, `/tmp` and `/var/lib/docker`: 705G, 73% used, 188G free after the reclaim | `df -Pk /work /tmp` in the runner container |
| Mirrors | 172 dirs in `/work/worktrees`, 178 registrations in `/work/repos/antiphon`, no `ws-*` slot dirs | `ls`, `git worktree list --porcelain` |
| Growth | 566 mirrors and 397 GiB in about 5 days, ~0.7 GiB each, ~80 GiB a day | e937b0c3 report |
| Manual reclaim | 437 passed the checks; 397 removed (190.7 GiB); 0 refused on Git grounds; 3 refused by the live-process guard (pooled sessions with cwd inside) | e937b0c3 report |
| Unmatched folders | 17, all registered worktrees: `task-cffa0c4b-{uA,uB,uC,wD,wE,wF,wG}` detached at `330c60041`; `task-e38d6427-{laneA,laneB,laneC,wE,wF,wG,wI,wM,wX}` detached at `0b903dd9d`, and `task-e38d6427-combined` on its own branch | `git worktree list` |
| `/tmp` | 50.4 GiB in 46,691 top-level entries | `find /tmp -maxdepth 1 \| xargs du -sk` |
| `/tmp` largest family | `/tmp/claude-1654/-work-worktrees-task-<8hex>/`: 311 dirs, 24.2 GiB. 21.1 GiB of that belongs to the 157 whose mirror is already gone | `du` against the mirror's presence |
| `/tmp` next families | `c487-probe-<32hex>` 9.4 GiB/204; `antiphon-c514-pty-*` 3.4/296; `antiphon-c79-stop-*` 1.5/186; `antiphon-adoption-tests-*` 0.9/115; `antiphon-c1653-sock-*` 0.6/400; `c487-<32hex>` 0.6/191; `c502-exit-cap-*` 0.5/42; `c1514-*` 0.5/573; `antiphon-land-verify-*` 0.3/105; `antiphon-bridge-queue-*` 0.04/5,001 | same census |
| Host-side residue | ~210G is invisible from inside the container (host Docker volumes and images) | e937b0c3; out of scope here (host ssh census) |

## 2. Ground truth: what the card assumes vs what the code does

| Card / brief assumption | Code at `c4e7aa06e` | Consequence |
|---|---|---|
| "CARD-0459's job targets the desktop" | `WorktreeResidueSweepService` uses `IWorktreeManager` on the server's own filesystem. The settled-task lane needs an active `TaskWorktreeRetirements` release (`EvaluateEligibilityAsync` returns `release_required`) | It cannot see `/work/worktrees`. Extending it would couple runner deletion to desktop `Execute` and to per-task releases |
| "Nothing on the runner removes finished worktrees" | True for terminal tasks. `RemoteWorkspaceService.RemoveMirrorAsync` runs only after a desktop retirement (`TaskWorktreeRetirementService` ~L364) or on a drain re-arm (`AgentTaskDispatcher` ~L1185) | Confirmed. The runner-side primitive exists but nothing schedules it |
| Existing runner remover is safe to reuse | `RunnerWorkspaceService.RemoveAsync` (L538) checks `status --porcelain` and optional `PublishedSha` ancestry, then runs `git worktree remove --force` and `git worktree prune`. It has no live-process check, no live-origin check and no age gate | It **violates** the card's "never forced" rule and prunes repository-wide. Retention needs its own runner path (D-5). `RemoveAsync` stays unchanged for its existing callers |
| Live-process check exists | `ProcWorkspaceSlotProcessProbe` (`WorkspaceSlotSeams.cs`) scans `/proc/*/cwd` and `/proc/*/fd`. It returns an **empty** list when `/proc` cannot be enumerated (fail-open), and it skips other uids | Retention needs a fail-closed result (`Unknown`) (D-7). Skipping other uids is the reviewed S1 limit; it is recorded, not refused |
| Free-space reading on host stats | Runner `HostStatsProbe` samples `SessionRunner:HostStats:Volumes`; `docker-compose.server2-runner.yml` L74-75 sets `/work` and `/state`. The server polls into `HostStatsCache` (`HostStatsPollService`). `RunnerHostCurrentDto.Disks[]` carries `RunnerHostDiskDto(Path, FreeBytes, TotalBytes)` | S3 reads `FreeBytes`/`TotalBytes` of the disk whose `Path` is the runner's configured worktree volume. No new runner field is needed |
| An alarm path exists | `AttentionService.GetAsync` appends `BuildRunnerUnavailableItems`/`BuildJournalStaleItems` from `RunnerAlarmState` (`AttentionService.Alarms.cs`). There is no disk item for runners. `HostCleanupAttentionService` (CARD-0826) has disk pressure, but only from `HostCleanupRuns`, which nothing produces | S3 adds one item builder beside the alarm items: kind `WorkspaceDiskLow` (wire 65), the key CARD-0824 §3.11 already named |
| `CacheDiskLow` gate | Lives only in scripts (`scripts/c590-remote.sh`, the `c1008` fixtures): a 20 GiB rollout gate | Unrelated to the attention feed. S3's threshold is a separate setting |
| Phone-home mutating ops | Every mutating op runs under one `MutationLock` (`PhoneHomeCommandDispatcher` L941-L1028), including `Input`, `WorkspaceMirror` and `ConditionalInput` | A multi-GiB deletion under that lock blocks every session's Input. Hence the two-phase detach-then-delete (D-6) |
| Unmatched folders are unknown | They are registered **detached** worktrees made by tasks cffa0c4b and e38d6427 (mutation shards per git-common) | The name grammar `^task-[0-9a-f]{8}$` excludes them. Report with the owner hint only |
| CARD-1590 dirty nested seeds | Seeds live under the gitignored `.antiphon/`. `git status --porcelain --untracked-files=all` at the mirror root does not descend into ignored nested repos, and `git worktree remove` (no force) deletes ignored content | Retention retires such a tree when the top level is clean. That clears CARD-1590's recycle refusals for terminal owners. A non-ignored nested repo shows as untracked and refuses (D-9) |
| CARD-0826 host cleanup | `src/Antiphon.HostCleanup` has a family registry and planner, but its doc says "not operational": native adapters throw, nothing is scheduled, and worktrees are inventory-only | Not reused (D-3). S2's family table borrows its hard-deny list |
| Residue run storage | `WorktreeResidueRun` (`Scope`) and `WorktreeResidueRunCandidate` (`Lane`, `Outcome`, `ReasonCode`, `RunnerId`, `TaskId`, `Path`, `Branch`, `DirectoryRemoved`, `RegistrationRemoved`) already exist, and `GET /api/agent-tasks/worktree-residue/runs/{runId}` pages them | Retention records in these tables with new lane strings. **No migration** (D-8) |
| Test temp roots are registered | `tests/Antiphon.SessionRunner.Tests/PtyHostLeakSweep.cs` L15 `KnownPrefixes` lists the `antiphon-<prefix>-<32hex>` roots | S2 registers the families by producer file, not by observation alone |

## 3. Design

### 3.1 Where the decision lives (D-2)

Task state lives in the server's `AgentTasks`, `AgentTaskSyncDebts`, land requests and sessions. The
filesystem is visible only to the runner. They meet in **one server-originated, runner-verified
operation per candidate**:

```
server RunnerWorktreeRetentionJob (Hangfire, recurring, DisableConcurrentExecution)
  for each runner in RunnerWorktreeRetention:Runners that is live and advertises workspaceRetentionV1:
    inventory   <- runner WorkspaceRetentionInventory (read-only, not under MutationLock)
    candidates  <- task table: server-side eligibility (3.2), oldest CompletedAt first, at most MaxRemovalsPerTick
    for each candidate, strictly one at a time:
      re-read the task row (AsNoTracking); skip it if any server fact changed
      outcome <- runner WorkspaceRetain{TaskId, Path, Branch, Mode=DryRun|Execute}
      record outcome (run candidate row and log); on Removed: RemoteWorktreePath=null plus an Info event
      pause PauseBetweenSeconds
    record inventory rows for names that match no task (Lane RunnerInventory, Held, identity_unknown)
```

The runner never decides that a task is finished, and the server never decides that a tree is safe
to delete. Either side alone can only refuse.

### 3.2 Server-side eligibility (rechecked from the row right before each send)

- The runner is in `RunnerWorktreeRetention:Runners` (default `[]`), is a phone-home runner, is
  online, and advertises `RunnerCapabilityFeatures.WorkspaceRetentionV1`. The local or desktop
  runner is never contacted (D-11).
- `Status` is one of `Succeeded`, `Failed` or `Canceled`, and `CompletedAt <= now - MinTerminalHours`
  (default 12, D-1). Otherwise `live_owner` or `settling`.
- `Workspace == Worktree`, `RunnerId` equals the runner, `RemoteWorktreePath` is a direct child of the
  runner worktree root whose leaf equals `task-` plus the task's first 8 hex, and `WorktreeBranch` is
  set. Otherwise `outside_scope`.
- `RemoteWorkspaceSlotId == null` (`slot_managed`). `SourceLandingOperationId == null` and
  `RepairSourceTaskId == null` (`outside_scope`). `Role != Mutation`, matching
  `TaskWorktreeRetirementService` (`mutation_excluded`).
- No land request in a non-terminal state. This uses the same predicate as `?landPending=true`
  (`land_pending`).
- No `AgentTaskSyncDebts` row for the task in `Pending`, `Ready` or `Held` (`sync_debt`). A reopened
  task re-mirrors from the desktop branch, so that branch must already hold the mirror's commits.
- No open task (Queued, Dispatched, Working or Blocked) whose `WorktreePath` or `RemoteWorktreePath`
  equals this path. This is the same query shape as `TerminalRunnerSeatReleaseService` ~L1235
  (`path_shared`).
- No `AgentSessions` row for the task in Created, Starting, Running or Stopping (`live_session`). A
  pooled session that lives on with its cwd inside is caught by the runner's process check.
- Report present: a null `Result` whose `ResultFilePath` points inside the mirror is held as
  `report_unpreserved`. `Result` in the database is the durable copy; gitignored `.antiphon/`
  evidence in the tree is disposable after N hours (D-9).

### 3.3 Runner-side recheck and removal (one candidate per request)

The runner reads the request and refuses on the first failed check, in this order. Every refusal
is a kept tree with a reason code, never an exception that loses the outcome.

1. **Confinement.** `Path` resolves without symlinks (`TryResolveFinal`) to a direct child of
   `WorktreeRoot`, and its leaf matches `^task-[0-9a-f]{8}$` and equals the task id prefix
   (`outside_scope`). `RunnerWorkspaceSlotPool.IsSlotLeaf` or `SlotByPath` is not null
   (`slot_managed`). Absent leaf: `already_absent`, with no `prune`.
2. **Ownership gates.** `RunnerWorkspaceRemovalJournal` has no overlapping record, and the
   `RunnerWorkspaceUseGate` (when installed) has no owner (`workspace_in_use`). The worktree is not
   `git worktree lock`ed (`locked`).
3. **Owning repository.** `OwningRepositoryAsync(path)` succeeds (`not_runner_repository`).
   `symbolic-ref HEAD` equals `refs/heads/<Branch>`, and `DelegateBranchName.TryParse(Branch)`'s short
   id equals the leaf id (`branch_mismatch`). A detached HEAD is `branch_mismatch`.
4. **Clean.** `git status --porcelain --untracked-files=all --ignore-submodules=none` is empty.
   Exit not 0: `status_unavailable`. Output present: `dirty`.
5. **Nothing unpushed.** `git rev-list --count HEAD --not --remotes=origin` returns `0`
   (`unpushed`).
6. **Live origin.** `git ls-remote origin refs/heads/<Branch>` (60 s budget) returns exactly one line
   (`branch_absent_on_origin`; a failure is `origin_unavailable`). The returned tip exists locally
   (`origin_tip_unknown`; the runner does not fetch) and `merge-base --is-ancestor HEAD <tip>` exits 0
   (`origin_does_not_contain_head`). This proves the branch against live origin, not stale
   remote-tracking refs.
7. **No live process.** The fail-closed holder probe (D-7) reports no holder (`live_process`
   pid/argv0/kind), and the scan was complete (`holder_scan_unknown`).
8. **DryRun stops here** with `would_remove` and returns the facts. No Git mutation runs.
9. **Execute, phase A, under `MutationLock`.** Re-run steps 1-7 (the recheck immediately before),
   then `git worktree move <path> <RetiringRoot>/<leaf>.<request8>` (a rename that keeps the
   registration). `RetiringRoot` (default `/work/worktrees-retiring`, a sibling of the root, never
   inside it) must be on the same device (`st_dev`). Otherwise `retiring_root_cross_device` and
   nothing moves.
10. **Execute, phase B, outside `MutationLock`.** `git -C <owning repo> worktree remove <moved path>`,
    **without `--force`**. Git repeats its own clean check. Exit 0 and the moved path absent:
    `removed`. Otherwise `retiring_refused` with Git's stderr tail. The moved tree stays registered
    in the retiring root. The next inventory reports it (Lane RunnerInventory, `retiring_residue`),
    and phase B is retried only for that exact path, with no new move.

The runner never runs `rm`, `git worktree prune`, `--force`, `git fetch` or `git branch -D`. The
response carries `{verdict, reason, headSha, originTip, holders[], nestedIgnoredRepos, phaseAMs,
phaseBMs}`. `nestedIgnoredRepos` is a bounded count (10 s budget) of `.git` entries under ignored
paths, kept as CARD-1590 evidence, not as a refusal.

### 3.4 Inventory (the 17 unmatched folders)

`WorkspaceRetentionInventory` (read-only) lists each top-level entry of `WorktreeRoot` and
`RetiringRoot`: `{name, isDirectory, isSymlink, registered, slotLeaf, mtimeUtc}`. The server writes a
`RunnerInventory` row for every name that matches no task: `identity_unknown`, plus `ownerHint`
(the `task-<8hex>` prefix and that task's status when one matches). It writes `identity_ambiguous`
for a name that matches more than one task. Retention never acts on these names, in any mode. The
first run's rows are the requested report on the 17 folders.

### 3.5 `/tmp` sweep (S2)

This is the sweep that `docs/docker-stack.md` "Manual main cleanup" ("never sweep by name pattern")
leaves to hand cleanup. S2 is the one automated name-pattern path, and it is narrowed so it cannot
act on an unregistered name:

- **Registered families only**, top level of `/tmp` only, each with an exact regex and a pinned
  producer file (table below). A source-reading witness fails when a producer file no longer
  contains its prefix literal. Any other name is reported as `unregistered` (count and bytes per
  normalized prefix) and never touched.
- **Age.** The newest write time of every regular file in the entry (bounded walk) is older than
  `MinAgeHours` (default 48: the card's "older than 2 days"). The directory's own mtime is not used.
- **Owner.** The entry and every descendant are owned by the runner's uid (`foreign_owner`). The
  entry is not a symlink (`symlink`). Descent never follows a link.
- **No holder.** The same fail-closed probe as 3.3 step 7.
- **Hard deny**, regardless of family: `/tmp/antiphon-pty-hosts` and children, every
  `antiphon-pty-*` (live pty-host runtime), `CoreFxPipe_*`, `dotnet-diagnostic-*`, `.X*`,
  `systemd-*`, `node-compile-cache`, plus CARD-0826's `CleanupFamilyRegistry.IsDenied` component list
  (`.git` top level, `.claude`, `.codex`, `.grok`, `build-slots`, `runner-state`, `postgres`,
  `docker`, `reports`, `deliverables`).
- **Bounded:** at most `MaxEntriesPerTick` (default 200) and `MaxBytesPerTick` (default 20 GiB) per
  tick. Deletion goes through the existing no-follow primitive `RunnerWorkspaceNoFollowDelete`. An
  entry over its 100,000-entry or 1 GiB inventory limit is refused as `inventory_limit` and
  reported.
- Dry-run by default, and off by default, under its own switch.

| Family id | Regex (top-level name) | Producer (file:line) | Measured 2026-11-03 |
|---|---|---|---|
| `c487-probe` | `^c487-probe-[0-9a-f]{32}$` | `tests/Antiphon.Tests/TestHelpers/TestClassificationPolicyTests.cs:469` | 9.40 GiB / 204 |
| `c487` | `^c487-[0-9a-f]{32}$` | `scripts/test-nightly-run.ps1`, `scripts/test-live-lane-receipt.ps1` (TestDesign pins the line) | 0.59 / 191 |
| `pty-test-root` | `^antiphon-(cpu-watchdog-tests\|liveness-tests\|adoption-tests\|bufbounds-tests\|backend-seam\|first-write-race\|0180-dto\|card0395-adoption\|card0395-write-fault\|card0395-http\|c420-barrier\|c502-v25\|c502-v26\|c502-v26-manifest\|c502-v26-herdr\|c502-v27\|c514-gen\|c514-seq\|c514-gate\|c514-dest\|c649-prompt\|c79-stop\|c628-token\|c710-platform\|c1636-adopt\|c1653-adopt\|c1653-launch)-[0-9a-f]{32}$` | `tests/Antiphon.SessionRunner.Tests/PtyHostLeakSweep.cs:15` (`KnownPrefixes`); the regex is generated from that array by the witness | ~4.5 / ~1,300 |
| `c514-pty` | `^antiphon-c514-pty-[0-9a-f]{32}$` | `tests/Antiphon.Tests/TestHelpers/RemoteControlPtyLane.cs:80` | 3.37 / 296 |
| `c502-exit-cap` | `^c502-exit-cap-[0-9a-f]{32}$` | `tests/Antiphon.Tests/Application/SessionGenerationExitTests.cs:658` | 0.52 / 42 |
| `c1514` | `^c1514-[0-9a-f]{32}$` | `tests/Antiphon.Tests/TestHelpers/C1514Support.cs:15` | 0.48 / 573 |
| `land-verify` | `^antiphon-land-verify-[0-9a-f]{32}$` | `server/Application/Services/AgentTaskLandService.cs:1946` | 0.28 / 105 |
| `bridge-queue` | `^antiphon-bridge-queue-[0-9a-f]{32}$` | `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs:117` | 0.04 / 5,001 |
| `rcspeed` | `^rcspeed-[0-9a-f]{32}(\.ps1)?$` | `tests/Antiphon.Tests/Scripts/RcFullTimingContractTests.cs:239,246` | 0.03 / 492 |
| `claude-scratch` (D-12) | `^claude-1654/-work-worktrees-task-[0-9a-f]{8}$` (depth 2, exact) | Claude Code CLI scratchpad keyed by the session cwd, which the runner sets to the mirror | 24.2 / 311 (21.1 GiB with mirror gone) |

`antiphon-c1653-sock-*`, `antiphon-c1653-peer-*`, `antiphon-c1150-*`, `c716-*`, `c1614-boot-*`,
`antiphon-c514-pty` siblings built by interpolation, `task-<hex>`, `ff-nightly-*` and the one-off
names (`g104-*`, `card0395`, `c1609-*`) stay `unregistered` until a later card pins their producers.
`claude-scratch` is eligible only when `/work/worktrees/task-<id>` is absent **and** that mirror is
not in the retiring root. It has its own family switch, default off: the card limits the sweep to
the repo's own scripts, and this producer is the CLI harness (D-12).

### 3.6 Low-disk alarm (S3)

- **Field.** `HostStatsCache` holds each runner's latest `RunnerHostStatsDto`. Its
  `Current.Disks[]` entry whose `Path` equals the configured volume (`WorkspaceDiskAlarm:Runners`,
  default `{ "server2": "/work" }`) gives `used = 1 - FreeBytes / TotalBytes`.
- **Path.** `AttentionService.GetAsync` appends `BuildWorkspaceDiskLowItems` beside
  `BuildRunnerUnavailableItems`. The kind is `AttentionKind.WorkspaceDiskLow = 65`, severity
  Warning, with `ConditionKey workspace-disk-low:<runnerId>`. Evidence:
  `path=/work; usedPercent=..; freeBytes=..; totalBytes=..; observedAt=..; retention=<Off|DryRun|Execute>`.
  The row clears when the sample drops below the threshold. A sample older than `SampleFreshMinutes`
  (default 15) raises nothing, and the evidence of a current row notes `stale`. No notifier, channel
  or new sink is added; the existing feed and its consumers carry it.
- **Threshold.** `UsedPercent` defaults to 85, the card's number. CARD-0824 §3.11's
  `max(20 GiB, 10%)` floor is the stricter low end and is not added here; S4 may add it as a second
  condition on the same kind.
- Report only. The alarm never triggers a deletion.

## 4. Safety invariants

Each invariant has a red-first witness (section 6) and a mutant (section 7).

| # | Invariant |
|---|---|
| I-1 | Only terminal tasks (Succeeded, Failed, Canceled) whose `CompletedAt` is older than `MinTerminalHours` are candidates. Queued, Dispatched, Working and Blocked (parked included) never are |
| I-2 | Clean: no tracked modification and no untracked file at the mirror root (`--untracked-files=all`) |
| I-3 | No commit missing from origin (`rev-list HEAD --not --remotes=origin` returns 0) |
| I-4 | The branch tip is present on **live** origin (`ls-remote`) and contains HEAD |
| I-5 | No land pending, no open sync debt, no open task sharing the path, no live session row |
| I-6 | No live process has its cwd or an fd under the tree. An incomplete scan refuses |
| I-7 | `git worktree remove` is never forced. No `rm`, `prune`, `fetch` or branch deletion |
| I-8 | One candidate per request and one request at a time, each rechecked immediately before (server row re-read, runner steps 1-7 re-run under the lock) |
| I-9 | Bounded: at most `MaxRemovalsPerTick` Execute requests per runner per tick, with `PauseBetweenSeconds` between them |
| I-10 | Superseded by the operator decision of 2026-11-03 ("Enable this on by default", D-15): the job ships ON with `Execute=true` for `Runners=["server2"]` (appsettings only). `Enabled=false` is the kill switch (re-read before every request) and `Execute=false` or the always-DryRun preview route is the dry run |
| I-11 | Never touches Docker volumes, `/state`, `/work/repos`, `/work/cache`, `/work/evidence`, slots (`ws-*`) or names outside `^task-[0-9a-f]{8}$` |
| I-12 | The desktop and the local runner are never contacted, and the desktop worktree is untouched |
| I-13 | The `/tmp` sweep removes only registered families, older than `MinAgeHours` by newest file write, owned by the runner uid, with no holder, not denied and not symlinked |
| I-14 | Every removal and every skip is logged with its reason and recorded as a run candidate row |

## 5. Settings and the enabling step

`server/appsettings.json`:

```json
"RunnerWorktreeRetention": {
  "Enabled": false, "Execute": false, "Runners": [], "Cron": "*/30 * * * *",
  "MinTerminalHours": 12, "MaxRemovalsPerTick": 25, "PauseBetweenSeconds": 5,
  "RequestTimeoutSeconds": 300, "RetiringRoot": "/work/worktrees-retiring",
  "TempSweep": { "Enabled": false, "Execute": false, "MinAgeHours": 48, "MaxEntriesPerTick": 200,
                 "MaxBytesPerTick": 21474836480, "Families": { "claude-scratch": false } }
},
"WorkspaceDiskAlarm": { "Enabled": true, "UsedPercent": 85, "SampleFreshMinutes": 15,
                        "Runners": { "server2": "/work" } }
```

The validator refuses `MinTerminalHours < 1`, `MaxRemovalsPerTick` outside 1..100, a relative
`RetiringRoot`, `UsedPercent` outside 50..99, and `Execute=true` with an empty `Runners` (the latter
as a startup warning).

**Enabling (documented in `docs/docker-stack.md`, new subsection "Runner worktree retention
(CARD-1677)", and in `docs/ops-http.md`):**

The original three enabling steps (dry run first, then `Execute=true` with `MaxRemovalsPerTick=1`) are
superseded by the operator decision of 2026-11-03 (D-15): the job ships enabled with `Execute=true` for
`Runners=["server2"]`, age 12 h. The census is the preview route (`POST /api/agent-tasks/worktree-residue/runner-retention/server2/preview`,
always DryRun) or any tick's run page (`GET /api/agent-tasks/worktree-residue/runs/{runId}?page=n`,
following every page). The temp sweep still follows the dry run first steps under `TempSweep:*`, and
`claude-scratch` stays off until its dry-run list has been read.

Rollback: `Enabled=false` (the kill switch; it also stops a running tick), or `Execute=false` for a dry run.
A tree left in the retiring root is reported and retried, and is never moved back.

## 6. Slices

### S1: retention engine, inventory, dry-run (server + runner, Linux runner only)

**Files.**
- `src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs`: operations
  `WorkspaceRetentionInventory = 45` and `WorkspaceRetain = 46`, plus request and response records.
- `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs`:
  `RunnerCapabilityFeatures.WorkspaceRetentionV1`.
- **new** `src/Antiphon.SessionRunner/RunnerWorkspaceRetention.cs` (steps 1-10, inventory).
- `src/Antiphon.SessionRunner/WorkspaceSlotSeams.cs`: a `HolderScan` result with `Complete` or
  `Unknown`. The slot probe keeps its current behaviour; retention uses the fail-closed overload.
- `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs`: phase A runs under `MutateAsync`;
  phase B and inventory run outside it.
- `src/Antiphon.SessionRunner/PhoneHomeRuntimeAdapter.cs` (capability).
- `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs` (two calls).
- **new** `server/Application/Settings/RunnerWorktreeRetentionSettings.cs` and its validator.
- **new** `server/Application/Services/RunnerWorktreeRetentionService.cs` (3.1, 3.2, recording).
- **new** `server/Infrastructure/Agents/RunnerWorktreeRetentionJob.cs`.
- `server/Infrastructure/Agents/HangfireConfiguration.cs`, `server/Program.cs`,
  `server/appsettings.json`.
- `server/Api/Endpoints/` preview route.
- `docs/docker-stack.md`, `docs/ops-http.md`, `docs/antiphon-api.md`.
- `tests/test-execution-policy.json` rows for the `[NativeLinuxOnly]` methods.

**Tests (red first; TestDesign names the methods).**
`tests/Antiphon.SessionRunner.Tests/C1677RetentionEngineTests.cs` is portable. It uses real Git: a
bare `origin`, a clone acting as the runner repository, and `task-<8hex>` worktrees under a temp
`WorktreeRoot`. A scripted `FakeHolderProbe` replaces the process scan, and a recording Git seam
captures argv.
- V-1 a clean, pushed, old, finished mirror with origin containing HEAD: Execute gives `removed`.
  The directory and registration are gone, the argv has no `--force` and no `prune`, and the retiring
  copy is gone.
- V-2 a tracked modification survives: `dirty`.
- V-3 an untracked file survives: `dirty` (`--untracked-files=all`).
- V-4 a local commit not on origin survives: `unpushed`.
- V-5 a branch absent on origin survives: `branch_absent_on_origin`.
- V-6 origin's branch is reset behind HEAD while the stale `refs/remotes/origin/<b>` still contains
  HEAD. The mirror survives with `origin_does_not_contain_head` (proves the live `ls-remote`).
- V-7 a holder (fake probe) survives: `live_process`, with pid and argv0 in the response.
- V-8 an `Unknown` scan survives: `holder_scan_unknown`.
- V-9 a leaf that does not match the task id, a symlinked leaf, or a path outside the root survives:
  `outside_scope`.
- V-10 `ws-03` and a ledger slot path survive: `slot_managed`.
- V-11 a locked worktree survives: `locked`.
- V-12 DryRun with all checks passing gives `would_remove`. The tree is intact and no mutating Git
  verb is in the argv.
- V-13 phase B refusal: a file written into the moved tree after phase A (seam hook) leaves it in
  the retiring root, registered, as `retiring_refused`. No force retry.
- V-14 the `MutationLock` is free during phase B: a concurrent `Input`-class mutate op completes
  while phase B is held at a seam gate.
- V-15 a cross-device retiring root refuses `retiring_root_cross_device` with nothing moved (seam
  `st_dev`).
- V-16 a nested repo under ignored `.antiphon/` is removed with the tree (`nestedIgnoredRepos=1`). A
  nested repo at a non-ignored path survives as `dirty`.
- V-17 an absent path gives `already_absent`, with no `prune` in the argv.

`tests/Antiphon.SessionRunner.Tests/C1677RetentionInventoryTests.cs`:
- V-18 the inventory lists `task-cffa0c4b-uA`-shaped names with `registered=true` and `slotLeaf=false`,
  and lists retiring-root entries.
- V-19 `WorkspaceRetain` for an unmatched name refuses `outside_scope` in Execute.

`tests/Antiphon.SessionRunner.Tests/C1677RetentionNativeProcessTests.cs` (`[NativeLinuxOnly]`, real
`/proc`):
- V-20 a child `sleep` with its cwd in the tree survives as `live_process`.
- V-21 a child holding an open fd under the tree survives as `live_process`.
- V-22 after the child exits, the same tree is removed.

`tests/Antiphon.Tests/Application/C1677RetentionServerTests.cs` (shared Postgres, fake phone-home
client, `FakeTimeProvider`):
- V-30 selection sends only the eligible old terminal task. Queued, Dispatched, Working and Blocked
  tasks (`live_owner`), a terminal task 11 h old (`settling`), land pending, sync debt, slot-bound,
  SourceLanding, repair-source, Mutation, path shared with an open task and live session rows are
  each recorded with their reason and never sent.
- V-31 `Enabled=false` sends nothing. `Enabled=true` with `Execute=false` sends `DryRun` only, and an
  unlisted runner or the local runner is never contacted.
- V-32 the bound: 5 eligible with `MaxRemovalsPerTick=2` give exactly 2 Execute requests, oldest
  `CompletedAt` first, the second only after the first response.
- V-33 recheck before each send: a task reopened (set to Queued) after selection and before its turn
  is not sent.
- V-34 recording: `removed` sets `RemoteWorktreePath=null` and writes an Info event and a `Removed`
  row. A kept tree gets a reason row with its path unchanged. A transport loss is `unknown` with the
  path unchanged.
- V-35 inventory names that match no task become `RunnerInventory`/`identity_unknown` rows with an
  owner hint. A two-task match is `identity_ambiguous`.

`tests/Antiphon.Tests/Application/C1677RetentionSettingsTests.cs`:
- V-36 the validator bounds from section 5, and the shipped `appsettings.json` has `Enabled=true`,
  `Execute=true`, `Runners=["server2"]` (D-15; originally off and dry-run), while the type keeps `Runners=[]`.

**Regression rows.** `RunnerWorkspaceServiceTests` (`RemoveAsync` and the mirror path unchanged),
`PhoneHomeConnectionTests.Supported_operations_preserve_contracts` (every-operation enumeration), and
`NativeLinuxPolicyWitnessTests` (new policy rows).

### S2: `/tmp` sweep (runner + server tick)

**Files.** **new** `src/Antiphon.SessionRunner/RunnerTempSweep.cs` (family table, walk, delete via
`RunnerWorkspaceNoFollowDelete`), the contract operation `TempSweep = 47`, the dispatcher (outside
`MutationLock`: it is not a workspace mutation and never touches `/work`), and
`RunnerWorktreeRetentionService` (tick and recording, Lane `RunnerTemp`), plus docs.

**Tests.** `tests/Antiphon.SessionRunner.Tests/C1677TempSweepTests.cs` (portable, injected temp root,
uid and holder seams):
- V-40 an old `c487-probe-<32hex>` with no holder is `removed` (Execute) or `would_remove` (DryRun).
- V-41 an unregistered name (`g104-<hex>`) is never touched; it is reported `unregistered` with its
  bytes.
- V-42 a 49 h-old directory mtime with one file written 1 h ago survives (`too_young`).
- V-43 a holder or an unknown scan survives.
- V-44 `antiphon-pty-hosts`, `antiphon-pty-<hex>` and `CoreFxPipe_x` are denied even when matching
  ages are faked.
- V-45 a foreign-uid descendant survives as `foreign_owner`.
- V-46 the per-tick entry and byte bounds hold.
- V-47 `claude-scratch` is eligible only with its family flag on and the mirror and retiring copy
  absent.

`tests/Antiphon.SessionRunner.Tests/C1677TempFamilyRegistryTests.cs`:
- V-48 every family's producer file still contains its prefix literal, and the `pty-test-root`
  alternation equals `PtyHostLeakSweep.KnownPrefixes`. Source read from the repository root, not the
  test's cwd.

`tests/Antiphon.SessionRunner.Tests/C1677TempSweepNativeTests.cs` (`[NativeLinuxOnly]`):
- V-49 a symlinked entry pointing at a directory outside `/tmp` is refused (`symlink`) and its
  target is intact.
- V-50 a real cwd holder survives.

`tests/Antiphon.Tests/Application/C1677TempSweepServerTests.cs`:
- V-51 `TempSweep:Enabled=false` sends nothing, the default mode is DryRun, and the rows are
  recorded.

### S3: `WorkspaceDiskLow` attention (server + client)

**Files.** `server/Application/Dtos/AttentionDtos.cs` (`WorkspaceDiskLow = 65`),
`AttentionService.Alarms.cs` (builder), `AttentionService.cs` (an optional `HostStatsCache`
dependency and call site), **new** `WorkspaceDiskAlarmSettings`, `Program.cs`, `appsettings.json`,
`client/src/api/attention.ts`, `client/src/features/attention/attentionVisuals.ts` and its test.

**Tests.** `tests/Antiphon.Tests/Application/C1677DiskAlarmTests.cs`:
- V-60 `/work` at 85.0 percent used raises one Warning `workspace-disk-low:server2`.
- V-61 84.9 percent raises nothing.
- V-62 only the configured volume counts: `/state` at 95 percent with `/work` at 50 percent raises
  nothing.
- V-63 a stale sample raises nothing.
- V-64 the desktop runner (unlisted) at 99 percent raises nothing.
- V-65 the evidence string carries free and total bytes and the retention mode.

`AttentionKindWireTests` (wire value 65) and client `attentionVisuals.test.ts` (`ALL_KINDS` gains
`WorkspaceDiskLow`).

### Checkpoints

The lane is server2 (Linux), the default runner (`GET /api/runner-defaults`: `globalRunnerId=server2`).
Omit `-Runner`. No row needs the desktop. Each slice group has its own builds (the import tool
refuses a build reused across After groups).

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c1677-s1r/ | c1677-runner-engine | /*/*/(C1677RetentionEngineTests*)\|(C1677RetentionInventoryTests*)/* | V-1..V-19 | classes, 0 failed/skipped | 19 | 4 | true |
| CP-2 | S1 | CP-1 | c1677-runner-native-process | /*/*/C1677RetentionNativeProcessTests/* | V-20..V-22 | class, 0 failed/skipped | 3 | 2 | true |
| CP-3 | S1 | CP-1 | c1677-runner-workspace-regression | /*/*/RunnerWorkspaceServiceTests/* | R-1 | existing class, 0 failed/skipped | 35 | 3 | true |
| CP-4 | S1 | CP-1 | c1677-native-policy-regression | /*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* | R-2 | existing class, 0 failed/skipped | 3 | 1 | true |
| CP-5 | S1 | tests/Antiphon.Tests -> bin-c1677-s1s/ | c1677-server-retention | /*/*/(C1677RetentionServerTests*)\|(C1677RetentionSettingsTests*)/* | V-30..V-36 | classes, 0 failed/skipped | 14 | 10 | true |
| CP-6 | S1 | CP-5 | c1677-phonehome-ops-regression | /*/*/PhoneHomeConnectionTests/Supported_operations_preserve_contracts | R-3 | method, 0 failed/skipped | 1 | 2 | true |
| CP-7 | S2 | tests/Antiphon.SessionRunner.Tests -> bin-c1677-s2r/ | c1677-runner-temp | /*/*/(C1677TempSweepTests*)\|(C1677TempFamilyRegistryTests*)/* | V-40..V-48 | classes, 0 failed/skipped | 10 | 3 | true |
| CP-8 | S2 | CP-7 | c1677-runner-temp-native | /*/*/C1677TempSweepNativeTests/* | V-49, V-50 | class, 0 failed/skipped | 2 | 2 | true |
| CP-9 | S2 | tests/Antiphon.Tests -> bin-c1677-s2s/ | c1677-server-temp | /*/*/C1677TempSweepServerTests/* | V-51 | class, 0 failed/skipped | 3 | 6 | true |
| CP-10 | S3 | tests/Antiphon.Tests -> bin-c1677-s3s/ | c1677-server-disk-alarm | /*/*/(C1677DiskAlarmTests*)\|(AttentionKindWireTests*)/* | V-60..V-65, R-4 | classes, 0 failed/skipped | 10 | 5 | true |
| CP-11 | S3 | n/a | c1677-client-attention | `pwsh -NoProfile -File scripts/test-client.ps1 attentionVisuals.test.ts` | R-5 | file, 0 failed | n/a | 3 | true |

Suffix collision check (grep of `class (C1677\|RunnerWorkspaceServiceTests\|AttentionKindWireTests\|PhoneHomeConnectionTests)`
at `c4e7aa06e`): no `C1677*` class exists yet, and each existing operand matches exactly one
class. `C1677TempSweepTests*` does not match `C1677TempSweepNativeTests` or
`C1677TempSweepServerTests`, and `C1677RetentionEngineTests*` does not match
`C1677RetentionNativeProcessTests`. TestDesign rechecks the roster after naming the methods and sets
the `Min` floors from the counted cases (`[Arguments]` cases included).

## 7. Positive controls (one mutant per check; SourceLanding Mutation runs them method-scoped)

| PC | Mutant | Must turn red |
|---|---|---|
| PC-1 | Drop the status check (step 4) | V-2, V-3 |
| PC-2 | `--untracked-files=no` | V-3 |
| PC-3 | Drop the `rev-list` unpushed check | V-4 |
| PC-4 | Replace `ls-remote` with `refs/remotes/origin/<b>` | V-6 |
| PC-5 | Treat zero `ls-remote` lines as present | V-5 |
| PC-6 | Ignore holders | V-7, V-20, V-21 |
| PC-7 | Treat an `Unknown` scan as empty (fail-open) | V-8 |
| PC-8 | Add `--force` to phase B | V-1 (argv), V-13 |
| PC-9 | Phase B under `MutationLock` | V-14 |
| PC-10 | DryRun proceeds to phase A | V-12 |
| PC-11 | Drop the slot guard | V-10 |
| PC-12 | Leaf/task-id equality removed | V-9, V-19 |
| PC-13 | Server: `>=` becomes `<` on the age floor | V-30 (too-recent sent) |
| PC-14 | Server: `Blocked` added to the terminal set | V-30 |
| PC-15 | Server: land-pending predicate removed | V-30 |
| PC-16 | Server: sync-debt predicate removed | V-30 |
| PC-17 | Server: no re-read before send | V-33 |
| PC-18 | Server: per-tick bound ignored | V-32 |
| PC-19 | Server: `Execute` ignored (always Execute) | V-31 |
| PC-20 | Server: success leaves `RemoteWorktreePath` set | V-34 |
| PC-21 | Temp: unregistered names eligible | V-41 |
| PC-22 | Temp: directory mtime instead of newest file | V-42 |
| PC-23 | Temp: deny list skipped | V-44 |
| PC-24 | Temp: follow symlinks | V-49 |
| PC-25 | Temp: uid check removed | V-45 |
| PC-26 | Alarm: `>` 85 becomes `>=` 86 / threshold inverted | V-60, V-61 |
| PC-27 | Alarm: first disk instead of the configured path | V-62 |
| PC-28 | Alarm: freshness ignored | V-63 |

## Decisions

- **D-1 Retention age 12 h (setting `MinTerminalHours`).** This is the card's default and the age
  the e937b0c3 run proved (437 eligible, 0 Git refusals). CARD-0824 H-6 decided 48 h for its S4
  legacy migration. That is a stricter setting for the same population; an operator may set 48 at
  enabling. Steady state at 80 GiB a day keeps ~40 GiB at 12 h or ~160 GiB at 48 h, and both fit
  the 188 GiB free today. *Operator choice at enabling, not blocking.*
- **D-2 Server decides, runner verifies (recommended).** Rejected alternatives:
  (a) *Extend CARD-0459's job.* It runs in the server process on the desktop filesystem. Its
  settled-task lane needs per-task caller releases, and one `Execute` switch would arm desktop and
  runner deletion together. (b) *A runner-only service.* The runner has no task status, land, sync
  debt or session facts, so it would guess "finished" from mtimes. (c) *A host-side script* (cron
  or Windmill over `docker exec`). It needs host ssh, is untested product logic outside the repo's
  test lanes, and CARD-0826's Windmill path is not operational. The chosen shape keeps the authority
  each side already has: the server's `AgentTasks` and the runner's uid-1654 Git custody of
  `/work/worktrees`.
- **D-3 Do not reuse CARD-0826 or CARD-1032 machinery.** CARD-0826 is not operational and treats
  worktrees as inventory-only. CARD-1032's completed-card removal needs a Done card, mirror-binding
  rows and a signed landing authority. It is right for card closure, but too narrow and too heavy
  for "task finished 12 h ago". Both stay as they are.
- **D-4 Pacing.** A Hangfire recurring job every 30 minutes, at most 25 removals per runner per tick
  and 5 s between requests: ~1,200 a day of capacity against ~115 a day of arrivals. The first
  backlog (~120 eligible) drains in about 5 ticks.
- **D-5 A new runner path, not `RemoveAsync`.** `RemoveAsync` forces and prunes. Changing it would
  alter retirement and drain re-arm semantics, which belong to their own cards. Retention gets
  `WorkspaceRetain` with the card's exact checks.
- **D-6 Detach then delete.** Phase A (recheck, then `git worktree move` within the same device)
  runs under `MutationLock`. Phase B (`git worktree remove`, no force) runs outside it. Deleting a
  multi-GiB tree under the lock would stall every session's `Input`. Rejected: removing under the
  lock (stalls input for an unmeasured time, up to minutes for the 23 GiB trees seen in 2026-09);
  removing outside the lock without the move (a reopened task's re-mirror of the same
  `task-<id>` name races the deletion).
- **D-7 Fail-closed holder probe.** A new `HolderScan` result with `Unknown` when `/proc` cannot be
  enumerated. Other-uid processes stay skipped (the reviewed S1 limit) and their count is recorded.
- **D-8 No migration.** Runs and candidates go into `WorktreeResidueRun`/`WorktreeResidueRunCandidate`
  with `Scope="runner-retention:<runner>"` and lanes `RunnerMirror`, `RunnerInventory` and
  `RunnerTemp`. The existing run read route serves them. `ShouldWriteRow` deduplication is reused
  so that unchanged `Held` rows are not rewritten every tick.
- **D-9 Ignored content is disposable after N hours.** `.antiphon/` evidence, `bin`/`obj` and nested
  scratch repos under ignored paths go with the tree, as `git worktree remove` already does without
  force. The durable report is `AgentTask.Result`, and a missing `Result` holds the tree
  (`report_unpreserved`). This resolves CARD-1590 for terminal owners. CARD-1590's fixture fix is
  still needed for live tasks and the recycle audit.
- **D-10 CARD-0824 relationship.** This engine is S4's runner half: S4's V-24 ("dirty, unpushed or
  unknown kept") and V-25 ("batch bound") are V-2..V-8 and V-32 here. S4 keeps the desktop automatic
  release policy and its slot-specific watchdog keys. S3's `WorkspaceDiskLow` is §3.11's
  `workspace_disk_low`.
- **D-11 Linux phone-home runners only.** The capability gate plus the `Runners` allow-list keep the
  desktop out. The desktop's 1,025 registrations remain CARD-0824 S5's (and CARD-0459's) problem.
  The alarm has its own `Runners` map, so desktop disks are not alarmed either.
- **D-12 `claude-scratch` family behind its own flag.** It is the largest `/tmp` consumer (21.1 GiB
  orphaned) and maps exactly to a mirror name, but it is created by the CLI harness, not by repo
  scripts, which the card's literal rule names. It is in the dry-run report from day one; deletion
  needs `Families:claude-scratch=true`.
- **D-13 Alarm default on, report only.** It deletes nothing, so it ships enabled for `server2`. Since D-15 the
  retention job it reports alongside is on too; the alarm keeps reading `RunnerWorktreeRetention:Enabled`/`Execute`.
- **D-14 Branch on origin is required.** An old task whose branch is gone from origin, but whose HEAD
  is in `master`, is kept as `branch_absent_on_origin` and counted. Accepting "contained in target"
  is a later relaxation, and only after the first dry run shows how many trees it would affect.
  CARD-0824 S2 (origin branch deletion) must therefore be enabled on a volume only after this
  engine has drained it, which is already S2's own rule.

- **D-15 On by default (operator decision 2026-11-03, "Enable this on by default"; review c238f9ca).**
  Shipped `Enabled=true`, `Execute=true`, `Runners=["server2"]` (appsettings only: the C# list stays empty
  because binding appends to an initializer), `MinTerminalHours=12`. It is safe on by default only together
  with the review's D-1 fix: a tree the runner keeps is backed off (`KeptRecheckMinutes` 60, doubling per
  repeat of the same reason, at most `KeptRecheckMaxHours` 24; a changed reason or changed task facts re-send
  at once), never-sent trees go first, and a dry run censuses the whole population tick by tick. A tick
  starts no request past `MaxTickMinutes` (20) and stops when `Enabled` or `Execute` is switched off. The
  runner keeps a tree it mirrored or launched a session in within `PhoneHome:RetentionRecentUseMinutes` (10)
  and any tree a live session of its own runs in (review D-2).
## 8. Measure on the real server2 mirror before `Execute=true`

1. A dry-run census: rows per verdict and reason, compared against a fresh manual run of the
   e937b0c3 checks on 10 sampled trees (the verdicts must agree).
2. `ls-remote` p95 and failure rate per candidate (budget 60 s; expected ~1 s).
3. Holder scan duration and pid count (expected under 0.5 s), plus how many trees are held by
   pooled sessions (e937b0c3 saw 3).
4. With `MaxRemovalsPerTick=1`: phase A ms (rename, expected under 1 s), phase B duration for the
   largest candidate, and `df -Pk /work` before and after. Set `RequestTimeoutSeconds` to about 2x
   the measured phase-B p95 (provisional 300).
5. That `/work/worktrees-retiring` is on the same `st_dev` as `/work/worktrees` (one LV today).
6. That `Input` p95 during a tick does not regress (phase B outside the lock).
7. The `RunnerInventory` report on the 17 folders, sent to the owning tasks' callers.
8. For S2: the dry-run family totals against this plan's census, and `unregistered` bytes by prefix.

## Performance budget

Owner: [docs/testing-and-build.md#performance-standards](../../testing-and-build.md#performance-standards)
and [test-performance-standards.md](../../test-performance-standards.md).

- **Selection.** CP-1..CP-11 above, three After groups, five isolated builds and one client row.
  The downstream graph is SessionRunner.Tests (runner and contracts changes) and Antiphon.Tests
  (server, contracts). No E2E: no UI behaviour changes beyond one attention visual entry.
- **Durations (provisional; TestDesign measures).** The source is the CARD-0824 S1 checkpoint table
  on server2 (Linux, loaded) at `2026-11-02`: portable SessionRunner slot classes with real Git
  temp repos took 2-4 min for 9-15 cases, a linux-native process class 2-4 s per body, and a
  server slot class with shared Postgres 14 min for 22 cases. Expected here: CP-1 ~19 cases at
  ~5-10 s each, ~3-4 min. CP-2 ~3 × 3 s. CP-5 ~14 cases at ~20-30 s (schema plus fake client),
  ~8-10 min. CP-10 ~10 cases, ~5 min. CP-11 ~3 min. Builds and slot waits are reported separately.
  Every row is under 30 minutes; none is expected over 15.
- **Phases.** Git fixture: one bare origin plus clone per test class (`[Before(Class)]`), and a
  fresh worktree per test (isolation needs a per-test tree). Each test deletes its trees; class
  teardown deletes the origin and clone. Server: the shared Postgres test database with per-test
  schema isolation (existing fixture). No container start in these rows. Native rows start one
  `sleep` child each and kill it in `finally`.
- **Setup sharing.** The class-level Git origin is shared, and so is the server fixture. The per-test
  isolation witness is that each test asserts its own `task-<8hex>` leaf and fresh id.
- **Timeouts.** Per test `[Timeout(60_000)]` for portable runner tests (2x a provisional p95 of
  ~25 s for the slowest Git case) and `[Timeout(120_000)]` for server tests. TestDesign replaces
  both with 2x measured p95 from its red run. The runner's `ls-remote` uses a 60 s budget (2x a 30 s
  worst case on a loaded host; expected ~1 s). The holder scan and nested-repo count use 10 s.
- **Recording.** Checkpoint receipts and TRX under the checkpoint run directory (gitignored).
  Per-phase timings (`phaseAMs`, `phaseBMs`) are in each response and so in the run candidate rows.
- **Production cost.** One tick does one inventory, up to 25 sequential requests of about 1-30 s,
  and 5 s pauses: under 15 min a tick at the start, ~1 min in steady state, against a 30 min
  period. `DisableConcurrentExecution` prevents overlap.

## Waiting-input checklist

Not applicable. This plan adds no session that waits for input, and parking is untouched.

## Risks

- A tree whose only copy of something lives under ignored `.antiphon/` is lost after N hours (D-9).
  Mitigation: `Result` is in the database, and `report_unpreserved` holds the tree when it is not.
- Other-uid holders are invisible to the probe (D-7). In the runner container, sessions and builds run
  as uid 1654.
- A Hangfire job that crashes mid-tick leaves at most one moved tree in the retiring root. It is
  reported, retried, and never moved back.
- The ~210 GiB of host-side residue is untouched. It needs the desktop lane's host ssh census
  (e937b0c3 recommendation).
