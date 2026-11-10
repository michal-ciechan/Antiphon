# CARD-1546: Antiphon.Server.Git and its mirrored test project

Date: 2026-11-09. Plan owner: task `35f8c58e`. Source read at
`09d80f11a71cffa03edebc2866b561a8d0962fcb` on `feat/card-task-35f8c58e`.
Umbrella: CARD-1729. Spec: [solution split by dependency](../specs/2026-11-05-solution-split-by-dependency.md).
Phase 0 plan: [2026-11-05-card-1729-phase0-plan.md](2026-11-05-card-1729-phase0-plan.md).
This plan is the Git slice. It does not edit product code.

**Outcome.** Create `src/Antiphon.Server.Git` with no reference to the server, and a mirrored
`tests/Antiphon.Server.Git.Tests`. The first closed slices move only the two types that are cheap
at this commit (`ServerGitProcess`, `GitIndexLock`) and the one record they need
(`LandingIndexLockObservation`). `LandingGit` stays in the server until a port exists for
`CardFileBoardLookup`. The spec's "446 tests" figure is a placement-rule estimate, not the count
this card moves.

Runner defaults were read for this plan (revision 2, no kind overrides; one Windows row, one Linux
row, one retired row). Every checkpoint names its lane and omits `-Runner` and `-Platform`.
Nothing in this plan runs on the desktop.

## Ground truth

| Assumption | Code at `09d80f11` | Consequence |
|---|---|---|
| CARD-1546 (2026-10-23) asked for a category mapping and said not to move tests unless cheap. | Operator 2026-11-10 and CARD-1729 require a mirrored test project and a move of what is cheap. | This plan moves the cheap types and the tests that do not construct server fixtures. |
| Spec Appendix A places all of `server/Infrastructure/Git`, WorkspaceHooks, Verification, and four Application services in `Antiphon.Server.Git` (about 12.4k lines) with 446 mirrored tests and 0 Postgres. | `server/Infrastructure/Git` is 39 files, 11,129 lines. `LandingGit.cs` is 2,024 lines and its constructor takes `CardFileBoardLookup?` and `IOptions<GitSettings>` (`LandingGit.cs:57-64`). Phase 0 has not added `IGitWorkspace`, `ICardFileBoardLookup`, or `ILandGit`. | Moving `LandingGit` now would make Git reference the server. It stays. |
| `GitProcessSpawnLimit` is production. | It is the test limiter `tests/Antiphon.Tests/TestHelpers/GitProcessSpawnLimit.cs`. | It stays in `Antiphon.Tests`. The new test project does not take a copy in S3. |
| A new `src/` project needs a Dockerfile edit. | Root `Dockerfile:22-26` already `COPY src/ src/` and then publishes `server/Antiphon.Server.csproj`. | No Dockerfile edit. A `ProjectReference` under `src/` is pulled in by that publish. |
| The session-runner image should publish every new server library. | `docker/session-runner-grok/Dockerfile:39-41` publishes SessionRunner, PtyHost, and FakeGrok only. | It must not gain a Server.Git publish. |
| `P-SERVER` will narrow when files leave `server/`. | `tests/test-selection-policy.json` rule `P-SERVER` is `^server/.*$` with `full: true`. An unmatched path is `P-UNKNOWN` and `unknown-path:` (`scripts/lib/test-selection.ps1:328-340`), and any fallback selects every set. | S2 adds no selection rule. A narrow rule before the covering tests are members would drop them. |
| The layering map must be edited when two service files move. | `tests/Antiphon.Tests/Layering/layers.txt:15` uses a brace that still matches `GitWorkspaceService.cs` and `OrchestratorWorkspaceLayout.cs`. A rule dies only when no glob matches (`LayeringGuardEngine.cs:89-90`). | No `layers.txt` edit in S1-S3. |
| An allowlist row whose target left the server is ignored while `strict: false`. | `ParseAllowlist` emits `unknown-path` when either path is outside the server census (`LayeringGuardEngine.cs:146`), and the guard fails on that error. | The six rows below are deleted in S2. |
| Adding a test project is a one-line disposition. | `Test-C1313_ProjectAndCatalogUniverse` (`scripts/test-test-selection.ps1:30`) pins primary count `7 + observability`, rc required-suite count `9 + observability`, and nightly required-suite count `7 + observability`. Observability is present, so those counts are 8, 10, and 8. `selectionCatalog.hash` is `c62da85de8c20bbbde124e9449d318d6ea335b49827802bb6d25d312552ba2c2`. `policyHash` is `fdc043758617a5e8c21735126539c559faf9dad95abe5f1122a6d5780ee9df4d`. | S3 bumps each literal by one and recomputes both hashes. It does not hand-edit a guessed hash. |
| CARD-1729 Phase 0 must finish before any Git project exists. | Landed CARD-1729 commits on this base are the P0-A1 value types and the P0-C4 probe/audit ports only. `IOrchestratorWorkspaceProbe` and `IAuditService` are registered (`server/Program.cs`). The strict layering allowlist is still `strict: false`. | This card does not wait. It does not create Core, Persistence, Integrations, Runners, or Application. |

`Antiphon.Tests` has 14,855 `[Test]` attributes at this commit. Do not re-measure the spec's 14,415 or 446 as current.

## Intended behaviour

There is no runtime state machine. Both moved types are static. `Program.cs` does not register them.
The change is which assembly owns the types, and which project is allowed to reference which.

Before:

- `ServerGitProcess` (`server/Application/Services/ServerGitProcess.cs:1-57`, namespace
  `Antiphon.Server.Application.Services`) sets `gc.auto=0`, `maintenance.auto=false`, and
  `GIT_OPTIONAL_LOCKS=0` on a `ProcessStartInfo`. Its usings are `System.Diagnostics` and
  `System.Globalization` only.
- `GitIndexLock` (`server/Application/Services/GitIndexLock.cs:1-257`, same namespace) classifies
  `.git/index.lock`. It uses `LandingIndexLockObservation`.
- That record is `server/Application/Dtos/LandingDtos.cs:6-13`, namespace
  `Antiphon.Server.Application.Dtos`. The rest of that file (through line 204) uses
  `Antiphon.Server.Domain.Enums` and stays.
- `ILandingGit.InspectIndexLockAsync` (`server/Application/Interfaces/ILandingGit.cs:129`) returns
  the record. The interface stays in the server.
- The server project references GitInProcess, Resilience, Observability, Agents.Pty, Messaging,
  Messaging.Client, and SessionRunner.Contracts (`server/Antiphon.Server.csproj:79-88`). It does
  not reference a Git project. LibGit2Sharp 0.32.0 stays on the server (`:52`) because other server
  files still use it.

After S2:

- The same three types live in `src/Antiphon.Server.Git` with the same namespaces and the same
  public members. `git mv` keeps the bytes of the two classes. The record is cut out of
  `LandingDtos.cs` into its own file; the other records stay.
- `server/Antiphon.Server.csproj` gains a `ProjectReference` to the Git project. The Git project
  has no `ProjectReference` at all.
- Callers in the server keep their usings. The namespace did not change.
- `Antiphon.Tests` still compiles `ServerGitProcessTests` and `GitIndexLockPathTests`. Transitive
  project references are on (nothing sets `DisableTransitiveProjectReferences`). If that compile
  fails, the allowed fix is a direct `ProjectReference` from `Antiphon.Tests` to the Git project.
  The Git project still must not reference the server.

After S3:

- `ServerGitAssemblyTests` and the `GitIndexLockTests` class run in
  `tests/Antiphon.Server.Git.Tests`, which references the Git project and not the server.
- `GitIndexLockPathTests` and `ServerGitProcessTests` stay in `Antiphon.Tests`. The path tests
  construct `LandingGitFixture` and call `InspectIndexLockAsync`. `ServerGitProcessTests` scans
  the ten server start-site files in its roster (`ServerGitProcessTests.cs:78-90`) and uses
  `LandingGitFixture` plus Roslyn. Those fixtures are not cheap to move.

Unchanged: `LandingGit`, `WorktreeManager` (1,430 lines), `GitWorkspaceService` (1,349),
`OrchestratorWorkspaceLayout` (609), WorkspaceHooks (`WorkspaceHookRunner.cs`), Verification
(`CheckpointReceiptValidator.cs`), every other file under `server/Infrastructure/Git`, namespaces,
`Program.cs`, both Dockerfiles, and the land-manifest `LogicalName` on
`LandVerificationBuildGraph.cs` in Application.

## Decisions

### D-1. Move only the dependency-free prefix in the closed slices

Reason: `ServerGitProcess` and `GitIndexLock` plus `LandingIndexLockObservation` compile with BCL
types only. `LandingGit.cs:57-64` names `CardFileBoardLookup` and `GitSettings`. A Git project that
referenced the server to compile `LandingGit` would reverse the direction this card requires.

Rejected: moving the whole Git folder in one slice (not 60-120 minutes, and it cannot compile
without the server). Waiting for CARD-1729 Phase 0 (the prefix does not need those ports).

### D-2. Namespaces and public type names stay

Reason: CARD-1729 D-1. Treenode filters, nightly class costs, and usings stay valid. The assembly
name becomes `Antiphon.Server.Git` because that is the SDK default for the new csproj. The witness
asserts that name.

Rejected: a namespace rewrite to `Antiphon.Server.Git.*` (breaks every using and every filter).

### D-3. The red witness lands before the project

S1 adds only `tests/Antiphon.Tests/Infrastructure/ServerGitAssemblyTests.cs`, namespace
`Antiphon.Tests.Infrastructure`, `[Category("Unit")]`, no limiter. Two methods:

- `Declaring_assembly_is_Antiphon_Server_Git` asserts
  `typeof(ServerGitProcess).Assembly.GetName().Name` equals `Antiphon.Server.Git`.
- `Project_does_not_reference_the_server` reads
  `src/Antiphon.Server.Git/Antiphon.Server.Git.csproj` and fails when the file is missing or when
  any `ProjectReference` include contains `server/` or `Antiphon.Server.csproj`.

S3 adds `Test_project_does_not_reference_the_server`, the same check for
`tests/Antiphon.Server.Git.Tests/Antiphon.Server.Git.Tests.csproj`. Do not loosen either assertion
to make S1 green. Find the repo root the way `ServerGitProcessTests.RepositoryRoot` already does.

Rejected: a witness that only checks the csproj text after the project exists (it cannot go red).

### D-4. Delete the six allowlist rows whose target leaves the server census

In `tests/Antiphon.Tests/Layering/allowlist.txt`, delete these rows in the S2 commit:

- line 105 `AgentTaskLandService.cs -> GitIndexLock @ server/Application/Services/GitIndexLock.cs`
- line 108 `AgentTaskLandingProtocol.cs -> GitIndexLock @ server/Application/Services/GitIndexLock.cs`
- line 111 `AgentTaskReplyService.cs -> ServerGitProcess @ server/Application/Services/ServerGitProcess.cs`
- line 112 `DelegationWorkspaceResolver.cs -> ServerGitProcess @ .../ServerGitProcess.cs`
- line 113 `DelegationWorktreeService.cs -> ServerGitProcess @ .../ServerGitProcess.cs`
- line 114 `ChangeDetectionService.cs -> ServerGitProcess @ .../ServerGitProcess.cs`

Do not retarget them at `src/`. Do not extend the layering census to `src/` in this card. The
direction witness is the guard for Git → server. `layers.txt:15` stays.

Rejected: leaving the rows (the guard fails `unknown-path` even with `strict: false`). Editing the
brace (the rule is still live).

### D-5. Selection stays full until the set names every covering test

S2 does not add a rule. S3 adds rule `P-SERVER-GIT` with `full: false` and patterns
`^src/Antiphon\.Server\.Git/` and `^tests/Antiphon\.Server\.Git\.Tests/`, set id `server.git`.
The set copies the twelve keys asserted at `scripts/lib/test-selection.ps1:65`. `remainderProjects`
is the new test csproj. `members` lists every `[Test]` method that still covers the moved types
from `Antiphon.Tests`: the twelve methods on `Antiphon.Tests.Infrastructure.ServerGitProcessTests`
and `Path_resolves_main_and_linked_worktree` plus `Missing_checkout_returns_path_error` on
`Antiphon.Tests.Application.GitIndexLockPathTests`. Each member has `project`, `class`, `method`,
`script` (empty), and `source`. Eligibility `unattended`, platforms `any`, resources as `core`
(no postgres, docker, browser, broker, or vendor executable). Owner `CARD-1546`.

`P-SERVER` stays `full: true`, so a `server/` edit still selects every set, including `server.git`.

Rejected: a narrow rule in S2 (the tests are still only in `Antiphon.Tests`, and a matched rule
replaces the full fallback). `full: true` on the new rule (does not name the tests). Putting
`Antiphon.Tests` in `remainderProjects` (selects the whole assembly).

### D-6. The new test project is a primary suite

Model the `suites` entry on `observability` in `tests/test-execution-policy.json:1682-1688`:
id `server-git`, project `tests/Antiphon.Server.Git.Tests/Antiphon.Server.Git.Tests.csproj`,
name and assembly `Antiphon.Server.Git.Tests`, mode `unattended`, processGroup `native-dotnet`.
Add that id to `defaultSuites`, `profiles.nightly.requiredSuites`, and `profiles.rc.requiredSuites`.
Add a `projectDispositions` row with role `primary`, consumers empty, and a reason that names
CARD-1546. In `scripts/test-test-selection.ps1` `Test-C1313_ProjectAndCatalogUniverse`, raise the
three literals by one (`7` to `8`, `9` to `10`, nightly `7` to `8`). Do not add a `watchdogs` key
unless a reader fails closed; `Get-NightlyWatchdogMs` falls back. Recompute
`selectionCatalog.hash` with `Get-TestSelectionCatalogHash`, pin it, then recompute `policyHash`
with `Get-NightlyPolicyHash`. A production csproj does not enter the `IsTestProject` universe.

Rejected: role `child` with consumer `Antiphon.Tests` (the moved tests would leave the antiphon
suite and would not be required on their own). Leaving the tests only in `Antiphon.Tests` (no
mirror).

### D-7. Split `GitIndexLockTests.cs` instead of moving the path class

The file holds `GitIndexLockTests` (Unit, five `[Test]`, lines 11-108) and `GitIndexLockPathTests`
(Integration, `GitProcessSpawnLimit`, two `[Test]`, lines 110-147). S3 moves only the first class
to `tests/Antiphon.Server.Git.Tests/GitIndexLockTests.cs`. The path class stays in
`tests/Antiphon.Tests/Application/GitIndexLockPathTests.cs` with its category, limiter, and
fixture usings. `GitIndexLockTests*` does not match `GitIndexLockPathTests` (the next character is
`P`, not a suffix of `Tests`). The S2 filter lists both operands anyway. The unit class's
`using Antiphon.Tests.TestHelpers` is unused at this commit; delete it in the move. If a
`TestHelpers` type is actually referenced, do not move the class.

Rejected: moving the whole file (the path class does not compile without `LandingGitFixture`).
Linking the limiter into the new project in S3 (nothing moved uses it). Moving the only copy of
`GitProcessSpawnLimit`.

### D-8. Later leaves and the board-lookup port are named, not closed rows

S4 moves a file only after that slice re-reads it and every type it names is BCL or already in the
Git project. Confirmed blockers: `WorktreeRoots.cs` uses `Application.Exceptions` and `Settings`;
`WorktreeReflogSalvage.cs` uses `Application.Dtos` and `Interfaces`. `WorktreePathLimit.cs` (59
lines) and `ServerPushCredential.cs` (48) have no `using` lines and look BCL-only, but
`WorktreePathLimit` names `WorktreeNoFollowDelete` in the same namespace, so they move together or
not at all. `GitCommonDirectory.cs` uses `System.Collections.Concurrent` only. The
`ServerGitProcessTests` roster paths stay until the named file moves, and that commit updates the
roster tuple. Acceptance is: the Git project still has no server `ProjectReference`, and the moved
file compiles.

S5 extracts `ICardFileBoardLookup` so `GitService`, `LandingGit`, and `WorktreeManager` stop naming
the concrete. `CardFileBoardLookup*.cs` is 18 `[Test]`. `Program.cs` registers the existing
concrete against the port. `LandingGit` stays in the server in S5. That port is what Phase 7 was
waiting on; this card does not move `LandingGit`.

Rejected: a closed checkpoint row for S4 or S5 (the file list is not stable, and a Min would be
invented).

### D-9. No session waits for input, and there is no migration

This plan adds no session that waits for input. The CARD-1083 park rule is unchanged. No database
migration.

## Mirrored test projects

`[Test]` counts are attribute matches at `09d80f11`. A `using` count is files that import the
namespace; it is a consumer census, not a list of tests whose SUT is that project.
`SessionRunner.Contracts` in particular is a wire type.

| Production project | Mirrored `tests/<Name>.Tests` today | `[Test]` in the mirror | Still in `tests/Antiphon.Tests` |
|---|---|---|---|
| Agents.Pty (46 .cs) | `Antiphon.Agents.Pty.Tests` | 638 | 516 `[Test]` in 80 files `using Antiphon.Agents.Pty` (server consumers) |
| PtyHost (8 .cs) | `Antiphon.PtyHost.Tests` (also references Client and Protocol) | 182 | 0 files `using Antiphon.PtyHost` |
| PtyHost.Client (5 .cs) | none of its own | — | 16 `[Test]` in `DirectSessionRunnerClientDisposalTests`, `TestOwnedPtyHostIdentityTests`, and helper `TestOwnedPtyHost` |
| PtyHost.Protocol (6 .cs) | none of its own | — | 19 `[Test]` in those two tests plus `RunnerGrokAdapterReadyTestsPty`, `TestOwnedPtyHost`, `Card1619NativeExitFixture` |
| SessionRunner (155 .cs) | `Antiphon.SessionRunner.Tests` | 1,562 | 466 `[Test]` in 82 files `using Antiphon.SessionRunner` (server consumers) |
| SessionRunner.Contracts (44 .cs) | none, and do not create one | — | 4,980 `[Test]` in 544 files use the contracts as wire types |
| Hosting | no csproj. `src/Antiphon.Hosting/PwshPrivateCache.cs` is one file, linked into the checkpoint tool and the runner | — | `PwshPrivateCacheTests` 21 `[Test]` (`tests/Antiphon.Tests/Scripts/PwshPrivateCacheTests.cs`). No `using Antiphon.Hosting`. `C1793SessionPwshCacheTests` is 2 `[Test]` and stays in SessionRunner.Tests |
| Resilience (10 .cs) | missing | — | 55 `[Test]` under `tests/Antiphon.Tests/Infrastructure/Resilience` (`ResilienceTestSupport.cs` is 0). `StaleWorkingTranscriptWatchdogTests` is 10 and is a consumer. `tests/Antiphon.E2E/Fixtures/PipelineContractScenario.cs` also imports Resilience |
| Observability (3 .cs) | `Antiphon.Observability.Tests` | 10 | `PhoneHomeDirectoryTests` 8 `[Test]` and helper `PhoneHomeTestHost`. Other `PhoneHome*` files do not import Observability |
| GitInProcess (6 .cs) | missing | — | 31 `[Test]`: `InProcessGitCommandsTests` 5, `LibGit2VsGitDifferentialTests` 11, `LibGit2VsGitJunctionTests` 1, `LibGit2VsGitWorkingTreeVerdictTests` 8, `LibGit2VsGitWorktreeListTests` 6. `ServerGitProcessTests` 12 and `LandingGitBatchingTests` 4 also import GitInProcess; their SUT is server git. `LibGit2ConfigIsolation.cs` is a helper |
| IO.WriteBehind (6 .cs) | `Antiphon.IO.WriteBehind.Tests` | 8 | The same `WriteBehindFileSystemTests.cs` is compiled into `Antiphon.Tests` (`Antiphon.Tests.csproj:89-90`). The mirror exists |
| HostCleanup (43 .cs) | missing | — | Exact `using Antiphon.HostCleanup`: `HostCleanupExecutionTests` 20, `HostCleanupPolicyTests` 22, `HostCleanupOwnershipTests` 12 (54). `HostCleanupFixture.cs` is 0 |
| NightlyWatchdog (16 .cs) | missing | — | `NightlyWatchdogCoreTests` 14. `NightlyVerificationContractTests.C545` is 28 and `C545World.cs` is its helper |
| Messaging (9 .cs) plus Client (7), Client.Testing (1), FakeGateway (3), Gateway (19), Service (13), Slack (4), Telegram (3) | `Antiphon.Messaging.Tests` is the family mirror, not one project per assembly | 232 | 658 `[Test]` in 52 files `using Antiphon.Messaging` are server consumers |

### Missing-mirror order

Each of these is its own card-sized slice with its own checkpoint. None of them need this card's
production move or CARD-1729 Phase 0. Each new test csproj edits `test-execution-policy.json`,
`test-selection-policy.json`, and the C1313 literals, so do not dispatch one beside S3. Land S3
first, then one registration at a time, each recomputing the hashes.

| Order | Slice | Filter and floor | Independent of the server split |
|---|---|---|---|
| 1 | `Antiphon.Resilience.Tests` for the seven classes under `Infrastructure/Resilience` | class-OR of those classes, Min 55. Leave `StaleWorkingTranscriptWatchdogTests` and the E2E scenario | yes |
| 2 | `Antiphon.GitInProcess.Tests` for the 31 tests named above | class-OR of those five classes, Min 31. Add `InternalsVisibleTo` for the new assembly on `Antiphon.GitInProcess.csproj` (today: Server, SessionRunner, SessionRunner.Tests, Antiphon.Tests) | yes |
| 3 | `Antiphon.NightlyWatchdog.Tests` for `NightlyWatchdogCoreTests` | `/*/*/NightlyWatchdogCoreTests/*`, Min 14. Leave the C545 contract until that card checks it | yes |
| 4 | `Antiphon.HostCleanup.Tests` for the three classes above | `/*/*/(HostCleanupExecutionTests*)\|(HostCleanupPolicyTests*)\|(HostCleanupOwnershipTests*)/*`, Min 54. The checkpoint tool already references HostCleanup; keep that reference | yes |
| 5 | Create `src/Antiphon.Hosting.csproj` from the linked file, then `Antiphon.Hosting.Tests` for `PwshPrivateCacheTests` | `/*/*/PwshPrivateCacheTests/*`, Min 21. SessionRunner and the checkpoint tool switch the link to a `ProjectReference`. The two C1793 tests stay | yes |
| 6 | Optional `PtyHost.Client.Tests` and `PtyHost.Protocol.Tests` carved from the existing PtyHost.Tests (182) | that card's own class list. The Antiphon.Tests files above are server fixtures, not the Client/Protocol suite | yes, after 1 and 2 |
| 7 | Do not create `SessionRunner.Contracts.Tests` | — | — |
| 8 | Optional per-assembly Messaging mirrors, carved from `Antiphon.Messaging.Tests` | that card's own class list | yes; the family mirror already exists |
| 9 | IO.WriteBehind debt is removing the `Compile Include` once qualification runs the standalone project | the existing 8 tests | the project already exists |

## Tests

### Witnesses

| Id | When | Assertion |
|---|---|---|
| V-1 | red in S1, green from S2 | `Declaring_assembly_is_Antiphon_Server_Git` |
| V-2 | red in S1, green from S2 | `Project_does_not_reference_the_server` |
| V-3 | added in S3, green in CP-6 | `Test_project_does_not_reference_the_server` |
| V-4 | green in S2 | `C1729LayeringGuardTests` (9 `[Test]`, no arguments), including no `unknown-path` from the live allowlist |
| V-5 | green in S2, and again in the mirror at S3 | `GitIndexLockTests`: `Classify_matrix` (7 arguments), `C1436_census_drops_git_that_exits_mid_census`, `C1436_census_keeps_live_holders`, `Evaluate_path_error_is_held`, `TryReclaimAfterKill_is_gone` |
| V-6 | green in S2 | `ServerGitProcessTests` (12 methods; argument rows included) still compiles against the moved type |
| V-7 | green in S2 and again after the S3 split | `GitIndexLockPathTests` both methods |

### Mutants

Mutation owns these. Code does not run them. Each positive control is one method, then restored.

| Id | Mutant | Witness that must fail |
|---|---|---|
| PC-1 | Expect `Antiphon.Server` from `Declaring_assembly_is_Antiphon_Server_Git` | that method |
| PC-2 | Add a `ProjectReference` from the Git csproj to `server/Antiphon.Server.csproj` | `Project_does_not_reference_the_server` |
| PC-3 | Restore allowlist line 111 with the old `server/Application/Services/ServerGitProcess.cs` path | `Repository_graph_satisfies_the_committed_allowlist` (`unknown-path`) |

### Doc-contract pins

- `layers.txt:15` is unchanged through S3.
- The six allowlist rows are absent.
- `tests/rc-project-categories.json` gains `git-operations` for both new csprojs. Search readers of that file in the same commit. The rc selection script uses a synthetic fixture, not a live census; the row is still added.
- `tests/impacted-classes.json` kind `server-git-start-sites` (`:147-163`) gains glob `src/Antiphon.Server.Git/**` and classes `GitIndexLockTests` and `ServerGitAssemblyTests`. Keep class `ServerGitProcessTests`. `ServerGitProcess.cs` is not in today's globs, so the move does not empty one. When a later slice moves `LandingGit.cs` or another globbed file, that commit retargets the glob. Do not leave a glob pointing at a moved file.
- Root Dockerfile and `docker/session-runner-grok/Dockerfile` are unchanged.
- The land-manifest `LogicalName` stays on `LandVerificationBuildGraph.cs` in Application.
- Both new projects are in `Antiphon.sln`.

### Regressions

| Id | Statement | Where |
|---|---|---|
| R-1 | Layering guard stays green after the six deletions | CP-2 |
| R-2 | The new test csproj is in the nightly universe and the pinned catalog hash matches | CP-7 |
| R-3 | Seed kinds stay present and the audit has no empty glob or missing class | CP-8 |
| R-4 | The land-graph digest matches the committed manifest after S2 and again after S3 | CP-5, CP-10 |

The ten-class guard batch (policy, classification guard, slow registry, tripwire,
`NativeLinuxPolicyWitness`, mechanical contract, `TaskPlatformGuidance`, `C1729LayeringGuard`,
lane category, `ProcessSpawnLimit`) is a Review re-run. Two of those classes live in `tests/Shared`
and this plan does not invent a Min for the union. CP-2 already runs the layering class.

Existing tests affected: `tests/impacted-classes.json` kind `server-git-start-sites`, plus the
selection catalog and the execution-policy hash in S3. No public HTTP route, no operation label,
no remote script, no statement budget, no migration.

## Migrations

None.

## Slices

Each slice is one commit group, 60-120 minutes, on the Code task's own branch. Code runs the
closed table after the slice is committed. S1 runs `--after S1` only and stops. S2 runs
`--after S2`. S3 runs `--after S3`.

| Slice | Files | Minutes |
|---|---|---|
| **S1. Red witness only** | `tests/Antiphon.Tests/Infrastructure/ServerGitAssemblyTests.cs` (D-3) | 60 |
| **S2. Git project and the two types** | `src/Antiphon.Server.Git/Antiphon.Server.Git.csproj` (SDK `Microsoft.NET.Sdk`, `net9.0`, nullable, implicit usings, assembly name left as the project name, `InternalsVisibleTo` for `Antiphon.Server`, `Antiphon.Server.Git.Tests`, and `Antiphon.Tests`, no `ProjectReference`). `git mv` of `ServerGitProcess.cs` and `GitIndexLock.cs`. New `LandingIndexLockObservation.cs` with the same namespace; delete only lines 6-13 of `LandingDtos.cs`. Server csproj `ProjectReference`. Both projects in `Antiphon.sln` (the test project arrives in S3). Delete the six allowlist rows. Extend `server-git-start-sites`. Add the production rc category `git-operations`. One `scripts/audit-land-verification-graph.ps1 -Update`, then the CP-5 `-Check`. No `Program.cs` edit. No selection rule. No Dockerfile edit | 90-120 |
| **S3. Mirrored test project** | `tests/Antiphon.Server.Git.Tests` modeled on `Antiphon.IO.WriteBehind.Tests.csproj` (TUnit 1.44.0, Shouldly 4.3.0, `OutputType` Exe, `IsTestProject` true, `EnableMicrosoftTestingPlatformRunner` true) with a `ProjectReference` only to the Git project. Move `ServerGitAssemblyTests` (namespace unchanged) and add V-3. Split `GitIndexLockTests` per D-7. Register the suite, the disposition, `P-SERVER-GIT`, and set `server.git` per D-5 and D-6. Add the test rc category `git-operations`. Recompute the two hashes in that order. Add the new classes to `server-git-start-sites`. One `-Update`, then CP-10 `-Check`. No local `[Before(Assembly)]` file: none exists in the tree yet, and this project does not use `TestDbFixture` | 90-120 |
| **S4. More leaves, not a closed row** | D-8. A future card. Checkpoint when the file list is frozen: build the Git project (no server reference) and re-run `C1729LayeringGuardTests` | 90 |
| **S5. `ICardFileBoardLookup`, not a closed row** | D-8. A future card. 18 `[Test]` in `CardFileBoardLookup*.cs`. Does not move `LandingGit` | 90-120 |

S4 and S5 are not in the checkpoint table. A Code dispatch of this plan implements S1, then S2,
then S3.

## Verification design

Lane: CP-1, CP-2, CP-6, CP-7, and CP-8 are Unit. CP-3, CP-4, and CP-9 are Integration (git fixtures
and process scans). CP-5 and CP-10 are command rows. No row pins a host or an OS.

### V/R rows

The V and R ids above are the closed set. Not run, with reason: the rest of `Antiphon.Tests`
(the moved types are static and the covering classes are the rows); client, runner, and messaging
projects (no reference change there); the ten-class guard batch (Review; CP-2 covers layering).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.Tests/Antiphon.Tests.csproj -> bin-c1546-s1/ | witness-red | `/*/*/ServerGitAssemblyTests/*` | V-1, V-2 | red: both methods failed, executed at least 2. A build error or zero tests is not this witness | 2 | 8 |
| CP-2 | S2 | tests/Antiphon.Tests/Antiphon.Tests.csproj -> bin-c1546-s2/ | witness-green | `/*/*/(ServerGitAssemblyTests*)\|(C1729LayeringGuardTests*)/*` | V-1, V-2, V-4, R-1 | 0 failed | 11 | 12 |
| CP-3 | S2 | CP-2 | process-callers | `/*/*/ServerGitProcessTests/*` | V-6 | 0 failed; 12 methods, argument rows included | 12 | 12 |
| CP-4 | S2 | CP-2 | index-lock | `/*/*/(GitIndexLockTests*)\|(GitIndexLockPathTests*)/*` | V-5, V-7 | 0 failed; 7 methods. GitIndexLockTests is not a prefix of GitIndexLockPathTests | 7 | 8 |
| CP-5 | S2 | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-4 | exit 0 | n/a | 6 |
| CP-6 | S3 | tests/Antiphon.Server.Git.Tests/Antiphon.Server.Git.Tests.csproj -> bin-c1546-s3/ | mirror | `/*/*/(ServerGitAssemblyTests*)\|(GitIndexLockTests*)/(Declaring_assembly_is_Antiphon_Server_Git*)\|(Project_does_not_reference_the_server*)\|(Test_project_does_not_reference_the_server*)\|(Classify_matrix*)\|(C1436_census_drops_git_that_exits_mid_census*)\|(C1436_census_keeps_live_holders*)\|(Evaluate_path_error_is_held*)\|(TryReclaimAfterKill_is_gone*)` | V-1, V-2, V-3, V-5 | 0 failed; 8 methods. Classify_matrix contributes 7 argument rows | 8 | 8 |
| CP-7 | S3 | tests/Antiphon.Tests/Antiphon.Tests.csproj -> bin-c1546-s3b/ | selection | `/*/*/(TestSelectionCatalogTests*)\|(TestSelectionTests*)/(C1313_ProjectAndCatalogUniverse*)\|(C1313_PinnedPolicy*)` | R-2 | 0 failed | 2 | 10 |
| CP-8 | S3 | CP-7 | impacted | `/*/*/ImpactedClassesSelectorTests/seed_kinds_globs_and_owner_sentences_stay_present*` | R-3 | 0 failed | 1 | 8 |
| CP-9 | S3 | CP-7 | path-stayed | `/*/*/GitIndexLockPathTests/*` | V-7 | 0 failed; the class stayed in Antiphon.Tests | 2 | 8 |
| CP-10 | S3 | n/a | land-graph-s3 | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-4 | exit 0 | n/a | 6 |

CP-1 is reported red by design and is not rerun as green. Code of S2 is the green run of V-1 and
V-2 (CP-2). CP-3 and CP-4 reuse CP-2. CP-8 and CP-9 reuse CP-7. Outputs differ across After groups
(`bin-c1546-s1/`, `bin-c1546-s2/`, `bin-c1546-s3/`, `bin-c1546-s3b/`).

Code builds `tools/Antiphon.Checkpoints` once through `scripts/build-slot.ps1` with
`UseAppHost=false` and no `-o` / `--output` / `OutputPath`, then `dotnet exec`s
`tools/Antiphon.Checkpoints/bin/Debug/net9.0/Antiphon.Checkpoints.dll`. The tool's `run` takes its
own build slots. Do not wrap `run` in a second slot. Exit 75 means the run is still going. Linux
`Antiphon.Tests` builds use `UseAppHost=false` (fakeclaude apphost collision). One build at a time.

### Cost

Checkpoint floor: 8+12+12+8+6+8+10+8+8+6 = **86 minutes**, plus authoring (S1 60, S2 90-120, S3
90-120). S1 alone is the red row plus authoring. The derived total deadline is 2×86+10 = 182
minutes and is not written as `totalTimeoutMinutes`. Import at plan time:
`dotnet exec tools/Antiphon.Checkpoints/bin/Debug/net9.0/Antiphon.Checkpoints.dll import --plan`
this file, 10 rows, exit 0, no warnings.

## Performance budget

- **Selection.** Ten rows, four cold builds, two command rows. No client, runner, or messaging
  project. S2 and S3 also change the land-graph inputs (`*proj`, sln), which is what CP-5 and
  CP-10 check.
- **Durations and provenance.** No measured p95 for these classes is in
  `tests/test-execution-policy.json`. Provisional planning figures, with this card as the tracking
  card until the first Code run replaces them (R5): a cold `Antiphon.Tests` build about 7 minutes;
  the Unit witness and the layering guard under a minute of execution; `ServerGitProcessTests` is
  one real git fixture plus source scans; `GitIndexLockPathTests` is two `LandingGitFixture`
  cases. Every estimate is at most 15 minutes, so 3× stays inside the 45-minute advisory. The
  first Code run's `CHECKPOINT-TIMING` lines are the measurement.
- **Phases.** The tool reports build separately from execution. Slot wait is the `BUILD SLOT`
  line. The Unit rows do not start Postgres. The Integration rows use the existing
  `LandingGitFixture` temp repositories, not a new container. No AppHost and no migration.
- **Timeouts.** No new `[Timeout]`. Row timeout is the importer's `max(15, 3 × EstimatedMinutes)`.
  The total deadline stays the derived 182 minutes.
- **Over 30 minutes.** No row is estimated over 15. If a row is observed over 30 minutes, Code
  files the Debug task with the receipt and splits that row by method name.
- **Recording.** `rows/<CP>/run.trx`, `report.json`, and the `CHECKPOINT` / `CHECKPOINT-TIMING`
  lines under `.antiphon/` (ignored), quoted unedited in the task report.
- **Setup sharing.** S1's witness reads the assembly and one csproj. It adds no fixture. S3 reuses
  `GitIndexLockTests` as written.
- **External calls.** `GitIndexLockPathTests` and `C1432_land_git_reads_gc_auto_zero` spawn git
  through the existing fixture. The new project in S3 does not spawn a process.

## Activation and rollout

This is a refactor of code that is on the rc line only in the sense that the types already exist.
Do not port it onto the live rc line as a casual replay. A master land is not activation. A
reviewed rc port is a separate card after the master land. Nothing in this plan deploys.

## Risks

- Transitive compile references are the reason `Antiphon.Tests` can see the moved public types
  without a new reference. If the S2 build says the type is missing, add the direct test
  `ProjectReference` described above. Do not add a server reference on the Git project.
- `ServerGitProcessTests` roster paths are still the ten `server/` files. S2 does not move any of
  them. A later move of a roster file updates that tuple in the same commit.
- S3's narrow rule is safe only while `server.git` members include the twelve
  `ServerGitProcessTests` methods and the two path methods. Dropping a member drops that test on
  a Git-only diff. `P-SERVER` still covers a `server/` diff.
- The C1313 literals are easy to miss by one. CP-7 is the witness. Recompute hashes after the
  catalog and the policy body are final. Do not keep the pre-change hashes
  `c62da85de8c20bbbde124e9449d318d6ea335b49827802bb6d25d312552ba2c2` or
  `fdc043758617a5e8c21735126539c559faf9dad95abe5f1122a6d5780ee9df4d`.
- `-Update` invokes the land-graph DLL. `-Check` checks the committed record. Run `-Update` once
  per slice after the last csproj edit, commit the generated manifest, evaluation, and pins, and
  do not hand-edit a digest.
- Shared policy files mean the missing-mirror cards in the table above wait until S3 has landed.
- Windows: S1-S3 have no Windows-only skip. `GitIndexLockPathTests` already normalizes slashes and
  stays on the Integration lane. `WorktreePathLimit.System` is a no-op off Windows and is not in
  S1-S3. Helpers whose names start with `WindowsWorktree` stay until a later slice names a Windows
  lane. Nothing runs on the desktop.
- Operator-only steps: none. Tags, publish, and promote stay human-only and are not part of this
  plan.
