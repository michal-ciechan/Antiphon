# CARD-1832: Declared test platform taxonomy

Plan for the operator taxonomy from 2026-11-10. Method declarations are authoritative. Class labels are derived. Lane filters exclude by trait. They do not select a test and then skip it.

Base: master `ac6d00022e940ee3b90584c7d313567b4491c498`. Checkpoints name the Linux lane. `-Runner` and `-Platform` are omitted. `GET /api/runner-defaults` and `GET /api/session-runners` were not reachable from this worktree (connection refused on port 17202). No fleet location is embedded.

## Intended behaviour

A test method runs on both Linux and Windows unless it carries one platform declaration. The declaration is a TUnit property, not a skip. The Linux lane's tree filter excludes `WindowsOnly`. The Windows lane's tree filter excludes `LinuxOnly`. A method that is not selected does not appear in the TRX as skipped, so an all-exclusion chunk is not launched and cannot exit 8.

### Declarations

Four sealed attributes in `tests/Shared/PlatformTaxonomy.cs`, namespace `Antiphon.TestSupport`. Each subclasses `TUnit.Core.PropertyAttribute` (unsealed in TUnit 1.44.0, commit `42e3be6d99bb637d21e1dac711d76991a99e49c3`) with property name `Platform` and one of these values:

| Attribute | Property value | Who runs it |
|---|---|---|
| `LinuxOnlyAttribute` | `LinuxOnly` | Linux lane only |
| `WindowsOnlyAttribute` | `WindowsOnly` | Windows lane only |
| `HasLinuxSpecificsAttribute` | `HasLinuxSpecifics` | both lanes |
| `HasWindowsSpecificsAttribute` | `HasWindowsSpecifics` | both lanes |

TUnit 1.44's TUnit0028 analyzer rejects a second `AttributeUsage` on a `PropertyAttribute` subclass, so these four inherit `PropertyAttribute`'s usage: Assembly, Class, and Method, `AllowMultiple = true`, `Inherited = true`. The planned Method-or-Class, `AllowMultiple = false`, `Inherited = false` usage cannot be declared. The constructor requires a nonempty reason, stored on the attribute for the diff and the census. The reason is not the filter value. The filter value stays the closed four-word set. No attribute subclasses `SkipAttribute`. S1 does not add the census that later forbids two `Platform` values on one method.

Class-level application is shorthand for every test method in that class. TUnit copies a class property onto each discovered test (`DiscoveredTestContext.AddProperty` in `TUnit.Engine` 1.44.0). A method attribute beside a class attribute is a census failure. There is no override.

`[Category]` stays the lane category (`Unit`, `Integration`, `Slow`). Platform is a different property key so `TestLaneCategoryGuardTests` is unchanged.

### Class derivation

`PlatformTaxonomy.Derive` reads the method labels. Non-test members are ignored. Zero test methods derive as unannotated.

| Method labels | Class runs on | Class label |
|---|---|---|
| every method `LinuxOnly`, at least one | Linux only | `LinuxOnly` |
| every method `WindowsOnly`, at least one | Windows only | `WindowsOnly` |
| anything else | both | neither exclusive label |
| and any method is `LinuxOnly` or `HasLinuxSpecifics` | both | also `HasLinuxSpecifics` |
| and any method is `WindowsOnly` or `HasWindowsSpecifics` | both | also `HasWindowsSpecifics` |
| every method unannotated | both | no specifics flag |

An all-`LinuxOnly` class is `LinuxOnly`, not also `HasLinuxSpecifics`. The same for Windows. An all-`HasLinuxSpecifics` class runs on both and is `HasLinuxSpecifics`. It is not `LinuxOnly`.

### What the specifics flags mean for selection

`HasLinuxSpecifics` and `HasWindowsSpecifics` do not exclude the method from either lane. They tell the selector which lane must include the class when a diff concerns that OS.

A diff concerns Windows when the matched `tests/impacted-classes.json` kind has `"windows": "required"` or `"windows": "only"`, or a selected edge lands on a method declared `WindowsOnly` or `HasWindowsSpecifics`. A diff concerns Linux in the symmetric case, and for every kind that is not `windows: only`. The selector does not scan `OperatingSystem.Is*` or helper names.

Hand-kind `windows` stays the floor from CARD-1819:

- `only` (`scripts/impacted-classes.ps1` lines 909 and 949): the class stays off the Linux `filters` and on `windowsFilters`. A `LinuxOnly` method inside that class is a census conflict, not an automatic split.
- `required`: the class stays on both lanes. A `LinuxOnly` method is removed from the Windows copy of the filter. A `WindowsOnly` method is removed from the Linux copy.
- no `windows` field: Linux `filters` as today. Add `windowsFilters` only when the derived label is `WindowsOnly`, or when the label has `HasWindowsSpecifics` and the diff concerns Windows. Never strip a `LinuxOnly` or `HasLinuxSpecifics` class off the Linux filter because a helper mentioned an OS.

### Lane filters

Microsoft.Testing.Platform 2.1.0, which TUnit 1.44 uses, evaluates `[Key!=Value]` as "no metadata value equals Value", including a node that lacks the key (`docs/mstest-runner-graphqueryfiltering/graph-query-filtering.md`). A method with `Category=Unit` and no `Platform` matches `[Platform!=LinuxOnly]`. A method with `Platform=LinuxOnly` does not. `!` as a unary operator is only valid immediately after `(`. Use `!=` inside the brackets.

Linux row, method segment: `[Platform!=WindowsOnly]`. Windows row: `[Platform!=LinuxOnly]`. Compose with an existing category on the same segment, for example `/*/*/*/*[Category=Unit]&[Platform!=WindowsOnly]`. Exact chunk filters gain the same suffix: `/*/Namespace/Type/*[Platform!=WindowsOnly]`.

A class whose derived label is `WindowsOnly` is omitted from the Linux chunk list. A `LinuxOnly` class is omitted from the Windows chunk list. A mixed class is launched on both lanes with the method suffix, so the lane runs the methods that belong there and does not launch a chunk whose every method was excluded. That is the repair for the twelve `process-exit-8` chunks in `docs/investigations/2026-11-10-rc-run-dce78ee0-end-unexecuted-outcomes.md` (the note lives on `origin/feat/card-task-276564a2`).

### Before and after

Today the same fact is stored in six places that disagree:

| Store | What it does now | After |
|---|---|---|
| `[NativeLinuxOnly]` `tests/Shared/NativeLinuxOnlyAttribute.cs` lines 8-24 | `SkipAttribute`; skips off Linux | deleted in S9; call sites are `[LinuxOnly]` |
| `NativeLinuxGate.Require` `tests/Antiphon.Tests/TestHelpers/NativeLinuxGate.cs` lines 3-9 | throws `SkipTestException` off Linux | deleted in S9 |
| `WindowsUnitSkipRoster` `tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs` | hand inventory of Windows-lane skips; `SkipOnLinux` at line 280 | generated from declarations, then deleted as a hand list in S9 |
| `tests/expected-platform-skips.json` | 25 methods the Windows coverage rule may skip | generated, then deleted in S9 |
| `tests/test-execution-policy.json` `platform` exclusions | 108 hand rows (102 `windows`, 6 `linux`) | generated from declarations in S9; headed, live, and opt-in rows stay |
| CARD-1820 `ClassifyWindowsOnly` on `origin/feat/card-task-693bdeb9` at `ed6270b61` (`DerivedImpactIndexBuilder.cs` token lists around lines 56-79 and 658-669, classify around 752-790) | any OS token marks the class `windowsOnly` | not on master. Round 4 reads `Derive` and does not retokenize |

`scripts/impacted-classes.ps1` lines 909 and 949 stay the `windows: only` skip. S4 adds a declaration read beside them. `scripts/lib/nightly-tests-impl.ps1` `Get-NightlyNativeExecutionArguments` (lines 858-911) gains the property suffix in S5. `scripts/lib/nightly-coverage.ps1` `Get-NightlyExpectedSkipRoster` (lines 1144-1169) gains a declaration source in S5 and drops the hand file in S9. `scripts/test-rc-platform.ps1` `Get-C1323LinuxOnlyCensus` (from line 588) reads `[LinuxOnly(` after S9.

### What stays unchanged

`[Category("Unit")]` and `[Category("Integration")]`. Kind globs and the CARD-1819 `windows` field. `tests/linux-test-roster.json` (725 classes, 528 include, 197 exclude): it is the CARD-0590 frozen admission roster, not an OS taxonomy. `tests/rc-project-categories.json`: project categories, not tests. Headed, live, QEMU, and opt-in policy exclusions. Stage bundles. No database, no production host, no new csproj.

## Ground truth

Measured on this tree unless a SHA is named.

| Assumption | What the code does |
|---|---|
| One declaration drives selection | Six lists above, plus token scans |
| Class label is the superset of its methods | Nothing derives a class label. CARD-1820 classified the file |
| Default is both lanes | Unannotated tests are selected, then some skip |
| Exclude, do not skip | `[NativeLinuxOnly]` and `NativeLinuxGate` skip. rc run `dce78ee0` recorded 25 expected skips and 12 exit-8 chunks whose only test had skipped |
| Token scan finds Windows-only tests | At `682375f5d` `windowsOnly` was 84. At `ed6270b61` it was 671. 71 Linux-gated classes with index edges and no Windows skip moved onto the Windows lane (`origin/feat/card-task-135047fd` review). Round 2 (`origin/feat/card-task-7c6f052c`) had missed `RuntimeInformation`, aliases, and helpers |
| `HasXSpecifics` means "runs on both, and the selector must schedule that OS" | The flag does not exist. Round 3's witness treated a Windows-lane hit as success for replay rows that the plan required on Linux |
| TUnit 1.44 can exclude one method | `/*/*/*/*[Category=Unit]` already matches a `TestMetadataProperty` on the method segment. `[Platform!=LinuxOnly]` is the same grammar |

Counts for the migration, `tests/**/*.cs` excluding `bin/` and `obj/`:

| Detector | Files | Of which `*Tests.cs` | Hits |
|---|---|---|---|
| `[NativeLinuxOnly(` | 99 | 99 | 225 textual, including witnesses |
| `NativeLinuxGate.Require(` | 19 | 19 | 127 |
| `if (!OperatingSystem.IsWindows()) throw` | 45 | 38 | |
| `if (!OperatingSystem.IsLinux()) throw` | 27 | 22 | |
| `if (OperatingSystem.IsWindows()) throw` | 9 | 8 | the LandGraph shape |
| `OperatingSystem.IsWindows(` anywhere | 290 | | portability included; not an annotation list |
| `RuntimeInformation.IsOSPlatform(` | 40 | | |
| `WindowsUnitSkipRoster` `new(` classes | 28 classes | | |
| `expected-platform-skips.json` | 25 methods, 10 classes, all `platform: windows` | | |
| policy `platform` exclusions | 108 rows, 102 `windows`, 6 `linux` | | |

The mechanical script annotates only explicit gates: a line whose trimmed text starts with `[NativeLinuxOnly(`, a `NativeLinuxGate.Require(` in a test method, and a skip thrown directly under `IsWindows` / `IsLinux` as in the three `if` shapes. It does not annotate a bare `IsWindows` branch. `HasLinuxSpecifics` and `HasWindowsSpecifics` are not assigned by the script.

## Decisions

### D-1. Property attribute, not Category, not Skip

`Platform` is its own property. Category already means Unit, Integration, and Slow. A second system of skip attributes recreates the exit-8 and unexecuted-outcome failures.

Rejected: more `[Category("LinuxOnly")]` values. Rejected: keeping `[NativeLinuxOnly]` as the skip and adding a trait beside it. Rejected: a Roslyn analyzer that fails the build on every current gate. The suite cannot be annotated in one slice, and an analyzer would make the intermediate commits unbuildable. The census test is the ratchet, with a baseline of current hits.

S1 proves a `PropertyAttribute` subclass is visible to `--treenode-filter`. If CP-1 executes zero tests, stop. Do not fall back to a skip attribute. The repair is to register the property the way that witness shows TUnit 1.44 requires: omit `AttributeUsage` (analyzer TUnit0028) and inherit `PropertyAttribute`'s usage.

### D-2. Shared file now, Support project later

`Antiphon.Tests.Support` does not exist on this base (CARD-1729). S1 adds `tests/Shared/PlatformTaxonomy.cs` and links it from `tests/Antiphon.Tests/Antiphon.Tests.csproj` the way lines 172-177 link `NativeLinuxOnlyAttribute.cs`. Migration slices link it into each test csproj that gains a use. CARD-1729 moves that file into `Antiphon.Tests.Support.Core` without renaming the namespace. This card does not add a project, a solution line, or a land-graph shape.

### D-3. Derivation is computed, class attributes are shorthand

Authors write method attributes, or one class attribute when every method shares it. The selector and the coverage rule call `Derive`. They do not trust a second hand-written class label.

### D-4. The detector is simple and does not choose the label

The census fails a test method that matches a pattern and has no `Platform` property, unless the method is in `tests/platform-taxonomy-baseline.txt` or `tests/platform-taxonomy-allowlist.txt`. Patterns, lexical, one hop to a same-file method the test calls by name:

- `OperatingSystem.IsWindows`, `IsLinux`, `IsMacOS`, `IsOSPlatform`
- `RuntimeInformation.IsOSPlatform`
- `Skip.Test`, `Skip.When`, `Skip.Unless`, or `throw new SkipTestException`, when the message within the same statement contains Windows, Linux, POSIX, junction, DPAPI, or kernel32
- `NativeLinuxGate.Require` or a line starting `[NativeLinuxOnly(`
- Windows-only API spellings: `kernel32.dll`, `user32.dll`, `ProtectedData`
- path-root literals `C:\`, `\\?\`, `/proc/`

Not hits: `Path.DirectorySeparatorChar`, `Path.Combine`, a bare `IsWindows ? "a.exe" : "a"` with no skip, comments, and files under `bin/` or `obj/`. Linked copies are scanned at `tests/Shared/`, once.

The census does not check that `LinuxOnly` agrees with the branch direction. That check is what made the token scan wrong. A wrong label is a human review of the diff, then a native lane.

The baseline is the sorted list of current hits, written by S3. Each annotation slice deletes the lines it declared. The allow-list starts empty and gains a line only when a slice shows the hit is not a gate. A new hit in neither file fails.

### D-5. Hand kinds stay; declarations refine method membership

CARD-1819 `windows: only` and `windows: required` are not re-derived. Method exclusion is applied on top. Splitting a `windows: only` class that also has Linux-capable siblings is out of scope (`CapabilityScriptTests` is the known case: the DPAPI method is Windows-only and a sibling stays on both, while the kind still sends the class to Windows only).

### D-6. CARD-1820 round 4 reads declarations

Do not repair CARD-1820 with another token grammar. After S2 lands, that builder deletes `OsGateTokens`, `SourceHasOsGate`, and the token hop in `ClassifyWindowsOnly`. `windowsOnly` is true only when `Derive` returns `LinuxOnly` false and `WindowsOnly` true. `HasLinuxSpecifics` is never `windowsOnly`. The replay witness must require rows 2, 4, 7, 9, and 14 on the Linux `filters`. Accepting `classes || windowsFilters` is the round-3 hole. This card does not edit the unlanded builder.

### D-7. Dual-read until S9, then one system

Until S9, a `[NativeLinuxOnly]` line counts as `LinuxOnly` for the reader, and `NativeLinuxGate.Require` counts as `LinuxOnly` for the method that calls it. The census forbids both spellings on one method. S9 deletes the old types, the gate, the hand skip roster, and the hand expected-skip file, and removes the alias from the reader. After S9 the old spelling is a census failure with no baseline entry.

### D-8. No migration

No database, no model, no `Down`.

### D-9. No new project and no CARD-1546 dependency

Repository-lease tests stay in `Antiphon.Tests`. CARD-1546's git csproj split does not order these slices. CARD-1729 does not order them either (D-2).

## Replay acceptance

The 17 rows are the CARD-1820 plan's table (`origin/feat/card-task-693bdeb9:docs/superpowers/plans/2026-11-09-selector-v2-derived-index.md`, the Replay section). Rows 2, 4, 7, 9, and 14 are the ones round 3 moved off Linux. Their introducing commits are in this history. S4 replays `scripts/impacted-classes.ps1 -Range <parent>..<sha> -Json` and fails unless the class is on the Linux `filters`.

| Row | Class | Commit | Declaration this card requires | Linux `filters` | `windowsFilters` |
|---|---|---|---|---|---|
| 2 | `ProcessSpawnLimitTests` | `be4c317a38568a37b15d41ae985fee6f8054ef1d` | none. Helper `IsWindows` is portability | yes | no |
| 4 | `RepositoryMutationLeaseTests` | `dff78584d636fa49eca37652f2c3f14a48002a92` | none | yes | no |
| 7 | `C1677TempFamilyRegistryTests` | `de3c59991297c9d49d18bba0b7ad6d25ea270156` | none | yes | no |
| 9 | `LandGraphEvaluationTests` | `bc96c71bef39761b4dbc5c05e1030312396a0e16` | `EvaluationBudgetRefusesSlowDotnet` is `LinuxOnly`. Class is both plus `HasLinuxSpecifics` | yes | not because of that method. The method is excluded from a Windows filter by `[Platform!=LinuxOnly]`, not skipped |
| 14 | `NightlyCoreTests` | `a930059d8b82f259549925120fda3abf196fa08e` | none | yes | no |

The other twelve stay in the fixture so they cannot drift. Row 1 stays the hand kind `windows: only` (`AgentTaskLandDeliveryE2ETests`). Row 3 (`Card1277PtyHostFixtureTests`) is `HasWindowsSpecifics`: Linux already passed, and the kind is `windows: required`, so both lanes keep the class. Row 5's newline class stays on both; the kind is already `windows: required`. Row 6 stays on Linux. Row 8's DPAPI method is `WindowsOnly` and the kind stays `windows: only` (D-5). Rows 10-13 and 15-17 are not platform rows; the selector's current kind or declaration behaviour stands, and this card does not move them onto a Windows-only filter.

## Verification design

Witnesses go red before the production change in the same slice. Each witness has one mutant. Mutation after land is method-scoped and is not a checkpoint row.

| Id | Witness | Mutant | Doc pin |
|---|---|---|---|
| V-1 | `LinuxOnly_is_a_property_not_a_skip` asserts `LinuxOnlyAttribute` subclasses `PropertyAttribute` and not `SkipAttribute`, and the property value is `LinuxOnly` | Change the base to `SkipAttribute`. V-1 fails | |
| V-2 | CP-1 selects the method-level and class-level Linux-only tests and the unannotated sibling fails under `ANTIPHON_PLATFORM_FILTER=include`. CP-2 selects only the unannotated test; a Linux-only body fails under `exclude` | Drop the `Platform` value. CP-1 executes the unannotated test and fails, or executes zero | |
| V-3 | The seven derivation methods, including mixed Linux-only plus unannotated equals both plus `HasLinuxSpecifics`, and all-`HasLinuxSpecifics` is not `LinuxOnly` | Return `LinuxOnly` for a mixed set. V-3 fails | |
| V-4 | Census: a fixture method with `SkipTestException` and the word Windows, and no attribute, fails; the same method with `[LinuxOnly("r")]` passes; two `Platform` values fail; a class attribute that disagrees with a method fails; the committed baseline matches the live scan | Return success for an undeclared fixture. V-4 stays green | |
| V-5 | Selector fixture: a `HasLinuxSpecifics` class stays on Linux `filters`; a `WindowsOnly` class does not; rows 2, 4, 7, 9, 14 are on Linux `filters` for the commits above | Strip `HasLinuxSpecifics` the way round 3 stripped token hits. V-5 fails | |
| V-6 | Lane builder appends `[Platform!=WindowsOnly]` on Linux and `[Platform!=LinuxOnly]` on Windows, and omits a `WindowsOnly` class from the Linux chunk list | Omit the suffix. V-6 fails | |
| V-7 | During S6-S8 the reader treats `[NativeLinuxOnly(` as `LinuxOnly`. S9 deletes that arm | Keep the arm after the type is gone. The S9 census fails | |
| R-1 | `ImpactedClassesSelectorTests` stays green. Unannotated classes keep today's filters | | |
| R-2 | `TestLaneCategoryGuardTests` stays green. Platform is not a category | | |
| R-3 | S9: `NativeLinuxPolicyWitnessTests` and `ReleaseGatePlatformTests` follow `[LinuxOnly(` and the recomputed `policyHash` | | R-3 pins `tests/test-execution-policy.json` `policyHash` and `tests/Shared/NativeLinuxPolicyWitness.cs` |
| R-4 | S8: `WindowsUnitPlatformAdmissionTests` matches declarations instead of the hand roster | | |

Existing tests affected: `ImpactedClassesSelectorTests` (S4), `TestLaneCategoryGuardTests` (S3, no production change expected), `WindowsUnitPlatformAdmissionTests` (S8), `NativeLinuxPolicyWitnessTests` and `ReleaseGatePlatformTests` (S9). No public DTO, route, operation label, remote script function, or statement budget changes. The impacted-classes row for a docs-only plan diff is none. Later slices name the classes they edit in the checkpoint filter.

A new test that only compares constants is a stub. V-1 reads the attribute base type. V-2 is the real tree filter, with the environment fault on the wrong method. V-4's red case is an in-memory fixture the production scanner returns a hit for.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1832-s1/` | s1-include | `/*/*/(PlatformTaxonomyFilterTests*)\|(PlatformTaxonomyClassFilterTests*)/*[Platform=LinuxOnly]` | V-1, V-2 | 2 executed, 0 failed, 0 skipped | 2 | 8 | false | `ANTIPHON_PLATFORM_FILTER=include` |
| CP-2 | S1 | `CP-1` | s1-exclude | `/*/*/PlatformTaxonomyFilterTests*/*[Platform!=LinuxOnly]` | V-2 | 1 executed, 0 failed, 0 skipped | 1 | 3 | false | `ANTIPHON_PLATFORM_FILTER=exclude` |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1832-s2/` | s2-derive | `/*/*/PlatformTaxonomyDerivationTests/*` | V-3 | 0 failed | 7 | 8 | false | n/a |
| CP-4 | S3 | `tests/Antiphon.Tests -> bin-c1832-s3/` | s3-census | `/*/*/(PlatformTaxonomyCensusTests*)\|(TestLaneCategoryGuardTests*)/*` | V-4, R-2 | 0 failed | 6 | 8 | false | n/a |
| CP-5 | S4 | `tests/Antiphon.Tests -> bin-c1832-s4/` | s4-selector | `/*/*/PlatformTaxonomySelectorTests/*` | V-5 | 0 failed | 5 | 8 | true | n/a |
| CP-6 | S4 | `CP-5` | s4-regression | `/*/*/ImpactedClassesSelectorTests/*` | R-1 | 0 failed | 40 | 12 | true | n/a |
| CP-7 | S5 | `tests/Antiphon.Tests -> bin-c1832-s5/` | s5-lanes | `/*/*/PlatformTaxonomyLaneFilterTests/*` | V-6 | 0 failed | 4 | 8 | false | n/a |
| CP-8 | S6 | `tests/Antiphon.Tests -> bin-c1832-s6/` | s6-census | `/*/*/PlatformTaxonomyCensusTests/*` | V-4 | 0 failed | 5 | 8 | false | n/a |
| CP-9 | S7 | `tests/Antiphon.Tests -> bin-c1832-s7/` | s7-census | `/*/*/PlatformTaxonomyCensusTests/*` | V-4 | 0 failed | 5 | 8 | false | n/a |
| CP-10 | S8 | `tests/Antiphon.Tests -> bin-c1832-s8/` | s8-roster | `/*/*/(PlatformTaxonomyCensusTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | V-4, R-4 | 0 failed | 10 | 8 | false | n/a |
| CP-11 | S9 | `tests/Antiphon.Tests -> bin-c1832-s9/` | s9-cutover | `/*/*/(PlatformTaxonomyCensusTests*)\|(NativeLinuxPolicyWitnessTests*)\|(ReleaseGatePlatformTests*)/*` | V-7, R-3 | 0 failed | 14 | 12 | false | n/a |

Suffix check: `PlatformTaxonomyFilterTests` is not a prefix of `PlatformTaxonomyClassFilterTests`. Census, derivation, lane, and selector names do not prefix each other. No row name contains `Land`. No output name contains `ref`.

CP-2 names only `PlatformTaxonomyFilterTests`. The checkpoint roster requires every parenthesized `(Class*)` token to execute or skip. `PlatformTaxonomyClassFilterTests` is entirely `LinuxOnly`, so naming it on `[Platform!=LinuxOnly]` is a roster miss even when the unannotated sibling passes and nothing is skipped. The class shorthand is proved by CP-1. `PlatformTaxonomyFilterTests*` does not match `PlatformTaxonomyClassFilterTests`.

`ImpactedClassesSelectorTests` is 28 `[Test]` methods. `malformed_table_is_refused` has 13 `[Arguments]` rows. Executed count is 40, which matches the CARD-1820 round-2 measurement of this class. CP-6 Min is 40. CP-3 Min is the seven methods named under V-3. CP-4 Min is five census methods plus the one `TestLaneCategoryGuardTests` method. CP-7 Min is four lane methods (Linux suffix, Windows suffix, omit `WindowsOnly` class, do not launch an all-excluded chunk). CP-10 adds the five `WindowsUnitPlatformAdmissionTests` methods. CP-11 adds three `NativeLinuxPolicyWitnessTests` and six `ReleaseGatePlatformTests`.

Code raises Min when a class has more tests. It does not lower it.

### Cost

Table sum of `EstimatedMinutes` is 91. That is the ordinary floor for the whole plan, plus authoring. A Code dispatch of one slice runs only that `After` group: S1 is 11, S2 is 8, S3 is 8, S4 is 20, S5 is 8, S6 is 8, S7 is 8, S8 is 8, S9 is 12. No explicit `totalTimeoutMinutes`. The tool default `max(30, 2 * sum + 10)` applies to whatever subset the dispatch selects. No row estimate exceeds 15, so import's 45-minute advisory stays silent (`3 * 12 = 36`).

## Performance budget

Standards: `docs/test-performance-standards.md` and `docs/testing-and-build.md` (Performance standards, plan format). Per-row timeouts in the tool are `max(15, 3 * EstimatedMinutes)`. That is the importer default, not a measured p95, and this plan does not treat it as one.

| Work | Selection | Expected duration | Source |
|---|---|---|---|
| CP-1 build | `tests/Antiphon.Tests`, one project, native output redirected only by the checkpoint tool's `bin-c1832-s1/` | 8 minutes for build plus two tests | Cold checkpoint builds in the CARD-1782 repair were 337 s. 8 minutes is above that sample and under the advisory |
| CP-2 | same build, two filters, three tests total across CP-1 and CP-2 | 3 minutes | no second build |
| CP-3, CP-4, CP-7, CP-8, CP-9 | one class or a class plus the lane guard | 8 minutes each, test body under 60 s | Lane guard published at 5.5 s in the CARD-1820 plan. The census walks `tests/**/*.cs` once per class run (2586 files on this tree). Allowance 60 s for the scan, about 2x a few-second sibling. Provisional until S3 records one sample |
| CP-5 | new selector tests, one script invocation per witness, serial | 8 minutes | Selector `-Range` on the CARD-1820 host, n=19, median 17 s, max 24.1 s. Five replays fit under 3 minutes plus build |
| CP-6 | `ImpactedClassesSelectorTests`, 40 results, serial | 12 minutes | Same class, loaded server2: 413 s in CARD-1820 round 2, 588 s in round 3 beside the derived-index tests. 12 minutes sits above the larger sample. Not a p95 (two loaded samples). Do not raise it inside the slice if one run is slower; report the number |
| CP-10 | census plus roster admission | 8 minutes | roster tests read source |
| CP-11 | census plus policy witnesses | 12 minutes | hash and marker witnesses. Provisional. A run over 15 minutes is reported, not folded into the estimate |

Phases: build and slot wait are the row estimate's build portion and are reported separately by the checkpoint line. No container, no migration, no snapshot, no shared Postgres. The census and derivation tests share nothing across tests; each is a source read. Selector tests own one PowerShell child each and carry `[ParallelLimiter<ProcessSpawnLimit>]` because they spawn `pwsh`. The filter witnesses do not. No fresh-start witness.

Recording: the checkpoint TRX under the run directory. S3 writes the baseline duration into the slice report. Timeouts are the tool's derived row timeout. Nothing here is budgeted over 30 minutes. A selector replay batch that exceeds 30 minutes is a Debug task with the timing, not a reason to widen the row.

`repeat-proof: at most 3 normal + 2 loaded repetitions per unchanged proof selection; none required after green.`

## Slices

Each slice is one Code task, 60-120 minutes, its own commit, its checkpoint row after the commit. The annotation script is `scripts/annotate-platform-taxonomy.ps1`, added in S6. It prints a unified diff and writes nothing until `-Apply`. It rewrites only the explicit gates from Ground truth: a trimmed line that starts with `[NativeLinuxOnly(` becomes `[LinuxOnly(reason)]`; a `NativeLinuxGate.Require(reason)` call becomes `[LinuxOnly(reason)]` and the call is removed; `if (!OperatingSystem.IsWindows()) throw` becomes `[WindowsOnly]`; `if (!OperatingSystem.IsLinux()) throw` becomes `[LinuxOnly]`; `if (OperatingSystem.IsWindows()) throw` of a skip becomes `[LinuxOnly]`. The existing reason string is copied. It does not annotate a bare `IsWindows` branch, `RuntimeInformation`, or `HasLinuxSpecifics` / `HasWindowsSpecifics`. D-4's wider patterns stay census hits until a human declaration.

### S1. Attributes and the filter proof

Add `tests/Shared/PlatformTaxonomy.cs` and the csproj link. Add `PlatformTaxonomyFilterTests` (one `[LinuxOnly("witness")]` method and one unannotated method) and `PlatformTaxonomyClassFilterTests` (`[LinuxOnly("class shorthand")]` on the class, one method). Both Linux-only bodies pass only when `ANTIPHON_PLATFORM_FILTER=include`. The unannotated body passes only when the value is `exclude`. Run CP-1 and CP-2. CP-2's filter names only `PlatformTaxonomyFilterTests`, for the roster reason under Checkpoints.

### S2. Derivation

Add `Derive` and `PlatformTaxonomyDerivationTests` with the seven rows in the derivation table, including the five replay rows as data: expected lane, not a live script call. Run CP-3.

### S3. Census and baseline

Add the scanner, `PlatformTaxonomyCensusTests`, `tests/platform-taxonomy-baseline.txt` (sorted, one `path:line method pattern` per hit), and `tests/platform-taxonomy-allowlist.txt` with a comment and no entries. Run CP-4.

### S4. Selector reads declarations

In `scripts/impacted-classes.ps1`, beside the `windows: only` continues at lines 909 and 949, classify the selected class with the same reader as the census (D-7 alias included). Apply the selection rules in "What the specifics flags mean". Do not scan tokens. Add `PlatformTaxonomySelectorTests` for V-5, including the five live `-Range` replays. Run CP-5 and CP-6. CP-6 must stay at 40 passed. A failure there is a behaviour change for an unannotated class and is a bug in S4.

### S5. Lane filters and the exclusion set

Add the builder that appends the property suffix and drops an all-excluded chunk. Teach `Get-NightlyExpectedSkipRoster`'s caller to treat a declared `LinuxOnly` method as excluded on Windows rather than as an expected skip. Leave the hand JSON in place for methods not yet declared. Run CP-7. Do not switch the live nightly path's chunk list until S9; CP-7 tests the builder function.

### S6. First 50 explicit files

Ordered repo-relative `*Tests.cs` paths that match the explicit patterns, files 1-50. Review the diff. Delete the matching baseline lines. Run CP-8. `LandGraphEvaluationTests.EvaluationBudgetRefusesSlowDotnet` is in this ordered list when its path falls in the first 50, and in S7 or S8 otherwise. It must be `[LinuxOnly]` with the existing skip reason before S9.

### S7. Next 50

The next 50 paths, including remaining `NativeLinuxGate.Require` files not already rewritten. Run CP-9.

### S8. Roster and the remainder under 50

Whatever explicit files remain, capped at 50. If more than 50 remain, stop and say so; do not exceed the cap. Point `WindowsUnitSkipRoster` readers at declarations for the rows this slice replaced, and keep unread hand rows until S9. Run CP-10.

### S9. One system

Generate the policy `platform` rows and `policyHash` from declarations. Delete `tests/expected-platform-skips.json` and the hand arrays in `WindowsUnitSkipRoster` once the baseline has no explicit-pattern lines left. Delete `NativeLinuxOnlyAttribute`, `NativeLinuxGate`, and the D-7 alias. Update `Get-C1323LinuxOnlyCensus`, the nightly coverage roster path, and the lane chunk builder to the declaration filter. Run CP-11. If the baseline is not empty, S9 does not start.

`HasLinuxSpecifics` and `HasWindowsSpecifics` for rows 3 and 5 are a human edit in the slice that touches those files, not a script guess. Row 3's class is already `windows: required`, so leaving it unannotated still schedules Windows. Add `HasWindowsSpecifics` only on the method that is the Windows race, with the reason that Linux passed.

## Out of scope

Rewriting `tests/linux-test-roster.json` or `tests/rc-project-categories.json`. Creating `Antiphon.Tests.Support`. CARD-1546's project move. A new token grammar for CARD-1820. Annotating every `IsWindows` portability branch. Headed, live, and opt-in exclusions. macOS. A TUnit upgrade. Any desktop run. Database migrations.

## Risks

| Risk | What the slice does |
|---|---|
| Subclass of `PropertyAttribute` is invisible to the source generator | CP-1 is the gate. Stop on zero tests (D-1) |
| Class property not copied onto methods | CP-1 Min 2 fails because the class-level test is absent |
| Two `Platform` values on one method | Census fails. Exclude filters then exclude the method from both lanes |
| Argument rows cannot differ | One attribute covers every `[Arguments]` row. Split the method. The census does not invent per-argument labels |
| Base class or fixture gate | `Inherited = false`. A helper skip is a baseline hit until the test method is declared. The script does not annotate the helper's callers by guessing |
| Generated code | The census skips `bin/` and `obj/` |
| Double count | A mixed class is in both chunk lists. Each exclusive method is in one lane. `Has*` and unannotated methods run on both, once per lane. Excluded methods are absent, not skipped, so coverage does not count them as unexecuted |
| Policy hash | S9 updates the hash and the witness in the same commit. An earlier slice must not edit `tests/test-execution-policy.json` |
| `windows: only` hides a Linux-capable sibling | Left as D-5. Do not split the kind here |
| Bulk script annotates a string fixture that mentions `[NativeLinuxOnly(` | The script requires the trimmed line to start with the attribute. Witnesses that embed the source stay until a human slice; they are baseline hits, not rewritten |
| Exit 8 if a chunk is still launched after every method is excluded | S5's builder drops that chunk. S9 wires it. The twelve dce78ee0 chunks are the examples |

## Operator-only steps

None. Windows proof is owed to the next Windows Full rc run: declared `LinuxOnly` methods are absent from that run, not skipped, and the twelve exit-8 shapes do not recur. This plan runs no desktop command and schedules no Windows checkpoint row.
## Performance budget note

S3 records the census scan's wall time in the slice report. That sample replaces the provisional 60 s allowance if it is higher. It does not change a checkpoint estimate inside the same slice.

## Migrations

None. (D-8.)
