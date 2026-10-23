# CARD-1528: close filesystem and metadata gaps in land graph approval

Plan task `5951c90a`, 2026-10-23. Inspected master/base:
`bd215d7b4f559db69fbf4d7363417fce0f2c2467`. Read the full card, the
[CARD-1483 plan](2026-10-22-card-1483-real-msbuild-land-audit-plan.md), and its
S2, S3, S4 and S5 amendments. This deliverable is documentation only; no build,
test-suite run, desktop access or product activation occurred. TestDesign is a
separate stage: this brief requests verification design but does not fold that
stage into Plan. It must finalize the small executable fixture/roster amendment.

The invariant is that a docs-only land must never earn a verification skip from
an approved graph whose result can change with unhashed documentation state.
Equality in two evaluations is a counterexample search, not a proof of independence:
an absent arbitrary filename and two reads returning the same Boolean evade it.
Keep the real evaluator; refuse filesystem property functions that it cannot
observe, including functions whose result is stored only in metadata.

## Ground truth

| Card/brief assumption | Code and measured behavior at the base | Consequence |
|---|---|---|
| Author time `-Update/-Check` both evaluate. | Only `-Update` invokes the DLL. `-Check` checks the pre-check, two-line manifest and three-line digest/SDK/pins record. Land compares the embedded manifest only. | Preserve this division; do not promise a fresh evaluation from `-Check`. Source: `scripts/audit-land-verification-graph.ps1`, `LandVerificationBuildGraph.cs`. |
| Any `Exists` on Markdown escapes. | MSBuild `Exists(...)` on present and absent docs paths returns exit 1, `file-probe FileOrDirectoryExists`. Static `System.IO.File.Exists` bypasses that recorder. | Retain `RecordingFileSystem`; do not replace its missing-input protection with a canary. |
| The canary removes Markdown. | `CanaryRepository` replaces docs bytes, removes README and adds only `docs/c1483-canary/canary.md`. Static Exists approves present docs, arbitrary absent docs, absent README and a metadata-projected docs path. Present README refuses by differential. | Deleting existing docs alone still misses arbitrary absent inputs. |
| Reading bytes necessarily changes the snapshot. | A property receiving docs bytes refuses by differential; the same read in item metadata or an item-definition default approves. A read used only in a false-in-both-passes condition also approves. | Both metadata sinks and equal-result reads need a rule independent of snapshot differences. |
| `%(Filename)` itself is an uncovered file read. | A transform of a safe item's Filename approves. A docs item whose only use is a Filename transform already refuses by the evaluated-item rule. | Pure metadata derived from the item identity is allowed. The demonstrated defect is an unobserved filesystem dependency stored in metadata. |
| Snapshot metadata is complete. | `EvaluationSnapshot.Add` records properties, item type + FullPath and imports, with multiplicity; no custom/default item metadata. | Do not describe the old snapshot as a complete build-state proof. |
| Linux and Windows File.Exists agree. | On Linux SDK 10.0.402, the MSBuild function returned True for slash and backslash docs spellings, False for `DOCS/FLAG.MD`. Direct Linux .NET 9 File.Exists returned False for the backslash spelling: MSBuild normalized it before the call. | Never infer evaluator behavior from a raw runtime probe. Typical Windows case-insensitive lookup can turn the Linux False into True; the new refusal must be syntax-based on both OSes. |
| All S1..S5 are already on rc. | Master contains S5. Fetched rc `5a126f6fc5ae4e16d803b628716b8f0485a37174` contains S1 plus repairs, S2 and S3; its tool lacks S4 and its wrapper lacks S5. | Planned picks remain prerequisites; master publication is not rc activation. |

### Evaluation measurements

Fifteen isolated fixture repos used real SDK projects, a one-project solution,
the repository global.json, disabled default items, and empty local
Directory.Build.props/targets to prevent ancestor imports. The retained evaluator
DLL SHA-256 was `0D364176896DAC3699B2A9590BD07230C1153439C70DADAF22D634C1C2437FAD`.
All eight tool source documents' portable-PDB SHA-256 checks matched this base's
source. This is exploratory evaluator evidence, not a qualified Code checkpoint.

Ten exit-0 results: present/absent docs File.Exists; absent README File.Exists;
metadata-projected File.Exists; custom/default metadata ReadAllText; the equal-result
read condition; pure Filename; backslash File.Exists; case-variant File.Exists.
Five exit-1 results: present README File.Exists; present/absent MSBuild Exists;
ReadAllText into a property; docs item used only by Filename. No exit 2.
The nine unsafe approvals are the red-oracle witnesses; pure Filename is the control.

Reproduction source/results are ignored under `.antiphon/card1528-plan/` in the
Plan checkout. The successful evaluation-only driver took 36 seconds under a
granted build-slot lease; the three `dotnet msbuild -getProperty:PackageTags`
path probes took 9 seconds under another lease. An earlier 25-second attempt
refused ancestor imports in all fixtures and is not defect evidence; the first
binary-copy attempt failed before evaluation. No builds were run. TestDesign
must put the fixture definitions below in durable test source, not depend on
these temporary binaries or ignored records.

## Decisions

**D-1: Add a conservative evaluation-expression gate, not another path evaluator.**
Before `SolutionAudit` evaluates projects, scan repository-owned MSBuild XML using
the same tracked definition census as the pin gate. Inspect evaluation-time
element text and attributes, including Conditions, item metadata, item-definition
defaults, transforms and false branches. Inspect imported definitions too. A
matching OS-element pin does not exempt evaluation-time expressions. Existing
exactly pinned Target bodies retain their separate reviewed trust contract;
the real tree's output-file ReadAllText inside a Target is not a new exemption.

Add `EvaluationExpressionPolicy.cs`; wire it in `SolutionAudit.cs`, sharing the
definition/XML enumeration with `TargetPins.cs` without changing pin identities.
Use MSBuild's unescape API after Locator registration, inspect raw and unescaped
expression text, and account for every property-function call head, including
nested calls. Do not expand arguments, resolve projected paths or guess values.
Unknown/dynamic/malformed function identities or unsupported call chains refuse.
Diagnostics name the definition and element/attribute plus `filesystem-function`
or `unsupported-function`, never file contents.

**D-2: Only an explicit pure-function set is admissible in repository evaluation
expressions.** Deny System.IO.File/Directory/FileInfo/DirectoryInfo and other
filesystem operations regardless of today's path, existence or result. Permit
only enumerated lexical System.IO.Path members (e.g. Combine, GetDirectoryName,
GetFileName, GetExtension, ChangeExtension), the required pure String operations,
and required pure MSBuild arithmetic/path-normalization functions. No type-wide
allowance, reflection, environment/file discovery, or user-configurable bypass.
TestDesign enumerates the exact small member list from the definition census;
Code must not silently widen it when a fixture refuses. Ordinary MSBuild Exists
conditions still go through the recorder and retain its non-skippable allowance.

Rejected: resolving only obvious docs arguments, trusting today's False, adding
one more canary filename, or extending the PowerShell emulator. Each still misses
metadata-projected/absent inputs or recreates the original evaluator problem.
The intentional compatibility tightening is that even a static read of a safe
non-Markdown file now refuses in repository evaluation expressions. Such files
can still be explicit build inputs. This is acceptable for a skip-approval audit;
refusal leaves verification enabled. Preserve pure lexical functions and the
real repository as positive controls so the rule cannot degenerate into deny-all.

**D-3: Refuse metadata-only filesystem reads at their source.** The same gate
visits metadata/default-metadata bodies even when no item identity points at docs
and no property or item list changes. Do not add a separate text-search exception
for metadata. Pure Filename/Extension/RelativeDir/Identity transforms remain legal
when their contributing items pass the existing item/glob rules. File-backed
well-known metadata of a docs item remains refused by those rules even if the
item is never otherwise consumed or is later removed.

Rejected for this small repair: expanding `EvaluationSnapshot` as the authority.
That catches the direct Body example but still approves arbitrary absent inputs
and constant-result reads. Eagerly reading every built-in timestamp also creates
copy-induced differences. The existing canary/snapshot remains defense in depth;
this card makes no claim that its metadata representation becomes complete.

**D-4: Preserve publication and land contracts.** No manifest format/count policy,
land-time evaluator, database migration, new pin allowance or production flag.
Only `-Update` regenerates records using the final staged tool. Any refusal or
exit 2 leaves both old records byte-for-byte intact, or both absent if initially
absent. Update the wrapper header/owner wording to accurately describe `-Check`.
The parent SDK, no-restore and reviewed-Target trust boundaries remain explicit;
this card closes repository evaluation expressions, not arbitrary SDK/task code.

**D-5: Portable work, one final test-project build.** Read
`GET /api/runner-defaults` and `GET /api/session-runners` at dispatch. The Plan
read revision 2 and found an eligible default Linux lane and an eligible Windows
lane. These are observations, not host pins: omit `-Runner` and `-Platform`.
All ordinary rows use the **portable .NET/MSBuild evaluator lane**, no PostgreSQL,
browser or live provider. No Windows session or qualification is touched.

## Slices

| Slice / authoring budget | Files and concrete outcome | Verification boundary |
|---|---|---|
| S1 / 30-45 min | New partial `tests/Antiphon.Tests/Application/LandGraphEvaluationFilesystemTests.cs`; durable fixture source in that partial or a shared test helper. Add the V methods below. Commit and push tests before the fix. | Run the fixture bodies against a source-verified base evaluator with an exit-1/no-approved oracle. The named defects must fail that oracle by returning 0, before changing production. No test-project build yet. Preserve the independent real-property/item observations, not a missing-tool/build failure. |
| S2 / 45-60 min | `tools/Antiphon.LandGraphAudit/EvaluationExpressionPolicy.cs`, `SolutionAudit.cs`, narrowly shared census/visitor in `TargetPins.cs`. Adjust `LandGraphEvaluationProbeTests.cs` for the deliberately earlier static refusal and replace its static safe-read allowance with explicit pure-function/MSBuild-Exists controls. | Commit and push. Keep existing assertions for recorder probes, item/glob/import refusal and pins. A changed diagnostic is recorded as policy tightening, not relaxed to any nonzero exit. |
| S3 / 30-45 min | Narrow `scripts/audit-land-verification-graph.ps1` header and `docs/orchestration-loop.md` wording; script publication assertions in `LandGraphEvaluationScriptTests.cs`; generate the two `LandVerificationBuildGraph.*.txt` records through -Update only. | Commit source before Update; commit generated records before final checkpoints. Run all rows once using one shared Antiphon.Tests build at the frozen S1-S3 tip. Preserve exact SHA, counts and receipts; hand off Review. |

To keep the single test-project build, the red proof uses the already-built base
CLI and the **same durable fixture definitions** that the new methods consume.
TestDesign must supply a small evaluation-only fixture/oracle driver with that
shared input, not a second independent reproduction. A missing compatible binary
permits one declared, slot-gated **tool-only** base build in an isolated detached
checkout; it does not permit a second Antiphon.Tests build or unverified old DLL.
The post-fix test-project build is the only ordinary compiled test build. Do not
claim the pre-fix oracle runs were TUnit executions. Tool-only bootstrap/Update
operations are declared separately from the closed test selection below.

No session waiting for input is added or changed. No release timer is involved.

## Verification design

All proposed methods below belong to the existing partial
`LandGraphEvaluationTests`; reuse its AuditRepo, staged DLL, process limiter,
timeouts and source-untouched assertions. New method names are planned, not
already implemented. Minimal fixture shell: SDK Microsoft.NET.Sdk, net9.0,
EnableDefaultItems=false; App/App.csproj in a one-project solution; local empty
Directory.Build.props/targets; flag bytes `secret-flag`; safe item `sentinel.txt`.
Use no Target or OS pin in new negative fixtures: those guards would mask the bug.

| ID / method | Fixture and exact oracle | Base expectation |
|---|---|---|
| V-1 `C1528_StaticExistsRefuses` / 5 arguments | PackageTags receives `$([System.IO.File]::Exists('$(MSBuildThisFileDirectory)../<path>'))`: present docs/flag.md, missing docs/missing.md, missing README.md, backslash docs spelling, case-variant DOCS/FLAG.MD. Exit 1, filesystem-function, owner, no approved. Missing paths stay absent throughout baseline and canary. | All five RED: current tool returns 0. |
| V-2 `C1528_ProjectedExistsRefuses` / 1 | Probe Include=sentinel, Rel=`$(MSBuildThisFileDirectory)../docs/flag.md`; PackageTags=`$([System.IO.File]::Exists(@(Probe->'%(Rel)')))`. Require the same refusal despite non-doc item identity. | RED: 0. |
| V-3 `C1528_MetadataReadRefuses` / 2 arguments | ReadAllText of docs/flag.md stored only in Probe.Body; second case stores Body in ItemDefinitionGroup then creates Probe Include=sentinel. No property/item depends on Body. Require filesystem-function with metadata owner. | Both RED: 0, because snapshots omit metadata. |
| V-4 `C1528_EqualResultReadRefuses` / 1 | PropertyGroup Condition uses docs ReadAllText `.Contains('never-in-either')`, false for both real and canary bytes. Require filesystem-function. | RED: 0 despite a real read. |
| V-5 `C1528_FunctionSyntaxAndAbsence` / 1 method | Internal matrix: XML/percent escape and whitespace spellings; nested call; false branch with missing file; a projected absent path; unknown/dynamic receiver/member and unsupported chain; imported props owner. Require named policy refusal, not evaluation failure. Include an absent docs directory and an absent README. | Additional safety cases; TestDesign measures exact base verdicts. Do not count an existing syntax/load refusal as new RED. |
| V-6 `C1528_PureMetadataAndFunctionsApprove` / 1 method | Safe Filename/Extension/RelativeDir/Identity transforms, pure allowed Path/String/MSBuild functions, empty item list, literal text containing System.IO.File, and ordinary Exists of non-skippable safe.txt. Require exit 0 and approved. A docs item used only by Filename, also with later Remove, must still refuse by item rule. | Existing allowances/refusals are controls, not new RED. |
| V-7 `C1528_UpdateRefusalPreservesRecords` / 1 method | Run real wrapper -Update with V-1/V-2/V-3 fixture variants and allowed pre-check property/item names. Run each with both records absent, then with valid prior records. Require exit 1 and byte-preservation/absence of both. Establish the pre-check result separately; never label an upstream pre-check refusal a new evaluator RED. | Integration regression; no unmeasured red claim. |

V-1..V-4 are nine assertion-level red witnesses measured above. A fresh base run
must reproduce exit 0 with clean fixture/SDK setup. S1's oracle expects refusal
and must therefore fail; zero cases, build errors, missing binary, ancestor import
or SDK failures are invalid red evidence. The green oracle remains exit **1** with
the intended diagnostic, not merely nonzero. Store fixture names and counts even
where one TUnit method loops over several cases.

| Regression ID | Existing exact method / purpose |
|---|---|
| R-1 | `C1483_S4_ExistsProbeOfDocsRefuses` (5): recording remains decisive for present/absent docs, directory, README and alias, alongside the safe JSON glob. |
| R-2 | `C1483_S4_FileReadingPropertyFunctionRefuses` (4): same dangerous reads now refuse at the earlier expression gate; explicit lexical/recorded-safe controls replace the old static-file-read allowance. |
| R-3 | `C1483_S1_HarmlessFixtureApproves`, `C1483_S4_RealRepositoryApproves`: do not reject everything or lose existing pinned targets/SDK compatibility. |
| R-4 | `C1483_S5_EvaluationFailurePreservesRecords`, `C1483_S5_CheckRequiresEvaluationRecord`, `LandNonCodeSkipTests.C1385_RegistryMatchesNarrowGraph`: publication on error, provenance, real manifest. |

### Independently bypassable positive controls

SourceLanding Mutation owns these later, each method-scoped and separately
restored. Ordinary Code does not execute mutants. A surviving mutant is a gap.

| PC | Single bypass and exact witness | Why another check cannot mask it |
|---|---|---|
| PC-1 | Admit File.Exists in the new function policy; V-1. | Present docs and arbitrary absent paths return the same values in the current canary; recorder does not see the static call. |
| PC-2 | Skip metadata/default-metadata expression traversal; V-3. | No unsafe evaluated item, property difference or metadata snapshot exists. Test both sink cases. |
| PC-3 | Inspect only currently active expressions; V-5's false branch. | Missing input and false branch produce no runtime probe or differential. |
| PC-4 | Ignore unsupported/nested call identities; the independently named V-5 subcases. | Fixtures must otherwise pass all old checks; TestDesign must replace any subcase caught by another guard. |
| PC-5 | Publish either generated record before evaluator success; V-7. | Assert the two record bytes separately; a final exit 1 alone does not pass. Existing S5 controls remain pending, not re-proven here. |

### Cost

Closed ordinary floor: **15 minutes**, including one isolated Antiphon.Tests build;
authoring is 105-150 minutes across S1-S3. The pre-fix fixture oracle is bounded
to two minutes of evaluation excluding slot wait. Declare separately one small
tool-only build if needed for base provenance, one final tool-only build for
pre-checkpoint -Update, and checkpoint-tool bootstrap if unavailable. All take
the host gate. No full class/assembly/Unit/namespace run, repeat-proof or native
Windows qualification is requested. After a real fix rerun only affected rows.

Use the checkpoint tool `run --plan <this-plan> --after S1-S3
--expected-source-sha <full-sha>` and own/wait the run until exit is not 75.
TestDesign must freeze the fixture driver and exact expanded roster before Code.
Validate per-row receipts, preserve CHECKPOINT lines, run the evidence-diff guard,
and remove only owned alternate output directories. Unexpected base failures get
their exact method rerun at base, never a whole suite.

### Checkpoints

Every row uses the **portable .NET/MSBuild evaluator lane**, effective runner
defaults, the same frozen S1-S3 source and **one shared test-project build**.
Counts are TUnit results; internal matrices do not increase Min.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c1528/` | filesystem | `/*/*/LandGraphEvaluationTests/(C1528_StaticExistsRefuses)\|(C1528_ProjectedExistsRefuses)\|(C1528_MetadataReadRefuses)\|(C1528_EqualResultReadRefuses)` | V-1..V-4 | exactly 9 passed, 0 failed/skipped | 9 | 10 |
| CP-2 | S1-S3 | CP-1 | syntax-controls | `/*/*/LandGraphEvaluationTests/(C1528_FunctionSyntaxAndAbsence)\|(C1528_PureMetadataAndFunctionsApprove)` | V-5 V-6 | exactly 2 passed, 0 failed/skipped | 2 | 1 |
| CP-3 | S1-S3 | CP-1 | recorded-probes | `/*/*/LandGraphEvaluationTests/(C1483_S4_ExistsProbeOfDocsRefuses)\|(C1483_S4_FileReadingPropertyFunctionRefuses)` | R-1 R-2 | exactly 9 passed, 0 failed/skipped | 9 | 1 |
| CP-4 | S1-S3 | CP-1 | real-approval | `/*/*/LandGraphEvaluationTests/(C1483_S1_HarmlessFixtureApproves)\|(C1483_S4_RealRepositoryApproves)` | R-3 | exactly 2 passed, 0 failed/skipped | 2 | 1 |
| CP-5 | S1-S3 | CP-1 | publication | `/*/*/LandGraphEvaluationTests/(C1528_UpdateRefusalPreservesRecords)\|(C1483_S5_EvaluationFailurePreservesRecords)\|(C1483_S5_CheckRequiresEvaluationRecord)` | V-7 R-4 | exactly 3 passed, 0 failed/skipped | 3 | 1 |
| CP-6 | S1-S3 | CP-1 | manifest | `/*/*/LandNonCodeSkipTests/C1385_RegistryMatchesNarrowGraph` | R-4 | exactly 1 passed, 0 failed/skipped | 1 | 1 |

## Rollout and activation

Commit source/pins first. Build the final tool through the host slot gate into an
owned alternate output, then run
`pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Update -ToolPath <built-dll>`
through the gate. Never edit digest, count, SDK or pins lines by hand. Commit the
generated records, then run the shared final test build/checkpoints. `-Check`
must pass for that environment; it validates the record, not a second evaluation.

At this base master has digest
`84E2796292E985EEECCB2381FFBE133A81376B81F7A042F4D7BAEEE9A2C517D3`, count **58**;
rc has `637CFA8577440D255076CBF5CBA40BECA0C3BC8F738705A5536A35182DAD9C3E`, count **58**.
C# source changes alone do not change this definition-only digest. Do not add
fixture .csproj files to the tracked graph: keep project text in test fixtures.
If a project definition/census changes, accept the newly calculated digest/count
only after -Update approves; never assume the historical 58 or copy master's
record onto rc. Even unchanged bytes need a fresh successful Update with the
fixed evaluator: -Check cannot distinguish an old policy's same-digest approval.
No evaluator provenance format expansion is included in this card.

Dependency order for the planned rc picks is **CARD-1483 S1 + both repairs -> S2
-> S3 -> S4 (all fixes) -> S5 (including record-replacement fix) -> CARD-1528**.
S1-S3 are present at the observed rc tip; S4/S5 remain planned, not activated.
Useful source boundaries are S3 `eb6101b1a0eb8b7542cd631f8064c6ae0671733c`,
S4 final code `fd08ab50057b179d5d98eecc8f533e8ed474dfed`, and S5
`bd215d7b4f559db69fbf4d7363417fce0f2c2467`. The port owner selects the actual
slice commits, including their tests/repairs, rather than treating these boundary
SHAs as one-commit patches. Re-fetch and verify rc content before porting, then
regenerate records on that final rc tree and qualify its narrow checkpoint rows.

The syntax gate must refuse case variants and slash/backslash spellings on either
OS without depending on File.Exists returning True. Linux proof of that early
refusal removes a native-Windows dependency from this repair; it does not claim
Windows evaluator measurements. Existing SDK/OS pins retain their trust boundary.
Product hosts use the reviewed rc tip; publication alone does not activate a new
embedded manifest. Activation belongs to the separately owned rc rollout/version
check after its qualification. This task does not restart a host or touch the
live Windows qualification. Rollback must not re-authorize new graphs with the
old permissive evaluator; retain verification when approval is unavailable.

--- next stage ---
next: test-design
handoff: Freeze CARD-1528's pure-function allowlist and shared fixture/oracle driver, prove V-1..V-4 red on the base evaluator, and finalize the 26-result closed roster with one final test-project build. Preserve the absent-input policy, metadata traversal PCs and S4->S5->1528 rc dependency.
artifact: docs/superpowers/plans/2026-10-23-card-1528-land-audit-filesystem-metadata-plan.md
