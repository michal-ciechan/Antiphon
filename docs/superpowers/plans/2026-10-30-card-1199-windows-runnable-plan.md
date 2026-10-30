# CARD-1199: run and gate the 35 Windows-skipped checkpoint rows

Date: 2026-10-30. Plan task `42241722`; source base
`fca55bdda2e1616bcae107ba17dab69d8de69452`. Plan review precedes Code.
The [native investigation](../../investigations/2026-10-30-card-1199-windows-runnable-feasibility.md)
is measurement evidence, not clean-source qualification.

## Round-3 amendment status

Plan task `f8a94733`, assigned base
`95d190a389fccd3b1e61f1d7f88bfb3af1e88af1`. Read the complete final review
`.antiphon/task-4d98fbcb.md`. This is the last, narrow plan amendment; no product
source changes or product tests. Round-2 findings and costs remain historical.

Amendment completed and imported; the detailed evidence is in Plan validation:

- D-1 closed: CP-46 is a bare quote-free shell command, with the audit's exact
  `approved` line and evaluator digest mandatory for credit. It is the only
  command row; the supported Expect contract includes a post-run assertion.
- D-2 closed: CARD-1613 publication on master and S3 publication on each target
  are hard prerequisites at their stated stages. One post-1613 roster/count
  applies to both targets; no old-name variant or admission stub is allowed.
- Advisories taken: no cross-host CP-37 authoring wait, current rc/top-up refs,
  explicit 0.5s/node fallback and family factor, actual S3 costBasis handoff.
  Import retains 53 rows, eight shared builds and 223 whole-minute estimates.

R-2 stays pending/required. The first remote observation in this round is
`rc/2026-10-27` = `ade54222543381e2e1b31ad79d8ae33a4035f70b`;
top-up 38 publication is not yet confirmed. Publish only the plan-file delta.
Verification design is included. Next is Code after confirmed top-up 38
publication, with the later hard prerequisites and pending R-2 retained.

## Round-2 amendment status (historical)

Plan task `dd46f01b`, base `421c3de68a81b32ded14f40af85a4e9ff50a1938`.
The complete review `.antiphon/task-3b1ff9b6.md` is the amendment input.
The revised plan retains the confirmed 35-row premise and verification design.
Round 2 addressed its three findings and A-1..A-9, then received final Opus
review `4d98fbcb`, which found the two defects amended in round 3 above.
Historical amendment decisions:

- Review D-1: independently build the graph-audit tool for CP-46; establish
  scheduler dependencies and cleanup lifetime from source.
- Review D-2: target rc first, then a separate reviewed master port; order every
  slice after rc top-up 38 / CARD-1613 and use its renamed policy wrapper.
- Review D-3: correct/add measured S/T/O entries in
  `scripts/lib/nightly-class-costs.json`, preserving policy-hash semantics and
  reconciling S3 admission pricing.
- A-1..A-9: six-minute CP-21/24 deadlines, compiled roster equality, explicit
  Git environment/NuGet configuration, CP-51 legacy control, CP-52 fixture
  requalification, separate lane budgets, and plan-only publication. The final
  importer record below supersedes the historical 50-row validation.

R-2 remains an open operator decision, default pending/required. No product
source edits or product test execution belong to this Plan task.

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
  Preserve the post-top-up policy hash. The round-1 historical hash was
  `a7c88a0ab7b92daa0e9a72a8b2f377ca4a8678eaa0d469d22d1d97551c676382`;
  do not restore it over independently landed policy changes.
  If the eventual Code base has covering exclusions, stop and enumerate the
  actual delta for plan review; this plan does not authorize arbitrary removals.
- D-6: include verification design here; the caller's requested final Opus
  review is recorded in `4d98fbcb`, with its final fixes in this round. Ordinary Code
  does not execute deliberate production mutants; their method-scoped inventory
  passes to post-land SourceLanding Mutation under the existing pipeline.
- D-7: no new project, package, build target or definition file is needed. New
  helper `.cs` files use the existing default compile glob. Perform the requested
  one final-tree graph audit `-Update` only after all planned source edits; never
  hand-edit pins or use approval regeneration to conceal a pin refusal.
- D-8: retain R-2 as a distinct unresolved native junction/custody obligation.
  Enabling these 35 is authorized already and does not require another go-ahead.
- D-9: all six slices are test/qualification fixes for code already on rc, not
  features. Land rc first after top-up 38, then separately review and port each
  slice to master only after the CARD-1613 master port is confirmed published.
  Reject racing the policy rewrite or merging this inherited
  investigation/review history wholesale; use the plan-only publication below.
- D-10: one Linux Code commission and one native Windows Verify commission per
  slice, on the identical pushed source SHA; neither host supplies the other
  lane's evidence. Keep the slice frozen until both have settled.
- D-11: refresh class prices from successful native evidence after S-F in a
  bounded cost-only follow-up, before Windows Full credit. Do not seed measured
  rows from failed or mixed historical runs. This follow-up is a required test
  infrastructure fix on rc and master, not an optional performance feature.
- D-12: CP-46 uses a bare, quote-free `dotnet build ... && pwsh -File ...`
  command on both shells. Require the evaluator's exact `approved` output line
  and its freshly built DLL digest before credit. The importer retains this
  requirement in ExpectText; the mandatory post-run assertion below enforces it.
  Reject an invented YAML log-assertion field or a new runner feature in this
  narrow amendment: command rows currently enforce exit/source status only.
- D-13: use one post-CARD-1613 roster on both targets. Its confirmed master port
  is a hard prerequisite of every CARD-1199 implementation port to master;
  S3's confirmed publication on each target is a hard prerequisite of that
  target's S-F-cost. Neither is waivable by an authoring-order default. Reject
  master-specific old-wrapper rows/counts: they duplicate the qualification
  contract and would conflict with the later CARD-1613 rewrite.
- D-14: retain a pushed immutable S-B1 red commit, then let Linux author the fix
  after its own CP-37 assertion failure. Windows runs the red commit separately
  before verifying the green commit; it need not finish before fix authoring.
  Both lanes' red and green evidence remain mandatory before S-B1 acceptance.

## Landing target and prerequisite order

Implementation target is the active `rc/YYYY-MM-DD` line (observed ref
`rc/2026-10-27`), followed by a **separate reviewed master port** of each accepted
test fix under standing operating rule 9. The caller resolves the active rc ref
at commissioning; this plan neither rebases rc nor publishes a target itself.
Round-3 `git ls-remote` observed that rc ref at
`ade54222543381e2e1b31ad79d8ae33a4035f70b`, master at
`98fc3ceb6440a17b68cafe3ecc21c76d190b18ca`, and top-up 38 at the exact tip below.
These observations do not confirm top-up 38 publication to rc.
S-A -> S-B1 -> S-B2 -> S-C1 -> S-C2 -> S-F is a strict sequence, and **every
slice starts after rc top-up 38 has confirmed publication**. A pushed merge-task
branch is not proof of landing. Rebase the design onto the target's actual
contents by reading them; do not rebase this assigned task branch.

Source audit: CARD-1613 branch `origin/feat/card-task-06fc8af7` at
`0f6058ac9` renames the wrapper and harness case to
`LinuxOnlyWindowsExclusionPolicy`, retaining six internal harness assertions
(previously eight). The pushed top-up branch `origin/feat/card-task-fcfa49ab`
at `130ea525aa1f25230123ee3584ea58dbf0604b95` contains that name and rewrite;
this is the exact top-up tree verified in round 3, not a claimed rc landing.
CP-36 and CP-45 select the new name; it is still ONE TUnit result, so their
totals remain 3 and 7. A suffix audit must include the actual landing base.
CARD-1613's `[NativeLinuxOnly]` reflection witness applies to policy-excluded
rows (25 whole classes plus 20 method-scoped entries), **not these 35**: S/T/O
have no such policy row and remain in the harness's required decision set.
Do not add the marker to these portable tests or restore the removed assertion
count/source-regex witness while editing the rewritten harness.

**Hard master prerequisite:** master at the observed SHA still has
`LinuxOnlyWindowsExclusions` and eight harness assertions, without CARD-1613's
reflection witness. CP-36/45 would select only 2/6 results there, not 3/7.
Before any implementation port to master, confirm a separately reviewed
CARD-1613 master port is published and inspect its renamed wrapper, six
assertions and reflection witness at the actual master SHA. Do not lower counts,
restore the old wrapper, or apply S-A's gap-identity edit to the old harness.
This prerequisite also applies before S-F-cost on master.

**Hard S-F-cost prerequisite on each target:** S3 admission must be published on
that same target before authoring/running/landing its S-F-cost. The source is
`feat/card-task-6738f5e6` at `192f15240f757e04fb5fdb032e447f926e563757`, carried
by rc top-up 39; require confirmed top-up 39 publication on rc and a separately
confirmed S3 master port. `scripts/lib/nightly-watchdog-admission.ps1` is absent
from observed master, rc and top-up 38. CP-53's real admission assertion cannot
run there. Check the file, `Resolve-NightlyLaunchWatchdog` and its planner call
site at each confirmed target SHA; a pushed S3/top-up branch is insufficient.
Never stub or skip that assertion. These are hard dependencies, independent of
the optional early-authoring scheduling choice in the next paragraph.

Serialize against the other rc top-ups: speed-ups S3 (`6738f5e6` at
`192f15240f757e04fb5fdb032e447f926e563757`), slice B, manifest publication,
and CARD-1609/1610/1615. Their exact confirmed target SHAs are commissioning
inputs, not inferred from task completion. Simpler fail-closed default: finish
those already-open top-ups before starting S-A; if the caller deliberately
allows independent authoring, no overlapping landing is allowed and S-F still
waits for the complete prerequisite chain. Read the merged harness, cost reader,
launch planner and publication contract at that base; preserve their changes.
The cost/pricing handoff below specifies S3's interaction. No slice grants Full
credit before its required evidence or the separate R-2 obligation is resolved.

Publish **only the plan**: a separate Docs landing commission starts at the
target and applies the reviewed final contents/delta for this single plan path,
including round 1 (`fca55bdda..c31d947b5`), round 2 (`421c3de68..13c691b16`)
and round 3 (`95d190a38..HEAD`). Inspect the delta against that target, rather
than assuming this branch's inherited history is a publication unit. It excludes the
inherited investigation and `.antiphon/task-*.md` reports. Inspect the resulting
single-path diff and commit it for review/land. Do not request a whole-branch
merge of this task: the inherited `.antiphon/task-af2b78c8.md` add/add conflict
is unrelated to the plan. The investigation remains cited historical evidence;
its separate publication is not smuggled into the plan land. Port this same
reviewed plan document to the other target as needed, without replaying reports.

## Ground truth

| Brief assumption | Source / measurement | Consequence |
|---|---|---|
| 35 policy exclusion removals | Policy has zero covering entries; `LinuxOnlyWindowsExclusionPolicy` asserts they remain required | Zero policy removals; remove 35 runtime gates only |
| Shape rows independently selectable | One method has 16 Arguments, plus one receipt helper test | Preserve bodies/count; give each shape an exact method identity |
| Gate-only means independent of host settings | S pins local autocrlf=false; T's BeginRepo does not; passed cases are one sample | S-A evidence is provisional; S-B also requalifies the five T passes |
| Fixture and production Git use the same config | `CheckpointFixtureProcess.Run` uses git:false; production `HardenGit` disables global/system configuration | Isolate fixture children before init/add/commit and during MSBuild Exec |
| Ten native failures are product defects | S BeginRepo uses rm/mv/printf/ln inside real MSBuild Exec | Replace Windows arrangement only; keep the same real production entry points |
| A directory symlink pass proves junction safety | S PlaceUser uses Directory.CreateSymbolicLink; no junction variant | Keep R-2 open |
| Approval refresh follows every C# edit | `LandVerificationBuildGraph.Includes` selects project/props/targets/solution/SDK/package definition inputs, not arbitrary C# or execution-policy JSON | Default definition delta is empty; final audit verifies, not invents, a new graph |
| 35m44s is the successful incremental Full cost | Initial run includes 23 early failures and one extra Windows sibling; build separate | Measure successful cost; do not reuse failing timings as success p95 |
| Table order makes CP-46 wait for CP-41 | Scheduler prioritizes a ready serial command; importer ignores command Build cells | CP-46 builds its own evaluator before its check, in a distinct owned output |
| Exit 0 proves a quoted command ran its audit | Windows command rows use cmd /d /s /c through ArgumentList; nested quotes can turn the PowerShell command into an echoed string | Bare quote-free chain plus exact `approved` line and fresh evaluator digest required |
| Command-row Expect text is an automatic log assertion | Importer stores ExpectText; RowRunner returns from its command branch before test-roster Expect checks | Mandatory post-run assertion implements CP-46's Expect contract; runner green alone is insufficient |
| The policy wrapper keeps its round-1 name/count on both targets | Top-up 38 uses LinuxOnlyWindowsExclusionPolicy/six assertions; observed master still uses LinuxOnlyWindowsExclusions/eight | Hard CARD-1613 master-port prerequisite; then CP-36=3 and CP-45=7 on both targets |
| S3 admission is already on both targets | Admission script absent from observed rc, master and top-up 38; present on S3 branch 192f15240 | Hard S3 publication prerequisite for S-F-cost on EACH target: rc top-up 39 and separate master port |
| Execution policy contains class prices | Separate nightly-class-costs.json has O=25s/6 with five failures; S/T absent | Require native S/T/O cost refresh and separate cost digest before Full |
| One Code delegate can supply both native lanes | Assigned worktrees execute on one OS | Separate Linux Code and native Windows Verify for every slice |

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

Every slice is a committed, pushed source group, authored by Linux Code, then
verified independently by native Windows Verify at the identical SHA. Both
tasks have explicit lane budgets below. No concurrent edits to shared files. Partial slices
leave the remaining Windows cases required/incomplete; they do not qualify Full.
Preserve the investigation's original failures and counts.

### S-A: twelve gate-only results

Files: S, T; `scripts/test-rc-platform.ps1`;
`tests/Antiphon.Tests/Scripts/ReleaseGatePlatformTests.cs` (mandatory named wrapper);
`docs/release-gates.md`. Extract the 16 thin shape methods without
changing their bodies. Lift the gates only for the six S shapes and receipt
method listed A below, and the five T methods. Keep temporary native gates on
the remaining named methods, removing each in its owning slice. Existing
WindowsUnitSkipRoster contains none of these classes: do not fabricate edits to
it. The namespace census must still count the original 35 plus the existing O
Windows sibling; historical listing fixtures remain historical.

Change the synthetic S gap identity in `LinuxOnlyWindowsExclusionPolicy` from
`shape_conflict_rule` to `shape_directory_on_file`. Keep all three classes in
its no-exclusion decision set. Add `C1199_WindowsRequiredRoster` to that script
and the mandatory same-named TUnit wrapper: enumerate all 35
post-extraction identities, ensure zero exclusions on both platforms, ensure a
synthetic NotExecuted in any of the 35 prevents completeness, and ensure 35
Passed contributes complete coverage for that isolated roster. This is a census
contract, not native execution evidence. Do not mark unrelated R-2 rows passed.

The wrapper passes its scratch input through the existing
`ScriptHarnessOptions.AdditionalArguments` seam (a named `-CompiledRosterPath`
parameter on the harness), keeping the process limiter/owner intact. It compares
the exact class/method set with
`CheckpointRoster.CompiledCases(typeof(CheckpointShapeConflictTests).Assembly,
"Antiphon.Tests.Checkpoints")`, selecting S/T/O and subtracting only the named
Windows O sibling. Require set equality, no duplicates, and count 35; a renamed,
deleted or extra compiled case must fail. Pass that same compiled roster to the
script through an owned scratch JSON input (never a source scan). The script
emits exactly six `C1323` assertions named `c1199-roster-35`,
`c1199-windows-required`, `c1199-linux-required`, `c1199-each-gap-incomplete`,
`c1199-all-passed-complete`, and `c1199-residual-skips-exact`. The wrapper requires
all six literal `C1323 <name>` strings with harness count 6, plus its compiled
set assertion. This remains one TUnit test. The gap check iterates each of the
35 identities through the real verdict function; one representative is insufficient.

Tests: CP-1..CP-12 and CP-36. Include the exact skip-census updates in
`scripts/lib/checkpoint-usage.ps1` described below. Linux authoring allowance 40 minutes; separate lane commissions and budgets are below.
The native existing-output-symlink setup remains Directory.CreateSymbolicLink.
In S-A, add a portable MSBuild WriteLinesToFile marker under the fixture's
ignored .antiphon directory immediately before the mutation Exec. Require that
marker absent for all four collision cases, alongside the exact named refusal.
This small anti-vacuity arrangement addition makes bypassing preflight fail even
if the later POSIX Exec is unavailable on Windows. Keep Linux's mutation commands
unchanged. S-C replaces the unreachable collision commands too; require marker
presence for the ten mutation cases once ported. Do not infer no-build solely
from the absence of Build succeeded.

### S-B1: isolated Git helper and eight baseline results

Files: new `tests/Antiphon.Tests/Checkpoints/CheckpointFixtureGitEnvironment.cs`,
new `CheckpointFixtureGitEnvironmentTests.cs`, selected call sites in S/T/O,
and an explicit overload in `CheckpointFixtureProcess.cs` if needed. Do not
change its default behavior: its existing FIFO/config boundary probes rely on it.

The helper owns an empty scratch home outside the fixture's Git worktree, plus
empty regular global/system config files. Set GIT_CONFIG_GLOBAL and
GIT_CONFIG_SYSTEM to those files and GIT_CONFIG_NOSYSTEM=1. Set child HOME,
USERPROFILE and XDG_CONFIG_HOME to owned empty directories; preserve SDK/NuGet
cache locations explicitly (NUGET_PACKAGES and a run-owned, once-initialized
DOTNET_CLI_HOME) for dotnet children so isolation does not force a package
download/first-use setup per test. Preserve NuGet configuration explicitly:
moving HOME drops Linux's `~/.nuget/NuGet/NuGet.Config`, while Windows still
finds `%APPDATA%/NuGet/NuGet.Config` (APPDATA is unchanged). Before isolation,
resolve the effective fixture restore sources/mappings and non-secret settings
once per run. Write a run-owned explicit restore config with those effective
values and pass its absolute path via `RestoreConfigFile` to every seed and
nested fixture restore/build on both platforms. Preserve NUGET_PACKAGES and
credential-provider mechanisms; do not copy credentials or profiles. Compare
effective sources/mappings before and after in the helper boundary test without
logging config contents. If preserving the effective configuration requires an
unapproved credential copy, fail setup with the named prerequisite rather than
silently changing sources or falling back to downloads.

Remove the installed Git's `git rev-parse --local-env-vars` set (query once with
a 30s bound), plus a fixed portable minimum: GIT_ALTERNATE_OBJECT_DIRECTORIES,
GIT_OBJECT_DIRECTORY, GIT_DIR, GIT_WORK_TREE, GIT_IMPLICIT_WORK_TREE,
GIT_GRAFT_FILE, GIT_INDEX_FILE, GIT_NO_REPLACE_OBJECTS, GIT_REPLACE_REF_BASE,
GIT_PREFIX, GIT_SHALLOW_FILE, GIT_COMMON_DIR, GIT_CONFIG,
GIT_CONFIG_PARAMETERS and GIT_CONFIG_COUNT. Also clear GIT_NAMESPACE,
GIT_CEILING_DIRECTORIES and every GIT_CONFIG_KEY_n/GIT_CONFIG_VALUE_n before
installing the owned configuration values. Set GIT_NO_REPLACE_OBJECTS=1 and
GIT_ATTR_NOSYSTEM=1 for isolated fixture children; the latter is an intentional
test-fixture restriction, not a change to production HardenGit (which does not
set it). Keep repository-local `.gitattributes` effective. Global/system
includes.path, safe.directory, credential helpers and init.defaultBranch are
discarded by replacing those configuration sources, consistent with HardenGit's
global/system boundary. Pin local core.autocrlf=false before writing/staging fixtures. Keep
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
Keep the separately named diagnostic test
`CheckpointFixtureGitEnvironmentTests.legacy_hostile_config_produces_hardened_status_differential`
(CP-51): it must PASS by observing the unisolated child's sentinel setting and
the expected dirty CRLF/LF differential. It does not call the repaired helper
and cannot match the regression method's suffix filter. Verify global and system config sources, local
config still effective, parent environment unchanged, two parallel child roots
independent, and no eager process launch outside the strict ProcessSpawnLimit. The environment
control must independently query a harmless sentinel setting present only in the
owned global/system config and assert it absent after isolation: local
autocrlf=false alone must not mask a broken config isolation helper.

Tests: CP-37 red first (one deliberate failing regression, no fixture/build
error); then CP-13..CP-20, CP-38/CP-48/CP-50/CP-51 green and CP-39 on Linux.
CP-47 requalifies the five S-A T methods and S staged-untracked once because
their shared setup changed.
The O sites can be wired now while their five gates remain until S-B2.
Linux authoring 40m; red/green and Windows evidence have separate task budgets.
Push the compiling red source and record its full immutable SHA. After Linux
obtains its named CP-37 assertion failure, author and push the fix without
waiting for Windows. Windows Verify runs CP-37 at the retained red SHA in its
own assigned checkout, then the green rows at the pushed green SHA; it never
rewinds the task branch or repairs/mutates source. Both red assertions and both
green selections must be complete before S-B1 is accepted. Only the cross-host
authoring wait is dropped; red-before-fix commit order and evidence stay required.
If measured runs exceed that bound, hand off committed remaining CP IDs;
do not silently replace real-build checks with mocks.

### S-B2: five confinement results

Files: O gates; the shared helper only if the preceding call-site audit missed a
route. Retain the already-Windows O sibling, its UseAppHost assertions and its
explicit separate count. Tests: CP-21..CP-25; CP-40 Windows only. Linux must run
exactly the five portable methods, not the Windows-only sibling (which skips).
Linux authoring allowance 20m; separate Windows Verify supplies native evidence.

### S-C1: seven ordinary native mutations

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
that arrangement change) and CP-52 (exact CP-1 staged-untracked requalification
using the S-C1 build). Include CP-52 unconditionally: WriteProject is shared,
so fail-closed requalification is simpler than guessing whether its route moved.
Linux authoring allowance 45m; separate lane execution budgets are below.

### S-C2: staged shape replacement and file symlink

Files: S/native fixture writer. Port directory-on-file, file-on-directory and
symlink-on-file, exactly as specified below; remove their last runtime gates.
Use File.CreateSymbolicLink or the PowerShell/.NET equivalent for the file link;
require actual LinkTarget and preserved target bytes. Explicitly check link
creation capability on the native lane and report missing privilege/Developer
Mode as a prerequisite failure, without changing host policy. The investigation
proves directory symlinks on one host, not universal file-link privilege.
Tests: CP-33..CP-35. Each mutation row requires its ignored build-target marker
and exact state transition before its result can qualify. Linux authoring allowance 45m; separate lane budgets are below.

### S-F: final acceptance and one graph approval

No fixture redesign here. Finish all source changes, inspect the final tracked
build-definition delta and commit/push BEFORE audit. There should be no project,
props, targets, solution, SDK or package-definition changes under D-7. Policy
JSON and its policyHash remain unchanged; adding a .cs fixture does not change
LandVerificationBuildGraph's definition hash. Do not edit
LandVerificationBuildGraph.pins.txt or invent a definition-file delta.

Build an author-time evaluator independently, under a host slot, to a fresh
owned `bin-c1199auditupdate/` in the tool and its referenced projects. Run exactly
one `-Update` on the final definition tree (Linux Code owns this write):

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1199-audit-update-build -SlotWaitMinutes 10 -- dotnet build tools/Antiphon.LandGraphAudit -c Release --property:OutputPath=bin-c1199auditupdate/ -nodeReuse:false
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1199-final-graph -SlotWaitMinutes 10 -- pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Update -ToolPath tools/Antiphon.LandGraphAudit/bin-c1199auditupdate/Antiphon.LandGraphAudit.dll
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

CP-46 **builds its own evaluator inside its command row** into a distinct fresh
`bin-c1199auditcheck/`, then runs `-Check` only if that build succeeds. It does
not read the test assembly's staged tool or the author-time update output.
`RunScheduler.RunAsync` gives a ready serial command priority over pending
builds; `Pick` does not wait for a command row's Build cell, and
`PlanTableImporter` ignores that cell. Consequently table order, `After`, or a
nominal `CP-41` Build reference cannot supply this dependency. The shell's `&&`
provides the actual dependency within CP-46, even when CP-46 is scheduled first.
The command is bare and contains no inner quotes or `pwsh -Command` wrapper:
Windows uses `cmd.exe /d /s /c` with `ProcessStartInfo.ArgumentList`, while Linux
uses `/bin/sh -lc`. All command arguments are space-free relative paths; keep
them that way. The working-directory path is supplied separately by the driver.
Reject merely moving CP-46 to a later invocation: test-build outputs are removed
after joined execution and are not a cross-run prerequisite.

**Mandatory CP-46 output assertion, on each lane:** after terminal `wait` and
before deleting its owned outputs, require build/audit exit 0, unchanged pins
and exact committed graph, plus exactly one case-sensitive whole line `approved`
in that run's `rows/CP-46/console.log`. `AuditPhases.Run` in
`tools/Antiphon.LandGraphAudit.Core/PhaseClock.cs` emits it only after pass 1,
canary copy and pass 2 all succeed; the audit script forwards that output.
An echoed command, pre-check `digest=...` line or build success is insufficient.
The table's supported Expect cell retains this assertion as `expectText`.
`PlanTableImporter` does not turn it into a log matcher, and RowRunner's command
branch does not inspect `Expect`. As in existing plan acceptance contracts,
Code/Verify must run this explicit fail-closed assertion in addition to checking
the runner result. Do not add an unsupported manifest property or claim that
import/runner exit alone validates it.

Execute this in PowerShell directly (or an owned scratch `.ps1` via `-File`),
never as a quoted command-row wrapper. Set `$c1199FinalRunRoot` to the exact
directory printed for this S-F run, not a glob or latest-run lookup; unset/missing
input fails. Retain its output with the source SHA and run receipt:

```powershell
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($c1199FinalRunRoot)) { throw 'CP-46 run root required' }
$c1199AuditLog = Join-Path $c1199FinalRunRoot 'rows/CP-46/console.log'
$c1199AuditLines = [System.IO.File]::ReadAllLines($c1199AuditLog)
if (@($c1199AuditLines | Where-Object { $_ -ceq 'approved' }).Count -ne 1) {
    throw 'CP-46 incomplete: expected exactly one audit approved line'
}
$c1199AuditDll = 'tools/Antiphon.LandGraphAudit/bin-c1199auditcheck/Antiphon.LandGraphAudit.dll'
$c1199AuditDigest = (Get-FileHash -LiteralPath $c1199AuditDll -Algorithm SHA256).Hash
Write-Output ('CP-46 audit-output=approved evaluator-sha256=' + $c1199AuditDigest)
```

Missing/ambiguous output or DLL leaves CP-46 incomplete even if the runner is
green. This read-only evidence assertion adds no build/test run and fits CP-46's
existing five-minute estimate. The command-row audit covers all 53 rows: CP-46
is the only command row; the other 52 are test filters, so no second command
needs the quoting repair. Recheck that census after import.

The scheduler acquires CP-46's host slot before starting its shell. Its explicit
`-maxcpucount:1` stays within any positive grant; do not acquire a second nested
slot. Record the command, source SHA, build exit, SDK, tool DLL digest and audit
exit because a command row has `buildSource=notApplicable`, not a test-build
receipt. Before invoking the row, require all its output roots absent; retain
an inventory of roots it creates, including referenced projects. Command outputs
are outside scheduled-build automatic cleanup. Remove only these owned roots
after terminal `wait`, preserving logs; a rerun builds afresh. The update output
may be removed after its audit exits and is never reused for CP-46. Author-time
build plus update budget is 5 minutes per final tree; CP-46 is 5 minutes per lane
(2 build + 3 audit), provisional 10-minute row deadline, evaluator clocks unchanged.

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

Each literal command below selects its matching CP-n and exact method in the
Checkpoints table. A one-row rerun may rebuild; its shared-build allowance stays
separate from that method's execution estimate.
A/B/C correspond to the slices above. Time is whole execution minutes, excluding
the shared outer test-assembly build; native seconds are historical first-run
observations. B timings were early failures; use the successful control aggregate
for batch budgeting. All controls below must observe both tool and script unless
explicitly labeled script-only or local helper. All 35 stay required in policy.

The literal per-row commands below use these two task-local variables after
bootstrap/import and the deadline overlay; they are diagnostic one-row commands.
For ordinary execution use the grouped row sets to share the build.

```powershell
$c1199Tool = Join-Path (Get-Location).Path 'tools/Antiphon.Checkpoints/bin-c1199-plan-tool/Antiphon.Checkpoints.dll'
$c1199Manifest = Join-Path (Get-Location).Path '.antiphon/c1199-checkpoints.yaml'
```

| Exact checkpoint command / slice | Exact test (class alias S/T/O) | Production seam and required red witness | Windows fixture decision | Minutes / original seconds |
|---|---|---|---|---|
| `dotnet $c1199Tool run $c1199Manifest --rows CP-1 --serial` / S-A | `S.shape_staged_untracked` | P1 Settle/index; P3 invalid receipt. Require USER-KEEP plus index entry, tampered path and invalid bound receipt; omitting build git-add must fail. | No fixture port | 1 / 56.57 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-2 --serial` / S-A | `S.shape_clean_build` | P1 Prepare/Settle; P2 real build; P3 evidence. Require executed=1 and side/keep bytes; bypassing build/test cannot pass. | No fixture port | 2 / 70.94 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-3 --serial` / S-A | `S.missing_receipt_is_not_negative_success` | S Receipt(null), test-local negative oracle only. Null receipt must not return ok; accepting absence fails. This row alone proves no native production seam. | No fixture port | 1 / 0 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-4 --serial` / S-A | `S.shape_existing_stamp` | P3 exclusive stamp/output preflight. Require USER-OUTPUT and exact pre-existing-build-source refusal before build; bypass preflight must fail sentinel/receipt checks. | Gate lift plus portable target marker; mutation command port stays in S-C1 | 1 / 25.08 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-5 --serial` / S-A | `S.shape_compiler_collision` | P3 output refusal before compiler. Require original App.dll, exact pre-existing-output refusal and no successful build; accepting output fails. | Gate lift plus portable target marker; mutation command port stays in S-C1 | 1 / 26.18 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-6 --serial` / S-A | `S.shape_existing_output_directory` | P3 exclusive owned directory creation. Require pre-existing-output refusal and unchanged tracked sentinel; accepting empty foreign directory fails. | Gate lift plus portable target marker; mutation command port stays in S-C1 | 1 / 31.65 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-7 --serial` / S-A | `S.shape_existing_output_symlink` | P3 reparse/link-component refusal; P4 native inspection. Require real directory LinkTarget plus original outside/App.dll and refusal; replacing link by directory/copy fails. | Gate lift plus portable target marker; mutation command port stays in S-C1 | 1 / 25.01 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-8 --serial` / S-A | `T.intermediate_output_path_tampering_is_reported` | P1 Settle; P2 IntermediateOutputPath. Require generated AssemblyVersion bytes retained, named tamper and invalid receipt; bypass generation/detection fails. | No fixture port | 3 / 176.28 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-9 --serial` / S-A | `T.generated_target_file_tampering_is_reported` | P1 Settle; P2 GenerateVersion target. Require generated version 1 bytes retained and named tamper; an unexecuted target or hidden tamper fails. | No fixture port | 2 / 116.62 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-10 --serial` / S-A | `T.dirty_tracked_tree_is_refused_and_unmodified` | P1 Prepare dirty preflight. Require dirty-tracked exit 2, unchanged dirty/marker bytes, no build success; bypass refusal fails. | No fixture port | 1 / 42.07 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-11 --serial` / S-A | `T.tracked_snapshot_contains_committed_files` | Script Get-CheckpointTrackedSnapshot/Git membership. Require all five names including outside.txt and notes/readme.txt; restricting to project directory fails. | No fixture port | 1 / 10.72 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-12 --serial` / S-A | `T.index_added_file_is_kept_and_indexed` | P1 Settle index delta; P3 script receipt. Require INDEX-KEEP, fresh-user.txt in index and invalid receipt; skipping update-index or restoration fails. | No fixture port | 3 / 130.81 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-13 --serial` / S-B1 | `T.local_output_property_tampering_is_reported` | P1 Settle; P2 TreatAsLocalProperty OutputPath. Require actual MZ replacement DLL retained, tampered legacy path and invalid receipt; early dirty refusal fails. | No fixture port | 2 / 12.62 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-14 --serial` / S-B1 | `T.imported_props_reference_tampering_is_reported` | P1 Settle; P2 imported ProjectReference. Require Lib/legacy/Lib.dll MZ replacement and named tamper; not building imported dependency fails. | No fixture port | 2 / 19.6 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-15 --serial` / S-B1 | `T.transitive_reference_tampering_is_reported` | P1 Settle; P2 App-to-Mid-to-Lib traversal. Require transitive Lib replacement and invalid receipt; omitting transitive evaluation fails. | No fixture port | 2 / 21.43 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-16 --serial` / S-B1 | `T.clean_untracked_output_build_exits_zero` | P1 clean baseline; P2 real build. Require preserved source, owned output, no TRX and script build exit 0; skipping build or dirty refusal fails. | No fixture port | 2 / 14.47 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-17 --serial` / S-B1 | `T.fresh_checkout_run_executes_a_test` | P1 clean fresh baseline; P2 build/run; P3 source binding. Require no obj before tool, executed=1, exit 0 and buildSource=verified from each entry; mock success without execution fails. | No fixture port | 3 / 66.89 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-18 --serial` / S-B1 | `T.staged_untracked_file_is_kept_and_indexed` | P1 index/worktree delta; P3 negative receipt. Require USER-KEEP, index entry, named tamper and source_ineligible; omitting git-add or deleting newly staged file fails. | No fixture port | 2 / 23.91 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-19 --serial` / S-B1 | `T.staged_ignored_file_is_kept_and_indexed` | P1 ignored-path/index delta; P3 negative receipt. Require IGNORED-KEEP and forced-add entry; removing force or undoing the index fails. | No fixture port | 2 / 49.07 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-20 --serial` / S-B1 | `T.passing_script_requires_writable_source_receipt` | Script passing build/test to required source.json write. Require collision directory and source.json exit 2 after executed=1; add executed=1/collision proof so unrelated early refusal cannot pass. Logging receipt failure with exit 0 must fail. | No fixture port | 2 / 16.43 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-21 --serial` / S-B2 | `O.imported_props_build_preserves_the_tracked_dependency` | P2 imported References.props; P1/P3 owned outputs. Require original tracked Lib DLL/assets AND new owned Lib.dll/artifacts assets; skipping graph/build or writing normal Lib output fails. | No fixture port | 3 / 164.99 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-22 --serial` / S-B2 | `O.directory_build_props_build_preserves_the_tracked_dependency` | P2 Directory.Build.props import; P1/P3 outputs. Require original DLL/assets plus both new owned files; bypass implicit import or confinement fails. | No fixture port | 2 / 111.92 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-23 --serial` / S-B2 | `O.conditional_import_build_preserves_the_tracked_dependency` | P2 real condition/property graph evaluation; P1/P3 outputs. Require condition-selected Lib in owned output plus unchanged tracked bytes; static-only graph or no build fails. | No fixture port | 2 / 147.07 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-24 --serial` / S-B2 | `O.transitive_import_build_preserves_the_tracked_dependency` | P2 transitive imported graph; P1/P3 outputs. Require deepest Lib outputs in App owned root and tracked bytes unchanged; dropping transitive edge fails. | No fixture port | 3 / 166.16 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-25 --serial` / S-B2 | `O.unevaluable_import_is_refused_before_the_build` | P2 graph evaluation refusal before build. Require graph-unevaluated and KEEP-SENTINEL, no build success; generic dirty refusal or treating missing import as empty graph fails. | No fixture port | 1 / 18.35 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-26 --serial` / S-C1 | `S.shape_untracked_descendant` | P1 CaptureUntracked/Stamp/Settle. Require moved USER-UNTRACKED descendant, tracked.txt tamper, manual command and invalid receipt; unchanged arrangement fails. | Port: remove tracked regular file; Directory.Move user-dir to tracked.txt; do not stage descendant. | 1 / 56.57 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-27 --serial` / S-C1 | `S.shape_ignored_descendant` | P1 ignored manifest/Stamp/Settle. Require USER-IGNORED and exact invalid receipt; overlooking ignored child or leaving original tracked file fails. | Port: same directory move, preserve user.dat ignore entry and unstaged state. | 1 / 54.28 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-28 --serial` / S-C1 | `S.shape_modified_tracked` | P1 content Stamp/Settle. Require MUTATED bytes retained and named tamper; a no-op or automatic restore fails. | Port: WriteAllBytes ASCII MUTATED to tracked.txt inside Exec, no newline. | 1 / 51.54 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-29 --serial` / S-C1 | `S.shape_deleted_tracked` | P1 absence Stamp/Settle. Require tracked.txt absent, named tamper and invalid receipt; no-op or recreation fails. | Port: File.Delete tracked.txt inside Exec; assert prior existence in fixture. | 2 / 60.38 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-30 --serial` / S-C1 | `S.shape_regular_user_move` | P1 tracked/untracked transition/Settle. Require USER-MOVED at tracked.txt and conflict; add source-absence assertion so a copy cannot masquerade as move. | Port: File.Move user-move.txt to tracked.txt with overwrite=true, preserving bytes. | 1 / 55.83 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-31 --serial` / S-C1 | `S.shape_deleted_untracked` | P1 pre-existing untracked manifest/Settle. Require user-move.txt absent and named tamper; add MUTATED tracked.txt witness; recreating user path fails. | Port: File.Delete user-move.txt then exact ASCII MUTATED write to tracked.txt. | 1 / 52.83 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-32 --serial` / S-C1 | `S.shape_changed_untracked` | P1 pre-existing untracked hash/Settle. Require CHANGED-USER and named tamper; add MUTATED tracked.txt witness; preserving original bytes or no-op fails. | Port: ASCII CHANGED-USER to user-move.txt and MUTATED to tracked.txt. | 1 / 50.22 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-33 --serial` / S-C2 | `S.shape_directory_on_file` | P1 shape stamp/index/unstaged descendants. Require USER-SECRET staged and USER-UNSTAGED unstaged, both exact bytes, named tamper and invalid receipt; missing target execution fails. | Port: File.Delete tracked.txt; Directory.Move user-dir; git add -A tracked.txt then git restore --staged tracked.txt/unstaged.txt. | 2 / 20.61 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-34 --serial` / S-C2 | `S.shape_file_on_directory` | P1 directory-to-file shape/index/Settle. Require USER-FILE at dir and dir/a.txt tamper; add staged replacement/old-entry absence assertions; no-op/copy arrangement fails. | Port: remove only owned fixture dir recursively, File.Move payload.dat to dir, git add -A dir. | 2 / 65.48 |
| `dotnet $c1199Tool run $c1199Manifest --rows CP-35 --serial` / S-C2 | `S.shape_symlink_on_file` | P1 StampLink/Settle; P4 native file link. Require real file LinkTarget ending link-target.txt, LINK-BYTES target unchanged, tamper and invalid receipt; copy/hardlink/no-op fails. | Port: delete tracked regular file, File.CreateSymbolicLink tracked.txt -> link-target.txt, git add tracked.txt. Verify link/index without assuming Git symlink mode on unqualified hosts. | 2 / 65.83 |

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
running at task settlement. This prebuilt DLL invocation is orchestration only
and cannot implicitly build. The executor acquires its own build/driver slots; do not hold an outer
slot around `run` while it waits for an inner slot. Bootstrap/import itself goes
through `scripts/build-slot.ps1`, and is not the test-assembly build. Running the
exported manifest is necessary here because the plan-table schema cannot carry
the explicit per-row deadlines; its selection must be byte-equivalent to import.

| Slice | Windows rows | Linux rows | Expected target results (support results separate) |
|---|---|---|---|
| S-A | CP-1..CP-12,CP-36 | same | 12 per lane |
| S-B1 red | CP-37 | CP-37 | 1 expected configuration assertion failure per lane at immutable red SHA committed before fix; Windows execution may follow fix authoring |
| S-B1 green | CP-13..CP-20,CP-38,CP-47,CP-48,CP-50,CP-51 | same plus CP-39 | 8 new plus 6 explicitly requalified per lane |
| S-B2 | CP-21..CP-25,CP-40 | CP-21..CP-25 | 5 plus 1 existing Windows sibling |
| S-C1 | CP-26..CP-32,CP-49,CP-52 | same | 7 new plus 4 collision and 1 staged-untracked requalifications |
| S-C2 | CP-33..CP-35 | same | 3 per lane |
| S-F | CP-41..CP-46 | CP-41..CP-43,CP-45,CP-46 | 35 per lane, plus 1 existing Windows sibling |
| S-F-cost | CP-53 | CP-53 | 1 support test per lane; no 35-row re-execution |

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
| CP-21 | S-B2 | `tests/Antiphon.Tests -> bin-c1199b2/` | both-s-b2-21 | `/*/*/CheckpointOutputConfinementTests/imported_props_build_preserves_the_tracked_dependency` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 13 | true |
| CP-22 | S-B2 | `CP-21` | both-s-b2-22 | `/*/*/CheckpointOutputConfinementTests/directory_build_props_build_preserves_the_tracked_dependency` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 2 | true |
| CP-23 | S-B2 | `CP-21` | both-s-b2-23 | `/*/*/CheckpointOutputConfinementTests/conditional_import_build_preserves_the_tracked_dependency` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 2 | true |
| CP-24 | S-B2 | `CP-21` | both-s-b2-24 | `/*/*/CheckpointOutputConfinementTests/transitive_import_build_preserves_the_tracked_dependency` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 3 | true |
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
| CP-36 | S-A | `CP-1` | both-required-roster | `/*/*/(ReleaseGatePlatformTests*)\|(CheckpointNamespaceCensusUsageTests*)/(C1199_WindowsRequiredRoster*)\|(LinuxOnlyWindowsExclusionPolicy*)\|(namespace_census_matches_compiled_checkpoint_cases*)` | V-3 R-1 | exactly 3 passed; 0 failed/skipped | 3 | 2 | true |
| CP-37 | S-B1-red | `tests/Antiphon.Tests -> bin-c1199bred/` | both-git-red | `/*/*/CheckpointFixtureGitEnvironmentTests/fixture_commit_matches_hardened_status_under_hostile_config` | V-2 red | exactly 1 executed; expected named dirty-baseline assertion failure | 1 | 11 | true |
| CP-38 | S-B1 | `CP-13` | both-git-child-boundary | `/*/*/CheckpointFixtureGitEnvironmentTests/(child_environment_isolated_without_changing_parent*)\|(real_driver_keeps_confinement_and_cancellation*)` | V-2 R-1 | exactly 2 passed; 0 failed/skipped | 2 | 2 | true |
| CP-39 | S-B1 | `CP-13` | linux-routing-cancellation | `/*/*/CheckpointFixtureRoutingTests/C1233_TokenCallers` | V-2 R-1 | exactly 4 passed; 0 failed/skipped | 4 | 1 | true |
| CP-40 | S-B2 | `CP-21` | windows-existing-apphost | `/*/*/CheckpointOutputConfinementTests/windows_confined_build_leaves_use_app_host_unset` | R-1 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-41 | S-F | `tests/Antiphon.Tests -> bin-c1199final/` | both-final-shapes | `/*/*/CheckpointShapeConflictTests/*` | V-1 V-4 R-1 R-4 | exactly 17 passed; 0 failed/skipped | 17 | 24 | true |
| CP-42 | S-F | `CP-41` | both-final-baseline | `/*/*/CheckpointTrackedBaselineTests/*` | V-1 V-2 R-1 R-4 | exactly 13 passed; 0 failed/skipped | 13 | 24 | true |
| CP-43 | S-F | `CP-41` | both-final-confinement | `/*/*/CheckpointOutputConfinementTests/(imported_props_build_preserves_the_tracked_dependency*)\|(directory_build_props_build_preserves_the_tracked_dependency*)\|(conditional_import_build_preserves_the_tracked_dependency*)\|(transitive_import_build_preserves_the_tracked_dependency*)\|(unevaluable_import_is_refused_before_the_build*)` | V-1 V-2 R-1 R-4 | exactly 5 passed; 0 failed/skipped | 5 | 10 | true |
| CP-44 | S-F | `CP-41` | windows-final-apphost | `/*/*/CheckpointOutputConfinementTests/windows_confined_build_leaves_use_app_host_unset` | R-1 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-45 | S-F | `CP-41` | both-final-guards | `/*/*/(CheckpointFixtureGitEnvironmentTests*)\|(ReleaseGatePlatformTests*)\|(CheckpointNamespaceCensusUsageTests*)\|(LandNonCodeSkipTests*)/(fixture_commit_matches_hardened_status_under_hostile_config*)\|(child_environment_isolated_without_changing_parent*)\|(real_driver_keeps_confinement_and_cancellation*)\|(C1199_WindowsRequiredRoster*)\|(LinuxOnlyWindowsExclusionPolicy*)\|(namespace_census_matches_compiled_checkpoint_cases*)\|(C1385_RegistryMatchesNarrowGraph*)` | V-2 V-3 R-3 | exactly 7 passed; 0 failed/skipped | 7 | 2 | true |
| CP-46 | S-F | n/a | both-final-audit | `dotnet build tools/Antiphon.LandGraphAudit -c Release --property:OutputPath=bin-c1199auditcheck/ -maxcpucount:1 -nodeReuse:false && pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin-c1199auditcheck/Antiphon.LandGraphAudit.dll` | R-3 | build and audit exit 0; exact committed graph and unchanged pins; require exactly one whole line `approved` in rows/CP-46/console.log and fresh evaluator SHA-256 via mandatory post-run assertion | n/a | 5 | true |
| CP-47 | S-B1 | `CP-13` | both-shared-git-regressions | `/*/*/(CheckpointShapeConflictTests*)\|(CheckpointTrackedBaselineTests*)/(shape_staged_untracked*)\|(intermediate_output_path_tampering_is_reported*)\|(generated_target_file_tampering_is_reported*)\|(dirty_tracked_tree_is_refused_and_unmodified*)\|(tracked_snapshot_contains_committed_files*)\|(index_added_file_is_kept_and_indexed*)` | V-1 V-2 R-1 | exactly 6 passed; 0 failed/skipped | 6 | 11 | true |
| CP-48 | S-B1 | `CP-13` | both-git-census | `/*/*/CheckpointNamespaceCensusUsageTests/namespace_census_matches_compiled_checkpoint_cases` | V-3 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-49 | S-C1 | `CP-26` | both-native-collision-sentinels | `/*/*/CheckpointShapeConflictTests/(shape_existing_stamp*)\|(shape_compiler_collision*)\|(shape_existing_output_directory*)\|(shape_existing_output_symlink*)` | V-4 R-1 | exactly 4 passed; 0 failed/skipped | 4 | 3 | true |
| CP-50 | S-B1 | `CP-13` | both-git-regression-green | `/*/*/CheckpointFixtureGitEnvironmentTests/fixture_commit_matches_hardened_status_under_hostile_config` | V-2 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-51 | S-B1 | `CP-13` | both-hostile-legacy-control | `/*/*/CheckpointFixtureGitEnvironmentTests/legacy_hostile_config_produces_hardened_status_differential` | V-2 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-52 | S-C1 | `CP-26` | both-staged-fixture-requalification | `/*/*/CheckpointShapeConflictTests/shape_staged_untracked` | V-1 V-4 R-1 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |
| CP-53 | S-F-cost | `tests/Antiphon.Tests -> bin-c1199cost/` | both-class-cost-admission | `/*/*/RcRosterRepairTests/C1199_Costs` | V-3 R-4 | exactly 1 passed; 0 failed/skipped | 1 | 12 | true |
| CP-54 | S-F | `CP-41` | both-final-documentation | `/*/*/ReleaseBranchModelDocumentationTests/C1199_FinalClosureDocumentation` | V-3 R-3 | exactly 1 passed; 0 failed/skipped | 1 | 1 | true |

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
| S-B2 five corrected O cases | Passing control 448.44s (7m28s); outer wall 504.14s includes 45s admission | 11, plus existing sibling 1 on Windows | 10m |
| S-C1 seven ports | First failed arrangements sum 381.65s; no successful port sample | 8, plus 3 collision checks and 1 staged-untracked requalification | 10m |
| S-C2 three ports | Failed arrangements sum 151.92s; no successful port sample | 6 | 10m |
| S-F all 35, class batches | No complete clean-source passing run exists | Shape 14 + T 24 + O 10 = 48; Windows sibling 1 separate | 10m, included in CP-41's 24 |
| Initial investigation aggregate | 35m43.591s includes the 56.273s Windows sibling and early failures | Historical cost retained, not a success estimate | Excludes 9m56.13s build |

Windows Full already discovers/requires these 35 but currently spends almost no
body time on them. Forecast its incremental successful body cost at **about
40-50 minutes**, provisional: 11m52s of unchanged passes + 20m46s of corrected
configuration controls + roughly 9-14 minutes for native ports and launch
remainder. The final checkpoint budget uses 48 minutes. The existing O Windows
sibling is already in Full, so do not count it again. The new helper's four
support tests add a separately measured small cost (budget 4 minutes).

Use `new Full wall = latest comparable Full wall - old 35-skip wall + measured
35-success wall + new support-test wall + measured build/packing delta`.
There is no current comparable complete Full baseline in this evidence; do not
assert a new fleet-wide total or reuse CARD-0856's historical 270 minutes as one.
The existing Full assembly build is already paid; fixture builds inside the
35 bodies are incremental. Costs live in **`scripts/lib/nightly-class-costs.json`**;
`tests/test-execution-policy.json` has no class-cost rows. Cost changes do not
authorize exclusions or rewriting historical outcomes.

### Required measured-cost follow-up (S-F-cost)

S-F's native Windows Verify task retains the complete successful CP-41/42/43/44
TRX and timing evidence. Then a separate Linux Code task changes only the three
cost entries below and their provenance, plus the named offline cost witness;
a native Windows Verify task runs CP-53 at that pushed SHA. This sequencing
avoids pretending a pre-cost source receipt verified the later cost commit.
No rerun of the expensive 35 is required merely to edit prices: their original
SHA and measured outcomes remain explicit. S-F-cost cannot start until S3 is
confirmed published on its target (rc top-up 39; separate S3 master port), and
master additionally requires the CARD-1613 port. These are hard prerequisites,
not defaults the caller can waive. S-F-cost must land after S-F and
before the next Windows Full qualification can earn credit. If native costs
are missing, keep this obligation incomplete and retain the old rows as stale;
do not claim the 40-50 minute forecast or correct packing has been qualified.

| Exact key under `classes` | Required edit / native sample | Historical evidence retained, not replacement data |
|---|---|---|
| `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests` | Add; nodes=17, sum of 17 Passed durations from S-F Windows CP-41, one complete class sample | `shape-native` at investigation SHA `ade54222543381e2e1b31ad79d8ae33a4035f70b`: 771.72s interval, 7 pass / 10 fail; about 770s is provisional, not passing class cost |
| `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests` | Add; nodes=13, sum of 13 Passed durations from S-F Windows CP-42, one complete class sample | `baseline-config-selected`: 8 pass, 797.40s, plus five initial passing methods around 476s; about 1270s is a cross-run forecast |
| `Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests` | Correct; nodes=6, sum of five CP-43 Passed durations plus the one CP-44 sibling, one complete class sample | Existing 25s/6 measured row: run `8f055f87`, `all-group-62`, 5 early failures. `confinement-config-selected` is 448.44s for FIVE cases; the 56.273s existing Windows sibling must also be priced, roughly 505s total |

Record each entry's source SHA, Windows platform, run ID, CP IDs, all TRX digests,
expanded sample counts, duration sum and `n=1 complete class sample; not p95` in
its `source`. Use `basis: trx`, rounded to one decimal as the refresh utility
does. Do not count setup/build/admission as case duration or mix Linux samples.
Retain `platform: windows` and every unrelated row and source field byte-for-byte
where possible. `scripts/refresh-nightly-class-costs.ps1` sums all supplied TRX
outcomes without filtering failures, merges duplicate selections, sets platform
from the invoking host, and drops existing per-row `source` fields. Therefore
run it only against a scratch copy using the vetted complete Windows TRX set;
review and transfer exactly these three candidate entries, adding provenance.
Never run its default in-place refresh on Linux over the committed whole file.

Extend `scripts/test-rc-roster-repairs.ps1` with case `C1199_Costs` and add the
same-named TUnit wrapper in `tests/Antiphon.Tests/Scripts/RcRosterRepairTests.cs`.
Require five `C1199` harness assertions: `complete-class-denominators` (17/13/6),
`successful-trx-provenance` (all three named evidence sources; no stale 25s O),
`planner-uses-committed-costs` (real planner prices full compiled S/T/O rosters
at their recorded seconds, and prices required subsets proportionally),
`policy-hash-unchanged`, and `admission-keeps-suite-watchdog` (real S3 admission
with no calibration, plus an over-ceiling refusal). CP-53 selects exactly this
one test; its fixture supplies a deterministic hostile underprice to show the
cost observation differs, without invoking a Full run. Record the real planned
UID sets, group membership, cost digest and admission decision beside CP-53.

`Get-NightlyClassCostTable` divides seconds by nodes; the packing target is
900s, not a hard watchdog. A roughly 1270s T class stays alone (under the 2400s
lone-class ceiling); S and O cannot fit together at roughly 770+505s. The old
model has a 0.5s/node fallback (`NightlyLaunchNodeSeconds`); the actual S/T
`Check` family names receive its 2x Heavy factor, hence 1s/node before measured
rows override it. O's existing measured row is 25s/6 and bypasses that factor.
Both fallback and measured prices severely understate these bodies. Validate
actual grouping after the refresh; do not force a 900s split
or increase watchdogs just to fit a forecast.

`Get-NightlyPolicyHash` hashes only canonical execution-policy JSON, excluding
its own hash field. The separate cost file does **not** change that policy hash;
record the new cost-file SHA-256 separately (`CostFileDigest` in the planned
manifest). Use the policy hash actually on the post-top-up base, preserving it
through this work; the round-1 hash above is historical, not a pin to restore.
S3 at `192f15240f757e04fb5fdb032e447f926e563757` consumes the revised planned
seconds for admission (antiphon estimate ceiling 2400s, suite watchdog 3600s).
The corrected forecasts T about 1270s, S about 770s and O about 505s all remain
below that ceiling; they change packing and planned seconds, not the admission
verdict. The stale prices conceal roughly 2500s in 900s-target groups and can
increase watchdog-kill risk; they do not demonstrate an admission flip.
Its live planner passes each chunk's `costBasis` and no calibration; changing a class
row's basis to `trx`/`measured` does not activate calibrated watchdog reduction.
Eligible calibration requires at least 20 uncensored matching samples, policy
binding and exact stratum including `costDigest`; a changed cost digest makes
old calibration ineligible even though policyHash is unchanged. Preserve the
unmeasured suite-watchdog fallback and fail-closed refusal above the ceiling.
Do not fabricate calibration from these n=1 class samples or alter S3 admission.

S-F-cost budget: Linux Code 60m (20 authoring + 20 build/check + 10 slot allowance
+ 10 reporting); Windows Verify 40m (20 build/check + 10 slot + 10 reporting).
One shared test build per lane; CP-53 body estimate 2m, deadline 4m. This is a
required follow-up, separate from the aggregate-run Debug investigation.
Its cost JSON and test/harness edits do not change graph definition inputs;
retain S-F's single `-Update`, with no second refresh. If implementation requires
a definition-file change, stop this follow-up and commission a revised graph
approval separately rather than silently expanding D-7.

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
metadata); verify all 53 IDs, After, build IDs, filters, counts and estimates
are identical to import. This is the executable checkpoint manifest for Code,
not a policy/schema change. Keep the unedited import alongside it.

| Rows | Explicit timeoutMinutes | Basis / limitation |
|---|---:|---|
| CP-1..CP-35 | Twice the per-row execution Minutes in the per-row table (2, 4 or 6) | CP-21 and CP-24 are 6m: about 2x late-failure samples 164.99/166.16s, and above retained 300s method clocks; other bounds provisional; shared build separate |
| CP-36 | 4 | 2m support batch, provisional |
| CP-37 | 2 | Tests-first one-case red body; its 10m build is separate |
| CP-38 | 4 | Two new helper tests, provisional |
| CP-39,CP-40 | 2 | Existing small control selections, provisional |
| CP-41 | 28 | 2x 14m shape batch, provisional |
| CP-42 | 48 | 2x 24m complete T batch, provisional; Debug required |
| CP-43 | 20 | 2x 10m confinement batch, provisional |
| CP-44 | 2 | Historical Windows sibling 56.273s, n=1 |
| CP-45 | 4 | 2m guard batch, provisional |
| CP-46 | 10 | 2x (2m independent tool build + 3m audit), provisional; evaluator clocks unchanged |
| CP-47 | 22 | 2x 11m six-case regression batch, provisional |
| CP-48,CP-50,CP-51,CP-52 | 2 | One small case each, provisional |
| CP-49 | 6 | 2x 3m collision batch, provisional |
| CP-53 | 4 | 2x 2m offline cost/admission witness; 10m shared build separate |

Set `timeouts.rowMinutes: 20` for the shared-build floor (approximately 2x the
9m56s build sample) and `build.slotWaitMinutes: 10` for each acquisition; never
retry an admission expiry unleased. The current scheduler couples each build's
deadline to its largest dependent row: S-B1 can become 22m and S-F becomes 48m.
That is a disclosed **CARD-1199/CARD-1404 provisional build-deadline exception**,
not a measured 48m build need or permission to let an owned idle build drift.
Debug measures it; removing that scheduler coupling is separate work. The
execution allowance uses the existing finite derivation
`max(30, 2 * sum(selected EstimatedMinutes) + 10)`; the extra ten minutes and
30-minute minimum are a disclosed provisional orchestration allowance, not a
per-test timeout. The default hard-wall allowance adds the one 10-minute admission
allowance. Do not set a blanket --row-timeout (it would overwrite every tight
per-row deadline). Report exit 4 as not run,
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

### Separate commission budgets

Each cell is a whole-minute wall ceiling, not a measured duration. Linux Code
includes authoring, its local checks and reporting; Windows Verify includes
only native checks and reporting. Both include one 10m admission contingency.
No task is assumed to reach the other OS. Task source remains frozen during
native verification; any repair gets a new committed SHA and affected rows.

| Slice | Linux Code | Native Windows Verify | Allocation rationale |
|---|---:|---:|---|
| S-A | 90 | 60 | Linux 40 authoring + 30 build/body + 10 admission + 10 report; Windows same 30 build/body + 10 admission + 20 report |
| S-B1 | 150 | 110 | Linux 40 authoring + 85 red/green build/body + 10 admission + 15 report; Windows 85 checks + 10 admission + 15 report, across the red then green commits |
| S-B2 | 65 | 45 | Linux 20 authoring + 25 build/body + 10 admission + 10 report; Windows 25 checks + 10 admission + 10 report |
| S-C1 | 90 | 50 | Linux 45 authoring + 25 build/body + 10 admission + 10 report; Windows 25 checks + 10 admission + 15 report |
| S-C2 | 85 | 45 | Linux 45 authoring + 20 build/body + 10 admission + 10 report; Windows 20 checks + 10 admission + 15 report |
| S-F | 110 | 95 | Linux 15 finalization + 70 build/body/audit + 10 admission + 15 report; Windows 70 checks + 10 admission + 15 report |
| S-F-cost | 60 | 40 | Linux 20 authoring + 20 build/body + 10 admission + 10 report; Windows 20 checks + 10 admission + 10 report |
| Total commissioned ceilings | 650 | 445 | 1095m aggregate task allocation, not elapsed critical path or a test run |

S-B1's Windows Verify task runs the retained red SHA and then the pushed green
SHA after Linux authoring; Linux does not wait for Windows before writing the
fix. Start native Verify when both immutable SHAs are available, using separate
assigned checkouts as needed, with no branch rewind. If scheduling requires
distinct red and green native tasks, they share the 110m allowance; do not hold
a live session waiting indefinitely. These are
execution handoffs, not changes to product input-wait/session release behavior.
A task that actually waits keeps its seat with parking disabled; only explicit
owner settlement/release ends that wait, with no automatic deadline (CARD-1083).

Re-use exact same-source receipts where permitted and keep their original SHA;
never relabel them. S-F-cost produces its own narrow final-source receipts.
The aggregate Debug obligation applies per over-30m selection and is not waived
by splitting the authorship and native evidence into different tasks.

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

## Plan validation and acceptance handoff

Round 3 imported successfully at `fd443b108` with **53 rows, eight shared test
builds and 223 whole-minute estimates**; per-lane totals remain Linux 221m and
Windows 222m. CP-46 remains 5m including its own evaluator build. Static audit
found 35 unique target filters, 35 canonical OR operands with no suffix
collisions, exactly one command row (CP-46), and zero quotes in that command.
Its exact command and required `approved` assertion survive import unchanged.
CP-36/45 still select 3/7 post-CARD-1613 methods; planned future names were
included explicitly. This is source/import evidence, not compiled/native proof.

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1199-round3-import -SlotWaitMinutes 10 -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c1199r3import/ -- import --plan docs/superpowers/plans/2026-10-30-card-1199-windows-runnable-plan.md --out .antiphon/plan-f8a94733/imported.yaml
```

Unedited round-3 importer/slot outcome (five compiler warnings, no errors):

```text
BUILD SLOT granted lease=dcb11df0-d8ca-4519-9cb5-d25786a1f87e waited=0s maxcpucount=6
WARNING CP-41: derived row timeout (72 minutes) exceeds the 45 minute advisory threshold; consider splitting the row (timeout is not capped)
WARNING CP-42: derived row timeout (72 minutes) exceeds the 45 minute advisory threshold; consider splitting the row (timeout is not capped)
imported 53 rows -> .antiphon/plan-f8a94733/imported.yaml
BUILD SLOT released lease=dcb11df0-d8ca-4519-9cb5-d25786a1f87e held=8s
```

The slot wrapper disclosed that this `dotnet run` implicit build used the
default node count. The two timeout advisories still require the explicit
overlay above. Generated import/static-audit evidence remains ignored under
`.antiphon/plan-f8a94733/`; remove only the two owned `bin-c1199r3import/`
outputs after all importer use. No product test, audit or Windows execution ran.
The post-run log assertion remains mandatory for future CP-46 native credit.

Round-3 source checks used top-up 38
`130ea525aa1f25230123ee3584ea58dbf0604b95`, remote rc
`ade54222543381e2e1b31ad79d8ae33a4035f70b`, master
`98fc3ceb6440a17b68cafe3ecc21c76d190b18ca`, and S3
`192f15240f757e04fb5fdb032e447f926e563757`. The 35-row runtime-skip premise,
16 shape arguments and 13 T / 6 O methods still hold. S/T/O have no covering
execution-policy exclusions or NativeLinuxOnly markers. The top-up wrapper has
six assertions, master the old eight; S3 admission is absent on all three
observed target/top-up-38 trees. Both discovery routes returned HTTP 200;
default placement resolves to Linux and Linux/native Windows lanes were
available. These are observations, not dispatch reservations or landed refs.

Round 3 fetched master and repeated `git merge-tree --write-tree origin/master
HEAD`: exit 1, the same four inherited conflicts recorded below. Whole-branch
landing remains refused; publish only the plan-file delta from this task.

### Historical round-2 validation

Round 2 importer accepted **53 rows, eight shared test builds**, exit 0 at
`e4a7e23bd`. The 223 whole-minute estimates in the union table include all
build-once allocations; actual closed lists total 221m on Linux and 222m on
Windows (443m across all slices/lanes, including the expected-red row). These
are planning sums, not one authorized aggregate execution. CP-46 also performs
its own command-scoped tool build, already included in its 5m estimate.

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1199-round2-import -SlotWaitMinutes 10 -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c1199r2import/ -- import --plan docs/superpowers/plans/2026-10-30-card-1199-windows-runnable-plan.md --out .antiphon/plan-dd46f01b/imported.yaml
```

Unedited importer/slot outcome:

```text
BUILD SLOT granted lease=26f5953f-18de-4c96-832b-6788384b116f waited=0s maxcpucount=6
WARNING CP-41: derived row timeout (72 minutes) exceeds the 45 minute advisory threshold; consider splitting the row (timeout is not capped)
WARNING CP-42: derived row timeout (72 minutes) exceeds the 45 minute advisory threshold; consider splitting the row (timeout is not capped)
imported 53 rows -> .antiphon/plan-dd46f01b/imported.yaml
BUILD SLOT released lease=26f5953f-18de-4c96-832b-6788384b116f held=7s
```

The bootstrap emitted five compiler warnings and no errors. Its implicit
`dotnet run` build used the default node count, as the slot wrapper disclosed;
Code bootstraps may build first under the wrapper to apply the grant's cap.
The two importer deadline advisories remain addressed by the explicit overlay,
not suppressed. No product tests, native Windows actions, graph audit or Full
qualification ran in Plan. Historical round-1 validation remains 50 rows,
lease `19e2a65a-beef-4843-ae13-de02c35e4264`, 25s held; it is superseded by
this 53-row plan rather than relabeled.

Static source/plan audit: 35 unique target identities, 53 checkpoint rows, and
35 class/method OR-operand checks without suffix collisions. The audit combined
current source with the top-up 38 policy wrapper and explicitly planned names;
it does not claim compiled execution. Code must repeat the identity comparison
against its fresh compiled roster/TRX. The exact CP-36/45 counts are 3/7; the
legacy control's distinct name cannot expand CP-45. The post-top-up policy has
zero S/T/O exclusions and no NativeLinuxOnly marker on those source classes.
The runtime skip census remains 23 -> 15 -> 10 -> 3 -> 0.

Both platform discovery routes returned HTTP 200 in round 2. Default placement
resolved to Linux, with eligible Linux and native Windows lanes; no host is
pinned. Only this plan file changed relative to the assigned base. Generated
import and static-audit evidence stays under ignored `.antiphon/plan-dd46f01b/`;
remove only the Plan-owned `bin-c1199r2import` directories after the joined
importer. Do not remove unrelated scratch or evidence.

After fetching origin/master, `git merge-tree --write-tree origin/master HEAD`
returned exit 1 with four inherited conflicts: `.antiphon/task-af2b78c8.md`,
`docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md`,
`server/Application/Services/CapacityRecoveryService.cs`, and
`tests/Antiphon.Tests/Application/CapacityLockOrderTests.cs`. This is not a
landable whole branch. Use the separate plan-only Docs publication described
above; no merge, rebase, conflict repair or target push was performed here.

The final Opus review is complete and its two defects are amended above.
After confirmed rc top-up 38 publication,
commission S-A, S-B1/S-B2, S-C1/S-C2, S-F, then the required S-F-cost follow-up
in order. Require the CARD-1613 master port before implementation ports and S3
on each target before its S-F-cost. Preserve pending R-2 and commission the aggregate Debug work. Existing
authority covers implementing the 35 rows; no further go-ahead is needed.

--- next stage ---
next: code
handoff: After confirmed rc top-up 38 publication, commission Linux Code and native Windows Verify per slice. Publish only the plan-file delta. Require CARD-1613 on master before ports and S3 on each target before S-F-cost. CP-46 needs approved log-line/digest proof; retain immutable CP-37 red SHA, pending R-2 and aggregate Debug.
artifact: docs/superpowers/plans/2026-10-30-card-1199-windows-runnable-plan.md
