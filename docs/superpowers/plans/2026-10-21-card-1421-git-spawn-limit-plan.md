# CARD-1421 plan: a wider, separate limiter for git-only spawners in Antiphon.Tests

Card: CARD-1421 (Antiphon board). Evidence: [Antiphon.Tests time profile](../../investigations/2026-10-21-antiphon-tests-time-profile.md), recommendation T1.
Base: `ed419bd2ca34446fa40b6b39ed1573221ae259cb`. Lane: server2 (Linux; `GET /api/runner-defaults` names `server2` as the global runner). No `-Runner` or `-Platform` pin.

## Goal

About 34% of `Antiphon.Tests` discovery nodes queue behind `ParallelLimiter<ProcessSpawnLimit>` (`Limit => 1`).
Many of those classes start only `git`. This card moves the classes whose only child processes are `git` onto a
second assembly-local limiter, `GitProcessSpawnLimit`, with a limit of 2 to 4. Every pty, TUI, fake-agent, pwsh,
dotnet worker, web-host and session-runner spawner stays on the strict one-slot lane. The AGENTS.md rule stays
as it is: each test project has its own limiter types, and no limiter is shared across assemblies.

## Ground truth (what the card assumes vs what the code does)

| Card / brief assumes | Code at base | Consequence |
|---|---|---|
| One limiter, `ProcessSpawnLimit`, limit 1 | `tests/Antiphon.Tests/TestHelpers/ProcessSpawnLimit.cs:13-16`, `Limit => 1`. 412 files in `Antiphon.Tests` carry the attribute, all at class level. Other projects (Pty, PtyHost, SessionRunner, E2E, MarkdownPdf) have their own copies. | Only `Antiphon.Tests` changes. The other projects' copies are untouched. |
| "1,560 nodes / 93 classes spawn only git" (investigation, classified by file) | The census below, which reads what each file and its helpers start, finds **114 files** that start only `git` once 7 lookalikes are excluded. That is about 2,250 `[Test]` + `[Arguments]` rows by source count, not by discovery. | The investigation's figure was a floor. The class list is in Appendix A. |
| Git-only classes can be found by name | Not reliable. `AgentTaskLandVerifierTests` looks like a git test but runs the real `LandingVerifier` (`dotnet build/test`). `AgentTaskLandActiveSourceClaimTests` also runs `cmd.exe /c mklink /J`. `ReviewEvidence*Delivery*` and `TaskParkPublicationTests` reach `BridgeQueueHarness` (`cmd.exe`, `WorkspaceHookRunner`) through `C544DeliveryRig` / `BlockedTaskParkFixture`. | The classification reads spawn sites (D-2). |
| Land harness runs safely in parallel | `AgentTaskLandAdmissionControlledTests` has no limiter and ran 24 tests at once (~13x) in run `ebd2ff29`. `LandingSafetyHarness` gives every test its own GUID temp root, its own `HOME` (`FixtureGit`), an isolated DB clone and a `ControlledVerifier` (no `dotnet`). | Direct evidence that the main git+DB harness tolerates concurrency. |
| Production code under test spawns only git | Server-side non-git spawners: `LandingVerifier`/`AgentTaskLandService.RunProcessAsync` (dotnet), `LandVerificationArtifacts` (dotnet), `WorkspaceHookRunner` (powershell/sh), `WindowsWorktreeLockDiagnostics` (handle tool), `CheckpointReceiptValidator` (pwsh), `RunnerProcessProbe`, `BashTool`. The hooks, handle and probe spawners are registered only in `server/Program.cs:455,483,484,689,838`. `AddDelegationWorktreeGraph` registers none of them. | A candidate is git-only only if it neither constructs one of these nor boots `Program`. `AgentTaskLandSourceFreshnessTests` constructs `new LandingVerifier(...)`, so it stays strict. |
| Shared repos could cause `index.lock` contention | No candidate uses a `ClassDataSource` shared fixture. No candidate writes `git config --global`/`--system`. `ScratchGitRepo` sets identity with repo-local `git config`. `ShellFixtureLineEndingContractTests` runs `git` against the real checkout (`DockerStackDocuments.RepoRoot`). | That class stays strict (D-4). Every other candidate owns its own repo directory. |
| Doc rule | `docs/testing-and-build.md:38` and `:79`; also `:1008`, `:1177`, `docs/release-gates.md:111,215`; AGENTS.md line 61 | S3 updates the testing guide. AGENTS.md keeps its wording, which is still true. |

## Decisions

- **D-1 Limit = `Math.Clamp(Environment.ProcessorCount / 2, 2, 4)`, overridable by `ANTIPHON_TEST_GIT_SPAWN_LIMIT` (1-8).** Reasons:
  - The build-slot broker runs the host at `-maxcpucount:4`.
  - server2 is shared by up to six tasks.
  - The DB clone path stays serialised by its own `SemaphoreSlim(1,1)`. Width beyond 4 would mostly queue there.
  - Setting the override to `1` reproduces today's ordering, so a suspected flake can be bisected without a code change.
  - Rejected: no limiter at all. TUnit would default to processor-count width, which on the desktop means dozens of 11-process fixtures at once, plus Defender scanning and handle pressure on Windows.
  - Rejected: a fixed `4` with no override. It leaves no clean way to bisect a flake.
- **D-2 Classify by what a file and its helpers start, never by name.**
  - A class is git-only when its file, and every helper type it references, has no non-`git` process start. That means no `pwsh`, `powershell`, `bash`/`sh`, `cmd.exe`, `dotnet`, test-assembly worker, `Environment.ProcessPath`, `FakeClaude`/`FakeGrok`/`FakeLlmApi`, `PtyHost`/`ConPty`, `SessionRunnerRuntime`, `AntiphonWebAppFactory`/`WebApplicationFactory`, `RealCliStub*`, `*CrashWorker`/`*Worker` processes, or `mklink`.
  - It also must not construct a production non-git spawner (Ground truth row 5).
  - It must reference at least one git spawner: `LandingGitFixture`/`FixtureGit`, `LandingSafetyHarness`, `ScratchGitRepo`, `StartRefGit`, `ReportWorkspace`, `CheckpointFixtureProcess.Git`, `GitWorkspaceService`, `WorktreeManager`, `DelegationWorktreeService` or a local `*GitAsync` helper.
  - Rejected: name-based classification. It would have moved `AgentTaskLandVerifierTests` and `AgentTaskLandActiveSourceClaimTests`.
- **D-3 Mixed classes stay strict.** A class that also uses `cmd.exe /c mklink` stays on the strict lane, even though a junction helper is short-lived. That includes `AgentTaskLandActiveSourceClaimTests`, the single largest class at about 28 minutes.
  - Moving it needs its own decision on Windows reparse-point races, so it is filed as follow-up F-1 and is not done here.
  - Rejected: "git plus short-lived helpers" as a third category. More rules would be needed to prove it safe than this card can verify.
- **D-4 No git against a shared checkout on the wide lane.** Any class that runs `git` in the developer checkout (`RepoRoot`) stays strict, because the real checkout's `index.lock` is shared with the operator and other tests.
- **D-5 `Checkpoints/*` candidates stay strict for now.** `CheckpointConfinementLedgerTests`, `CheckpointOwnedOutputTests` and `CheckpointSelectedOutputTests` call into `tools/Antiphon.Checkpoints` (build-slot client, executor). This census does not trace that code's spawns. Follow-up F-2.
- **D-6 The census is not shipped as a test.** The roster tests keep their "floor, not census" contract (`docs/testing-and-build.md:38`). Two changes:
  - The strict roster drops the classes that move. Those classes go into a new git roster that asserts `ParallelLimiter<GitProcessSpawnLimit>`.
  - A new test asserts that no class in either roster carries both limiters.
  - Rejected: a source-scanning guard test. It is brittle (the comment-stripping flaw found while planning) and it is scope beyond T1.
- **D-7 Verification runs on Linux (server2).** The Windows file-lock risk is covered by keeping the override knob (D-1) and by the next Windows rc launch-group run, which is the run this card speeds up. A desktop CP would cost more than the change.
- **D-8 Move the whole class, not individual methods.** All 114 candidates apply the limiter at class level. One attribute swap per file; no method-level limiters added.

## Risks and mitigations

| Risk | Where | Mitigation |
|---|---|---|
| Shared repo fixtures / `index.lock` contention | Any test sharing a repo across tests | None of the 114 shares one. Each fixture uses a GUID temp root (`LandingGitFixture.cs:26`, `ScratchGitRepo`). Real-checkout git stays strict (D-4). CP-2 runs `GitIndexLockPathTests` and `AgentTaskLandIndexLockTests` 3x concurrently. |
| Global git config races | `ScratchGitRepo` inherits the user's global config | Global config is read only; no candidate writes `--global`/`--system`. `LandingGitFixture` uses a private `HOME`. |
| Windows file locks (Defender, `git gc --auto` detach, `fsmonitor`) during temp-dir cleanup | Windows rc runs | Width is capped at 4 (D-1). Override to 1 to bisect. Any new Windows cleanup red in the next rc is triaged against `ANTIPHON_TEST_GIT_SPAWN_LIMIT=1` before it is called a regression. |
| Process-tree safety | git children (`git-remote-*`, credential helpers) | No candidate kills a process tree or owns a long-lived child. Each git call is awaited to exit. Pty and worker classes, which own process trees, stay strict. |
| Git lane now overlaps the strict pty/FakeClaude lane | CPU contention could perturb pty timing (the FakeClaude rotation noted in the testing guide) | The lane is small (≤4). CP-3 runs one strict pty class beside two git-lane classes in one host. |
| DB clone serialisation erases the gain | git+DB classes | The gain is bounded but real: `ControlledTests` showed about 13x with clones serialised. Measure wall time in CP-2 (reported, not gated). |
| Bare `[NotInParallel]` / keyed groups on candidates | 15 candidates (e.g. `CardFilePrivacy*`, `MessageQueue`, `AgentQueue`) | Unchanged. Those constraints still serialise their own groups. |

## Slices

### S1: `GitProcessSpawnLimit` and roster tests (test-first)

Files:
- `tests/Antiphon.Tests/TestHelpers/GitProcessSpawnLimit.cs` (new).
  - Implements `IParallelLimit`. `Limit` is computed once from D-1.
  - XML doc: git-only spawners; assembly-local; no pty, worker or shell children.
- `tests/Antiphon.Tests/ProcessSpawnLimitTests.cs`:
  - `Git_lane_limit_is_bounded_between_two_and_four_by_default`: no env var, clamp holds.
  - `Git_lane_limit_honours_the_override`: done through an internal static `GitProcessSpawnLimit.Resolve(int processorCount, string? env)` seam, so the test sets no process environment. Covers `"1"` → 1, `"9"` → clamped 8, garbage → default.
  - `Git_only_classes_carry_the_git_limiter`: roster of the 16 classes in Appendix B that move.
  - `No_class_carries_both_limiters`: over both rosters.

Commit the red tests first: they do not compile or fail until the type and the attribute swaps exist. Then commit the type.

### S2: reclassify the 114 classes

Files: the 114 test files in Appendix A. In each one, `[ParallelLimiter<ProcessSpawnLimit>]` becomes `[ParallelLimiter<GitProcessSpawnLimit>]`. Remove the 16 Appendix-B entries from the strict roster in `ProcessSpawnLimitTests.cs`.

Rules for Code:
- Before each swap, re-check that the file has no non-git signal from D-2, using a grep over the file and its helper types. A file that drifted since the base stays strict and is listed in the report.
- Do not touch any file in Appendix C.

Commit per namespace group (Application A–L, Application M–Z, Infrastructure) so a red bisects to a group.

### S3: documentation

`docs/testing-and-build.md`:
- **Line 38:** replace "Process-spawning tests (...) also carry `[ParallelLimiter<ProcessSpawnLimit>]` so at most one runs at a time per assembly" with a two-lane rule:
  - Classes whose only children are `git`, in fixture-owned repositories, carry `[ParallelLimiter<GitProcessSpawnLimit>]`. Width is 2-4 from processor count; `ANTIPHON_TEST_GIT_SPAWN_LIMIT=1` restores serial order for bisecting (CARD-1421).
  - Every other spawner (pty, pwsh, cmd, dotnet worker, fakeclaude/fakegrok, web host, session runtime) keeps the one-slot `ProcessSpawnLimit` (CARD-0050 S5).
  - A class that mixes git with any other child takes the strict limiter.
  - Classify by what is spawned, not by name.
  - Both are assembly-local and are not shared across test projects.
  - The rosters are floors.
- **Line 79:** "Process-spawning tests share a 1-wide lane" becomes "non-git process-spawning tests share a 1-wide lane; git-only tests a 2-4-wide lane". A failure on either lane is still a real defect unless it also fails at the base commit.
- **Lines 1008 and 1177:** add "(and `GitProcessSpawnLimit`)" where the text says the limiter is per process. At 1177, note that the 46 UIDs/minute figure predates CARD-1421.

AGENTS.md line 61 stays as it is: "carry an assembly-local ParallelLimiter<ProcessSpawnLimit> (one limiter per test project...)". It is still true for non-git spawners, and the owner doc carries the detail. Edit AGENTS.md only if Review reads it as excluding the git lane. If it does, append "; git-only spawners use the wider GitProcessSpawnLimit (docs/testing-and-build.md)".

## Verification design

Minimal by brief. The new tests are the roster and limit tests. They can go red: removing the attribute swap or changing the clamp fails them. The flake check is one repeated run of a contention-prone subset of the moved classes, not of all 114. All 114 at 3x would be hours of git+DB work.

- **V-1:** `GitProcessSpawnLimit` limit is 2-4 by default and honours the override with clamping (S1 tests).
- **V-2:** Moved classes carry the git limiter, strict roster classes keep theirs, none carries both (S1/S2 roster tests).
- **V-3 (no new flakiness):** a repeated run, `--repeat 3`, within the repeat-proof budget (3 normal), of the classes most exposed to git contention. It runs in one host, so the git lane really runs them concurrently with each other:
  - `LandingGitTests`
  - `GitIndexLockPathTests` (the limited class in `GitIndexLockTests.cs`; `GitIndexLockTests` itself is a unit class with no limiter)
  - `AgentTaskLandIndexLockTests`
  - `GitWorkspaceServiceIndexLockTests`
  - `AgentTaskDispatchBaseGuardTests`
  - `CardFilePrivacyConcurrencyTests`
  - `AgentTaskLandConcurrencyTests`

  Zero failures in all three ordinals. Report wall time beside the base's serial sum, for information only.
- **V-4:** one pass with the strict pty class `SessionMessageQueuePtyIntegrationTests` beside two git-lane classes. Zero failures shows the pty lane is not perturbed.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Repeat |
|---|---|---|---|---|---|---|---:|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1421-lanes/` | rosters | `/*/*/ProcessSpawnLimitTests/*` | V-1, V-2 | all pass, 0 failed | 7 | 8 | |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1421-repeat/` | git-lane-repeat | `/*/*/(LandingGitTests*)\|(GitIndexLockPathTests*)\|(AgentTaskLandIndexLockTests*)\|(GitWorkspaceServiceIndexLockTests*)\|(AgentTaskDispatchBaseGuardTests*)\|(CardFilePrivacyConcurrencyTests*)\|(AgentTaskLandConcurrencyTests*)/*` | V-3, run with checkpoint-tool `--repeat 3` and qualified with `--expected-repeat 3` | 0 failed in every ordinal | 150 | 25 | 3 |
| CP-3 | S2 | CP-1 (-NoBuild) | pty-beside-git | `/*/*/(SessionMessageQueuePtyIntegrationTests*)\|(GatedCommitServiceTests*)\|(AgentTaskWorktreeBaseResolverTests*)/*` | V-4 | 0 failed | 60 | 12 | |

Run S1-S2 as one checkpoint-tool run (`--after S2`). CP-2 needs its own build output because the tool requires one per repeat N (testing guide, Repeat proof); it has `bin-c1421-repeat/`, and the table's `Repeat` column carries its 3 so one `run --plan` executes CP-1 once and CP-2 three times. `Min` is a floor taken from source row counts: confirm `executed=` in each CHECKPOINT line. S3 is docs-only and needs no checkpoint.

### Cost

One `Antiphon.Tests` build, the roster row, one repeated subset row and one mixed row: about 45 minutes on server2. Anything beyond this table needs a stated reason.

## Code amendments (task 40c93127)

- **Six Appendix-A classes stay strict.** The census read one file per class. These classes are partial, and a sibling file, or the production seam, starts a non-git child, so D-2 keeps them strict:
  - `AdvisoryChangePolicyTests` (`.Correction.cs`: `CheckpointReceiptValidator`, real `DelegateScriptRunner.RepoRoot`).
  - `AgentTaskLandPublicationTests` (`.Delivery.cs`: `BridgeQueueHarness`). It stays in the strict roster, so the git roster has 15 entries, not 16.
  - `DispatchBaseNotificationTests` (`.Coverage.cs`, `.InspectionDelivery.cs`: `BridgeQueueHarness`).
  - `DispatcherSiblingInspectionSchedulingTests` (`.BriefRecovery.cs`, `.Delivery.cs`: `BridgeQueueHarness`, a Raw `/bin/sh` agent).
  - `PipelineSpeedIntegrationTests` (`.Http.cs`: `C544DeliveryRig`, an `AntiphonWebAppFactory`).
  - `DeliverableBundleServiceTests` (the renderer browser-launch seam, exercised with a fake `fake-browser.sh`).
  - `AgentTaskLandIndexLockTests` (returned after CP-2 run `20261021-215501-51ab`). Its stale-lock assertions read `GitIndexLock.CensusGitProcesses`, a census of every `git` process on the host. A git process that exits mid-census can get `StartUtc = null`, which `HasHolder` counts as a holder. That is the likely reason `C688_AdmissionProbesLandWorktreeAndMainCheckoutWithoutListing` read `git_index_lock_held` in 1 of 3 ordinals. The class is in the strict roster now. Strict only lowers the overlap: git-lane classes can still run beside it (F-4, since closed by CARD-1436).
  104 classes move (107 at the end of task 40c93127; the repair below returned three more).
- **CP-2 names `GitIndexLockPathTests`.** In `GitIndexLockTests.cs` the limiter sits on `GitIndexLockPathTests`. `GitIndexLockTests` is a unit class with no limiter, so the planned `(GitIndexLockTests*)` operand would not have run the git class.
- **CP-2 has its own build, `bin-c1421-repeat/`.** The checkpoint tool refuses to reuse a build for a row with a different repeat count (`reused build ... has a different repeat`).
- **D-1 bisect claim corrected.** Setting `ANTIPHON_TEST_GIT_SPAWN_LIMIT=1` serialises the git lane only. The strict lane and the git lane are separate limiters, so a strict class still runs beside a git-lane class. The override does not reproduce the base ordering. CP-2 rerun `20261021-222525-c7aa` (strict lane) still read `held` in 1 of 3 C688 ordinals.
- **Git hooks count as git.** `LandingGitTests`, `CardFileGitFailureAcceptanceTests` and `GitWorkspaceServiceIndexLockTests` install `#!/bin/sh` git hooks. `git` itself starts those hooks, and `LandingGit`'s budget kill ends them with it. They are treated as git children, not test-started processes.

## Repair amendments (task 4721be7c, Review b529639c D-1)

- **Three more Appendix-A classes stay strict.** `WorktreeGuardedCleanupTests`, `AgentTaskLandHalfResetWindowsTests` and `AgentTaskLandHoldVisibilityTests` call `DirectoryLink.TryCreate` (`tests/Antiphon.Tests/TestHelpers/DirectoryLink.cs`). On Windows that runs `Scripts/Fixtures/directory-link.ps1` through `DirectoryLinkCommand.RunAsync` and `ScriptHarnessProcess`, so the class starts pwsh beside git. The census read `git` only. They carry `[ParallelLimiter<ProcessSpawnLimit>]` again and sit in `StrictClasses`. 104 classes move. `AgentTaskLandIndexLockTests` is not moved.
- **Guard test.** `ProcessSpawnLimitTests.Git_lane_classes_do_not_reach_the_pwsh_backed_link_helpers` reflects over every type that carries `[ParallelLimiter<GitProcessSpawnLimit>]`, its nested types (async state machines, lambda closures, local functions, partial-class siblings compile into the same type), their field, parameter, return and local types and the members their IL names, and fails with one line per reference to `DirectoryLink`, `DirectoryLinkCommand` or `ScriptHarnessProcess`. Red at `a535b5d055ef358efe4fcb8373949be0efccee6e` with 13 references across the three classes; green once they returned to the strict lane. It is a direct-reference guard: a helper that reaches those types through a further test helper is not traced.
- **Checkpoints rerun at the final tip.** CP-1 and CP-2 (`--repeat 3`, now the table's `Repeat` column) run again on server2; CP-3 is not rerun (the repair brief asks for CP-1 and CP-2 only). The CP-2 selection is unchanged from task 40c93127, which already ran it twice at 3; this third run is the code-change rerun the repair brief requires.
- **Landing note.** This plan file was landed on master as `a0e95fadb` (the Plan stage copy, identical to this branch's `e716f79e0`), so `git merge-tree --write-tree origin/master HEAD` reports an add/add conflict on it. A commit-by-commit replay (what landing does) applies cleanly because the base copies are identical; this branch's amended plan is the version to keep.

## Follow-ups (file as cards, not in this card)

- **F-1:** move `AgentTaskLandActiveSourceClaimTests` (git + `cmd /c mklink /J`) to the git lane after a Windows reparse-point concurrency check. This is the largest single gain, about 28 minutes serial.
- **F-2:** trace `tools/Antiphon.Checkpoints` spawns for the three `Checkpoints/*` git candidates (D-5).
- **F-3:** classify the 14 limited files where the census found no spawn signal (for example `LandingAdmissionControlTests`, `OutputDistillationApplyRaceTests`, `BuildSlotClientTests`). They may not need any limiter. Re-measure lane occupancy from the next full Windows rc run.

- **F-4 (closed by CARD-1436, master `76cd76fadc8cac31fbdf6fcf2304400f6ed32f0a`):** `GitIndexLock.CensusGitProcesses` no longer counts a git that exits mid-census (an unreadable start time plus `HasExited` drops it; a live git with an unreadable start still counts), and `LandingGit` takes the census through an `IProcessSource` seam so a fixture can scope it to its own children. A live git older than the lock still counts as a holder, so `AgentTaskLandIndexLockTests` stays on the strict lane.

## Appendix A: classes that move to `GitProcessSpawnLimit` (114 files)

Census over `tests/Antiphon.Tests` at the base:
- Direct non-git signals are found per file.
- One level of helper-type propagation from helpers with direct signals.
- Then a second-level check of the git helper worlds. That check found `C544DeliveryRig` and `BlockedTaskParkFixture` reach `BridgeQueueHarness`; their users are excluded.
- Then a sweep for production non-git spawners.

Rows = `[Test]` / `[Arguments]` attribute counts in the file.

| Class | Namespace dir | Tests / Arguments | Git signals |
|---|---|---|---|
| `AdvisoryChangePolicyTests` | Application | 5 / 0 | LandingGitFixture, WorktreeManager, GitService, DelegationWorktreeService, GitWorkspaceService |
| `AgentReportStoreTests` | Application | 7 / 25 | GitAsync, ReportWorkspace, ReportTempWorkspace |
| `AgentTaskDispatchBaseGuardTests` | Application | 27 / 39 | GitReadAsync, ScratchGitRepo, LandingGit, AddDelegationWorktreeGraph |
| `AgentTaskLandAdmissionTests` | Application | 1 / 8 | LandingSafetyHarness |
| `AgentTaskLandAdoptionConcurrencyTests` | Application | 7 / 0 | LandHalfResetFixture |
| `AgentTaskLandAdoptionTests` | Application | 10 / 15 | LandingSafetyHarness |
| `AgentTaskLandApprovalRecoveryTests` | Application | 68 / 33 | LandingSafetyHarness, LandingProtocolHarness, TwoOwnerLandingWorld |
| `AgentTaskLandBareRepositoryCleanupTests` | Application | 7 / 13 | LandingGitFixture, LandingSafetyHarness, LandHalfResetFixture |
| `AgentTaskLandBoundaryTests` | Application | 8 / 36 | LandingGitFixture, LandingSafetyHarness, WorktreeManager |
| `AgentTaskLandCleanupSafetyTests` | Application | 4 / 26 | LandingSafetyHarness |
| `AgentTaskLandConcurrencyTests` | Application | 4 / 14 | LandingSafetyHarness, DelegationWorktreeService |
| `AgentTaskLandDispatchYieldTests` | Application | 6 / 7 | LandingSafetyHarness |
| `AgentTaskLandHalfResetTests` | Application | 20 / 48 | LandingSafetyHarness, LandHalfResetFixture |
| `AgentTaskLandHalfResetWindowsTests` | Application | 4 / 0 | LandingGitFixture, LandHalfResetFixture |
| `AgentTaskLandHeldNotificationTests` | Application | 10 / 4 | LandingSafetyHarness |
| `AgentTaskLandHoldVisibilityTests` | Application | 3 / 18 | LandingGitFixture, LandingSafetyHarness |
| `AgentTaskLandIdentityMatrixTests` | Application | 5 / 11 | LandingSafetyHarness |
| `AgentTaskLandIndexLockTests` | Application | 12 / 2 | LandingSafetyHarness, GitInAsync, ScratchGitRepo |
| `AgentTaskLandLocalContainmentTests` | Application | 1 / 2 | LandingSafetyHarness |
| `AgentTaskLandNestedRepositoryCleanupTests` | Application | 4 / 6 | LandHalfResetFixture |
| `AgentTaskLandPersistenceFailureTests` | Application | 12 / 50 | LandingSafetyHarness |
| `AgentTaskLandPreparationIdentityTests` | Application | 5 / 13 | LandingSafetyHarness, DelegationWorktreeService |
| `AgentTaskLandPublicationTests` | Application | 23 / 38 | LandingGitFixture, LandingSafetyHarness, FixtureGit, LandingGit, LandingProtocolHarness |
| `AgentTaskLandRefusedRetryTests` | Application | 12 / 6 | LandingGitFixture, LandingSafetyHarness, FixtureGit |
| `AgentTaskLandRegistrationSafetyTests` | Application | 4 / 6 | LandingSafetyHarness, LandingGit, LandHalfResetFixture |
| `AgentTaskLandRemoteSourceRetentionATests` | Application | 1 / 8 | LandingSafetyHarness, Card0452LandingCases |
| `AgentTaskLandRemoteSourceRetentionBTests` | Application | 1 / 8 | LandingSafetyHarness, Card0452LandingCases |
| `AgentTaskLandRemoteSourceRetentionCTests` | Application | 1 / 8 | LandingSafetyHarness, DelegationWorktreeService, Card0452LandingCases |
| `AgentTaskLandRemovalMatrixTests` | Application | 3 / 50 | LandingGitFixture, LandingSafetyHarness, FixtureGit |
| `AgentTaskLandTargetRaceTests` | Application | 9 / 4 | LandingGitFixture, LandingSafetyHarness, FixtureGit, LandingGit |
| `AgentTaskLocalMergeSafetyTests` | Application | 1 / 9 | LandingSafetyHarness, DelegationWorktreeService |
| `AgentTaskProjectScopeTests` | Application | 6 / 0 | ScratchGitRepo |
| `AgentTaskScopeOverlapCreateTests` | Application | 6 / 0 | ScratchGitRepo |
| `AgentTaskWorktreeBaseCreateTests` | Application | 5 / 23 | GitReadAsync, ScratchGitRepo, LandingGit |
| `AgentTaskWorktreeBaseResolverTests` | Application | 16 / 66 | GitReadAsync, ScratchGitRepo, LandingGit |
| `CardDoneWorktreeCleanupTests` | Application | 20 / 0 | CompletedCardCleanupFixture |
| `CardFileBoardLookupTests` | Application | 6 / 0 | LandingGit, GitWorkspaceService, CardFilePrivacyWorld, CardFileTestRepository |
| `CardFileGitFailureAcceptanceTests` | Application | 3 / 6 | GitReadAsync, ScratchGitRepo, CardFileRepository |
| `CardFileIgnoreAcceptanceTests` | Application | 6 / 8 | GitAsync, ScratchGitRepo, CardFileRepository, CardFilePrivacyWorld, CardFileTestRepository |
| `CardFileIgnoreTests` | Application | 4 / 6 | GitReadAsync, ScratchGitRepo, CardFileRepository, CardFilePrivacyWorld |
| `CardFilePrivacyBoundaryAcceptanceTests` | Application | 4 / 0 | GitReadAsync, CardFilePrivacyWorld |
| `CardFilePrivacyConcurrencyTests` | Application | 4 / 8 | GitInAsync, ScratchGitRepo, CardFilePrivacyWorld, CardFileTestRepository |
| `CardFilePrivacyHostedServiceTests` | Application | 2 / 2 | GitReadAsync, CardFilePrivacyWorld, CardFileTestRepository |
| `CardFilePrivacyOwnershipAcceptanceTests` | Application | 6 / 13 | GitReadAsync, CardFilePrivacyWorld |
| `CardFilePrivacyOwnershipTests` | Application | 5 / 0 | ScratchGitRepo, CardFilePrivacyWorld |
| `CardFilePrivacyPersistenceTests` | Application | 2 / 0 | ScratchGitRepo, CardFilePrivacyWorld |
| `CardFilePrivacyRecoveryTests` | Application | 3 / 6 | GitReadAsync, CardFileRepository, CardFilePrivacyWorld |
| `CardFilePrivacySyncAcceptanceTests` | Application | 6 / 33 | GitReadAsync, CardFilePrivacyWorld |
| `CardFilePrivacySyncTests` | Application | 9 / 20 | GitReadAsync, CardFilePrivacyWorld |
| `CardPrivateNotesBoundaryTests` | Application | 2 / 0 | CardFilePrivacyWorld |
| `CardTaskFileServiceTests` | Application | 21 / 0 | GitReadAsync, ScratchGitRepo, GitWorkspaceService, CardFileRepository |
| `CardWorktreeCleanupRecoveryTests` | Application | 6 / 0 | CompletedCardCleanupFixture |
| `CheckpointSourceApprovalTests` | Application | 4 / 0 | LandingSafetyHarness, C544World, C544Land, LandingProtocolHarness |
| `CommitOnSettlePolicyTests` | Application | 8 / 6 | ScratchGitRepo |
| `DelegationScopeHoldTests` | Application | 10 / 0 | AddDelegationWorktreeGraph |
| `DeliverableBundleServiceTests` | Application | 15 / 11 | "git", GitWorkspaceService |
| `DispatchBaseInspectionDiagnosticsTests` | Application | 2 / 12 | SiblingInspectionFixture |
| `DispatchBaseNotificationTests` | Application | 11 / 12 | GitAsync, ScratchGitRepo, LandingGit, AddDelegationWorktreeGraph |
| `DispatchConcurrencyPipelineTests` | Application | 5 / 0 | DispatchConcurrencyShop, DispatchConcurrencyWireHost, ConcurrencyDispatchWorld |
| `DispatcherSiblingInspectionSchedulingTests` | Application | 3 / 32 | GitReadAsync, ScratchGitRepo, SiblingInspectionFixture |
| `DispatchSiblingInspectionTests` | Application | 6 / 32 | GitAsync, ScratchGitRepo, LandingGit, AddDelegationWorktreeGraph |
| `GatedCommitServiceTests` | Application | 21 / 34 | GitInAsync, ScratchGitRepo, LandingGit, GitWorkspaceService, RecordingGitWorkspaceService |
| `GitIndexLockTests` | Application | 5 / 7 | LandingGitFixture |
| `GitWorkspaceServiceIndexLockTests` | Application | 3 / 0 | GitInAsync, ScratchGitRepo, GitWorkspaceService |
| `InterimVerificationLandGitTests` | Application | 3 / 0 | LandingGitFixture, LandingSafetyHarness, ObserverGitAsync, ScratchGitRepo, FixtureGit, C544World |
| `InterimVerificationLandGuardTests` | Application | 13 / 0 | C544World, C544Land, LandingProtocolHarness |
| `InterimVerificationPolicyTests` | Application | 19 / 0 | GitAsync, ScratchGitRepo, C544World |
| `LandDispatchHandoffTests` | Application | 1 / 0 | RepairSourceWorld |
| `LandRequestWriteDiagnosticTests` | Application | 7 / 11 | LandingSafetyHarness |
| `LandRestartReplayTests` | Application | 1 / 0 | LandingSafetyHarness |
| `LandSourceIdentityTests` | Application | 5 / 21 | LandingGitFixture |
| `MatrixChildReceiptTests` | Application | 4 / 0 | ScratchGitRepo |
| `OutputDistillationDispatchTests` | Application | 1 / 2 | AddDelegationWorktreeGraph |
| `PipelineSpeedIntegrationTests` | Application | 5 / 0 | C544World |
| `ProjectReadinessBatchTests` | Application | 2 / 0 | SkipIfGitUnavailableAsync, ScratchGitRepo |
| `ProjectSetupServiceTests` | Application | 10 / 2 | GitReadAsync, "git", ScratchGitRepo, GitWorkspaceService, CardFilePrivacyWorld, CardFileTestRepository |
| `PublishedCardCompletionTests` | Application | 7 / 0 | LandingSafetyHarness |
| `RepairSourceDispatchTests` | Application | 19 / 30 | LandingSafetyHarness, GitInAsync, "git", ScratchGitRepo, LandingGit |
| `RepairSourceLandRefusalTests` | Application | 4 / 3 | GitInAsync, ScratchGitRepo, RepairSourceWorld |
| `RepairSourceRecoveryLandingTests` | Application | 2 / 3 | GitInAsync, ScratchGitRepo, RepairSourceWorld |
| `ReviewEvidenceRebindingTests` | Application | 15 / 0 | LandingGitFixture |
| `ReviewEvidenceResettlementTests` | Application | 10 / 0 | RunnerSettlementWorld |
| `ReviewEvidenceSettlementTests` | Application | 11 / 0 | LandingSafetyHarness, C544World |
| `RunnerAlarmCoordinatorTests` | Application | 11 / 0 | GitAsync, ScratchGitRepo, LandingGit |
| `RunnerAlarmHostedServiceTests` | Application | 6 / 0 | ScratchGitRepo, LandingGit |
| `RunnerSettlementSyncTests` | Application | 30 / 0 | "git", LandingGit |
| `RunnerTaskSettlementTests` | Application | 28 / 6 | C544World, RunnerSettlementWorld |
| `SettlementSyncDebtLandingTests` | Application | 1 / 0 | LandingSafetyHarness |
| `SettlementSyncRecoveryTests` | Application | 15 / 14 | RunnerSettlementWorld |
| `SourceBundleManifestTests` | Application | 2 / 0 | GitWorkspaceService |
| `StartRefAvailabilityTests` | Application | 12 / 2 | SkipIfGitUnavailableAsync, StartRefGit, LandingGit |
| `StartRefRepairAdoptionTests` | Application | 6 / 4 | LandingSafetyHarness |
| `TaskProgressGitTests` | Application | 8 / 5 | GitInAsync, ScratchGitRepo, LandingGit, GitWorkspaceService |
| `TaskWorktreeRetirementTests` | Application | 19 / 29 | SettledRemovalHarness |
| `VerificationRoundDispatchTests` | Application | 9 / 0 | GitAsync, C544World, ControlledInterimReadiness, C544Land |
| `VerificationRoundSettlementTests` | Application | 10 / 0 | DelegationWorktreeService, C544World, C544Land |
| `WorktreeDefaultAdmissionTests` | Application | 6 / 0 | GitInAsync, ScratchGitRepo, RepairSourceWorld |
| `WorktreeDefaultContinuationTests` | Application | 8 / 0 | GitInAsync, ScratchGitRepo, RepairSourceWorld |
| `WorktreeResidueSweepTests` | Application | 15 / 0 | GitAsync, ScratchGitRepo, WorktreeManager, SettledRemovalHarness |
| `WorktreeStartRefDispatchTests` | Application | 10 / 26 | GitInAsync, ScratchGitRepo, RepairSourceWorld |
| `CompletedCardWorktreeRemovalTests` | Infrastructure | 36 / 0 | CompletedCardCleanupFixture, CompletedCardRemovalFixture |
| `GitDiffSpikeTests` | Infrastructure | 5 / 0 | "git", GitService |
| `LandingGitTests` | Infrastructure | 24 / 44 | LandingGitFixture, ScratchGitRepo, FixtureGit, LandingGit |
| `LandingRegistrationFreshnessTests` | Infrastructure | 4 / 5 | LandingGitFixture, FixtureGit, LandingGit |
| `LandingSourceFreshnessTests` | Infrastructure | 21 / 0 | LandingGitFixture, LandingGit, LandingProtocolHarness |
| `LandRecoveryCheckoutTests` | Infrastructure | 12 / 30 | LandingGitFixture |
| `LandRecoveryIgnoredNestedRepositoryTests` | Infrastructure | 3 / 15 | LandingGitFixture |
| `LandRecoveryIgnoredObstructionTests` | Infrastructure | 6 / 2 | LandingGitFixture |
| `LandWorkspaceTests` | Infrastructure | 7 / 2 | LandingGitFixture, WorktreeManager, LandingGit |
| `RepositoryChildJournalInspectorTests` | Infrastructure | 7 / 0 | ScratchGitRepo, LandingGit |
| `RepositoryFenceObserverTests` | Infrastructure | 1 / 0 | ScratchGitRepo, LandingGit |
| `SlowTestRegistryMergeTests` | Infrastructure | 3 / 4 | LandingGitFixture |
| `WorktreeGuardedCleanupTests` | Infrastructure | 44 / 7 | LandingSafetyHarness, LandingGit |
| `WorktreeManagerStartRefLocalOnlyTests` | Infrastructure | 2 / 0 | SkipIfGitUnavailableAsync, StartRefGit, WorktreeManager |

## Appendix B: strict-roster entries that move to the git roster

`AgentTaskDispatchBaseGuardTests`, `AgentTaskLandAdmissionTests`, `AgentTaskLandBoundaryTests`, `AgentTaskLandCleanupSafetyTests`,
`AgentTaskLandConcurrencyTests`, `AgentTaskLandIdentityMatrixTests`, `AgentTaskLandPersistenceFailureTests`,
`AgentTaskLandPreparationIdentityTests`, `AgentTaskLandPublicationTests`, `AgentTaskLandRemovalMatrixTests`,
`AgentTaskLandTargetRaceTests`, `AgentTaskLocalMergeSafetyTests`, `DelegationScopeHoldTests`, `GitDiffSpikeTests`,
`LandSourceIdentityTests`, `LandingGitTests`.

Every other entry in the current strict roster stays: it is mixed, other, or had no signal found.

## Appendix C: look like git tests but stay strict

| Class | Why |
|---|---|
| `AgentTaskLandVerifierTests` | Runs the real `LandingVerifier` (`dotnet build/test`). |
| `AgentTaskLandSourceFreshnessTests` | Constructs `new LandingVerifier(buildSlots: ...)`. |
| `AgentTaskLandActiveSourceClaimTests` | `cmd.exe /c mklink /J` (D-3, F-1). |
| `ReviewEvidenceConsistencyTests`, `ReviewEvidenceDeliveryTests`, `PipelineHandoffPilotTests`, `ContinuationSettlementTests`, `CompletionWarningDeliveryTests` | `C544DeliveryRig` → `BridgeQueueHarness` (`cmd.exe`, `WorkspaceHookRunner`). |
| `TaskParkPublicationTests` | `BlockedTaskParkFixture` → `BridgeQueueHarness`. |
| `ShellFixtureLineEndingContractTests` | git in the real checkout (D-4). |
| `CheckpointConfinementLedgerTests`, `CheckpointOwnedOutputTests`, `CheckpointSelectedOutputTests`, `CheckpointTrackedBaselineTests` | Checkpoint tool code paths (D-5); `TrackedBaseline` also runs `run-checkpoint.ps1`. |
