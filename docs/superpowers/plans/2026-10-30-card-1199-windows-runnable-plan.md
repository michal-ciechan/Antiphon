# CARD-1199: run and gate the 35 Windows-skipped checkpoint rows

Date: 2026-10-30. Plan task `42241722`; source base
`fca55bdda2e1616bcae107ba17dab69d8de69452`. Plan review precedes Code.
The [native investigation](../../investigations/2026-10-30-card-1199-windows-runnable-feasibility.md)
is measurement evidence, not clean-source qualification.

## Objective and premise correction

Run the exact 35 existing cases on native Windows, preserving all their assertions
and Linux behavior. Twelve passed with only their gates lifted, thirteen more
passed with isolated Git configuration, and ten need native fixture arrangements.
No production implementation gap was demonstrated. This plan changes test
fixtures and their admission/coverage checks, not checkpoint production behavior.

**These rows are runtime-skipped, not policy-excluded at this base.**
`tests/test-execution-policy.json` has no exclusion for any of the three classes.
`docs/release-gates.md` explicitly keeps CARD-1199 unverified Windows behavior
required. `scripts/test-rc-platform.ps1:561,970-1027` requires those classes to
remain unexcluded and demonstrates that their NotExecuted results prevent Full.
The earlier policy task's report at
`origin/feat/card-task-93cbfd8a:.antiphon/task-93cbfd8a.md` agrees. The closed-form
policy removal roster for this plan is therefore **empty**, at every slice.
There are 35 runtime gate removals, each named below. Inventing exclusion entries
and then removing them would weaken the existing gate and misstate the change.

## Decisions

- D-1: S-A first (12 observed passes), S-B next (common configuration repair),
  S-C last (ten native fixture ports). Split S-B and S-C into bounded commissions
  below so each Grok Code task has at most 150 minutes including ordinary checks.
- D-2: turn the sixteen `shape_conflict_rule` argument cases into sixteen named
  one-case methods, delegating to the same private `AssertShapeAsync(shape, token)`
  body. Keep the class, shape strings, Judge, receipts and total 17 results.
  Names are `shape_<shape with hyphens replaced by underscores>`. This permits
  exact one-case checkpoints and later method-scoped controls. Reject one broad
  class gate removal (ten broken cases) and argument-filter syntax that the
  pinned importer/runner has not qualified. Linux executes the same 17 bodies.
- D-3: fixture Git isolation is explicit and per child, shared by both OS paths.
  Do not change process-wide environment, real Git configuration, production Git
  hardening or all unrelated callers of `CheckpointFixtureProcess`.
- D-4: port all ten POSIX arrangements. No new Windows exclusion or capability
  skip is allowed. A missing native link prerequisite is a failed setup with a
  named prerequisite, never green/Skipped. Do not substitute a copy, hard link,
  directory link or junction for the file symbolic-link case.
- D-5: preserve the existing Windows Full obligation. Add exact roster/census
  regression checks; no relaxed skip verdict or cross-platform receipt merger.
  Existing policy hash stays `a7c88a0ab7b92daa0e9a72a8b2f377ca4a8678eaa0d469d22d1d97551c676382`.
  If the eventual Code base has covering exclusions, stop and enumerate the
  actual delta for plan review; this plan does not authorize arbitrary removals.
- D-6: include verification design here, then obtain the caller's requested Opus
  plan review. Its first check is the policy-premise correction. Ordinary Code
  does not execute deliberate production mutants; their method-scoped inventory
  passes to post-land SourceLanding Mutation under the existing pipeline.
- D-7: no new project, package, build target or definition file is needed. New
  helper `.cs` files use the existing default compile glob. Perform the requested
  one final-tree graph audit `-Update` only after all planned source edits; never
  hand-edit pins or use approval regeneration to conceal a pin refusal.
- D-8: retain R-2 as a distinct unresolved native junction/custody obligation.
  Enabling these 35 is authorized already and does not require another go-ahead.

## Ground truth

| Brief assumption | Source / measurement | Consequence |
|---|---|---|
| 35 policy exclusion removals | Policy has zero covering entries; `LinuxOnlyWindowsExclusions` asserts they remain required | Zero policy removals; remove 35 runtime gates only |
| Shape rows independently selectable | One method has 16 Arguments, plus one receipt helper test | Preserve bodies/count; give each shape an exact method identity |
| Gate-only means independent of host settings | S pins local autocrlf=false; T's BeginRepo does not; passed cases are one sample | S-A evidence is provisional; S-B also requalifies the five T passes |
| Fixture and production Git use the same config | `CheckpointFixtureProcess.Run` uses git:false; production `HardenGit` disables global/system configuration | Isolate fixture children before init/add/commit and during MSBuild Exec |
| Ten native failures are product defects | S BeginRepo uses rm/mv/printf/ln inside real MSBuild Exec | Replace Windows arrangement only; keep the same real production entry points |
| A directory symlink pass proves junction safety | S PlaceUser uses Directory.CreateSymbolicLink; no junction variant | Keep R-2 open |
| Approval refresh follows every C# edit | `LandVerificationBuildGraph.Includes` selects project/props/targets/solution/SDK/package definition inputs, not arbitrary C# or execution-policy JSON | Default definition delta is empty; final audit verifies, not invents, a new graph |
| 35m44s is the successful incremental Full cost | Initial run includes 23 early failures and one extra Windows sibling; build separate | Measure successful cost; do not reuse failing timings as success p95 |

## Source map

Aliases below are exact repository files, not replacement implementations:

- S: `tests/Antiphon.Tests/Checkpoints/CheckpointShapeConflictTests.cs`;
  `BeginRepo`, `PlaceUser`, `WriteProject`, `Judge`, `ReadReceipt` and both runners.
- T: `tests/Antiphon.Tests/Checkpoints/CheckpointTrackedBaselineTests.cs`;
  `BeginRepo`, `AssertDetectedAsync`, `DescribeKept`, `RunToolAsync`, `RunScriptAsync`.
- O: `tests/Antiphon.Tests/Checkpoints/CheckpointOutputConfinementTests.cs`;
  graph builders, `SeedBuild`, `Finish`, `AssertOwnedAndUnchanged`, both runners.
- P1: `tools/Antiphon.Checkpoints/BuildConfinement.cs` `Prepare`, `Settle`,
  `Classify`, `Stamp`, `Capture` and hardened Git; script equivalents in
  `scripts/run-checkpoint.ps1`: `Get-CheckpointTrackedSnapshot`,
  `Get-CheckpointContentStamp`, `Complete-CheckpointBaseline`.
- P2: same C# file `Rewrite`, `Walk`/real MSBuild evaluation and
  `AddConfinedUseAppHost`; script `Invoke-CheckpointGraphEval`,
  `Invoke-ConfinedCheckpointBuild`, `Get-CheckpointOutputLayout`.
- P3: `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs` exclusive creation,
  `SafeDelete.cs` link/tracked-path refusal, script output-collision preflight;
  `ReportValidator` source eligibility and script receipt validation.
- P4: `src/Antiphon.HostCleanup/Checkpoints/CheckpointNativeIo.cs`,
  `CheckpointNativeIo.Anchored.cs`, `CheckpointFileReader.cs` and script
  `scripts/lib/checkpoint-file-reader.ps1`, selecting native Windows handles and
  reparse inspection. The unrelated top-level Windows adapter stub is not used.

## Slices and handoff bounds

Every slice is a committed, pushed source group with exact ordinary checkpoints
on Windows and Linux. No concurrent edits to these shared files. Partial slices
leave the remaining Windows cases required/incomplete; they do not qualify Full.
Preserve the investigation's original failures and counts.

### S-A: twelve gate-only results (110-minute Code ceiling)

Files: S, T; `scripts/test-rc-platform.ps1`;
`tests/Antiphon.Tests/Scripts/ReleaseGatePlatformTests.cs` only if a named wrapper
is needed; `docs/release-gates.md`. Extract the 16 thin shape methods without
changing their bodies. Lift the gates only for the six S shapes and receipt
method listed A below, and the five T methods. Keep temporary native gates on
the remaining named methods, removing each in its owning slice. Existing
WindowsUnitSkipRoster contains none of these classes: do not fabricate edits to
it. The namespace census must still count the original 35 plus the existing O
Windows sibling; historical listing fixtures remain historical.

Change the synthetic S gap identity in `LinuxOnlyWindowsExclusions` from
`shape_conflict_rule` to `shape_directory_on_file`. Keep all three classes in
its no-exclusion decision set. Add `C1199_WindowsRequiredRoster` to that script
(and a thin TUnit wrapper if required by local convention): enumerate all 35
post-extraction identities, ensure zero exclusions on both platforms, ensure a
synthetic NotExecuted in any of the 35 prevents completeness, and ensure 35
Passed contributes complete coverage for that isolated roster. This is a census
contract, not native execution evidence. Do not mark unrelated R-2 rows passed.

Tests: CP-1..CP-12 and CP-36. Include the exact skip-census updates in
`scripts/lib/checkpoint-usage.ps1` described below. Implementation/review preparation 40 minutes;
ordinary two-lane execution/build allowance 60 minutes; reporting 10 minutes.
The native existing-output-symlink setup remains Directory.CreateSymbolicLink.
Collision fixtures' unreachable POSIX sentinel commands are replaced in S-C;
S-A must assert exact refusal and no build-start evidence, not infer it merely
from an Exec command failing later.

### S-B1: isolated Git helper and eight baseline results (150-minute ceiling)

Files: new `tests/Antiphon.Tests/Checkpoints/CheckpointFixtureGitEnvironment.cs`,
new `CheckpointFixtureGitEnvironmentTests.cs`, selected call sites in S/T/O,
and an explicit overload in `CheckpointFixtureProcess.cs` if needed. Do not
change its default behavior: its existing FIFO/config boundary probes rely on it.

The helper owns an empty scratch home outside the fixture's Git worktree, plus
empty regular global/system config files. Set GIT_CONFIG_GLOBAL and
GIT_CONFIG_SYSTEM to those files and GIT_CONFIG_NOSYSTEM=1. Set child HOME,
USERPROFILE and XDG_CONFIG_HOME to owned empty directories; preserve SDK/NuGet
cache locations explicitly for dotnet children so isolation does not force a
package download per test. Remove inherited GIT_CONFIG, GIT_CONFIG_PARAMETERS,
GIT_CONFIG_COUNT and matching KEY_n/VALUE_n entries, as well as repository/index
redirectors. Pin local core.autocrlf=false before writing/staging fixtures. Keep
explicit fixture commit identity and signing=false; never rewrite input bytes
just to make status pass. The helper is used on BOTH platforms.

Apply the environment to direct Git, seed dotnet, pwsh script/snapshot/validator
and MSBuild's Git grandchildren. The tool runner is currently in-process:
use the existing Runtime.Driver seam with a test-only IConfinedDriver decorator
around the REAL ProcessDriver, merging the environment into DriverRequest.
Forward CaptureLedger, Kill, cancellation, Ledger, OnOutput, Exited and every
result unchanged; never use a fake success driver or DotnetShim. This preserves
Prepare/Settle and real graph/build execution. Production's separate hardened
Git readers remain unchanged. Test helper use is opt-in in S/T/O only; consumers
of their shared WaitAsync must retain caller cancellation and bounded drains.

Commit the regression and a compiling pass-through helper BEFORE the helper
repair; a missing helper/build error is not the red witness. Its exact method is
`CheckpointFixtureGitEnvironmentTests.fixture_commit_matches_hardened_status_under_hostile_config`.
Use owned child configuration containing autocrlf=true and explicit CRLF file
bytes, stage through the existing unisolated fixture route, invalidate the Git
stat cache deterministically, and assert the fixture commit appears clean to
production's hardened reader. Base fails with a named dirty path, as the native
report demonstrated; it must not depend on this host's actual .gitconfig.
The corrected path isolates before init/add/commit and passes that assertion.
Keep a separate diagnostic legacy control proving the hostile config was read
and produces the differential. Verify global and system config sources, local
config still effective, parent environment unchanged, two parallel child roots
independent, and no eager process launch outside the strict ProcessSpawnLimit.

Tests: CP-37 red first (one deliberate failing regression, no fixture/build
error); then CP-13..CP-20, CP-38/CP-48/CP-50 green and CP-39 on Linux.
CP-47 requalifies the five S-A T methods and S staged-untracked once because
their shared setup changed.
The O sites can be wired now while their five gates remain until S-B2.
Authoring 40m + red/green builds and two-lane checks 95m + reporting 15m.
If measured runs exceed that bound, hand off committed remaining CP IDs;
do not silently replace real-build checks with mocks.

### S-B2: five confinement results (100-minute ceiling)

Files: O gates; the shared helper only if the preceding call-site audit missed a
route. Retain the already-Windows O sibling, its UseAppHost assertions and its
explicit separate count. Tests: CP-21..CP-25; CP-40 Windows only. Linux must run
exactly the five portable methods, not the Windows-only sibling (which skips).
Authoring 20m + two-lane ordinary evidence 65m + reporting 15m.

### S-C1: seven ordinary native mutations (125-minute ceiling)

Files: S and a test-only Windows fixture writer in the same file or new
`CheckpointShapeFixture.cs`. Keep the existing Linux command strings byte-for-byte.
For Windows, WriteProject writes a tracked PowerShell fixture before Commit and
invokes it via MSBuild Exec (`pwsh -NoProfile -NonInteractive -File` with XML and
path quoting), at the SAME BeforeTargets=Build point. File bytes use explicit
ASCII/.NET writes, no implicit newline or BOM. Throw on any failed Git/native
operation. No POSIX executable discovery, WSL or host PATH modification.

The seven C1 rows below are untracked-descendant, ignored-descendant,
modified-tracked, deleted-tracked, regular-user-move, deleted-untracked and
changed-untracked. Port the collision sentinel actions in the same change so an
incorrectly launched build can actually mutate its sentinel on Windows and be
caught. Tests: CP-26..CP-32 plus CP-49 (four collision guards affected by
that arrangement change). Authoring 45m + evidence 65m + reporting 15m.

### S-C2: staged shape replacement and file symlink (125-minute ceiling)

Files: S/native fixture writer. Port directory-on-file, file-on-directory and
symlink-on-file, exactly as specified below; remove their last runtime gates.
Use File.CreateSymbolicLink or the PowerShell/.NET equivalent for the file link;
require actual LinkTarget and preserved target bytes. Explicitly check link
creation capability on the native lane and report missing privilege/Developer
Mode as a prerequisite failure, without changing host policy. The investigation
proves directory symlinks on one host, not universal file-link privilege.
Tests: CP-33..CP-35. Authoring 45m + evidence 65m + reporting 15m.

### S-F: final acceptance and one graph approval (150-minute ceiling)

No fixture redesign here. Finish all source changes, inspect the final tracked
build-definition delta and commit/push BEFORE audit. There should be no project,
props, targets, solution, SDK or package-definition changes under D-7. Policy
JSON and its policyHash remain unchanged; adding a .cs fixture does not change
LandVerificationBuildGraph's definition hash. Do not edit
LandVerificationBuildGraph.pins.txt or invent a definition-file delta.

Use the existing staged `land-graph-audit/Antiphon.LandGraphAudit.dll` from a
leased isolated test build. Run exactly once on that final definition tree:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1199-final-graph -- pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Update -ToolPath <owned-output>/land-graph-audit/Antiphon.LandGraphAudit.dll
```

It writes `server/Application/Services/LandVerificationBuildGraph.manifest.txt`
and `LandVerificationBuildGraph.evaluation.txt`; commit/push their genuine delta
only if changed. The latter binds digest, selectable SDK and pins blob. A pin
refusal requires separate authority; never hand-correct it or extend deadlines.
Rebuild once for final acceptance if the embedded manifest changed. No second
Update after verification. A later build-definition change invalidates this
finalization and needs a fresh commission, not another hidden refresh.

Tests: CP-41..CP-46 once per indicated lane, one shared build per lane on the
final source. Confirm exactly 35 target cases pass with zero skipped/failed;
report CP-44's pre-existing Windows sibling separately. Audit is a named,
necessary authoring action outside checkpoints because it writes source.
Final source receipts bind the post-audit committed tree. No tag, publish,
restart, deployment, rc rebase or Full rc run is performed by these Code tasks.

## Verification design

V-1: every enabled Windows case reaches the same real production seam and
assertions as Linux. V-2: fixture Git and hardened production Git agree despite
host configuration, without changing the parent or unrelated tests. V-3: the
exact 35 remain Windows-required; any NotExecuted prevents coverage. V-4: native
ports cause the intended mutation during Build, with exact post-build bytes,
index, path and negative receipt checks. R-1: same Linux bodies remain green.
R-2 is the separate open junction/custody decision below, not a checkpoint credit.
R-3: final graph approval and manifest binding match the committed definition
inputs. R-4: timing, count and source provenance are complete.

The tables below define both ordinary execution and anti-vacuity controls.
Each witness is a concrete assertion that fails if the indicated seam is
bypassed; it is not a claim that a production mutant was run in this Plan.
Later Mutation uses one exact detecting method per cycle and the real bound
pack, preserves red/restore-green evidence, and never credits zero tests,
missing executables, compile errors or setup failure as intended red.
S-B's tests-first configuration regression is ordinary Code red, distinct from
those pending controls. Never weaken Judge or catch errors into a pass.

### Runtime skip census (distinct from release policy)

There IS a second roster to change: `scripts/lib/checkpoint-usage.ps1`
`Get-NamespaceCensus.windowsSkips` currently has three broad class patterns for
S/T/O. S-A replaces those patterns with the exact ten still-gated S methods,
eight T methods and five O methods in the tables below (23 remaining). S-B1
removes its eight T entries (15 remain), S-B2 removes the five O entries (10),
S-C1 removes seven S entries (3), S-C2 removes the last three (0). Never keep a
class wildcard that would excuse a newly portable row's skip. The pre-existing
Windows O sibling was not one of these 35 and must never gain a Windows skip.
Add its exact method to linuxSkips if namespace validation selects it; that is
an existing native-Windows obligation, not a new Linux exemption for these 35.
Keep every unrelated census entry unchanged. `namespace_census_matches_compiled_checkpoint_cases`
checks compiled counts and live patterns; extend the C1199 roster regression to
pin this exact remaining-skip set per slice as well as the unchanged RC policy.

### Per-row contract and native arrangement

`CP-n` below is the exact checkpoint command key defined under Checkpoints;
its one-case selector and command expansion are executable, not pseudofilters.
A/B/C correspond to the slices above. Time is whole execution minutes, excluding
the shared outer test-assembly build; native seconds are historical first-run
observations. B timings were early failures; use the successful control aggregate
for batch budgeting. All controls below must observe both tool and script unless
explicitly labeled script-only or local helper. All 35 stay required in policy.

| Command key / slice | Exact test (class alias S/T/O) | Production seam and required red witness | Windows fixture decision | Minutes / original seconds |
|---|---|---|---|---|
| CP-1 / S-A | `S.shape_staged_untracked` | P1 Settle/index; P3 invalid receipt. Require USER-KEEP plus index entry, tampered path and invalid bound receipt; omitting build git-add must fail. | No fixture port | 1 / 56.57 |
| CP-2 / S-A | `S.shape_clean_build` | P1 Prepare/Settle; P2 real build; P3 evidence. Require executed=1 and side/keep bytes; bypassing build/test cannot pass. | No fixture port | 2 / 70.94 |
| CP-3 / S-A | `S.missing_receipt_is_not_negative_success` | S Receipt(null), test-local negative oracle only. Null receipt must not return ok; accepting absence fails. This row alone proves no native production seam. | No fixture port | 1 / 0 |
| CP-4 / S-A | `S.shape_existing_stamp` | P3 exclusive stamp/output preflight. Require USER-OUTPUT and exact pre-existing-build-source refusal before build; bypass preflight must fail sentinel/receipt checks. | No fixture port | 1 / 25.08 |
| CP-5 / S-A | `S.shape_compiler_collision` | P3 output refusal before compiler. Require original App.dll, exact pre-existing-output refusal and no successful build; accepting output fails. | No fixture port | 1 / 26.18 |
| CP-6 / S-A | `S.shape_existing_output_directory` | P3 exclusive owned directory creation. Require pre-existing-output refusal and unchanged tracked sentinel; accepting empty foreign directory fails. | No fixture port | 1 / 31.65 |
| CP-7 / S-A | `S.shape_existing_output_symlink` | P3 reparse/link-component refusal; P4 native inspection. Require real directory LinkTarget plus original outside/App.dll and refusal; replacing link by directory/copy fails. | No fixture port | 1 / 25.01 |
| CP-8 / S-A | `T.intermediate_output_path_tampering_is_reported` | P1 Settle; P2 IntermediateOutputPath. Require generated AssemblyVersion bytes retained, named tamper and invalid receipt; bypass generation/detection fails. | No fixture port | 3 / 176.28 |
| CP-9 / S-A | `T.generated_target_file_tampering_is_reported` | P1 Settle; P2 GenerateVersion target. Require generated version 1 bytes retained and named tamper; an unexecuted target or hidden tamper fails. | No fixture port | 2 / 116.62 |
| CP-10 / S-A | `T.dirty_tracked_tree_is_refused_and_unmodified` | P1 Prepare dirty preflight. Require dirty-tracked exit 2, unchanged dirty/marker bytes, no build success; bypass refusal fails. | No fixture port | 1 / 42.07 |
| CP-11 / S-A | `T.tracked_snapshot_contains_committed_files` | Script Get-CheckpointTrackedSnapshot/Git membership. Require all five names including outside.txt and notes/readme.txt; restricting to project directory fails. | No fixture port | 1 / 10.72 |
| CP-12 / S-A | `T.index_added_file_is_kept_and_indexed` | P1 Settle index delta; P3 script receipt. Require INDEX-KEEP, fresh-user.txt in index and invalid receipt; skipping update-index or restoration fails. | No fixture port | 3 / 130.81 |
| CP-13 / S-B1 | `T.local_output_property_tampering_is_reported` | P1 Settle; P2 TreatAsLocalProperty OutputPath. Require actual MZ replacement DLL retained, tampered legacy path and invalid receipt; early dirty refusal fails. | No fixture port | 2 / 12.62 |
| CP-14 / S-B1 | `T.imported_props_reference_tampering_is_reported` | P1 Settle; P2 imported ProjectReference. Require Lib/legacy/Lib.dll MZ replacement and named tamper; not building imported dependency fails. | No fixture port | 2 / 19.6 |
| CP-15 / S-B1 | `T.transitive_reference_tampering_is_reported` | P1 Settle; P2 App-to-Mid-to-Lib traversal. Require transitive Lib replacement and invalid receipt; omitting transitive evaluation fails. | No fixture port | 2 / 21.43 |
| CP-16 / S-B1 | `T.clean_untracked_output_build_exits_zero` | P1 clean baseline; P2 real build. Require preserved source, owned output, no TRX and script build exit 0; skipping build or dirty refusal fails. | No fixture port | 2 / 14.47 |
| CP-17 / S-B1 | `T.fresh_checkout_run_executes_a_test` | P1 clean fresh baseline; P2 build/run; P3 source binding. Require no obj before tool, executed=1, exit 0 and buildSource=verified from each entry; mock success without execution fails. | No fixture port | 3 / 66.89 |
| CP-18 / S-B1 | `T.staged_untracked_file_is_kept_and_indexed` | P1 index/worktree delta; P3 negative receipt. Require USER-KEEP, index entry, named tamper and source_ineligible; omitting git-add or deleting newly staged file fails. | No fixture port | 2 / 23.91 |
| CP-19 / S-B1 | `T.staged_ignored_file_is_kept_and_indexed` | P1 ignored-path/index delta; P3 negative receipt. Require IGNORED-KEEP and forced-add entry; removing force or undoing the index fails. | No fixture port | 2 / 49.07 |
| CP-20 / S-B1 | `T.passing_script_requires_writable_source_receipt` | Script passing build/test to required source.json write. Require collision directory and source.json exit 2 after executed=1; add executed=1/collision proof so unrelated early refusal cannot pass. Logging receipt failure with exit 0 must fail. | No fixture port | 2 / 16.43 |
| CP-21 / S-B2 | `O.imported_props_build_preserves_the_tracked_dependency` | P2 imported References.props; P1/P3 owned outputs. Require original tracked Lib DLL/assets AND new owned Lib.dll/artifacts assets; skipping graph/build or writing normal Lib output fails. | No fixture port | 2 / 164.99 |
| CP-22 / S-B2 | `O.directory_build_props_build_preserves_the_tracked_dependency` | P2 Directory.Build.props import; P1/P3 outputs. Require original DLL/assets plus both new owned files; bypass implicit import or confinement fails. | No fixture port | 2 / 111.92 |
| CP-23 / S-B2 | `O.conditional_import_build_preserves_the_tracked_dependency` | P2 real condition/property graph evaluation; P1/P3 outputs. Require condition-selected Lib in owned output plus unchanged tracked bytes; static-only graph or no build fails. | No fixture port | 2 / 147.07 |
| CP-24 / S-B2 | `O.transitive_import_build_preserves_the_tracked_dependency` | P2 transitive imported graph; P1/P3 outputs. Require deepest Lib outputs in App owned root and tracked bytes unchanged; dropping transitive edge fails. | No fixture port | 2 / 166.16 |
| CP-25 / S-B2 | `O.unevaluable_import_is_refused_before_the_build` | P2 graph evaluation refusal before build. Require graph-unevaluated and KEEP-SENTINEL, no build success; generic dirty refusal or treating missing import as empty graph fails. | No fixture port | 1 / 18.35 |
| CP-26 / S-C1 | `S.shape_untracked_descendant` | P1 CaptureUntracked/Stamp/Settle. Require moved USER-UNTRACKED descendant, tracked.txt tamper, manual command and invalid receipt; unchanged arrangement fails. | Port: remove tracked regular file; Directory.Move user-dir to tracked.txt; do not stage descendant. | 1 / 56.57 |
| CP-27 / S-C1 | `S.shape_ignored_descendant` | P1 ignored manifest/Stamp/Settle. Require USER-IGNORED and exact invalid receipt; overlooking ignored child or leaving original tracked file fails. | Port: same directory move, preserve user.dat ignore entry and unstaged state. | 1 / 54.28 |
| CP-28 / S-C1 | `S.shape_modified_tracked` | P1 content Stamp/Settle. Require MUTATED bytes retained and named tamper; a no-op or automatic restore fails. | Port: WriteAllBytes ASCII MUTATED to tracked.txt inside Exec, no newline. | 1 / 51.54 |
| CP-29 / S-C1 | `S.shape_deleted_tracked` | P1 absence Stamp/Settle. Require tracked.txt absent, named tamper and invalid receipt; no-op or recreation fails. | Port: File.Delete tracked.txt inside Exec; assert prior existence in fixture. | 2 / 60.38 |
| CP-30 / S-C1 | `S.shape_regular_user_move` | P1 tracked/untracked transition/Settle. Require USER-MOVED at tracked.txt and conflict; add source-absence assertion so a copy cannot masquerade as move. | Port: File.Move user-move.txt to tracked.txt with overwrite=true, preserving bytes. | 1 / 55.83 |
| CP-31 / S-C1 | `S.shape_deleted_untracked` | P1 pre-existing untracked manifest/Settle. Require user-move.txt absent and named tamper; add MUTATED tracked.txt witness; recreating user path fails. | Port: File.Delete user-move.txt then exact ASCII MUTATED write to tracked.txt. | 1 / 52.83 |
| CP-32 / S-C1 | `S.shape_changed_untracked` | P1 pre-existing untracked hash/Settle. Require CHANGED-USER and named tamper; add MUTATED tracked.txt witness; preserving original bytes or no-op fails. | Port: ASCII CHANGED-USER to user-move.txt and MUTATED to tracked.txt. | 1 / 50.22 |
| CP-33 / S-C2 | `S.shape_directory_on_file` | P1 shape stamp/index/unstaged descendants. Require USER-SECRET staged and USER-UNSTAGED unstaged, both exact bytes, named tamper and invalid receipt; missing target execution fails. | Port: File.Delete tracked.txt; Directory.Move user-dir; git add -A tracked.txt then git restore --staged tracked.txt/unstaged.txt. | 2 / 20.61 |
| CP-34 / S-C2 | `S.shape_file_on_directory` | P1 directory-to-file shape/index/Settle. Require USER-FILE at dir and dir/a.txt tamper; add staged replacement/old-entry absence assertions; no-op/copy arrangement fails. | Port: remove only owned fixture dir recursively, File.Move payload.dat to dir, git add -A dir. | 2 / 65.48 |
| CP-35 / S-C2 | `S.shape_symlink_on_file` | P1 StampLink/Settle; P4 native file link. Require real file LinkTarget ending link-target.txt, LINK-BYTES target unchanged, tamper and invalid receipt; copy/hardlink/no-op fails. | Port: delete tracked regular file, File.CreateSymbolicLink tracked.txt -> link-target.txt, git add tracked.txt. Verify link/index without assuming Git symlink mode on unqualified hosts. | 2 / 65.83 |

### Checkpoint commands and lanes

Read GET `/api/runner-defaults` and GET `/api/session-runners` immediately before
commissioning. Both returned 200 in this Plan session: the default resolves to
Linux; an eligible native Windows lane and an eligible Linux lane were present.
No fleet URL or host is pinned here. Omit `-Runner`; use `-Platform Windows` for
native evidence and `-Platform Linux` for the Linux parity portion. OS-independent
plan review/import omits `-Platform` (or Any to clear a prior pin). Availability
and capacity are observations, not reservations.

In the table, `both-` means the identical row is required separately on Windows
and Linux; `windows-` and `linux-` are native-only. Keep separate source-bound
receipts; Linux cannot earn Windows release credit. Do not execute the entire
multi-slice/multi-lane table indiscriminately. Suffixes in every OR operand are
parenthesized and starred; exact one-method rows need no wildcard. Check fresh
TRX against exact class/method identities and argument expansion, not only Min.
The new shape names preserve the original sixteen shape strings one-for-one.

After a leased bootstrap of the checkpoint tool to an owned alternate output,
import this plan and apply only the deadline overlay described in Performance
budget to `.antiphon/c1199-checkpoints.yaml`. The exact execution command for
**every CP-n** in both tables is this expansion, with n replaced by its integer:

```powershell
dotnet tools/Antiphon.Checkpoints/bin-c1199-plan-tool/Antiphon.Checkpoints.dll run .antiphon/c1199-checkpoints.yaml --rows CP-n --serial
```

For each committed slice, replace the one ID with the comma-separated row set
below and run once. The runner shares its build within that invocation. Do not
run each row as a separate build. Use `wait --run <printed-id> --max-wait 50s`
until terminal when the foreground observer returns 75; never leave a child
running at task settlement. This prebuilt DLL invocation is orchestration only and cannot implicitly
build. The executor acquires its own build/driver slots; do not hold an outer
slot around `run` while it waits for an inner slot. Bootstrap/import itself goes
through `scripts/build-slot.ps1`, and is not the test-assembly build. Running the
exported manifest is necessary here because the plan-table schema cannot carry
the explicit per-row deadlines; its selection must be byte-equivalent to import.

| Slice | Windows rows | Linux rows | Expected target results (support results separate) |
|---|---|---|---|
| S-A | CP-1..CP-12,CP-36 | same | 12 per lane |
| S-B1 red | CP-37 | CP-37 | 1 expected configuration assertion failure per lane, separately committed before fix |
| S-B1 green | CP-13..CP-20,CP-38,CP-47,CP-48,CP-50 | same plus CP-39 | 8 new plus 6 explicitly requalified per lane |
| S-B2 | CP-21..CP-25,CP-40 | CP-21..CP-25 | 5 plus 1 existing Windows sibling |
| S-C1 | CP-26..CP-32,CP-49 | same | 7 new plus 4 collision requalifications |
| S-C2 | CP-33..CP-35 | same | 3 per lane |
| S-F | CP-41..CP-46 | CP-41..CP-43,CP-45,CP-46 | 35 per lane, plus 1 existing Windows sibling |

Ranges above are descriptive: pass explicit comma-separated IDs to `--rows`.
Build outputs are slice-owned, fresh, inventoried and removed after joined
execution; never change an output to reuse a DLL across source commits. Red
CP-37 owns its own output and must be cleaned by its producer before final
source qualification. Slot refusal is not test execution. All rows require
zero skipped/failed except the explicitly expected tests-first CP-37 red.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S-A | `tests/Antiphon.Tests -> bin-c1199a/` | both-s-a-1 | `/*/*/CheckpointShapeConflictTests/shape_staged_untracked` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 11 | true |
| CP-2 | S-A | `CP-1` | both-s-a-2 | `/*/*/CheckpointShapeConflictTests/shape_clean_build` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 2 | true |
| CP-3 | S-A | `CP-1` | both-s-a-3 | `/*/*/CheckpointShapeConflictTests/missing_receipt_is_not_negative_success` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-4 | S-A | `CP-1` | both-s-a-4 | `/*/*/CheckpointShapeConflictTests/shape_existing_stamp` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-5 | S-A | `CP-1` | both-s-a-5 | `/*/*/CheckpointShapeConflictTests/shape_compiler_collision` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-6 | S-A | `CP-1` | both-s-a-6 | `/*/*/CheckpointShapeConflictTests/shape_existing_output_directory` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-7 | S-A | `CP-1` | both-s-a-7 | `/*/*/CheckpointShapeConflictTests/shape_existing_output_symlink` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-8 | S-A | `CP-1` | both-s-a-8 | `/*/*/CheckpointTrackedBaselineTests/intermediate_output_path_tampering_is_reported` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 3 | true |
| CP-9 | S-A | `CP-1` | both-s-a-9 | `/*/*/CheckpointTrackedBaselineTests/generated_target_file_tampering_is_reported` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 2 | true |
| CP-10 | S-A | `CP-1` | both-s-a-10 | `/*/*/CheckpointTrackedBaselineTests/dirty_tracked_tree_is_refused_and_unmodified` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-11 | S-A | `CP-1` | both-s-a-11 | `/*/*/CheckpointTrackedBaselineTests/tracked_snapshot_contains_committed_files` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-12 | S-A | `CP-1` | both-s-a-12 | `/*/*/CheckpointTrackedBaselineTests/index_added_file_is_kept_and_indexed` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 3 | true |
| CP-13 | S-B1 | `tests/Antiphon.Tests -> bin-c1199b1/` | both-s-b1-13 | `/*/*/CheckpointTrackedBaselineTests/local_output_property_tampering_is_reported` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 12 | true |
| CP-14 | S-B1 | `CP-13` | both-s-b1-14 | `/*/*/CheckpointTrackedBaselineTests/imported_props_reference_tampering_is_reported` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 2 | true |
| CP-15 | S-B1 | `CP-13` | both-s-b1-15 | `/*/*/CheckpointTrackedBaselineTests/transitive_reference_tampering_is_reported` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 2 | true |
| CP-16 | S-B1 | `CP-13` | both-s-b1-16 | `/*/*/CheckpointTrackedBaselineTests/clean_untracked_output_build_exits_zero` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 2 | true |
| CP-17 | S-B1 | `CP-13` | both-s-b1-17 | `/*/*/CheckpointTrackedBaselineTests/fresh_checkout_run_executes_a_test` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 3 | true |
| CP-18 | S-B1 | `CP-13` | both-s-b1-18 | `/*/*/CheckpointTrackedBaselineTests/staged_untracked_file_is_kept_and_indexed` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 2 | true |
| CP-19 | S-B1 | `CP-13` | both-s-b1-19 | `/*/*/CheckpointTrackedBaselineTests/staged_ignored_file_is_kept_and_indexed` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 2 | true |
| CP-20 | S-B1 | `CP-13` | both-s-b1-20 | `/*/*/CheckpointTrackedBaselineTests/passing_script_requires_writable_source_receipt` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 2 | true |
| CP-21 | S-B2 | `tests/Antiphon.Tests -> bin-c1199b2/` | both-s-b2-21 | `/*/*/CheckpointOutputConfinementTests/imported_props_build_preserves_the_tracked_dependency` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 12 | true |
| CP-22 | S-B2 | `CP-21` | both-s-b2-22 | `/*/*/CheckpointOutputConfinementTests/directory_build_props_build_preserves_the_tracked_dependency` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 2 | true |
| CP-23 | S-B2 | `CP-21` | both-s-b2-23 | `/*/*/CheckpointOutputConfinementTests/conditional_import_build_preserves_the_tracked_dependency` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 2 | true |
| CP-24 | S-B2 | `CP-21` | both-s-b2-24 | `/*/*/CheckpointOutputConfinementTests/transitive_import_build_preserves_the_tracked_dependency` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 2 | true |
| CP-25 | S-B2 | `CP-21` | both-s-b2-25 | `/*/*/CheckpointOutputConfinementTests/unevaluable_import_is_refused_before_the_build` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-26 | S-C1 | `tests/Antiphon.Tests -> bin-c1199c1/` | both-s-c1-26 | `/*/*/CheckpointShapeConflictTests/shape_untracked_descendant` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 11 | true |
| CP-27 | S-C1 | `CP-26` | both-s-c1-27 | `/*/*/CheckpointShapeConflictTests/shape_ignored_descendant` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-28 | S-C1 | `CP-26` | both-s-c1-28 | `/*/*/CheckpointShapeConflictTests/shape_modified_tracked` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-29 | S-C1 | `CP-26` | both-s-c1-29 | `/*/*/CheckpointShapeConflictTests/shape_deleted_tracked` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 2 | true |
| CP-30 | S-C1 | `CP-26` | both-s-c1-30 | `/*/*/CheckpointShapeConflictTests/shape_regular_user_move` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-31 | S-C1 | `CP-26` | both-s-c1-31 | `/*/*/CheckpointShapeConflictTests/shape_deleted_untracked` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-32 | S-C1 | `CP-26` | both-s-c1-32 | `/*/*/CheckpointShapeConflictTests/shape_changed_untracked` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-33 | S-C2 | `tests/Antiphon.Tests -> bin-c1199c2/` | both-s-c2-33 | `/*/*/CheckpointShapeConflictTests/shape_directory_on_file` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 12 | true |
| CP-34 | S-C2 | `CP-33` | both-s-c2-34 | `/*/*/CheckpointShapeConflictTests/shape_file_on_directory` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 2 | true |
| CP-35 | S-C2 | `CP-33` | both-s-c2-35 | `/*/*/CheckpointShapeConflictTests/shape_symlink_on_file` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 2 | true |
| CP-36 | S-A | `CP-1` | both-required-roster | `/*/*/(ReleaseGatePlatformTests*)\|(CheckpointNamespaceCensusUsageTests*)/(C1199_WindowsRequiredRoster*)\|(LinuxOnlyWindowsExclusions*)\|(namespace_census_matches_compiled_checkpoint_cases*)` | V-3 R-1 | exactly 3 passed; 0 failed/skipped | 3 | 2 | true |
| CP-37 | S-B1-red | `tests/Antiphon.Tests -> bin-c1199bred/` | both-git-red | `/*/*/CheckpointFixtureGitEnvironmentTests/fixture_commit_matches_hardened_status_under_hostile_config` | V-2 red | exactly 1 executed; expected named dirty-baseline assertion failure | 1 | 11 | true |
| CP-38 | S-B1 | `CP-13` | both-git-child-boundary | `/*/*/CheckpointFixtureGitEnvironmentTests/(child_environment_isolated_without_changing_parent*)\|(real_driver_keeps_confinement_and_cancellation*)` | V-2 R-1 | exactly 2 passed; 0 failed/skipped | 2 | 2 | true |
| CP-39 | S-B1 | `CP-13` | linux-routing-cancellation | `/*/*/CheckpointFixtureRoutingTests/C1233_TokenCallers` | V-2 R-1 | exactly 4 passed; 0 failed/skipped | 4 | 1 | true |
| CP-40 | S-B2 | `CP-21` | windows-existing-apphost | `/*/*/CheckpointOutputConfinementTests/windows_confined_build_leaves_use_app_host_unset` | R-1 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-41 | S-F | `tests/Antiphon.Tests -> bin-c1199final/` | both-final-shapes | `/*/*/CheckpointShapeConflictTests/*` | V-1 V-4 R-1 R-4 | exactly 17 passed; 0 failed/skipped | 17 | 24 | true |
| CP-42 | S-F | `CP-41` | both-final-baseline | `/*/*/CheckpointTrackedBaselineTests/*` | V-1 V-2 R-1 R-4 | exactly 13 passed; 0 failed/skipped | 13 | 24 | true |
| CP-43 | S-F | `CP-41` | both-final-confinement | `/*/*/CheckpointOutputConfinementTests/(imported_props_build_preserves_the_tracked_dependency*)\|(directory_build_props_build_preserves_the_tracked_dependency*)\|(conditional_import_build_preserves_the_tracked_dependency*)\|(transitive_import_build_preserves_the_tracked_dependency*)\|(unevaluable_import_is_refused_before_the_build*)` | V-1 V-2 R-1 R-4 | exactly 5 passed; 0 failed/skipped | 5 | 10 | true |
| CP-44 | S-F | `CP-41` | windows-final-apphost | `/*/*/CheckpointOutputConfinementTests/windows_confined_build_leaves_use_app_host_unset` | R-1 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-45 | S-F | `CP-41` | both-final-guards | `/*/*/(CheckpointFixtureGitEnvironmentTests*)\|(ReleaseGatePlatformTests*)\|(CheckpointNamespaceCensusUsageTests*)\|(LandNonCodeSkipTests*)/(fixture_commit_matches_hardened_status_under_hostile_config*)\|(child_environment_isolated_without_changing_parent*)\|(real_driver_keeps_confinement_and_cancellation*)\|(C1199_WindowsRequiredRoster*)\|(LinuxOnlyWindowsExclusions*)\|(namespace_census_matches_compiled_checkpoint_cases*)\|(C1385_RegistryMatchesNarrowGraph*)` | V-2 V-3 R-3 | exactly 7 passed; 0 failed/skipped | 7 | 2 | true |
| CP-46 | S-F | n/a | both-final-audit | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tests/Antiphon.Tests/bin-c1199final/land-graph-audit/Antiphon.LandGraphAudit.dll` | R-3 | audit exit 0; exact committed graph and unchanged pins | n/a | 3 | true |
| CP-47 | S-B1 | `CP-13` | both-shared-git-regressions | `/*/*/(CheckpointShapeConflictTests*)\|(CheckpointTrackedBaselineTests*)/(shape_staged_untracked*)\|(intermediate_output_path_tampering_is_reported*)\|(generated_target_file_tampering_is_reported*)\|(dirty_tracked_tree_is_refused_and_unmodified*)\|(tracked_snapshot_contains_committed_files*)\|(index_added_file_is_kept_and_indexed*)` | V-1 V-2 R-1 | exactly 6 passed; 0 failed/skipped | 6 | 11 | true |
| CP-48 | S-B1 | `CP-13` | both-git-census | `/*/*/CheckpointNamespaceCensusUsageTests/namespace_census_matches_compiled_checkpoint_cases` | V-3 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-49 | S-C1 | `CP-26` | both-native-collision-sentinels | `/*/*/CheckpointShapeConflictTests/(shape_existing_stamp*)\|(shape_compiler_collision*)\|(shape_existing_output_directory*)\|(shape_existing_output_symlink*)` | V-4 R-1 | exactly 4 passed; 0 failed/skipped | 4 | 3 | true |
| CP-50 | S-B1 | `CP-13` | both-git-regression-green | `/*/*/CheckpointFixtureGitEnvironmentTests/fixture_commit_matches_hardened_status_under_hostile_config` | V-2 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |

## Performance budget

Apply [testing/build performance standards](../../testing-and-build.md#performance-standards)
and [R1-R9](../../test-performance-standards.md). This Plan runs only an importer
build/validation, not the product tests. The Code selections above are the whole
ordinary scope; no full assembly, Unit sweep, E2E or provider canary is authorized
as an extra confidence run. All three classes are Category=Unit despite their
real subprocess/build cost; keep that category in this change and record an
explicit `checkpoint/git-build` group. Future project/category extraction is
separate CARD-1545/CARD-1546 work, not a prerequisite or an excuse to lose coverage.

### Measurement provenance and Full cost

Measurements come from investigation source
`ade54222543381e2e1b31ad79d8ae33a4035f70b`, native Windows 10 x64/NTFS,
SDK 10.0.300, PowerShell 7.6.9, Git 2.50.1.windows.1, TUnit 1.44.0. Three
uncommitted gate-only changes were present in that binary. Load is unknown;
slot wait was zero except the last control's 45 seconds. There is **one sample
per row/path, no measured p95**, and the phases were not instrumented. Do not
merge failed first-run durations with passing controls into an invented p95.

| Selection | Observed native execution | Planned whole-minute execution | Build separate |
|---|---|---:|---|
| S-A twelve passes | Sum of per-case samples 711.93s (11m52s); not a contiguous run | 18 across exact-method rows | 10m per source group/lane, based on 9m56s native build |
| S-B1 eight corrected T cases | Passing control 797.40s (13m17s) | 17; plus 11 for six shared-setup regression cases | Same group build; red needs its own build |
| S-B2 five corrected O cases | Passing control 448.44s (7m28s); outer wall 504.14s includes 45s admission | 9, plus existing sibling 1 on Windows | 10m |
| S-C1 seven ports | First failed arrangements sum 381.65s; no successful port sample | 8, plus 3 collision checks | 10m |
| S-C2 three ports | Failed arrangements sum 151.92s; no successful port sample | 6 | 10m |
| S-F all 35, class batches | No complete clean-source passing run exists | Shape 14 + T 24 + O 10 = 48; Windows sibling 1 separate | 10m, included in CP-41's 24 |
| Initial investigation aggregate | 35m43.591s includes the 56.273s Windows sibling and early failures | Historical cost retained, not a success estimate | Excludes 9m56.13s build |

Windows Full already discovers/requires these 35 but currently spends almost no
body time on them. Forecast its incremental successful body cost at **about
40-50 minutes**, provisional: 11m52s of unchanged passes + 20m46s of corrected
configuration controls + roughly 9-14 minutes for native ports and launch
remainder. The final checkpoint budget uses 48 minutes. The existing O Windows
sibling is already in Full, so do not count it again. The new helper's three
support tests add a separately measured small cost (budget 3 minutes).

Use `new Full wall = latest comparable Full wall - old 35-skip wall + measured
35-success wall + new support-test wall + measured build/packing delta`.
There is no current comparable complete Full baseline in this evidence; do not
assert a new fleet-wide total or reuse CARD-0856's historical 270 minutes as one.
The existing Full assembly build is already paid; fixture builds inside the
35 bodies are incremental. Record successful distributions before changing
`test-execution-policy.json` cost rows; updating those is measured-cost work,
not an exclusion change or authority to rewrite historical results.

### Aggregate-run Debug and parallel work

**Caller must commission/reuse a Debug task before paying another aggregate
selection over 30 minutes**, including S-F and any expected two-lane batch above
that threshold. Attach the investigation's exact run IDs, SHA, TRX durations,
35m44s aggregate, missing phases and this forecast; reference CARD-1199 with
CARD-1404/CARD-1514/CARD-1314. This delegate cannot dispatch that task. Debug must
measure successful per-case fixture creation, Git calls, graph evaluation,
restore/build, TUnit startup, receipt validation, teardown and unassigned
remainder, with build/admission separated. Preserve the original 23 failures.
Split or repair only after attributing cost; a retry is not the Debug outcome.
Source authoring and bounded slice verification need not wait for a speed-up
implementation, but no over-30-minute run is credited without its Debug record.

The separately commissioned manifest-publication plan must publish these 35
planned identities and their actual terminal outcomes/timings from the run's
own evidence. This work emits fresh TRX, exact selectors, logs and phase JSONL
now; it does not depend on that publisher landing. Until it is available, retain
the local planned/executed mapping beside each checkpoint receipt. Do not claim
publisher completion or Full credit from this plan. Relevant existing work:
[Windows timing repair](2026-10-23-card-1514-windows-timing-repair-plan.md),
[phase instrumentation](2026-10-23-card-1514-test-timing-instrumentation-plan.md),
[slow-test speedups](2026-10-23-card-1514-slow-test-speedups-plan.md), and
[cost-balanced groups](2026-10-21-card-1404-cost-balanced-launch-groups-plan.md).
Their improvements can reduce the same measured cost; they cannot remove a
required identity, change assertions, combine receipts across SHAs, or turn a
runtime skip into Passed. The caller's separate publisher plan has not been
identified by artifact in this brief; link its exact reviewed artifact at
commissioning rather than inventing one.

### Phases, sharing and observability

Use the existing TestTiming API in changed fixture helpers/call sites. Once per
assembly/run: recorder setup, cached SDK/NuGet assets and any immutable fixture
text. Each case owns its Git repository/home and exact cleanup inventory. No
AppHost, HTTP server, Docker, database container, migration or database seed is
needed by this selection; report those phases as not applicable. Do not start
such infrastructure to test configuration isolation.

Fresh repositories/build outputs are the subject of these tests: graph variants,
pre-existing user output, clean fresh-checkout behavior, and tool versus script
must each start with the prescribed independent baseline. O seeds each graph
once and reuses its captured bytes for the two entries, as it already does.
Keep `fresh_checkout_run_executes_a_test`'s absent-obj witness. Do not clone
absolute project.assets.json paths across repositories or share mutable Git
indexes. Any later template optimization must retain those fresh-start checks.

For a case with execution estimate M, provisional planning allocation is setup
0.2M, graph/restore/build/body 0.6M, assertions/receipt processing 0.1M and teardown
0.1M; these are budgets, NOT measurements. Record each real boundary and the
remainder instead of reporting those ratios as observed. Use FixtureSetup,
ProcessSpawn/ProcessWait, Filesystem, OurCode and FixtureTeardown spans. Distinguish
seed builds, tool builds, script builds and nested TUnit runs; record external
Git/MSBuild/pwsh calls individually, never secrets or full environment payloads.
Retain existing 30s Git, 120s seed-build and cancellation-bound wrapper limits;
record their elapsed values. New helper diagnostic calls have a 30s whole-operation
limit (provisional inherited Git bound); new helper tests at most 120s. No new
retry, longer external-call timeout or process-wide limiter is introduced.

Record under ignored `.antiphon/checkpoints/<run-id>/` plus
`.antiphon/c1199-timing/<lane>/<source>/<attempt>/`. Set
ANTIPHON_TEST_PHASE_ROOT to an absolute owned path and the existing RUN_ID,
GROUP_ID, ATTEMPT and SUITE metadata for the test child; keep discovery/bootstrap
outside its case duration. Preserve per-test p50/p95/max when enough comparable
samples exist, per-row counts, host/platform/load, phase last-progress and
wall/build/admission totals. Missing spans remain unknown/null and unassigned
wall is remainder, not zero. Capture the actual project-reference closure from
Antiphon.Tests (including Checkpoints and HostCleanup and staged helpers) with
the run; downstream selection is these real consumers, not a claimed whole-app
coverage result.

### Explicit deadlines, sampling and exceptions

Do not run with the importer's automatic 15-minute/3x defaults: import currently
sets CP-41/CP-42 to **72 minutes** and warns. Produce an ignored YAML from this
plan, then change ONLY the documented timeout fields (and timing environment
metadata); verify all 50 IDs, After, build IDs, filters, counts and estimates
are identical to import. This is the executable checkpoint manifest for Code,
not a policy/schema change. Keep the unedited import alongside it.

| Rows | Explicit timeoutMinutes | Basis / limitation |
|---|---:|---|
| CP-1..CP-35 | Twice the per-row execution Minutes in the per-row table (2, 4 or 6) | Rounded 2x provisional successful/sample-based planning bound; excludes shared build |
| CP-36 | 4 | 2m support batch, provisional |
| CP-37 | 2 | Tests-first one-case red body; its 10m build is separate |
| CP-38 | 4 | Two new helper tests, provisional |
| CP-39,CP-40 | 2 | Existing small control selections, provisional |
| CP-41 | 28 | 2x 14m shape batch, provisional |
| CP-42 | 48 | 2x 24m complete T batch, provisional; Debug required |
| CP-43 | 20 | 2x 10m confinement batch, provisional |
| CP-44 | 2 | Historical Windows sibling 56.273s, n=1 |
| CP-45 | 4 | 2m guard batch, provisional |
| CP-46 | 6 | 3m audit estimate; retain evaluator's own unchanged bounds |
| CP-47 | 22 | 2x 11m six-case regression batch, provisional |
| CP-48,CP-50 | 2 | One small case each, provisional |
| CP-49 | 6 | 2x 3m collision batch, provisional |

Set `timeouts.rowMinutes: 20` for the shared-build floor (approximately 2x the
9m56s build sample) and `build.slotWaitMinutes: 10` for each acquisition; never
retry an admission expiry unleased. The current scheduler couples each build's
deadline to its largest dependent row: S-B1 can become 22m and S-F becomes 48m.
That is a disclosed **CARD-1199/CARD-1404 provisional build-deadline exception**,
not a measured 48m build need or permission to let an owned idle build drift.
Debug measures it; removing that scheduler coupling is separate work. Set the
execution allowance per invocation to twice its selected EstimatedMinutes sum,
and hard-wall allowance to that plus 10 minutes. Report exit 4 as not run,
execution expiry as failure, and joined cancellation if the 150-minute task
ceiling is reached. Admission time remains visible and consumes task wall time.

Existing T/O method watchdogs are finite (60/180/300/420s); S was 600s. Give the
new shape wrappers at most the smaller of their old 600s bound and their row
budget above. Do not widen any existing test timeout from a single sample.
Retained longer T/O limits are an explicit temporary CARD-1199/CARD-1404 exception
until successful case history exists, not compliant measured p95. Collect the
next **three naturally required native passes** (Code, final acceptance, later
Review/Full as applicable), without repeated-green runs solely for timing, and
review the sufficient-history threshold in the existing timeout-audit tool.
Then set each test/row deadline to rounded approximately 2x comparable measured
p95, with any remaining exception named. A 40s operation should have no more
than approximately two minutes. Failed arrangement samples cannot justify a
larger successful deadline; Linux and Windows distributions stay separate.

S-A budget 110m; S-B1 150m (40 authoring, 95 red/green and both-lane checks,
15 reporting); S-B2 100m; S-C1/C2 125m each; S-F 150m. These are bounded task
ceilings, not a claim of measured implementation time. Reuse exact same-source
receipts when permitted, never relabel them. Caller may commission the second
OS verification as its own task against the immutable pushed slice when a lane
cannot be reached from the authoring host. No running delegate is sent a remote
snapshot or silently moved; no source mutation while its verification runs.

## R-2 decision retained

The investigation established a directory symbolic-link arrangement on one
Windows host. It did not run a junction variant, establish privilege-free file
symlinks, or qualify hostile same-account/process-custody races. The native
implementations exist, but these are different acceptance obligations. Also
retain the old plan's Windows file-sharing qualification disclosure; these
35 do not close it.

The operator's remaining decision is to commission explicit native junction
and custody qualification (recommended), or accept a narrowly written release
limitation through the normal card/release-policy decision process. In the
latter case the operator must specify which requirement is deferred and its
owner; this plan grants no exclusion or Full waiver. Candidate junction seams
are `OutputCleanupTests.parent_symlink_does_not_delete_a_tracked_stamp` and
`CheckpointOwnedOutputTests.changed_inventory_is_refused`'s link argument.
Keep their exact target/index survival and native reparse evidence separate
from S's existing directory-symbolic-link case. No R-2 decision is required to
implement the authorized 35-row portability work; default is pending/required.

No session-waits-for-input behavior is added or changed by this plan.
