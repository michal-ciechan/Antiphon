# Incremental rc S3 owner-map checkpoints (Code task 1f7120d3, redesign 2dda603e)

Every tracked path is one of three classes. Owned: project closure
(directory, ProjectReference, Import, Compile/Content/None/EmbeddedResource/
AdditionalFiles/Analyzer), a census-proven reader, or an explicit root.
No-op: a row in `scripts/lib/rc-input-noop.json`, which selects nothing and
raises no flag. Unowned: everything else, including a csproj nobody
references. Unowned selects nothing, is reported with a hint, and sets
`needsOrchestratorDecision`. While unowned paths or unresolved closure flags
remain, the cut records `creditBlockedBy=unowned-paths`. `gateCredit` stays
false. `unmatched-path-fallback` and `unresolved-conservative-all-inputs`
are not applied. A historical `**` row with that reason or with consumers
`*` is ignored. A `**` row with reason `repo-walk` and named suites selects
those suites for every non-no-op path. No production project is a repo-walk;
the walks table says `repo-walk=none`.

`RcInputOwnersMapTests` reads the committed map. A changed Compile link selects
the including project. An unlinked sibling selects the project that contains
the file. `release-selection.ps1` is unchanged and still emits Full.
Activation stays `shadow-only`.

D-1 (review 0b1b4c83): non-code reader roots (`docs/**`, `**/*.md`,
`scripts/**`, `docker/**`, `.claude/**`, `**/docker-compose*.yml`,
`**/Dockerfile*`, `client/**`) select every suite until CARD-1820 item 1
supplies per-suite edges. `antiphon.areas.json`, `.editorconfig`,
`.github/**`, `tests/derived-impact-index.json`, and
`tests/impacted-classes.json` are explicit forced-full roots.
`**/NuGet.config` matches without case. Cross-suite fixture reads in the
checked-in census `scripts/lib/rc-input-reader-census.json` select at least
the listed suites. Compile, ProjectReference, and link selectivity for source
files stays. Windows is unobserved.

Round 2 (Code task 1e0a0939): the literal scan is a lower bound. Narrow rows
the scan contradicts were removed. Round 4 (Code task 2dda603e) adds the
directory walk. `tests/Antiphon.Tests/**` selects GitInProcess.Tests,
Resilience.Tests, and Server.Git.Tests (`antiphon-tests-source-scan`).
`src/Antiphon.Messaging.Service/Migrations/**` selects Antiphon.Tests
(`messaging-migration-walk`). `src/**/*.cs` and `server/**/*.cs` select
Antiphon.Tests because `DerivedImpactIndexBuilder` lists those files
(`derived-impact-source-walk`). `docs/testing-and-build.md` also carries
`doc-contract-reader` for Antiphon.Tests. `docs/reviews/**` and
`docs/superpowers/reviews/**` are no-op rows. `scripts/rc-input-reader-census.ps1`
runs the literal scan plus the directory walk with broadening disabled and
exits 1 when any pair is under-selected, a project is unanalysable, or a
proven pair is classified no-op. It prints `UNOWNED-COUNT` and writes
`.antiphon/rc-unowned-paths.txt`. Unowned paths do not by themselves fail
the census. CP-1 floor is 32 methods. CP-2 stays 81 and CP-3 stays 27.

`WindowsUnitPlatformAdmissionTests` stays off this Linux selection.
`C1210_Windows_linux_only_outcomes_are_exact` skips on Linux, and a skip is
not a pass. CP-2 is the nine-class guard union, floor 81, and the single
`TestClassificationPolicyTests` run. CP-3 runs only after
`tests/derived-impact-index.json` includes the new class. No migration.
No positive-control row. Mutation execution is waived (operator 2026-11-12)
and is not discharged.

The nightly `scripts` suite has no csproj, so it is not an owner-map consumer.
`scripts/**` selects every suite, and `scripts/lib/**` stays forced full.

The checkpoint tool's own row deadlines govern these rows. A test-host
`--timeout 570s` is not passed to the tool: that cap would kill the 15-minute
first row. `bin-s3-owners/` contains no `ref` substring.

## Verification design

- V-1. `RcInputOwnersMapTests`, `RcInputManifestTests`, and `RcChunkInputDigestTests`. 32 methods.
- V-2. Nine-class guard union, without `WindowsUnitPlatformAdmissionTests`, floor 81. This is the single `TestClassificationPolicyTests` run. `C1729LayeringGuardTests` stays at VIOLATIONS 0 LISTED 162.
- V-3. `DerivedImpactIndexTests` alone, floor 27.
- V-4. `scripts/audit-land-verification-graph.ps1 -Check`.
- V-5. Nightly policy hash and pinned input digest recomputed against the committed pins. No re-pin unless a guard fails.
- V-6. `scripts/rc-input-reader-census.ps1` with broadening disabled. Exit 0 and `under-selected=0`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | s3 | `tests/Antiphon.Tests -> bin-s3-owners/` | s3-owners | `/*/*/(RcInputOwnersMapTests*)\|(RcInputManifestTests*)\|(RcChunkInputDigestTests*)/*` | V-1 | 0 failed, executed at least 32 | 32 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | s3 | CP-1 | s3-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-2 | 0 failed, executed at least 81 | 81 | 9 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | s3 | CP-1 | s3-index | `/*/*/DerivedImpactIndexTests/*` | V-3 | 0 failed, executed at least 27 | 27 | 9 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | s3 | n/a | s3-land | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | V-4 | exit 0 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | s3 | n/a | s3-pins | `pwsh -NoProfile -Command '. ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-policy.ps1; $p = Read-NightlyExecutionPolicy -RepoRoot (Get-Location).Path; $d = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $q = [char]39; $m = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), ("PinnedInputDigest = " + $q + "([0-9a-f]{64})" + $q)); if (-not $m.Success) { throw "pin line missing" }; $pin = $m.Groups[1].Value; $quote = [char]34; $legacyPattern = "LegacyExclusionDigest = ${quote}([0-9a-f]{64})${quote}"; $legacy = [regex]::Match((Get-Content -Raw ./tests/Shared/NativeLinuxPolicyWitness.cs), $legacyPattern).Groups[1].Value; Write-Output ("policyHash=" + $p.Hash); Write-Output ("inputDigest=" + $d); Write-Output ("pinnedInputDigest=" + $pin); Write-Output ("legacyExclusionDigest=" + $legacy); if (-not $legacy) { throw "legacy pin line missing" }; if ($d -cne $pin) { throw ("input digest drift computed=" + $d) }'` | V-5 | exit 0, printed digest equals the script pin | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | s3 | n/a | s3-census | `pwsh -NoProfile -File scripts/rc-input-reader-census.ps1` | V-6 | exit 0 and under-selected=0 | n/a | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
