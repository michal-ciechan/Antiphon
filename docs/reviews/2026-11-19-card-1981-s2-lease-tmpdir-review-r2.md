# CARD-1981 S2 lease TMPDIR: review round 2

Subject: `origin/feat/card-task-276b8fe7` at `9ec1fd73809cb11ec1f53fba257842c3136ae248` (Grok author). Round 2 covers the diff only:
`05802489ac0f43715a69c342b4429e07da5e0ecf..9ec1fd73809cb11ec1f53fba257842c3136ae248`. Blockers only. Reviewer: Claude Opus, task 7a8c54aa.
Master was `b2547853c096260c4ca7319f763a7e5e25a5510e`.

**Verdict: CLEAN.** Round 1 blocker B-1 (review dd9a3bb3) is resolved, and the change adds no new blocker or regression.

## B-1 resolution

- **Policy coverage.** All three new `[NativeLinuxOnly]` methods now have a Windows rc exclusion:
  - `C1981LeaseTempDriverTests` is one new row. It lists both methods and its owner is CARD-1981.
  - `BuildSlotScriptTests.C1981_LeaseTempDirConfinesSdkScratch` is added to the existing row beside `C800_WrapperInterruptKillsChildAndReleasesLease`. That row keeps owner CARD-0590.
  - The session-runner method `C1981_dotnet_sdk_empty_accepts_only_the_exact_shape` is still covered by the `C1677TempSweepNativeTests` class row. That row has no `methods`, so it excludes the whole class on Windows (owner CARD-1677).
- **Editing the existing row follows repository convention.**
  - No class in `profiles.rc.exclusions` has two rows. A census of all rows found zero duplicate suite/class/platform keys.
  - Rows already name other cards in their reason while keeping their original owner: `CardWorktreeMirrorCleanupTests` (owner CARD-1561; reason cites CARD-1017 and CARD-1032) and `ScriptModelSpawnLaneTests` (owner CARD-1514; reason cites CARD-1984).
- **The C800 exclusion is not weakened.**
  - C800 is still listed, and the platform is still `windows`. The reason still starts with `Linux-only by construction:` and still names CARD-0590.
  - The frozen witness key moved to the ordinal pair. So `witnessed-row-reshaped` and `witnessed-row-missing` still guard the row, and removing C800 would turn the witness red.
  - Rows that are Linux-prefixed Windows rows are left out of the legacy payload. So `LegacyExclusionDigest` correctly stays `6d6ff646…b367b252a` (unchanged in this diff), and CP-6 is green against it.
- **The pins match a recomputation from the tree.**
  - `Get-NightlyPolicyHash` (nightly-common + nightly-policy) gives `99f1ff45…fffdc6`.
  - `scripts/test-live-lane-run.ps1 -PrintInputDigest` gives `cd5378ad…3c318c3`.
  - Both equal the committed values. The reseal method is the standard one, so a replay onto master's CARD-1744 pins (`9f2b4b77`/`1bc080d7`) recomputes them the same way.
- **The CP-6 floor change from 118 to 114 is a count correction.**
  - `TaskPlatformGuidanceTests` has five `[Test]` methods at both the subject tip and master, and this branch never touches that file.
  - The CP-6 TRX has 115 results: 114 passed and one `NotExecuted`, which is `C1210_Windows_linux_only_outcomes_are_exact` (the planned Linux skip). Five of the results are `TaskPlatformGuidanceTests`.
  - The value 118 first appeared in the intermediate commit bf8b1ba2 and was never correct. No test was dropped.

## Runs (one checkpoint-tool run, 20261119-003215-c395, at 9ec1fd73, sourceState=clean buildSource=verified, slot=granted on every row)

The run used the plan's CP-1 to CP-6 plus a reviewer row, CP-7, written to a gitignored copy of the plan at `.antiphon/review-7a8c54aa-plan.md`.

| Row | Filter | executed / passed / failed / skipped |
|---|---|---|
| CP-1 | BuildSlotScriptTests/C1981_LeaseTempDirConfinesSdkScratch | 1/1/0/0 |
| CP-2 | C1981LeaseTempDriverTests/* | 2/2/0/0 |
| CP-3 | C1677TempSweepNativeTests/C1981_dotnet_sdk_empty_accepts_only_the_exact_shape | 1/1/0/0 |
| CP-4 | C1677TempFamilyRegistryTests/Every_family_producer_still_contains_its_prefix | 1/1/0/0 |
| CP-5 | C1677TempSweepTests/Unregistered_random_and_process_names_aggregate_by_prefix | 1/1/0/0 |
| CP-6 | guard union, 11 classes (includes the five-class guard batch) | 114/114/0/1 (C1210 planned skip) |
| CP-7 | ReleaseGatePlatformTests/(C1686_LinuxOnlyCensus*)\|(LinuxOnlyWindowsExclusionPolicy*) | 2/2/0/0 |

- **Validation.** `validate --rows CP-1,CP-2,CP-3,CP-4,CP-5,CP-7 --expected-source-sha 9ec1fd73…` reports VALID for 6 rows. Validating the whole run refuses with `row_failed`, but only because of CP-6's planned skip.
- **Cleanup.** No `/tmp/aslot-*` directory remained after the run.
- **Evidence diff.**
  - `check-evidence-diff.ps1` over `05802489..9ec1fd73`: commits=5 entries=0 violations=0.
  - Over `6414a719..9ec1fd73`: commits=10 entries=0 violations=0.
- **Build outside the slot gate.** The reviewer built `tools/Antiphon.Checkpoints` with `dotnet run -- import` outside a slot, to validate the plan table before starting the run. That was a tool build, not a test run.

## Merge-tree against master b2547853 (the land replay must resolve these)

`git merge-tree --write-tree origin/master 9ec1fd73` reports conflicts in:

- **`policyHash` and `PinnedInputDigest`.** Reseal both on the combined tree.
- **The tail of `tests/test-execution-policy.json`.** Master appended a row for `C1744G1DesktopWinComposeTests` and this branch appended a row for `C1981LeaseTempDriverTests`. Keep both rows.
- **`LandVerificationBuildGraph.{evaluation,manifest}.txt`.** These records are regenerated at land.
- **The registry count in `tests/Antiphon.SessionRunner.Tests/C1677TempFamilyRegistryTests.cs`.** Master asserts 20 and this branch asserts 13. The auto-merged `RunnerTempSweep.RegisteredFamilies` has **21** entries (master's 20 plus `dotnet-sdk-empty`). The resolution must be `ShouldBe(21)`, not either side's value. Round 1 already reported this conflict.

The `RunnerTempSweep.cs` changes from both sides merged without conflict.

## Advisories (cards; do not hold the land)

**A-1. CP-1's last assertion reads all of host `/tmp`, so it is flaky on a shared host.**
- **Where:** `scripts/test-build-slot.ps1`, `Test-C1981_LeaseTempDirConfinesSdkScratch`, assertion "parent /tmp gains no six-character directory".
- **Failure:** The test compares the six-character directories in all of `/tmp` before and after the run. Any other process on the host that runs `dotnet` without a lease can create one during that window: another task's direct build, the checkpoint tool's own `dotnet run`, or the runner. That is the red the fixer reported once.
- **Why:** The other four assertions already prove the confinement. The child's TMPDIR has the lease shape, the logged SDKDIR is gone, and the lease directory is gone.
- **Fix:** Assert that the logged `SDKDIR` matches `^/tmp/aslot-[0-9a-f]{8}/[A-Za-z0-9]{6}$`. Either drop the global diff, or limit it to new names whose owner and ctime fall inside the wrapper's own pid window.
- **Impact:** The class is `[Category("Integration")]`. I found no sign that it runs in Merge's short verification, so this can turn a Linux nightly or rc unit-lane run red but not a land.
