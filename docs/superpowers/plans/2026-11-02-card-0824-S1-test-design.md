# CARD-0824 S1 runner workspace slot pool: test design

TestDesign task f189bd64, 2026-11-02, server2 (Linux), base `b6bb58e763c9a8cdc5c8ff86f31e344b92e36a93`
(plan revision 2). Scope: **S1 only** (runner slot pool: lease, warm reset, cold reset, overflow slots,
re-homing, quarantine, flag default off; plan CP-1..CP-10). This companion adds verification detail to the
[plan](2026-11-02-card-0824-per-slot-worktrees-plan.md) and does not change its fix design. Read-only:
no production code, build or test run. The three git probes under "Measured facts" ran in a scratch
directory; they are not tests. Every expected result below is a design, not executed evidence.

Standing authority used: the operator's "H1 yes, H2 hold ... Rest are fine" covers the plan's D-n defaults.
This design relies on it for the defaults in "Open questions" and does not stop to ask.

## Measured facts used by this design (server2, uid 1654, git 2.47.3)

- `git clean -ffdx` on a file under a `0555` subdirectory prints `warning: failed to remove ro/sub/f:
  Permission denied`, **exits 1**, and leaves `?? ro/sub/f` in `status --porcelain=v1
  --untracked-files=all --ignored=matching`. So V-36's real fixture fails on server2. The runner uid is
  not 0, so V-36's uid-0 refusal does not fire there.
- `git clean -ffdx -e '**/obj/' -e '**/bin/' -e '**/node_modules/' -e '/client/dist/'` removed `.antiphon/`,
  `bin-ws/`, `src/P/bin-c1/` and a nested repository. It kept `src/P/bin/Debug/a.dll` (mtime
  `2026-01-01` unchanged), `src/P/obj/p.json`, `node_modules/x/i` and `client/dist/i.js`. So the §3.4
  keep-list does what it says, and `-x` still honours `-e`.
- Consequence (accepted by design, made explicit by V-2): an untracked file that task A plants **inside**
  a keep-list directory (`src/P/bin/notes.txt`) survives a warm reset. See open question Q-3.

## Verification design

### Inspection

Bodies read at `b6bb58e76`, before naming the cases:
- The plan, in full, including §3.2a–§3.12, S1 Files/Acceptance/Witnesses, the performance budget and
  `### Checkpoints` (CP-1..CP-14). → every V/R row below.
- `src/Antiphon.SessionRunner/RunnerWorkspaceService.cs`: `MirrorAsync`, `MirrorUnboundAsync`,
  `MirrorBoundAsync`, `CreateBoundWorktreeAsync`, `RequireCurrentGenerationAsync`, `RemoveAsync` (`:535`),
  `PublishAsync` (`:585`, leaf `NamePattern` check), `OwningRepositoryAsync`, `GitAsync`/`RunGitCoreAsync`.
  The seams in use: `Func<ProcessStartInfo, Process?> startProcess` (internal ctor), `BindingPhaseCut`,
  `WorkspaceUseGate`, `WorkspaceAdmissionForTest`. → V-1..V-11, V-27..V-36. The process seam is enough to
  inject a git failure with a realistic stderr. A new filesystem seam is needed only for non-git
  filesystem work (directory create, evidence copy, `du`).
- `RunnerWorkspaceBindingStore.cs` (intent/added/complete phases, `OwnerStampFileName =
  antiphon-mirror-owner.json`, `WriteOwnerStamp`/`OwnerStampMatches`). → the ledger journal style for
  V-6, and D-7's stamp reuse.
- `tests/Antiphon.SessionRunner.Tests/RunnerWorkspaceBindingTests.cs`: the nested `Scratch` fixture
  (bare-ish `origin` + `repo` clone + `work` allowed root, `GIT_CONFIG_NOSYSTEM`, empty global config,
  `Scratch.Git`), `[ParallelLimiter<ProcessSpawnLimit>]`, `[Category("Integration")]`, and the
  `BindingPhaseCut` crash-cut pattern of `C1032_BindingCreationRecovery`. 11 `[Test]`s, one with 10
  `[Arguments]` cases (20 executions). → the nearest fixture for every new SessionRunner class.
- `tests/Antiphon.SessionRunner.Tests/ProcessSpawnLimit.cs`: Limit 1, assembly-local. **SessionRunner.Tests
  has no `GitProcessSpawnLimit`** (it exists only in `tests/Antiphon.Tests/TestHelpers`). The plan's
  "git-only classes take `GitProcessSpawnLimit`" therefore applies only to Antiphon.Tests. Every new
  SessionRunner class takes `[ParallelLimiter<ProcessSpawnLimit>]`, as `RunnerWorkspaceBindingTests` does.
- SessionRunner.Tests csproj: `Microsoft.Extensions.TimeProvider.Testing` 9.10.0 is referenced
  (`FakeTimeProvider` is available). `tests/Shared/NativeLinuxOnlyAttribute.cs` is linked, and
  `tests/test-execution-policy.json` rows of `"suite": "session-runner"` pin each `[NativeLinuxOnly]`
  method (`NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute` and
  `Markers_do_not_expand_policy_exclusions`). → the linux-native classes need policy rows. That adds
  regression row CP-15.
- `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs`: `WorkspaceMirror` (`:351`) and
  `WorkspaceRemove` (`:391`) go through `MutateAsync` (`MutationLock`, `:883`). → the recipient half of
  the delivery inventory, and the busy-recipient case.
- `src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs`: `PhoneHomeWorkspaceMirrorBinding`,
  `PhoneHomeWorkspaceMirrorRequest(Branch, Sha, Name, Repository, Binding)`,
  `PhoneHomeWorkspaceMirrorResponse(Path, Binding)`, `PhoneHomeWorkspaceRemoveRequest`. → the wire
  additions (`Cold`, `SlotId`, `Generation`) and V-12's byte-identity check.
- Server: `RemoteWorkspacePreparer.cs` (owned background mirror, D-6 back-off, `RemoteWorktreePath`
  recorded when runner and attempt are still current); `TerminalRunnerSeatReleaseService.AmbiguousOwnershipAsync`
  (`:1216`, path equality over Queued/Dispatched/Working/Blocked); `TaskWorktreeRetirementService.RemoveRemoteMirrorAsync`
  (`:372`, clears the path only when residue is null); `AgentTaskDispatcher` drain re-arm (`:1150-1185`, calls
  `RemoveMirrorAsync(task, ct, task.WorktreeBaseSha)` and then clears `RemoteWorktreePath`). → V-10, V-12, V-13, V-37.
- `tests/Antiphon.Tests/Application/RemoteWorkspacePreparerTests.cs`: the `internal sealed class Rig`
  (`IsolatedTestSchema`, `PhoneHomeTestHost`, `PhoneHomeScriptedPeer`, `FakeTimeProvider`, `SeedAsync`,
  `TickAsync`, `EventsAsync(taskId, AgentTaskEventType.Held)`, `WaitForRequestsAsync`), 18 `[Test]`s. It is
  reusable from another class as `RemoteWorkspacePreparerTests.Rig`. → the scripted-peer half of CP-4.
- `tests/Antiphon.Tests/Application/RunnerSeatReleaseFixture.cs` `StartTransportAsync` (`:795`): a real
  `ClientWebSocket` + real `PhoneHomeCommandDispatcher` pump against `PhoneHomeTestHost`. Antiphon.Tests
  references `src/Antiphon.SessionRunner`. → the producer-to-recipient world in CP-4.
- `TerminalRunnerSeatReleaseTests.Legacy_ambiguous_answer_requires_fresh_absence_without_another_release`
  → regression CP-17 for the unchanged legacy ambiguity rule.
- Provider cwd-keyed state (plan §8 unknown): `CodexTranscriptTailer.cs:10-20`. Codex `resume`/`fork`
  take an id. Antiphon never launches `resume --last`, and the tailer needs positive evidence. **But**
  `AgentSessionService.cs:2620` maps `AgentSessionResumeMode.Continue` to Claude `--continue`. That flag
  resumes the newest conversation **in the cwd**, so in a shared slot it would resume the previous
  lessee's conversation. → new V-39 and Q-1.
- `docs/testing-and-build.md` checkpoint manifest and NativeLinuxOnly sections; `docs/test-performance-standards.md`
  (2x p95, shared setup, no wall-clock sleeps). → Checkpoints, timeouts, Cost.

Boundaries excluded after reading: `LandWorkspace`, `RunnerVerificationWorkspace` and `LocalVerificationWorkspace`
are unchanged (D-8, D-12). Their regression is CP-14 (S3, Windows). Desktop halves stay per task (S5).

### Delivery inventory

S1 adds or changes four asynchronous runner deliveries. None of them is a session-input queue. All of them
are phone-home request/response operations through `PhoneHomeLiveConnection` → runner
`PhoneHomeCommandDispatcher.MutateAsync`. The durable identity joins each delivery from producer to recipient.

| Path | Producer | Destination | Persistence boundary | Recovery | Observable receipt (recipient evidence) | Durable identity |
|---|---|---|---|---|---|---|
| D-A lease | `RemoteWorkspacePreparer` (owned background op) | runner `WorkspaceMirror` → `RunnerWorkspaceSlotPool.LeaseAsync` | runner ledger `Leasing` intent before git, `Leased` after verify; server `AgentTask.RemoteWorktreePath/RemoteWorkspaceSlotId/RemoteWorkspaceSlotGeneration` | runner restart: crash recovery (§3.5); server restart: existing re-arm resends the same preparation; lost reply: replay by `PreparationId` returns the stored `(slotId, generation)` | ledger `Leased(task, preparationId, gen)` **and** on-disk `HEAD == sha`, `symbolic-ref == refs/heads/<branch>`, owner stamp | `(TaskId, PreparationId)` → `(SlotId, Generation)` |
| D-B release | new `WorkspaceSlotReleaseService` (server reconciler) | runner release op | runner ledger `Releasing` intent; server clears `RemoteWorktreePath` only after a success response | runner crash mid-release: recovery check; refused/lost reply: next reconciler pass resends; already Free = idempotent | ledger `Free(gen)` + detached HEAD at the published SHA + task refs absent | `(SlotId, Generation, TaskId)` |
| D-C cold-reset request | server route `POST /api/session-runners/{runnerId}/workspace-slots/cold-reset` | runner cold-reset op (phone-home command on server2) | server audit row (request id, requester, reason, slot list); runner ledger `ColdResetPending(requestId)` or `ColdResetting` | idempotent by request id; pending survives runner restart (ledger) | ledger `lastColdReset.requestId == id` (Free slot) or `pending == id` (Leased slot); native `bin/` absent after it runs | `RequestId` |
| D-D quarantine release | `scripts/workspace-slots.ps1` → runner `POST /sessions/workspace-slots/quarantine/{dirId}/release` | runner ledger `quarantinedDirectories[]` | ledger entry disposition | re-run check is idempotent | entry closed + directory absent (Pushed/AcceptLoss/Removed) or kept with the error | `DirId` |

Producer-to-recipient tests through the **real** transport (real `PhoneHomeTestHost` + `ClientWebSocket` +
real `PhoneHomeCommandDispatcher` + real `RunnerWorkspaceService`/slot pool on a scratch repository, as in
`RunnerSeatReleaseFixture.StartTransportAsync`). They are all in CP-4:
- V-40 `C0824_LeaseRoundTripRealRunner`. One Free slot already eligible. Server tick → runner ledger
  `Leased` → server row holds the same `(SlotId, Generation)` and path. **Recipient evidence** is the ledger
  and the on-disk HEAD, never the server's "request sent" or the Result frame alone.
- V-41 `C0824_BusyRunnerLeaseSerialisedNotDuplicated`. A busy recipient: the test holds the runner's
  `MutationLock` by starting a slow `WorkspaceRemove` behind a gate. The lease completes after the gate
  opens. There is exactly one ledger lease and no overflow slot.
- V-42 `C0824_LeaseRecoveryAtEachHandoff` `[Arguments("reply-lost")]`, `[Arguments("runner-crash-after-leasing")]`,
  `[Arguments("server-restart-before-reply")]`. These cover crash or delivery failure at each handoff. In
  every case the recovered run ends with one `Leased` entry for `(TaskId, PreparationId)`, the same
  `(SlotId, Generation)` stored on the task, and no second slot touched.
- V-43 `C0824_ReleaseRoundTripRealRunner`. Reconciler → runner ledger `Free` + detached HEAD → server
  clears the path and keeps the slot history. Includes `[Arguments("reply-lost")]`: a resend is an
  idempotent success, not a second reset.

Declared substitutes and what each cannot prove:
- `PhoneHomeScriptedPeer` (V-10, V-12, V-13, V-37, V-39) proves request shape and the server's reaction to a
  given response. It cannot prove that the runner acted. V-40..V-43 close that gap.
- Direct `RunnerWorkspaceSlotPool` calls (CP-1..CP-3, CP-7..CP-10) prove recipient behaviour. They cannot
  prove server production or wire framing. `C0824_LeaseThroughDispatcherFrame` (CP-2) drives one
  `WorkspaceMirror` frame through a real `PhoneHomeCommandDispatcher` to pin the runner's wire decode.
- D-C on the desktop (direct HTTP) and D-D are exercised at the runner handler only. The
  `workspace-slots.ps1` front door is a script. A Code-stage `-WhatIf`/parse check is listed as R-6, and
  it cannot prove a live server2 command channel. That is the canary's job (plan §8 rollout step 2).

### Fixtures and binding conventions

- **New `C0824SlotScratch`** (`tests/Antiphon.SessionRunner.Tests/C0824SlotScratch.cs`, internal). It is
  copied from `RunnerWorkspaceBindingTests.Scratch`, not referenced: that one is private. It creates `origin`
  with `main` + two task branches + a commit that **deletes a project** (for the stale-output case), a
  `repo` clone and a `work` allowed root. One template per test (git init ≈ 0.2–0.5 s on Linux). The plan's
  "clone a shared template with `--shared`" is optional; Code measures before adding it.
  `Pool(settings, clock, probes)` builds a `RunnerWorkspaceService` with the empty global config env and a
  `RunnerWorkspaceSlotPool` on top. Helpers: `Plant(path, bytes, mtime)`, `Tree(slot)` (sorted
  path→sha256+mtime map, excluding `.git`), `Ledger()`, `Git(...)`.
- **Fake clock.** `FakeTimeProvider`, injected as `TimeProvider` into the new pool. It drives
  `OverflowWaitSeconds`, linger back-off (1, 2, 4 min), `IdleRetireHours`, the weekly boundary and the
  reflog "since lease" stamp. No `Task.Delay` on wall time and no `Thread.Sleep`. A wait is observed through
  a pool test seam `WaitingForReleaseAsync` (a `TaskCompletionSource` completed when the pool enters the
  wait), then the clock advances.
- **Git failure injection.** Use the existing `startProcess` seam. A wrapper matches the argv
  (`clean`, `checkout`, `worktree add`, `worktree remove`) and substitutes `sh -c 'printf "%s" "$1" >&2; exit 1' x
  "<stderr text>"` with a realistic message, for example `warning: failed to remove src/a: Permission
  denied` or `fatal: cannot create directory ... File name too long`. The wrapper counts calls, so "the
  replacement fails too" is a second matched call. No production interface is needed for git.
- **New `IWorkspaceSlotFileSystem`** (plan name) for non-git work only: create a directory, copy and verify
  evidence, measure bytes. The fake throws `UnauthorizedAccessException`/`IOException` (`HResult` for
  `ERROR_SHARING_VIOLATION`, `ENAMETOOLONG`) on a named path. It also models the Windows long-path and lock
  cases portably (V-34). Production is `PhysicalWorkspaceSlotFileSystem`.
- **New `IWorkspaceSlotProcessProbe`** returns holders `(pid, argv0, kind: cwd|fd)` under a path.
  Portable tests use `FakeProcessProbe` (scripted holders per call). The linux-native class uses the real
  `/proc` probe with real children: `sleep 30` started with `WorkingDirectory=<slot>` (cwd holder), and
  `tail -f <slot>/held.txt` with cwd outside the slot (fd holder). Each child is in its own process group
  and killed in `finally` with a 10 s bound.
- **New `IWorkspaceSlotDiskProbe`** returns `(freeBytes, totalBytes)` and the measured pool bytes. The fake
  is scripted. Real `du` is not used in portable tests.
- **Runner capacity.** The pool takes `capacity` as a constructor input (production: the runner's seat
  capacity). Tests pass 2 with `Headroom=2` (target 4) and `MaxOverflowSlots=2`, so bounds are reached with a
  handful of git worktrees.
- **Unremovable directory (linux-native).** A real `0755` slot with `ro/sub/f` and `chmod 0555 ro/sub`. The
  class **fails** (`Assert.Fail`, never skip) when `Environment.UserName == "root"` or `geteuid()==0`
  (read `/proc/self/status` `Uid:`), and restores `0755` in `finally` before scratch cleanup.
- **Server.** `RemoteWorkspacePreparerTests.Rig` (scripted peer) for the shape tests. The new
  `C0824SlotTransportWorld` (`tests/Antiphon.Tests/Application/C0824SlotTransportWorld.cs`) copies
  `RunnerSeatReleaseFixture.StartTransportAsync`'s real-socket pump with `PhoneHomeSettings { AllowedCwd,
  RunnerRepository, RunnerCloneSource }` pointed at a scratch repo. It is a per-test `IsolatedTestSchema`.
  `[ParallelLimiter<ProcessSpawnLimit>]` (git children), `[Category("Integration")]`.
- **Linux-native classes** carry `[NativeLinuxOnly("CARD-0824 ...")]` per method. Each method gets a
  `tests/test-execution-policy.json` row (`suite: session-runner`, `platform: windows`, `owner: CARD-0824`,
  reason "Linux-only by construction: /proc process scan" or "Unix 0555 directory mode"). CP-15 checks
  them.

### Proves it works now

Format: V-n: behaviour | layer | test | expected. "Red today" says why the test fails at base.
"Mutant" is the one-line product change that must turn it red after green (the PC column of the guard
inventory).

**CP-1 `C0824WorkspaceSlotResetTests`** (SessionRunner, portable)
- V-1: a lease resets to the exact SHA and branch from an unrelated previous state | runner | `C0824_LeaseResetsToExactShaFromUnrelatedState` | Arrange: slot `ws-01` left by task A on `feat/card-task-aaaaaaaa` at another SHA, with a tracked modification, a staged file and an untracked file. Act: lease for task B (sha B, branch B). Assert `rev-parse HEAD == shaB`, `symbolic-ref HEAD == refs/heads/<B>`, porcelain (`-z --untracked-files=all`) empty, path `ws-01`. Red today: the pool type does not exist (compile-red commit). Then a behavioural red against the stub `LeaseAsync` (returns the slot without checkout).
- V-1b: verify catches a silent checkout | runner | `C0824_VerifyQuarantinesSilentCheckout` | The process seam makes `checkout --force` exit 0 but do nothing. The lease must not return that directory: it is quarantined, and the lease retries on another slot. Red against the stub: the stub returns the old HEAD.
- V-2: the keep-list is exact | runner | `C0824_WarmResetWipesEveryNonKeepListClass` | Planted by A: `.claude/settings.local.json`, `.antiphon/inbox/m.md`, `.antiphon/checkpoints/x/report.json`, `bin-c1/x`, `src/P/bin-ws/y`, a nested repository `nested/.git`, `config.worktree` (with `extensions.worktreeConfig`), `CHERRY_PICK_HEAD` from a real conflicting cherry-pick, `refs/worktree/x`, `refs/bisect/bad`, `.playwright/`, `TestResults/t.trx`, an ignored `*.log`, an untracked file. After the release + lease of B, every one is absent. `src/P/bin/notes.txt` (inside the keep-list) is **present**, which is the accepted Q-3 behaviour, asserted so a change is visible. Red: stub reset does nothing.
- V-27: native output survives by default | runner | `C0824_WarmResetKeepsNativeOutputByteAndMtime` | Plant `src/P/bin/Debug/a.dll`, `src/P/obj/project.assets.json`, `node_modules/x/i`, `client/dist/i.js` with mtime `2026-01-01`. After the release + lease, `Tree()` entries for those paths are byte-identical and mtimes unchanged. The recorded git argv of the whole lease contains no `OutputPath`, `-o` or `bin-ws`. Red: a stub that runs `clean -ffdx` without `-e`.
- V-11: a Free slot is detached at a published SHA with no task refs | runner | `C0824_FreeSlotDetachedAtPublishedShaWithNoTaskRefs` | After a release: `symbolic-ref -q HEAD` exits 1; HEAD == fetched `origin/master` (here `origin/main` via `publishedBranch`); `merge-base --is-ancestor HEAD origin/main` = 0; `reflog show HEAD` empty; `refs/heads/<A>` and `refs/remotes/origin/<A>` absent; the admin dir holds only git's own files plus `antiphon-mirror-owner.json`; `FreeInvariant.Check` passes. Red: the stub leaves HEAD on the branch.
- V-38: evidence is retained before the wipe | runner | `C0824_EvidenceRetainedBeforeWipe` | `.antiphon/reports/r.md` and `.antiphon/checkpoints/c1/report.json` are copied byte-identically to `<allowed>/retained/<task>/<gen>/` **before** the clean (asserted with a phase-cut at `before-clean` that reads the retained root); `.antiphon/inbox/big.bin` is not retained; a cap override of 1 KiB truncates retention to the cap and records `evidence_truncated`. Red: stub has no retention.
- V-44: a reset never touches anything outside its slot | runner | `C0824_ResetNeverTouchesOutsideSlot` | Sibling files `work/worktrees/ws-02/keep.txt` (another slot), `work/worktrees/task-deadbeef/x` (legacy mirror) and `work/tmp-c723-x/y` (temp-root shape) are byte-identical after a release + lease of `ws-01`. Red: compile only (guards a mutant that scopes the clean to the parent).
- V-45: the pool stays bounded | runner | `C0824_PoolBoundedAcrossSequentialAndConcurrentLeases` | Capacity 2, headroom 2: 10 sequential lease/release cycles, then 4 concurrent leases. At most 4 `ws-*` directories, zero `task-*` directories, every lease distinct while concurrent. Red: the stub creates one directory per lease.
- V-46: a Review lease is the exact tree | runner | `C0824_ReviewLeaseIsExactTreeNeverSubjectBranch` | A Review task's lease (its own branch at the subject SHA) gives an exact clean tree. A lease naming the subject branch while it is leased elsewhere is refused (V-7b mechanism). Red: as V-1.
- V-12r: flag off, the runner keeps the mirror path | runner | `C0824_FlagOffRunnerKeepsTaskMirror` | `new PhoneHomeWorkspaceSlotSettings().Enabled` is false. With it false, a `task-deadbeef` mirror request creates exactly `work/worktrees/task-deadbeef` through `worktree add -B` (the same argv as the base, recorded through the seam), and no `workspace-slots/` ledger directory exists. Red: the setting does not exist (compile).

**CP-2 `C0824WorkspaceSlotCustodyTests`** (SessionRunner, portable)
- V-3a: an unpushed tip quarantines | `C0824_UnpushedTipQuarantines` | Task commits on its branch, no push, release. The directory is kept byte-identical, the ledger has `quarantinedDirectories[0]{class: custody, reason: unpushed}`, the slot re-homes to `ws-01-d2`, and HEAD in the old directory is still the commit. Red: the stub release always resets.
- V-3b: an unpushed commit reached only through the reflog quarantines | `C0824_UnpushedReflogOnlyCommitQuarantines` | The task pushes its branch tip, but made `checkout -b side`, committed, then `checkout <branch>` and deleted `side`. Only the HEAD reflog since the generation stamp reaches the commit. Release quarantines. Red: the stub; after green, the mutant that checks the tip only.
- V-4: dirt quarantines, bytes preserved, re-home | `C0824_DirtQuarantinesPreservesBytesAndRehomes` | A tracked modification + an untracked file. Old directory `Tree()` unchanged, `git worktree list --porcelain` shows it `locked` with reason `antiphon quarantine ws-01 custody dirty`, the next lease returns `ws-01-d2` clean at the new SHA, and the slot id is unchanged.
- V-6: a crash at each journal phase recovers | `C0824_CrashAtEachPhaseRecoversNeverDirty` `[Arguments("Leasing")]` `[Arguments("Leased")]` `[Arguments("Releasing")]` | `SlotPhaseCut` (new, like `BindingPhaseCut`) throws after the named intent is written. A new pool instance over the same ledger runs `RecoverAsync` (no server lease confirmed). It ends Free with the free invariant **or** Quarantined. For `Releasing` with a planted dirty file it must be Quarantined. A following lease never returns a directory with that file. Red: the stub has no recovery.
- V-6b: the lease re-verifies the free invariant | `C0824_LeaseReverifiesFreeInvariant` | After a clean release, the test writes `x.txt` into the Free slot (a late writer). The next lease quarantines that directory and returns another slot. Red: the stub trusts the ledger.
- V-7a: two concurrent leases never share a slot | `C0824_ConcurrentLeasesNeverShareSlot` | `SlotPhaseCut("picked")` blocks the first lease on a barrier after it picks; a second lease runs. Distinct slot ids; ledger has two Leased entries. Red: the stub has no lock (deterministic through the barrier, not timing).
- V-7b: the same branch in a second slot is refused | `C0824_BranchCheckedOutInAnotherSlotRefused` | Task A's branch is leased in `ws-01`. A lease naming the same branch is refused typed `branch_checked_out` and creates nothing. Red: git itself refuses `checkout -B` of a checked-out branch only in some forms, so the test asserts the typed refusal and an unchanged ledger.
- V-8: `RemoveAsync` refuses a slot path | `C0824_RemoveAsyncRefusesSlotManaged` `[Arguments("ws-01")]` `[Arguments("ws-01-d2")]` | `RemoveAsync(new(path, Force: true))` throws `PhoneHomeAdmissionException` whose detail is `slot_managed`. The lessee's `Tree()` is unchanged. Red today: `RemoveAsync` removes it (a **behavioural red at base**, before any slot code: the test plants a real `ws-01` worktree with plain git).
- V-9: a stale-generation release is refused | `C0824_StaleGenerationReleaseRefused` | Lease A (gen 1), release, lease B (gen 2). A release `(ws-01, gen 1, A)` is refused `slot_generation_mismatch`, and B's tree and ledger are unchanged. A repeated `(ws-01, gen 2, B)` after success is idempotent success with no second reset (`Tree()` + call count unchanged).
- V-47: a leased slot is never reset by another task | `C0824_LeasedSlotNeverResetByForeignRelease` | A release `(ws-01, gen 2, otherTask)` is refused. This is a separate guard from V-9: the task id is checked too.
- V-48: `PublishAsync` resolves by ledger | `C0824_PublishResolvesSlotByLedger` | `PublishAsync(ws-01 path, branch A)` while `ws-01` is leased to B is refused; for B it is accepted. Red today: the leaf `NamePattern` check refuses every `ws-*` (a behavioural red for the accept half).
- V-49: the runner wire decode | `C0824_LeaseThroughDispatcherFrame` | One `WorkspaceMirror` frame with `Cold: false` and a slot-feature binding through a real `PhoneHomeCommandDispatcher` → a Result carrying `SlotId`, `Generation`, and a path under `worktrees/ws-`. Red: the contract fields do not exist.

**CP-3 `C0824WorkspaceSlotProcessScanTests`** (SessionRunner, linux-native, real `/proc`)
- V-5a: a cwd holder defers, then quarantines | `C0824_CwdHolderDefersThenQuarantines` | `sleep 30` with cwd in `ws-01`. Release → `Deferred(1)`; the clock advances 1, 2, 4 minutes → attempts 2 and 3 deferred; the 4th evaluation quarantines with `pid` and `argv0 == "sleep"` and re-homes. The next lease gets `ws-01-d2`. Red: the stub probe sees nothing.
- V-5b: an fd holder defers, then quarantines | `C0824_OpenFdHolderDefersThenQuarantines` | `tail -f ws-01/held.txt`, cwd outside. Same sequence. Separate guard (fd scan).
- V-5c: a holder that exits is released normally | `C0824_HolderExitsBeforeRetryReleasesNormally` | The child is killed after `Deferred(1)`; the next attempt releases to Free with no quarantine. This guards against a mutant that quarantines on the first sighting.

**CP-7 `C0824WorkspaceSlotOverflowTests`** (portable, fake clock, fake disk probe)
- V-31a: `C0824_AllLeasedGrowsOverflowSlot`. With 4 leased and none Releasing, a lease creates `ws-05` with `createdAs: overflow`, the creating task and time; attention record `workspace_slot_overflow` once.
- V-31b: `C0824_ReleasingSlotWaitsBeforeGrowing` `[Arguments("completes")]` `[Arguments("expires")]`. One slot is Releasing. The lease enters the wait (seam). "completes": the release finishes before 10 s of fake time, so the lease reuses that slot and no `ws-05` exists. "expires": the clock advances 10 s, so `ws-05` is created.
- V-32a: `C0824_OverflowCapRefuses`. At target + `MaxOverflowSlots` the lease refuses `workspace_slot_unavailable{bound: overflow_cap}`; the directory count and ledger are unchanged.
- V-32b: `C0824_DiskBudgetRefuses` `[Arguments("free_floor")]` `[Arguments("pool_budget_with_quarantine")]`. Free bytes below `max(20 GiB, 10%) + 2×3 GiB`; or the pool total including a quarantined directory's bytes would exceed `PoolDiskBudgetGiB`. Refuses `{bound: disk_budget}` and creates nothing.
- V-33a: `C0824_IdleOverflowRetiredGuarded`. An overflow slot Free and idle 72 h + 1 s above target is removed through `git worktree remove` (argv recorded; no `rm`), the highest-numbered first, after a free-invariant re-check.
- V-33b: `C0824_LeasedRecentOrQuarantinedNeverRetired`. A leased slot, a Free slot idle 71 h, a base slot at target and a quarantined directory all survive maintenance.
- V-50: `C0824_SlotNumberNeverReusedWhileDirectoryExists`. After retiring `ws-06` while `ws-05`'s quarantined `ws-05` directory still exists, the next overflow is `ws-07`, not `ws-05`.

**CP-8 `C0824WorkspaceSlotRehomeTests`** (portable, injected failures)
- V-34: `C0824_InjectedFsFailureQuarantinesAndRehomes` `[Arguments("EACCES")]` `[Arguments("ENAMETOOLONG")]` `[Arguments("EBUSY")]`. The `clean` in the release reset fails with that stderr. The old directory still exists, is `worktree lock`ed with the reason, and the ledger + attention `workspace_slot_rehomed` carry the exact absolute path, class `filesystem` and raw error ≤ 4 KiB. `ws-01-d2` exists at the published SHA, the slot id is `ws-01`, the lease generation continues (next lease = gen+1), and the next lease returns `ws-01-d2`.
- V-34b: `C0824_IdentityBrokenDirectoryRehomes`. The `.git` file in `ws-01` was deleted (CARD-1448 shape). Lease-time verify quarantines and re-homes.
- V-34c: `C0824_SharingViolationViaFileSystemSeam`. The `IWorkspaceSlotFileSystem` fake throws `IOException` (sharing violation HResult) on the evidence copy. The slot re-homes; no evidence is lost (the source stays in the quarantined directory).
- V-35a: `C0824_ReplacementFailureMarksOnlySlotFailed`. `worktree add` fails for `ws-01-d2` and `ws-01-d3`. The slot becomes `Failed` with `workspace_slot_rehome_failed`, while `ws-02` and an overflow still lease.
- V-35b: `C0824_RehomeLoopGuardRetiresSlot`. Four re-homes in 24 h of fake time retire the slot id; a later lease uses other slots.
- V-51: `C0824_QuarantinedDirectoryBoundHoldsPool`. Five open quarantined directories → the lease refuses `workspace_slot_pool_degraded`.
- V-52: `C0824_QuarantineReleaseDispositions` `[Arguments("Pushed")]` `[Arguments("AcceptLoss")]` `[Arguments("Removed")]`. Pushed refuses while still unpushed and removes once pushed. AcceptLoss writes `git diff`, the untracked list and the patch to the retained root **before** the guarded removal. Removed retries the removal and records the outcome.

**CP-9 `C0824WorkspaceSlotColdResetTests`** (portable)
- V-28a: `C0824_ColdLeaseWipesNativeOutput`. A lease with `Cold=true` leaves no `bin/`, `obj/`, `node_modules/` or `client/dist/`.
- V-28b: `C0824_OperatorColdResetFreeSlotWipes`. A request `{slots:["ws-01"], requestId, reason}` on a Free slot runs `Free → ColdResetting → Free` under the lock; the ledger records the request id, requester and reason; native output is gone; a repeat request id is a no-op.
- V-28c: `C0824_LeaseWithoutColdKeepsNative`. The control: a plain lease keeps it (pairs with V-27).
- V-29: `C0824_ColdRequestOnLeasedSlotPendsUntilRelease` `[Arguments("operator")]` `[Arguments("schedule")]`. The lessee's `Tree()` (including `bin/`) is unchanged and the ledger has `ColdResetPending`. At release the cold reset runs **before** `Free` (a phase-cut at `free` sees no `bin/`).
- V-30a: `C0824_WeeklyBoundaryMarksOnce`. The fake clock crosses Sunday 02:00 UTC; three maintenance ticks mark every slot pending exactly once.
- V-30b: `C0824_ScheduleOffMarksNone`.
- V-30c: `C0824_MissedBoundaryRunsOnceAtStart`. A pool constructed after a boundary with `lastScheduledColdReset` before it marks once at start, and not again at the next tick.
- V-53: `C0824_ColdResetRemovesDeletedProjectResidue` (§3.8 hazard). Task A's SHA has `src/Q/Q.csproj` with `src/Q/bin/Q.dll`. The next SHA deletes project Q. A warm reset keeps `src/Q/bin/Q.dll` (asserted: documented residue). A cold reset removes it.

**CP-10 `C0824WorkspaceSlotNativeRehomeTests`** (linux-native)
- V-36: `C0824_Real0555DirectoryRehomes`. The real `git clean` exits 1 (measured above). The slot re-homes to `ws-01-d2`, the old directory survives with `ro/sub/f`, and the ledger error contains `Permission denied`. Fails loudly as uid 0.

**CP-4 `C0824WorkspaceSlotServerTests`** (Antiphon.Tests.Application)
- V-10a: `C0824_ReleasedTaskCoordinatesNotAmbiguous`. Task A (terminal, released: path cleared, slot history `ws-01`/gen 1) and live task B (`ws-01`/gen 2 at the same path string). `AmbiguousOwnershipAsync` for B → false. With A's path **not** cleared but gen 1 ≠ 2 → still false. Two live tasks with the same `(slot, gen)` → true. Red today: path equality makes the second case true.
- V-10b: `C0824_LegacyRemovalTreatsSlotManagedAsRemoved` `[Arguments("retirement")]` `[Arguments("drain-rearm")]` `[Arguments("completed-card")]`. The scripted peer answers `slot_managed`. `RemoteWorktreePath` is cleared, `RemoteWorktreeResidue` is null, no warning event, and no further remove request is sent.
- V-54: `C0824_LandCleanupRecordsSlotManaged`. For a slot-bound task, `AgentTaskLandingProtocol.CleanupAsync` records `slot-managed` and sends no remove.
- V-12: `C0824_FlagOffMirrorRequestUnchanged` `[Arguments("flag-off")]` `[Arguments("runner-lacks-WorkspaceSlotV1")]`. The serialized `WorkspaceMirror` payload has exactly the base property set (`branch, sha, name, repository, binding`), and `name == task-<8hex>`: no `cold`, `slotId` or `generation`. The stored path is the runner's `task-<8hex>`. The `Enabled` default is false.
- V-13: `C0824_SlotUnavailableHoldsWithoutFallback` `[Arguments("overflow_cap")]` `[Arguments("disk_budget")]`. The task stays Queued; one `Held` event names `workspace_slot_unavailable` and the bound; attention is raised; after two more ticks no `task-<id>` mirror request is ever sent.
- V-37a: `C0824_ColdWorkspacePersistsAndLeaseCarriesCold`. `WorkspaceColdReset=true` → the lease payload has `cold: true`; false → `cold: false`.
- V-37b: `C0824_OperatorColdResetRouteRecordsAndForwards`. The route records the requester and reason, forwards `["ws-03","ws-01"]` unchanged (order and set), and `"all"` as `"all"`.
- V-55: `C0824_ReleaseReconcilerRequiresAllConditions` `[Arguments("not-terminal")]` `[Arguments("seat-occupied")]` `[Arguments("sync-debt-pending")]` `[Arguments("sync-debt-held")]` `[Arguments("live-follow-up")]` `[Arguments("all-hold")]`. Only `all-hold` sends a release with `(slotId, generation, taskId)`. A Blocked task never sends one.
- V-56: `C0824_BaseSelectionIgnoresSlotHistory` `[Arguments("StartRef")]` `[Arguments("BaseTask")]` `[Arguments("Auto")]`. The lease payload `sha` equals `WorktreeBaseResolver`'s result for that dispatch, whatever SHA the slot last held (the slot history is set to an unrelated SHA on the runner rig).
- V-39: `C0824_ContinueResumeRefusedForSlotCwd` (Q-1 default). A Claude resume with `AgentSessionResumeMode.Continue` for a slot-bound task is refused typed `continue_in_shared_workspace` (or mapped to `--resume <id>` if Code chooses Q-1's alternative). No launch argv contains `--continue`. Red today: argv contains `--continue`.
- V-40..V-43: the delivery tests above (`C0824_LeaseRoundTripRealRunner`, `C0824_BusyRunnerLeaseSerialisedNotDuplicated`, `C0824_LeaseRecoveryAtEachHandoff` ×3, `C0824_ReleaseRoundTripRealRunner` ×2 with `[Arguments("direct")]` `[Arguments("reply-lost")]`).

### Guards the regression

- R-1: the flag-off mirror path and its server reaction are unchanged | CP-5 `RemoteWorkspacePreparerTests` (18 methods), all green; decisive: `A_process_restart_re_arms_the_mirror_and_the_row_is_written_once` and `C1032_CaptureLostReplyRecovery`.
- R-2: CARD-1032 binding is unchanged | CP-6 `RunnerWorkspaceBindingTests` (20 executions); decisive: `C1032_LegacyUnboundPathOnlyContract` and `C1032_ForeignOwnerAdoptionRefused`.
- R-3: legacy mirror create, remove and publish are unchanged | CP-16 `RunnerWorkspaceServiceTests` (35 methods; `RunnerWorkspaceService.cs` changes in three methods).
- R-4: the NativeLinuxOnly policy pins the new linux-native methods | CP-15 `NativeLinuxPolicyWitnessTests` (3). Check at base first: the memory of earlier inherited reds names bases `cbe3e6b42`/`2051d3271`, not this one.
- R-5: legacy seat-release ambiguity is unchanged | CP-17 `TerminalRunnerSeatReleaseTests/Legacy_ambiguous_answer_requires_fresh_absence_without_another_release`.
- R-6: `scripts/workspace-slots.ps1` is ASCII-only and parses | a Code-stage command row folded into CP-15's run notes: `pwsh -NoProfile -Command "[System.Management.Automation.Language.Parser]::ParseFile(...)"` plus a non-ASCII byte scan. Not a TUnit row; reported as executed n/a.

### Brief negative and safety checklist → witnesses

| Brief item | Witness |
|---|---|
| a dirty slot from a crashed task is never handed over | V-6, V-6b, V-4 |
| two concurrent tasks never share a slot | V-7a, V-7b, V-45 |
| a leased slot is never reset or cold-reset | V-9, V-47, V-29 |
| a reset failure quarantines the directory and re-homes without deleting the old one | V-34, V-34b, V-34c, V-36 |
| the replacement failing too marks only that slot Failed | V-35a (and V-35b loop guard) |
| overflow limits (count, disk) hold | V-32a, V-32b, V-51 |
| native bin/obj survive a normal reset | V-27, V-28c |
| a cold reset wipes ignored build output only when asked/scheduled/pre-rc, never on a leased slot | V-28a/b, V-29, V-30a..c; pre-rc = the operator route V-37b (the rc skill step is a doc change) |
| a task never sees another task's tracked/untracked files, `.antiphon` scratch or temp roots | V-1, V-2, V-38, V-44 (temp roots outside the tree are untouched: owned by CARD-0804/0805) |
| StartRef/-BaseTask/Auto when the slot's previous state is unrelated | V-56 (server) + V-1 (runner) |
| review tasks get an exact tree | V-46 (on its own branch, per plan §3.3; see Q-2 on "detached") |
| flag default OFF, flag off byte-identical | V-12, V-12r, R-1..R-3 |
| remote-branch deletion only after confirmed publication; local mirror refs cleaned | S2 (V-14..V-19, not this slice). The S1 part, runner-local refs deleted at release, is V-11 |

### Guard inventory

Every safety-critical guard, split where it can be bypassed independently, maps 1:1 to a PC.

| G | Plan ref + guard | PC |
|---|---|---|
| G-1 | §3.3.5 `checkout --force` to the exact SHA/branch | PC-1 |
| G-2 | §3.3.6 post-checkout verify (HEAD, symbolic-ref, porcelain) | PC-2 |
| G-3 | §3.4 `clean -ff` (nested repos) | PC-3 |
| G-4 | §3.4 keep-list exact (`-e` list not widened) | PC-4 |
| G-5 | §3.4 `-x` wipes ignored non-keep content (deny hook, `.antiphon`, `bin-*`) | PC-5 |
| G-6 | §3.4 sequencer abort/quit | PC-6 |
| G-7 | §3.4 `config.worktree` + `refs/worktree/*` + `refs/bisect/*` removal | PC-7 |
| G-8 | §3.4/H-3 keep-list contains native `bin/`/`obj/`/`node_modules/`/`client/dist/` | PC-8 |
| G-9 | §3.12 the default reset is warm | PC-9 |
| G-10 | §3.4 Free = detached at a published SHA | PC-10 |
| G-11 | §3.4 the task's local + tracking refs deleted | PC-11 |
| G-12 | §3.4 HEAD reflog expired at release | PC-12 |
| G-13 | §3.5 unpushed check: branch tip | PC-13 |
| G-14 | §3.5 unpushed check: reflog walk since the generation stamp | PC-14 |
| G-15 | §3.5 dirt → quarantine, never reset | PC-15 |
| G-16 | §3.5/D-15 custody quarantine re-homes the slot | PC-16 |
| G-17 | §3.3.3 lease-time free-invariant re-verify | PC-17 |
| G-18 | §3.5 crash recovery checks every non-Free slot | PC-18 |
| G-19 | §3.3 ledger lock around pick | PC-19 |
| G-20 | §3.3.4 branch-in-other-worktree scan | PC-20 |
| G-21 | §3.6 `RemoveAsync` refuses `slot_managed` | PC-21 |
| G-22 | §3.5 release generation check | PC-22 |
| G-23 | §3.5 release task-id check | PC-23 |
| G-24 | §3.6 `PublishAsync` by ledger | PC-24 |
| G-25 | §3.4 process scan: cwd | PC-25 |
| G-26 | §3.4 process scan: fd | PC-26 |
| G-27 | §3.5/D-11 linger retries bounded at 3, then quarantine | PC-27 |
| G-28 | §3.5 defer, not quarantine, on first sighting | PC-28 |
| G-29 | §3.2a grow overflow when no slot is Free | PC-29 |
| G-30 | §3.2a wait `OverflowWaitSeconds` for a Releasing slot | PC-30 |
| G-31 | §3.2a overflow count cap | PC-31 |
| G-32 | §3.2a volume free-floor check | PC-32 |
| G-33 | §3.2a pool budget includes quarantined directories | PC-33 |
| G-34 | §3.2a idle retirement only Free + idle + above target | PC-34 |
| G-35 | §3.2a retirement never removes a quarantined directory | PC-35 |
| G-36 | §3.2a slot number never reused while its directory exists | PC-36 |
| G-37 | §3.5a old directory kept (never deleted/moved) | PC-37 |
| G-38 | §3.5a old directory `worktree lock`ed | PC-38 |
| G-39 | §3.5a same slot id; lease generation continues | PC-39 |
| G-40 | §3.5a replacement at a new path `ws-NN-dK` | PC-40 |
| G-41 | §3.5a replacement failure → only that slot Failed | PC-41 |
| G-42 | §3.5a `MaxRehomesPerSlotPerDay` loop guard | PC-42 |
| G-43 | §3.5 `MaxQuarantinedDirectories` hold | PC-43 |
| G-44 | §3.5 AcceptLoss retains the diff before removal | PC-44 |
| G-45 | §3.5a identity-broken directory detected | PC-45 |
| G-46 | §3.12 `Cold=true` lease honoured | PC-46 |
| G-47 | §3.12 operator cold reset of a Free slot | PC-47 |
| G-48 | §3.12 cold never while leased (pending) | PC-48 |
| G-49 | §3.12 pending cold runs at release before Free | PC-49 |
| G-50 | §3.12 schedule marks once per boundary | PC-50 |
| G-51 | §3.12 schedule `Off` | PC-51 |
| G-52 | §3.12 missed-boundary catch-up | PC-52 |
| G-53 | §3.5a a real clean error is not swallowed | PC-53 |
| G-54 | §3.4 evidence retained before the wipe | PC-54 |
| G-55 | §3.4/H-5 evidence cap | PC-55 |
| G-56 | §3.4 reset scoped to its slot | PC-56 |
| G-57 | §5 runner flag off → legacy path | PC-57 |
| G-58 | §3.6 server ambiguity by `(slot, generation)` | PC-58 |
| G-59 | §3.6 retirement treats `slot_managed` as removed | PC-59 |
| G-60 | §3.6 drain re-arm treats `slot_managed` as removed | PC-60 |
| G-61 | §3.7 land cleanup `slot-managed` | PC-61 |
| G-62 | §6 settings default `Enabled=false` (server) | PC-62 |
| G-63 | §3.2/D-6 `workspace_slot_unavailable` holds, no fallback | PC-63 |
| G-64 | §3.12 `-ColdWorkspace` carried on the lease | PC-64 |
| G-65 | §3.12 operator route forwards the slot list unchanged | PC-65 |
| G-66 | D-10 release needs terminal | PC-66 |
| G-67 | D-10 release needs the seat free | PC-67 |
| G-68 | D-10 release needs sync debt not Pending/Held | PC-68 |
| G-69 | D-10 release needs no live follow-up | PC-69 |
| G-70 | §3.5 server clears the path only after a success response | PC-70 |
| G-71 | §3.3 lease replay by preparation returns the same slot | PC-71 |
| G-72 | §2 base SHA from the resolver, never from slot history | PC-72 |
| G-73 | §8 cwd-keyed `--continue` refused in a slot (Q-1) | PC-73 |
| G-74 | §3.3 runner lease serialized under `MutationLock` (no double lease when busy) | PC-74 |

guards=74, mapped=74, missing=0, duplicate PC maps=0.

### Positive controls

Each PC: break the guard with a compiling one-line defect; expect the named method red at the named
assertion. Mutation runs break/red/restore/green after the land. Code runs the V/R rows. Review judges
before the land. Filters are method-scoped (`/*/<ns>/<Class>/<Method>`). A method with `[Arguments]` runs
all its cases.

| PC | Break | Expect red |
|---|---|---|
| PC-1 | `checkout --force -B` → `checkout -B` | `C0824_LeaseResetsToExactShaFromUnrelatedState` at porcelain empty |
| PC-2 | delete the post-checkout verify call | `C0824_VerifyQuarantinesSilentCheckout` at "returned directory != quarantined one" |
| PC-3 | `-ffdx` → `-fdx` | `C0824_WarmResetWipesEveryNonKeepListClass` at `nested/` absent |
| PC-4 | add `-e '**'` to the keep-list | same method, at the untracked-file absent assertion |
| PC-5 | `-ffdx` → `-ffd` | same method, at `.claude/settings.local.json` absent |
| PC-6 | skip the sequencer abort | same method, at `CHERRY_PICK_HEAD` absent |
| PC-7 | skip the `refs/worktree`/`config.worktree` cleanup | same method, at `refs/worktree/x` absent |
| PC-8 | drop `-e '**/bin/'` | `C0824_WarmResetKeepsNativeOutputByteAndMtime` at the `bin/Debug/a.dll` hash |
| PC-9 | default `cold = true` | same method, at the `obj/project.assets.json` present |
| PC-10 | park with `checkout <branch>` instead of `--detach` | `C0824_FreeSlotDetachedAtPublishedShaWithNoTaskRefs` at `symbolic-ref` exit 1 |
| PC-11 | skip `update-ref -d` of the task refs | same method, at `refs/heads/<A>` absent |
| PC-12 | skip `reflog expire` | same method, at reflog empty |
| PC-13 | treat the tip as published unconditionally | `C0824_UnpushedTipQuarantines` at quarantined count 1 |
| PC-14 | walk only the tip, not the reflog | `C0824_UnpushedReflogOnlyCommitQuarantines` at quarantined count 1 |
| PC-15 | classify dirt as resettable | `C0824_DirtQuarantinesPreservesBytesAndRehomes` at old `Tree()` unchanged |
| PC-16 | skip the re-home after a custody quarantine | same method, at next lease path `ws-01-d2` |
| PC-17 | skip the lease-time re-verify | `C0824_LeaseReverifiesFreeInvariant` at returned slot != planted one |
| PC-18 | recovery marks Free without the check | `C0824_CrashAtEachPhaseRecoversNeverDirty` (Releasing case) at Quarantined |
| PC-19 | remove the ledger lock | `C0824_ConcurrentLeasesNeverShareSlot` at distinct slot ids |
| PC-20 | skip the `worktrees/*/HEAD` scan | `C0824_BranchCheckedOutInAnotherSlotRefused` at `branch_checked_out` |
| PC-21 | delete the `slot_managed` refusal | `C0824_RemoveAsyncRefusesSlotManaged` at the thrown detail |
| PC-22 | ignore the generation | `C0824_StaleGenerationReleaseRefused` at `slot_generation_mismatch` |
| PC-23 | ignore the task id | `C0824_LeasedSlotNeverResetByForeignRelease` at the refusal |
| PC-24 | accept any `ws-*` leaf without the ledger | `C0824_PublishResolvesSlotByLedger` at foreign-publish refused |
| PC-25 | drop the cwd scan | `C0824_CwdHolderDefersThenQuarantines` at `Deferred(1)` |
| PC-26 | drop the fd scan | `C0824_OpenFdHolderDefersThenQuarantines` at `Deferred(1)` |
| PC-27 | `LingerRetries` loop never ends in quarantine | `C0824_CwdHolderDefersThenQuarantines` at quarantined after attempt 4 |
| PC-28 | quarantine on the first sighting | `C0824_HolderExitsBeforeRetryReleasesNormally` at Free, no quarantine |
| PC-29 | hold instead of growing | `C0824_AllLeasedGrowsOverflowSlot` at `ws-05` exists |
| PC-30 | skip the Releasing wait | `C0824_ReleasingSlotWaitsBeforeGrowing` ("completes") at no `ws-05` |
| PC-31 | remove the count cap | `C0824_OverflowCapRefuses` at `overflow_cap` |
| PC-32 | skip the free-floor check | `C0824_DiskBudgetRefuses` ("free_floor") at `disk_budget` |
| PC-33 | exclude quarantined bytes from the pool total | `C0824_DiskBudgetRefuses` ("pool_budget_with_quarantine") at `disk_budget` |
| PC-34 | retire on count alone | `C0824_LeasedRecentOrQuarantinedNeverRetired` at the leased slot present |
| PC-35 | include quarantined directories in retirement | same method, at the quarantined directory present |
| PC-36 | next number = `max(live slot)+1` | `C0824_SlotNumberNeverReusedWhileDirectoryExists` at `ws-07` |
| PC-37 | delete the old directory after quarantine | `C0824_InjectedFsFailureQuarantinesAndRehomes` at old directory exists |
| PC-38 | skip `worktree lock` | same method, at `locked` in `worktree list --porcelain` |
| PC-39 | mint a new slot id on re-home | same method, at slot id `ws-01` |
| PC-40 | reuse the old path for the replacement | same method, at path `ws-01-d2` |
| PC-41 | rethrow a replacement failure out of the pool | `C0824_ReplacementFailureMarksOnlySlotFailed` at `ws-02` leases |
| PC-42 | drop the loop guard | `C0824_RehomeLoopGuardRetiresSlot` at the slot retired |
| PC-43 | drop the `MaxQuarantinedDirectories` check | `C0824_QuarantinedDirectoryBoundHoldsPool` at `workspace_slot_pool_degraded` |
| PC-44 | remove before writing the AcceptLoss patch | `C0824_QuarantineReleaseDispositions` ("AcceptLoss") at the patch exists before the removal |
| PC-45 | skip the `.git`-file identity check | `C0824_IdentityBrokenDirectoryRehomes` at quarantined |
| PC-46 | ignore `Cold` on the lease | `C0824_ColdLeaseWipesNativeOutput` at `bin/` absent |
| PC-47 | operator request on a Free slot only marks pending | `C0824_OperatorColdResetFreeSlotWipes` at `bin/` absent |
| PC-48 | run the cold reset immediately on every slot | `C0824_ColdRequestOnLeasedSlotPendsUntilRelease` at the lessee `bin/` present |
| PC-49 | clear the pending flag at release without running it | same method, at the `free` phase-cut no `bin/` |
| PC-50 | mark on every maintenance tick | `C0824_WeeklyBoundaryMarksOnce` at mark count 1 |
| PC-51 | ignore `Off` | `C0824_ScheduleOffMarksNone` at mark count 0 |
| PC-52 | skip the missed-run catch-up | `C0824_MissedBoundaryRunsOnceAtStart` at mark count 1 |
| PC-53 | ignore the `clean` exit code | `C0824_Real0555DirectoryRehomes` at re-homed |
| PC-54 | wipe before retaining | `C0824_EvidenceRetainedBeforeWipe` at the `before-clean` phase-cut |
| PC-55 | ignore the cap | same method, at `evidence_truncated` |
| PC-56 | scope the clean to the parent directory | `C0824_ResetNeverTouchesOutsideSlot` at `ws-02/keep.txt` hash |
| PC-57 | the runner uses the pool regardless of the flag | `C0824_FlagOffRunnerKeepsTaskMirror` at path `task-deadbeef` |
| PC-58 | compare paths, not `(slot, gen)` | `C0824_ReleasedTaskCoordinatesNotAmbiguous` at the uncleared-path case false |
| PC-59 | retirement keeps the path on `slot_managed` | `C0824_LegacyRemovalTreatsSlotManagedAsRemoved` ("retirement") at path null |
| PC-60 | drain re-arm warns on `slot_managed` | same method ("drain-rearm") at no warning event |
| PC-61 | land cleanup sends a guarded remove | `C0824_LandCleanupRecordsSlotManaged` at the remove count 0 |
| PC-62 | default `Enabled = true` | `C0824_FlagOffMirrorRequestUnchanged` ("flag-off") at the property set |
| PC-63 | fall back to `task-<id>` on refusal | `C0824_SlotUnavailableHoldsWithoutFallback` at the mirror requests named `task-` = 0 |
| PC-64 | drop `Cold` from the lease request | `C0824_ColdWorkspacePersistsAndLeaseCarriesCold` at `cold: true` |
| PC-65 | forward `"all"` for any list | `C0824_OperatorColdResetRouteRecordsAndForwards` at the list equality |
| PC-66 | drop the terminal check | `C0824_ReleaseReconcilerRequiresAllConditions` ("not-terminal") at release count 0 |
| PC-67 | drop the seat check | same ("seat-occupied") |
| PC-68 | drop the sync-debt check | same ("sync-debt-pending") |
| PC-69 | drop the follow-up check | same ("live-follow-up") |
| PC-70 | clear the path before the response | `C0824_ReleaseRoundTripRealRunner` ("reply-lost") at the path kept until the resend succeeds |
| PC-71 | replay mints a new lease | `C0824_LeaseRecoveryAtEachHandoff` ("reply-lost") at one ledger lease |
| PC-72 | use the slot's parked SHA as the base | `C0824_BaseSelectionIgnoresSlotHistory` at `sha == resolver` |
| PC-73 | allow `--continue` | `C0824_ContinueResumeRefusedForSlotCwd` at no `--continue` |
| PC-74 | bypass `MutateAsync` for the lease op | `C0824_BusyRunnerLeaseSerialisedNotDuplicated` at one lease, ledger order |

All 74 PCs run in Mutation. None is justified away.

### Out of scope

- S2 branch deletion (V-14..V-19), S3 Windows (V-20..V-23), S4 migration (V-24..V-26), S5 desktop halves.
- Process holders owned by another uid: the scan is scoped to the runner uid by design, and a test cannot
  create a foreign uid on server2 without root. The design accepts this; S3/the canary watch it.
- Temp roots outside the tree (`/tmp/c723-*`): they stay with the CARD-0804/0805 owners. S1 asserts only
  that a reset never touches them (V-44).
- The real 9,290-file tree reset p95 (≤ 15 s), cold p95 (≤ 60 s) and overflow creation: these are canary
  measurements (plan §Performance), not CP rows.
- The pre-rc cold-reset step in `.claude/skills/antiphon-rc-qualification/SKILL.md` and `release-gates.md`:
  docs. V-37b covers the route they call.

### Implementation steps (red first, then green)

1. Contracts + settings + empty types: `PhoneHomeWorkspaceSlotSettings` (all keys, `Enabled=false`), the
   contract fields, `WorkspaceSlotResetPolicy`, the seams `IWorkspaceSlotFileSystem`, `IWorkspaceSlotProcessProbe`,
   `IWorkspaceSlotDiskProbe`, and a `RunnerWorkspaceSlotPool` **stub** (lease returns the first directory
   unchanged; release no-op). Commit with all CP-1/2/7/8/9 test classes and `C0824SlotScratch` → **red
   commit 1** (behavioural reds against the stub; V-8 and V-48 red against base behaviour). Policy rows +
   the CP-3/CP-10 classes in the same commit.
2. Green 1: ledger + lease (pick, lock, branch scan, checkout, verify, stamp) and warm reset + release +
   evidence retention → CP-1, CP-2 green. Commit, then run the run A subset.
3. Green 2: process probe + linger → CP-3. Overflow + idle retirement → CP-7.
4. Green 3: re-homer + quarantine bound + quarantine release → CP-8, CP-10.
5. Green 4: cold reset (lease flag, operator, schedule, pending) → CP-9. Then run A in full.
6. Red commit 2 (server): `C0824WorkspaceSlotServerTests` + `C0824SlotTransportWorld`, EF migration
   stub, the `delegate.ps1 -ColdWorkspace` param → red.
7. Green 5: `RemoteWorkspaceService/Preparer` lease fields, hold reason, the reconciler
   `WorkspaceSlotReleaseService`, the legacy consumers (§3.6), the cold-reset route, the `Continue` refusal
   → CP-4; then run B (CP-4, CP-5, CP-17).
8. Docs (session-runtime-invariants, docker-stack, ops-http, rc skill, release-gates) and
   `workspace-slots.ps1` (R-6). Final run A + run B at the tip.

## Performance budget

No test ran here. Sources: the plan's cf8b6982 receipts (SessionRunner.Tests build p95 135 s; Antiphon.Tests
build p95 579 s, loaded server2), the measured scratch git probes above (each git call < 50 ms on a
2-file repo), and existing comparable classes (`RunnerWorkspaceBindingTests`: about 10 git spawns per
body). Estimates: SessionRunner bodies 1–3 s (re-home and crash cases up to 5 s: two worktrees);
linux-native process bodies 2–4 s; server bodies 2–5 s (schema + socket); real-runner round trips 4–8 s.
- **Per-test `[Timeout]`.** 30 s provisional for SessionRunner and 60 s for the V-40..V-43 transport tests
  (about 2x a provisional p95 of 15/30 s under load). Code records per-test durations from the first green
  TRX and sets ≈ 2x measured p95 (floor 10 s). The child-process bound is 10 s; the linger sequence is
  fake-clock driven.
- **Shared setup.** One `C0824SlotScratch` per test (no cross-test state, as the ledger is per root). No
  AppHost or container. `TestDbFixture` once per assembly for CP-4.

### Checkpoints

S1 runs as **two checkpoint runs**: run A `--rows CP-1,CP-2,CP-3,CP-6,CP-7,CP-8,CP-9,CP-10,CP-15,CP-16`
(SessionRunner, two builds) and run B `--rows CP-4,CP-5,CP-17` (Antiphon.Tests, one build). These rows
replace the plan's S1 rows. CP-15..CP-17 are new regression rows; CP-11..CP-14 stay with their slices.
`Min` = executed TUnit floor (`-MinExecuted`).

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c0824-s1r/ | portable-slot-reset | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotResetTests/* | V-1, V-1b, V-2, V-11, V-12r, V-27, V-38, V-44, V-45, V-46 | class, 0 failed/skipped | 10 | 4 | true |
| CP-2 | S1 | CP-1 | portable-slot-custody | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotCustodyTests/* | V-3a, V-3b, V-4, V-6, V-6b, V-7a, V-7b, V-8, V-9, V-47, V-48, V-49 | class, 0 failed/skipped | 15 | 2 | true |
| CP-3 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c0824-s1l/ | linux-native-slot-process | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotProcessScanTests/* | V-5a, V-5b, V-5c | class, 0 failed/skipped | 3 | 4 | true |
| CP-6 | S1 | CP-1 | portable-binding-regression | /*/Antiphon.SessionRunner.Tests/RunnerWorkspaceBindingTests/* | R-2 | existing class, 0 failed/skipped | 20 | 3 | true |
| CP-7 | S1 | CP-1 | portable-slot-overflow | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotOverflowTests/* | V-31a, V-31b, V-32a, V-32b, V-33a, V-33b, V-50 | class, 0 failed/skipped | 9 | 2 | true |
| CP-8 | S1 | CP-1 | portable-slot-rehome | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotRehomeTests/* | V-34, V-34b, V-34c, V-35a, V-35b, V-51, V-52 | class, 0 failed/skipped | 11 | 2 | true |
| CP-9 | S1 | CP-1 | portable-slot-cold-reset | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotColdResetTests/* | V-28a, V-28b, V-28c, V-29, V-30a, V-30b, V-30c, V-53 | class, 0 failed/skipped | 9 | 2 | true |
| CP-10 | S1 | CP-3 | linux-native-slot-rehome | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotNativeRehomeTests/* | V-36 | class, 0 failed/skipped | 1 | 2 | true |
| CP-15 | S1 | CP-1 | portable-native-policy-regression | /*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* | R-4 (+ R-6 notes) | existing class, 0 failed/skipped | 3 | 1 | true |
| CP-16 | S1 | CP-1 | portable-workspace-service-regression | /*/Antiphon.SessionRunner.Tests/RunnerWorkspaceServiceTests/* | R-3 | existing class, 0 failed/skipped | 35 | 3 | true |
| CP-4 | S1 | tests/Antiphon.Tests -> bin-c0824-s1s/ | portable-slot-server | /*/Antiphon.Tests.Application/C0824WorkspaceSlotServerTests/* | V-10a, V-10b, V-12, V-13, V-37a, V-37b, V-39, V-40..V-43, V-54, V-55, V-56 | class, 0 failed/skipped | 28 | 14 | true |
| CP-5 | S1 | CP-4 | portable-slot-regression | /*/Antiphon.Tests.Application/RemoteWorkspacePreparerTests/* | R-1 | existing class, 0 failed/skipped | 18 | 3 | true |
| CP-17 | S1 | CP-4 | portable-seat-ambiguity-regression | /*/Antiphon.Tests.Application/TerminalRunnerSeatReleaseTests/Legacy_ambiguous_answer_requires_fresh_absence_without_another_release | R-5 | method, 0 failed/skipped | 1 | 2 | true |

Min derivation (executions): CP-1 10 methods; CP-2 11 methods + 2 extra V-6 cases + 1 extra V-8 case = 14,
plus V-49 = 15; CP-3 3; CP-6 11 + 9 = 20; CP-7 7 + 2 extra = 9; CP-8 7 + 2 (V-34) + 2 (V-52) = 11; CP-9 8 + 1 =
9; CP-10 1; CP-15 3; CP-16 35 (floor; 1 `[Arguments]` line); CP-4 14 methods + V-10b 2 + V-12 1 + V-13 1 + V-55 5
+ V-56 2 + V-42 2 + V-43 1 = 28; CP-5 18 (floor); CP-17 1.

Class names are unique prefixes: `...SlotRehome...` vs `...SlotNativeRehome...`, `...SlotReset...` vs
`...SlotColdReset...`; `C0824SlotScratch`/`C0824SlotTransportWorld` have no `[Test]`. Code runs
`import --plan` on this table before the first run (memory: the tool refuses reused builds across After
groups; all rows share After S1).

### Cost

Labels: **estimated** throughout (no run here).
- Ordinary V/R floor (Code): run A = 4+2+4+3+2+2+2+2+1+3 = **25 min**; run B = 14+3+2 = **19 min**; total
  **44 min** of row time. The builds are inside the build-bearing rows (CP-1, CP-3, CP-4). Slot wait is
  reported separately. This is 8 min above the plan's 36 min, from the three regression rows (CP-15..17:
  6 min) and the larger CP-4 (+2). Saving considered: CP-3 reuses CP-1's build when one host runs both
  lanes (the import warning). That saves 2.3 min, but the plan keeps the lanes separate on purpose. Merging
  CP-15 into CP-16 is not possible (different classes, one exact filter per row).
- Positive-control floor (Mutation), method-scoped, incremental build per cycle:
  - SessionRunner PCs (PC-1..PC-57, 57 rows): red (incremental build ≈ 60 s + method run ≈ 10 s) + restore +
    green ≈ **2.5 min** each → **143 min**.
  - Server PCs (PC-58..PC-74, 17 rows): Antiphon.Tests incremental build ≈ 2.5–4 min → ≈ **7 min** each →
    **119 min**.
  - Setup: one cold build per assembly ≈ 2.3 + 9.7 min = **12 min**.
  - Total PC ≈ **274 min** serial. Batching: PCs in different files and methods can share one build. The
    candidate batches are the server-file PCs (PC-58..PC-61, four files) and Contracts-vs-pool pairs; that
    saves about 25 min. Sharding: 74 rows exceeds the 15–20 row threshold, so three worktree shards bring
    the wall time to about 100 min. Most runner PCs edit one file (`RunnerWorkspaceSlotPool.cs`), so they
    cannot batch.
- **Total** (setup + V/R + PC) ≈ 44 + 274 ≈ **318 min estimated**, about **146 min** wall time with
  batching and three shards.

### Completion and handoff

- guards=74, mapped=74, missing=0, duplicate PC maps=0; all 74 PCs run; Cost numeric (estimated).
- No unverifiable seam blocks Code. Q-1 to Q-4 below each have a default Code can proceed under.

## What a native (Windows) run must prove separately (S3), and what Linux cannot prove

- Restart Manager (`RmGetList`) attribution of a held `bin/` file (V-20). The Linux `/proc` scan says
  nothing about Windows handles.
- `ERROR_SHARING_VIOLATION` from real `git checkout`/`clean` on a locked `bin/*.dll`. On Linux an unlink of
  an open file succeeds, so V-34's "EBUSY" case is a seam only.
- `core.longpaths` + `\\?\` removal over 260 characters, and a real `Filename too long` that re-homes
  (V-21). Linux has no 260 limit, so `ENAMETOOLONG` here is injected.
- Read-only attribute and Defender interference with a reset. Windows `0555` has no equivalent; Windows
  needs its own "undeletable file" fixture (an open handle with `FileShare.None`).
- Desktop resets taking the repository mutation lease and the CARD-1379 fence (V-23). The runner repository
  has no land and no child journal.
- Windows git `status --ignored=matching` path casing and CRLF effects on the free invariant.

## Open questions for Code (each with a default)

- **Q-1 Claude `--continue` in a shared slot.** `AgentSessionResumeMode.Continue` adds `--continue`
  (`AgentSessionService.cs:2620`), which resumes the newest conversation in the cwd. That is the previous
  lessee's conversation in a slot. Default: refuse `Continue` for a slot-bound session with a typed reason
  (V-39). The alternative is to map it to `--resume <stored id>`. Codex/Grok are unaffected (id-based).
- **Q-2 "Review gets an exact detached tree" (brief) vs plan §3.3 (its own branch).** Default: the plan's
  semantics. A Review lease is on the Review task's own branch at the subject SHA, never the subject
  branch, with an exact clean tree (V-46). A truly detached Review lease would break `PublishAsync`.
- **Q-3 Untracked files inside a keep-list directory** (`src/P/bin/notes.txt`) survive a warm reset
  (measured). Default: accept (native output is per-slot by H-3). V-2 pins it so a change is deliberate;
  the cold reset removes it.
- **Q-4 Per-test timeouts.** Default 30 s (60 s transport) until the first green TRX, then ≈ 2x measured
  p95, recorded in the Code report.
- **Q-5 `GitProcessSpawnLimit` in SessionRunner.Tests** (the plan assumed it exists there; it does not).
  Default: use the existing `ProcessSpawnLimit` (Limit 1). Add no new limiter.
