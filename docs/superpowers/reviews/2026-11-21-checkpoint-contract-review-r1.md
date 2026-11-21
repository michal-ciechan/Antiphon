# Checkpoint tool project contract, review round 1

Subject: origin/feat/card-task-df54fc26 at `2bde7a4b4f8f79bd8db9e6fc9187340f5aaac715` (Codex-authored Debug task df54fc26-e64f-44b9-bac1-711b395ed4c3). Base `bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0`. Reviewer task 614c28b4 (Grok). Range is two files, +117/-11: the contract test and `docs/investigations/2026-11-21-why-missed-rc-run-3528e6b1-group-123.md`.

Verdict: **blockers**. The widened project-reference contract matches the shipped leaf. `tests/derived-impact-index.json` was not regenerated, so `committed_index_matches_the_builder` would fail. `next: code`.

reviewedSourceClean: false. This debug change has no checkpoint manifest. The rerun below is a leased `dotnet run`, not a checkpoint-tool receipt. ordinaryScopeCompleted: Interim. The commissioned class filter, evidence-diff, and merge-tree ran. `land-graph -Check` did not: it always evaluates MSBuild and rebuilds `tools/Antiphon.LandGraphAudit` when its output is stale, which would have been a second build under this round's one-build rule. Definition files are unchanged in this range, so this commit does not itself require a pin refresh. `-Update` was not run. PCs stay pending. Mutation is waived (operator 2026-11-12). Author and reviewer are different companies.

## Widening

Adding `Antiphon.GitInProcess` is the design shipped in `869fe194e9cbe1c98df7cd420cb3e5686103741e` (`perf(checkpoints): source identity v2, read with libgit2 in the tool`, Mike Ciechan, Co-Authored-By Claude Opus 5.5) and kept by review-fix commits `6192bf1dfb` and `b93f4b8b60`. `b2d7665906` made HostCleanup depend only on GitInProcess. No plan or review markdown in this search names a Review task id that amends the CARD-0987 closed set, and the introducing message names no card. The closed set in the contract test was stale. This repair updates the test to the graph those commits already ship.

The isolation property holds on the reviewed tree:

- `tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj` ProjectReferences are exactly `../../src/Antiphon.GitInProcess/Antiphon.GitInProcess.csproj` and `../../src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj`.
- HostCleanup's only ProjectReference is `../Antiphon.GitInProcess/Antiphon.GitInProcess.csproj`.
- GitInProcess has zero ProjectReferences and one PackageReference, `LibGit2Sharp` 0.32.0. `InternalsVisibleTo` grants to Server, SessionRunner, and the tool are friend assemblies. `LandingGitResult.cs` lives in GitInProcess under namespace `Antiphon.Server.Application.Dtos`. No server project is compiled into the leaf.
- LibGit2Sharp 0.32.0 depends on `LibGit2Sharp.NativeBinaries` version `[2.0.324]`. The restored package contains `runtimes/linux-x64/native/libgit2-5853918.so` and `runtimes/win-x64/native/git2-5853918.dll`. A clean git checkout does not vendor those binaries; restore does. `ToolLibGit2.IsNativeUnavailable` treats `DllNotFoundException`, `TypeInitializationException`, `BadImageFormatException`, and `EntryPointNotFoundException` as native-unavailable, and `Available == false` reads through git. The checkpoint tool was not republished in this round.

## Allow-list

`References` parses the Include attributes and compares them with `StringComparer.Ordinal`. The three project-reference methods and the GitInProcess package method are present-and-exact:

| Mutant | Result |
|---|---|
| Third ProjectReference on the tool | Red. `csproj_references_only_isolated_libraries` expects the two-path array. |
| Extra ProjectReference on HostCleanup | Red. `host_cleanup_references_only_git_in_process` expects one path. |
| ProjectReference on GitInProcess (Server or any shared project) | Red. `git_in_process_has_no_project_references` expects empty. The empty check is the absence half of a present-and-exact pair. |
| Extra PackageReference on GitInProcess | Red. `git_in_process_references_only_libgit2sharp` expects `["LibGit2Sharp"]`. |
| Extra PackageReference on the tool | Stays green. See A-2. |

These mutants were read from the assertions. This round did not edit the csproj files.

## Windows separators

`DockerStackDocuments.Read` replaces `/` only on the file path it opens. The Include text is compared raw. The committed ProjectReference Includes use forward slashes. The historical Windows failure was that same forward-slash GitInProcess Include, which the old test rejected. Both operating systems read the same XML bytes. An Include rewritten with backslashes would fail on both. The solution and test-project assertions already use the backslash text stored in `Antiphon.sln` and `tests/Antiphon.Tests/Antiphon.Tests.csproj`.

## Blocker

### B-1. Stale derived impact index

Where: `tests/derived-impact-index.json`, against the new literals `src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj` and `src/Antiphon.GitInProcess/Antiphon.GitInProcess.csproj` in `tests/Antiphon.Tests/Infrastructure/CheckpointToolProjectContractTests.cs`.

Failure: `DerivedImpactIndexBuilder.CollectStringTargets` records an exact tracked path of length at least 8. The committed index has no `src/Antiphon.GitInProcess/Antiphon.GitInProcess.csproj` edge. The HostCleanup csproj edge lists only `HostCleanupCheckpointExtractionTests`. The tool csproj edge already lists `CheckpointToolProjectContractTests`. `DerivedImpactIndexTests.committed_index_matches_the_builder` compares the full UTF-8 bytes.

Why: this commit added the two path constants and did not regenerate the index. `scripts/impacted-classes.ps1 -Range bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0..2bde7a4b4f8f79bd8db9e6fc9187340f5aaac715` selects `CheckpointToolProjectContractTests` and `TestLaneCategoryGuardTests` (derived class from the changed test file). `-Path tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj` includes the contract class in `derivedClasses`. `-Path src/Antiphon.GitInProcess/Antiphon.GitInProcess.csproj` returns eight GitInProcess, Landing, LibGit2, and ServerGit classes and omits the contract class (`derivedClasses` empty). `-Path src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj` returns only `HostCleanupCheckpointExtractionTests`.

Fix: run `scripts/regenerate-derived-impact-index.ps1` and commit `tests/derived-impact-index.json` so both new edges include `CheckpointToolProjectContractTests`.

## Advisories

These do not independently hold the land. A-1 should ride with the B-1 repair because the next stage is already code.

- **A-1. The hand selector row is only proposed.** `docs/investigations/2026-11-21-why-missed-rc-run-3528e6b1-group-123.md` shows id `checkpoint-tool-project-contract` and says it is a follow-up. `tests/impacted-classes.json` still has `checkpoint-tool` (`tools/Antiphon.Checkpoints/**` to the three receipt classes) and does not contain that id. `-MergeSubset -Path tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj` omits `CheckpointToolProjectContractTests`. Its tests filter is `AppDbContextModelTests`, `CheckpointReceiptSafetyTests`, `CheckpointTerminalReceiptTests`, `HostCleanupCheckpointReceiptTests`, `ReceiptScanDestinationIntegrationTests`, and `TestTimingOperationLabelTests`. `-MergeSubset` ignores derived edges, so regenerating the index does not put this class on the merge subset. Add the proposed row (globs for the tool csproj pattern plus the two library csproj paths, class `CheckpointToolProjectContractTests`) in the same code pass.
- **A-2. Tool and HostCleanup package lists are open.** The tool already has `Microsoft.CodeAnalysis.CSharp` 5.9.0 and `YamlDotNet` 16.3.0. HostCleanup has `System.Management` 9.0.21. Neither PackageReference set is asserted, so another package there stays green. The LibGit2Sharp version is not asserted. None of those packages is a product project.
- **A-3. Compile Include into GitInProcess is unasserted.** GitInProcess currently has no Compile Include. A product file compiled into that leaf would stay green. The tool already Compile-includes `src/Antiphon.Hosting/PwshPrivateCache.cs` and three script files. That include predates this commit.

## Evidence at `2bde7a4b4f8f79bd8db9e6fc9187340f5aaac715`

| Check | Result |
|---|---|
| Leased filter `/*/*/(CheckpointToolProjectContractTests*)\|(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)/*` | total 63, failed 0, succeeded 63, skipped 0, 1m 15s 041ms, TEST_EXIT 0. Lease `46532db7-a207-4cff-8cba-78d606d5534f`, waited 0s, held 387s. Started at load1 12.81 after polls at 14.02 and 15.91. Native default output, `UseAppHost=false`, `TEMP=/tmp`, `--timeout 570s`. Per-class counts were not split out of the gzip HTML payload. |
| check-evidence-diff `bd34a7ff..2bde7a4` | commits=1, entries=0, violations=0, exit 0 |
| merge-tree `origin/master` `4aaeea4760983178357cf99c35bc2f0118247b9c` vs HEAD | clean, tree `a8f0b74f27ef4c9379296a0b4f481a985dc2f91b`. `origin/feat/card-task-df54fc26` is still `2bde7a4b4f8f79bd8db9e6fc9187340f5aaac715`. |
| Downgrade selector | `migrations=false` on `-MergeSubset -Range` of this range. The range contains no migration files. |
| land-graph -Check | Not run. See the verdict paragraph. |
| OS-branch witness script | Not run. This range changes no production behavior, and the debug task has no plan file for `-Plan`. |
| BUILD SLOT note | The implicit `dotnet run` build does not receive `-maxcpucount`. Known. Not rebuilt. |

Positive controls remain pending.
