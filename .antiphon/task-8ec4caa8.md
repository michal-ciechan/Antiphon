# CARD-1820 S1 Review, round 1 (Final): derived reverse-reference index

Subject: origin/feat/card-task-6fde97f6 at 238af3ff3e300f0e13fae828d5286c97cf8e65a8 (Code owner task 6fde97f6, Grok author). Range 9e050cf96..238af3ff3. Master da4f168de5a771adf5dab22336826d19b4ec613b.

Verdict: FOUR BLOCKERS (D-1..D-4). The rows run green. The index costs Merge far more, misses the three catches the plan claims, and its drift witness goes red on Windows by construction.

## Evidence (fresh, this review)

Checkpoint run `20261110-011019-a361` from the `.antiphon/r8ec-plan.md` extract, one build (672 s cold, loaded server2). `validate` printed `CHECKPOINT SOURCE VALID source=238af3ff3 rows=3`.

| Row | Filter | executed / passed / failed / skipped |
|---|---|---|
| CP-1 | `(DerivedImpactIndexTests*)\|(ImpactedClassesSelectorTests*)` | 37 / 37 / 0 / 0 (DerivedImpactIndexTests 301 s, ImpactedClassesSelectorTests 224 s) |
| CP-R1 | `(MergeHighRadiusSubsetTests*)`, a touched class | 6 / 6 / 0 / 0 |
| CP-R2 | guards `TestClassificationPolicy*`, `TestClassificationGuard*`, `SlowTestRegistryMerge*`, `SlowTestTripwire*`, `NativeLinuxPolicyWitnessTests*`, `TestLaneCategoryGuardTests*`, `TaskPlatformGuidanceTests*` | 58 / 58 / 0 / 0 |

- `check-evidence-diff.ps1 -Repository . -BaseRef 9e050cf96 -HeadRef 238af3ff3`: commits=5 entries=0 violations=0.
- `git merge-tree --write-tree origin/master 238af3ff3`: clean, tree c3d488121e67.
- The diff changes no policy JSON, migration or stage bundle.
- Committed index: 603,372 bytes, LF only, 2142 edges, 151 cappedPaths, no timestamp. The rebuild-and-compare witness took 28.8 s. Linux drift is green.
- Selector wall time per call on loaded server2: branch 11-29 s, master 9-22 s. Both are under the 60 s budget.
- Importing the branch `-MergeSubset` rows for a54a6d4f, ae5b545d9, 0f76e7b0a9, dcb973d8 and the no-range case gave 7, 7, 6, 5 and 5 rows with 0 warnings.
- Not run within the 40-minute budget: `land-graph -Check` and the V-1..V-6 mutants. The mutants are PCs and stay pending for SourceLanding Mutation.

## Selector before and after (master vs branch selector on the same tree)

| Range | Merge classes | Merge filters | Non-merge classes | skippedByCap |
|---|---|---|---|---|
| none | 15 -> 15 | 5 -> 5 | n/a | - |
| ff7cceaf | 67 -> 67 | 7 -> 7 | 55 -> 55 | - |
| dcb973d8 (Quiet) | 15 -> 19 | 5 -> 5 (tests row widened) | 2 -> 6 | - |
| a54a6d4f (remote-scripts) | 16 -> 51 | 6 -> 7 | 1 -> 36 | - |
| ae5b545d9 | 67 -> 75 | 7 -> 7 | 62 -> 70 | AgentTaskLandNotificationService.cs, AppDbContext.cs, MigrationTestDatabase.cs |
| 0f76e7b0a9 / dff78584d6 | 19 -> 41 | 5 -> 6 | 4 -> 26 | GitSettings.cs, LandingGit.cs |
| 9e050cf96 (recent normal) | 15 -> 16 | 5 -> 6 | 0 -> 1 | - |

The fixed classes, fixed methods and fixed filters are byte-identical in every Merge output. The 44/34/104 land floors still hold.

## Defects

### D-1 Blocker: the derived index feeds `-MergeSubset` with no cost control
- Where: `scripts/impacted-classes.ps1` (`Apply-Derived $derivedSelection $wholeSubset $subsetMethods` in the MergeSubset branch). `ImpactedClassesSelectorTests.cs:211-217` and `MergeHighRadiusSubsetTests.cs` (`QuietTestsFilter`, `RemoteTestsFilter`, `AssertImpactedMethods`) were rewritten.
- Failure: on the a54a6d4f remote-scripts range the Merge subset grows from 16 to 51 classes. The CARD-1801 five-method `CP-merge-subset-RemoteScriptContractTests` row is replaced by the whole `RemoteScriptContractTests` class, about 67 min serial on server2. `RollingVolumeRecycleScriptTests`, the `VolumeRetirement*` classes and `Hangfire*` are added as well. Remote, Rolling and VRHP together have overrun 90 min before. On dff78584d6 the Merge subset grows from 19 to 41 classes, and the Quiet range from 15 to 19.
- Why: the brief says the index feeds the Code/Review output and only cost-capped rows into Merge. The Merge fixed rows are about 10 min. The plan says the `ImpactedClassesSelectorTests` pins stay "Unchanged". Code edited those pins to accept the growth. It did not justify or measure the growth.
- Fix: keep derived classes out of the `-MergeSubset` payload, or admit only a measured, cost-capped allowlist. A derived whole-class hit must not erase a hand method row under Merge. Restore the a54a6d4f method pin and the original Quiet/Remote Merge assertions.

### D-2 Blocker: the cap drops the plan's own replay catches, and the replay witness cannot go red on that
- Where: `DerivedImpactIndexBuilder.cs:142` (`names.Length > Cap`, which counts hop and type-name edges together), `tests/derived-impact-index.json` cappedPaths, and `DerivedImpactIndexTests.s1_replay_matches_declaration_and_index_edges`.
- Failure: CP-1 printed `REPLAY 4 RepositoryMutationLeaseTests selected=False`, `REPLAY 6 PreDispatchFailureDeliveryTests selected=False` and `REPLAY 7 C1677TempFamilyRegistryTests selected=False`. The reason is that `LandingGit.cs` (64 classes), `AgentTaskLandNotificationService.cs` (36) and `PtyHostLeakSweep.cs` (31) are all capped. A probe with `-Path server/Infrastructure/Git/LandingGit.cs` selects 0 derived classes. The replay test asserts selection only when an edge exists, so a capped target passes silently. `committed_index_matches_the_builder` even pins LandingGit and PtyHostLeakSweep as capped.
- Why: those three rows are the plan's whole case for S1's index edges (Replay rows 4, 6, 7). The brief names RepositoryMutationLeaseTests under a LandingGit change as a required catch. V-2 and V-3 pass only on 2-class fixture repos.
- Fix: do not let the generation cap drop direct type-construction or dotted-script edges from the Code/Review output. One option is to cap only one-hop or name-only edges, or to apply the cap only where Merge reads the index. Make the replay witness assert that rows 4, 6 and 7 are selected. Raise this with the plan owner if the cap design itself must change.

### D-3 Blocker: the drift witness is red on Windows by construction
- Where: `DerivedImpactIndexBuilder.cs:32-36` and `:170`, `.gitattributes`.
- Failure: `JsonSerializerOptions { WriteIndented = true }` with no `NewLine` writes `Environment.NewLine`, which is CRLF on Windows, and then a literal `"\n"`. The committed file has no `eol=lf` attribute. Under `* text=auto` a Windows checkout gets CRLF throughout. On Windows the bytes of `committed_index_matches_the_builder` never match: the builder emits a final LF after CRLF lines, and the checkout has CRLF or LF depending on settings. The new Unit tag puts this class in the Windows Unit lane and rc qualification.
- Why: the brief requires "LF endings on Windows checkouts too". The OS-branch witness rule applies. This was found by reading the code. Nothing was run on Windows, because the brief forbids the desktop.
- Fix: set `NewLine = "\n"` on `JsonOptions` and add `tests/derived-impact-index.json text eol=lf` to `.gitattributes`. Optionally, have the witness assert that there is no CR.

### D-4 Blocker: derived output puts Windows-skip classes on Linux filters (CARD-1819 rule)
- Where: `Resolve-Derived` in `scripts/impacted-classes.ps1`, with no platform census.
- Failure: `WindowsUnitPlatformAdmissionTests` enters the dcb973d8 Merge `CP-merge-subset-tests` row, and `QuietTestsFilter` now pins it there. Its `C1210_Windows_linux_only_outcomes_are_exact` throws `SkipTestException` off Windows (`WindowsUnitPlatformAdmissionTests.cs:493`). `C1008PlatformContractTests` (`:104`, `:113` skip off Windows) enters a54a6d4f's Merge and non-merge rows. A Linux row with skipped>0 fails `validate` (row_failed), so these Merge receipts can never validate.
- Fix: drop derived classes that skip off Windows from the Linux filters, or route them to `windowsFilters`, using a census witness. The other 18 platform-excluded derived classes are Linux-only (`platform=windows` exclusions), which is fine on Linux.

## Advisories (do not hold a land)
- A-1: `C1514LandingSplitBudgetTests` (C1584_E2EExpandedBudget) has 0 index edges, so a `tests/Antiphon.E2E` change does not select it. The plan gives this to the S3 roster (`c1584-e2e`). Track it there.
- A-2: Broad paths behave as the plan says. `Antiphon.sln`, `Directory.Build.props` and `Directory.Build.targets` each select 0 derived classes and appear in `skippedByCap`. The one `Directory.Build.targets` class comes from a hand kind.
- A-3: the CP-1 Min was raised from 25 to 37 by editing the committed plan, which is allowed (raise only).
