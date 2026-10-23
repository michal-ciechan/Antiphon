# CARD-1540: refuse environment- and timestamp-gated graphs in land approval

Plan task `6847dfb7`, 2026-10-23. Base `523a8098b` (contains CARD-1528 recut `77eb7806d`).
Inputs: card revision 2; the investigation
[2026-10-23-card-1540-timestamp-path-land-audit.md](../../investigations/2026-10-23-card-1540-timestamp-path-land-audit.md)
(Investigate `5bc616ca`, `72579b68d`, now on master as `0f9ff8281`); the
[CARD-1528 plan](2026-10-23-card-1528-land-audit-filesystem-metadata-plan.md) for shape and
dependency order. This is a docs-only deliverable. No build, test run, desktop access or
qualification occurred. The only commands run were a lexical census script and two runner API reads.

Invariant (unchanged from CARD-1483/1528): a docs-only land must never skip verification on the
strength of an approved graph that could read docs in a different environment. The audit
evaluates once, in the author's environment. The land evaluates nothing and compares only
definition-file text (`LandVerificationPolicy.cs:71-95`). So any dependence on environment or time
has to be refused **lexically**, independently of the evaluated sample.

## Ground truth

| Card/brief assumption | What the code does at base | Consequence |
|---|---|---|
| The real tree reads five environment-overridable names. | A census of plain `$(Name)` reads in non-Target evaluation text of the 54 tracked XML definitions finds 9 names. `MSBuildThisFileDirectory` is reserved. The five knobs are confirmed. **Three more names can be overridden by the environment:** `DefaultItemExcludes` (`Directory.Build.props:15`, `scripts/fixtures/nightly/c1375-probe/Probe.csproj:8`, self-append), `SourceRevisionId` (`Directory.Build.props:24`, self-default) and `WarningsAsErrors` (`src/Messaging.Pack.props:16`, self-append). | The admitted reviewed set has 8 environment names, not 5 (D-2). None reaches docs today. |
| Admit "the audit's own globals" (`Configuration`, `Platform`, `TargetFramework`, `Solution*`, `ArtifactsPath`). | The tree reads none of them in evaluation text. `TargetFramework` is global only in inner builds. In `Directory.Build.props` and in outer evaluations it is undefined, so the environment can supply it. | Admit no globals (D-2). A future need adds a name and its fixture. |
| R1 leaves P7 (directory `%(ModifiedTime)`) approved. | R1 as specified is syntactic, so P7 will refuse. | P7 moves from "control" to a refusal case. The empty-directory-timestamp fact stops mattering, and so does its unchecked Windows behaviour (D-5). |
| Which cwd does the land's solution build evaluate with? | `AgentTaskLandingProtocol.cs:306-313` decides. When it does not skip, `AgentTaskLandService.VerifyUnderSlotAsync` (`:1883-1886`) runs `dotnet build --artifacts-path <tmp> --configuration Debug [-maxcpucount:N]` with **no project argument**. `RunProcessAsync` (`:1916`) sets `WorkingDirectory` to the verified land worktree root, so the solution is the one in the root. The environment is the **server process environment** plus `BuildDriverEnvironment.Apply` (MSBuild-server/node-reuse switches). It is not the auditor's environment. The audit wrapper also starts the tool at repo root (`audit-land-verification-graph.ps1:33`) and strips only `MSBuild*` variables. | Process cwd is the same for both: worktree/repo root. Two things are MSBuild-internal and **UNCHECKED**: which cwd each project evaluation uses inside the build (in-proc or worker node), and the land host's variables. R1 refuses timestamp metadata whatever the cwd, so correctness does not depend on either. Nothing to measure (D-5). |
| The tree uses no timestamp metadata. | Confirmed: no `ModifiedTime`, `CreatedTime` or `AccessedTime` in any tracked definition. The only metadata item function is `WithMetadataValue('Identity', …)` inside a Target (`tests/Shared/CheckpointRepeat.targets:8`). | R1 has no real-tree impact. |
| The manifest changes only if R3(a) is chosen. | `pins.txt` is not a definition (`LandVerificationBuildGraph.cs:131-137`), so it is not in the manifest digest. `evaluation.txt` records the pins blob SHA (`audit-land-verification-graph.ps1:107-117`). | With D-3, manifest `84E2796292E985EEECCB2381FFBE133A81376B81F7A042F4D7BAEEE9A2C517D3` / 58 stays **byte-identical**. The `evaluation.txt` pins-SHA line changes through `-Update` only. |
| Existing test fixtures read only admitted names. | Fixture texts in `LandGraph*Tests.cs` and `land-graph-c1528.json` read `MSBuildThisFileDirectory` (36), `MSBuildProjectDirectory` (7), `PackageTags` (3), `UsePublishedPackages` (2), `OS`, `X`, `Receiver`, `Member`, `ProbeValue` and `AbsentProperty`. | Strict R2 adds an `environment-property` refusal to some existing fixtures. That can flip approve fixtures and **mask CARD-1528 positive controls** (PC-4). TestDesign must classify each one (D-6). |

Platform read at plan time: `GET /api/runner-defaults` revision 2 (global default, all kinds) and
`GET /api/session-runners` (an eligible Linux default lane and an eligible Windows lane). These
are observations, not pins. Omit `-Runner` and `-Platform` for every Code row.

## Decisions

**D-1 (R1) `timestamp-metadata`: refuse syntactically, independent of cwd.**
In raw and unescaped repository evaluation text (attributes, text and CDATA, metadata,
item-definition defaults, false branches; Target bodies excluded as today), refuse:
(i) `%(` [ws] [`ItemType` [ws] `.` [ws]] `ModifiedTime|CreatedTime|AccessedTime` [ws] `)`, case-insensitive;
(ii) a metadata-name argument of an item function (`Metadata`, `WithMetadataValue`,
`WithoutMetadataValue`, `HasMetadata`, `AnyHaveMetadataValue`) that is a timestamp name **or is
not a plain quoted literal identifier** (for example `'$(N)'`), which fails closed;
(iii) an unclosed `%(`, which fails closed as `timestamp-metadata`.
Rejected: refusing the bare word anywhere. It still misses `Modified$(T)` and adds false refusals.
Also rejected: making the tool's cwd match the land. The land's per-project cwd is MSBuild-internal
and unchecked, and P8/P12 show the two can disagree.

**D-2 (R2) `environment-property`: strict, census-minimal name set.**
A plain `$(Name)` (trimmed, compared with `OrdinalIgnoreCase` because MSBuild property names are
case-insensitive) in repository evaluation text is admitted only from one explicit set, kept in
`EvaluationExpressionPolicy.cs`:
- reserved (the environment cannot override them): `MSBuildThisFileDirectory`, `MSBuildProjectDirectory`, `OS`;
- reviewed environment knobs (these need R3 pins): `UsePublishedPackages`, `AntiphonMessagingVersion`,
  `EnablePublicApiAnalyzers`, `AntiphonCleanFileMaxBytes`, `PkgMicrosoft_Windows_Console_ConPTY`,
  `DefaultItemExcludes`, `SourceRevisionId`, `WarningsAsErrors`.

Every other name refuses, including `PATH`, an undefined name and a repository-defined helper
property. A name that a repository property defines is **not** environment-free: MSBuild seeds every
property from a same-named environment variable, and the self-default/self-append patterns read it.
"Defined somewhere" would need ordering analysis across imports. Rejected: a prefix rule such as
`MSBuild*` (`MSBuildExtensionsPath` and `MSBuildSDKsPath` can be overridden from the environment),
admitting globals (see ground truth), and a test-only extension hook. Refusal only loses the skip,
so a later need adds one name with a matched pair of fixtures in a reviewed change.

**D-3 (R3) DECISION: option (b), the knob set as a reviewed trust boundary, enforced by exact
pins rather than a set-pinning test.** Option (a) is rejected.
`TargetPins` already requires an exact reviewed pin on every element whose Condition depends on
the OS (`TargetPins.cs:30,124-127`; CARD-1483 D-5: "evaluation cannot inspect … the other OS
branch"). Widen that predicate. A non-Target element whose **own** attributes or direct text read
any reviewed environment knob from D-2 must also carry an exact pin. The existing
`(missing pin)`, `(changed pin)` and `(stale pin)` diagnostics apply unchanged. Elements inside a
Target are already covered by the Target's pin.

*Integrity argument.* After D-2, repository evaluation text can read only 3 reserved names and the
8 knobs. A reserved name is environment-proof by MSBuild. Every site that reads a knob is pinned to
exact bytes, and that includes the elements that derive values from it: the derived names (the knob
itself, `WarningsAsErrors`, `DefaultItemExcludes`) are themselves knobs. So no unpinned element can
read an environment-derived value, and any new reading site or change to a pinned body refuses
until a reviewer adds or replaces a pin. That puts a knob-gated branch under exactly the reviewed
contract the other OS branch already has. The V-5 shape (`'$(UsePublishedPackages)'=='true'` on a
docs item) refuses as `missing pin` with no further rule. Timestamps are closed by D-1. What remains
outside the audit is unchanged and explicit: SDK-internal environment reads (CARD-1528 D-4) and
bodies a reviewer pinned.

*Rejected (a)*, a lexical "docs-literal Include/Update in any branch" rule. It cannot be complete.
It misses indirection through a property set in a gated PropertyGroup, string assembly
(`../do$(X)s/a.md`), globs (`../**/*.md`), `Exists('../docs/a.md')` in a gated Condition (adding a
docs file is itself a docs-only diff), and gated imports. Making it complete means refusing almost
every gated item spec, which is deny-all in practice and still gives false assurance.
*Rejected plain (b)*, a test that pins only the set of names. Any new element could then use an
admitted knob to gate docs with no mechanical signal, which leaves the whole defect to Review. The
audit exists to catch it independently of Review.

*Cost.* By census, about 15 new pins in `LandVerificationBuildGraph.pins.txt`: `Directory.Build.props`
2, `Directory.Build.targets` 1, `EchoGateway.csproj` 5, `Messaging.Pack.props` 3,
`Antiphon.Agents.Pty.csproj` 3, `c1375-probe/Probe.csproj` 1. The authoritative list is the tool's
own `(missing pin)` output after S2. Review checks each pinned body for docs reach in **every**
branch. Pins are hand-reviewed and not generated by `-Update`.

**D-4: Manifest and publication.** No definition file changes, so manifest digest and count stay
`84E2…C517D3` / 58. If `-Update` produces any other value, stop and report it; do not accept it. The
pins change rewrites the `evaluation.txt` pins-SHA line, and only through `-Update` with the final
staged tool, after pins and source are committed. No land-time evaluator and no runtime or
database change: **zero runtime at land**. Activation still waits for the rc rollout.

**D-5: Windows and cwd.** D-1 is syntactic, so the unchecked Windows behaviour (empty directory
timestamps, cwd resolution of relative timestamp metadata) and the land's internal evaluation cwd
cannot change a verdict. They are recorded as **OWED, informational** to the desktop lane (CP-W)
and do not block Code or Review. D-2 is case-insensitive, which covers Windows' case-insensitive
variable names.

**D-6: Existing fixtures keep their meaning.** TestDesign censuses every existing land-graph fixture
for names outside the D-2 set. If a refusal fixture would now also refuse as `environment-property`,
rewrite it to read an admitted name (usually `MSBuildThisFileDirectory`) so each CARD-1528/1483
positive control still has a single cause. An approve fixture that changes verdict is rewritten the
same way. Never widen the set to make a fixture pass, and list every rewritten fixture.

**D-7: Small, portable, one build.** Every row runs on the portable .NET/MSBuild evaluator lane with
effective runner defaults: no PostgreSQL, browser or provider. One shared `Antiphon.Tests` build.
No session that waits for input is added or changed, and no release timer is involved.

## Code execution corrections (task 6c903205)

The assigned base is c51023b99b3fcccd222ddbf8137de8c203e04760. Its current manifest is
60C8870DF97C1FA3231BA95C442C2CFD7B3E870A379D8A1501F7D4A6C1C6491A / 59,
which supersedes the historical digest/count above. No definition is changed by this task;
-Update must preserve these base manifest bytes.

A gated native MSBuild probe with OS=../docs/a.md returned that value: OS is environment
overridable. Correct D-2/D-3 to TWO reserved names and NINE pinned environment knobs,
including OS. V-6 now requires both pinned approval and unpinned refusal for OS; the two
reserved-directory controls feed an evaluated item, proving environment overrides cannot
redirect those items into docs. V-7 covers all eleven names as before.

PATH is also an executable-search prerequisite: removing it caused the base tool to fail
launching git (exit 2), before evaluation. V-3 therefore uses an inherited usable PATH;
V-1/V-2 still exercise actual absent/empty/set child variables. The first PATH probe's
exit 2 is infrastructure failure, excluded from red evidence; its corrected isolated probe
approved at base. All ten required base witnesses approved (exit 0): gate/include/path,
modified/created/accessed/stamp-gate/item-function/directory and unpinned.

CP-4 has six results, since the OS method has three arguments. The corrected closed floor
is 45. Historical counts and costs above remain provenance, not claimed execution.
D-6 rewrites Receiver, Member and AbsentProperty in c1528 fixtures to the reserved
MSBuildThisFileDirectory; their original unsupported-function/approval oracles remain.
Probe tests replace helper-property item reads with safe literal items or the same Exists
condition. The imported-directory publication fixture now forwards a ProbeInput item,
retaining its MSBuildProjectDirectory docs dependency without a new property refusal.

The two function arguments of R-2 were already red at the assigned base: with an exact
pin, IsOSPlatform and RuntimeInformation.OSDescription both refuse unsupported-function.
Base-tool oracle evidence confirms that inherited mismatch. Correct the test expectations to
retain that named refusal; an exact pin is not an evaluation-expression exemption.

## Performance budget

The closed selection remains CP-1..CP-7, estimated 26 minutes including one shared
Antiphon.Tests build; there is no full assembly or Unit lane (the task brief selects narrow
ordinary verification). Scope/category: git operations, Integration; Antiphon.Tests references
and stages Antiphon.LandGraphAudit, while production landing/runtime binaries are unchanged.
No database, container, web host, browser, migration or seed setup is used. Each fresh tiny Git
repository is the subject of the evaluator test, not repeated application infrastructure.

The initial ten-fixture base oracle took 34s execution plus 16s admission; the isolated corrected
PATH witness took 7s plus 0s admission. The two inherited OS-function probes took 2s plus 0s.
Samples are provisional, not measured p95 (CARD-1540); collect final TRX per-test times once.
New individual tests use 60s deadlines and the syntax matrix 120s; inherited child/tool bounds
are unchanged. Checkpoint defaults remain historical tool policy, not measured p95; use a
finite 35-minute run deadline. No selection is expected over 30 minutes.

Retain .antiphon/c1540 and checkpoint run directories with slot waits, exact groups, source
SHA and TRX durations. LAND_GRAPH_EVALUATION records each external evaluator process wall
span/exit. Internal evaluator filesystem/evaluation and Git preparation breakdowns are unknown;
unattributed time is remainder, not zero. No latency/p95 claim is made from these small samples.

## Slices

| Slice / budget | Files and outcome | Boundary |
|---|---|---|
| S1 / 30-45 min | New partial `tests/Antiphon.Tests/Application/LandGraphEvaluationEnvironmentTests.cs` (class `LandGraphEvaluationTests`); durable input `tests/Antiphon.Tests/Application/Fixtures/land-graph-c1540.json`; the existing `scripts/test-land-graph-c1528-fixtures.ps1` gains a `-Fixtures` path, defaulting to the c1528 input. | Commit and push the tests before the fix. Prove V-1..V-5 red on the source-verified base tool through the driver (exit 0 = approved = red). This is not a TUnit build. |
| S2 / 45-60 min | `tools/Antiphon.LandGraphAudit/EvaluationExpressionPolicy.cs` (D-1 and D-2, plus the knob set exposed to pins); `TargetPins.cs` (D-3 predicate); D-6 fixture rewrites in `LandGraph*Tests.cs` / `land-graph-c1528.json`. | Commit and push. Existing assertions keep their exact refusal class. |
| S3 / 30-45 min | Add the reviewed pins to `server/Application/Services/LandVerificationBuildGraph.pins.txt`; update the wrapper header and the audit wording in `docs/orchestration-loop.md` (environment/timestamp classes, pinned knob sites); `-Update`, then `evaluation.txt` changes and `manifest.txt` must stay unchanged. | Commit pins and source before `-Update`, and commit the records before the checkpoint run. Run CP-1..CP-7 once at the frozen S1-S3 tip. |

## Verification design

All new methods go in partial `LandGraphEvaluationTests`. They reuse AuditRepo, the staged DLL, the
process limiter and timeouts, and set each child's environment explicitly per run. The fixture
shell is the CARD-1528 one (net9.0, `EnableDefaultItems=false`, one-project solution, local empty
Directory.Build files, `docs/a.md`). The tool must start with cwd = fixture repo root, as the
wrapper does. If AuditRepo uses another cwd, P8 is not valid red (it refuses at cwd `app/`), and
TestDesign must say so. Negative fixtures carry no Target and no OS pin.

**Absent input (CARD-1525/1526).** "Absent / empty / set" means the real child-process environment
variable is removed, set to `""`, or set to a value that takes the docs branch. It is not a mocked
property. The verdict must be identical in all three states.

| ID / method | Fixture and oracle | Base expectation |
|---|---|---|
| V-1..V-3 `C1540_EnvironmentPropertyRefuses` / 3 args | `gate` (P2: `'$(C1540_GATE)'=='1'` on docs item), `include` (P11: `Include="$(C1540_DOCS)"`), `path` (P4: `<ToolPathCopy>$(PATH)</ToolPathCopy>`). Each runs absent/empty/set. Oracle: exit 1, `environment-property` naming owner, no `approved`, in all 3 states. | RED: approved when absent (P2, P11, P4 measured). In the set state P2/P11 already refuse as docs items, so the base red is the absent row. |
| V-4 `C1540_TimestampMetadataRefuses` / 6 args | `modified`, `created`, `accessed` (P6 on `Program.cs`), `stamp-gate` (P8, file touched to 2001), `item-function` (`@(Stamp->Metadata('ModifiedTime'))`), `directory` (P7). Oracle: exit 1, `timestamp-metadata`. | RED: approved (P6, P7, P8 measured at repo cwd). `item-function` is unmeasured: TestDesign measures it and does not count it as red until it does. |
| V-5 `C1540_KnobGateNeedsPin` / 2 args | `unpinned`: P1 with `Condition="'$(UsePublishedPackages)'=='true'"`, knob absent, expect `(missing pin)`. `changed`: a pinned knob ItemGroup with a docs item added inside, expect `(changed pin)`. | `unpinned`: RED expected (approved by reasoning, not measured). TestDesign must measure it. `changed`: not red at base (no knob pins exist); it is an integration case. |
| V-6 `C1540_AdmittedNamesApprove` / 11 args | One case per D-2 name. A knob is read in a pinned element (fixture pins file) with the variable set: approved. The same element unpinned: `(missing pin)`. A reserved name is read unpinned, with a same-named variable set to `../docs/a.md`: approved and no docs item. | Positive controls. They stop D-2/D-3 from becoming deny-all and pin the reserved claim on the real evaluator. |
| V-7 `C1540_AdmittedVerdictIgnoresEnvironment` / 1 | One fixture with all 11 admitted reads (knob sites pinned), run absent/empty/set for every knob: approved ×3. | Absent-input control. |
| V-8 `C1540_ExpressionSyntaxFailsClosed` / 1 method | Named internal matrix: `$( PATH )`, `$(path)`, `%24(PATH)`, `$(UsePublishedPackages )` unpinned; `%( ModifiedTime )`, `%(Stamp.ModifiedTime)`, `%25(ModifiedTime)` (escaped), unclosed `%(ModifiedTime`, `Metadata('$(N)')`, `WithMetadataValue('CreatedTime','x')`; item-definition default; false-branch use; imported `.props` owner. Each expects its named class. Controls: `%(Filename)` and a timestamp read inside a pinned Target still approve. | Measure per subcase. A subcase that base already refuses for another reason is not red. |

V-1..V-5 are the red witnesses: 3 + 6 + 1 cases. An exit of 0 at base is red. A build, SDK,
ancestor-import or zero-case failure is not red evidence. The green oracle is exit **1** with the
named class, not merely nonzero.

| R-id | Existing method / purpose |
|---|---|
| R-1 | `C1483_S4_RealRepositoryApproves`, `C1483_S1_HarmlessFixtureApproves`: the real tree, with the new pins, is still approved. |
| R-2 | `C1483_S3_OsConditionedElementNeedsPin`, `C1483_S3_PinnedTargetMatchesOnlyItsHash`: the OS pin rule is unchanged. |
| R-3 | `C1528_*` (6 methods, 11 results): filesystem refusals keep their class after D-6 rewrites. |
| R-4 | `C1483_S5_UpdateRefusesWhenEvaluationRefuses`, `C1483_S5_CheckRequiresEvaluationRecord`, `C1528_UpdateRefusalPreservesRecords`: publication on refusal is independent of the refusal class. |
| R-5 | `LandNonCodeSkipTests.C1385_RegistryMatchesNarrowGraph`: the embedded manifest is unchanged (D-4). |

Positive controls for SourceLanding Mutation (pending, method-scoped, each restored separately):
PC-1 admit every plain name (V-1..V-3); PC-2 skip `%(…)` timestamp detection (V-4 `modified`);
PC-3 skip item-function name arguments (V-4 `item-function`); PC-4 drop the D-3 knob predicate
(V-5 `unpinned`); PC-5 compare names case-sensitively (V-8 `$(path)`); PC-6 inspect only the
unescaped spelling or only the raw one (V-8 `%24(PATH)`/`%25(ModifiedTime)`). Each witness fixture
must pass every other guard. That is why D-6 matters.

### Checkpoints

Every row: portable .NET/MSBuild evaluator lane, effective runner defaults, frozen S1-S3 tip,
**one shared test-project build**. Counts are TUnit results; internal matrices do not add to Min.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c1540/` | env-time | `/*/*/LandGraphEvaluationTests/(C1540_EnvironmentPropertyRefuses*)\|(C1540_TimestampMetadataRefuses*)` | V-1..V-4 | exactly 9 passed, 0 failed/skipped | 9 | 12 |
| CP-2 | S1-S3 | CP-1 | knobs | `/*/*/LandGraphEvaluationTests/(C1540_KnobGateNeedsPin*)\|(C1540_AdmittedNamesApprove*)\|(C1540_AdmittedVerdictIgnoresEnvironment*)` | V-5..V-7 | exactly 14 passed, 0 failed/skipped | 14 | 4 |
| CP-3 | S1-S3 | CP-1 | syntax | `/*/*/LandGraphEvaluationTests/(C1540_ExpressionSyntaxFailsClosed*)` | V-8 | exactly 1 passed, 0 failed/skipped | 1 | 2 |
| CP-4 | S1-S3 | CP-1 | approval-pins | `/*/*/LandGraphEvaluationTests/(C1483_S4_RealRepositoryApproves*)\|(C1483_S1_HarmlessFixtureApproves*)\|(C1483_S3_OsConditionedElementNeedsPin*)\|(C1483_S3_PinnedTargetMatchesOnlyItsHash*)` | R-1 R-2 | exactly 6 passed, 0 failed/skipped | 6 | 3 |
| CP-5 | S1-S3 | CP-1 | c1528 | `/*/*/LandGraphEvaluationTests/(C1528_StaticExistsRefuses*)\|(C1528_ProjectedExistsRefuses*)\|(C1528_MetadataReadRefuses*)\|(C1528_EqualResultReadRefuses*)\|(C1528_FunctionSyntaxAndAbsence*)\|(C1528_PureMetadataAndFunctionsApprove*)` | R-3 | exactly 11 passed, 0 failed/skipped | 11 | 2 |
| CP-6 | S1-S3 | CP-1 | publication | `/*/*/LandGraphEvaluationTests/(C1483_S5_UpdateRefusesWhenEvaluationRefuses*)\|(C1483_S5_CheckRequiresEvaluationRecord*)\|(C1528_UpdateRefusalPreservesRecords*)` | R-4 | exactly 3 passed, 0 failed/skipped | 3 | 2 |
| CP-7 | S1-S3 | CP-1 | manifest | `/*/*/LandNonCodeSkipTests/(C1385_RegistryMatchesNarrowGraph*)` | R-5 | exactly 1 passed, 0 failed/skipped | 1 | 1 |

Closed floor: 45 results, about 26 minutes including the one build. Declared separately, all
gated: one tool-only base build for S1 red if no verified base DLL exists, and one final tool-only
build for `-Update`. Run `dotnet run --project tools/Antiphon.Checkpoints -- run --plan <this plan>
--after S1-S3 --expected-source-sha <sha>` and wait until the exit is not 75. Run
`check-evidence-diff.ps1 -Repository . -BaseRef <base> -HeadRef HEAD`. After a fix, rerun only the
affected rows.

**CP-W (OWED to the desktop lane, informational, not in Code's closed list):** run CP-1 and CP-3 at
the reviewed tip with `-Platform Windows` (the only pin, because the OS is the subject). This
confirms that the syntactic refusals hold on Windows. It does not block Review, land or rc (D-5).
It does not touch the live Windows qualification (supervisor pid 209072).

## Rollout, activation and rc

There is no land-time or runtime change. The embedded manifest bytes are unchanged and
`evaluation.txt` is regenerated by `-Update`. Publication to master is not activation. Product hosts
pick it up only with the reviewed rc tip after rc qualification. **rc order: CARD-1483 S4 -> S5 ->
CARD-1528 -> CARD-1540.** S1-S3 are already on rc. This card needs CARD-1528's
`EvaluationExpressionPolicy.cs`, so it cannot be picked before it. On rc, recompute the knob pins
from rc's own definition bodies and run `-Update` on the final rc tree. Never copy master's pins or
records (the rc digest was `637CFA85…`, count 58). The rc carries no `docs/superpowers/plans`, so
drop this plan from the pick. Rollback keeps verification on; it must not re-approve graphs with
the permissive evaluator.

--- next stage ---
next: test-design
handoff: Freeze CARD-1540 fixtures (land-graph-c1540.json + driver -Fixtures), measure V-1..V-5 red on base tool (P2/P11/P4/P6/P7/P8 measured; item-function and V-5 unpinned unmeasured), classify D-6 fixture rewrites so CARD-1528 PCs keep single causes, confirm AuditRepo cwd = repo root, and finalize the 43-result CP-1..CP-7 roster; R3 decided as knob-site pins (D-3), manifest unchanged.
artifact: docs/superpowers/plans/2026-10-23-card-1540-land-audit-environment-timestamp-plan.md
