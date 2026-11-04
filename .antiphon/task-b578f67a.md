# CARD-1704 S1 Code report (task b578f67a)

Outcome: S1 is implemented on `feat/card-task-b578f67a`, red first. Branch only, not landed. The IGitSession seam, the CLI implementation and the observer are in place. The nine typed reads are in place and the callers use them. `Landing:GitBackend` has its own settings type, and the session is owned by the lease scope. The land's git trace and its process count match the base. Every S1 checkpoint row is green at the tested tip except CP-4, where 6 C498 cases fail. Those 6 cases also fail at the base when the same 4-class selection is run there, so they are inherited.

- Branch: `feat/card-task-b578f67a`. Worktree: `/work/worktrees/task-b578f67a`. Original Code task: b578f67a.
- Base: `5449f8f50b9272d6ea468920725724ec88c56d68`.
- Tested tip: `344d40585b8c065fcdbedeeeeca5fe51a06aedbf`. The report commit follows it and contains this file only.
- Plan rows: test design `origin/feat/card-task-212363b0:docs/superpowers/plans/2026-11-04-card-1704-s1-b1-test-design.md` (6c5900297). The checkpoint tool ran an ignored copy of it with `--after S1` and `--after S1b`, and the copy was deleted afterwards.

## Commits

| SHA | What |
|---|---|
| `c6e84673efad316a0f5fd099dd0e35105481cb29` | Red: tests, fixtures and the base golden, on compiling stub seams. Recorded RED: 88 of 89 cases failed. T-0 was green because no double clashes at the base. |
| `97e1faab64ddfdcda371ac0a72beeb51ef16046d` | Implementation: typed reads, observer, settings, session scope, profile line, callers moved, EvidenceGit moved to the recorder. |
| `bdb0641b9088dbbab4ee09560f669554225540a4` | Test fix: E-4 looked up EvidenceGit on the wrong declaring type. It is nested in `LandDeliveryOptions`. |
| `344d40585b8c065fcdbedeeeeca5fe51a06aedbf` | Fix: restore `LandingGit(CardFileBoardLookup? cardFiles = null)` exactly. `C448_C24_KilledWorkerLeavesLiveGitChildFenced` builds LandingGit by reflection with one argument and was red at bdb0641b9. The observer/backend constructor is now a second overload. |

## Seams and defaults

- **`ILandingGit`** has 9 default methods. Each issues today's exact argv through the 3-argument `RunAsync` (`GitReadArguments`): `ReadCommitAsync`, `ReadAncestryAsync`, `ReadRefAsync`, `ReadSymbolicRefAsync`, `ReadGitDirectoryAsync`, `ReadConfigAsync`, `ReadTreeZAsync`, `ReadDiffTreeZAsync`, `ReadWorktreeStatusAsync`.
  - Each returns a typed record (`GitCommitRead`, `GitRefRead`, …) that carries git's own `LandingGitResult`.
  - Exit mappings: show-ref exit 2 is Missing and any other failure is Error; ancestry exit 128 is null, never false; `-z` listings are untrimmed.
- **`LandingGit`** implements the 9 reads publicly and non-virtually. Each read asks the scope's session first, then calls the virtual `RunAsync`. The T-0 census confirms no double hides a read: 51+ types in Server and Tests, plus E-5 for the E2E types.
- **New file `server/Application/Interfaces/IGitSession.cs`** holds `IGitSession`, `GitAnswer<T>`, `IGitOperationObserver`, `GitOperation(Kind, Repository, Arguments, Backend, Elapsed, ExitCode?)`, `NoGitOperationObserver`, `LandGitBackendNames` and the typed records.
- **`CliGitSession`** (`server/Infrastructure/Git/CliGitSession.cs`) is a null object: every read returns NotHandled, so the CLI path runs with today's argv.
- **Observer** (deviation from T-D-10, needed by the base): `LandingGit` calls it once per git call, not once per process.
  - **Process**: called in a `finally` after the journal settles; a timeout or throw reports exit null. **Stdin**: `RunGitInputBytesAsync`. **InProcess**: libgit2 `InProcessGitReads` answers and ref-format memo hits.
  - **Why:** base 5449f8f50 already answers about 116 reads per land in process with libgit2. A per-process observer would have cut EvidenceGit's protocol-git records below the base and shortened the C467 receipt renewals.
  - Observer exceptions are swallowed and counted in `ObserverFailures`. Production uses `NoGitOperationObserver.Instance`.
- **`Landing:GitBackend`** lives in its own `LandGitBackendSettings` (`server/Application/Settings/LandGitBackendSettings.cs`), not on `LandingSettings`.
  - The binder reads only that one key. Two validators run: a grammar check (exact ordinal `Cli`, `LibGit2` or `Compare`, error `git_backend_invalid`), and a shape check that enumerates children and refuses an array, object, null or empty value.
  - `ValidateOnStart` and change-token reload are on.
  - The `LandGitBackend.Resolve()` resolver runs once per outer `BeginOperationScope()` and never throws. An invalid or hot-reloaded-invalid value resolves to Cli with one warning per distinct value. `LibGit2` and `Compare` resolve to Cli with one `git_backend_not_built` warning per value.
  - The default `LandingGit()` has no resolver, which means Cli. `Program.cs` adds `AddLandGitBackendSettings`, `AddSingleton<LandGitBackend>()` and the no-op observer.
- **Session lifetime**: the outer `OperationScope` owns the session and disposes it with the scope (once); a nested scope borrows it. `LandRepositoryLeaseScope` already disposed the scope before the lease, and SC-3 pins that order. The test hook is `internal Func<IGitSession>? SessionFactory`. Production attaches no session for any backend value (SC-1).
- **Profile line** gains `backend=Cli inProcess=<libgit2 hits> fallbacks=0 mismatches=0`. `LandingGitProfile` gains `Backend`, `Fallbacks` and `Mismatches`.
- **Callers moved** to typed reads: AgentTaskLandingProtocol (6 sites), LandOperationFactory (4), AgentTaskLandSourceResolver (5), LandVerificationPolicy (ls-tree), GuardedWorktreeRemoval (8), GuardedVerificationRemoval (7), LandWorkspace (5), and LandingGit's own recovery inspection (diff-tree, ls-tree, config). These sites keep raw `RunAsync` because their argv differs from the typed shapes:
  - `status --untracked-files=no` without `--ignore-submodules`: AgentTaskLandingProtocol, 3 sites.
  - `diff-tree --raw`: LandVerificationPolicy.
  - `show-ref --verify --hash`: several sites.
  - LandRefRetention and LandRecoveryWitness have no typed-shape read.
- **E2E**: `EvidenceGit` writes through the new `ProtocolGitRecorder` (`tests/Antiphon.E2E/Fixtures/ProtocolGitRecorder.cs`). Records keep the same JSON plus `backend` and `kind`, and its caches are unchanged. It now also records the stdin batches and 4-argument calls, so renewals only increase.
- **Corrections to the test design**:
  - O-1 and O-3 assert one record per call, and that process and stdin records equal the started processes. They do not assert records equal processes, because of the libgit2 base above.
  - T-3b asserts `processes <= started` rather than equality: the profile counts only processes inside the scope, and the update-ref batch sits on the stdin path.
  - T-0's census covers the Server and Tests assemblies; E-5 covers E2E.
  - SC-5 also covers a scope whose lease is already gone, so PC-16 has a reachable red.

## Checkpoints (closed list, checkpoint tool, server2, at tested tip 344d40585)

| CP | Run | Executed / passed / failed / skipped | Verdict |
|---|---|---|---|
| CP-1 | 20261104-121935-d93c | 56/56/0/0 | green |
| CP-2 | 20261104-121935-d93c | 33/33/0/0 | green |
| CP-3 | 20261104-121935-d93c | 108/108/0/0 | green |
| CP-5 | 20261104-121935-d93c | 91/91/0/0 | green |
| CP-4 | 20261104-123807-196f | 127/121/6/2 | red, inherited (below) |
| CP-6 | 20261104-123807-196f | 14/14/0/0 | green |

All rows: slot=granted, `source=344d40585…` with sourceState=clean and buildSource=verified. Each run reported "unlisted: none". Waits: S1 rows 30/15/45/90 s and S1b rows 0/0 s.

Earlier runs:

| Run | SHA | Result |
|---|---|---|
| 20261104-103124-92e6 (S1) | 97e1faab6 | GREEN: 56, 33, 108, 91 |
| 20261104-105717-7f0e (S1b) | 97e1faab6 | RED: CP-4 C498 ×6, and CP-6 E-4 from the test bug fixed in bdb0641b9 |
| 20261104-111909-d57a (S1b) | bdb0641b9 | CP-6 green 14/14; CP-4 red, C498 fault matrix ×3 |
| CP-4 row rerun | bdb0641b9 | green 4/4 |

- **CP-4 reds are inherited**: `AgentTaskLandFailureDiagnosticTests.C498_TerminalTransactionFaultMatrix` (4 cases, which wait a fixed 2 s) and `C498_ReplacementBeforeFailureLock` (2 cases).
  - Base 5449f8f50 fails the same 6 cases when the same 4-class CP-4 selection runs in one host process: 129 total, 6 failed, 2 skipped.
  - The 6 cases pass when run alone, both at the base and at the tip: 6/6 each.
  - Inside the row this class ran for 604–825 s.
  - The ControlledLandingGit path these cases use never reaches LandingGit.
- **The 2 CP-4 skips** are `C688_Repair_WindowsOpenFilePreventsResetOrCleanUntilReleased` (clean, reset), which only run on Windows. The design's "0 skipped" expectation for CP-4 cannot hold on Linux.
- CP-14..16 (Windows) and the C467 E2E rows were not run, as briefed. They are for the desktop.

## Zero-behaviour-change evidence

- **T-3 golden**: the base land's RunAsync-level trace was recorded 3 times at 5449f8f50. All 3 recordings were identical once the random land-worktree id was scrubbed: 223 calls. The tip trace equals the golden (CP-1).
- **Real-repository land process count**: unchanged. Base: started=86, profile `processes=84`, RunAsync calls=223. Tip: started=86, `processes=84`, calls=223, and `inProcess=116`, which the base did not print.
- **Land graph** (`-Check`, never `-Update`): HEAD evaluates to digest `6536D70E936DB342C7254DF59E08062E1E20D968AD4FBC1829839D29B6E53D20` count=69, the same as the base. So no land-graph input changed.
  - The brief expected 266595CB…. -Check refuses "manifest does not match" at the base itself on server2, so that refusal is inherited.
  - No policy input changed, so there is no policyHash or PinnedInputDigest commit.
- **Wider land suite**, run by the brief as unlisted extra runs, one lease each, at bdb0641b9 in a scratch worktree:
  - The 48 LandingSafetyHarness classes not covered by CP rows, plus ProgressAwareWaitTests.
  - Chunk A: 568 run, 0 failed, 1 skipped (Windows).
  - Chunk B: 525 run, 11 failed, 5 skipped.
    - 10 of the failures are in `LandNonCodeSkipTests` (C1380 ×8, C1385 ×2). The same 10 fail at the base. The cause is the host land-graph fingerprint mismatch above, so they are inherited.
    - The 11th failure was `RepositoryMutationLeaseTests.C448_C24_KilledWorkerLeavesLiveGitChildFenced`. That was my regression: the reflection constructor, fixed in 344d40585.
  - Re-verified at 344d40585: `RepositoryMutationLeaseTests`, `CardFileBoardLookupTests` and `CardFileBoardLookupRepairTests` ran 79, with 77 passed, 0 failed and 2 skipped (Windows).
- **Mutants run**: none. Mutation owns them. The natural reds were the red commit (88/89) and the reflection-constructor regression caught by C448_C24.

## Unlisted builds and runs (reasons)

- Native `dotnet build tests/Antiphon.Tests`, 3 times, each through build-slot: the red build, the green build and a stub recompile. Dev runs of the 5 new classes: the red run (88/89 failing), 2 runs recording the golden, and one green dev run (89/89).
- In a scratch base/tip worktree:
  - Antiphon.Tests built 4 times, for the base probe (process count), the tip wide runs, the base re-checks and the tip re-check.
  - LandGraphAudit (Release) built once.
  - Runs: base probe 1/1; base C498 6/6; tip C498 6/6 (in this worktree); wide chunks A and B; base LandNonCodeSkipTests (111 run, 10 failed); tip C448 and lease classes; base CP-4 selection.
- The checkpoint tool's own `dotnet run` build happened once.
- Slot waits were 0–180 s, and every run held a lease.

## Not done (deferred; never marked passed)

- **Whole Unit lane** (Final profile): not run. Its estimated cost on loaded server2 is about 3 h (2 lanes, 20 shards), past this task's budget. Owner: the caller or Review.
- **Windows rows** CP-14, CP-15 and CP-16, including the C467 receipt evidence for the observer: these are desktop-only. CP-15 is required before S1 is complete for E2E.
- **PCs pending for Mutation**: PC-1 to PC-32, all of the S1 rows. The B1 PCs are not this slice.

## Other checks

- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 5449f8f50 -HeadRef HEAD`: 3 commits, 0 entries, 0 violations (at bdb0641b9; 344d40585 changes only `LandingGit.cs`).
- `git merge-tree --write-tree origin/master HEAD`: clean. origin/master is still 5449f8f50.
- Cleanup: the scratch worktree was removed; native bin/obj and the kept `bin-c1704-*` outputs were deleted by exact path; the ignored plan copy was deleted. Checkpoint evidence stays under `.antiphon/checkpoints/` (gitignored).
