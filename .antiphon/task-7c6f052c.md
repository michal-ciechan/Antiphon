# CARD-1820 S1 Review, round 2 (Final, diff only): derived reverse-reference index

Subject: origin/feat/card-task-f4320f2f-rebased at 682375f5d54c4cf8328ff6810e1046c1ddca7fec (Grok Code author). Round-1 reviewed tip 238af3ff3; rebased range 72fada665..682375f5d (12 commits). Master 52e13fdca8dcb780ef4afd3ca9247bc32d28bfe4.

Verdict: ONE BLOCKER (D-1, the round-1 D-4 repair is incomplete). D-1, D-2 and D-3 from round 1 are fixed in behaviour. Both conflict resolutions keep both sides.

## Evidence (fresh, this review)

Checkpoint run `20261110-053419-7765` from the `.antiphon/r7c6-plan.md` extract, one build (441 s, loaded server2). `validate` printed `CHECKPOINT SOURCE VALID source=682375f5d54c4cf8328ff6810e1046c1ddca7fec rows=3`. The final `wait` call returned exit 2 after it printed `verdict: GREEN exit=0`. The receipt validates.

| Row | Filter | executed / passed / failed / skipped |
|---|---|---|
| CP-1 | `(DerivedImpactIndexTests*)\|(ImpactedClassesSelectorTests*)` | 56 / 56 / 0 / 0 (DerivedImpactIndexTests 586 s with 16 tests, ImpactedClassesSelectorTests 413 s with 40 tests), Min 41 |
| CP-R1 | `(MergeHighRadiusSubsetTests*)` | 6 / 6 / 0 / 0 |
| CP-R2 | guards `TestClassificationPolicy*`, `TestClassificationGuard*`, `SlowTestRegistryMerge*`, `SlowTestTripwire*`, `NativeLinuxPolicyWitnessTests*`, `TestLaneCategoryGuardTests*`, `TaskPlatformGuidanceTests*` | 58 / 58 / 0 / 0 |

The class run is 62 passed (56 + 6), which matches the Code claim. The guard batch is 58, not the 78 the brief quotes, and it is the same 58 that round 1 measured with the same filter.

- `check-evidence-diff.ps1 -Repository . -BaseRef 72fada665 -HeadRef 682375f5d`: commits=12 entries=0 violations=0.
- `git merge-tree --write-tree origin/master 682375f5d` (master 52e13fdca): clean, tree 82259c9abea6.
- `import --plan docs/superpowers/plans/2026-11-09-selector-v2-derived-index.md`: 5 rows, 0 warnings, exit 0.
- Committed index: 0 CR bytes. `git check-attr` gives `eol: lf` and `text: set`.
- Not run: `land-graph -Check`. The diff changes no project, csproj or solution file, and the round has a blocker, so I deferred it to the next round. The V-1..V-6 mutants are PCs and stay pending for SourceLanding Mutation. Instead of two V mutants I ran the index-edge mutant below and did the NewLine mutant by reading the code.

## Round-1 blockers

- D-1 Merge cost: **fixed.** `-MergeSubset -Json` gives byte-identical output on the branch and on master 52e13fdca for all six ranges (none 2990 B, ff7cceaf 8621 B, dcb973d8 3320 B, a54a6d4f 3635 B, ae5b545d9 9731 B, dff78584 4045 B). `MergeHighRadiusSubsetTests.cs` has no diff against master. `merge_subset_matches_the_hand_selector` pins the same comparison.
- D-2 cap catches: **fixed for selection.** The `-Path` probes select RepositoryMutationLeaseTests for `LandingGit.cs`, PreDispatchFailureDeliveryTests for `AgentTaskLandNotificationService.cs` and C1677TempFamilyRegistryTests for `PtyHostLeakSweep.cs`. All three come from the CARD-1819 hand kinds already on master (`repository-lease-reflected-constructors`, `land-notification`, `temp-family-registry`). The index ranks hand-covered classes last. The capped remainder is not dropped silently: JSON `owed[]` gives count 39, 12 and 7 and names the first 8; text mode prints an `owed/capped <path> count=N first=...` line. Of the 32 classes that construct `LandingGit`, 7 are left out of `derivedClasses` by the cap, and all 7 are named in the `first=` list. See A-1 for the replay witness.
- D-3 CRLF: **fixed in behaviour.** `JsonOptions.NewLine = "\n"` is set, and `.gitattributes` has `tests/derived-impact-index.json text eol=lf`. See A-2 for the witness.
- D-4 Windows-skip classes on Linux filters: **not fixed.** See D-1 below.

## Conflict resolutions (8e37d8b0c -> ee0b70226, 2d945ebfe -> 2970b3800)

Both CARD-1819 behaviours survive: `if ($kind.Windows -eq 'only') { continue }` in both the merge and the non-merge loops, and the E2E probe / Windows E2E split in `C1801_non_merge_land_notification_filter_names_every_class`. The derived selection is applied in the non-merge output only. No behaviour from either side was lost. The drift witness `committed_index_matches_the_builder` ran green in CP-1, so the committed index is byte-for-byte what the builder emits on the rebased tree.

## Defects

### D-1 Blocker: the windowsOnly census misses the common non-Windows skip guards, so derived output still puts Windows-skip classes on Linux filters (round-1 D-4 not fixed)
- Where: `tests/Antiphon.Tests/Scripts/DerivedImpactIndexBuilder.cs:653-672` (`IsNonWindowsGuard`/`ConditionIsNonWindows` match only the text `OperatingSystem.IsWindows()`, and only inside the test class itself). `tests/derived-impact-index.json` `windowsOnly`. `ImpactedClassesSelectorTests.WhyMissedTestsFilter` pins the defect.
- Failure: these guards are not recognised: `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)`, an `IsWindows` property or field, and shared helper gates such as `HeadedClaudeGate`, `RealCliStubGate` and `RemoteControlPtyLane`. Examples:
  - `-Path server/Infrastructure/Agents/Pty/CodexAdapter.cs` puts `(CodexAdapterLocalShellTests*)` on the `tests/Antiphon.Tests` Linux filter. All 7 of its tests throw `SkipTestException` off Windows. Master selects nothing for that path.
  - `-Path server/Infrastructure/Agents/Pty/RawPtyAdapter.cs` gives the Linux filter `(RawPtyAdapterTests*)`, where 11 of 12 tests skip.
  - `WhyMissedTestsFilter` pins `AgentSessionServiceIntegrationTests` on the Linux row; it has a `SkipIfNotWindows` (RuntimeInformation) test.

  A grep census found 31 classes that skip under one of these guards, are not in `windowsOnly`, and have index edges. Among them are SessionMessageQueuePtyIntegrationTests, GrokDelegateEndToEndTests, DelegateLaunchArgvIntegrityTests and FakeClaudeContractTests. Any Linux checkpoint row built from these selections has skipped>0, so `validate` fails it as row_failed. This is the round-1 D-4 failure mode.
- Why: round-1 D-4 and the brief require every class that skips off Windows to be `windowsOnly` and on no Linux filter (CARD-1819 rule). `windows_only_classes_stay_out_of_linux_filters` checks only the six Merge ranges, against the builder's own list, so it cannot see a class the census missed.
- Fix: make the census recognise `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` and local `IsWindows` aliases, and follow skip-helper calls (a same-file helper method, or a known gate type). Add a witness that greps every `SkipTestException` / `Skip.Test` site under a non-Windows guard and requires the owning `*Tests` class in `windowsOnly`. Regenerate the index, and move AgentSessionServiceIntegrationTests and the other affected classes from `WhyMissedTestsFilter` to the Windows lane.

## Advisories (do not hold a land)

- A-1: `s1_replay_matches_declaration_and_index_edges` does not depend on the index. Mutant: set `edges = []` in a scratch copy of the committed index. All 12 required replay rows still print selected=true, because hand kinds carry every S1 catch. The index ranking is still pinned on real data by the `owed/capped` count and `first=` pins in `capped_real_paths_keep_the_specific_class`, and by the drift witness. Rename the test, or add a real-data row that only an index edge selects.
- A-2: the D-3 witness `index_bytes_stay_lf_when_the_newline_source_is_crlf` cannot go red on Linux. `NewLineSource` is only null-checked (`DerivedImpactIndexBuilder.cs:212`) and never reaches serialization. If `NewLine = "\n"` is deleted, System.Text.Json falls back to `Environment.NewLine`, which is `\n` on Linux, so the test stays green. I did this mutant by reading the code, not by running it. The real witness is `committed_index_matches_the_builder` on the Windows Unit lane. Feed the injected newline into the serializer options, or assert `JsonOptions.NewLine == "\n"`.
- A-3: plan text is stale. Plan line 96 and V-5 say a path over the cap "is omitted from the index ... selects nothing". The builder now keeps the top 25 specific classes and lists the rest as owed. Amend the plan at the next Docs touch.
- A-4: an owed `first=` list can name a class that a hand kind already selects (C1677TempFamilyRegistryTests for `PtyHostLeakSweep.cs`). This is harmless but can be misread as a miss.

--- review evidence ---
subjectTaskId: f4320f2f-32f7-4aad-a3d7-8b7491ae08a0
reviewedSourceSha: 682375f5d54c4cf8328ff6810e1046c1ddca7fec
reviewedSourceClean: true
ordinaryScopeCompleted: Interim
reviewRound: 2
reviewedRange: 238af3ff3e300f0e13fae828d5286c97cf8e65a8..682375f5d54c4cf8328ff6810e1046c1ddca7fec
