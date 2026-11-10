# CARD-1819 Review round 3 (task 0fe759b9)

Subject: `origin/feat/card-task-eaca6643`. The repair is task eaca6643 (Grok author) on Code owner
`43bdb0ea-3dba-4b29-848d-ee1b8d84902b`. Reviewed tip: `934c4056d16b19989e8162e865bb0ebe401f4775`. The tested
code is `d196ac954`, and the tip adds only the Code report. Round 3 is blockers only and covers the diff since
`b6b6e9075`. Master is `da4f168de5a771adf5dab22336826d19b4ec613b`.

Verdict: **CLEAN, no blockers.** Round 2's B-1 is closed. Four advisories follow; they go to cards and do not
hold the land.

## (1) RepositoryMutationLeaseTests on Linux

- The checkpoint run `20261110-011020-424a` at `934c4056d` is GREEN. It records `sourceState=clean
  buildSource=verified`, and `validate` gives `CHECKPOINT SOURCE VALID source=934c4056d... rows=3`.
  - CP-1, `/*/*/(RepositoryMutationLeaseTests*)/*`: executed=32 passed=32 failed=0 skipped=0. At `b6b6e9075`
    it was executed=32 skipped=2 RED. Build 671 s (cold), tests 26 s.
- The move is byte-identical. All 113 removed non-blank lines (V28, `OutputHoldingGit`, V13) appear in the
  same order in `RepositoryLeaseWindowsBoundaryTests.cs`, and the copied `RunChildAsync` is identical to the
  original. The new class carries `[Category("Integration")]` and `[ParallelLimiter<ProcessSpawnLimit>]`,
  which match the source class.
- Placement: `-Json -Path server/Infrastructure/Git/LandingGit.cs` puts `RepositoryMutationLeaseTests` and
  `ServerGitProcessTests` on the Linux filter. Its windowsFilters entry is
  `/*/*/(RepositoryLeaseWindowsBoundaryTests*)/*`.

## (2) Census witness

- **Red at the round-2 inputs.** I restored the `b6b6e9075` copies of `tests/impacted-classes.json` and
  `RepositoryMutationLeaseTests.cs` and ran only
  `ImpactedClassesSelectorTests/C1819_linux_kind_classes_do_not_skip_off_windows` (lease 4839bbe5, held 21 s).
  Result: total 1, failed 1. Violations:
  `["RepositoryMutationLeaseTests/C448_V13_WindowsJunctionAndOtherProcessShareTheLease",
  "RepositoryMutationLeaseTests/C448_V28_ExitedRootKeepsItsJournalWhileADescendantOwnsOutput"]`.
- **Green at the tip.** The CP-2 TRX shows it Passed.
- **Defeat probes.** I added 17 scratch classes under `tests/c1819-r3-probe/` and a temporary Linux kind,
  without a rebuild (lease f491a1e3). Both changes are reverted, and `git status` is clean.
  - Caught (3): a direct `!IsWindows` throw, `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` with
    `Skip.Test`, and a helper in the same class. The census names the helper itself.
  - **Not caught (14):**
    - a helper in another class that throws `SkipTestException`
    - a helper in another class that calls `Skip.Test`
    - a helper in another class that asserts `IsWindows()`
    - a renamed bool predicate followed by `.ShouldBeTrue()`
    - a local `var windows = IsWindows(); if (!windows)`
    - `IsWindows() == false`
    - `Skip.Unless(IsWindows())`
    - `Skip.When(IsLinux())`
    - `if (IsLinux()) throw SkipTestException`
    - a skip in the constructor
    - an expression-bodied ternary throw
    - a skip in a field initializer
    - a `SkipAttribute` subclass on the class
    - a `SkipAttribute` subclass on the method
- **Exhaustive scan.** I scanned the 58 classes on Linux-running kinds (`linux` and `windows: required`),
  in 107 source files. No class skips on Linux through any shape, direct or indirect. Every hit is one of:
  - a Linux-only gate: `NativeLinuxOnly`, `C1008HostFixture.RequireNativeLinux`, or `!IsLinux` in
    `CheckpointReceiptSafetyTests` and `CheckpointTerminalReceiptTests`
  - a skip that fires on Windows: `LandGraphEvaluation*`, or the WSL check in `RemoteScriptContractSupport`
  - the root-only mode-000 probe; server2 runs as uid 1654
  - `RemoteScriptContractTests` `RequireLinuxJq`/`RequireLinuxPwsh`, which the five method rows do not call
  - `!IsWindows` blocks that only call `SetUnixFileMode`

  Under the brief's rule, the 14 uncaught vectors are an advisory (A-1).

## (3) Ranges, land rows, import

I ran `-MergeSubset -Json` at the tip and recomputed every range. They match the Code report:

| Range | classes / Linux filters / windows filters |
|---|---|
| none | 15/5/0 |
| ff7cceaf | 67/7/0 |
| dcb973d8 | 18/5/0 |
| a54a6d4f | 17/6/0 |
| ae5b545d9 | 73/8/2 |
| 0f76e7b0a9 | 21/5/1 |

- No Linux filter names `RepositoryLeaseWindowsBoundaryTests`, V28/V13, `CapabilityScriptTests`,
  `DelegateScriptCapabilityTests` or `AgentTaskLandDeliveryE2ETests`.
- land-1 and land-2 are unchanged. Each string occurs once, verbatim, in `docs/orchestration-loop.md`.
- `import --plan` on each range's Linux rows imported 5, 7, 5, 6, 8 and 5 rows, with 0 warnings and 0
  land-row warnings.

## (4) Guards and gates (all at `934c4056d`, same run)

- CP-2, `ImpactedClassesSelectorTests` and `MergeHighRadiusSubsetTests`: 46/46, 0 skipped.
  `ImpactedClassesSelectorTests` took 383 s and `MergeHighRadiusSubsetTests` 66 s.
- CP-3, the full guard union: 78/78, 0 skipped. It covers TestClassificationPolicy, TestClassificationGuard,
  SlowTestRegistryMerge, SlowTestTripwire, NativeLinuxPolicyWitness, MechanicalPcContract,
  TaskPlatformGuidance, C1729LayeringGuard and TestLaneCategoryGuardTests.
- `check-evidence-diff`:
  - `b6b6e9075..934c4056d`: exit 0, commits=3, violations=0.
  - `9e050cf96..934c4056d`: exit 0, commits=11, violations=0.
- `git merge-tree --write-tree origin/master(da4f168de) 934c4056d`: exit 0, tree `092bcf810d26`.
- `audit-land-verification-graph.ps1 -Check -ToolPath <native Release dll>`: exit 0, `approved`, digest
  `3ED4A38C...EB46`, count 75. Lease ec379d61, held 21 s.
- Other leases: native test build 2a953255 (held 380 s), checkpoint tool build 80d42998, LandGraphAudit build
  5832214f. The run reports `unlisted: none`.

## Advisories (cards, never land holds)

- **A-1:** the census catches only a skip written directly in a method of the listed class. It misses the 14
  shapes above, including cross-class helpers, `Skip.When`/`Skip.Unless`, positive `IsLinux` skips,
  constructor and initializer skips, and `SkipAttribute` subclasses. No existing class uses any of them.
- **A-2:** the census skips `windows: required` kinds, but the selector also puts those classes on the Linux
  filter. For example, `ServerImagePushCredentialTests` and `ServerRepositoryMirrorScriptTests` are in the
  `0f76e7b0a9` Linux `CP-merge-subset-tests`. A Windows skip added to one of the 8 required classes would
  go unwitnessed. None skips today.
- **A-3:** the census matches a Linux skip only through `IsWindows`. A skip phrased as "skip when Linux"
  (`if (IsLinux()) skip`) has the same red effect and is not checked.
- **A-4:** round 2's A-1 stands: the migration row's `EstimatedMinutes` of 60 triggers the timeout advisory.
  It is master doc policy.

## Owed / pending

- **Native Windows lane:** `RepositoryLeaseWindowsBoundaryTests` (V28, V13), `CapabilityScriptTests`,
  `DelegateScriptCapabilityTests` and `AgentTaskLandDeliveryE2ETests`. The Windows causes of the 8
  `windows: required` classes are owed too.
- Every PC stays pending for method-scoped SourceLanding Mutation.
