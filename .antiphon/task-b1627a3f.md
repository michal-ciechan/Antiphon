# CARD-1814 Review round 1 (Final/Full): CLEAN

Review task b1627a3f. Subject: Code 3a284cc6 (`3a284cc6-dfb8-4b06-b620-cf0f4c0d89ef`), Grok author, branch
`feat/card-task-3a284cc6`. Red `1b9dc9918a0f5d6ba4e8cdd2ed2d1d448e148352`, fix/tested
`7473ef10b037e767b7a0da8778fcd279c08144b9`, report tip (reviewed) `3dba93626ef2fcfa55f385997bc804c09244d376`.
Base `364e271331fc1af83f5b7eabaa956a8c0a289a5f`. Current origin/master `25b347f493cb781b693abc450e7090491c8638ae`.

Verdict: CLEAN. No blockers, no regressions.

## Checks

1. **Skips and audit.** The five methods in the row each call `WriteBlob`. `WriteBlob` calls `Skip.Test` off
   Windows before `ProtectedData.Protect`. `missing_capability_file_does_not_advise_AllowedRoots` never calls it.
   The diff to the class only adds the witness and two usings, so the Windows bodies are unchanged.
   `CapabilityScriptTests` is untouched apart from its roster entry.
   Grep over `tests/**` for `ProtectedData|DataProtectionScope|DPAPI`:
   - Only `DelegateScriptCapabilityTests` and `CapabilityScriptTests` call DPAPI.
   - `DelegationCapabilityApiTests` only builds a `.dpapi` path string.
   - The csproj only has the package reference.
   - The policy, witness, roster and live-lane fixture files only contain data.

   `[SupportedOSPlatform("windows")]` also appears in OwnedProcessDirectoryCleanup, PtyHostLeakSweep,
   AgentTuiSecretProtectorTests, RunnerProcessProbeTests and OperatorTokenFileTests. In each one it marks a
   private helper behind an `OperatingSystem.IsWindows()` branch. Only the two capability classes carry it at
   class level. No DPAPI class is missing a gate.
2. **Policy rows.** A semantic JSON diff against the base shows rc exclusions went 187 -> 188: one row added,
   none removed. Apart from `policyHash`, the rest of the JSON is byte-identical. There are now 45
   method+platform rows, all with one shape (`suite,class,methods,platform,reason,owner`), the same as the
   CARD-1811 row. The reason does not carry the `Linux-only by construction:` prefix. It is one record with five
   methods, so the legacy count goes 92 -> 93, not 97. The witnessed count stays 95.
3. **Witness red.** At `1b9dc9918` the policy has no `DelegateScriptCapabilityTests` row (grep count 0), so
   `rows.Length.ShouldBe(1)` fails deterministically. The platform mutant fails on `platform`. The skip-deletion
   mutant fails on Linux because `ProtectedData` throws PlatformNotSupportedException, which is not
   SkipTestException. I confirmed this by reading the code and accepted the Code red evidence. I did not rerun
   the red: the PC stays pending for method-scoped SourceLanding Mutation. Roster: card A-2 said "remove or
   update". The CapabilityScriptTests entry was updated (3 methods, sha `f3284bb2…` = actual file sha256). The
   DelegateScriptCapabilityTests entry also matches (7, `99cba2db…`).
4. **Pins, recomputed with repo tools.**
   - At the tip: `Read-NightlyExecutionPolicy` gives hash `70cb2f24…8898`, matching the stated value.
     `-PrintInputDigest` gives `c618d0cd…9bfb`, matching the pin.
   - LegacyExclusionDigest `3d964fa8…c115` is proven by NativeLinuxPolicyWitnessTests green in CP-3.
   - Scratch merge onto origin/master `25b347f49`: merge-tree exit 0, tree `5b0be362663cde640e1b3bddfeb1a865169665aa`.
     The same three values hold on the merged tree.
   - **A Merge needs to re-pin nothing** against `25b347f49`, which touches only `LandNonCodeSkipTests.cs`.
     `d15459456` and `364e27133` are already in the base. If master gains a change to any of the six live-lane
     protected inputs or to an rc exclusion before land, re-run `-PrintInputDigest`, the policyHash reader and
     NativeLinuxPolicyWitnessTests on the merged tree. Those inputs are `scripts/lib/nightly-{common,policy,coverage,core}.ps1`,
     `tests/test-execution-policy.json` and `scripts/fixtures/nightly/c487-probe/discovery.diag`.
   - land-graph `-Check` with a native Release evaluator: `approved` exit 0 on the merged scratch tree and on
     the branch tip. No refresh was needed and there were no property/construct refusals.
   - check-evidence-diff: this mirror is not shallow. Over `364e27133..3dba93626`: commits=3 entries=1
     violations=0, exit 0.
5. **Docs.** The release-gates.md paragraph and the investigation Follow-up match the diff. The 92 -> 93 count
   and the three digests are correct.

## Checkpoint run (tool, one run)

There is no plan `### Checkpoints` for this follow-up card. I ran an ignored extract,
`.antiphon/review-1814-plan.md`: run `20261109-130215-c75c`, one build, GREEN, exit 0.
`validate --expected-source-sha 3dba936…` gave CHECKPOINT SOURCE VALID rows=5. Every row reported dirty=0,
sourceState=clean and buildSource=verified.

| CP | Filter | executed/passed/failed/skipped |
|---|---|---|
| CP-1 | DelegateScriptCapabilityTests/(missing_*)\|(C1814_*) | 2/2/0/0 |
| CP-2 | CapabilityScriptTests/(error_paths_*)\|(C1811_*) | 2/2/0/0 |
| CP-3 | guards: TestClassificationPolicy, TestClassificationGuard, SlowTestRegistryMerge, SlowTestTripwire, NativeLinuxPolicyWitness, NativeLinuxPolicyWitnessMutation, NativeLinuxPolicyMatcherParity, MechanicalPcContract, TaskPlatformGuidance, C1729LayeringGuard | 90/90/0/0 |
| CP-4 | ReleaseGatePlatformTests/* | 6/6/0/0 |
| CP-5 | LiveLaneRunnerTests/* | 4/4/0/0 |

The five Linux-skipped DPAPI methods are kept out of rows because the tool fails a row that has skips. They
are exercised through C1814 in CP-1, which asserts that each one throws SkipTestException with "DPAPI". The
same applies to the CARD-1811 method through C1811 in CP-2.

Other builds, each with a stated reason: one build-slot lease, `dotnet build tools/Antiphon.LandGraphAudit -c Release`
(17 s), for the `-Check` evaluator.

## Advisories (no land hold)

- A-1: C1814 pins five named methods. A future `WriteBlob` caller added without a row is not caught, because
  there is no census of `WriteBlob` callers.
- A-2: NativeLinuxPolicyWitnessTests compiles into four suites, and only Antiphon.Tests ran it here and in
  Code. The legacy digest is computed over the whole exclusion array with one shared constant, and the new row
  is not Windows-prefixed, so the other three suites cannot differ. Their run is left to nightly.
- A-3: the card A-1 says "DPAPI / ProtectedData / Windows-only APIs". The audit covered DPAPI only. Many other
  `if (!OperatingSystem.IsWindows()) Skip.Test(...)` classes exist, such as C0824DesktopLegacyReleaseNativeTests,
  CheckpointWindowsAnchored*, EvidenceFolderTests, LandWorkspaceTests and LandingRemovalPolicyControlTests.
  This review did not audit them for Linux rc rows. If the operator wants that scope, it is a separate audit card.
- Windows-native execution of the five bodies and of CARD-1811's method stays owed to the Windows rc lane.
  PC `C1814_Dpapi_cases_are_windows_required_and_linux_excluded` stays pending for Mutation.
