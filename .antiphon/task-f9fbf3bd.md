# CARD-1843 Review round 1 (task f9fbf3bd), Final/Full

Verdict: CLEAN. No blockers and no regressions in `3db1a43b0..8d426d265` (origin/feat/card-task-3e96732b, Grok Code task 3e96732b). Three advisories are listed below for cards. None of them holds the land.

- Reviewed SHA `8d426d2650e20c4781f1c4b4d38177bbaee81afd`. Base and master are both `3db1a43b02246706dc28075fce7e7b5e84804ddb`; origin/master had not moved when this review ran.
- Runner: server2 Linux. Every build and test ran through the checkpoint tool, which leases its own slots. The tool build and the index regeneration ran under `scripts/build-slot.ps1`.

## Brief checks

| Check | Result |
|---|---|
| Every declaring project gets a row; no class lost its Antiphon.Tests row | Holds. A node scan of all tracked `tests/**/*.cs` compared the old map with the new one. No class whose old project was `tests/Antiphon.Tests` lacks that project in its new set, unless every file that declares it sits in a remainder project directory. Antiphon.Tests compiles no test class from those directories: its linked files there are helpers only (CodexStartupFixtures, EffortTestScreen, ResilienceTestSupport, and the SessionRunner/Messaging fakes). `ProcessSpawnLimitTests` now has rows in Agents.Pty, SessionRunner and Antiphon.Tests. `HostStatsEndpointTests` (a real class in both Antiphon.Tests and SessionRunner) and `PtyBackendEnvGuardTests` (real in five projects) gain rows they lacked before. About 270 Pty/PtyHost/Resilience/Server.Git classes that used to be wrongly filtered against Antiphon.Tests now point at their own project. |
| A malformed catalog exits 1, never falls back | Holds. Scratch probes of `{}`, `[]`, an empty file and `{"sets":null}` all exit 1 with `impacted-classes catalog refused: tests/test-selection-policy.json: ...`. A null `remainderProjects` falls back to the three hard-coded projects, and a backslash entry is normalised (the Mirror class maps to its own project). |
| Agents.Pty, Resilience and Server.Git recognised | Holds. `-Path` probes map `VisiblePtyOutputTests` to Agents.Pty, `FramingTests` to PtyHost and `GitIndexLockTests` to Server.Git. All eight catalog remainderProjects are read. |
| C1801 why-missed constants | The tests now assert exact tuples. The Antiphon.Tests filter drops only `GitIndexLockTests` and `ServerGitAssemblyTests`, which move to the new exact Server.Git row. SessionRunner gains `ProcessSpawnLimitTests`, and there is a new exact Agents.Pty row. `ProcessSpawnLimitTests` stays in the Antiphon.Tests filter. No assertion is loosened: `ShouldContain` was already the form before this change. |
| Merge oracle vs frozen 72fada66 | A range with identical output is still byte-compared. The `AssertOnlyProjectsMoved` path requires all of the following: the same field names; byte-equal non-filter fields (`classes`, `kinds`, `fixedClasses`, ...); identical class and method entry keys in `filters` and `windowsFilters`; per-entry projects equal to an independent C# scan (catalog plus a regex over `tests/`); byte-equal land rows; and `moved > 0`. The top `treenodeFilter` is not compared, but it is a projection of the checked rows (`Get-TopFilter`). |
| Docs subsection | The rule and the template rows are correct, and every class the `guards` row names exists. See A-1. |
| Derived impact index | A rerun of `scripts/regenerate-derived-impact-index.ps1` (with build, in a slot) printed `derived-impact-index unchanged`. |

## Runs at 8d426d26

Run `20261111-224415-0a89`, from plan extract `.antiphon/review-f9fbf3bd-plan.md` (gitignored):

- CP-1 (impacted rows for the range, plus `MergeHighRadiusSubsetTests`; 26 classes): executed=332 passed=331 failed=1 skipped=0, buildSource=verified.
  - The one failure is `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged`: `The application to execute does not exist: .../Antiphon.DerivedImpactIndex.dll`.
  - This is the known environmental precondition: the regenerate driver is not in the solution and has to be prebuilt. The Code report saw the same.
- CP-2 (guard batch: TestClassificationPolicy, TestClassificationGuard, SlowTestRegistryMerge, SlowTestTripwire, NativeLinuxPolicyWitness, TaskPlatformGuidance, C1729LayeringGuard): executed=66 passed=66. `MechanicalPcContractTests` and `TestLaneCategoryGuardTests` ran green inside CP-1.
- CP-3 (the new SessionRunner row, `C1677TempFamilyRegistryTests`): executed=3 passed=3.
- Run verdict RED exit=1, from the CP-1 environmental red only. The receipt refuses `report_failed`.
- Slow classes: DerivedImpactIndexTests 640 s, ImpactedClassesSelectorTests 533 s.

Run `20261111-232710-4402`, after the driver was built by the regenerate script:

- CP-1R `/*/*/DerivedImpactIndexTests/regenerate_script_reports_changed_then_unchanged`: executed=1 passed=1, buildSource=verified.
- `validate` printed `CHECKPOINT SOURCE VALID source=8d426d2650e20c4781f1c4b4d38177bbaee81afd rows=1`.

Other checks:

- `check-evidence-diff.ps1 -Repository . -BaseRef 3db1a43b0 -HeadRef 8d426d265`: commits=5 entries=1 violations=0, exit 0.
- `git merge-tree --write-tree origin/master 8d426d265`: tree `8947c4b9`, exit 0.

PC-1..PC-5 stay pending for method-scoped SourceLanding Mutation. Neither this round nor nightly discharges them.

## Advisories (cards, not land holds)

- A-1. The docs subsection says that a remainderProjects class runs in its own project and that "Today that is tests/Antiphon.Resilience.Tests and tests/Antiphon.Server.Git.Tests", then that "Any other class runs against tests/Antiphon.Tests".
  - Agents.Pty, PtyHost, E2E, Messaging and SessionRunner are also catalog remainderProjects; the first three are new to the map with this change.
  - Fix: name all of them, or say "every remainderProjects entry, for example ...".
- A-2. "Every declaring file" includes C# declarations inside raw-string test fixtures, so these classes get phantom `tests/Antiphon.Tests` rows that run 0 tests:
  - `C1677TempFamilyRegistryTests`, declared in a fixture in `DerivedImpactIndexTests.cs`.
  - `DispatchBaseWarningDeliveryE2ETests`, declared in fixtures in `DispatchBaseWarningRosterTests.cs`.
  - In a union row this is harmless. A selection made only of such classes gives a zero-count CP row.
  - It fails loud, unlike the old first-file rule, which could silently drop the real project in the same situation.
  - The C# oracle uses the same regex, so it cannot see this.
- A-3. Projects with tests that are not in remainderProjects still map to `tests/Antiphon.Tests`. This is unchanged from master.
  - `Antiphon.MarkdownPdf.Tests` and `Antiphon.Observability.Tests` are not compiled into Antiphon.Tests, so their rows run against the wrong project.
  - `tests/Shared/*Tests.cs` (NativeLinuxPolicyWitnessTests, TestClassificationGuardTests) compile into four projects but get only the Antiphon.Tests row.
