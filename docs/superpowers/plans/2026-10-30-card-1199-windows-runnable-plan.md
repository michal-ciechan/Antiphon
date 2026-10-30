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
  `Classify`, `Stamp`, `CaptureTracked` and hardened Git; script equivalents in
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

Tests: CP-1..CP-12 and CP-36. Implementation/review preparation 40 minutes;
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

Commit the regression BEFORE the helper repair. Its exact method is
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
error); then CP-13..CP-20, CP-37..CP-39 green. Re-run CP-8..CP-12 once here
because their shared T setup changed, and CP-1 (staged Git grandchild) once.
The O sites can be wired now while their five gates remain until S-B2.
Authoring 55m + two test builds/lanes and red witness 80m + reporting 15m.
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
caught. Tests: CP-26..CP-32 plus CP-4..CP-7 (four collision guards affected by
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

Tests: CP-41..CP-45 once per indicated lane, one shared build per lane on the
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
