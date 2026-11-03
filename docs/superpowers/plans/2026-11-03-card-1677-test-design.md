# CARD-1677 runner-side retention of finished task worktrees: test design

TestDesign task 988017cf, 2026-11-03, server2 (Linux, uid 1654), on the Plan branch tip
`b477d7ccf4faafa94cb91fd785ced348192885dc` (base master `c4e7aa06e` plus the plan commit).
Scope: S1, S2 and S3 of the [plan](2026-11-03-card-1677-runner-worktree-retention-plan.md) (§3-§8, D-1..D-14).
This companion adds verification detail and does not change the fix design. D-1 (12 h default), D-6
(detach then delete) and D-8 (no migration) are kept. Where a seam the plan assumes is missing or does
not work, the smallest seam is named (§Seams). No production or test source was changed. Every
expected result below is a design, not executed evidence, except the facts under "Measured facts".

## Measured facts used by this design (server2, tip `b477d7ccf`, native output, one build-slot lease each)

Load average 5-14 on 24 cores during these runs (lighter than the 29-37 of the CARD-1653/CARD-1660
samples). Loaded figures below use CARD-1660's ratio: C0824 server p95 17.2 s at load 37 against
10.4 s here, a factor of 1.65.

- `tests/Antiphon.SessionRunner.Tests` build (`-p:UseAppHost=false`): **27 s held**, slot wait 0 s
  (warm; CARD-1653 S1 measured 82 s cold; the overall p95 stays 135 s).
- `tests/Antiphon.Tests` build: **143 s held**, slot wait 0 s.
- SessionRunner analogs, `/*/*/(RunnerWorkspaceServiceTests*)|(C0824WorkspaceSlotProcessScanTests*)|(C0824WorkspaceSlotResetTests*)|(NativeLinuxPolicyWitnessTests*)/*`:
  **51/51 green, 14.0 s**, lease held 16 s. Per class (TRX): `RunnerWorkspaceServiceTests` 35, sum 5.1 s,
  p95 0.21 s (CP-3 Min **35**); `C0824WorkspaceSlotResetTests` (real Git scratch origin + clone + worktree
  per test, the closest analog of the engine class) 10, sum 6.0 s, p50 0.46 s, **p95 2.12 s**;
  `C0824WorkspaceSlotProcessScanTests` (real `/proc` cwd/fd holder children, the analog of the native
  class) 3, sum 1.8 s, **p95 0.78 s** (CP-12 Min **3**); `NativeLinuxPolicyWitnessTests` 3, p95 0.66 s,
  all green at this tip (CP-4/CP-13 Min **3**).
- Server analogs, `/*/*/(C0824WorkspaceSlotServerTests*)|(WorktreeResidueCandidateRowTests*)|(RunnerAlarmAttentionTests*)|(AttentionKindWireTests*)/*`:
  **34/34 green, 58.3 s**, lease held 62 s. `C0824WorkspaceSlotServerTests` (the `Rig` with a scripted
  phone-home peer and shared Postgres, run in parallel) 24, p50 8.2 s, **p95 10.4 s**, max 10.4 s;
  `WorktreeResidueCandidateRowTests` 3, p95 3.7 s; `RunnerAlarmAttentionTests` (isolated schema, the S3
  analog) 3, **p95 5.3 s**; `AttentionKindWireTests` **4**, p95 0.04 s (CP-10 operand).
- `PhoneHomeConnectionTests/Supported_operations_preserve_contracts`: **1/1 green, 0.44 s** (CP-6 Min **1**).
- Raw Git cost of one full retention cycle (bare origin + seed + clone + `worktree add`, two passes of
  status/rev-list/ls-remote/cat-file/merge-base, `worktree move` + `worktree remove`), 8 shell runs:
  **90-176 ms**, fixture 49-111 ms, checks 27-61 ms, move+remove 5-13 ms. The per-test cost of the Git
  classes is the .NET process spawning and fixture, which the C0824 analog measures (p95 2.1 s).
- Git 2.47.3 behaviour checked in a scratch repo (each fact is a witness precondition):
  - `git --git-dir=/dev/null ls-remote origin <ref>` run **inside a mirror** fails: `fatal: 'origin' does
    not appear to be a git repository`. `RunGitCoreAsync(inspection: true)` adds exactly that git-dir
    for `ls-remote`, so the plan's `ls-remote origin` cannot work under the inspection runner (seam S-1).
    The same command run from a directory that contains a folder named `origin` succeeds by resolving it
    as a relative path: the fixture never runs ls-remote with cwd = the scratch root.
  - `ls-remote <url> refs/heads/<b>` matches by tail: a decoy `refs/heads/x/refs/heads/<b>` is listed as
    a second line. The exact ref-name compare is therefore its own guard (G-17).
  - `status --porcelain` with default `normal` shows `?? d/` and `--untracked-files=all` shows `?? d/f`;
    `=no` shows nothing. So only `=no` is a live mutant; `normal` is equivalent for emptiness.
  - An empty untracked directory shows nothing and `worktree remove` (no force) deletes it.
  - A nested repo under ignored `.antiphon/` leaves status empty, and `worktree remove` (no force)
    removes the tree with it (exit 0). A nested repo at a non-ignored path shows `?? seedrepo/`.
  - `worktree move` moves a **dirty** tree (exit 0); `worktree remove` without force then refuses it
    (`contains modified or untracked files`, exit 128). A **locked** tree refuses `worktree move`
    (exit 128). Both are Git's own checks that mask a dropped runner check on survival (§Masking).
  - `/work/worktrees` and `/work/worktrees-retiring` report the same `st_dev` in a scratch dir
    (64768), as on the real volume (plan §8 item 5 is still measured at enabling).
- Windows: **not measured and not needed**: every row is server2; the desktop is never a retention target (D-11).
- `import --plan` on this document's table: see §Checkpoints, "Import verdict".

## Verification design

### Inspection

Bodies read at `b477d7ccf` before naming the cases:

- `RunnerWorkspaceService` (`RunnerWorkspaceService.cs`): `NamePattern ^task-[0-9a-f]{8}$` (:21), the
  internal ctor `(repository, allowedCwd, cloneSource, Func<ProcessStartInfo, Process?> startProcess, …)`
  (:55) that every Git fixture uses to record argv and inject faults; `WorktreeRoot = <allowedCwd>/worktrees`
  (:83); `RemoveAsync` (:538-589: plain `status --porcelain`, `worktree remove --force`, repository-wide
  `worktree prune`, and `PruneRepositoriesAsync` on an absent path); `PublishAsync`'s `TryResolveFinal` +
  direct-child check (:609-615) and `symbolic-ref -q HEAD` (:617); `OwningRepositoryAsync` /
  `IsOwnedCommonDirectory` (:674-690); `InspectionGitAsync` → `RunGitCoreAsync(inspection: true)`
  (:949-1000, `--git-dir=/dev/null` for `ls-remote`, scrubbed config). → seams S-1, S-2; G-1..G-23.
- `RunnerCompletedCardWorkspaceRemoval.cs:115`: the existing inspection `ls-remote --refs <identity URL>`
  call, the pattern S-1 copies.
- `WorkspaceSlotSeams.cs` (:44-110): `ProcWorkspaceSlotProcessProbe.Holders` returns `[]` off Linux and
  when `/proc` cannot be enumerated (fail-open), skips its own pid and unreadable fds. → S-3, G-24..G-26.
- `RunnerWorkspaceSlotPool.IsSlotLeaf` / `SlotByPath` (`RunnerWorkspaceSlotPool.cs:73-79`: ledger slots by
  normalized path; ledger paths are `ws-NN[-dK]` only) and the public `RunnerWorkspaceSlotLedger.Write`
  (`RunnerWorkspaceSlotLedger.cs:124`). → G-5/G-6 and the masking note M-2.
- `RunnerWorkspaceUseGate.AcquireRemovalAsync` (`RunnerWorkspaceUseGate.cs:20-42`): refuses
  `workspace_in_use` (admission busy or owner) and `workspace_retiring_or_unknown` (journal overlap or
  census not ready); its header says no production composition installs it. → G-7, M-13.
- `PhoneHomeCommandDispatcher`: `MutateAsync` (:941-958) and the single `MutationLock` (:1028); every
  mutating op including `Input` (:378) and `WorkspaceMirror` (:389) runs under it; `IPhoneHomeRuntimeSurface`
  default interface methods (:7-60). → S-4, G-36..G-38.
- Tests and fixtures: `C0824SlotScratch` (origin, clone, `Argv` recording, `GitFault` sh-fault injection,
  `FakeProcesses`, `Plant`, `Tree` hash, `GitExit`), `C0824WorkspaceSlotProcessScanTests` (`Child`,
  `WaitForHolderAsync`, `Stop`; `[NativeLinuxOnly]`, `[ParallelLimiter<ProcessSpawnLimit>]`,
  `[Timeout(10_000)]`), `PhoneHomeCommandDispatcherTests.Dispatcher(...)`/`RecordingRuntime`,
  `RemoteWorkspacePreparerTests.Rig` (scripted peer, `FakeTimeProvider Clock`, `SeedAsync`,
  `SeedLandingAsync`, `WaitForRequestsAsync`, `Peer.SilentFor`/`EmitAsync`/`RequestCount`),
  `C0824WorkspaceSlotServerTests.C0824_ReleaseReconcilerRequiresAllConditions` (the one-condition-per-case
  `[Arguments]` shape) and `C0824SlotTransportWorld` (server `Rig` plus a **real** runner dispatcher over a
  scratch repository; `Deliver`, `Intercept`, `StartRunner`), `RunnerAlarmAttentionTests` (isolated schema,
  `RunnerAlarmState`), `HostStatsCache.Record(hostId, dto)`, `PtyHostLeakSweep.TestSessionLogRoot.KnownPrefixes`
  (27 entries at this tip), `tests/test-execution-policy.json` (`C0824WorkspaceSlotProcessScanTests` row at
  :884-890, `policyHash` at :3). → fixtures F-1..F-7.
- Producer lines of the S2 families, re-read at this tip: `TestClassificationPolicyTests.cs:469`
  (`c487-probe-`), **`scripts/lib/c487-harness.ps1:36`** (`'c487-' + [guid]::NewGuid().ToString('N')` under
  `$env:TEMP`; `test-nightly-run.ps1:15` and `test-live-lane-receipt.ps1:8` only dot-source it, so the pin
  is the lib file), `RemoteControlPtyLane.cs:80`, `SessionGenerationExitTests.cs:658`, `C1514Support.cs:15`,
  `AgentTaskLandService.cs:1946`, `BridgeQueueHarness.cs:117`, `RcFullTimingContractTests.cs:239,246`,
  `PtyHostLeakSweep.cs:15` (`KnownPrefixes`). All present. → V-48.

### Seams

| # | Plan assumption | Code at `b477d7ccf` | Verdict and smallest seam |
|---|---|---|---|
| S-1 | step 6 `git ls-remote origin refs/heads/<Branch>` | **Does not work** under the inspection runner (measured: `'origin' does not appear to be a git repository`), and the non-inspection runner would honour repository-configured URLs and helpers | The engine calls `InspectionGitAsync(path, ct, "ls-remote", "--refs", "--", <endpoint>, "refs/heads/" + Branch)` where `<endpoint>` is the **runner-owned** clone source of the owning repository (`RunnerRepositoryPolicy.PrimaryCloneSource` for the primary, the secondary's configured source otherwise), never a name and never a value from repository config. In the fixture the endpoint is the bare origin's path. G-23 pins it. |
| S-2 | runner test cuts for V-13/V-14 | Missing (new type) | `RunnerWorkspaceRetention(RunnerWorkspaceService, IWorkspaceHolderScan, Func<string, ulong?> deviceOf, string retiringRoot, TimeProvider)` with `internal Action<string>? PhaseCut` (names `"checked"` after the dry pass, `"locked"` at phase A entry before its recheck, `"moved"` after a successful move), `internal Func<string, Task>? PhaseBGate` (awaited with the moved path before `worktree remove`), `internal TimeSpan LsRemoteBudget = 60 s` and `NestedCountBudget = 10 s`. `deviceOf` defaults to the `st_dev` half of `RunnerWorkspaceNoFollowDelete.Identity(path)` (`"<st_dev hex>:<st_ino hex>"` from its `fstat`, `RunnerWorkspaceNoFollowDelete.cs:30-40`, `:274-286`); no new P/Invoke and no `stat` process. |
| S-3 | "a `HolderScan` result with `Complete` or `Unknown`" | Missing | `public sealed record HolderScan(bool Complete, IReadOnlyList<WorkspaceSlotHolder> Holders)` and `public interface IWorkspaceHolderScan { HolderScan Scan(string path); }`. `ProcWorkspaceSlotProcessProbe` implements both; a new internal ctor `(string procRoot = "/proc", bool? isLinux = null)` lets a test point the scan at an unreadable root. `Holders()` keeps today's fail-open result for the slot pool (CP-12). Off Linux `Scan` is `Complete=false`. |
| S-4 | "phase A runs under `MutateAsync`; phase B and inventory run outside it" | The dispatcher owns the only lock; the runtime surface has no retention members | Two default-refusing members on `IPhoneHomeRuntimeSurface`: `Task<WorkspaceRetainStep> RetainWorkspaceCheckAndMoveAsync(PhoneHomeWorkspaceRetainRequest, CancellationToken)` and `Task<PhoneHomeWorkspaceRetainResponse> RetainWorkspaceCompleteAsync(WorkspaceRetainStep, CancellationToken)`. `WorkspaceRetainStep` is either a final response (kept, would_remove, already_absent, a phase-A refusal) or `Moved(movedPath, …)`. The dispatcher runs the first inside `MutateAsync` and the second **after** it returns, only for `Moved`. The split therefore lives in the dispatcher alone, and a fake runtime witnesses it (V-14). |
| S-5 | retiring residue retry "for that exact path" | Not specified where the lookup happens | Before step 1's `already_absent`, the engine looks for `<RetiringRoot>/<leaf>.<8hex>` entries of **this** leaf; one present means phase B is retried for it with no new move (`removed` or `retiring_refused`); `already_absent` is returned only when the leaf and every retiring entry of it are absent. Response field `retiringResidue` (bool). |
| S-6 | `/tmp` sweep "injected temp root, uid and holder seams" | Missing (new type) | `RunnerTempSweep(string tempRoot, IReadOnlyList<TempFamily> families, IWorkspaceHolderScan holders, Func<string, int?> ownerUid, int runnerUid, TimeProvider clock, TempSweepLimits limits, Func<string, bool> mirrorPresent)`. `internal static IReadOnlyList<TempFamily> RegisteredFamilies` is the shipped table; tests inject a broad `^.*$` family for the deny witness (M-14). `TempSweepLimits(MaxEntries, MaxBytes, InventoryEntries = 100_000, InventoryBytes = 1 GiB)`. |
| S-7 | alarm "85.0 percent raises" | double arithmetic on `1 - Free/Total` is not exact at the boundary | Compare in integers: `100 * (Total - Free) >= UsedPercent * Total` (`long`, checked; `Total <= 0` raises nothing). The V-60/V-61 samples are exact under it. |
| S-8 | server tests drive one tick | Missing (new service) | `RunnerWorktreeRetentionService.RunTickAsync(CancellationToken)` returns `(int Sent, Guid? RunId)` per runner; the job only calls it. Registered in the `Rig` with `services.AddScoped<RunnerWorktreeRetentionService>()` and an `IOptionsMonitor<RunnerWorktreeRetentionSettings>` the test edits. |

### Fixtures and binding conventions

- **F-1 `C1677RetentionScratch`** (new `tests/Antiphon.SessionRunner.Tests/C1677RetentionScratch.cs`, no
  `[Test]`), copied from `C0824SlotScratch` (private helpers reused by `using static`): root
  `Path.GetTempPath()/c1677-ret-<32hex>`; a **bare** `origin.git` (`init --bare --initial-branch=main`)
  seeded from a scratch `seed` clone; the runner repository `<root>/repos/antiphon` (a clone of the bare
  origin; `RepositoriesRoot` = `<root>/repos`); `Work = <root>/work`, `Worktrees = Work + "/worktrees"`,
  `Retiring = Work + "/worktrees-retiring"`. `.gitignore`: `.antiphon/`, `bin/`, `obj/` (not `nested/`,
  which `C0824SlotScratch` ignores). Members:
  - `Mirror(string id8 = "a1677001")` → `(Path, Branch, TaskId, Head)`: pushes `feat/card-task-<id8>` to
    origin from `seed`, fetches it into the runner repository (so the tracking ref exists), and runs
    `worktree add <Worktrees>/task-<id8> feat/card-task-<id8>`. TaskId = `Guid.Parse(id8 + "-0000-4000-8000-000000001677")`.
  - `Argv` (every git argv with its cwd, recorded through the internal ctor), `Faults` (`GitFault`, adds
    `GitFault.Sleep(cwd, verb, seconds)`), `Holders` (`FakeHolderScan`: scripted `HolderScan`, `Calls`),
    `Device` (`Func<string, ulong?>`, default real), `Registered(path)` (`worktree list --porcelain` of the
    runner repository lists exactly `worktree <path>`), `MutatingVerbs()` (argv whose verb is one of
    `worktree move|remove|prune|add|lock|unlock|repair`, `fetch`, `push`, `branch`, `reset`, `clean`,
    `checkout`, `update-ref`, `gc`, `rm`), `PushByUrl(m)` (commit in the mirror, `git push <origin path>
    HEAD:refs/heads/<b>`, which does not move `refs/remotes/origin/<b>`), `ResetOriginBehind(m)`
    (`update-ref` in `origin.git` to `HEAD~1`), `DeleteOriginBranch(m)`, `AdvanceOriginFromSide(m)`
    (a commit pushed from `seed`, not fetched), `Decoy(m)` (`refs/heads/x/refs/heads/<b>` at the mirror
    HEAD in origin), `StaleRegistration(id8)` (`worktree add` then delete the directory, leaving a prunable
    registration), `Engine(...)` (S-2, `deviceOf` = `Device`), `Request(m, mode, taskId?, branch?, path?)`.
  - `Kept(response, reason, m, before)`: the common refusal assertion block, in order: (1) `Verdict ==
    "kept"` and `Reason == reason` exactly; (2) `Directory.Exists(m.Path)` and `Tree(m.Path)` equals
    `before`; (3) `Registered(m.Path)`; (4) `Retiring` has no entry; (5) `MutatingVerbs()` is empty.
- **F-2 `C1677RetentionDispatchTests.RetainRuntime`** (private, implements `IPhoneHomeRuntimeSurface` like
  `RecordingRuntime`): `CheckAndMove` and `Complete` each await a `TaskCompletionSource` gate and record
  start/finish counts; `SendInputAsync` optionally awaits its own gate.
- **F-3 native holders**: `C0824WorkspaceSlotProcessScanTests.Child`/`Stop`/`WaitForHolderAsync` are private;
  copy them into `C1677RetentionNativeProcessTests` (no shared-file edit).
- **F-4 `C1677RetentionTransportWorld`** (new `tests/Antiphon.Tests/Application/C1677RetentionTransportWorld.cs`,
  no `[Test]`): copied from `C0824SlotTransportWorld`; the runner side is a real `PhoneHomeCommandDispatcher`
  over a real `RunnerWorkspaceService` + `RunnerWorkspaceRetention` on a scratch bare origin, with the
  runtime adapter advertising `WorkspaceRetentionV1`. `Intercept` and `PhaseCut` pass-through as in C0824.
- **F-5 server seeds** in `C1677RetentionServerTests`: `EligibleAsync(rig, completedAgo = 24 h, id8 = null)`
  seeds `Succeeded`, `Workspace = Worktree`, `RunnerId = rig.RunnerId`, `RemoteWorktreePath =
  /work/worktrees/task-<id8>`, `WorktreeBranch = feat/card-task-<id8>`, `Result = "done"`, `CompletedAt =
  now - completedAgo`. Every V-30 case seeds **one control task** with `EligibleAsync` beside the case's
  ineligible task, so a case can never pass because nothing is sent (the CARD-1653 S1 PC-26 lesson).
- **F-6 capability**: every server rig is `Rig.StartAsync(..., capabilities: RetentionRunner())` with
  `Features: [RunnerCapabilityFeatures.WorkspaceRetentionV1]`, except V-31c's `"no-capability"` case.
- **F-7 policy rows** in `tests/test-execution-policy.json`, one per Linux-only class, in that slice's
  red commit, with `policyHash` resealed from the `NativeLinuxPolicyWitnessTests` output (memory recipe:
  `platform: windows` plus a Linux-only reason):
  - S1: `{ "suite": "session-runner", "class": "C1677RetentionNativeProcessTests", "platform": "windows",
    "reason": "Linux-only by construction: the retention holder scan reads /proc/*/cwd and /proc/*/fd of real children and an unreadable proc root. The case remains required on Linux.", "owner": "CARD-1677" }`
  - S2: the same shape for `C1677TempSweepNativeTests`, reason `Linux-only by construction: a real symlinked /tmp entry and a real /proc cwd holder. The case remains required on Linux.`
  Both classes carry `[NativeLinuxOnly("CARD-1677 Linux-only by construction: …")]` on **every** method
  (the C0824 pattern). No `linux-test-roster.json` change: that roster is frozen at CARD-0590 and lists no
  C0824 or Card1653 class either.
- **F-8 class attributes**: every new SessionRunner class `[Category("Integration")]`,
  `[ParallelLimiter<ProcessSpawnLimit>]` (git and child processes; SessionRunner.Tests has no git lane);
  every new Antiphon.Tests class `[Category("Integration")]`. Timeouts are in §Performance budget.

### Delivery inventory

| Path | Producer | Destination | Persistence boundary | Recovery | Observable receipt (recipient) | Durable identity |
|---|---|---|---|---|---|---|
| D-1 `WorkspaceRetain` | `RunnerWorktreeRetentionService` tick | runner dispatcher → engine | phase A: the `git worktree move` (the moved tree and its registration in `RetiringRoot`); phase B: the tree's absence; server: `WorktreeResidueRunCandidate` row and `AgentTask.RemoteWorktreePath` | reply lost → `unknown`, path kept, next tick resends and the runner answers `already_absent` (S-5) or retries phase B; runner crash between phases → `retiring_residue` inventory row, phase B retried on the next request for that task | the tree absent, its registration gone, `RemoteWorktreePath` null, a `Removed` row and one Info event (V-39) | `TaskId` + `<leaf>` + request id (`<leaf>.<request8>`) |
| D-2 `WorkspaceRetentionInventory` | same tick | runner (read-only, outside the lock) | `RunnerInventory` rows | the next tick repeats it; `ShouldWriteRow` dedupe | rows per unmatched name (V-35) | runner id + entry name |
| D-3 `TempSweep` (S2) | same tick | runner (outside the lock) | `RunnerTemp` rows; the entry's absence | the next tick re-inventories; a partial no-follow delete leaves a smaller entry that is re-measured | rows and the entry absent (V-40, V-51b) | runner id + entry name |
| D-4 `WorkspaceDiskLow` (S3) | `HostStatsCache` sample | attention feed read | none new (computed per read) | the next poll | the attention item (V-60) | `workspace-disk-low:<runnerId>` |

Recipient evidence: V-39 drives D-1 producer → real `PhoneHomeRunnerClient` → socket → real runner
dispatcher → real engine → real Git, and reads the recipient's filesystem and Git registration. V-39b is
the busy recipient (one request outstanding, ticks send no second), V-39c the recovery at each handoff
(reply lost after removal; runner crash between phase A and phase B). "One already eligible" is the V-30
control task in every case. Substitutes, declared:

- **Substitute 1:** V-14/V-14b/V-14c witness the lock split with a fake runtime (F-2), not the real engine.
  They prove the dispatcher's sequencing; they cannot prove that a real `worktree remove` of a large tree
  is slow (measured at enabling, plan §8 item 6).
- **Substitute 2:** S2 and S3 have no transport round trip beyond the scripted peer (V-51) and the cache
  (V-60). The temp sweep's recipient evidence is the entry's absence in the runner-side class (V-40).
- No session input is delivered by this card, so no UserPrompt transcript applies.

### Proves it works now

Common names: `m` is a `Mirror()`; `before = Tree(m.Path)`; "kept(r)" is F-1's `Kept` block with reason
`r`. All runner cases run in `Execute` unless named `DryRun`.

**Class `C1677RetentionEngineTests`** (portable, F-1; S1).

- **V-1** `Clean_pushed_old_mirror_is_removed_without_force_or_prune`: `StaleRegistration("a16770ff")`
  first. Execute → `Verdict == "removed"`; `m.Path` absent; not `Registered`; `Retiring` empty; argv has
  exactly one `worktree move <m.Path> <Retiring>/task-a1677001.<request8>` (cwd = runner repository) and
  exactly one `worktree remove <moved>` whose args contain neither `--force` nor `-f`; no `prune`,
  `fetch`, `branch`, `rm`; `worktree list --porcelain` **still lists** `task-a16770ff` (no prune,
  behaviourally); `HeadSha == m.Head`, `OriginTip == m.Head`, `PhaseAMs >= 0`, `PhaseBMs >= 0`; the
  ls-remote argv is `ls-remote --refs -- <origin.git path> refs/heads/feat/card-task-a1677001` (S-1).
- **V-9** `Confinement_refuses_outside_scope(string shape)`, 4 cases, each kept(`outside_scope`) and every
  other fact eligible (clean, pushed, branch matches, no holder):
  - `"outside-root"`: a real clean pushed worktree at `<Work>/elsewhere/task-a1677001` (registered in the
    runner repository);
  - `"nested-under-root"`: the same at `<Worktrees>/sub/task-a1677001`;
  - `"symlinked-leaf"`: `<Worktrees>/task-a1677001` is a symlink to a real worktree at `<Work>/real/task-a1677001`
    (assert the target's tree and registration, and that the link still exists);
  - `"leaf-id-mismatch"`: the mirror is `task-b1677002` on branch `feat/card-task-b1677002`, request
    `TaskId` = `a1677001…`, `Branch` = `feat/card-task-b1677002`.
- **V-10** `Slot_paths_are_refused(string shape)`, 2 cases, kept(`slot_managed`):
  - `"ws-leaf"`: a clean pushed worktree at `<Worktrees>/ws-03` on `feat/card-task-a1677001`;
  - `"ledger-path"`: `m` itself, after `new RunnerWorkspaceSlotLedger(pool.LedgerRoot).Write(...)` records
    a slot whose `Path == m.Path` (the only case where the ledger is the sole refusal, M-2).
- **V-11** `Ownership_gates_refuse(string shape)`, 3 cases:
  - `"use-owner"`: a `RunnerWorkspaceUseGate` installed (`WorkspaceUseGate`), census completed, and
    `RecordOwner(m.Path, Guid.NewGuid(), () => true)` → kept(`workspace_in_use`);
  - `"removal-journal"`: the gate's journal holds a record whose binding path is `m.Path` → kept(`workspace_retiring_or_unknown`);
  - `"locked"`: `git worktree lock m.Path` → kept(`locked`) (the argv may contain no `lock` from the engine).
- **V-24** `Repository_and_branch_must_match(string shape)`, 4 cases:
  - `"foreign-repository"`: a second clone at `<root>/foreign` (outside `RepositoriesRoot`) adds a clean,
    pushed worktree at `<Worktrees>/task-a1677001` → kept(`not_runner_repository`), the foreign registration intact;
  - `"other-branch"`: `m` checked out on `feat/card-task-a1677001-x` (pushed, same commit) while the request
    names `feat/card-task-a1677001` → kept(`branch_mismatch`);
  - `"branch-id-not-leaf"`: `m` on `feat/card-task-b1677002` (pushed, same commit), request `Branch =
    feat/card-task-b1677002` → kept(`branch_mismatch`);
  - `"detached"`: `git checkout --detach` at `m.Head` → kept(`branch_mismatch`).
- **V-2** `Dirty_tree_is_kept(string shape)`, 4 cases:
  - `"tracked"`: `a` modified → kept(`dirty`) (V-2);
  - `"untracked-nested-file"`: `d/f` in an untracked directory → kept(`dirty`) (V-3);
  - `"non-ignored-nested-repo"`: a committed repo at `seedrepo/` → kept(`dirty`) (V-16b);
  - `"status-unavailable"`: `GitFault.In(m.Path, "status", "boom", remaining: 1)` → kept(`status_unavailable`).
  Every case also asserts the status argv is `status --porcelain --untracked-files=all --ignore-submodules=none`.
- **V-4** `Unpushed_commit_is_kept(string shape)`, 2 cases:
  - `"local-commit"`: one commit in the mirror, not pushed → kept(`unpushed`);
  - `"pushed-by-url-tracking-stale"`: `PushByUrl(m)` → live origin's tip equals HEAD, so only
    `rev-list HEAD --not --remotes=origin` is non-zero → kept(`unpushed`) (V-4b, M-5).
- **V-5** `Live_origin_must_hold_the_branch(string shape)`, 5 cases, each with a fresh tracking ref that
  contains HEAD (so `rev-list` is 0):
  - `"deleted-on-origin"`: `DeleteOriginBranch(m)` → kept(`branch_absent_on_origin`);
  - `"decoy-tail-match"`: `DeleteOriginBranch(m)` then `Decoy(m)` (the only ls-remote line is the decoy,
    at HEAD) → kept(`branch_absent_on_origin`) (V-5c, G-17);
  - `"origin-unavailable"`: `GitFault.In(m.Path, "ls-remote", "fatal", exitCode: 128)` → kept(`origin_unavailable`) (V-5b);
  - `"origin-reset-behind"`: `ResetOriginBehind(m)` while `refs/remotes/origin/<b>` still equals HEAD →
    kept(`origin_does_not_contain_head`) (V-6);
  - `"origin-advanced-unknown-tip"`: `AdvanceOriginFromSide(m)` (the tip is a descendant of HEAD that
    the runner never fetched) → kept(`origin_tip_unknown`) (V-6b).
  The argv has no `fetch` in any case.
- **V-25** `Ls_remote_is_bounded`: `LsRemoteBudget = 1 s`, `GitFault.Sleep(m.Path, "ls-remote", 8)` →
  kept(`origin_unavailable`) and the call returns in **< 5 s** (stopwatch).
- **V-7** `Holder_keeps_the_tree(string shape)`, 2 cases:
  - `"holder"`: `Holders` returns `Complete` with `(4242, "sleep", "cwd")` → kept(`live_process`) and
    `Holders == [(4242, "sleep", "cwd")]` in the response (V-7);
  - `"scan-unknown"`: `Holders` returns `Complete = false` → kept(`holder_scan_unknown`) (V-8).
- **V-12** `Dry_run_reports_would_remove_and_mutates_nothing`: DryRun → `Verdict == "would_remove"`, the
  facts set (`HeadSha`, `OriginTip`), `Tree(m.Path) == before`, `Registered`, `Retiring` empty,
  `MutatingVerbs()` empty, `Holders.Calls == 1`.
- **V-26** `Execute_rechecks_under_the_lock_before_the_move(string change)`, 2 cases; `PhaseCut("locked")`
  makes the change after the dry pass passed:
  - `"holder-appears"`: the fake scan starts returning a holder → kept(`live_process`), no `worktree move`;
  - `"file-appears"`: an untracked file is planted → kept(`dirty`), no `worktree move`.
  `Holders.Calls == 2` in the first case (the recheck ran).
- **V-13** `Phase_b_refusal_keeps_the_moved_tree_registered`: `PhaseCut("moved")` plants an untracked file
  in the moved tree → `Verdict == "retiring_refused"`, the stderr tail contains `contains modified or
  untracked files`; the moved tree exists in `Retiring` and is registered there; `m.Path` absent; exactly
  one `worktree remove` argv, without `--force`/`-f`; no `rm`, no `prune`.
- **V-27** `Move_failure_leaves_the_tree_in_place`: `GitFault.In(<runner repository>, "worktree", "fatal: move",
  exitCode: 128)` matching only `worktree move` → `Verdict == "kept"`, `Reason == "retiring_move_failed"`;
  `Tree(m.Path) == before`; `Registered(m.Path)`; `Retiring` empty; **no** `worktree remove` argv.
- **V-28** `Retiring_residue_is_retried_by_phase_b_only`: run V-13's refusal, delete the planted file, also
  leave a retiring residue `task-c1677003.<8hex>` of another task (moved by hand with `worktree move`), then
  resend `WorkspaceRetain` for `m` → `removed`, `RetiringResidue == true`; argv of the second request has
  **no** `worktree move` and one `worktree remove` of the first moved path; the other task's residue still
  exists and is registered.
- **V-15** `Cross_device_retiring_root_moves_nothing`: `Device` returns 1 for the worktree root and 2 for
  `Retiring` → kept(`retiring_root_cross_device`) (F-1 block).
- **V-29** `Retiring_root_inside_the_worktree_root_is_refused`: the engine built with
  `retiringRoot = <Worktrees>/retiring` → kept(`retiring_root_invalid`) (F-1 block, the root unchanged).
- **V-16** `Ignored_nested_repo_goes_with_the_tree`: a committed repo at `.antiphon/seed/` → `removed`,
  `NestedIgnoredRepos == 1`.
- **V-17** `Absent_path_is_already_absent_without_prune`: `StaleRegistration("a16770ff")`, request for the
  absent `task-a1677001` → `Verdict == "already_absent"`, `RetiringResidue == false`; no `prune` argv;
  `task-a16770ff` still listed.

Count: 1 + 4 + 2 + 3 + 4 + 4 + 2 + 5 + 1 + 2 + 1 + 2 + 1 + 1 + 1 + 1 + 1 + 1 + 1 = **38 executions**.

**Class `C1677RetentionInventoryTests`** (portable, F-1; S1).

- **V-18** `Inventory_lists_root_and_retiring_entries`: `task-cffa0c4b-uA` (a registered detached
  worktree), `ws-01` (a plain directory), a symlink `task-dddddddd` → `/tmp`, a mirror, and one retiring
  residue → entries with `registered`/`slotLeaf`/`isSymlink`/`isDirectory` exact, the residue listed with
  root `retiring`.
- **V-18b** `Inventory_is_read_only`: with `StaleRegistration("a16770ff")` present → the only argv is
  `worktree list --porcelain`; every `Tree` unchanged; `task-a16770ff` still listed.
- **V-19** `Unmatched_name_is_refused_in_execute`: `WorkspaceRetain` for `<Worktrees>/task-cffa0c4b-uA`,
  `TaskId` `cffa0c4b-…` → kept(`outside_scope`).

Count: **3**.

**Class `C1677RetentionDispatchTests`** (portable, F-2; S1). A `Dispatcher(runtime)` as in
`PhoneHomeCommandDispatcherTests`.

- **V-14** `Phase_b_runs_outside_the_mutation_lock`: `CheckAndMove` returns `Moved`; `Complete` is held at
  its gate. While held, an `Input` frame for a fake session completes within **2 s** with `ok`; then the
  gate opens and the `WorkspaceRetain` frame completes `removed`.
- **V-14b** `Phase_a_waits_for_the_mutation_lock`: an `Input` held at its gate; a `WorkspaceRetain` frame
  sent; after 500 ms `CheckAndMove` has **not started**; the Input gate opens; then `CheckAndMove` starts
  and the frame completes.
- **V-14c** `Phase_b_runs_only_after_a_move`: `CheckAndMove` returns a final `kept(dirty)` → the frame's
  result is `kept`/`dirty` and `Complete` was called **0** times.
- **V-14d** `Capability_is_advertised_only_on_linux(bool isLinux)`, 2 cases, through the pure
  `PhoneHomeRuntimeAdapter.RetentionFeatures(isLinux)`: `true` → contains `WorkspaceRetentionV1`; `false`
  → does not.

Count: 1 + 1 + 1 + 2 = **5**.

**Class `C1677RetentionNativeProcessTests`** (`[NativeLinuxOnly]`, real `/proc`, F-1 + F-3; S1).

- **V-20** `Cwd_holder_keeps_the_tree`: `Child("sleep", ["30"], m.Path)` → kept(`live_process`), the
  response holder has the child pid, `Argv0` ending in `sleep`, `Kind == "cwd"`.
- **V-21** `Open_fd_holder_keeps_the_tree`: `Plant(m.Path, "held.log")`, `Child("tail", ["-f", m.Path +
  "/held.log"], scratch.Root)`, `WaitForHolderAsync` → kept(`live_process`), `Kind == "fd"`.
- **V-22** `Tree_is_removed_after_the_holder_exits`: V-20's child stopped and reaped → `removed`.
- **V-23** `Unreadable_proc_root_is_unknown`: `new ProcWorkspaceSlotProcessProbe(procRoot: <root>/no-proc)`
  → `Scan(m.Path).Complete == false`; the same probe with `/proc` → `Complete == true`, no holders; the
  slot path `Holders(m.Path)` on the unreadable root still returns `[]` (unchanged fail-open, S-3).

Count: **4**.

**Class `C1677RetentionServerTests`** (shared Postgres, `Rig`, F-5/F-6; S1).

- **V-30** `Each_ineligible_task_alone_is_recorded_and_never_sent(string mode)`, 23 cases. Each seeds the
  control task and one task that differs from `EligibleAsync` in exactly the named fact; one tick; then:
  exactly **one** `WorkspaceRetain` request, whose `TaskId` is the control's; a run candidate row for the
  ineligible task with Lane `RunnerMirror`, Outcome `Held`, `ReasonCode` exact; its `RemoteWorktreePath`
  unchanged. Modes and expected reason:
  - `queued`, `dispatched`, `working`, `blocked`: that status **with `CompletedAt = now - 24 h`** (a
    reopened task keeps its old `CompletedAt`, M-8) → `live_owner`;
  - `settling`: `Succeeded`, `CompletedAt = now - 11 h 59 min` → `settling`;
  - `land-pending`: `rig.SeedLandingAsync(id)` in a non-terminal state → `land_pending`;
  - `sync-debt-pending`, `sync-debt-ready`, `sync-debt-held`: an `AgentTaskSyncDebts` row in that state → `sync_debt`;
  - `slot-bound`: `RemoteWorkspaceSlotId = "ws-02"` with the `task-<id8>` path kept (M-9) → `slot_managed`;
  - `source-landing`: `SourceLandingOperationId` set, `Role = Code` (M-10) → `outside_scope`;
  - `repair-source`: `RepairSourceTaskId` set, `Role = Code` → `outside_scope`;
  - `mutation`: `Role = Mutation`, no SourceLanding id → `mutation_excluded`;
  - `path-shared`: a **second, open** (`Working`) task whose `RemoteWorktreePath` equals this task's path
    (M-12) → `path_shared`;
  - `session-created`, `session-starting`, `session-running`, `session-stopping`: an `AgentSessions` row
    for the task in that status (M-11) → `live_session`;
  - `report-unpreserved`: `Result = null`, `ResultFilePath = <its path>/.antiphon/task-<id8>.md` → `report_unpreserved`;
  - `leaf-mismatch`: `RemoteWorktreePath = /work/worktrees/task-ffffffff` → `outside_scope`;
  - `not-direct-child`: `RemoteWorktreePath = /work/worktrees/x/task-<id8>` → `outside_scope`;
  - `other-runner`: `RunnerId = "server2-temp"` → no row for this runner's run (the row is in neither
    scope), and no request carrying its id;
  - `branch-null`: `WorktreeBranch = null` → `outside_scope`.
- **V-30b** `Terminal_statuses_at_the_age_floor_are_sent(string status)`, 3 cases (`Succeeded`, `Failed`,
  `Canceled`, `CompletedAt` exactly `now - 12 h`) → one request for that task (no-false-refusal control).
- **V-31** `Disabled_job_contacts_no_runner`: `Enabled = false`, an eligible task → `RunTickAsync` sends
  **no** frame of any retention operation (inventory included); a recording `IWorktreeManager` registered
  in the rig saw 0 calls.
- **V-31b** `Execute_off_sends_dry_run_only`: `Enabled = true`, `Execute = false` → one request with
  `Mode == DryRun`; reply `would_remove` → row `would_remove`, `RemoteWorktreePath` unchanged.
- **V-31c** `Only_listed_capable_phone_home_runners_are_contacted(string shape)`, 3 cases: `"unlisted"`
  (`Runners = []`), `"not-phone-home"` (`Runners = ["desktop"]`, an eligible task with `RunnerId =
  "desktop"`), `"no-capability"` (rig without `WorkspaceRetentionV1`) → 0 requests; the first two record
  a run row `runner_not_eligible`.
- **V-32** `Execute_requests_are_bounded_per_tick`: 5 eligible tasks with `CompletedAt` 20..24 h ago,
  `MaxRemovalsPerTick = 2`; each request answered `removed` and the clock advanced past the pause → exactly
  **2** requests, the oldest two in order.
- **V-32b** `Second_request_waits_for_the_first_reply`: 2 eligible, `Peer.SilentFor(WorkspaceRetain)` →
  after 2 s of real time, `RequestCount == 1`.
- **V-32c** `Requests_are_paced`: 2 eligible, `PauseBetweenSeconds = 5`; the first answered → for 1 s of
  real time with the clock unchanged, `RequestCount == 1`; `Clock.Advance(5 s)` → `RequestCount == 2`.
- **V-33** `Reopened_task_is_rechecked_before_its_turn`: 2 eligible; when the first request arrives the test
  sets the second task to `Queued`, then answers → `RequestCount == 1` for the tick, the second has a
  `live_owner` row.
- **V-34** `Outcome_recording(string outcome)`, 4 cases:
  - `"removed"` → `RemoteWorktreePath == null`, one Info event naming the path, a `Removed` row with
    `PhaseAMs`/`PhaseBMs`; the recording `IWorktreeManager` saw 0 calls (I-12);
  - `"kept"` (reply `kept`/`dirty`) → a `Held`/`dirty` row, path unchanged, no event;
  - `"lost"` (the peer answers an `Error` frame `transport_lost`) → an `unknown` row, path unchanged;
  - `"already-absent"` (reply `already_absent`, `RetiringResidue = false`) → path null, a `Removed` row
    with reason `already_absent`.
- **V-35** `Unmatched_inventory_names_are_reported_not_sent`: inventory reply lists `task-cffa0c4b-uA`
  (a seeded task `cffa0c4b…` is `Succeeded`) and `task-eeeeeeee` with two seeded tasks whose ids start
  `eeeeeeee` → `RunnerInventory` rows `identity_unknown` (owner hint `task-cffa0c4b`, `Succeeded`) and
  `identity_ambiguous`; **no** `WorkspaceRetain` request for either.
- **V-39** `Retain_round_trip_real_runner` (F-4): one eligible task whose mirror exists in the scratch
  repository, `Execute = true` → the tree absent, not registered, `RemoteWorktreePath` null, a `Removed` row.
- **V-39b** `Busy_runner_retain_is_serialised` (F-4): `Peer.SilentFor(WorkspaceRetain)`, two more ticks
  while the first request is outstanding (the job's one-at-a-time loop) → `RequestCount == 1`; delivered →
  removed once.
- **V-39c** `Retain_recovery_at_each_handoff(string handoff)` (F-4), 2 cases:
  - `"reply-lost"`: `Intercept` delivers to the runner and returns an empty result → `unknown`, path kept;
    the next tick → `already_absent`, path null; argv on the runner has exactly one `worktree remove`;
  - `"crash-between-phases"`: the runner engine's `PhaseCut("moved")` throws once → the tick records
    `unknown`; the next tick's inventory row is `retiring_residue` and its request retries phase B →
    `removed`, one `worktree move` in total.

Count: 23 + 3 + 1 + 1 + 3 + 1 + 1 + 1 + 1 + 4 + 1 + 1 + 1 + 2 = **44 executions**.

**Class `C1677RetentionSettingsTests`** (no DB; S1).

- **V-36** `Shipped_defaults_are_on_for_server2_with_execute` (renamed from `Shipped_defaults_are_off_and_dry_run`:
  operator decision 2026-11-03, "Enable this on by default", plan D-15): from `new RunnerWorktreeRetentionSettings()`
  **and** `server/appsettings.json` read from the repository root (`TestPaths.RepoRoot`, not the cwd): `Enabled`
  true, `Execute` true, `Runners` empty in the type and exactly `["server2"]` in the JSON and when bound,
  `MaxTickMinutes` 20, `KeptRecheckMinutes` 60, `KeptRecheckMaxHours` 24, `MinTerminalHours` 12, `TempSweep.Enabled` false,
  `TempSweep.Execute` false, `TempSweep.Families["claude-scratch"]` false.
- **V-36b** `Validator_bounds(string field, string value, bool valid)`, 12 cases (`MaxTickMinutes` 0 ✗ / 20 ✓ and
  `KeptRecheckMinutes` 0 ✗ / 1 ✓ added in round 1): `MinTerminalHours` 0 ✗ / 1 ✓;
  `MaxRemovalsPerTick` 0 ✗ / 1 ✓ / 100 ✓ / 101 ✗; `RetiringRoot` `work/r` ✗ / `/work/worktrees-retiring` ✓.
- **V-38** `Job_is_not_concurrent`: `RunnerWorktreeRetentionJob.RunAsync` carries `DisableConcurrentExecution`.

Count: 1 + 8 + 1 = **10**.

**Class `C1677TempSweepTests`** (portable, injected temp root under `<scratch>/tmp`, S-6; S2). Ages through
`FakeTimeProvider` and `File.SetLastWriteTimeUtc`; `ownerUid` and `holders` are fakes.

- **V-40** `Old_registered_entry_is_swept(bool execute)`, 2 cases: an old `c487-probe-<32hex>` (3 files,
  newest 49 h) → `removed` and absent (`true`) or `would_remove` and intact (`false`).
- **V-41** `Unregistered_name_is_reported_and_kept`: `g104-<32hex>` (old) → absent from candidates,
  `unregistered` count 1 with its bytes, intact.
- **V-42** `Newest_file_write_decides_age(string shape)`, 2 cases, each kept(`too_young`): `"new-top-file"`
  (directory mtime 49 h, one top file written 1 h ago); `"new-nested-file"` (top files 49 h, `a/b/c.txt`
  written 1 h ago).
- **V-43** `Holder_keeps_the_entry(string shape)`, 2 cases: `"holder"` → `live_process`; `"scan-unknown"` →
  `holder_scan_unknown`.
- **V-44** `Denied_names_are_kept_under_a_broad_family(string name)`, 8 cases, with the injected family
  `^.*$` (M-14) and every other fact eligible: `antiphon-pty-hosts`, `antiphon-pty-<32hex>`,
  `CoreFxPipe_x`, `dotnet-diagnostic-1-2-socket`, `.X11-unix`, `systemd-private-x`, `node-compile-cache`,
  `.codex` (the `CleanupFamilyRegistry.IsDenied` list) → `denied`, intact.
- **V-45** `Foreign_owned_descendant_keeps_the_entry`: `ownerUid` reports uid+1 for one nested file →
  `foreign_owner`, intact.
- **V-46** `Per_tick_bounds(string bound)`, 2 cases: `"entries"` (`MaxEntries = 2`, 5 eligible) → exactly 2
  removed, the oldest; `"bytes"` (`MaxBytes` = 1.5 × one entry, 3 equal entries) → exactly 1 removed.
- **V-46b** `Inventory_limit_keeps_the_entry`: `InventoryEntries = 10`, an entry with 11 files → `inventory_limit`, intact.
- **V-47** `Claude_scratch_needs_flag_and_no_mirror(string shape)`, 4 cases on
  `claude-1654/-work-worktrees-task-a1677001`: `"flag-off"` → `family_disabled`; `"mirror-present"`
  (`mirrorPresent` true for the task path) → `mirror_present`; `"retiring-present"` (true for a retiring
  entry of the leaf) → `mirror_present`; `"eligible"` → `removed`, and `claude-1654` itself still exists.
- **V-47b** `Only_top_level_entries_are_considered`: `x-unregistered/c487-probe-<32hex>` (old) → intact,
  and `c487-probe` is not reported for it.

Count: 2 + 1 + 2 + 2 + 8 + 1 + 2 + 1 + 4 + 1 = **24**.

**Class `C1677TempFamilyRegistryTests`** (portable, source read from the repository root; S2).

- **V-48** `Every_family_producer_still_contains_its_prefix`: for each `RegisteredFamilies` row, its
  producer file contains its prefix literal (`c487-` in `scripts/lib/c487-harness.ps1`).
- **V-48b** `Pty_test_root_alternation_equals_known_prefixes`: the `pty-test-root` regex alternation, split,
  equals `TestSessionLogRoot.KnownPrefixes` as a set (from the SessionRunner.Tests assembly itself).
- **V-48c** `Family_regexes_are_anchored_and_exact`: every regex starts `^`, ends `$`, contains no `.*`,
  `.+` or `\S`, and matches its measured sample name.

Count: **3**.

**Class `C1677TempSweepNativeTests`** (`[NativeLinuxOnly]`; S2).

- **V-49** `Symlinked_entry_is_refused_and_its_target_kept`: `<tmp>/c487-probe-<32hex>` is a symlink to an
  old directory outside the temp root → `symlink`; the link and the target's files intact.
- **V-49b** `Inner_symlink_is_not_followed`: an old family directory holds a symlink to an outside directory
  with an old file → `removed`; the outside file still exists.
- **V-50** `Real_cwd_holder_keeps_the_entry`: a `sleep` child with cwd in an old family entry, the real
  `ProcWorkspaceSlotProcessProbe` → `live_process`, intact.

Count: **3**.

**Class `C1677TempSweepServerTests`** (Rig; S2).

- **V-51** `Disabled_sweep_sends_nothing`: `TempSweep.Enabled = false` → 0 `TempSweep` frames.
- **V-51b** `Enabled_sweep_defaults_to_dry_run_and_records_rows`: `TempSweep.Enabled = true` → one frame
  with `Mode == DryRun`; reply rows become Lane `RunnerTemp` rows.
- **V-51c** `Claude_scratch_is_sent_off_by_default`: the frame's `Families["claude-scratch"] == false`.

Count: **3**.

**Class `C1677DiskAlarmTests`** (isolated schema, the `RunnerAlarmAttentionTests` shape; S3). Samples via
`HostStatsCache.Record("server2", dto)` with `Disks = [("/work", free, total), ("/state", …)]`.

- **V-60** `Work_at_85_percent_raises_one_warning`: total 100 GiB, free 15 GiB → one item, Warning,
  `ConditionKey == "workspace-disk-low:server2"`.
- **V-61** `Work_below_85_percent_raises_nothing`: total 1000 GiB, free 151 GiB (84.9 %).
- **V-62** `Only_the_configured_volume_counts`: `/state` 95 % and `/work` 50 % → none; and `/work` 90 %
  listed **second** → one item.
- **V-63** `Stale_sample_raises_nothing`: sample 16 min old (`SampleFreshMinutes = 15`) → none.
- **V-64** `Unlisted_runner_raises_nothing`: `desktop` at 99 % → none.
- **V-65** `Evidence_names_bytes_and_retention_mode`: evidence contains `freeBytes=`, `totalBytes=`,
  `usedPercent=85`, `retention=Off`.
- **V-66** `Row_clears_below_the_threshold`: 90 % then 50 % → the second read has no item.
- **V-67** `Used_percent_bounds(int value, bool valid)`, 4 cases: 49 ✗, 50 ✓, 99 ✓, 100 ✗.

Count: 7 + 4 = **11**.

### Guards the regression

- **R-1** `RemoveAsync` and the mirror path are unchanged: `RunnerWorkspaceServiceTests` (CP-3, 35 measured).
- **R-2** the Windows suites stay sealed with the new Linux-only classes: `NativeLinuxPolicyWitnessTests`
  (CP-4 after S1, CP-13 after S2; 3 measured).
- **R-3** the operation enumeration: `PhoneHomeConnectionTests/Supported_operations_preserve_contracts`
  (CP-6, 1 measured).
- **R-4** the wire value `WorkspaceDiskLow = 65`: `AttentionKindWireTests` (CP-10, 4 measured).
- **R-5** the client visuals list: `attentionVisuals.test.ts` (CP-11).
- **R-6** the slot pool's fail-open `Holders()` is unchanged by S-3: `C0824WorkspaceSlotProcessScanTests`
  (CP-12, 3 measured).

### Masking and reachability (checked against the code and the plan's pseudocode)

Every refusal witness violates **one** condition and asserts the exact reason, the original path and the
registration. Where another check (or Git itself) would still refuse after a mutant removes the first, the
mutant still goes red on the exact reason; the cases below are those where survival alone would hide it,
and the unit witness that isolates each.

- **M-1** The leaf grammar `^task-[0-9a-f]{8}$` is **subsumed** by the leaf-equals-task-id check: any leaf
  equal to `task-` + 8 hex matches the grammar. Removing the grammar alone is equivalent and is not listed
  as a guard. The plan's PC-12 claim that V-19 goes red is **wrong**: under PC-12 the grammar still refuses
  `task-cffa0c4b-uA`. The equality's own witness is V-9 `"leaf-id-mismatch"` (grammar passes).
- **M-2** `IsSlotLeaf` and the grammar mask each other for a `ws-*` leaf (the grammar refuses `ws-03` as
  `outside_scope`). The slot-leaf check is therefore witnessed by reason only (V-10 `"ws-leaf"`, which
  requires the slot check to run first). `SlotByPath` is witnessed on survival by V-10 `"ledger-path"`,
  where the ledger names a `task-<id8>` path and nothing else refuses. Production ledgers hold only
  `ws-*` paths, so this guard is defence in depth.
- **M-3** `locked` is masked on survival by Git: `worktree move` refuses a locked tree (measured). Reason
  witness only (V-11 `"locked"`; the mutant gives `retiring_move_failed`).
- **M-4** The status check (`dirty`, `--untracked-files=all`) is masked for **data** by phase B: `worktree
  remove` without force refuses a dirty or untracked tree, so the work would survive in the retiring root.
  The decisive assertion is "the tree is at its original path and nothing moved" (F-1 block 2 and 4).
- **M-5** `unpushed` (rev-list) and live-origin containment mask each other on V-4 `"local-commit"`: each
  alone still refuses. Unique witnesses: V-4 `"pushed-by-url-tracking-stale"` (only rev-list fails) and
  V-5 `"origin-reset-behind"` (only containment fails; rev-list is 0 through the stale tracking ref).
- **M-6** zero ls-remote lines treated as present still refuses later (`origin_tip_unknown` or
  `origin_does_not_contain_head`): reason witness (V-5 `"deleted-on-origin"`). The exact-name compare has a
  survival witness (V-5 `"decoy-tail-match"`).
- **M-7** the tip-exists check is masked by containment (`merge-base` exits 128 on an unknown object):
  reason witness (V-5 `"origin-advanced-unknown-tip"`).
- **M-8** a non-terminal task usually has `CompletedAt = null`, which also fails the age floor. Each status
  case seeds an old `CompletedAt`, so only the status set refuses. Code must treat a null `CompletedAt` as
  `settling` (that is the age floor, not the status set).
- **M-9** a slot task's path is `ws-NN`, which the leaf check also refuses: `"slot-bound"` keeps the
  `task-<id8>` path.
- **M-10** SourceLanding tasks are usually `Role = Mutation`: `"source-landing"` and `"repair-source"` use
  `Role = Code`; `"mutation"` has no SourceLanding id.
- **M-11** a live session normally implies a non-terminal task: `"session-*"` keep the task `Succeeded`.
- **M-12** `path_shared` needs a **different** open task; the candidate's own row never counts.
- **M-13** `RunnerWorkspaceUseGate` is not installed by any production composition at this tip (its
  header). G-7 is live only where it is installed; V-11 installs it.
- **M-14** no shipped temp family matches a denied name, so the deny list is unreachable through
  `RegisteredFamilies` (the plan's PC-23 against V-44 as written would stay green). V-44 injects `^.*$`.
- **M-15** the plan's PC-26 ("`>` 85 becomes `>=` 86 / threshold inverted") is two mutants; split into
  PC-118 (boundary) and PC-119 (below).
- **M-16** the plan's PC-8 claims V-1's argv; with `--force` on a clean tree V-1 still removes, so only the
  argv assertion catches it there. V-13 is the survival witness.
- **M-17** the holder probe skips its own pid: the test process is never a holder of its own fixture, so
  V-20/V-21 need a child process (F-3), as C0824 does.
- **M-18** V-14 and V-14b mask nothing between them: V-14's mutant (phase B inside the lock) leaves V-14b
  green, and V-14b's mutant (phase A outside the lock) leaves V-14 green; each has its own PC.

### Guard inventory

| G | Plan ref + guard | PC |
|---|---|---|
| G-1 | §3.3-1 the path is a direct child of `WorktreeRoot` | PC-1 |
| G-2 | §3.3-1 the leaf resolves without symlinks (`TryResolveFinal`) | PC-2 |
| G-3 | §3.3-1 the leaf equals `task-` + the task id prefix | PC-3 |
| G-5 | §3.3-1 a `ws-*` slot leaf is refused (reason, M-2) | PC-5 |
| G-6 | §3.3-1 a ledger slot path is refused | PC-6 |
| G-7 | §3.3-2 the use gate (when installed) has no owner and no journal overlap | PC-7 |
| G-8 | §3.3-2 a locked worktree is refused (reason, M-3) | PC-8 |
| G-9 | §3.3-3 the owning repository is a runner repository | PC-9 |
| G-10 | §3.3-3 `symbolic-ref HEAD` equals `refs/heads/<Branch>` (detached refused) | PC-10 |
| G-11 | §3.3-3 the branch's short id equals the leaf id | PC-11 |
| G-12 | I-2 status is empty (tracked) | PC-12 |
| G-13 | I-2 untracked files are counted (`--untracked-files=all`, not `no`) | PC-13 |
| G-14 | §3.3-4 a failed status refuses | PC-14 |
| G-15 | I-3 `rev-list HEAD --not --remotes=origin` is 0 | PC-15 |
| G-16 | I-4 zero ls-remote lines refuse (reason, M-6) | PC-16 |
| G-17 | I-4 the ls-remote line names exactly `refs/heads/<Branch>` | PC-17 |
| G-18 | I-4 an ls-remote failure refuses (no fallback) | PC-18 |
| G-19 | I-4 the tip is read from live origin, not the tracking ref | PC-19 |
| G-20 | I-4 the live tip exists locally (reason, M-7) | PC-20 |
| G-21 | I-4 HEAD is an ancestor of the live tip | PC-21 |
| G-22 | §3.3-6 ls-remote is bounded | PC-22 |
| G-23 | S-1 the ls-remote endpoint is the runner-owned clone source | PC-23 |
| G-24 | I-6 a holder refuses | PC-24 |
| G-25 | I-6 an incomplete scan refuses | PC-25 |
| G-26 | D-7 the retention scan reports an unreadable `/proc` as incomplete | PC-26 |
| G-27 | I-10 DryRun runs no Git mutation | PC-27 |
| G-28 | I-8 phase A rechecks under the lock immediately before the move | PC-28 |
| G-29 | §3.3-9 a cross-device retiring root moves nothing | PC-29 |
| G-30 | §3.3-9 the retiring root is never inside the worktree root | PC-30 |
| G-31 | brief: a move failure leaves the tree in place and skips phase B | PC-31 |
| G-32 | I-7 phase B is never forced | PC-32 |
| G-33 | I-7 no `prune` after a removal | PC-33 |
| G-34 | §3.3-10 a retiring residue is retried without a new move | PC-34 |
| G-35 | §3.3-10 the retry touches only this task's residue | PC-35 |
| G-36 | D-6 phase B runs outside `MutationLock` | PC-36 |
| G-37 | D-6 phase A runs under `MutationLock` | PC-37 |
| G-38 | D-6 phase B runs only after a move, on the moved path | PC-38 |
| G-39 | §3.3-1 an absent path is `already_absent` with no prune | PC-39 |
| G-40 | §3.4 the inventory is read-only | PC-40 |
| G-41 | D-11 the capability is advertised only on Linux | PC-41 |
| G-42 | I-1 `Queued` is not terminal | PC-42 |
| G-43 | I-1 `Dispatched` is not terminal | PC-43 |
| G-44 | I-1 `Working` is not terminal | PC-44 |
| G-45 | I-1 `Blocked` is not terminal | PC-45 |
| G-46 | I-1 the age floor `CompletedAt <= now - MinTerminalHours` | PC-46 |
| G-47 | §3.2 the server leaf equals `task-<id8>` | PC-47 |
| G-48 | §3.2 the server path is a direct child of the root | PC-48 |
| G-49 | §3.2 `WorktreeBranch` is set | PC-49 |
| G-50 | §3.2 `RunnerId` equals the runner | PC-50 |
| G-51 | I-11 a slot-bound task is refused | PC-51 |
| G-52 | I-11 a SourceLanding task is refused | PC-52 |
| G-53 | I-11 a repair-source task is refused | PC-53 |
| G-54 | I-11 a Mutation task is refused | PC-54 |
| G-55 | I-5 no land pending | PC-55 |
| G-56 | I-5 no `Pending` sync debt | PC-56 |
| G-57 | I-5 no `Ready` sync debt | PC-57 |
| G-58 | I-5 no `Held` sync debt | PC-58 |
| G-59 | I-5 no open task shares the path | PC-59 |
| G-60 | I-5 no `Created` session | PC-60 |
| G-61 | I-5 no `Starting` session | PC-61 |
| G-62 | I-5 no `Running` session | PC-62 |
| G-63 | I-5 no `Stopping` session | PC-63 |
| G-64 | D-9 a missing report holds the tree | PC-64 |
| G-65 | I-10 `Enabled=false` contacts no runner | PC-65 |
| G-66 | I-10 `Execute=false` sends DryRun | PC-66 |
| G-67 | D-11 only listed runners | PC-67 |
| G-68 | D-11 only phone-home runners (the desktop never) | PC-68 |
| G-69 | D-11 only runners advertising the capability | PC-69 |
| G-70 | I-9 at most `MaxRemovalsPerTick` Execute requests | PC-70 |
| G-71 | I-8 one request at a time | PC-71 |
| G-72 | I-9 `PauseBetweenSeconds` between requests | PC-72 |
| G-73 | I-8 the row is re-read before each send | PC-73 |
| G-74 | I-14 a kept tree keeps `RemoteWorktreePath` | PC-74 |
| G-75 | I-14 a lost reply records `unknown` and keeps the path | PC-75 |
| G-76 | §3.4 inventory names are never sent | PC-76 |
| G-77 | I-12 the desktop worktree manager is never called | PC-77 |
| G-78 | D-15 shipped `Enabled=true` (was I-10 `false`) | PC-78 |
| G-79 | D-15 shipped `Execute=true` (was I-10 `false`) | PC-79 |
| G-80 | D-15 shipped `Runners=["server2"]` in JSON, `[]` in the type (was I-10 `[]`) | PC-80 |
| G-81 | §5 validator `MinTerminalHours >= 1` | PC-81 |
| G-82 | §5 validator `MaxRemovalsPerTick <= 100` | PC-82 |
| G-83 | §5 validator refuses a relative `RetiringRoot` | PC-83 |
| G-84 | D-4 the job never overlaps (`DisableConcurrentExecution`) | PC-84 |
| G-85 | I-13 only registered families | PC-85 |
| G-86 | I-13 the newest file write decides age (not the directory mtime) | PC-86 |
| G-87 | I-13 the age walk descends | PC-87 |
| G-88 | I-13 a holder keeps the entry | PC-88 |
| G-89 | I-13 an incomplete scan keeps the entry | PC-89 |
| G-90 | §3.5 deny `antiphon-pty-hosts` | PC-90 |
| G-91 | §3.5 deny `antiphon-pty-*` | PC-91 |
| G-92 | §3.5 deny `CoreFxPipe_*` | PC-92 |
| G-93 | §3.5 deny `dotnet-diagnostic-*` | PC-93 |
| G-94 | §3.5 deny `.X*` | PC-94 |
| G-95 | §3.5 deny `systemd-*` | PC-95 |
| G-96 | §3.5 deny `node-compile-cache` | PC-96 |
| G-97 | §3.5 deny the `CleanupFamilyRegistry.IsDenied` components | PC-97 |
| G-98 | I-13 the runner uid owns every descendant | PC-98 |
| G-99 | I-13 a symlinked entry is refused | PC-99 |
| G-100 | I-13 descent never follows a link | PC-100 |
| G-101 | §3.5 at most `MaxEntriesPerTick` | PC-101 |
| G-102 | §3.5 at most `MaxBytesPerTick` | PC-102 |
| G-103 | §3.5 the inventory limit refuses | PC-103 |
| G-104 | D-12 `claude-scratch` needs its flag | PC-104 |
| G-105 | D-12 `claude-scratch` needs the mirror absent | PC-105 |
| G-106 | D-12 `claude-scratch` needs no retiring copy | PC-106 |
| G-107 | §3.5 top level only | PC-107 |
| G-108 | §3.5 each producer still contains its prefix | PC-108 |
| G-109 | §3.5 `pty-test-root` equals `KnownPrefixes` | PC-109 |
| G-110 | §3.5 family regexes are anchored and exact | PC-110 |
| G-111 | §3.5 `TempSweep.Enabled=false` sends nothing | PC-111 |
| G-112 | §3.5 the sweep defaults to DryRun | PC-112 |
| G-113 | D-12 `claude-scratch` is sent off by default | PC-113 |
| G-114 | §4 the Linux-only S1 class is pinned and excluded on Windows | PC-114 |
| G-115 | §4 the Linux-only S2 class is pinned and excluded on Windows | PC-115 |
| G-116 | §3.6 only the configured volume counts | PC-116 |
| G-117 | §3.6 a stale sample raises nothing | PC-117 |
| G-118 | §3.6 85.0 percent raises (boundary) | PC-118 |
| G-119 | §3.6 below 85 percent raises nothing | PC-119 |
| G-120 | D-11 an unlisted runner's disk is not alarmed | PC-120 |

G-4 (the leaf grammar alone) is equivalent (M-1) and is not listed. The S3 alarm deletes nothing, so its
evidence string, clearing, validator bounds, wire value and client visual (V-65..V-67, R-4, R-5) are
correctness rows without a positive control.

guards=119, mapped=119, missing=0, duplicate PC maps=0.

### Positive controls

Each PC breaks its guard with a compiling defect and expects the named method red at the named assertion.
Mutation runs break/red/restore/green after the land; Code runs the V/R rows; Review judges before the land.
Filters are method-scoped (`--treenode-filter "/*/<Assembly>/<Class>/<Method>"`); a method with `[Arguments]`
runs all its cases and the named case must go red. Zero tests, a build error or a fixture error is not red.
Classes: EN `C1677RetentionEngineTests`, IN `…InventoryTests`, DI `…DispatchTests`, NP `…NativeProcessTests`,
SV `…ServerTests`, ST `…SettingsTests`, TS `C1677TempSweepTests`, TF `…FamilyRegistryTests`, TN
`…TempSweepNativeTests`, TV `…TempSweepServerTests`, DA `C1677DiskAlarmTests`. Files: `Ret`
`RunnerWorkspaceRetention.cs`, `Seam` `WorkspaceSlotSeams.cs`, `Disp` `PhoneHomeCommandDispatcher.cs`, `Adp`
`PhoneHomeRuntimeAdapter.cs`, `Svc` `RunnerWorktreeRetentionService.cs`, `Set` the settings + validator,
`Job` `RunnerWorktreeRetentionJob.cs`, `json` `server/appsettings.json`, `Tmp` `RunnerTempSweep.cs`, `Al`
`AttentionService.Alarms.cs`, `pol` `tests/test-execution-policy.json` / the class attribute.

| PC | Break | Expect red | Reachability |
|---|---|---|---|
| PC-1 | `Ret`: drop the direct-child check | EN/`Confinement_refuses_outside_scope("outside-root")` at kept (removed) | every other fact eligible |
| PC-2 | `Ret`: use the lexical path instead of `TryResolveFinal` | same `("symlinked-leaf")` at the target registration/tree | the target is a real eligible worktree |
| PC-3 | `Ret`: drop leaf == `task-` + id8 | same `("leaf-id-mismatch")` at kept | grammar passes `task-b1677002` (M-1) |
| PC-5 | `Ret`: drop `IsSlotLeaf` | EN/`Slot_paths_are_refused("ws-leaf")` at `Reason == "slot_managed"` | reason only; the grammar keeps the tree (M-2) |
| PC-6 | `Ret`: drop `SlotByPath` | same `("ledger-path")` at kept | only the ledger refuses |
| PC-7 | `Ret`: skip `AcquireRemovalAsync` | EN/`Ownership_gates_refuse("use-owner")` and `("removal-journal")` at kept | gate installed (M-13) |
| PC-8 | `Ret`: drop the locked check | same `("locked")` at `Reason == "locked"` | reason only (M-3) |
| PC-9 | `Ret`: accept any `--git-common-dir` (skip `IsOwnedCommonDirectory`) | EN/`Repository_and_branch_must_match("foreign-repository")` at kept | the foreign clone then moves/removes its own tree |
| PC-10 | `Ret`: drop the `symbolic-ref` compare | same `("other-branch")` and `("detached")` at kept | both commits are on live origin |
| PC-11 | `Ret`: drop the short-id == leaf compare | same `("branch-id-not-leaf")` at kept | HEAD equals the request branch |
| PC-12 | `Ret`: drop the status check | EN/`Dirty_tree_is_kept("tracked")` at block 2/4 (moved) | M-4: Git keeps the data in the retiring root |
| PC-13 | `Ret`: `--untracked-files=no` | same `("untracked-nested-file")` at block 2/4 | `normal` is equivalent (measured) |
| PC-14 | `Ret`: treat status exit != 0 as clean | same `("status-unavailable")` at kept | fault only on the first status; the recheck's real status is clean |
| PC-15 | `Ret`: drop the `rev-list` check | EN/`Unpushed_commit_is_kept("pushed-by-url-tracking-stale")` at kept | only rev-list fails (M-5) |
| PC-16 | `Ret`: zero ls-remote lines treated as one with an empty tip | EN/`Live_origin_must_hold_the_branch("deleted-on-origin")` at `Reason` | reason only (M-6) |
| PC-17 | `Ret`: accept any matching line (drop the exact name compare) | same `("decoy-tail-match")` at kept | the decoy contains HEAD |
| PC-18 | `Ret`: on ls-remote failure fall back to `refs/remotes/origin/<b>` | same `("origin-unavailable")` at kept | the tracking ref contains HEAD |
| PC-19 | `Ret`: read the tip from `refs/remotes/origin/<b>` | same `("origin-reset-behind")` at kept | stale tracking ref contains HEAD |
| PC-20 | `Ret`: skip the `cat-file -e` tip check | same `("origin-advanced-unknown-tip")` at `Reason` | reason only (M-7) |
| PC-21 | `Ret`: drop `merge-base --is-ancestor` | same `("origin-reset-behind")` at kept | live tip is HEAD~1 and known locally |
| PC-22 | `Ret`: no ls-remote budget | EN/`Ls_remote_is_bounded` at `< 5 s` | the fault sleeps 8 s, inside the 15 s timeout |
| PC-23 | `Ret`: endpoint `"origin"` | EN/`Clean_pushed_old_mirror_is_removed_without_force_or_prune` at `removed` (and the argv pin) | measured: `origin` does not resolve under `--git-dir=/dev/null` |
| PC-24 | `Ret`: ignore holders | EN/`Holder_keeps_the_tree("holder")` at kept; also NP V-20/V-21 | |
| PC-25 | `Ret`: treat `Complete=false` as empty | same `("scan-unknown")` at kept | |
| PC-26 | `Seam`: `Scan` returns `Complete=true` when `/proc` cannot be enumerated | NP/`Unreadable_proc_root_is_unknown` at `Complete == false` | S-3 `procRoot`; the slot path stays fail-open |
| PC-27 | `Ret`: DryRun proceeds to phase A | EN/`Dry_run_reports_would_remove_and_mutates_nothing` at `would_remove` / block | |
| PC-28 | `Ret`: phase A skips its recheck | EN/`Execute_rechecks_under_the_lock_before_the_move("holder-appears")` at kept | Git cannot see processes, so nothing else refuses |
| PC-29 | `Ret`: drop the `st_dev` compare | EN/`Cross_device_retiring_root_moves_nothing` at kept | `Device` seam |
| PC-30 | `Ret`: drop the retiring-root-inside check | EN/`Retiring_root_inside_the_worktree_root_is_refused` at kept | the move into `<Worktrees>/retiring` succeeds otherwise |
| PC-31 | `Ret`: on a move failure run phase B on the original path | EN/`Move_failure_leaves_the_tree_in_place` at `Tree == before` | the original tree is clean, so a no-force remove succeeds |
| PC-32 | `Ret`: `worktree remove --force` | EN/`Phase_b_refusal_keeps_the_moved_tree_registered` at the moved tree exists | M-16 |
| PC-33 | `Ret`: `worktree prune` after the remove | EN/`Clean_pushed_old_mirror_is_removed_without_force_or_prune` at `task-a16770ff` listed | `StaleRegistration` |
| PC-34 | `Ret`: the retry moves the residue again (new request id) | EN/`Retiring_residue_is_retried_by_phase_b_only` at no second `worktree move` | |
| PC-35 | `Ret`: the retry removes every retiring entry | same, at the other task's residue exists | the other residue is clean, so a remove succeeds |
| PC-36 | `Disp`: call `RetainWorkspaceCompleteAsync` inside `MutateAsync` | DI/`Phase_b_runs_outside_the_mutation_lock` at Input within 2 s | M-18 |
| PC-37 | `Disp`: call `RetainWorkspaceCheckAndMoveAsync` outside `MutateAsync` | DI/`Phase_a_waits_for_the_mutation_lock` at not started | M-18 |
| PC-38 | `Disp`: call `Complete` for every step | DI/`Phase_b_runs_only_after_a_move` at 0 calls | |
| PC-39 | `Ret`: prune on an absent path (the `RemoveAsync` shape) | EN/`Absent_path_is_already_absent_without_prune` at `task-a16770ff` listed | |
| PC-40 | `Ret`: the inventory runs `worktree prune` first | IN/`Inventory_is_read_only` at the argv / listed | |
| PC-41 | `Adp`: `RetentionFeatures` ignores `isLinux` | DI/`Capability_is_advertised_only_on_linux(false)` | pure |
| PC-42 | `Svc`: `Queued` in the terminal set | SV/`Each_ineligible_task_alone_is_recorded_and_never_sent("queued")` at one request (two sent) | old `CompletedAt` (M-8) |
| PC-43 | `Svc`: `Dispatched` in the set | same `("dispatched")` | M-8 |
| PC-44 | `Svc`: `Working` in the set | same `("working")` | M-8 |
| PC-45 | `Svc`: `Blocked` in the set | same `("blocked")` | M-8 |
| PC-46 | `Svc`: drop the age floor | same `("settling")` | |
| PC-47 | `Svc`: drop the server leaf compare | same `("leaf-mismatch")` | |
| PC-48 | `Svc`: drop the direct-child compare | same `("not-direct-child")` | |
| PC-49 | `Svc`: drop `WorktreeBranch != null` | same `("branch-null")` | |
| PC-50 | `Svc`: select by root only, not `RunnerId` | same `("other-runner")` at no request with its id | |
| PC-51 | `Svc`: drop `RemoteWorkspaceSlotId == null` | same `("slot-bound")` | M-9 |
| PC-52 | `Svc`: drop `SourceLandingOperationId == null` | same `("source-landing")` | M-10 |
| PC-53 | `Svc`: drop `RepairSourceTaskId == null` | same `("repair-source")` | M-10 |
| PC-54 | `Svc`: drop `Role != Mutation` | same `("mutation")` | M-10 |
| PC-55 | `Svc`: drop the land-pending predicate | same `("land-pending")` | |
| PC-56 | `Svc`: sync debt states without `Pending` | same `("sync-debt-pending")` | |
| PC-57 | `Svc`: without `Ready` | same `("sync-debt-ready")` | |
| PC-58 | `Svc`: without `Held` | same `("sync-debt-held")` | |
| PC-59 | `Svc`: drop the shared-path query | same `("path-shared")` | M-12 |
| PC-60 | `Svc`: live session states without `Created` | same `("session-created")` | M-11 |
| PC-61 | without `Starting` | same `("session-starting")` | M-11 |
| PC-62 | without `Running` | same `("session-running")` | M-11 |
| PC-63 | without `Stopping` | same `("session-stopping")` | M-11 |
| PC-64 | `Svc`: drop `report_unpreserved` | same `("report-unpreserved")` | |
| PC-65 | `Svc`: `Enabled` not read | SV/`Disabled_job_contacts_no_runner` at 0 frames | |
| PC-66 | `Svc`: `Mode = Execute` always | SV/`Execute_off_sends_dry_run_only` at `Mode == DryRun` | |
| PC-67 | `Svc`: ignore `Runners` | SV/`Only_listed_capable_phone_home_runners_are_contacted("unlisted")` | |
| PC-68 | `Svc`: contact a listed runner through the directory whatever its kind | same `("not-phone-home")` at the row `runner_not_eligible` | |
| PC-69 | `Svc`: drop the capability check | same `("no-capability")` | the rig peer answers any op |
| PC-70 | `Svc`: ignore `MaxRemovalsPerTick` | SV/`Execute_requests_are_bounded_per_tick` at 2 | |
| PC-71 | `Svc`: send the candidates concurrently (`Task.WhenAll`) | SV/`Second_request_waits_for_the_first_reply` at 1 | |
| PC-72 | `Svc`: no pause | SV/`Requests_are_paced` at 1 before the advance | `FakeTimeProvider` delay |
| PC-73 | `Svc`: no re-read before each send | SV/`Reopened_task_is_rechecked_before_its_turn` at 1 | |
| PC-74 | `Svc`: clear the path on every reply | SV/`Outcome_recording("kept")` at path unchanged | |
| PC-75 | `Svc`: a transport loss recorded as `removed` | same `("lost")` | |
| PC-76 | `Svc`: send `WorkspaceRetain` for an owner-hint match | SV/`Unmatched_inventory_names_are_reported_not_sent` at no request | |
| PC-77 | `Svc`: on `removed` also call `IWorktreeManager.RemoveAsync` | SV/`Outcome_recording("removed")` at 0 manager calls | |
| PC-78 | `Set`+`json`: `Enabled = false` (D-15 flip) | ST/`Shipped_defaults_are_on_for_server2_with_execute` | |
| PC-79 | `Set`+`json`: `Execute = false` (D-15 flip) | same | |
| PC-80 | `json`: `Runners = []` (D-15 flip) | same | |
| PC-81 | `Set`: `MinTerminalHours >= 0` | ST/`Validator_bounds("MinTerminalHours","0",false)` | |
| PC-82 | `Set`: upper bound 1000 | same `("MaxRemovalsPerTick","101",false)` | |
| PC-83 | `Set`: no rooted check | same `("RetiringRoot","work/r",false)` | |
| PC-84 | `Job`: remove `DisableConcurrentExecution` | ST/`Job_is_not_concurrent` | |
| PC-85 | `Tmp`: unregistered names eligible | TS/`Unregistered_name_is_reported_and_kept` | |
| PC-86 | `Tmp`: the directory mtime decides | TS/`Newest_file_write_decides_age("new-top-file")` | |
| PC-87 | `Tmp`: top-level files only | same `("new-nested-file")` | |
| PC-88 | `Tmp`: ignore holders | TS/`Holder_keeps_the_entry("holder")`; also TN V-50 | |
| PC-89 | `Tmp`: `Complete=false` as empty | same `("scan-unknown")` | |
| PC-90 | `Tmp`: drop `antiphon-pty-hosts` | TS/`Denied_names_are_kept_under_a_broad_family("antiphon-pty-hosts")` | M-14 |
| PC-91 | drop `antiphon-pty-*` | same `("antiphon-pty-<hex>")` | M-14 |
| PC-92 | drop `CoreFxPipe_*` | same `("CoreFxPipe_x")` | |
| PC-93 | drop `dotnet-diagnostic-*` | same `("dotnet-diagnostic-1-2-socket")` | |
| PC-94 | drop `.X*` | same `(".X11-unix")` | |
| PC-95 | drop `systemd-*` | same `("systemd-private-x")` | |
| PC-96 | drop `node-compile-cache` | same `("node-compile-cache")` | |
| PC-97 | skip `CleanupFamilyRegistry.IsDenied` | same `(".codex")` | |
| PC-98 | `Tmp`: drop the uid check | TS/`Foreign_owned_descendant_keeps_the_entry` | |
| PC-99 | `Tmp`: follow a top-level symlink | TN/`Symlinked_entry_is_refused_and_its_target_kept` | |
| PC-100 | `Tmp`: `Directory.Delete(entry, true)` instead of `RunnerWorkspaceNoFollowDelete` | TN/`Inner_symlink_is_not_followed` at the outside file | .NET 9 does not follow; Mutation records whether this is equivalent, then uses "walk follows links" (`RecurseSubdirectories` without `AttributesToSkip = ReparsePoint`) |
| PC-101 | `Tmp`: ignore `MaxEntries` | TS/`Per_tick_bounds("entries")` | |
| PC-102 | `Tmp`: ignore `MaxBytes` | same `("bytes")` | |
| PC-103 | `Tmp`: ignore the inventory limit | TS/`Inventory_limit_keeps_the_entry` | |
| PC-104 | `Tmp`: ignore the family flag | TS/`Claude_scratch_needs_flag_and_no_mirror("flag-off")` | |
| PC-105 | `Tmp`: skip the mirror probe | same `("mirror-present")` | |
| PC-106 | `Tmp`: skip the retiring probe | same `("retiring-present")` | |
| PC-107 | `Tmp`: recurse for family names | TS/`Only_top_level_entries_are_considered` | |
| PC-108 | `Tmp`: `c487-` family producer path changed to `scripts/test-nightly-run.ps1` | TF/`Every_family_producer_still_contains_its_prefix` | that file only dot-sources the lib |
| PC-109 | `Tmp`: drop `c1636-adopt` from the alternation | TF/`Pty_test_root_alternation_equals_known_prefixes` | |
| PC-110 | `Tmp`: `c1514` regex `^c1514-.*$` | TF/`Family_regexes_are_anchored_and_exact` | |
| PC-111 | `Svc`: `TempSweep.Enabled` not read | TV/`Disabled_sweep_sends_nothing` | |
| PC-112 | `Svc`: temp `Mode = Execute` always | TV/`Enabled_sweep_defaults_to_dry_run_and_records_rows` | |
| PC-113 | `Set`: `claude-scratch` default true | TV/`Claude_scratch_is_sent_off_by_default` | |
| PC-114 | `pol`: remove `[NativeLinuxOnly]` from one NP method | `Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/Policy_rows_require_pinned_attribute` | green at this tip (measured) |
| PC-115 | `pol`: the same for one TN method | same | |
| PC-116 | `Al`: the first disk instead of the configured path | DA/`Only_the_configured_volume_counts` | `/state` listed first |
| PC-117 | `Al`: ignore freshness | DA/`Stale_sample_raises_nothing` | |
| PC-118 | `Al`: `>=` becomes `>` | DA/`Work_at_85_percent_raises_one_warning` | S-7 integers: exact |
| PC-119 | `Al`: threshold `UsedPercent - 1` | DA/`Work_below_85_percent_raises_nothing` | 84.9 >= 84 |
| PC-120 | `Al`: alarm every runner in the cache | DA/`Unlisted_runner_raises_nothing` | |

All 119 PCs run on server2 (no Windows-lane PC). PC-24 and PC-88 also turn the named native methods red
where noted; that is extra evidence, not a second map.

Not listed as guards: G-4 (grammar, M-1); `--untracked-files=normal` (equivalent, measured); dropping
`--ignore-submodules=none` (a submodule tree is refused by `worktree move` itself, and the fixture has no
submodule; Review checks the argv pin in V-2); the other-uid holder count (D-7, recorded, not a refusal);
`ShouldWriteRow` dedupe (not a safety fact).

### Review c238f9ca round-1 repairs (D-1, D-2, D-3, A-2, A-3, D-15)

Witnesses added by the Code repair task 5fefd110 (on top of the S1 tip `d08bf72b6`). The new guards and PCs are
pending for method-scoped SourceLanding Mutation, like every other PC here.

| V | Class / method | Guard |
|---|---|---|
| V-70 | EN `Residue_retry_keeps_a_residue_a_live_process_holds` (DryRun, Execute) | G-134 (review D-3 c) |
| V-71 | EN `Tree_not_registered_at_its_path_is_kept` (DryRun, Execute) | G-133 (review D-3 b) |
| V-72 | DI `Recent_use_by_this_runner_keeps_the_tree` (mirror-then-retain, live-session; window-elapsed is the control) | G-129..G-132 (review D-2) |
| V-73 | (V-72's control case) | none |
| V-74 | SV `Kept_trees_do_not_starve_newer_removable_ones` (30 kept older than 5 removable, bound 25, two ticks) | G-121 (review D-1) |
| V-75 | SV `Dry_run_census_covers_the_whole_population` | G-122 |
| V-76 | SV `Kept_tree_backs_off_until_its_reason_changes` | G-121, G-123 |
| V-77 | SV `Changed_task_facts_resend_a_backed_off_tree_at_once` | G-124 |
| V-78 | SV `Preview_is_dry_run_even_when_execute_is_on` | G-125 (review D-3 a) |
| V-79 | SV `Tick_stops_starting_requests_past_its_budget` | G-126 (review A-2) |
| V-80 | SV `Switching_off_stops_a_running_tick` (enabled, execute) | G-127, G-128 (review A-3) |

| PC | Mutant (one condition) | Witness |
|---|---|---|
| PC-121 | `Svc` `RecordSend`: never set `NotBefore` (the backoff removed) | SV/`Kept_trees_do_not_starve_newer_removable_ones`, `Kept_tree_backs_off_until_its_reason_changes` |
| PC-122 | `Svc` selection: order by `CompletedAt` only (no never-sent first) | SV/`Dry_run_census_covers_the_whole_population` |
| PC-123 | `Svc` `RecordSend`: a changed reason backs off like a repeat | SV/`Kept_tree_backs_off_until_its_reason_changes` |
| PC-124 | `Svc` `Fingerprint`: a constant | SV/`Changed_task_facts_resend_a_backed_off_tree_at_once` |
| PC-125 | `Svc` `RunTickAsync`: `execute = settings.Execute` (preview not forced) | SV/`Preview_is_dry_run_even_when_execute_is_on` |
| PC-126 | `Svc` `StopReason`: no `MaxTickMinutes` term | SV/`Tick_stops_starting_requests_past_its_budget` |
| PC-127 | `Svc` `StopReason`: no `Enabled` re-read | SV/`Switching_off_stops_a_running_tick(enabled)` |
| PC-128 | `Svc` `StopReason`: no `Execute` re-read | SV/`Switching_off_stops_a_running_tick(execute)` |
| PC-129 | `Act` `Covers`: ignore the recent stamps | DI/`Recent_use_by_this_runner_keeps_the_tree(mirror-then-retain)` |
| PC-130 | `Act` `Covers`: ignore live sessions | DI/`Recent_use_by_this_runner_keeps_the_tree(live-session)` |
| PC-131 | `Disp` Mirror: no `RecordMirror` | DI/`Recent_use_by_this_runner_keeps_the_tree(mirror-then-retain)` |
| PC-132 | `Disp` Launch: no `RecordLaunch` | DI/`Recent_use_by_this_runner_keeps_the_tree(live-session)` |
| PC-133 | `Ret` step 3: drop `if (block is null)` (continue as registered) | EN/`Tree_not_registered_at_its_path_is_kept` |
| PC-134 | `Ret` `ResidueOrAbsentAsync`: drop the holder refusal | EN/`Residue_retry_keeps_a_residue_a_live_process_holds` |
| PC-135 | `Set`: `Runners = ["server2"]` in the type | ST/`Shipped_defaults_are_on_for_server2_with_execute` |

guards (round 1) = 15 new (G-121..G-135), mapped 15, missing 0.

### Out of scope

- The real-volume measurements of plan §8 (dry-run census, ls-remote p95 per candidate, phase B duration of
  the largest tree, `Input` p95 during a tick): enabling-time operator steps, not tests.
- Host-side residue (~210 GiB, plan §1) and the desktop's registrations (D-11, CARD-0824 S5).
- A cross-uid holder: one uid (1654) on server2; the uid seam stands in (V-45).
- A real cross-device retiring root: one LV on server2; the `Device` seam stands in (V-15).
- A real slow multi-GiB delete: the `PhaseBGate` seam stands in (V-14, Substitute 1).

### Implementation steps (red first, then green)

Per slice, the red commit holds the fixture, the classes, the policy row with the resealed hash, and a
compile shim: the new contracts, settings, seams S-1..S-8 and types with no behaviour. Engine and sweep
methods return `kept`/`not_implemented` for every request; the server tick sends nothing; the alarm
builder returns no item; the capability list is unchanged. Code runs the slice's rows against the red
commit and records the red list:

- **S1 red:** every EN/IN/DI/NP/SV refusal case is **green by accident** under a `kept` shim (it keeps the
  tree), so the red commit's shim returns `removed` after an unconditional `worktree move` + `worktree
  remove --force` (the `RemoveAsync` shape). Expected red then: every refusal case of EN/IN/NP (the tree is
  gone), V-12, V-13, V-17, V-26, V-27, V-28 (argv/tree), V-14/V-14b/V-14c (the shim dispatcher runs both
  phases under the lock); expected green: V-1 except its `--force`/`prune` argv assertions (red), V-16,
  V-22. Server: every V-30 case is red at "exactly one request" (the shim tick sends nothing, so the
  control is not sent), V-31..V-39 red; ST red except `Job_is_not_concurrent` (attribute present in the
  shim). This makes every witness's red reason the one its PC names, not a missing type.
- **S2 red:** the same trick (`removed` for every entry, `Directory.Delete(…, true)`): every TS/TN keep case
  red, V-40 `false` (dry run) red, V-48* green (the registry is data). TV red.
- **S3 red:** no item: V-60, V-62 (second half), V-65 red; V-61, V-63, V-64, V-66 green (controls with PCs).
- Green: the engine, the sweep, the service, the builder. Commit, then the slice's checkpoint run.

## Performance budget

Owner: [test-performance-standards.md](../../test-performance-standards.md). Sources: §Measured facts.

- **Per-test bodies (estimated from measured analogs).**
  - Engine/inventory Git cases: analog p95 2.12 s at load ~10 (`C0824WorkspaceSlotResetTests`), raw Git
    0.09-0.18 s; retention adds a bare origin and a side clone, so ~2.5 s p95, **~4.1 s loaded** (×1.65).
  - Dispatch cases: < 0.5 s plus V-14b's fixed 500 ms wait and V-14's ≤ 2 s bound.
  - Native holder cases: analog p95 0.78 s; ~1.3 s loaded.
  - Server rig cases: analog p95 10.4 s here, **17.2 s loaded** (CARD-1660). Transport-world cases
    (V-39*) add the scratch repo, ~12 s / 20 s loaded.
  - Settings, registry and dispatch-pure cases: < 0.2 s.
  - Temp sweep cases: file planting only, < 0.5 s; native < 1.3 s.
  - Disk alarm: analog p95 5.3 s (isolated schema), ~8.7 s loaded.
- **Timeouts** (2x loaded p95, rounded up; every internal bound fits inside):
  `[Timeout(15_000)]` engine, inventory and dispatch classes (2 × 4.1 s = 8.2 s, raised to 15 s for V-25's
  8 s fault sleep under PC-22); `[Timeout(10_000)]` native, temp sweep and registry classes (the C0824
  native value); `[Timeout(45_000)]` server and temp-server classes (2 × 20 s for the transport cases;
  the C0824 class uses 35 s for the plain rig); `[Timeout(20_000)]` disk alarm (2 × 8.7 s); settings none.
  Code resets each class from its first green TRX by the same formula.
- **Shared setup.** None across tests: one scratch per test (each test asserts its own `task-<id8>` leaf
  and fresh root), one rig per server test (the C0824 class pattern; it runs its cases in parallel).
- **Teardown.** Scratch delete (C0824 `Dispose`, best effort) and `Stop(child)` in `finally`.
- **Phases.** `TestTiming.BeginPhase(ProcessWait, …)` around each engine request and each server tick.
  Slot wait is reported separately (0 s measured today; 31-90 s on the CARD-1653 day).
- No row or run is near 30 minutes (R1). The largest row is CP-5 at about 7 minutes with its build (54 cases).

### Checkpoints

Three checkpoint runs, one per slice After group. All rows are server2 (Linux); none needs the desktop.
These rows replace the plan's rows with the same numbers; CP-12 and CP-13 are new. `Min` is the TUnit
executed floor (`-MinExecuted`).

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c1677-s1r/ | c1677-runner-engine | /*/*/(C1677RetentionEngineTests*)\|(C1677RetentionInventoryTests*)\|(C1677RetentionDispatchTests*)/* | V-1..V-19, V-24..V-29, V-14d, V-70..V-73 | 3 classes, 0 failed/skipped | 53 | 5 | true |
| CP-2 | S1 | CP-1 | c1677-runner-native-process | /*/*/C1677RetentionNativeProcessTests/* | V-20..V-23 | class, 0 failed/skipped | 4 | 1 | true |
| CP-3 | S1 | CP-1 | c1677-runner-workspace-regression | /*/*/RunnerWorkspaceServiceTests/* | R-1 | existing class, 0 failed/skipped | 35 | 1 | true |
| CP-4 | S1 | CP-1 | c1677-native-policy-regression | /*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* | R-2 | existing class, 0 failed/skipped | 3 | 1 | true |
| CP-12 | S1 | CP-1 | c1677-slot-probe-regression | /*/*/C0824WorkspaceSlotProcessScanTests/* | R-6 | existing class, 0 failed/skipped | 3 | 1 | true |
| CP-5 | S1 | tests/Antiphon.Tests -> bin-c1677-s1s/ | c1677-server-retention | /*/*/(C1677RetentionServerTests*)\|(C1677RetentionSettingsTests*)/* | V-30..V-39c, V-74..V-80 | 2 classes, 0 failed/skipped | 66 | 7 | true |
| CP-6 | S1 | CP-5 | c1677-phonehome-ops-regression | /*/*/PhoneHomeConnectionTests/Supported_operations_preserve_contracts | R-3 | method, 0 failed/skipped | 1 | 1 | true |
| CP-7 | S2 | tests/Antiphon.SessionRunner.Tests -> bin-c1677-s2r/ | c1677-runner-temp | /*/*/(C1677TempSweepTests*)\|(C1677TempFamilyRegistryTests*)/* | V-40..V-48c | 2 classes, 0 failed/skipped | 27 | 4 | true |
| CP-8 | S2 | CP-7 | c1677-runner-temp-native | /*/*/C1677TempSweepNativeTests/* | V-49, V-49b, V-50 | class, 0 failed/skipped | 3 | 1 | true |
| CP-13 | S2 | CP-7 | c1677-native-policy-regression-s2 | /*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* | R-2 | existing class, 0 failed/skipped | 3 | 1 | true |
| CP-9 | S2 | tests/Antiphon.Tests -> bin-c1677-s2s/ | c1677-server-temp | /*/*/C1677TempSweepServerTests/* | V-51..V-51c | class, 0 failed/skipped | 3 | 4 | true |
| CP-10 | S3 | tests/Antiphon.Tests -> bin-c1677-s3s/ | c1677-server-disk-alarm | /*/*/(C1677DiskAlarmTests*)\|(AttentionKindWireTests*)/* | V-60..V-67, R-4 | 2 classes, 0 failed/skipped | 15 | 5 | true |
| CP-11 | S3 | n/a | c1677-client-attention | `pwsh -NoProfile -File scripts/test-client.ps1 attentionVisuals.test.ts` | R-5 | file, 0 failed | n/a | 3 | true |

How Min is counted (executions):

- CP-1: EN 38 + IN 3 + DI 5 = 46, plus the round-1 repairs EN 4 + DI 3 = **53**. CP-2: 4. CP-5: SV 44 + ST 10 = 54,
  plus the round-1 repairs SV 8 + ST 4 = **66**. CP-7: TS 24 + TF 3 = 27. CP-8: 3.
  CP-9: 3. CP-10: DA 11 + `AttentionKindWireTests` 4 (measured) = 15.
- CP-3 35, CP-4/CP-13 3, CP-6 1, CP-12 3: measured at this tip. `Min` is a floor; a later method added to
  an existing class keeps it valid.

Roster and suffix check (grep of `class (C1677|RunnerWorkspaceService|C0824WorkspaceSlot|NativeLinuxPolicyWitness|PhoneHomeConnection|AttentionKindWire)` at
`b477d7ccf`): no `C1677*` class exists. Among the new names, `C1677RetentionEngineTests*`,
`C1677RetentionInventoryTests*` and `C1677RetentionDispatchTests*` match none of `C1677RetentionNativeProcessTests`,
`C1677RetentionServerTests`, `C1677RetentionSettingsTests` or `C1677RetentionScratch`;
`C1677TempSweepTests*` matches neither `C1677TempSweepNativeTests` nor `C1677TempSweepServerTests`.
`C1677RetentionScratch` and `C1677RetentionTransportWorld` have no `[Test]`. `NativeLinuxPolicyWitnessTests`
is filtered by its exact name, so `NativeLinuxPolicyWitnessMutationTests` is not run. CP-4 and CP-13 run
the same class in different After groups with different builds (the policy file changes in each slice).

**Import verdict (measured, server2, 2026-11-03, one lease each, held 7-10 s):** `dotnet run --project
tools/Antiphon.Checkpoints -- import --plan docs/superpowers/plans/2026-11-03-card-1677-test-design.md
--out .antiphon/c1677-td-manifest-r2.yaml` on the committed table → exit 0, `imported 13 rows`, **no warnings**
(the first draft imported the same way; re-importing onto its existing `--out` file is `refused:
unrecorded-existing`, exit 2, so each import uses a fresh `--out`). The manifest has five
builds (`bin-c1677-s1r`, `-s1s`, `-s2r`, `-s2s`, `-s3s`), one per project per After group, and every
`minExecuted` equals the `Min` column above; the escaped `\|` filters parse to the `(A*)|(B*)` grammar.

### Cost

Labels: **measured** where stated, otherwise estimated. Slot wait is reported separately.

- **Ordinary V/R floor (Code)** = the sum of the CP `EstimatedMinutes`:
  - S1 run: CP-1 5 + CP-2 1 + CP-3 1 + CP-4 1 + CP-12 1 + CP-5 7 + CP-6 1 = **17 min** (builds: SessionRunner
    27 s measured warm, 2.3 min p95; Antiphon.Tests 2.4 min measured).
  - S2 run: CP-7 4 + CP-8 1 + CP-13 1 + CP-9 4 = **10 min**.
  - S3 run: CP-10 5 + CP-11 3 = **8 min**.
  - Total **35 min**. Savings against the plan's 41 min: CP-3 measured at 1 min instead of 3 (-2), CP-5
    7 instead of 10 (-3; measured rig p95 10.4 s, parallel), CP-9 4 instead of 6 (-2); CP-12/CP-13 add 2.
- **Positive-control floor (Mutation)**, method-scoped, one incremental build per mutant and per restore:
  - Runner PCs (PC-1..PC-41, PC-85..PC-110, PC-114, PC-115: 68): an incremental SessionRunner.Tests build
    of about 30 s (27 s measured for the whole project, warm) twice, plus two method runs of 3-15 s: about
    **1.5 min each = 102 min serial**. Batching pairs on different files (`Ret` with `Seam`/`Disp`/`Adp`,
    `Tmp` with `pol`; about 10 pairs) saves about 15 min → about 87 min.
  - Server PCs (PC-42..PC-84, PC-111..PC-113, PC-116..PC-120: 51): an incremental Antiphon.Tests build of
    about 90 s (143 s measured full) twice plus two runs of the V-30 method (23 cases in parallel, about
    1 min loaded): about **5 min each = 255 min serial**. The V-30 PCs (PC-42..PC-64, 23 mutants) all edit
    `Svc` and cannot batch; three detached shards (gitcommon UNSOURCED sharding) bring the server wall time
    to about **90 min**.
  - Setup: one cold build per project and shard: about 2.3 + 2.4 × 3 = **about 10 min**.
- **Total** = setup 10 + V/R 35 + PC 87 + 255 = **about 387 min estimated** (about 190 min wall with three
  server shards beside one runner lane).
- **Cheaper option for the Mutation stage:** the batch-21 env-gated recipe (every mutant compiled behind
  `On("PC-n")` in one build per project, rounds selected by an environment variable, one lease per run;
  measured 50 mutants in about 90 min on loaded server2) would cut the 119 build pairs to about 6 builds:
  about 119 × 1.8 min ≈ **214 min serial**, about 80 min wall in three rounds. Savings ≈ 170 min. Its gate
  must not touch the scanned source of TF (`RunnerTempSweep.cs` producer table is data the TF tests read
  only through reflection, so it is safe) and PC-114/PC-115 edit test attributes, which stay per-build.

### Completion and handoff

- guards=119, mapped=119, missing=0, duplicate PC maps=0. All 119 PCs run on server2. Cost is numeric
  (estimated, with the measured inputs marked).
- Seams S-1..S-8 are missing at the tip and are part of the Code slices. S-1 is a correction of the plan
  (measured), not a choice; none is unverifiable.

## Slices and parallel Code

- **S1 (engine + server + contracts + dry-run + inventory)** first. Rows CP-1..CP-6, CP-12.
- **S3 (alarm)** can run **in parallel with S1**: it touches `AttentionDtos.cs`, `AttentionService*.cs`,
  a new `WorkspaceDiskAlarmSettings`, the client visuals and their test, plus `Program.cs` and
  `server/appsettings.json`. To stay independent of S1's new settings type, the alarm reads the retention
  mode from `IConfiguration` (`RunnerWorktreeRetention:Enabled`/`Execute`, absent = `Off`). `appsettings.json`
  and `Program.cs` are the only shared files: S1 adds `RunnerWorktreeRetention` beside `WorktreeResidue`,
  S3 adds `WorkspaceDiskAlarm` beside `Alarms`. Whichever lands second checks `git merge-tree` first.
- **S2 (temp sweep)** after S1 lands: it extends S1's contracts (`TempSweep = 47`), dispatcher, service and
  settings, so a parallel S2 would conflict on every S1 file.
- **Land-order hazard:** CARD-1653 S2 (in flight) adds `c1653-sock` and `c1653-peer` to `KnownPrefixes`. V-48b
  then fails until S2's `pty-test-root` alternation includes them (the plan lists those prefixes as
  `unregistered`, so the alternation must be generated from `KnownPrefixes`, or S2 updates both). Code for
  S2 rebases its alternation on whichever master it lands on.

## Retention age

The measured fixture cost does not depend on the age. At ~80 GiB a day, 12 h keeps ~40 GiB and 48 h
~160 GiB of finished mirrors against 188 GiB free (plan D-1). **Recommendation: enable at 12 h** (the e937b0c3
run proved 437 eligible with 0 Git refusals at that age); 48 h leaves only ~28 GiB of headroom above a day
of growth on one LV that also backs `/tmp` and Docker.

## Open questions for Code (each with a default)

- **Q-1 `already_absent` with a retiring residue.** Default S-5: the residue lookup precedes `already_absent`,
  and the server clears the path only for `already_absent` with `RetiringResidue == false` (V-34).
- **Q-2 Retiring entry name.** Default `<leaf>.<request id first 8 hex>`; the inventory classifies it by
  the `task-<8hex>.` prefix.
- **Q-3 Use-gate registration root.** Default: retention calls `AcquireRemovalAsync(path, path)`, because it
  never prunes, so an owner elsewhere in the root does not block it.
- **Q-4 `[Timeout]` values.** As §Performance budget until the first green TRX, then by the same formula.
