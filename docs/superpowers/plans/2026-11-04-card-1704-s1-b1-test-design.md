# CARD-1704 S1 + B1 typed git reads and fewer git processes: test design

TestDesign task 212363b0, 2026-11-04, server2 runner mirror (Linux), base master
`dd30b167ac522e5462f52f3307ef861e648fb0bf`. Scope: slices **S1** and **B1** of the
CARD-1704 plan (`origin/feat/card-task-31e20f5a`,
`docs/superpowers/plans/2026-11-04-card-1704-libgit2sharp-plan.md`, §3.2 B1, §4 S1, D-2, D-3, D-5..D-8), with the
investigations A (`origin/feat/card-task-1cf13574`), B (`…4a75ce55`) and C (`…c7f02607`). This document adds
verification detail. It does not change the fix design, except where the code at the base contradicts the plan
(each such correction is marked **Correction** and is a fact, not a choice). Docs only: no production or test
source was changed, and no test ran. Every expected result below is a design, not executed evidence, except the
`import --plan` verdict at the end.

Nothing here lands on master or rc without the operator's decision (plan §9). Both slices are Code tasks on
their own branches; they run in parallel on server2.

## Code facts used by this design (read at `dd30b167a`, not run)

- **The doubles.** A perl census of `class X … : … (ILandingGit|LandingGit)` over every tracked `.cs` file gives
  52 declarations: `LandingGit` itself, the production subclass `TaskProgressGit`, **36 test subclasses of
  `LandingGit`** (one is `EvidenceGit` in `tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs:274`) and **14 direct
  `ILandingGit` test doubles** (`ControlledLandingGit`, `CustodyGit`, `ScratchLandingGit`, `RemovalFixture`,
  `BlockingPushGit`, `IdleGit`, `PushGit` ×3, `FixedGit`, `PushOnlyGit`, `ScriptedGit`, `PreparationGit`, `NoGit`).
  That is the plan's "51" (50 test doubles plus `TaskProgressGit`).
- **A name collision the plan did not see.** `TaskProgressGit : LandingGit, ITaskProgressGit` already declares
  `public async Task<bool?> IsAncestorAsync(string repository, string ancestorSha, string descendantSha,
  CancellationToken ct)` (`TaskProgressGit.cs:175`), and `RemoteWorkspaceService`/`TaskCompletionProgressService`
  call it through `ITaskProgressGit`. A typed `ILandingGit.IsAncestorAsync` with the same parameter list would be
  hidden by that member in the subclass (CS0108) and the two would answer through different code. `ReadStatusAsync`
  is used by three unrelated test classes. → T-D-1.
- **`LandingGit` layering.** The public virtual `RunAsync`/`RunOwnedAsync` overloads call the private
  `ExecuteAsync` (`LandingGit.cs:90`), which holds the CARD-1432/2b342761f `check-ref-format` memo for `refs/heads/*`
  **below** the virtual layer, then `ExecuteScopedAsync` (profile `Record`, registration invalidation) and
  `ExecuteProcessAsync` (journal, process, kill). The 4-argument `RunAsync(…, LandingGitRunOptions, …)` and the
  internal rebase-head `rev-parse` (`:206`) reach `ExecuteAsync` without the 3-argument virtual. The byte-input path
  `RunGitInputBytesAsync` (`:1076`, `check-attr --stdin`, `cat-file --batch`, `update-ref --stdin`) bypasses
  `ExecuteScopedAsync`, so it is not in `LandingGitProfile` today.
- **The scope.** `BeginOperationScope()` (`:639`) puts an `OperationScope` in an `AsyncLocal`; a nested call returns
  a `BorrowedScope` over the same profile. `OperationScope` caches `Registrations` (stamp-keyed), `Canonical` and
  `Common` (path-keyed, cleared on a topology change). `LandRepositoryLeaseScope`
  (`PreparedLandingVerification.cs:14-59`) creates the scope with the lease, disposes it **before** the lease in
  `ReleaseAsync`, and `BeginReadScope()` makes a new one after `ReacquireAsync`.
- **The real-repository land fixture.** `LandingSafetyHarness` (`TestHelpers/LandingSafetyHarness.cs`) uses
  `LandingGitFixture.FixtureGit : LandingGit` (real git, `LandingGitFixture.cs:160`): `RunAsync` records `Trace` and
  `Commands` (directory + argv) and runs `BeforeCommand`/`BeforeObservedCommand`/`AfterCommand` hooks **before**
  `base.RunAsync`; `RunGitInputBytesAsync` is traced too; `StartProcess` is overridden (timing phase, child census).
  The harness exposes `Logger` (the land's `ILogger<AgentTaskLandService>`). **Correction:** the plan's T-3 says
  "recorded from `ControlledLandingGit.Trace`"; `ControlledLandingGit` is an in-memory git model that starts no
  process. Every process-count and trace witness below uses `LandingSafetyHarness`/`FixtureGit`.
- **The profile line.** `AgentTaskLandService.cs:548-556` logs `Land git profile … processes={Processes} …
  histogram=…` from `LandRepositoryLeaseScope.Profile`; `LandingGitProfile.Record` (`LandingDtos.cs:130`) counts one
  per `ExecuteScopedAsync` process. A's trace2 count for `PostLandMutationDeliveryTests.C478_V09a_LandProducerToCaller`
  is **211** top-level processes at `14703405` (the base adds only `dd30b167a`, a dispatch-time `git cherry` change).
- **E2E evidence.** `EvidenceGit` (`LandDeliveryOptions.cs:274-410`) writes one `protocol-git-<guid>.json` (written
  to `.tmp`, then moved) per `RunAsync`/`RunOwnedAsync` override call that reached `base`, and serves "stable"
  (`--git-common-dir`, `check-ref-format`, `remote get-url`) and volatile reads from its own caches **without** a
  record. `LandVerifierEvidence` writes `protocol-verify-*.json`. `ProtocolProgress()` counts committed records of
  both kinds (`LandDeliveryFixtureProbeTests:137-171`). **`LandDeliveryFixture.InitializeAsync` asserts
  `OperatingSystem.IsWindows()`, ConPty and a native `fakegrok.exe` (`LandDeliveryFixture.cs:59-61`):** the C467
  land receipt methods cannot run on server2. The probe class constructs the fixture without initialising it and
  is portable.
- **Settings.** `Landing` binds with a plain `Configure<LandingSettings>` and no validator (`Program.cs:510`).
  `PublishedBranchDeletionService` reads `IOptionsMonitor<LandingSettings>.CurrentValue` for its kill switch
  (`:47`, `:68`). The CARD-0824 S1c precedent (`DelegationSettingsRegistration.cs`,
  `WorktreeLegacyReleaseShapeValidator`) measured that an array or object written into a scalar is silently ignored
  by the binder, that `Exists()` reports `[]` as absent, and that an invalid hot reload makes **every**
  `CurrentValue` read of that options type throw `OptionsValidationException` until the file is fixed.
- **Layout stamp.** `GitCommonDirectory.Stamp` (`GitCommonDirectory.cs:48-74`) is `dir|real|ctime|commondir` for a
  `.git` directory and `file|real|ctime|mtime|pointer text` for a pointer file. For a linked worktree the
  administrative `commondir` file is **not** in the stamp, and `GIT_DIR`/`GIT_COMMON_DIR`/`GIT_WORK_TREE` in this
  process disable caching.
- **Antiphon ref names** built in `server/` are all of the form `refs/antiphon/(land|progress|retirement)/<32 hex>/…`
  with lowercase-hyphen leaves (`adopt`, `source-observed`, `remote-observed`, `mirror-observed`,
  `recover-source-observed`, `recover-owner-observed`, `settlement-sync`, pin names), plus
  `refs/antiphon/start-ref-probe` and `refs/heads/feat/card-task-<…>`. The 13 remaining `check-ref-format` processes
  per land are the pin and observation refs (`LandingGit.cs:1250, 1288, 1312, 1475`) because the memo keeps only
  `refs/heads/*`.
- **Fault injections keyed on argv B1 could remove.** `LandRequestWriteDiagnosticTests:105` (throws at the first
  `check-ref-format`); `AgentTaskLandIndexLockTests:157,193,408` (create a lock file or record probes at
  `--git-path index.lock`, one of them only after `push`); `LandingGitTests:259` (fails `--absolute-git-dir`/
  `--git-common-dir`); `AgentTaskLandIdentityMatrixTests:97` (fails `--absolute-git-dir` on the source);
  `LandingIdentityControlTests.Replies` (overrides `RunAsync` entirely); `LandingGitBatchingTests:126-140` (asserts
  the trace `check-ref-format, rev-parse, update-ref[, show-ref]`). Identity-argv injections (`symbolic-ref`,
  `show-ref --exists`, `HEAD^{commit}`) appear in about 30 test files (largest: `AgentTaskLandApprovalRecoveryTests`,
  `LandHalfResetFixture`, `PostLandMutationCleanupTests`, `AgentTaskLandIdentityMatrixTests`,
  `CompletedCardWorktreeRemovalTests`, `WorktreeResidueSweepTests`, `AgentTaskLandRefusedRetryTests`,
  `AgentTaskLandPublicationTests`).

## Decisions taken by this design (T-D-n; stated defaults, each overridable)

- **T-D-1 Typed read names do not collide.** The typed defaults are `ReadCommitAsync`, `ReadAncestryAsync`,
  `ReadRefAsync` (Exists/Missing/Error), `ReadSymbolicRefAsync`, `ReadGitDirectoryAsync`, `ReadConfigAsync`,
  `ReadTreeZAsync`, `ReadDiffTreeZAsync` and `ReadWorktreeStatusAsync` (the plan's nine, renamed). A grep at the base
  finds none of these names in `server/`, `tests/` or `tools/`. T-0 (interface map) makes a future collision red.
- **T-D-2 B-1 and B-2 sit below the virtual `RunAsync`, beside the existing memo.** The .NET ref-name check and the
  stamp-keyed path cache live in `ExecuteAsync`, exactly where 2b342761f put the `refs/heads/*` memo. Every
  `RunAsync` override therefore still sees every call: traces, `BeforeCommand` injections and `EvidenceGit` records
  are unchanged, and only the process is saved. This removes all B1a test churn listed above (they fire before
  `base.RunAsync`), and it makes B1a independent of S1's call-site migration.
- **T-D-3 The .NET ref check only ever says "valid".** A name inside the Antiphon grammar returns exit 0 without
  a process; every other name runs `git check-ref-format`. .NET never produces exit 1 or 128, so a refusal reason
  never comes from .NET (plan D-5). The grammar is anchored with `\A … \z` (never `$`, which accepts a trailing
  `\n`).
- **T-D-4 The path cache stores git's answer, never a derivation.** `rev-parse --path-format=absolute
  --git-common-dir`, `rev-parse --absolute-git-dir` and `rev-parse --path-format=absolute --git-path index.lock` are
  asked once per (instance, operation scope, repository argument, argv, stamp). Only exit 0 with one non-empty line
  is kept. The stamp is `GitCommonDirectory.Stamp` **plus, for a pointer file, the resolved administrative
  directory's `commondir` text and creation time** (the gap noted above; the journal's own use of the stamp is not
  changed). The cache dies with the scope, so it never crosses a lease episode. Outside a scope nothing is cached.
- **T-D-5 Cleanup dedupe is limited to inputs that are provably unchanged.** B-1/B-2 already remove the repeated
  ref-name checks and path reads inside the four cleanup inspections. The inspections' `status`/`ls-files`/
  `worktree list` reads and the three `cleanup-observed` remote observations are **not** deduped: the working tree
  and the remote can change between them, and no local stamp proves otherwise. Reusing a remote proof stays the
  operator's Q-8 (default no).
- **T-D-6 B1 is two After groups.** **B1a** = B-1 + B-2 (+T-D-5); it starts now, in parallel with S1. **B1b** = B-3
  identity batch + B-4 pin loops through `ReadRefsAsync`. B1b changes `RunAsync`-level argv, so the ~30 files of
  identity injections must be re-keyed or proven to still fire. Default: B1b runs **after S1 lands** (plan D-3: the
  batch is written once, into the typed methods). The B1b witnesses are argv-agnostic (outcome, reason code,
  process count), so they hold under either order. The brief lists B1b inside "B1"; this split is the deviation,
  and Q-1 lets the caller fold it back.
- **T-D-7 `Landing:GitBackend` is its own options type.** `LandGitBackendSettings { string GitBackend = "Cli" }` binds
  the same `Landing` section through `AddLandGitBackendSettings(IServiceCollection, IConfiguration)` with a grammar
  validator (exactly `Cli`, `LibGit2` or `Compare`, ordinal, untrimmed), a shape validator (the
  `WorktreeLegacyReleaseShapeValidator` pattern for one key: refuses children, a null value and an empty string) and
  `ValidateOnStart`. It is not a `LandingSettings` property, so an invalid hot reload of it cannot make
  `IOptionsMonitor<LandingSettings>.CurrentValue` throw inside the branch-deletion kill switch. **Correction** of
  the plan's file list (`LandingSettings.cs` gains `GitBackend`).
- **T-D-8 The resolver reads the monitor once per `BeginOperationScope()`.** `LandGitBackend.Resolve()` returns the
  effective backend: an `OptionsValidationException` or an unparsable value gives `Cli` plus one Warning per
  distinct bad value; in S1 `LibGit2` and `Compare` give `Cli` plus one `git_backend_not_built` Warning per value.
  The resolver never throws. The default `LandingGit()` constructor has no resolver and means `Cli` (D-7), so no
  test double opts in.
- **T-D-9 The session seam has one test hook.** `internal Func<IGitSession>? SessionFactory` on `LandingGit`: when
  set, `BeginOperationScope()` attaches its session to the new outer scope regardless of the backend (production
  never sets it). Without it the S1 scope never has a session, and SC-2..SC-6 would be unreachable.
- **T-D-10 The observer is per process and never changes git.** `IGitOperationObserver.Completed(GitOperation op)`
  with `op = (Kind: Process|Stdin|InProcess, Repository, Arguments, Backend, Elapsed, int? ExitCode)` is called
  exactly once per **started** process from `ExecuteProcessAsync` and `RunGitInputBytesAsync`, in a `finally`
  (ExitCode null when the call threw), after the journal record is settled. Exceptions from the observer are
  swallowed and counted. Production registers `NoGitOperationObserver.Instance`. `LandingGitProfile.Record` is
  **not** extended to the stdin path in S1, so `processes=` keeps its meaning across the base and both slices.
- **T-D-11 The E2E recorder is its own fixture type.** `tests/Antiphon.E2E/Fixtures/ProtocolGitRecorder.cs`
  (`internal sealed class ProtocolGitRecorder(string root) : IGitOperationObserver`) writes the base JSON fields
  (`repository`, `arguments`, `ExitCode`, `at`, `pid`) plus `backend` and `kind`, through `.tmp` and a move.
  `EvidenceGit` passes it to `LandingGit` and drops `RecordAsync`. Its stable/volatile caches stay.
- **T-D-12 The S1 trace golden is `RunAsync`-level and B1-proof.** T-3 compares the normalised `FixtureGit.Commands`
  (directory class + argv + channel `run`/`owned`/`stdin`) of one harness land with a committed golden recorded at
  the base. Because T-D-2 keeps B1a below that layer, B1a cannot change it. B1b changes it and re-records it in its
  own commit. Normalisation: 40/64-hex → `<oid>`, GUIDs (N and D forms) → `<guid>`, the fixture root →
  `<root>`, directories → `canonical|source|land|remote|other`. Read argv compares as a multiset; mutating argv
  (`LandingGit.IsMutating`) compares in order. Code records the golden at the base three times and commits it only if
  the three are identical (otherwise it reports the nondeterministic entries, which become multiset-only).

## Verification design

### Inspection

Bodies read at `dd30b167a` before naming the cases:

- `ILandingGit.cs` (all 147 lines: the `ReadRefsAsync` default-method precedent at :65-76, `BeginOperationScope`
  default `None`) → T-0..T-2, SC-1.
- `LandingGit.cs` :13-230 (ctor, virtual seams, memo, `ExecuteAsync`/`ExecuteScopedAsync`/`ExecuteProcessAsync`),
  :505-760 (`IsMutating`, `CanonicalDirectoryAsync`, `CommonDirectoryAsync`, registrations, `ChangesRegistrations`,
  `OperationScope`, `HasActiveSequencerAsync`), :796-860 and :1020-1210 (recovery inspection, byte path,
  `IdentityAsync`), :1247-1335 (observation refs), :1355-1376 (`InspectIndexLockAsync`), :1440-1475 → G-1..G-17,
  G-40..G-52, seams S-1..S-5.
- `PreparedLandingVerification.cs` `LandRepositoryLeaseScope` (all) → SC-3..SC-5, G-14..G-16.
- `GitCommonDirectory.cs` (all) → T-D-4, P-3/P-4, G-45/G-46.
- `TaskProgressGit.cs` :1-60, :170-196 → T-D-1, M-1.
- `LandDeliveryOptions.cs` :260-447 (`EvidenceGit`), `LandVerifierEvidence.cs` (all), `LandDeliveryFixtureProbeTests.cs`
  :1-180, `LandDeliveryFixture.cs` :50-75 → E-1..E-4, M-10, the Windows row.
- `LandingSettings.cs`, `Program.cs` :471, :496, :500-515, :1170-1185, `DelegationSettingsRegistration.cs`,
  `WorktreeLegacyReleaseSettings.cs` :60-140, `PublishedBranchDeletionService.cs` :36-70 → T-D-7/8, BS-1..BS-8.
- `LandingDtos.cs` `LandingGitProfile` (:100-170), `AgentTaskLandService.cs` :540-560 → T-3b, P-6.
- Tests and fixtures: `LandingGitFixture.cs` (`FixtureGit`, hooks, `RequiredAsync`), `LandingSafetyHarness.cs`
  (:20-75, `GitOverride`, `Logger`), `LandingGitProcessSavingTests.cs` (all: `CountingGit` overrides `StartProcess`;
  the nearest fixture for `C1704ProcessReductionTests`), `LandingGitTests.cs` :250-265, :425-445,
  `LandingGitBatchingTests.cs` :118-142, `LandingIdentityControlTests.cs` (all), `LandRequestWriteDiagnosticTests.cs`
  :95-125, `AgentTaskLandIndexLockTests.cs` :150-200, :400-430, `AgentTaskLandIdentityMatrixTests.cs` :85-110,
  `PostLandMutationDeliveryTests.cs` :20-60 (C478_V09a), `ControlledLandingGit.cs` :40-260,
  `AgentTaskLandDeliveryE2ETests.cs` :180-240 → fixtures F-1..F-5, R-1..R-12, M-2..M-9.
- Investigation A's histogram and phase split (211; cleanup 82) → P-6, P-14.

### Seams (missing at the base; part of Code)

| # | Needed by | Smallest seam |
|---|---|---|
| S-1 | SC-2..SC-6 | `internal Func<IGitSession>? SessionFactory` on `LandingGit` (T-D-9); `internal IGitSession? CurrentSession` (the live outer scope's session or null). |
| S-2 | O-1..O-5, E-1..E-4 | `LandingGit(CardFileBoardLookup? cardFiles = null, IGitOperationObserver? observer = null, LandGitBackend? backend = null)`; `IGitOperationObserver`, `GitOperation`, `GitOperationKind` in `server/Application/Interfaces/IGitSession.cs`. |
| S-3 | P-6, P-14, O-3 | `FixtureGit.Started` (`List<(string Directory, string[] Arguments)>`) appended in its existing `StartProcess` override from `start.WorkingDirectory` and `start.ArgumentList` (minus the subclass's leading `-c` pairs, which `FixtureGit` does not add). |
| S-4 | P-9 | `internal Action<string, IReadOnlyList<string>, LandingGitResult, LandingGitResult>? SavedCallShadow` on `LandingGit`: when set, a call answered by B-1/B-2 also runs the process and reports (saved, git). Off in production. |
| S-5 | T-3 | `tests/Antiphon.Tests/TestHelpers/C1704LandTrace.cs` (normaliser + comparer, T-D-12) and the golden `tests/Antiphon.Tests/Infrastructure/C1704/land-trace-base.txt`. S1 owns both. If B1b runs before S1 lands, it adopts S1's exact bytes (identical add/add merges cleanly). |

### Fixtures and binding conventions

- **F-1 `C1704Corpus`** (`tests/Antiphon.Tests/TestHelpers/C1704Corpus.cs`, no `[Test]`). Built once per class in
  `[Before(Class)]` under a fresh temp root with the `FixtureGit` environment (`HOME`, empty global config, no
  system config). Each test gets `CloneShared(name)` (`git clone --shared`, about 0.2 s) or the template itself for
  read-only cases. Repositories: `empty` (`init` only, unborn `master`); `plain` (3 commits, annotated tag `v1`,
  lightweight tag `l1`, branch `feat/x`); `detached` (HEAD at a commit); `packed` (refs packed by `pack-refs --all`,
  then `feat/x` also written loose with another oid, so packed and loose disagree); `refs5k` (5,000 refs under
  `refs/heads/bulk/`, built once); `worktrees` (linked worktrees: live, `locked`, `prunable` with its directory
  deleted, and one whose administrative `commondir` is relative); `shallow` (`clone --depth 1` of `plain`);
  `odd-names` (branches `ü/ñ`, `a-b_c.d`, `x/@`, a file named `trailing\n` and one named `tab\tname` committed through
  `update-index --add --cacheinfo` with `-z` input); `ref-lock` (`refs/heads/feat/x.lock` present); `corrupt` (one
  loose object truncated); `ext` (format v1 with `extensions.unknownfoo=true`); `partial` (`clone --filter=blob:none`
  of a file-protocol bare remote, one blob absent). Git oracle: `C1704Corpus.Git(dir, args)` runs git directly (no
  `LandingGit`), returning exit, stdout bytes and stdout text.
- **F-2 `RecordingGit : LandingGit`** (in `C1704TypedReadShapeTests`): overrides both `RunAsync` overloads and
  `RunOwnedAsync`, records `(repository, argv)` and returns a scripted result. **F-2b `DirectFake : ILandingGit`**
  implements only the abstract members (throws `NotSupportedException` from all but `RunAsync`), records `RunAsync`.
- **F-3 `RecordingObserver : IGitOperationObserver`** (thread-safe list; optional `Throw` flag) and
  `ThrowingObserver`.
- **F-4 `LeaseOrder`** (in `C1704SessionScopeTests`): a fake `IRepositoryMutationLease` whose `RepositoryLease`
  disposal appends `"lease"` to a shared log, and a `RecordingSession : IGitSession` that appends `"session"` on
  `Dispose` and counts live instances.
- **F-5 `C1704Settings.Build(json)`**: writes `appsettings.json` into a temp dir, builds
  `ConfigurationBuilder().AddJsonFile(path, optional: false, reloadOnChange: true)`, a `ServiceCollection` with
  `AddLogging`, `Configure<LandingSettings>(section)` (as `Program.cs:510`) and `AddLandGitBackendSettings`, and
  returns the provider plus a `Rewrite(json)` that writes the file and waits up to 10 s for the change token.
- Land witnesses use `LandingSafetyHarness` (one per test, `TestDbFixture` isolated schema), `AddSourceAsync()`,
  `RunAsync()`, and a capturing `ILogger<AgentTaskLandService>` assigned to `Logger` before `InitializeAsync`.
- Every new class: `[Category("Integration")]` and `[ParallelLimiter<GitProcessSpawnLimit>]` (git-only, the
  `PostLandMutationDeliveryTests` lane) except `C1704BackendSettingTests` (`[Category("Unit")]`, no limiter). No test
  sets a process environment variable (M-9).

### Delivery inventory

One delivery path changes, and it is evidence, not product: **git process → observer → protocol-git record →
receipt quiet window.**

| Step | Producer | Destination | Persistence boundary | Recovery | Observable receipt |
|---|---|---|---|---|---|
| 1 | `LandingGit.ExecuteProcessAsync` / `RunGitInputBytesAsync` after the child's journal settles | `IGitOperationObserver.Completed` (synchronous call) | none (in memory) | an observer exception is swallowed and counted (O-4); the git result is unchanged | `RecordingObserver` list (O-1..O-5) |
| 2 | `ProtocolGitRecorder.Completed` | `<root>/protocol-git-<guid>.json` | `.tmp` write then `File.Move` | a crash leaves only `.tmp`, which `ProtocolProgress()` does not count (existing probe) | the committed file (E-1) |
| 3 | `LandDeliveryFixture.ProtocolProgress()` | `UntilProtocolAsync` quiet-window renewal | file count | none needed | the land notification receipt and `AssertOnePromptAsync` (the matching complete UserPrompt transcript), C467 on Windows only |

Durable identity: the record's GUID file name, joined to the land by `repository` and `arguments`. The product
delivery path (land notification → session queue → recipient transcript) is **not changed** by S1 or B1; its
producer-to-recipient witnesses stay as regression rows: `C478_V09a_LandProducerToCaller` (real queue,
`ConfirmLandReceiptAsync … busy: false`) and the busy/recovery methods of `PostLandMutationDeliveryTests` (CP-4,
CP-10). Substitutes and what they cannot prove: E-1..E-4 run the recorder over a real `LandingGit` on Linux and prove
one committed record per process and its counting; they cannot prove that the Windows E2E land stays inside its 60 s
window. Only CP-15 (C467 on the desktop) proves that, and S1 is not complete for E2E until CP-15 is green (the
design does not stop before recipient evidence: CP-15 is a required Windows row, M-10).

### Proves it works now

Classes: **TS** `C1704TypedReadShapeTests`, **OB** `C1704GitObserverTests`, **SC** `C1704SessionScopeTests`, **BS**
`C1704BackendSettingTests`, **K** `C1704ContractTests` (all `tests/Antiphon.Tests/Infrastructure/`, namespace
`Antiphon.Tests.Infrastructure`), **E** `C1704ProtocolGitRecorderTests` (`tests/Antiphon.E2E/`), **PR**
`C1704ProcessReductionTests`, **IB** `C1704IdentityBatchTests`. Each row is recipe | variant/case | oracle |
reachability.

**S1 — TS**
- **T-0** `Typed_reads_map_to_the_interface_or_LandingGit` | reflection over `Antiphon.Server`, `Antiphon.Tests` and
  `Antiphon.E2E` assemblies: every non-abstract type assignable to `ILandingGit` (expected count asserted:
  at least **52** at the base: `LandingGit`, `TaskProgressGit` and the 50 doubles) | for each of the nine typed methods, `GetInterfaceMap` target's
  `DeclaringType` is `ILandingGit` (direct doubles) or `LandingGit` (subclasses) | the census is the decisive
  assertion; a hiding or implicitly implementing member makes it red.
- **T-1** `Default_reads_issue_todays_argv(string method)`, 9 cases | `DirectFake` | exact argv list per method
  (table below) and the parsed result for a scripted exit 0/1/128 | direct call.
- **T-2** `LandingGit_reads_go_through_virtual_RunAsync(string method)`, 9 cases | `RecordingGit` | the same argv
  recorded by the override, and no process started (`StartProcess` override throws) | direct call.

  | Method | argv at the base call sites |
  |---|---|
  | `ReadCommitAsync(r, rev)` | `rev-parse --verify <rev>^{commit}` |
  | `ReadAncestryAsync(r, a, d)` | `merge-base --is-ancestor <a> <d>` (0 true, 1 false, else Error) |
  | `ReadRefAsync(r, fullRef)` | `show-ref --exists <ref>` (0 Exists, 2 Missing, else Error) |
  | `ReadSymbolicRefAsync(r, name)` | `symbolic-ref -q <name>` (1 Detached/NotSymbolic) |
  | `ReadGitDirectoryAsync(r)` | `rev-parse --absolute-git-dir` |
  | `ReadConfigAsync(r, key)` | `config --get <key>` (1 Missing) |
  | `ReadTreeZAsync(r, sha)` | `ls-tree -r -z <sha>` (bytes untrimmed) |
  | `ReadDiffTreeZAsync(r, a, b)` | `diff-tree --no-commit-id --name-only -r -z <a> <b>` |
  | `ReadWorktreeStatusAsync(r, untracked)` | `status --porcelain=v1 -z --untracked-files=<all\|no> --ignore-submodules=none` |

  Code checks each row against the call sites it migrates and keeps the call site's exact argv where a site differs
  (a site that differs keeps raw `RunAsync`, and the report lists it).
- **T-3** `Harness_land_trace_equals_the_base_golden` | `LandingSafetyHarness`, one land (C478_V09a shape without
  the bridge) | `C1704LandTrace.Compare(golden, harness.Fixture.Git.Commands)`: read multiset equal, mutating order
  equal, channel per entry equal; the decisive assertion lists the first differing entry | real git.
- **T-3b** `Harness_land_profile_line_names_the_backend` | same land, capturing logger | the `Land git profile`
  state contains `backend=Cli`, `inProcess=0`, `fallbacks=0`, `mismatches=0`, and `processes` equals
  `harness.Fixture.Git.Started.Count` minus stdin processes | real git.

**S1 — OB** (real `LandingGit` with `RecordingObserver`, `F-1 plain` clone)
- **O-1** `One_record_per_process_with_its_argv` | `ReadCommitAsync`, `ReadRefAsync` (missing), a failing command
  (exit 128) and `RunOwnedAsync` | records == started processes (a `StartProcess`-counting subclass), argv and exit
  equal, `Kind=Process`, `Backend=Cli`.
- **O-2** `Stdin_path_records_once` | `InspectRecoveryCheckoutAsync` path through `check-attr --stdin` and an
  `UpdateRefsAsync` deletion batch | exactly one `Kind=Stdin` record each with the argv.
- **O-3** `Harness_land_records_every_process_once` | `LandingSafetyHarness` with the observer injected through
  `GitOverride = new LandingGitFixture.FixtureGit(…) { … }` built with S-2 | multiset(records) ==
  multiset(`Started`); and every `Commands` entry whose call reached `base.RunAsync` and started a process has a
  record (the base `EvidenceGit` set ⊆ records).
- **O-4** `Observer_exceptions_never_fail_git(string when)`, 2 cases (`process`, `stdin`) | `ThrowingObserver` |
  the result equals the result with no observer; one swallowed count.
- **O-5** `Timed_out_or_thrown_process_records_once` | `RunAsync(…, new LandingGitRunOptions(Budget: 200 ms), …)` on
  a `LandingGit` subclass whose `StartProcess` swaps the start info for a 5 s sleeper (`sleep 5` on Linux,
  `powershell -NoProfile -Command Start-Sleep 5` on Windows; no skip) | `TimeoutException("git_timeout")`, exactly
  one record, `ExitCode == null`.

**S1 — SC** (`LandingGit` with `SessionFactory` = `RecordingSession` factory unless stated)
- **SC-1** `S1_scopes_carry_no_session(string backend)`, 3 cases (`Cli`, `LibGit2`, `Compare`) | no `SessionFactory`,
  resolver over F-5 | `CurrentSession` is null inside the scope; the factory is never called.
- **SC-2** `Nested_scope_borrows_and_only_the_outer_disposes` | outer then inner `BeginOperationScope()` | one
  session; inner `Dispose` leaves it live; outer `Dispose` disposes it; `CurrentSession` null after.
- **SC-3** `Release_disposes_the_session_before_the_lease` | `LandRepositoryLeaseScope` over F-4 | log ==
  `["session", "lease"]`.
- **SC-4** `Reacquire_and_BeginReadScope_open_a_fresh_session` | release, reacquire, `BeginReadScope()` | factory
  called twice; the first instance disposed before the second exists.
- **SC-5** `No_session_outlives_the_lease_scope` | `DisposeAsync` after a release/reacquire/read-scope cycle and after
  a cycle that ended in an exception | live sessions == 0 after each.
- **SC-6** `Sessions_exist_only_inside_a_scope` | no scope | `CurrentSession` null and typed reads run the CLI.
- **SC-7** `A_cli_session_answers_nothing` | `SessionFactory = () => new CliGitSession()`, every typed read on
  `RecordingGit` | the argv trace equals the trace with no session (T-2's table).

**S1 — BS** (F-5)
- **BS-1** `Backend_values_bind(string json, string expected)`, 4 cases: absent → `Cli`; `"Cli"`; `"LibGit2"`;
  `"Compare"` | `IOptions<LandGitBackendSettings>.Value.GitBackend`.
- **BS-2** `Startup_refuses_bad_backend_values(string json)`, 10 cases: `[]`, `{}`, `["Cli"]`, `null`, `""`, `"cli"`,
  `" Cli"`, `"Cli\n"`, `"Libgit2"`, `"1"` | `IStartupValidator.Validate()` throws `OptionsValidationException`
  whose message names `Landing:GitBackend`.
- **BS-3** `Unbuilt_backends_resolve_to_cli_and_log_once(string value)`, 2 cases | `Resolve()` twice | effective
  `Cli` both times; exactly one Warning containing `git_backend_not_built` and the value.
- **BS-4** `Invalid_hot_reload_resolves_to_cli` | start `"Cli"`, rewrite to `[]`, then `"Bogus"`, then `"Compare"` |
  `Resolve()` never throws; `Cli` each time; one Warning per distinct bad value; after the fix the warning stops.
- **BS-5** `Invalid_backend_reload_leaves_landing_settings_readable` | start with
  `DeleteSourceBranchAfterPublication: true`, rewrite `GitBackend` to `[]` | `IOptionsMonitor<LandingSettings>`
  `.CurrentValue.DeleteSourceBranchAfterPublication` is still readable and true.
- **BS-6** `Backend_is_read_once_per_scope` | a counting `IOptionsMonitor<LandGitBackendSettings>`; one scope with 20
  typed reads, then a reload, then a second scope | 1 read per scope; the second scope sees the new value.
- **BS-7** `Program_registers_the_backend_and_observer` | reads `server/Program.cs` from the repo root (the
  `ReleaseGateActivationTests:69` pattern) | exactly one `AddLandGitBackendSettings(` call, no
  `Configure<LandGitBackendSettings>`, and one `IGitOperationObserver` registration of `NoGitOperationObserver`.

**S1 — K** (`C1704ContractTests`, `[Arguments("Cli")]` on each method so S2 only adds `"LibGit2"`; oracle =
`C1704Corpus.Git` on the same repository; 14 methods, 33 executions)
- **K-1** `Commit_resolution(string backend, string repo, string rev, string expected)`: `plain` HEAD; `plain` `v1`
  (annotated → peeled commit, not the tag oid); `plain` `l1`; `detached` HEAD; `empty` HEAD → Missing; `shallow`
  HEAD; `plain` `feat/x\n` → Missing, never the trimmed name (the `\z` class) | 7 cases.
- **K-2** `Ref_tristate(…)`: `plain` existing; missing; `packed` packed-only; `packed` packed+loose (`Exists`, and
  `ReadRefsAsync` returns the **loose** oid, as git does); `ref-lock` `feat/x` (git's verdict, whatever it is,
  equals the typed verdict); `corrupt` → never `Missing` unless git exits 2 | 6 cases.
- **K-3** `Symbolic_head(…)`: `plain` → `refs/heads/master`; `detached` → Detached; `empty` (unborn) →
  `refs/heads/master` | 3 cases.
- **K-4** `Ancestry(…)`: true, false, unrelated, missing object → Error (never false) | 4 cases.
- **K-5** `Git_directory(…)`: main checkout; live linked worktree; worktree with a relative `commondir`; a path
  through a symlink (git's spelling equals the typed answer byte for byte) | 4 cases.
- **K-6** `Tree_bytes_are_untrimmed` (`odd-names`, file `trailing\n` and `tab\tname`) | bytes equal.
- **K-7** `Diff_tree_bytes_are_untrimmed` (`odd-names`, two commits).
- **K-8** `Config_get` (set, unset → Missing, multi-valued key → git's exit).
- **K-9** `Status_bytes` (`plain` dirty with `text=auto` and `eol=lf` files): typed bytes equal git's.
- **K-10** `Refs5k_read_refs_in_order` (`refs5k`, 200 refs with one missing at position 150): the dictionary holds
  exactly the first 149 in order (CARD-1432 G1 semantics).
- **K-11** `Concurrent_external_update_is_seen` (`plain` clone): `ReadRefAsync`, external `update-ref`, read again →
  the new oid (no value cache).
- **K-12** `Unknown_extension_and_partial_clone_answer_like_git` (`ext`, `partial`): every typed read equals git's
  exit and bytes (S2 later expects NotHandled with the CLI answer).
- **K-13** `Unicode_branch_names` (`odd-names` `ü/ñ`, `x/@`): `ReadRefAsync` and `ReadSymbolicRefAsync` equal git.
- **K-14** `Prunable_and_locked_worktrees` (`worktrees`): `ReadGitDirectoryAsync` and `ReadWorktreeStatusAsync`
  on the locked one equal git; the prunable one errors exactly as git does.

**S1 — E** (`tests/Antiphon.E2E/C1704ProtocolGitRecorderTests.cs`, portable, `[NotInParallel("C467LandDelivery")]`
like the probe class)
- **E-1** `Recorder_commits_one_record_per_process` | `LandingGit(observer: new ProtocolGitRecorder(root))` over a
  scratch repo, 3 reads + 1 `update-ref --stdin` | 4 committed `protocol-git-*.json`, no `.tmp`, fields `repository`,
  `arguments`, `ExitCode`, `at`, `pid`, `backend` = `Cli`, `kind` ∈ {`process`, `stdin`}.
- **E-2** `Recorder_records_renew_protocol_progress` | `new LandDeliveryFixture()` root (not initialised) |
  `ProtocolProgress()` == the record count.
- **E-3** `Recorder_write_failure_never_fails_git` | root is a file, not a directory | the git result is unchanged and
  no exception escapes.
- **E-4** `EvidenceGit_stable_cache_hit_writes_no_record` | reflection-built `EvidenceGit` (private nested type,
  `BindingFlags.NonPublic`) twice over `rev-parse --path-format=absolute --git-common-dir` | 1 record (the base
  behaviour kept).

**B1a — PR** (`C1704ProcessReductionTests`; `CountingGit` pattern from `LandingGitProcessSavingTests`)
- **P-1** `Antiphon_grammar_names_match_git(string name)`, 40 cases: 14 grammar names (each server-built shape with
  fresh hex) expected git exit 0; 26 near-misses expected outside the grammar: uppercase hex, 31/33 hex, `.lock`
  leaf, `..`, `@{`, trailing `/`, trailing `.`, leading `.` leaf, space, `~`, `^`, `:`, `?`, `*`, `[`, `\`, control
  `\x01`, `\n` suffix, `\r` suffix, tab, unicode leaf, empty leaf `//`, `refs/antiphon/other/…`,
  `refs/heads/…` (memo, not grammar), `HEAD` | grammar accepted ⇒ git exit 0 (soundness), and `LandingGit.RunAsync`
  exit == git exit for all 40.
- **P-2** `Names_outside_the_grammar_still_start_git` | 5 near-misses | one `check-ref-format` process each, and the
  `refs/heads/*` memo still serves a repeat (2b342761f unchanged).
- **P-3** `Path_answers_come_from_git_once_per_stamp(string query, string layout)`, 6 cases (3 queries × main/linked)
  | in a scope: first call one process, second call none, answers byte-equal to git's (including the symlinked-root
  spelling).
- **P-4** `A_changed_layout_asks_again(string change)`, 4 cases: `repoint` (pointer text), `recreate` (delete and
  re-add the worktree: new ctime), `admin-commondir` (rewrite the linked admin `commondir`), `dir-commondir` | one
  new process after the change.
- **P-5** `Path_cache_is_scope_bound` | answer cached in scope 1; scope disposed; scope 2 asks again; outside any
  scope every call asks.
- **P-7** `Failures_and_injections_are_never_cached` | (a) a non-repository directory → exit 128 twice = 2 processes;
  (b) a `FixtureGit.BeforeCommand` that fails the second `--absolute-git-dir` → the injection fires (trace has 2) and
  the result is the injected failure.
- **P-8** `Trailing_newline_names_are_never_validated_in_dotnet` | `…/source\n`, `…/source\r\n` | git runs and exits 1.
- **P-6** `Harness_land_starts_fewer_processes` | C478_V09a shape, capturing logger | `Started.Count <= BaseStarted
  - 25` and logged `processes <= BaseProfile - 25` (both base constants recorded by Code at `dd30b167a` in the red
  commit, with the measured tip numbers printed to the test output); every `Commands` entry missing from `Started`
  is in the whitelist {`check-ref-format` on a grammar name, the three path queries}; mutating argv in `Started`
  equals mutating argv in `Commands`, in order (no write path changes); the land outcome and the notification body
  `publication=` are unchanged.
- **P-9** `Every_saved_call_equals_git_during_a_land` | the same land with S-4 `SavedCallShadow` set | shadow count
  > 0 (at least 25) and zero mismatches (exit and trimmed output).

**B1b — IB** (`C1704IdentityBatchTests`; after S1 by default)
- **P-10** `Batched_identity_returns_the_same_snapshot` | `plain`/linked worktree | snapshot equals the one from the
  fallback path (S-6: `internal bool DisableIdentityBatch`), fewer processes.
- **P-11** `Identity_refusals_keep_their_codes(string code)`, 6 cases: `detached_head`, `symbolic_head_error`,
  `source_branch_mismatch`, `source_ref_missing`, `source_ref_error`, `source_changed` | real repository states
  (detached, broken HEAD, other branch, deleted ref, unreadable ref via a `FixtureGit` injection on the
  **batch** argv, moved ref) | `InspectAsync.Reason == code`, identical to the fallback-only run.
- **P-12** `Malformed_batch_output_falls_back` | `BeforeCommand` returns exit 0 with a short oid, an extra line, or one
  line missing | today's sequence runs (trace), reason equals the fallback-only run.
- **P-13** `Pin_loops_keep_first_missing_order` | `GuardedWorktreeRemoval`, `LandOperationFactory`,
  `AgentTaskLandSourceResolver` pin sets with the second ref missing | the same refusal and no read past it.
- **P-14** `Harness_land_starts_fewer_processes_after_batching` | P-6 with `BaseStarted - 45` and the B1b whitelist
  (identity argv, per-ref `show-ref --verify --hash`).

### Guards the regression

- **R-1** `LandingGitTests` (39 methods): `C498_RequiredCommandFailureKeepsCommandIdentity` (injections on
  `--absolute-git-dir`/`--git-common-dir`) and `RefFormatHits`/`Processes = Trace.Count - RefFormatHits` (:440)
  stay green: B1a counts its saved calls as `RefFormatHits`/a new `PathHits` and the invariant becomes
  `Processes == Trace.Count - RefFormatHits - PathHits` (the one assertion Code may edit; the decisive part is
  unchanged).
- **R-2** `LandingGitBatchingTests` (4): the trace assertions at :126-140 are `RunAsync`-level and stay byte-equal.
- **R-3** `LandingGitProcessSavingTests` (4), `LandingGitProfileTests` (9): memo and profile unchanged.
- **R-4** `LandRequestWriteDiagnosticTests` (7): the `check-ref-format` throw still fires (T-D-2).
- **R-5** `AgentTaskLandIndexLockTests` (13): lock creation timed on `--git-path index.lock` still fires, including
  after `push`.
- **R-6** `LandingIdentityControlTests` (3 methods, 26 cases), `AgentTaskLandIdentityMatrixTests` (5),
  `LandingRegistrationFreshnessTests` (5): identity decisions and their reason codes.
- **R-7** `LandWorkspaceTests` (7), `PostLandMutationDeliveryTests` (35), `AgentTaskLandCheckpointMatrixTests` (4),
  `AgentTaskLandFailureDiagnosticTests` (8): call-site migration and land reason codes.
- **R-8** `C0824PublishedBranchDeletionTests` (19): `LandingSettings` monitor reads unaffected by T-D-7.
- **R-9** `LandDeliveryFixtureProbeTests` (9): `ProtocolProgress` and the verifier records.
- **R-10** B1b roster: `LandSourceIdentityTests` (5), `AgentTaskLandApprovalRecoveryTests` (68),
  `PostLandMutationCleanupTests` (41), `AgentTaskLandCleanupSafetyTests` (8), `AgentTaskLandRemovalMatrixTests` (3).
  Before writing B1b, Code greps the ~30 identity-injection files (the census command in §Inspection) and, for each
  injection on an argv the batch removes, either shows it still fires (the batch failed first) or re-keys it to the
  batch argv; the report lists each file with its verdict.
- **R-11** Reason-code stability: every land test above asserts its reason; R rows run them unedited except the R-1
  invariant line and R-10 re-keys.
- **R-12** Windows (later, desktop): `LandWorkspaceTests`, `AgentTaskLandHalfResetWindowsTests` (4),
  `AgentTaskLandRegistrationSafetyWindowsTests` (2), `C1704ProcessReductionTests` on NTFS (ctime and junction
  spelling in the stamp), and `AgentTaskLandDeliveryE2ETests.C467_V29_RealOutcomeProducerMatrix_Batch2` (2 cases).

### Masking and reachability

- **M-1** `TaskProgressGit.IsAncestorAsync` would hide a same-named typed default (T-D-1); T-0 is red for any future
  collision, PC-3 proves it.
- **M-2** `ControlledLandingGit` starts no process; using it for T-3/O-3/P-6 would be vacuous (correction above).
- **M-3** `EvidenceGit`'s cache hits start no process, so they write no record before or after S1 (E-4).
- **M-4** The stdin path is not in `processes=` (T-D-10): T-3b subtracts it; P-6 counts `Started` and the profile
  separately so neither masks the other.
- **M-5** B1a saves are invisible to `Commands`/`Trace` by design (T-D-2); P-6/P-14 count `Started`, P-9 shadows.
- **M-6** `LandingIdentityControlTests.Replies` overrides `RunAsync` and never reaches `ExecuteAsync`: B1a cannot
  change it, and its fixture has no `.git`, so a cache bug cannot hide there either. Its decisive power for B1a is
  nil; P-4/P-9 carry the stamp guards instead.
- **M-7** Broken global git config: a .NET-validated pin no longer sees exit 128 at `check-ref-format`. Accepted (the
  2b342761f memo already does this for branches); the land's earlier commands fail first with the same config.
- **M-8** `GitCommonDirectory.Stamp`'s pointer-file form misses an edited administrative `commondir` for the
  journal's own cache. Not changed here (out of scope); reported to the caller.
- **M-9** The git lane is 2-4 wide: no test changes `GIT_DIR`/`GIT_*` in the test process, so the stamp's
  environment override is not exercised (it is the existing helper's, unchanged).
- **M-10** `LandDeliveryFixture` is Windows-only: E-1..E-4 are the portable substitute and CP-15 is required.
- **M-11** In S1 no session is ever attached in production; SC-2..SC-7 are reachable only through S-1, and SC-1 is
  the production witness.

### Guard inventory

| G | Plan ref + safety-critical guard | PC |
|---|---|---|
| G-1 | S1: each typed default issues today's argv | PC-1 |
| G-2 | S1: `LandingGit` typed reads go through the virtual `RunAsync` | PC-2 |
| G-3 | S1/T-D-1: no implementor hides or implicitly implements a typed read | PC-3 |
| G-4 | S1: migrated call sites keep the base argv sequence (no new process spawns) | PC-4 |
| G-5 | S1: mutating commands keep their channel (no write path changes) | PC-5 |
| G-6 | S1: one observer call per `RunAsync` process | PC-6 |
| G-7 | S1: one observer call per stdin process | PC-7 |
| G-8 | S1: a thrown or timed-out process is observed exactly once | PC-8 |
| G-9 | S1: observer exceptions never fail git | PC-9 |
| G-10 | S1: recorder records are counted by `ProtocolProgress` (receipt renewal) | PC-10 |
| G-11 | S1: a recorder write failure never fails git | PC-11 |
| G-12 | S1/D-7: S1 attaches no session for any backend value | PC-12 |
| G-13 | S1: a nested scope borrows; only the outer disposes | PC-13 |
| G-14 | D-6: the session is disposed before the lease is released | PC-14 |
| G-15 | D-6: a new lease episode gets a fresh session | PC-15 |
| G-16 | D-6: no session outlives the lease scope | PC-16 |
| G-17 | S1: an attached CLI session answers nothing (zero behaviour change) | PC-17 |
| G-18 | S1c lesson: array/object/null/empty refused at startup | PC-18 |
| G-19 | S1c lesson: grammar is exact (case, whitespace, unknown) | PC-19 |
| G-20 | T-D-8: an invalid hot reload resolves to `Cli`, never throws | PC-20 |
| G-21 | T-D-7: a bad backend value never breaks `LandingSettings` reads | PC-21 |
| G-22 | D-8: the backend is read once per scope | PC-22 |
| G-23 | S1: `LibGit2`/`Compare` resolve to `Cli` | PC-23 |
| G-24 | T-D-7: `ValidateOnStart` is registered | PC-24 |
| G-25 | T-D-7: `Program` uses the registration extension and the no-op observer | PC-25 |
| G-26 | errors are never absence: ref Error is not Missing | PC-26 |
| G-27 | commit reads peel `^{commit}` (annotated tags) | PC-27 |
| G-28 | a detached HEAD is Detached, not an error | PC-28 |
| G-29 | `-z` bytes are never trimmed | PC-29 |
| G-30 | input names are never trimmed (`\z` class) | PC-30 |
| G-31 | ancestry Error is never `false` | PC-31 |
| G-32 | the profile line carries `backend=Cli` and zero in-process counts | PC-32 |
| G-40 | B-1: the grammar accepts only names git accepts | PC-40 |
| G-41 | B-1: the grammar is `\z`-anchored | PC-41 |
| G-42 | B-1/T-D-3: a name outside the grammar runs git | PC-42 |
| G-43 | T-D-2: the .NET check sits below the virtual `RunAsync` | PC-43 |
| G-44 | T-D-4: path answers come from git, never derived | PC-44 |
| G-45 | T-D-4: the stamp includes the pointer text and ctime | PC-45 |
| G-46 | T-D-4: the stamp includes a linked worktree's admin `commondir` | PC-46 |
| G-47 | P-5: the path cache never crosses a scope | PC-47 |
| G-48 | T-D-4: failures are never cached | PC-48 |
| G-49 | B1: the harness land starts at least 25 fewer processes | PC-49 |
| G-50 | B1: only whitelisted calls lose their process; mutating order unchanged | PC-50 |
| G-51 | B1: every saved answer equals git's (per-call equivalence) | PC-51 |
| G-60 | B-3/D-5: a batch only confirms; refusals come from today's commands | PC-60 |
| G-61 | B-3: batch output is accepted only as full oids, one per input | PC-61 |
| G-62 | B-3: the batched snapshot keeps HEAD and branch separate | PC-62 |
| G-63 | B-4: pin loops stop at the first missing ref | PC-63 |
| G-64 | B1b: at least 20 further processes removed | PC-64 |

Guards = 49, mapped = 49, missing = 0, duplicate PC maps = 0 (G-33..G-39 and G-52..G-59 are unused numbers).

### Positive controls (the mutant list)

Each PC breaks its guard with a compiling defect and expects the named method red at the named assertion. The
Mutation stage runs break/red/restore/green after the land, method-scoped
(`--treenode-filter "/*/*/<Class>/<Method>"`; a method with `[Arguments]` runs all cases and the named case must be
red); zero tests, a build error or a fixture error is not red. Code runs the V/R rows; Review judges before the land.
Files: `IL` `ILandingGit.cs`, `LG` `LandingGit.cs`, `PLV` `PreparedLandingVerification.cs`, `SET`
`LandGitBackendSettings.cs` (+validators, resolver), `PRG` `Program.cs`, `REC` `ProtocolGitRecorder.cs`, `CS`
`CliGitSession.cs`, `CALL` a migrated caller, `GCD` `GitCommonDirectory.cs` (or the B-2 stamp helper), `DBL` a test
double file.

| PC | Break (file) | Expect red |
|---|---|---|
| PC-1 | `IL`: `ReadSymbolicRefAsync` drops `-q` | TS/`Default_reads_issue_todays_argv("symbolic-ref")` at the argv |
| PC-2 | `LG`: `ReadCommitAsync` calls `ExecuteAsync` directly | TS/`LandingGit_reads_go_through_virtual_RunAsync("commit")` at the recorded argv |
| PC-3 | `DBL`: `IdleGit` gains `public Task<GitRefState> ReadRefAsync(string, string, CancellationToken)` returning Exists | TS/`Typed_reads_map_to_the_interface_or_LandingGit` at the census |
| PC-4 | `CALL`: `LandOperationFactory` passes `HEAD` instead of `HEAD^{commit}` / swaps ancestry order | TS/`Harness_land_trace_equals_the_base_golden` at the first differing read |
| PC-5 | `CALL`: `merge --ff-only` moved from `RunOwnedAsync` to `RunAsync` | same method at the channel of the mutating entry |
| PC-6 | `LG`: no observer call on the `ExecuteProcessAsync` success path | OB/`One_record_per_process_with_its_argv` at the count |
| PC-7 | `LG`: no observer call in `RunGitInputBytesAsync` | OB/`Stdin_path_records_once` |
| PC-8 | `LG`: observer called in `try` only (not `finally`) | OB/`Timed_out_or_thrown_process_records_once` at count 1 |
| PC-9 | `LG`: observer call without try/catch | OB/`Observer_exceptions_never_fail_git("process")` |
| PC-10 | `REC`: file prefix `protocol-gitop-` | E/`Recorder_records_renew_protocol_progress` |
| PC-11 | `REC`: drop the IOException catch | E/`Recorder_write_failure_never_fails_git` |
| PC-12 | `LG`: attach `new CliGitSession()` when the effective backend string is not `Cli` before resolution | SC/`S1_scopes_carry_no_session("LibGit2")` |
| PC-13 | `LG`: `BorrowedScope.Dispose` disposes the session | SC/`Nested_scope_borrows_and_only_the_outer_disposes` |
| PC-14 | `PLV`: `ReleaseAsync` disposes the lease before the scope | SC/`Release_disposes_the_session_before_the_lease` at the order |
| PC-15 | `LG`: reuse the previous session in `BeginOperationScope` | SC/`Reacquire_and_BeginReadScope_open_a_fresh_session` |
| PC-16 | `PLV`: `DisposeAsync` returns early when `_lease` is null | SC/`No_session_outlives_the_lease_scope` |
| PC-17 | `CS`: `ReadCommitAsync` returns `Handled(default)` | SC/`A_cli_session_answers_nothing` |
| PC-18 | `SET`: shape validator uses `GetSection(key).Exists()` | BS/`Startup_refuses_bad_backend_values("[]")` |
| PC-19 | `SET`: grammar compares `OrdinalIgnoreCase` | BS/`Startup_refuses_bad_backend_values("\"cli\"")` |
| PC-20 | `SET`: resolver does not catch `OptionsValidationException` | BS/`Invalid_hot_reload_resolves_to_cli` |
| PC-21 | `SET`: register the validators as `IValidateOptions<LandingSettings>` with `GitBackend` on `LandingSettings` | BS/`Invalid_backend_reload_leaves_landing_settings_readable` |
| PC-22 | `LG`: resolve per typed read | BS/`Backend_is_read_once_per_scope` at 1 |
| PC-23 | `SET`: resolver returns the configured value | BS/`Unbuilt_backends_resolve_to_cli_and_log_once("LibGit2")` |
| PC-24 | `SET`: drop `.ValidateOnStart()` | BS/`Startup_refuses_bad_backend_values("[]")` (no throw) |
| PC-25 | `PRG`: keep only `Configure<LandGitBackendSettings>(…)` | BS/`Program_registers_the_backend_and_observer` |
| PC-26 | `IL`: `ReadRefAsync` maps every non-zero exit to Missing | K/`Ref_tristate("corrupt")` |
| PC-27 | `IL`: `ReadCommitAsync` drops `^{commit}` | K/`Commit_resolution("v1")` |
| PC-28 | `IL`: exit 1 of `symbolic-ref` is Error | K/`Symbolic_head("detached")` |
| PC-29 | `IL`: `ReadTreeZAsync` returns `Output.Trim()` bytes | K/`Tree_bytes_are_untrimmed` |
| PC-30 | `IL`: `ReadCommitAsync` trims its revision | K/`Commit_resolution("feat/x\\n")` |
| PC-31 | `IL`: ancestry exit 128 → false | K/`Ancestry("missing")` |
| PC-32 | `LG`/service: omit `backend=` from the profile line | TS/`Harness_land_profile_line_names_the_backend` |
| PC-40 | `LG`: grammar leaf allows `.` | PR/`Antiphon_grammar_names_match_git(".lock leaf")` |
| PC-41 | `LG`: grammar anchored with `$` | PR/`Trailing_newline_names_are_never_validated_in_dotnet` |
| PC-42 | `LG`: a near-miss returns exit 1 without git | PR/`Names_outside_the_grammar_still_start_git` at the process count |
| PC-43 | `LG`: `ObserveWithPrefixAsync` validates in .NET before calling `RunAsync` | R-4 `LandRequestWriteDiagnosticTests` (the throw never fires) **and** PR/`Failures_and_injections_are_never_cached` (b) |
| PC-44 | `GCD`: `--absolute-git-dir` derived from the pointer text | PR/`Path_answers_come_from_git_once_per_stamp("git-dir","linked")` at the symlinked spelling |
| PC-45 | `GCD`: stamp drops the pointer text | PR/`A_changed_layout_asks_again("repoint")` |
| PC-46 | `GCD`: stamp drops the admin `commondir` | PR/`A_changed_layout_asks_again("admin-commondir")` |
| PC-47 | `LG`: path cache on the instance, not the scope | PR/`Path_cache_is_scope_bound` |
| PC-48 | `LG`: cache any exit | PR/`Failures_and_injections_are_never_cached` (a) |
| PC-49 | `LG`: B-2 cache disabled | PR/`Harness_land_starts_fewer_processes` at the count |
| PC-50 | `LG`: also cache `status` answers in the scope | PR/`Harness_land_starts_fewer_processes` at the whitelist |
| PC-51 | `LG`: `index.lock` answered as `<common>/index.lock` | PR/`Every_saved_call_equals_git_during_a_land` (land worktree mismatch) |
| PC-60 | `LG`: batch failure returns `source_ref_missing` directly | IB/`Identity_refusals_keep_their_codes("source_ref_error")` |
| PC-61 | `LG`: accept a batch line shorter than a full oid | IB/`Malformed_batch_output_falls_back` |
| PC-62 | `LG`: `BranchSha` taken from the HEAD line | IB/`Identity_refusals_keep_their_codes("source_changed")` |
| PC-63 | `ReadRefsAsync` caller: continue past a missing ref | IB/`Pin_loops_keep_first_missing_order` |
| PC-64 | `LG`: `DisableIdentityBatch` default true | IB/`Harness_land_starts_fewer_processes_after_batching` |

### Windows-only checks (run later on the desktop; nothing runs there now)

The desktop is running a Windows qualification. These rows run only after it ends, through `-Platform Windows`
with no runner pin: CP-14 (R-12 land classes for S1's moved call sites), CP-15 (C467 E2E receipt, the recipient
evidence for the observer) and CP-16 (B1a's stamp on NTFS: creation time, junction resolution, `C:\` spelling,
plus `AgentTaskLandIndexLockTests`). PC re-runs on Windows are not required: every PC has a portable red method.

### Out of scope

- S2..S5 (libgit2), B-5 remote proof reuse (Q-8), and the journal's own stamp gap (M-8).
- `GitWorkspaceService`, the dispatcher, `TaskProgressGit`'s own methods and the runner (D-7).
- A Windows E2E run now (the desktop is busy); the client; migrations (none).

### Implementation steps (red first, then green)

- **S1:** (1) commit F-1/F-2/F-3/F-4/F-5 and the TS/OB/SC/BS/K/E classes against stub seams that compile
  (typed defaults `throw new NotImplementedException()`, observer never called, settings type absent → BS red);
  record the T-3 golden at the base three times (identical) and commit it; push; (2) implement the typed defaults,
  `LandingGit` impls, observer, recorder, settings, session seam; migrate call sites one file per commit, re-running
  T-3 after each; (3) run the S1 and S1b checkpoint runs (CP-1..CP-6).
- **B1a:** (1) commit PR with base constants measured at `dd30b167a` (`BaseStarted`, `BaseProfile` printed from a
  first run) and S-3/S-4 seams; P-6/P-9 red; push; (2) B-1 grammar, B-2 cache and stamp extension; (3) CP-7..CP-10.
- **B1b:** after S1 lands (default): roster census (R-10), IB red, batch + pin loops, the B1b and B1b2 runs (CP-11..CP-13).

## Performance budget

Owner: [test-performance-standards.md](../../test-performance-standards.md). No test ran in this task; the inputs
are the plan's measured figures.

- **Per-test bodies (estimated).** Corpus and process-reduction cases: one `--shared` clone (about 0.2 s) plus 1-6
  git calls (A: 1.3-6.6 ms spawn, 77 ms for `worktree list` on the real tree): ≤ 2 s, about 3.3 s loaded (×1.65,
  the CARD-1660 ratio). Harness lands (T-3, T-3b, O-3, P-6, P-9, P-14): A measured C478_V09a at **50 s** on loaded
  server2 including the fixture; the C1704 lands omit the bridge, so ≤ 50 s. Settings cases: < 0.5 s plus BS-4/BS-5
  reload waits (≤ 10 s each, the change token usually fires in < 1 s). Session cases: < 0.2 s.
- **Timeouts** (about 2× loaded p95, provisional until the first green TRX): `[Timeout(15_000)]` K, PR, IB, OB, SC
  (land methods override with `[Timeout(120_000)]`); `[Timeout(30_000)]` BS; E `[Timeout(15_000)]`. Code resets each
  from its first green TRX by the same rule.
- **Shared setup.** F-1 templates once per class (`[Before(Class)]`, about 3 s plus the 5,000-ref repository about
  2 s); land methods share `TestDbFixture` (once per assembly). No AppHost, container or database in K/PR/SC/BS/E.
- **Phases.** `TestTiming.BeginPhase(ProcessWait, …)` already wraps `FixtureGit.RunAsync`; template build and each land
  run get their own phase. P-6/P-14 print `started=`, `profileProcesses=` and the elapsed land wall so every run
  records process count against time. Slot wait is reported separately.
- No run reaches 30 minutes (R1). The largest is B1 at about 27 min including its build; S1 and B1b are split
  into two runs each (24 + 22 and 21 + 15 min) for that reason.

### Checkpoints

Lanes: `portable` rows run on server2 (default placement, no `-Runner`/`-Platform`): After groups `S1` and `S1b`
(the S1 Code task, two checkpoint runs), `B1` (the B1a Code task, one run), `B1b` and `B1b2` (the B1b task, two
runs). `windows` rows (After `S1w`, `S1we`, `B1w`) run later on the desktop with `-Platform Windows`, each its own
checkpoint run after the portable group is green at the same commit. Each After group has its own builds, and
every run stays under 30 minutes (R1). `Min` is the TUnit executed floor (`-MinExecuted`),
counted as test methods (a floor; `[Arguments]` cases execute more). Every OR operand is parenthesised with a
trailing `*` and an escaped pipe.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1704-s1/ | portable-c1704-s1-new | /*/*/(C1704TypedReadShapeTests*)\|(C1704GitObserverTests*)\|(C1704SessionScopeTests*)\|(C1704BackendSettingTests*)/* | T-0..T-3b, O-1..O-5, SC-1..SC-7, BS-1..BS-7 | 4 classes, 0 failed/skipped | 24 | 9 | true |
| CP-2 | S1 | CP-1 | portable-c1704-contract-cli | /*/*/C1704ContractTests/* | K-1..K-14 (Cli) | class, 0 failed/skipped | 14 | 4 | true |
| CP-3 | S1 | CP-1 | portable-landing-git-regression-s1 | /*/*/(LandingGitTests*)\|(LandingGitBatchingTests*)\|(LandingGitProcessSavingTests*)\|(LandingGitProfileTests*)/* | R-1..R-3 | 4 existing classes, 0 failed/skipped | 56 | 6 | true |
| CP-4 | S1b | tests/Antiphon.Tests -> bin-c1704-s1b/ | portable-land-regression-s1 | /*/*/(LandWorkspaceTests*)\|(PostLandMutationDeliveryTests*)\|(AgentTaskLandCheckpointMatrixTests*)\|(AgentTaskLandFailureDiagnosticTests*)/* | R-7, R-11 | 4 existing classes, 0 failed/skipped | 54 | 15 | true |
| CP-5 | S1 | CP-1 | portable-identity-settings-regression-s1 | /*/*/(LandingIdentityControlTests*)\|(AgentTaskLandIdentityMatrixTests*)\|(C0824PublishedBranchDeletionTests*)/* | R-6, R-8 | 3 existing classes, 0 failed/skipped | 27 | 5 | true |
| CP-6 | S1b | tests/Antiphon.E2E -> bin-c1704-s1e/ | portable-c1704-e2e-recorder | /*/*/(C1704ProtocolGitRecorderTests*)\|(LandDeliveryFixtureProbeTests*)/* | E-1..E-4, R-9 | 2 classes, 0 failed/skipped | 13 | 7 | true |
| CP-7 | B1 | tests/Antiphon.Tests -> bin-c1704-b1/ | portable-c1704-process-reduction-b1 | /*/*/C1704ProcessReductionTests/* | P-1..P-9 | class, 0 failed/skipped | 9 | 7 | true |
| CP-8 | B1 | CP-7 | portable-landing-git-regression-b1 | /*/*/(LandingGitTests*)\|(LandingGitBatchingTests*)\|(LandingGitProcessSavingTests*)\|(LandingGitProfileTests*)/* | R-1..R-3 | 4 existing classes, 0 failed/skipped | 56 | 6 | true |
| CP-9 | B1 | CP-7 | portable-injection-regression-b1 | /*/*/(AgentTaskLandIndexLockTests*)\|(LandingRegistrationFreshnessTests*)\|(LandRequestWriteDiagnosticTests*)\|(LandingIdentityControlTests*)/* | R-4..R-6 | 4 existing classes, 0 failed/skipped | 28 | 6 | true |
| CP-10 | B1 | CP-7 | portable-land-regression-b1 | /*/*/(PostLandMutationDeliveryTests*)\|(LandWorkspaceTests*)\|(AgentTaskLandIdentityMatrixTests*)/* | R-6, R-7, R-11 | 3 existing classes, 0 failed/skipped | 47 | 8 | true |
| CP-11 | B1b | tests/Antiphon.Tests -> bin-c1704-b1b/ | portable-c1704-identity-batch-b1b | /*/*/(C1704IdentityBatchTests*)\|(C1704ProcessReductionTests*)/* | P-1..P-14 | 2 classes, 0 failed/skipped | 14 | 9 | true |
| CP-12 | B1b | CP-11 | portable-identity-roster-b1b | /*/*/(AgentTaskLandIdentityMatrixTests*)\|(LandSourceIdentityTests*)\|(AgentTaskLandApprovalRecoveryTests*)\|(LandingGitBatchingTests*)/* | R-6, R-10 | 4 existing classes, 0 failed/skipped | 82 | 12 | true |
| CP-13 | B1b2 | tests/Antiphon.Tests -> bin-c1704-b1b2/ | portable-cleanup-roster-b1b | /*/*/(PostLandMutationCleanupTests*)\|(AgentTaskLandCleanupSafetyTests*)\|(AgentTaskLandRemovalMatrixTests*)\|(PostLandMutationDeliveryTests*)/* | R-7, R-10 | 4 existing classes, 0 failed/skipped | 87 | 15 | true |
| CP-14 | S1w | tests/Antiphon.Tests -> bin-c1704-s1w/ | windows-land-regression-s1 | /*/*/(LandWorkspaceTests*)\|(AgentTaskLandHalfResetWindowsTests*)\|(AgentTaskLandRegistrationSafetyWindowsTests*)/* | R-12 | 3 existing classes, 0 failed/skipped | 13 | 20 | true |
| CP-15 | S1we | tests/Antiphon.E2E -> bin-c1704-s1we/ | windows-e2e-land-receipt-s1 | /*/*/AgentTaskLandDeliveryE2ETests/C467_V29_RealOutcomeProducerMatrix_Batch2 | delivery step 3 (recipient) | method, 2 cases, 0 failed/skipped | 2 | 25 | true |
| CP-16 | B1w | tests/Antiphon.Tests -> bin-c1704-b1w/ | windows-process-reduction-b1 | /*/*/(C1704ProcessReductionTests*)\|(AgentTaskLandIndexLockTests*)/* | P-1..P-9 on NTFS, R-5 | 2 classes, 0 failed/skipped | 22 | 20 | true |

How `Min` is counted (test methods, a floor): CP-1 TS 5 + OB 5 + SC 7 + BS 7 = **24**; CP-2 14; CP-3 39+4+4+9 = 56; CP-4 7+35+4+8 = 54; CP-5
3+5+19 = 27; CP-6 E 4 + probe 9 = 13; CP-7 9 (P-1..P-9); CP-8 56; CP-9 13+5+7+3 = 28; CP-10 35+7+5 = 47; CP-11 IB 5 +
PR 9 = 14; CP-12 5+5+68+4 = 82; CP-13 41+8+3+35 = 87; CP-14 7+4+2 = 13; CP-15 2 cases; CP-16 9+13 = 22. Method
counts are `grep -c '[Test]'` per class file at `dd30b167a`.

Roster and suffix check (grep at `dd30b167a`): no `C1704*` class exists. Among the new names no full name is a
prefix of another (`C1704ProcessReductionTests` and `C1704ProtocolGitRecorderTests` diverge at `Pro[c|t]`;
`C1704IdentityBatchTests` is unique). Existing operands each match one class: `LandingGitTests*` does not match
`LandingGitFixture` or `LandingGitBatchingTests` (prefix `LandingGitT`); `PostLandMutationDeliveryTests*` does not
match `PostLandMutationDeliveryWorkerTests` (`…DeliveryT` vs `…DeliveryW`); `PostLandMutationCleanupTests*` is
unique; `LandWorkspaceTests*` is unique; `AgentTaskLandIdentityMatrixTests*` is unique. CP-15's `Batch2` method
segment matches its two `[Arguments]` cases (memory: a plain method segment matches `[Arguments]` cases).

### Cost

Labels: estimated unless stated. Slot wait is reported separately.

- **Ordinary V/R floor (Code)** = the sum of the CP `EstimatedMinutes`:
  - S1 portable: run S1 = CP-1 9 + CP-2 4 + CP-3 6 + CP-5 5 = 24; run S1b = CP-4 15 + CP-6 7 = 22; **46 min** (the
    Antiphon.Tests build p50 326 s / p95 579 s on loaded server2, measured n=117, inside CP-1 and CP-4; the E2E
    build about 5 min inside CP-6).
  - B1a portable: CP-7 7 + CP-8 6 + CP-9 6 + CP-10 8 = **27 min**.
  - B1b portable: run B1b = CP-11 9 + CP-12 12 = 21; run B1b2 = CP-13 15 (build p50 5.4 min plus about 9 min of 87 land methods); **36 min**.
  - Windows (later): CP-14 20 + CP-15 25 + CP-16 20 = **65 min** (provisional: the Windows build is unmeasured).
  - Total portable **109 min**; with Windows **174 min**.
- **Positive-control floor (Mutation)**, method-scoped, one incremental build per mutant and per restore:
  - Antiphon.Tests PCs (47 of 49: all but the E2E-file PC-10/PC-11): an incremental build of about 90 s twice
    plus two method runs (10-50 s): about **4 min each, 188 min serial**.
    The land-method PCs (PC-4, PC-5, PC-32, PC-49, PC-50, PC-51, PC-64: 7) are about 6 min each (+14 min).
  - E2E PCs (PC-10, PC-11): about 4 min each, 8 min.
  - Batching (git-common: different files and methods only): `IL` (PC-1, PC-26..PC-31), `SET` (PC-18..PC-24), `PLV`
    (PC-14, PC-16) and `REC` are separate files; pairing one `IL` mutant with one `SET` mutant per build saves about
    14 builds ≈ **28 min**.
  - Setup: one cold build per project: about 10 + 5 = **15 min**.
  - PC floor ≈ 188 + 14 + 8 - 28 = **182 min serial**; with two detached shards (UNSOURCED sharding, independent
    files) about **100 min wall**.
- **Total** = setup 15 + V/R 109 (portable) + PC 182 = **about 306 min estimated** (65 min Windows V/R separately,
  later). Savings against the plan's rows: CP-6 moves off server2's impossible E2E land method (it would fail, not
  run) to a 7 min portable recorder row plus the Windows CP-15; T-D-2 removes the B1a test churn the plan's P-9 and
  B2 test-churn note expected (no re-keyed injections in B1a: 0 extra rows).

## Effort estimate for Code

- **S1:** about 7 h (plan: 6 h). ~12 production files: `ILandingGit.cs` (9 defaults), new `IGitSession.cs`
  (`IGitSession`, `GitAnswer<T>`, `IGitOperationObserver`, `GitOperation`), `LandingGit.cs` (9 impls, observer at
  two sites, `SessionFactory`, resolver, `CurrentSession`), new `CliGitSession.cs`, new
  `LandGitBackendSettings.cs` (+ validators, resolver, registration), `PreparedLandingVerification.cs` (no ordering
  change; disposal already precedes the lease, so likely untouched), `Program.cs` (2 lines), `LandingDtos.cs` +
  `AgentTaskLandService.cs` (profile fields and `backend=`), the nine callers (mechanical), `LandDeliveryOptions.cs`
  (`EvidenceGit` → recorder) and new `ProtocolGitRecorder.cs`. Tests: 6 new classes (~62 methods with cases), F-1
  corpus, the golden, `FixtureGit.Started`. **Risk to the 51 doubles:** none compile-breaking (defaults);
  behaviour-changing only through a same-signature member, which T-0 makes red; `TaskProgressGit` is the one known
  near-miss (avoided by T-D-1). `EvidenceGit` is the only double whose behaviour changes (records move to the
  observer, plus stdin and options-overload records: more renewals, never fewer).
- **B1a:** about 3 h (plan: 3 h). `LandingGit.cs` (grammar, path cache in `ExecuteAsync`, `PathHits`),
  `GitCommonDirectory.cs` (stamp extension for linked admin `commondir`, exposed as a helper; journal behaviour
  unchanged), `LandingDtos.cs` (`PathHits`), `LandingGitFixture.cs` (`Started`), `LandingGitTests.cs` (one invariant
  line), new PR class.
- **B1b:** about 6 h, mostly the R-10 roster census and re-keys.
- **Parallel S1 + B1a hazard:** both edit `LandingGit.cs` (S1: new methods and two observer calls in
  `ExecuteProcessAsync`/`RunGitInputBytesAsync`; B1a: `ExecuteAsync` and the scope) and `LandingGitFixture.cs`
  (S1 does not need `Started` if B1a adds it first; whichever needs it first adds it, the other adopts the exact
  bytes) and `LandingDtos.cs` (different members). The second to land checks `git merge-tree` against the first and
  re-runs its CP rows at the rebased tip.

## Open questions (each with a default)

- **Q-1 B1b timing.** Default: after S1 lands (T-D-6, plan D-3). Alternative: in the B1a task as a second After
  group now, re-keying the identity injections onto raw argv, then again when S1 migrates them.
- **Q-2 Golden determinism.** Default: exact read multiset + mutating order (T-D-12). If the three base recordings
  differ, the differing entries become count-only and the report names them.
- **Q-3 The journal stamp gap (M-8).** Default: reported, not fixed here; a separate card if the caller wants it.
- **Q-4 Process-count floors.** Default ≥ 25 (B1a) and ≥ 45 (B1b) below the measured base, the plan's P-6/P-11
  floors; Code prints the exact tip counts for the S0 re-measure.

## Import verdict (measured)

Measured on server2, 2026-11-04, one build-slot lease each (waited 0 s and 16 s, held 6 s and 8 s):
`dotnet run --project tools/Antiphon.Checkpoints -p:UseAppHost=false -- import --plan
docs/superpowers/plans/2026-11-04-card-1704-s1-b1-test-design.md --out .antiphon/c1704-td-manifest[-r2].yaml`.

- First import (draft at `04252b78f`): exit 0, `imported 16 rows`, four advisory warnings (derived row timeout above
  45 minutes for CP-13, CP-14, CP-15 and CP-16).
- After the CP-13 estimate was corrected to 15 min (18 counted the build twice): exit 0, `imported 16 rows`, three
  advisory warnings, all Windows rows: CP-14 (60 min derived), CP-15 (75 min) and CP-16 (60 min). They stay: the
  Windows build is unmeasured (the plan's provisional 15 min), and CP-15 runs two C467 cases with a 600 s cap each
  (CARD-1584). The Windows run re-estimates them from its first measured build; none is a portable row.
- The manifest has nine builds (`bin-c1704-s1`, `-s1b`, `-s1e`, `-b1`, `-b1b`, `-b1b2`, `-s1w`, `-s1we`, `-b1w`), one
  per project per After group, and every `minExecuted` equals the `Min` column. No `.antiphon/checkpoints.yaml` was
  left; both manifests were deleted by exact path after the run.
