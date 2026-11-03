# CARD-0824 S4a automatic desktop worktree release (on by default): test design

TestDesign task e814f8ee, 2026-11-03, server2 runner mirror (Linux), base master
`97e77f9f1fd6f71e2719bc661b48f8f89f129029`. Scope: slice S4a of the
[S4/S5 amendment](2026-11-03-card-0824-s4-s5-amendment.md) (§3.2, A-D-12, §8), with the operator decision
of 2026-11-03 on Q-3: **S4a ships ON** (`Enabled=true`, `Execute=true`, like CARD-1677 retention), with the
preview route, the kill switch and a per-tick cap; terminal ≥ 48 h; a system release through
`TaskWorktreeRetirementService`; no runner-mirror removal for runners listed in
`RunnerWorktreeRetention:Runners`; never a tree with dirty or unpushed work. That decision replaces
A-D-12's default (off) and the amendment's settings default `Enabled=false, Execute=false`. Format template:
the [CARD-1677 test design](2026-11-03-card-1677-test-design.md); settings shape: the
[S1c test design](2026-11-03-card-0824-s1c-test-design.md) T-D-1/T-D-2 findings. No production or test
source changed and nothing ran except the checkpoint import (§Checkpoints). Expected results below are
design, not executed evidence.

This design keeps the amendment's §3.2 design and its L-1..L-10 intent. It **adds five corrections**
(T-D-2..T-D-6) because the code read below shows that §3.2 as written would either retire almost nothing
(T-D-2), wedge on its first refused removal (T-D-4) or leave handoff and dependent hazards unstated
(T-D-5). Each is a stated default under the standing authority; the caller can override any before Code
(§Open questions).

## Code facts used by this design (read at the base, not run)

- **Release filing** (`TaskWorktreeRetirementService.ReleaseAsync`, `:72-185`) requires
  `NoFurtherWorkspaceUse`, a reason, the full source SHA equal to `rev-parse refs/heads/<branch>` in the
  repository (`:118-120`), the current `ConcurrencyToken`, terminal status, `CompletedAt` past
  `WorktreeResidue:MinSettledMinutes` (120), `Workspace=Worktree`, not Mutation, SourceLanding or
  repair-source, a unique ordinary owner with leaf `card-task-<id8>` (exact, `OrdinalIgnoreCase`) under
  `Git:WorktreeBasePath` (`:461-503`), a report or `MissingReportReviewed`, retrievable artifacts, resolved
  handoffs (a task with `NextStage` set needs a disposition, `:552-563`) and no recovery debt (a pending
  land request or **any** `AgentTaskLandings` row, `:543-550`). `CallerIdentity` is `"operator"` for a
  token-less caller (`:161`); there is no system identity. One active release per task attempt; a replay
  with an equal snapshot returns the same row (`:128-146`).
- **Retirement** (`TryRetireCoreAsync`, `:284-370`): repository mutation lease, one attempt row, a claim
  (workspace reservation), a live-consumer check, then a **command intent** committed through
  `IRetirementCommandJournal` **before** `IWorktreeManager.TryRemoveAsync(SettledTask)`. The runner mirror
  removal (`RemoveRemoteMirrorAsync`, `:364`) runs after the desktop removal, best effort, for every
  non-card-done retirement whatever its outcome.
- **One command per release, forever.** `GetOrCreateAttemptAsync` returns the latest attempt whenever it has
  a `CommandIntentId` (`:433-434`), and `TryRetireCoreAsync` returns `cleanup_command_slot_spent` for an
  attempt with an intent and `DirectoryRemoved is not true` (`:295-298`). No server code sets an attempt's
  `FinishedAt` (grep: only the migration). So **any** guarded-removal refusal after the intent (a dirty
  tree, a held file, a remote read failure) spends the release: every later `TryRetireAsync` returns
  `cleanup_command_slot_spent`, and `RevokeAsync` refuses (`revoke_after_intent`, `:193-196`).
- **Guarded removal** (`GuardedWorktreeRemoval.RemoveDirectoryAsync`, `:26-252`) for `SettledTask`:
  confinement (`ManagedRoot`, and `CanonicalDirectory(path) == path` for both the tree and the root,
  `:47-53`); authority (`AuthorityAsync`, `:313-348`: lease owned, retirement row matches exactly, and
  `ObserveRetirementAsync` proves the **retirement's `DestinationFullRef` on origin contains the source
  SHA**, else `remote_no_longer_contains_source`); registration present, not locked, not prunable, no
  nested registration; `InspectAsync(Full)` = clean `status --porcelain=v1 --untracked-files=all`
  (`LandingGit.cs:724-740`, `source_dirty`) and `HEAD == SourceSha`; the CARD-0665 ignored-content gate
  (protected content refuses `ignored_content_preserved`; allowlisted **evidence is retained** through
  `IWorktreeEvidenceRetention` before the second reading; reparse points refuse `ignored_reparse_point`);
  then move-aside (a rename; a handle or cwd inside the tree refuses it with nothing moved,
  `worktree_remove_failed`), unregister, no-follow delete; then `CompleteBranchAsync` deletes the **local**
  branch at the exact SHA. It never passes `--force` and never hands git the path.
- **The destination is the merge target.** `RequireUniqueOrdinaryOwnerAsync` sets
  `DestinationFullRef = DestinationAsync(repo, MergeTargetRef ?? Git:DefaultBranch)` (`:497-502`), so a
  release today needs the source **contained in origin master**. Landed Code tasks are already removed by
  the land's Publication cleanup (`AgentTaskLandingProtocol.cs:447`), and a rebasing land publishes a
  different commit. What is left on the desktop is mostly runner-task halves of unlanded stages (Plan,
  Investigate, TestDesign, Review, Debug), whose branch is on origin but not in master (S5-0 §1). Under the
  current destination S4a would retire only trees with no commit beyond their base.
  `LandingGit.DestinationAsync` (`:1168-1175`) accepts any `refs/heads/*` ref, so the task's own branch can
  be the destination with no new git primitive.
- **The residue sweep** (`WorktreeResidueSweepService.cs:186-249`) already calls `EvaluateEligibilityAsync`
  and `TryRetireAsync` for every task with an active release when `WorktreeResidue:Execute=true` (false in
  `server/appsettings.json:223`), and stamps a per-task cooldown cursor on a refusal (`StampTaskCooldown`).
  Its rows use Lane `SettledTask`.
- **CARD-1677 shapes reused:** `IOptionsMonitor` re-read before every request (`StopReason`,
  `RunnerWorktreeRetentionService.cs:306-315`: `tick_stopped_disabled`, `tick_stopped_execute_off`,
  `tick_budget_exhausted`); a re-read of the row before each send; a per-task send cursor with doubling
  backoff (`KeptRecheckMinutes`/`KeptRecheckMaxHours`); `\z`-anchored leaf patterns plus
  `WorkspaceRetentionNames.HasControlCharacter` (`char.IsControl` plus U+2028/U+2029) checked first; one
  `WorktreeResidueRun` per tick with a `Scope`; the operator-only preview route
  (`AgentTaskEndpoints.cs:143-155`); no migration (1677 D-8).
- **`RunnerWorktreeRetention:Runners` is a `List<string>`** whose shipped `["server2"]` lives only in
  `server/appsettings.json` (`RunnerWorktreeRetentionSettings.cs:22-26`). S4a reads it; it adds no list.
- **The remote mirror removal** goes through `RemoteWorkspaceService.RemoveMirrorAsync` →
  phone-home `WorkspaceRemove` (`RemoteWorkspaceService.cs:1021-1046`), and already returns without a
  request for a runner slot that is not released (`:1029-1030`).
- **Test fixtures at the base.** `SettledRemovalHarness.CreateAsync(namedLeaf: true)` builds a
  `LandingSafetyHarness` (real git with an `origin` remote, an isolated Postgres schema, the production
  `AddDelegationWorktreeGraph` registration with `WorktreeBasePath = <root>/trees`) and one
  `card-task-<id8>` worktree at the target SHA. `LandingSafetyHarness` exposes `Clock`, `ConfigureServices`,
  `RetirementCut` (a save interceptor with `Cut`/`Pause`), and `Fixture.Git` command hooks
  (`BeforeObservedCommand`, `AfterCommand`) plus a `Trace`. `RemoteWorkspacePreparerTests.Rig` and
  `PhoneHomeTestHost` give a scripted phone-home peer with `RequestCount(op)`.
  `TaskWorktreeRetirementTests` has 19 methods and 40 executions, `WorktreeResidueSweepTests` 15 and 15
  (counted from source by `[Test]` plus `[Arguments]`). Both carry `[ParallelLimiter<GitProcessSpawnLimit>]`.
  Windows-only tests skip with `Skip.Test` (`LandWorkspaceTests.cs:163`).

## Decisions taken by this design (T-D-n; defaults, each overridable)

- **T-D-1 Settings: scalars only, shipped on.** Section `WorktreeLegacyRelease`, class
  `WorktreeLegacyReleaseSettings`:

  | Key | Type | Default (class and `server/appsettings.json`) | Validator |
  |---|---|---|---|
  | `Enabled` | bool | `true` (the kill switch) | |
  | `Execute` | bool | `true` (false = dry run) | |
  | `MinTerminalHours` | int | `48` | ≥ 48 (operator floor) and ≤ 8760 |
  | `MaxReleasesPerTick` | int | `25` | 1..100 |
  | `PauseBetweenSeconds` | int | `5` | 0..300 |
  | `MaxTickMinutes` | int | `20` | 1..55 (below the hourly cron) |
  | `RefusedRecheckMinutes` | int | `60` | ≥ 1 |
  | `RefusedRecheckMaxHours` | int | `24` | ≥ 1 |
  | `MaxRefusedAttempts` | int | `5` | 1..50 |
  | `RecurringJobId` | string | `antiphon:desktop-legacy-release` | non-empty |
  | `Cron` | string | `15 * * * *` | five fields |

  There is **no list**. The runner skip set is the existing `RunnerWorktreeRetention:Runners`.
  A **shape validator** (the S1c T-D-2 pattern) enumerates `GetSection("WorktreeLegacyRelease").GetChildren()`
  and refuses any child with children (an array or object) or a JSON `null` value, naming the key, and
  refuses the section itself as an array. Reason (S1c measured facts): an array written into a scalar is
  silently ignored and binds the **default**, which for `Enabled` and `Execute` is `true`. So
  `"Enabled": ["false"]` or `"Enabled": []` would silently keep S4a on. Both validators are registered with
  `ValidateOnStart` through one extension, `AddWorktreeLegacyReleaseSettings(IServiceCollection,
  IConfiguration)`, which `Program.cs` calls.
- **T-D-2 "Pushed" is proved against the task's own origin branch, else the target.** For a system release
  the policy sets `DestinationFullRef` to `refs/heads/<WorktreeBranch>` when `ls-remote origin` names that
  ref, else to the merge target (today's behaviour). `TargetFullRef` stays the merge target, so
  `SourceFullRef != TargetFullRef` still holds. The guarded removal then proves containment of the exact
  source SHA in that origin ref under the lease (`ObserveRetirementAsync`), and a read failure refuses.
  Removing the tree deletes only the **local** branch; the origin branch keeps the work. Rejected: the
  merge-target-only proof (the fact above: S4a would retire almost nothing); a branch-absent-means-pushed
  shortcut.
- **T-D-3 The policy pre-checks, the guarded removal decides.** Before filing a release the policy runs,
  read-only, for each candidate it will act on: the leaf, root and reparse checks, `status
  --porcelain=v1 -z --untracked-files=all --ignore-submodules=none` in the tree, `rev-parse HEAD`,
  the branch ref, and `ls-remote` plus `merge-base --is-ancestor` (only when the origin object is already
  local; otherwise `remote_unverified`). A failing pre-check files nothing and records a Held row
  (`source_dirty`, `source_changed`, `unpushed`, `remote_unverified`). The guarded removal re-proves every
  fact under the lease, so the two are independent guards (G-25..G-32). This is the brief's "evidence step":
  - dirty tracked or untracked files and unpushed commits are **never** removed by S4a. The Held row
    (path, branch, head SHA, origin tip, reason) is the documented evidence, and removal stays the
    caller-filed path;
  - ignored files go through the CARD-0665 gate: allowlisted evidence is **retained before deletion**,
    protected ignored content refuses.
- **T-D-4 A refusal with nothing removed reopens the attempt, for system releases only.** Under the
  repository mutation lease, when the active **system** release's latest attempt has an intent, no component
  removed (`DirectoryRemoved`, `RegistrationRemoved` and `BranchRemoved` not true), the directory and its
  registration present, no set-aside record, and `HEAD == SourceSha`, `TryRetireAsync` sets that attempt's
  `FinishedAt` and opens attempt n+1 with a new intent. Caller-filed releases keep the CARD-1446
  one-command rule unchanged (V-26). The policy paces retries per task (60 min doubling to 24 h) and stops
  after `MaxRefusedAttempts` (Held `retry_exhausted`). A held repository lease
  (`repository_lease_required`) is a deferral that counts no attempt. This is what makes a locked directory
  **defer, never force**: the move-aside fails with nothing moved, and the next attempt is a fresh, bounded
  command. Rejected: a new release per retry (two active rows per attempt are refused, `release_conflict`);
  forcing the delete.
- **T-D-5 System dispositions and dependents.** The policy files
  `HandoffDispositions = [{NextStage, Kind=Superseded, Reason="CARD-0824 S4a: terminal >= 48h; branch <ref>
  on origin at <sha>"}]` when `NextStage` is set, and none otherwise. It never sets `MissingReportReviewed`:
  a null `Result` is Held `report_missing`. It refuses a task with an **open dependent**: an open task whose
  `ParentTaskId` is the task, or whose recorded start or base ref (`WorktreeBaseRef`, or the dispatch
  `StartRef` field that `DelegationWorktreeService.CreateForTaskAsync` resolves) names the task's branch.
  Deleting that local branch could break the dependent's worktree creation. Code confirms the exact start-ref
  field at the base (reachability, §Masking).
- **T-D-6 Runner mirror ownership.** "A worktree of a runner owned by CARD-1677 retention is refused" is
  read as: the **runner mirror** of such a task is never touched by S4a. The desktop half is still retired.
  `TryRetireCoreAsync` skips `RemoveRemoteMirrorAsync` when the task's `RunnerId` is in the effective
  `RunnerWorktreeRetention:Runners`, whatever `RunnerWorktreeRetention:Enabled` says (leaving a mirror is
  the safe direction), and records `runner_mirror_owned_by_retention` on the run row. Rejected: refusing the
  desktop half, which would leave about 85 desktop trees a day (the reason S4a exists).
- **T-D-7 Candidate selection order and the cap before git.** Candidates come from one DB query, oldest
  `CompletedAt` then `Id`. Never-attempted tasks go first, then due retries by last attempt. The cap and the
  time budget apply **before** any per-candidate git or filesystem call, so a first enable over hundreds of
  old trees costs at most `MaxReleasesPerTick` pre-checks. Backed-off and Held rows consume no cap.
- **T-D-8 Preview.** `POST /api/agent-tasks/worktree-residue/desktop-legacy-release/preview` is
  operator-only (a child token is refused, as in 1677) and always dry-run. It writes one run (`Preview=true`)
  with a row per DB candidate: `would_release` for the capped head that passes the pre-checks, Held rows with
  reasons, and `over_cap` Deferred for the rest. It files nothing and runs no mutating git (§V-21 allowlist).
  It runs even when `Enabled=false`, so the operator can preview before switching S4a back on. A tick
  (`Enabled=true`, `Execute=false`) writes the same rows with `Preview=false`.

## Verification design

### Inspection

- **Read:**
  - `TaskWorktreeRetirementService.cs` (all 645 lines);
  - `GuardedWorktreeRemoval.cs:1-452`;
  - `LandingGit.cs:720-745`, `:1168-1240` and `:1325-1331`;
  - `WorktreeManager.cs:525-545`;
  - `WorktreeIgnoredContentGate.cs:1-80`;
  - `RunnerWorktreeRetentionService.cs:1-460`;
  - `RunnerWorktreeRetentionSettings.cs` (all) and `WorktreeResidueSettings.cs` plus its validator;
  - `WorktreeResidueSweepService.cs:140-260`;
  - `RemoteWorkspaceService.cs:40-70` and `:1021-1072`;
  - `AgentTaskEndpoints.cs:140-165`;
  - `WorkspaceRetentionContracts.cs:53-63`;
  - `WorktreeRetirementEnums.cs`.
- **Tests and helpers read:** `TaskWorktreeRetirementTests.cs:1-125` plus its method roster;
  `SettledRemovalHarness.cs:1-140`; `LandingSafetyHarness.cs:1-230` (services, the retirement crash worker,
  the hooks); `C1677RetentionServerTests.cs:1-125` plus its roster (the `World` and `Monitor` pattern);
  `WorktreeResidueSweepTests` header and roster; the S1c test design.
- **Boundaries:**

  | Boundary | IDs |
  |---|---|
  | configuration to bound settings (shape, ranges, reload) | V-1..V-4 |
| leaf pattern (pure) | V-27 |
  | Hangfire job to policy (no concurrency) | V-5, V-22 |
  | DB candidate query to candidate (age, status, predicates, dependents) | V-7..V-9, V-15 |
  | path and name grammar (leaf, root, `ws-*`, master, rc, canonical, control characters) | V-14, V-16, V-17 |
  | pre-check git to release filing (dirty, unpushed, remote) | V-10, V-11 |
  | release filing to retirement attempt and intent | V-6, V-22, V-23, V-26 |
  | retirement to guarded removal (TOCTOU re-proof) | V-12, V-16 |
  | guarded removal to filesystem, registration and branch (recipient) | V-6, V-11, V-24, N-1..N-3 |
  | retirement to runner mirror (phone-home `WorkspaceRemove`) | V-13 |
  | settings monitor to the in-flight tick | V-18..V-20 |
  | preview route to run rows | V-21, V-25 |
  | per-task cursor to the next tick (backoff, exhaustion, starvation) | V-24 |

  Excluded: the runner side (S4a changes no runner file) and the client (no new view; run rows render
  through the existing residue run page).

### Seams (missing at the base; part of Code)

- **S-1** `WorktreeLegacyReleaseSettings`, its range validator, the shape validator and
  `AddWorktreeLegacyReleaseSettings` (T-D-1).
- **S-2** `DesktopLegacyReleasePolicyService.RunTickAsync(CancellationToken ct, bool preview = false)`
  returning `TickResult(Guid? RunId, int Examined, int Eligible, int Filed, int Retired, int BackedOff,
  string? Stopped)`, over `AppDbContext`, `TaskWorktreeRetirementService`, `ILandingGit`,
  `IOptionsMonitor<WorktreeLegacyReleaseSettings>`, `IOptionsMonitor<RunnerWorktreeRetentionSettings>`,
  `IOptions<GitSettings>` and `TimeProvider`. `DesktopLegacyReleasePolicyJob` (Hangfire,
  `[DisableConcurrentExecution(600)]`, `[AutomaticRetry(Attempts = 0)]`) calls it.
- **S-3** In `TaskWorktreeRetirementService`:
  - `ReleaseSystemAsync(Guid taskId, SystemReleaseRequest body, CancellationToken ct)`, which shares
    `ReleaseAsync`'s checks and sets `CallerIdentity = "system:desktop-legacy-policy"` and the T-D-2
    destination. The token-less HTTP caller path cannot reach it.
  - The T-D-4 reopen.
  - The T-D-6 mirror skip.
- **S-4** Test seams:
  - `SettledRemovalHarness.CreateAsync(namedLeaf, Action<IServiceCollection>? configure = null)`, passed
    through to `LandingSafetyHarness.ConfigureServices`;
  - `DesktopLegacyWorld` (new helper beside the test class) with `AddTaskAsync(Action<AgentTask>? seed,
    string shape)`. It creates `<root>/trees/card-task-<id8>` with `git worktree add -b feat/card-task-<id8>`,
    and the shape decides whether a commit is made and pushed (`pushed`, `base-only`, `unpushed`);
  - a `MutableMonitor<T>` (the `C1677RetentionServerTests.Monitor` pattern, settable `CurrentValue`, can
    throw `OptionsValidationException`);
  - an `OnRow` hook on the policy (internal, test-only `Action<Guid>` invoked after each row is recorded)
    for in-flight flips and fault injection.

  No existing harness member changes behaviour.

### Fixtures and binding conventions

- **Class LS `C0824DesktopLegacyReleaseSettingsTests`** (`[Category("Unit")]`, no DB, no git). Binding
  cases build `ConfigurationBuilder().AddJsonFile(<temp file>, reloadOnChange: <case>)` under
  `Path.GetTempPath()/c0824-s4a-<guid>/` (deleted in `finally`) and call S-1. Reload waits poll every 100 ms,
  up to 10 s.
- **Class LP `C0824DesktopLegacyReleasePolicyTests`** (`[Category("Integration")]`,
  `[ParallelLimiter<GitProcessSpawnLimit>]`: its only children are git in fixture-owned repositories). One
  `DesktopLegacyWorld` per test, so the class runs in parallel on the git lane.
  - The world uses a fake `TimeProvider` (`LandingSafetyHarness.Clock`) and the settings `Enabled=true`,
    `Execute=true`, `PauseBetweenSeconds=0`, `MinTerminalHours=48`, `MaxReleasesPerTick=25`.
  - An **eligible task** is: `Status=Succeeded`, `CompletedAt = now - 49 h`, `Result="done"`,
    `NextStage=null`, `Workspace=Worktree`, `Role=Plan`, no session, no debt, no land, shape `pushed`.
  - **Every refusal case seeds one eligible control task beside the refused one** (the CARD-1677 V-30 rule),
    so no case passes because the tick did nothing. The control must end retired.
  - **Recipient evidence** is the filesystem and git, never the DB flag alone:
    - `Directory.Exists(path)`;
    - `git worktree list --porcelain` (the registration);
    - `show-ref --exists refs/heads/<branch>` (exit 2 = gone);
    - `ls-remote origin` byte-equal before and after;
    - for "intact": `status --porcelain` and `rev-parse HEAD` equal before and after, and a SHA-256 over the
      tree's files.
  - **"No git"** means `Fixture.Git.Trace` contains no command whose cwd or arguments name the refused
    task's path or branch.
  - The runner-mirror cases add a `PhoneHomeTestHost` with one scripted peer whose `RunnerId` is the task's,
    and register `RemoteWorkspaceService(host.Directory, fixture.Git, logger)` through `configure`.
- **Class LN `C0824DesktopLegacyReleaseNativeTests`** (`[Category("Integration")]`,
  `[ParallelLimiter<ProcessSpawnLimit>]`: it starts non-git children). Each method calls
  `Skip.Test("Windows ...")` unless `OperatingSystem.IsWindows()`, so it is listed only in the Windows
  checkpoint doc. Children are killed in `finally`.

### Delivery inventory

Each path is joined by its durable identity.

**P-A: the tick to the system release** (new). The durable identity is `(TaskId, TaskAttempt)` → the
`TaskWorktreeRetirements` row with `CallerIdentity = system:desktop-legacy-policy`.
- **Producer:** the Hangfire job → `RunTickAsync` (S-2) → `ReleaseSystemAsync` (S-3).
- **Destination:** the release row.
- **Persistence:** the release row (one active per attempt) and the run candidate row.
- **Recovery:** the next tick finds the active system release and goes straight to `TryRetireAsync`
  (V-23 "after-release-save"). A replay never files a second row (V-22).
- **Receipt:** the release row is necessary but **not** delivery. Delivery is P-B's recipient evidence.

**P-B: the release to the removal** (changed). The durable identity is `(RetirementId, AttemptNumber,
CommandIntentId)`.
- **Producer:** `TryRetireAsync`.
- **Destination:** `GuardedWorktreeRemoval` → the filesystem, the registration and the local branch ref.
- **Persistence:** the attempt row, the command intent (`IRetirementCommandJournal`), the set-aside record
  and the component stamps.
- **Recovery:**
  - a crash after the intent and before git reopens the attempt under T-D-4 (V-23 "after-intent-before-git");
  - a crash after the move-aside resumes from the set-aside record (V-23 "after-move-aside");
  - a busy recipient (the repository lease held elsewhere) defers with no attempt counted (V-24
    "lease-busy");
  - a held file (Windows) defers and retries (N-1).
- **Receipt:** directory absent, registration absent, local branch absent, origin unchanged, release
  `Complete`, and the run row `Removed`. All are asserted together in V-6.

**P-C: the retirement to the runner mirror** (changed by T-D-6). The durable identity is `(TaskId,
RemoteWorktreePath)`.
- **Producer:** `TryRetireCoreAsync` → `RemoveRemoteMirrorAsync`.
- **Destination:** the phone-home `WorkspaceRemove`.
- **Persistence:** `RemoteWorktreePath` and `RemoteWorktreeResidue`.
- **Recovery:** best effort. A failure records the residue (V-13 "runner-unreachable"), and the operator's
  sweep owns it.
- **Receipt:** the peer's `RequestCount(WorkspaceRemove)` and the row's path. For a listed runner the
  receipt is the **absence** of a request, plus the run row's `runner_mirror_owned_by_retention`.

**Substitutes, and what each cannot prove:**
- The scripted peer (V-13) cannot prove the runner's removal itself. That is CARD-0604's and 1677's
  witnesses. V-13 claims only "sent or not sent".
- The git fault hook (V-24) substitutes for a held file on Linux. It cannot prove Windows sharing semantics;
  N-1 does, with a real child process.
- `MutableMonitor` (V-18) cannot prove the file → monitor path; V-4 does, with a real reloading file.
- Linux symlinks (V-16) substitute for junctions; N-3 proves junctions and 8.3 aliases.

There is no session input path: no transcript evidence applies.

### Proves it works now

LS = `C0824DesktopLegacyReleaseSettingsTests`, LP = `C0824DesktopLegacyReleasePolicyTests`, LN =
`C0824DesktopLegacyReleaseNativeTests`.

- **V-1** LS `Defaults_and_shipped_file_are_on`.
  - `new WorktreeLegacyReleaseSettings()` has the T-D-1 defaults.
  - Binding the **repository's** `server/appsettings.json` (located from the test source root) through S-1
    yields `Enabled == true`, `Execute == true`, `MinTerminalHours == 48`, `MaxReleasesPerTick == 25`,
    and `Cron == "15 * * * *"`.
  - Oracle: operator Q-3.
- **V-2** LS `Out_of_range_values_are_refused(string key, string value)`, 13 cases:

  | Key | Values |
  |---|---|
  | `MinTerminalHours` | `47`, `0`, `8761` |
  | `MaxReleasesPerTick` | `0`, `101` |
  | `PauseBetweenSeconds` | `-1` |
  | `MaxTickMinutes` | `0`, `56` |
  | `Cron` | `"15 * * *"` |
  | `RecurringJobId` | `""` |
  | `RefusedRecheckMinutes` | `0` |
  | `RefusedRecheckMaxHours` | `0` |
  | `MaxRefusedAttempts` | `0` |

  Each case fails `Validate` with a message that names `WorktreeLegacyRelease:<key>`. A paired valid value
  (`48`, `25`, `5`, `20`, `"15 * * * *"`, ...) passes.
- **V-3** LS `Non_scalar_values_are_refused(string json)`, 8 cases, bound through S-2's registration with
  `ValidateOnStart`, resolved through `IOptions<>.Value`:
  - refused, with `OptionsValidationException` naming the key: `"Enabled": []`, `"Enabled": ["false"]`,
    `"Execute": {}`, `"MaxReleasesPerTick": [25]`, `"Enabled": null`, `"WorktreeLegacyRelease": []`;
  - accepted: `"Enabled": false` → `false`, and `"Enabled": "false"` → `false`.

  Oracle: the measured S1c behaviour, under which an array binds the default `true`.
- **V-4** LS `Invalid_reload_fails_closed_then_recovers`. A file-backed monitor (`reloadOnChange: true`)
  through S-2 and the policy's settings reader (`DesktopLegacyReleasePolicyService.ReadSettings`, static
  over the monitor):
  1. valid → `(Enabled: true)`;
  2. write `"Enabled": []` and poll until the reader returns `(Enabled: false, Reason: settings_invalid)`.
     It does not throw, and exactly one Warning names `WorktreeLegacyRelease`;
  3. write `"Enabled": false` → `(false, disabled)`;
  4. write `"Enabled": true` → `(true, null)`.
- **V-5** LS `Job_never_runs_concurrently`. Reflection: `DesktopLegacyReleasePolicyJob.RunAsync` carries
  `DisableConcurrentExecutionAttribute` and `AutomaticRetryAttribute { Attempts: 0 }`.
- **V-27** LS `Leaf_pattern_is_end_anchored(string suffix)`, 2 cases: `""` → `DesktopLegacyReleasePolicyService.IsLegacyLeaf("card-task-" + id8 + suffix, id8) == true`, and `"\n"` → `false`. It is the pure `\z` witness: the control-character check is not in front of it.
- **V-6** LP `Eligible_terminal_task_gets_exactly_one_system_release(AgentTaskStatus status, string shape)`,
  3 cases: `(Succeeded, "pushed")`, `(Failed, "base-only")` and `(Canceled, "pushed")`. One tick, then:
  - exactly one `TaskWorktreeRetirements` row for the task, with `CallerIdentity ==
    "system:desktop-legacy-policy"`, `ReleaseReason == "CARD-0824 S4a terminal>=48h"`, `State == Complete`,
    and `DestinationFullRef` = the task's own ref (`pushed`) or the merge target (`base-only`);
  - exactly one attempt with a `CommandIntentId`;
  - recipient: directory absent, registration absent, local branch absent, `ls-remote origin` byte-equal;
  - one run with `Scope == "desktop-legacy-release"`, `Preview == false` and one candidate row
    `(Lane "DesktopLegacyPolicy", Outcome Removed, TaskId, RetirementId, Path, Branch)`, readable through
    `GetRunAsync(runId, 0, operator)`;
  - `TickResult.Filed == 1` and `Retired == 1`.
- **V-7** LP `Age_floor_is_48_hours(string age)`, 3 cases: `"47h59m"` (Held `settling`, no release, no git),
  `"48h"` exact (retired) and `"null"` (`CompletedAt` null: Held `settling`). Oracle: `>=` 48 h.
- **V-8** LP `Non_terminal_task_is_never_released(AgentTaskStatus status)`, 4 cases: `Queued`,
  `Dispatched`, `Working` and `Blocked` (§8: Blocked is not terminal). Each is Held `live_owner`, with no
  release, no git and the tree intact.
- **V-9** LP `Server_predicates_keep_the_tree(string predicate)`, 15 cases. Each case gives the Held reason,
  no release row and the tree intact:

  | Predicate | Seed | Reason |
  |---|---|---|
  | `land-requested` | `LandRequestedAt` set | `land_pending` |
  | `land-started` | `LandStartedAt` set | `land_pending` |
  | `land-request-pending` | a pending `AgentTaskLandRequests` row | `land_pending` (the policy's, ahead of the service's `recovery_debt`) |
  | `landing-row` | an inactive `AgentTaskLandings` row only | `recovery_debt` (the service's) |
  | `sync-Pending`, `sync-Ready`, `sync-Held` | `AgentTaskSyncDebts` in that state | `sync_debt` |
  | `path-shared` | an open task with the same `WorktreePath` | `path_shared` |
  | `live-session` | a bound `AgentSessions` row `Running` | `live_session` |
  | `result-null` | `Result=null` | `report_missing` |
  | `child-open` | an open task with `ParentTaskId = id` | `dependent_open` |
  | `startref-open` | an open task whose start/base ref is `feat/card-task-<id8>` | `dependent_open` |
  | `mutation` | `Role=Mutation` | `mutation_excluded` (the service's) |
  | `source-landing` | `SourceLandingOperationId` set | `source_landing_excluded` (the service's) |
  | `operator-release-active` | an active operator-filed release | `release_active_other` (left to the residue sweep) |
- **V-10** LP `Dirty_or_unpushed_tree_is_never_released(string shape)`, 6 cases. Each gives no release row,
  the tree byte-intact, the local branch present and a Held row:

  | Shape | Reason |
  |---|---|
  | `tracked-modified` | `source_dirty` |
  | `untracked` (a new untracked file) | `source_dirty` |
  | `staged` | `source_dirty` |
  | `unpushed-commit` (a local commit not on origin) | `unpushed` |
  | `origin-branch-deleted` (the pushed branch deleted on origin, the commit not in the target) | `unpushed` |
  | `origin-unreachable` (`origin` URL pointed at a missing path) | `remote_unverified` |

  For each case, the Held row's `Branch` and reason, plus the logged head SHA, are the evidence.
- **V-11** LP `Ignored_content_goes_through_the_evidence_step(string shape)`, 2 cases:
  - `"evidence-allowlisted"`: an ignored file that the CARD-0665 classifier puts in Evidence. Code picks the
    path from `WorktreeIgnoredContentClassifier`'s allowlist at the base and records it. Result: retired,
    and the retained copy exists under the retention root with equal bytes **before** the directory is
    gone (asserted through the retention journal row's timestamp against `DirectoryRemovedAt`);
  - `"protected"`: an ignored file outside the allowlist. Result: `Refused ignored_content_preserved`, the
    tree and the file intact, the release active, the backoff cursor stamped.
- **V-12** LP `Removal_re_proves_every_fact_after_filing(string change)`, 3 cases. The `RetirementCut`
  interceptor fires on the release save and changes the world before `TryRetireAsync`:
  - `"dirty-after-filing"`: write an untracked file → `source_dirty`;
  - `"commit-after-filing"`: commit in the tree → `source_changed`;
  - `"origin-rewound-after-filing"`: force the origin branch back to the base → `remote_no_longer_contains_source`.

  For each: nothing removed (directory, registration and branch present), the release active, the attempt
  closed under T-D-4 (eligible for one later retry), and the cursor stamped.
- **V-13** LP `Retention_owned_runner_mirror_is_never_touched(string runner)`, 3 cases:
  - `"listed"` (`RunnerWorktreeRetention:Runners` names the task's runner): the desktop tree is retired,
    `RequestCount(WorkspaceRemove) == 0`, `RemoteWorktreePath` unchanged, and the run row Detail has
    `runner_mirror_owned_by_retention`;
  - `"unlisted"`: retired, exactly 1 `WorkspaceRemove` with the row's path, and the path cleared;
  - `"runner-unreachable"` (unlisted, the peer disconnected): retired, `RemoteWorktreeResidue == path`.
- **V-14** LP `Only_ordinary_card_task_leaves_are_candidates(string shape)`, 9 cases. Each case files no
  release and runs no git against the path; any directory involved is intact:

  | Shape | Reason |
  |---|---|
  | `ws-01` | `slot_managed` |
  | `ws-01-d2` | `slot_managed` |
  | `card-task-other` (another task's id) | `leaf_mismatch` |
  | `card-task-rc-suffix` (`card-task-<id8>-rc`) | `leaf_mismatch` |
  | `canonical-checkout` (`WorktreePath == RepoPath`) | `leaf_mismatch` |
  | `canonical-checkout-named` (a main working tree, a clone, placed at `<root>/card-task-<id8>`) | `main_worktree` |
  | `master-branch` (leaf ok, `WorktreeBranch=master`) | `outside_scope` (the service's branch-ownership check) |
  | `rc-branch` (`rc/2026-11-04`) | `outside_scope` (the service's) |
  | `rc-candidate-dir` (`<root>/../releases/candidates/rc-2026-11-04-<sha12>`) | `outside_root` |
- **V-15** LP `Leased_runner_slot_keeps_the_desktop_half(string slot)`, 2 cases:
  - `"leased"`: `RemoteWorkspaceSlotId="ws-03"`, `RemoteWorkspaceSlotReleasedAt=null` → Held `slot_leased`,
    with no release, no git and no peer request;
  - `"released"`: the same with `ReleasedAt` set → retired.
- **V-16** LP `Path_escapes_are_refused(string escape)`, 6 cases. The target directory is a real git
  worktree **outside** the root, holding a marker file. Its bytes and registration are equal before and
  after in every case:

  | Escape | Result |
  |---|---|
  | `dotdot` (`<root>/x/../../card-task-<id8>`) | `outside_root`, no release |
  | `relative` (`trees/card-task-<id8>`) | `outside_root`, no release |
  | `symlink-leaf` (`<root>/card-task-<id8>` is a symlink to the outside worktree) | `reparse_point`, no release |
  | `symlink-ancestor` (`<root>/link -> <outside>`, path `<root>/link/card-task-<id8>`) | `outside_root` (not a direct child), no release |
  | `symlink-inside-tree` (an ignored symlink inside the tree to the outside target) | `Refused ignored_reparse_point`; the target intact |
  | `symlink-leaf-after-filing` (the leaf swapped for a symlink through the release-save cut) | `worktree_confinement_changed`; the target intact |
- **V-17** LP `Control_character_names_are_refused(string where, string suffix)`, 6 cases:
  - leaf + `"\n"`, `"\r"`, `"\u2028"`, `"\t"` and `"\u0085"`;
  - branch + `"\n"`.

  Each gives Held `name_control_character` (asserted exactly), no release and no git. The names are built
  with C# escapes (the CARD-1677 S1 `b06fbe678` lesson); no directory with that name is created.
- **V-18** LP `Switch_off_stops_the_tick_at_the_next_row(string setting)`, 2 cases: `"enabled"` and
  `"execute"`. Five eligible tasks. `OnRow` flips the monitor after the first row is recorded:
  - exactly 1 task retired; the other 4 have no release and are intact;
  - one Deferred row `tick_stopped_disabled` or `tick_stopped_execute_off`;
  - `TickResult.Stopped` equals that reason.
- **V-19** LP `Disabled_at_start_does_nothing`. `Enabled=false`: `RunTickAsync` returns no run, writes no
  run row, files no release and runs zero git commands (an empty trace).
- **V-20** LP `Tick_is_bounded(string bound)`, 2 cases:
  - `"count"`: 30 eligible tasks, cap 25. The 25 oldest by `CompletedAt` are retired; the 5 newest are
    untouched (no release, no git). A second tick retires those 5;
  - `"time"`: `MaxTickMinutes=20`, and `OnRow` advances the fake clock 11 min per row. 2 are retired, then
    Deferred `tick_budget_exhausted`.
- **V-21** LP `Preview_and_dry_run_write_nothing(string mode)`, 2 cases:
  - `"preview"`: `Execute=true`, `RunTickAsync(preview: true)`;
  - `"execute-false"`: `Execute=false`, an ordinary tick.

  Three eligible tasks, one dirty and one `ws-01`. For each case:
  - zero `TaskWorktreeRetirements` and attempt rows;
  - every tree intact;
  - `Fixture.Git.Trace` holds only allowlisted read commands (`status`, `rev-parse`, `show-ref`, `ls-remote`,
    `merge-base`, `worktree list`), and none of `fetch`, `update-ref`, `worktree remove|prune|move`,
    `branch -d|-D` or `push`;
  - run rows: 3 `would_release` (Preview), 1 `source_dirty`, 1 `slot_managed`, with `run.Preview ==
    (mode == "preview")`.

  Also `"preview"` with `Enabled=false` writes the same rows (T-D-8).
- **V-22** LP `Double_run_is_idempotent(string mode)`, 3 cases:
  - `"sequential"`: two ticks;
  - `"concurrent"`: two `RunTickAsync` calls in two scopes started together;
  - `"residue-sweep-first"`: the residue sweep (`Execute=true`) runs after the policy has filed (the
    release-save cut) and before its `TryRetireAsync`.

  For each: exactly one release row and one attempt per task, one command intent, the tree retired once,
  and no `release_conflict` or `cleanup_command_slot_spent` row.
- **V-23** LP `Crash_at_each_handoff_recovers_once(string cut)`, 3 cases:
  - `"after-release-save"`: the `RetirementCut` throws after the release commit (the tick ends with the
    exception caught per row). Tick 2 files no second release and retires through the existing one;
  - `"after-intent-before-git"`: a throw at the intent save's acknowledgement. Tick 2 (clock + 61 min):
    T-D-4 closes attempt 1 and opens attempt 2, and the tree is retired. Exactly 2 attempts, 2 intents,
    one release;
  - `"after-move-aside"`: a `Fixture.Git.BeforeObservedCommand` throw at `worktree` unregister after the
    move-aside. Tick 2 resumes from the set-aside record: the tree is put back or finished, never orphaned.
    It ends Complete with no unregistered directory left at any point (checked after tick 1 and tick 2).
- **V-24** LP `Failures_back_off_per_task_and_never_starve_others(string failure)`, 5 cases. Task A (oldest)
  fails, and B and C (newer) are eligible, with `MaxReleasesPerTick=2`:
  - `"retry-succeeds"`: a git hook fails A's unregister once (nothing removed, A restored).
    - Tick 1: A Refused `worktree_remove_failed` and intact; B is retired in the same tick (the cap is 2:
      A's attempt counts, B is the second).
    - Tick 2 at +30 min: A Held `refused_recheck_pending` with no git for A; C retired.
    - Tick 3 at +61 min: A retired (attempt 2).
  - `"backoff-doubles"`: A always fails. A's NotBefore after successive refusals is 60, 120, 240 ... min,
    capped at 24 h. B and C are retired by tick 2.
  - `"exhausted"`: after `MaxRefusedAttempts=3` refusals A is Held `retry_exhausted` and is never attempted
    again, even past 24 h (no attempt 4, no git for A).
  - `"throws"`: `ReleaseSystemAsync` throws for A (an interceptor throws `DbUpdateException` on A's
    insert). The row is Deferred `release_failed`, B and C are retired in the same tick, and the tick
    returns normally.
  - `"lease-busy"`: another holder owns the repository mutation lease during tick 1. A, B and C are
    Deferred `repository_lease_required` with **no attempt counted** (A's refused count stays 0) and
    nothing removed. After release, tick 2 retires A and B.
- **V-25** LP `First_enable_on_a_large_backlog_is_bounded(string mode)`, 2 cases. 300 eligible task rows
  (all `CompletedAt` 3 to 30 days old). The 27 oldest have real pushed worktrees; the other 273 have
  `card-task-<id8>` paths that do not exist:
  - `"preview-first"`: one preview run with 300 candidate rows over 6 pages of 50 (`GetRunAsync`):
    25 `would_release`, 275 Deferred `over_cap`. No release, and git commands name only the 25 oldest paths;
  - `"execute"`: tick 1 files and retires exactly the 25 oldest. `Fixture.Git.Trace` names no path outside
    those 25, and the tick's wall time is under 60 s. Tick 2 retires the next 2 and records the 273
    missing paths as Held `directory_absent`; it never files a release for an absent path.
- **V-26** LP `Caller_filed_release_keeps_one_command`. An **operator** release (through `ReleaseAsync`) whose
  removal is refused after the intent (the V-24 hook) returns `cleanup_command_slot_spent` on a second
  `TryRetireAsync`, with no attempt 2. This pins T-D-4's system-only scope (CARD-1446 behaviour unchanged).

**Windows-native (LN, the desktop lane only):**

- **N-1** LN `Held_directory_defers_never_forces(string holder)`, 2 cases:
  - `"open-handle"`: a child `dotnet` process (a copied tiny console app) opens `bin/held.dll` inside the
    tree with `FileShare.Read`;
  - `"cwd"`: a child whose working directory is `<tree>/node_modules/x` (the MCP shape).

  Tick 1: Refused `worktree_remove_failed`. The directory, every file (the hash), the registration and the
  local branch are intact; the trace has no `--force`; the release stays active and the cursor is stamped.
  The child is killed in `finally`. Tick 2 at +61 min: retired (attempt 2).
- **N-2** LN `Long_path_never_leaves_an_unregistered_orphan`. An ignored, allowlisted-disposable build path
  of more than 300 characters under `bin/` inside the tree.
  - **Hard assertion** (after the tick and after one retry): never "registration gone and directory present".
  - **Expected:** `Complete`.
  - If the result is `worktree_removal_incomplete` with a set-aside record, Code records it and files a card
    for the `\\?\` delete. The safety assertion still holds.
- **N-3** LN `Windows_aliases_and_junctions_are_refused(string alias)`, 3 cases:
  - `"junction-leaf"`: `<root>\card-task-<id8>` is a directory junction to an outside worktree →
    `reparse_point`, no release, the target intact;
  - `"junction-inside"`: an ignored junction inside the tree pointing outside → `ignored_reparse_point`,
    the target intact;
  - `"short-name"`: the 8.3 alias of the leaf (`GetShortPathName`; when 8.3 names are disabled on the
    volume the case uses the synthesized string `CARD-T~1`) → `leaf_mismatch`, no release.

### Guards the regression

- **R-1** `TaskWorktreeRetirementTests` (40 executions): caller-filed release, retirement, revoke,
  snapshot, handoff, artifacts and the exclusions are unchanged by S-3.
- **R-2** `WorktreeResidueSweepTests` (15): the daily sweep's selection and rows are unchanged, and it
  coexists with system releases (V-22 covers the interaction).
- **R-3** V-26 (in LP): caller-filed one-command rule.

### Masking and reachability

- **Every refusal case has a control task** that must end retired, so "nothing happened" cannot pass.
- **Overlapping guards are told apart by the exact reason code.** `land-request-pending` expects the
  policy's `land_pending`, and the service's `recovery_debt` stays behind it. The policy does **not** repeat
  the service's Mutation, SourceLanding and repair exclusions: it files and records the service's
  `ConflictException.Code`. So each guard has exactly one owner, and one PC.
- **`startref-open` (V-9).** Code confirms the field at the base that records a dispatch start ref
  (`WorktreeBaseRef` or the delegate request's `StartRef` persisted on the task). If none is persisted, the
  case seeds the field the dispatcher reads, and Code reports which one. That is a known reachability risk,
  not a guess at the outcome.
- **`canonical-checkout-named` (V-14)** bypasses the leaf grammar on purpose: the name is a valid leaf, so
  only `main_worktree` (the first `worktree list` entry, or the absence of a `.git` file) refuses it.
- **V-11 `evidence-allowlisted`** depends on the classifier's allowlist at the base. Code picks a path that
  the classifier puts in Evidence, not Disposable, and asserts the classifier's verdict first (a guard
  against a vacuous case).
- **V-12** fires through `LandingSafetyHarness.RetirementCut` on the release save. That runs before
  `TryRetireAsync`'s lease, so the change is visible to the guarded removal's readings.
- **V-23 `after-move-aside`.** The unregister fault must fire after `MoveAside`. The hook matches
  `worktree` with the set-aside path absent from argv (git is never handed the path), and Code asserts the
  set-aside record exists at the cut.
- **V-25 timing.** The 60 s bound is wall time on the loaded server2 host and is 4x the expected ~15 s.
  Code replaces it with 2x the measured p95 from the first green TRX.
- **V-20 `"time"`** uses the fake clock, never real time.
- **N-1** cannot run on Linux: a Linux rename succeeds under an open handle, which is why V-24 uses the git
  hook substitute.

### Guard inventory

| G | Plan ref + safety-critical guard | PC |
|---|---|---|
| G-1 | Q-3/T-D-1 `Enabled=false` at tick start: no run, no git | PC-1 |
| G-2 | Q-3 `Enabled` re-read before each row (in flight) | PC-2 |
| G-3 | Q-3 `Execute` re-read before each row (in flight) | PC-3 |
| G-4 | §3.2 `Execute=false` files nothing | PC-4 |
| G-5 | T-D-8 preview is dry-run whatever `Execute` says | PC-5 |
| G-6 | T-D-8 dry-run/preview runs no mutating git | PC-6 |
| G-7 | Q-3 per-tick count cap | PC-7 |
| G-8 | 1677 A-2 tick time budget | PC-8 |
| G-9 | T-D-7 cap applied before per-candidate git (backlog bound) | PC-9 |
| G-10 | §3.2 oldest `CompletedAt` first | PC-10 |
| G-11 | H-6 age floor `>=` 48 h | PC-11 |
| G-12 | 1677 M-8 null `CompletedAt` is not a candidate | PC-12 |
| G-13 | §3.2/§8 terminal status only (Blocked excluded) | PC-13 |
| G-14 | §3.2 policy `land_pending` (requested, started, pending request) | PC-14 |
| G-15 | service `recovery_debt` (any landing row) | PC-15 |
| G-16 | §3.2 sync debt Pending/Ready/Held | PC-16 |
| G-17 | §3.2 path shared by an open task | PC-17 |
| G-18 | §3.2 live session | PC-18 |
| G-19 | T-D-5 `Result` present; the system never sets `MissingReportReviewed` | PC-19 |
| G-20 | T-D-5 open dependent: child task | PC-20 |
| G-21 | T-D-5 open dependent: start/base ref names the branch | PC-21 |
| G-22 | service Mutation excluded | PC-22 |
| G-23 | service SourceLanding excluded | PC-23 |
| G-24 | §3.2 an active non-system release is left alone | PC-24 |
| G-25 | T-D-3 pre-check: tracked dirty | PC-25 |
| G-26 | T-D-3 pre-check: untracked counts (`--untracked-files=all`) | PC-26 |
| G-27 | T-D-3 pre-check: unpushed commit | PC-27 |
| G-28 | T-D-3 pre-check: an origin read failure fails closed | PC-28 |
| G-29 | guarded removal: `source_dirty` at removal | PC-29 |
| G-30 | guarded removal: `HEAD == SourceSha` at removal | PC-30 |
| G-31 | guarded removal: origin destination contains the source at removal | PC-31 |
| G-32 | T-D-2 destination: own branch when present, else target; absent never means pushed | PC-32 |
| G-33 | CARD-0665 protected ignored content refuses | PC-33 |
| G-34 | CARD-0665 evidence retained before deletion (the evidence step) | PC-34 |
| G-35 | leaf grammar `^card-task-[0-9a-f]{8}\z` equals the task's id | PC-35 |
| G-36 | `\z` anchoring, never `$` | PC-36 |
| G-37 | control characters refused before any pattern | PC-37 |
| G-38 | managed-root confinement after `GetFullPath` (`..`, relative) | PC-38 |
| G-39 | policy: reparse point at the leaf refused before filing | PC-39 |
| G-40 | guarded removal: canonical-directory confinement at removal | PC-40 |
| G-41 | guarded removal: ignored reparse point inside the tree refuses | PC-41 |
| G-42 | §3.2 L-9 `ws-*` is `slot_managed` | PC-42 |
| G-43 | S1 a leased runner slot keeps the task (`slot_leased`) | PC-43 |
| G-44 | branch ownership: master/rc/foreign branches refused | PC-44 |
| G-45 | the registration must be a linked worktree, never the main working tree | PC-45 |
| G-46 | A-D-12/T-D-6 no mirror removal for a listed runner | PC-46 |
| G-47 | T-D-6 an unlisted runner's mirror is still removed (no over-skip) | PC-47 |
| G-48 | S-3 system identity `system:desktop-legacy-policy`, not `operator` | PC-48 |
| G-49 | one active release per attempt under concurrent ticks | PC-49 |
| G-50 | L-10 a crash after filing reuses the active system release | PC-50 |
| G-51 | T-D-4 an idle spent attempt reopens (no wedge) | PC-51 |
| G-52 | T-D-4 reopen only for system releases | PC-52 |
| G-53 | T-D-4 reopen only when nothing was removed and no set-aside record exists | PC-53 |
| G-54 | T-D-4 per-task backoff after a refusal | PC-54 |
| G-55 | T-D-4 the backoff is capped at `RefusedRecheckMaxHours` | PC-55 |
| G-56 | T-D-4 exhaustion after `MaxRefusedAttempts` | PC-56 |
| G-57 | a failing row never ends the tick (exception isolation) | PC-57 |
| G-58 | backed-off and Held rows consume no cap (no starvation) | PC-58 |
| G-59 | a busy repository lease defers without counting an attempt | PC-59 |
| G-60 | settings range validator (`MinTerminalHours` ≥ 48) | PC-60 |
| G-61 | T-D-1 the shape validator refuses arrays, objects and null | PC-61 |
| G-62 | T-D-1 the shape validator is registered by the production extension | PC-62 |
| G-63 | T-D-1 an invalid reload fails closed (treated as disabled) | PC-63 |
| G-64 | S-2 the job never runs concurrently | PC-64 |
| G-65 | L-5 retirement goes through `TryRetireAsync` (no direct removal) | PC-65 |
| G-66 | Windows: a held directory defers, never forces | PC-66 |
| G-67 | Windows: never an unregistered directory with bytes (long path) | PC-67 |
| G-68 | Windows: a junction leaf is refused | PC-68 |

guards=68, mapped=68, missing=0, duplicate PC maps=0. Guards in no witness: none. G-15, G-22, G-23, G-29..G-31,
G-33, G-34, G-40 and G-41 are existing service or removal guards. They are listed because S4a is the first
caller that reaches them with no human review, so each needs its own red through the policy.

### Positive controls

Each PC breaks its guard with a compiling defect and expects the named method red at the named assertion.
Mutation runs break/red/restore/green after the land; Code runs the V/R rows; Review judges before the land.

Filters are method-scoped: `--treenode-filter "/*/*/<Class>/<Method>"`. A method with `[Arguments]` runs all
its cases, and the named case must go red. Zero tests, a build error or a fixture error is not red.

Files: `Pol` = `DesktopLegacyReleasePolicyService.cs`, `Set` = `WorktreeLegacyReleaseSettings.cs` (with its
validators and extension), `Job` = `DesktopLegacyReleasePolicyJob.cs`, `Ret` =
`TaskWorktreeRetirementService.cs`, `Grd` = `GuardedWorktreeRemoval.cs`, `Gate` =
`WorktreeIgnoredContentGate.cs` / `WorktreeIgnoredContentClassifier.cs`, `Lg` = `LandingGit.cs`.

| PC | Break | Expect red | Reachability |
|---|---|---|---|
| PC-1 | `Pol`: skip the `Enabled` check at tick start | LP/`Disabled_at_start_does_nothing` at "no run row" | the eligible control exists |
| PC-2 | `Pol`: drop `Enabled` from the per-row stop check | LP/`Switch_off_stops_the_tick_at_the_next_row("enabled")` at "exactly 1 retired" | the flip happens after row 1 |
| PC-3 | `Pol`: drop `Execute` from the per-row stop check | same `("execute")` at "4 have no release" | |
| PC-4 | `Pol`: `Execute=false` files the release, then skips `TryRetireAsync` | LP/`Preview_and_dry_run_write_nothing("execute-false")` at zero release rows | |
| PC-5 | `Pol`: `preview` uses `settings.Execute` | same `("preview")` at zero release rows | `Execute=true` |
| PC-6 | `Pol`: the pre-check runs `fetch` of the origin branch into a pin before `merge-base` | same `("preview")` at the trace allowlist | the pushed control needs the remote object |
| PC-7 | `Pol`: drop `.Take(MaxReleasesPerTick)` | LP/`Tick_is_bounded("count")` at "5 newest untouched" | 30 > 25 |
| PC-8 | `Pol`: drop the `MaxTickMinutes` check | same `("time")` at `tick_budget_exhausted` | fake clock |
| PC-9 | `Pol`: run the git pre-check for every DB candidate, then take the cap | LP/`First_enable_on_a_large_backlog_is_bounded("execute")` at "the trace names no path outside the 25" | 273 extra paths |
| PC-10 | `Pol`: order by `CompletedAt` descending | LP/`Tick_is_bounded("count")` at "the 25 oldest" | distinct `CompletedAt` |
| PC-11 | `Pol`: `>` 48 h instead of `>=` | LP/`Age_floor_is_48_hours("48h")` at retired | exact 48 h on the fake clock |
| PC-12 | `Pol`: treat a null `CompletedAt` as `DateTime.MinValue` | same `("null")` at Held `settling` | the service floor would also refuse; the case asserts the policy's reason `settling` with no release attempt (no `ConflictException` logged) |
| PC-13 | `Pol`: include `Blocked` in terminal | LP/`Non_terminal_task_is_never_released(Blocked)` at no release | the service refuses non-terminal too; asserts reason `live_owner` from the policy, no `terminal_required` |
| PC-14 | `Pol`: drop the `land_pending` predicate | LP/`Server_predicates_keep_the_tree("land-requested")` at the tree intact (no request row: the service does not see `LandRequestedAt`) | |
| PC-15 | `Ret`: `HasRecoveryDebtAsync` ignores `AgentTaskLandings` | same `("landing-row")` at no release | the policy has no landing-row predicate |
| PC-16 | `Pol`: the sync debt state set drops `Held` | same `("sync-Held")` at the tree intact | |
| PC-17 | `Pol`: drop `path_shared` | same `("path-shared")` at reason/intact | the admission check needs a live consumer reservation, which is not seeded |
| PC-18 | `Pol`: drop `live_session` | same `("live-session")` at the tree intact | no admission reservation seeded |
| PC-19 | `Pol`: send `MissingReportReviewed=true` for a null `Result` | same `("result-null")` at no release | |
| PC-20 | `Pol`: drop the child-dependent check | same `("child-open")` at no release | |
| PC-21 | `Pol`: drop the start-ref dependent check | same `("startref-open")` at no release | |
| PC-22 | `Ret`: drop the Mutation refusal in the release path | same `("mutation")` at no release | the policy does not repeat it |
| PC-23 | `Ret`: drop the SourceLanding refusal in the release path | same `("source-landing")` at no release | |
| PC-24 | `Pol`: treat any active release as its own and call `TryRetireAsync` | same `("operator-release-active")` at the tree intact | the operator release is valid |
| PC-25 | `Pol`: the pre-check ignores status output | LP/`Dirty_or_unpushed_tree_is_never_released("tracked-modified")` at "no release row" (the guarded removal still keeps the tree; this PC is red on the row assertion) | |
| PC-26 | `Pol`: status with `--untracked-files=no` | same `("untracked")` at no release | |
| PC-27 | `Pol`: skip the containment pre-check | same `("unpushed-commit")` at no release | |
| PC-28 | `Pol`: an `ls-remote` failure counts as pushed | same `("origin-unreachable")` at no release | |
| PC-29 | `Lg`: `InspectAsync` ignores untracked status lines | LP/`Removal_re_proves_every_fact_after_filing("dirty-after-filing")` at the directory present | the pre-check passed before the cut |
| PC-30 | `Grd`: `Matches` drops `HeadSha == ExpectedSourceSha` | same `("commit-after-filing")` at the directory present | |
| PC-31 | `Grd`: `AuthorityAsync` treats `ContainsSource == false` as authorized | same `("origin-rewound-after-filing")` at the directory present | |
| PC-32 | `Ret`/`Pol`: a missing origin branch selects the own-branch destination as "contained" | LP/`Dirty_or_unpushed_tree_is_never_released("origin-branch-deleted")` at no release | the commit is not in the target |
| PC-33 | `Gate`: classify everything ignored as Disposable | LP/`Ignored_content_goes_through_the_evidence_step("protected")` at the file intact | |
| PC-34 | `Grd`: skip `ignored.RetainAsync` | same `("evidence-allowlisted")` at "the retained copy exists" | the path is in Evidence (asserted first) |
| PC-35 | `Pol`: the leaf pattern accepts any 8 hex digits (no id equality) | LP/`Only_ordinary_card_task_leaves_are_candidates("card-task-other")` at reason `leaf_mismatch` (the service would answer `outside_scope`) | the policy codes are distinct from the service's (`leaf_mismatch`, `outside_root`) |
| PC-36 | `Pol`: `$` instead of `\z` | LS/`Leaf_pattern_is_end_anchored("\n")` at `IsLegacyLeaf == false` | `$` matches before a final `\n`; the pure pattern test has no control-character gate in front of it |
| PC-37 | `Pol`: drop `HasControlCharacter` | same `("leaf","\u2028")` at reason `name_control_character` (becomes `leaf_mismatch`) | |
| PC-38 | `Pol`: compare the root without `Path.GetFullPath` | LP/`Path_escapes_are_refused("dotdot")` at reason `outside_root` (the service would answer `outside_scope`) | |
| PC-39 | `Pol`: drop the leaf reparse-point check | same `("symlink-leaf")` at reason `reparse_point` and no release (the guarded removal would still refuse later) | |
| PC-40 | `Grd`: drop the canonical-directory confinement | same `("symlink-leaf-after-filing")` at the target intact | the swap happens after the pre-check |
| PC-41 | `Gate`: `ReparsePoints` returns empty | same `("symlink-inside-tree")` at the target intact | |
| PC-42 | `Pol`: drop the `ws-*` check (leaf grammar only) | LP/`Only_ordinary_card_task_leaves_are_candidates("ws-01")` at reason `slot_managed` (the grammar gives `leaf_mismatch`) | |
| PC-43 | `Pol`: drop the `slot_leased` predicate | LP/`Leased_runner_slot_keeps_the_desktop_half("leased")` at no release | |
| PC-44 | `Ret`: `DelegateBranchName.IsOwned` check removed | LP/`Only_ordinary_card_task_leaves_are_candidates("master-branch")` at no release | the leaf is valid |
| PC-45 | `Pol`: drop the main-worktree check | same `("canonical-checkout-named")` at no release | the grammar passes |
| PC-46 | `Ret`: drop the T-D-6 skip | LP/`Retention_owned_runner_mirror_is_never_touched("listed")` at `RequestCount == 0` | |
| PC-47 | `Ret`: skip the mirror removal for every runner | same `("unlisted")` at `RequestCount == 1` | |
| PC-48 | `Ret`: `ReleaseSystemAsync` writes `CallerIdentity = "operator"` | LP/`Eligible_terminal_task_gets_exactly_one_system_release(Succeeded,"pushed")` at `CallerIdentity` | |
| PC-49 | `Ret`: drop the `FOR UPDATE` lock in `ReleaseSystemAsync` | LP/`Double_run_is_idempotent("concurrent")` at "one release row" | two scopes race |
| PC-50 | `Pol`: always file a new release (ignore an active system release) | LP/`Crash_at_each_handoff_recovers_once("after-release-save")` at "no second release" / no `release_conflict` row | |
| PC-51 | `Ret`: never reopen (the base behaviour) | same `("after-intent-before-git")` at retired on tick 2 | |
| PC-52 | `Ret`: reopen for any caller | LP/`Caller_filed_release_keeps_one_command` at `cleanup_command_slot_spent` | |
| PC-53 | `Ret`: reopen ignoring the set-aside record and the component stamps | same `("after-move-aside")` at "no unregistered directory" or the exact attempt count | the set-aside record exists at the cut |
| PC-54 | `Pol`: no NotBefore stamp after a refusal | LP/`Failures_back_off_per_task_and_never_starve_others("retry-succeeds")` at "tick 2: no git for A" | |
| PC-55 | `Pol`: no cap on the doubling | same `("backoff-doubles")` at NotBefore ≤ 24 h | |
| PC-56 | `Pol`: drop the `MaxRefusedAttempts` check | same `("exhausted")` at "no attempt 4" | |
| PC-57 | `Pol`: rethrow a row exception | same `("throws")` at "B and C retired" | |
| PC-58 | `Pol`: a backed-off row consumes a cap slot | same `("retry-succeeds")` at "C retired at tick 2" | cap 2 |
| PC-59 | `Pol`: `repository_lease_required` counts as a refusal | same `("lease-busy")` at "A's refused count stays 0" | |
| PC-60 | `Set`: drop the `MinTerminalHours` floor | LS/`Out_of_range_values_are_refused("MinTerminalHours","47")` at Failed | |
| PC-61 | `Set`: the shape check uses `GetSection(key).Exists()` | LS/`Non_scalar_values_are_refused("\"Enabled\": []")` at the exception | `[]` has `Exists() == false` (S1c, measured) |
| PC-62 | `Set`: the extension omits the shape validator | same `("\"Enabled\": [\"false\"]")` at the exception | |
| PC-63 | `Pol`: an `OptionsValidationException` returns the last good settings | LS/`Invalid_reload_fails_closed_then_recovers` at `(false, settings_invalid)` | |
| PC-64 | `Job`: remove `[DisableConcurrentExecution]` | LS/`Job_never_runs_concurrently` at the attribute | |
| PC-65 | `Pol`: call `IWorktreeManager.TryRemoveAsync` directly with a synthesized request | LP/`Eligible_terminal_task_gets_exactly_one_system_release(Succeeded,"pushed")` at "exactly one attempt with an intent" | |
| PC-66 | `Grd`: on a move-aside failure fall back to `WorktreeNoFollowDelete.Delete(source)` | LN/`Held_directory_defers_never_forces("open-handle")` at "every file intact" | Windows only |
| PC-67 | `Grd`: unregister before the move-aside | LN/`Long_path_never_leaves_an_unregistered_orphan` at the hard assertion | Windows only |
| PC-68 | `Pol`: the reparse check uses `LinkTarget` only on files | LN/`Windows_aliases_and_junctions_are_refused("junction-leaf")` at no release | Windows only |

**Mutant list** (each deletion guard; Mutation also runs these on the named witnesses; none needs its own
guard):
- **M-1** `Pol`: cursor keyed by path instead of task id → LP `("retry-succeeds")` (a re-pathed task loses its backoff).
- **M-2** `Pol`: re-read the row before each send dropped → LP/`Server_predicates_keep_the_tree` with the predicate seeded by `OnRow` after selection (Code adds it as case `"late-land-request"` if M-2 survives; the CP Min then rises by 1).
- **M-3** `Ret`: T-D-2 always picks the target destination → LP/`Eligible...("pushed")` at Complete (it ends `remote_no_longer_contains_source`).
- **M-4** `Ret`: the `HandoffDispositions` for a `NextStage` task are omitted → LP/`Eligible...` with `NextStage=Review` (Code adds the case `(Succeeded,"pushed-next-stage")` beside V-6's three; Min +1).
- **M-5** `Pol`: newest-first retries → `("backoff-doubles")`.
- **M-6** `Pol`: the preview writes release rows and then deletes them → `("preview")` (an interceptor counts inserts).
- **M-7** `Grd`: `CompleteBranchAsync` deletes the branch without the exact old SHA → R-1 `C459_*`.
- **M-8** `Pol`: the preview honours `Enabled=false` → V-21 `"preview"` with `Enabled=false`.

### Out of scope

- **The runner side** (CARD-1677 owns runner mirrors; T-D-6 only stops S4a touching them).
- **The client.** Run rows render through the existing residue run page. No new attention kind: a Held
  `retry_exhausted` row is read from the run page. A follow-up card may add attention if exhausted rows
  accumulate.
- **The preview route's HTTP caller-scope check.** It copies the 1677 route's two lines (a child token is
  forbidden). The preview writes no release, so the exposure is run rows only. No HTTP test is added.
- **Windows qualification overlap.** S4a runs in the desktop server while a Windows rc run may hold
  `C:\Antiphon\verification\native-run.lock`. The candidates directory is not under the worktree root (V-14
  `rc-candidate-dir`), so there is no correctness overlap. The IO cost (≤ 25 removals per hour) is not gated
  on the lock; Q-4 offers a fence.
- **The Linux roster and execution policy entries** for the three new classes. Code adds whatever
  `tests/linux-test-roster.json` and `tests/test-execution-policy.json` require at the base for a new
  git-lane class and a Windows-skipping class (LN skips on Linux and must not count as a Linux failure).
  Review checks the roster gate output.

### Implementation steps (red first, then green)

1. Commit LS, LP and LN with S-4 and compiling stubs: the settings class with defaults, a policy whose tick
   records rows but files nothing, `ReleaseSystemAsync` that throws `NotImplementedException`, and no T-D-4,
   T-D-6 or shape validator. Red as expected: most of V-2..V-26 red, V-1 and V-19 may be green.
2. S-1 (V-1..V-4 green). Then S-2 selection and predicates (V-7..V-10, V-14..V-17, V-19). Then S-3 filing,
   T-D-2 and T-D-6 (V-6, V-11..V-13). Then T-D-4 and backoff (V-22..V-24, V-26). Then the bounds and preview
   (V-18, V-20, V-21, V-25). Then the job and registration (V-5).
3. `server/appsettings.json`: the `WorktreeLegacyRelease` section with the T-D-1 values. Docs:
   `docs/orchestration-loop.md` (the legacy cleanup paragraph: S4a on, kill switch `Enabled=false`, dry run
   `Execute=false`, the preview route, the Held reasons and that dirty or unpushed trees stay for the
   caller-filed path) and `docs/ops-http.md` (the preview route).
4. Run CP-23, then CP-24 (server2). The desktop lane runs CP-25 from the native checkpoint doc after the
   live Windows qualification run ends. **Nothing runs on the desktop before then.**

## Performance budget

Owner: [test-performance-standards.md](../../test-performance-standards.md).

- **Measured inputs** (from earlier receipts; not re-measured here):
  - `tests/Antiphon.Tests` build: 143 s warm on server2 (CARD-1677 TD), p50 326 s and p95 579 s loaded
    (n=117);
  - `LandingSafetyHarness` worlds: the C459 retirement class runs 40 executions on the git lane. No per-test
    p95 is recorded for it; Code records it from CP-24's TRX.
- **Per-test bodies (estimated).**
  - LS under 50 ms, and V-4 under 2 s, with a 10 s poll bound.
  - LP: one harness (git fixture plus isolated schema) is about 3 s typical and 8 s loaded, plus about
    0.3 s per extra worktree. Single-task cases take about 5 s typical and 12 s loaded. V-20 (30 trees) and
    V-25 (27 trees and 300 rows) take about 25 s typical and 50 s loaded.
  - LN about 10 s each (a child process plus one tick).
- **Timeouts.**
  - LS `[Timeout(15_000)]`.
  - LP class `[Timeout(45_000)]` (about 2x the 20-25 s loaded figure, the CARD-1677 transport-world value),
    with method-level `[Timeout(120_000)]` on V-20 and V-25.
  - LN `[Timeout(60_000)]`.
  - Code resets each from the first green TRX at 2x the measured p95.
- **Shared setup.** None across tests, so each test owns its world and the class parallelises on the git
  lane. A shared template repository cloned per test (`--shared`) is the fallback if CP-23's measured class
  wall time exceeds 12 minutes.
- **Phases:** build, fixture setup, bodies and teardown, with slot wait reported separately.
- No row is near 30 minutes. CP-23 with its build is the largest, at about 20 minutes.

### Checkpoints

One server2 checkpoint run, After group S4a, one build. These rows replace the amendment's CP-23 and CP-24
(same numbers). The Windows-native row CP-25 lives in its own doc,
[2026-11-03-card-0824-s4a-native-checkpoints.md](2026-11-03-card-0824-s4a-native-checkpoints.md), so that the
server2 run never schedules a Windows-skipping class and the desktop lane runs it alone, after the live rc
qualification. `Min` is the TUnit executed floor (`-MinExecuted`).

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-23 | S4a | tests/Antiphon.Tests -> bin-c0824-s4a/ | portable-desktop-legacy-policy | /*/*/(C0824DesktopLegacyReleaseSettingsTests*)\|(C0824DesktopLegacyReleasePolicyTests*)/* | V-1..V-27, R-3 | 2 classes, 0 failed/skipped | 109 | 20 | true |
| CP-24 | S4a | CP-23 | portable-retirement-regression | /*/*/(TaskWorktreeRetirementTests*)\|(WorktreeResidueSweepTests*)/* | R-1, R-2 | 2 existing classes, 0 failed/skipped | 55 | 8 | true |

**How Min is counted (executions).**
- CP-23 = LS 26 + LP 83 = **109**.
  - LS: V-1 1, V-2 13, V-3 8, V-4 1, V-5 1 and V-27 2 = **26**.
  - LP:

    | Witness | Cases | Witness | Cases | Witness | Cases |
    |---|---:|---|---:|---|---:|
    | V-6 | 3 | V-13 | 3 | V-20 | 2 |
    | V-7 | 3 | V-14 | 9 | V-21 | 2 |
    | V-8 | 4 | V-15 | 2 | V-22 | 3 |
    | V-9 | 15 | V-16 | 6 | V-23 | 3 |
    | V-10 | 6 | V-17 | 6 | V-24 | 5 |
    | V-11 | 2 | V-18 | 2 | V-25 | 2 |
    | V-12 | 3 | V-19 | 1 | V-26 | 1 |

    Total **83**. M-2 and M-4 may add up to 2 cases. `Min` is a floor, so it stays valid.
- CP-24 = `TaskWorktreeRetirementTests` 40 + `WorktreeResidueSweepTests` 15 = **55**, counted from source
  at the base.

**Roster and prefix check** (grep `class <name>` in `tests/` at the base):
- None of `C0824DesktopLegacyReleaseSettingsTests`, `C0824DesktopLegacyReleasePolicyTests` or
  `C0824DesktopLegacyReleaseNativeTests` exists. No name is a prefix of another (`...ReleaseS`, `...ReleaseP`,
  `...ReleaseN`).
- `DesktopLegacyWorld` has no `[Test]`.
- `TaskWorktreeRetirementTests` and `WorktreeResidueSweepTests` each match exactly one class, with no
  partial files and no longer names sharing the prefix.

**Import verdict.** See the "Import verdict (measured)" paragraph below the Cost section; it records the
`import --plan` result for this doc and for the native checkpoint doc.

### Cost

Labels: estimated unless marked measured. Slot wait is reported separately.

- **Ordinary V/R floor (Code)** = the sum of `EstimatedMinutes` = CP-23 20 + CP-24 8 = **28 min** on
  server2. That is one build (143 s warm measured; 579 s loaded p95). The desktop row CP-25 adds **15 min**
  on the desktop lane later (the native doc). The ordinary total is **43 min**.
- **Positive-control floor (Mutation)**, method-scoped, one incremental `Antiphon.Tests` build per mutant
  and per restore (about 90 s each):
  - LS PCs (PC-60..PC-64: 5): 2 builds plus 2 runs of under 30 s each, about 3.5 min each = **18 min**.
  - LP PCs (PC-1..PC-59 and PC-65: 60): 2 builds plus 2 runs of 30-60 s loaded, about 5 min each =
    **300 min**.
  - LN PCs (PC-66..PC-68: 3), on the desktop lane: about 6 min each = **18 min**.
  - Mutants M-1..M-8: 8 × about 5 min = **40 min**.
  - Setup: one cold build, about **10 min** loaded.
  - **Total** = setup 10 + V/R 43 + PC 336 + M 40 = **about 429 min estimated**, serial.
- **Cheaper option (recommended):** the env-gated batch recipe (memory "C1364 temp vector review recipe";
  the S1c TD). Compile all 76 mutants behind `ANTIPHON_MUTANT=<id>` in one build, select one per run, and
  take one lease per run. That is about 3 builds plus 73 server2 runs of about 1 min plus 3 desktop runs,
  for **about 95 min**, a saving of about 330 min. Shard it across 3 detached worktrees on server2 (allowed
  above about 15-20 rows): about **40 min** wall time. No mutant edits source that a witness reads as data.

## Completion and handoff

- guards=68, mapped=68, missing=0, duplicate PC maps=0. All 68 PCs and the 8 extra mutants run: 65 on
  server2, 3 on the desktop lane after the rc run. Cost is numeric (estimated, with measured inputs marked).
- Seams S-1..S-4 are missing at the base and are part of Code.
- T-D-2..T-D-6 are corrections backed by the code facts above, not open preferences. Each is overridable
  (Q-1..Q-3).

## Open questions (each with a default)

- **Q-1 T-D-2 the own-branch proof.** Default: a system release proves containment in the task's own origin
  branch (else the target). The alternative is the target only, which keeps today's rule, but S4a would then
  retire almost nothing (code facts).
- **Q-2 T-D-4 the reopen.** Default: an idle spent attempt reopens for system releases only, bounded by
  backoff and `MaxRefusedAttempts=5`. The alternative is a held tree that stays spent until an operator
  acts. A locked directory would then never retry, which contradicts "defer".
- **Q-3 T-D-5 the system disposition.** Default: `Superseded`, with the origin branch and SHA in the
  reason, and never with an open dependent. The alternative is holding every task with `NextStage` set
  (most pipeline tasks), which would leave most desktop trees.
- **Q-4 rc fence.** Default: none (no path overlap; ≤ 25 removals per hour). The alternative is to skip the
  tick while `native-run.lock` exists (the A-D-9 pattern) and record a Deferred `rc_fence` row.

## Import verdict (measured)

Pending: filled in by this task after `import --plan` ran (see the commit that adds this paragraph's
result).
