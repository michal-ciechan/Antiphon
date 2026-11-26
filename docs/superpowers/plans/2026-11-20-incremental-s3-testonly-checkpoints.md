# Incremental rc S3 test-only class checkpoints (CARD-1820, task ef772e0a)

Base `8a89aa0e5715f86062bbcc7ee778810c2cf4cb3e`. The test-only class is one
object on `scripts/lib/rc-input-owners.json`. `"enabled": true` is the line
that turns it on. Setting that field to false restores the previous
selection. The evaluator reads `paths`, `exceptPaths`, `exceptReasons` and
`productEdgeReasons` from that object. It does not contain Antiphon path
literals.

A changed path is test-only when the class is enabled, the path matches
`paths`, it does not match `exceptPaths`, and no explicit owner root that
matches it carries an `exceptReasons` value. The checked-in paths are
`tests/**/*.cs`, `tests/**/Fixtures/**` and `tests/fixtures/**`.
`tests/Shared/**` is the exception path. Forced-full reasons
(`forced-full-build-graph`, `forced-full-policy`, `forced-full-selector`,
`forced-full-spawn-limit`, `forced-full-root`, `shared-test-infrastructure`)
stay owned. Policy JSON, the build graph and scripts do not match the
test-only paths, so they stay on their owner rows.

A diff confined to test-only paths selects nothing. The manifest records
`testOnlyDeferred` as `suite:path` for every suite the closure would have
dirtied. A product edge still selects the suite, which then runs whole:
`productEdgeReasons` are `Compile`, `ProjectReference`, `Import`, `Content`,
`None`, `EmbeddedResource`, `AdditionalFiles` and `Analyzer`. A reader row
on a path that is not test-only is a product edge because the path itself
is not deferred. `gateCredit` stays false and `activation` stays
`shadow-only`. `release-selection.ps1` is unchanged.

## Walker decision

`tests/Antiphon.Tests/**` with `"walker": true` names GitInProcess.Tests,
Resilience.Tests and Server.Git.Tests (`antiphon-tests-source-scan`). Those
three suites count or reflect test source:

- `GitInProcessAssemblyTests` walks `tests/Antiphon.Tests/**/*.cs` and
  counts class declarations (`CountClass`) plus `using Antiphon.GitInProcess`
  lines. It does not count a total of every class. A new file can still
  introduce a counted name or a using line.
- `ResilienceAssemblyTests` counts specific class names in the same tree.
- `ServerGitAssemblyTests` counts specific class names and method
  declarations (`void Name(` / `Task Name(`) in that tree.

A new class therefore can change a walker count. The selector does not try
to know which names are counted. An add or delete of a file, or an add or
delete of a `class` declaration, selects those walker suites. An edit that
leaves the file set and the class-declaration multiset unchanged does not.
That includes a method-body edit and a method-signature edit.
`ServerGitAssemblyTests` would notice a signature edit of a counted method.
The operator model stops at the class declaration, so those edits stay
deferred with the body. A class that exists only on the candidate is
`changedNotRun` as `suite:path:ClassName` on the suite whose project
directory contains the file. Deleting a class selects the walkers and does
not add a changed-not-run row.

## Advisories

A no-op row that matches an explicit owner root does not skip that root.
The manifest lists `noopConflicts` as `path reason=<reason>`, the owner
still selects, and `scripts/rc-input-reader-census.ps1` exits 1. The
review carve-outs stay no-ops because `docs/**` and `**/*.md` carry
`except` for `docs/reviews/**` and `docs/superpowers/reviews/**`, so those
roots do not claim the carved paths. `new-csproj` is used only when the
csproj is not a suite project, nothing ProjectReferences it, and the
parent commit does not contain it. Above 40 unowned paths the census
prints `UNOWNED-COUNT`, the first 40 `UNOWNED-PATH` lines, and
`UNOWNED-LIST` plus the list file path.

`WindowsUnitPlatformAdmissionTests` stays off this Linux selection.
`C1210_Windows_linux_only_outcomes_are_exact` skips on Linux, and a skip
is not a pass. `DerivedImpactIndexTests` is its own row, run after the
driver is built and the index is regenerated. Land-graph `-Check` is
manual: an inherited mismatch is reported and is not a failure of this
slice. No migration. Mutation execution is waived (operator 2026-11-12)
and is not discharged. CP-1 floor is 22 methods.

The checkpoint tool's own row deadlines govern these rows. A test-host
`--timeout 570s` is not passed to the tool. `bin-s3-testonly/` contains
no `ref` substring.

## Verification design

- V-1. `RcInputOwnersMapTests`. 22 methods. The six new methods fail at the witness commit and pass after the class lands.
- V-2. Nine-class guard union, without `WindowsUnitPlatformAdmissionTests` and without `DerivedImpactIndexTests`, floor 81. `C1729LayeringGuardTests` stays at VIOLATIONS 0 LISTED 162.
- V-3. `DerivedImpactIndexTests` alone, floor 27, after the index driver commit.
- V-4. `scripts/rc-input-reader-census.ps1`. Exit 0, `under-selected=0`, `noop-vs-owner=0`.
- V-5. Nightly policy hash and pinned input digest recomputed against the committed pins. No re-pin unless a guard fails.
- V-6. `scripts/audit-land-verification-graph.ps1 -Check`, warn only.
- V-7. `scripts/check-evidence-diff.ps1` over the task base..HEAD.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | s3 | `tests/Antiphon.Tests -> bin-s3-testonly/` | s3-testonly | `/*/*/RcInputOwnersMapTests/*` | V-1 | 0 failed, executed at least 22 | 22 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | s3 | CP-1 | s3-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-2 | 0 failed, executed at least 81 | 81 | 9 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | s3 | CP-1 | s3-index | `/*/*/(DerivedImpactIndexTests*)\|(DerivedImpactIndexInvariantTests*)/*` | V-3 | 0 failed, executed at least 27 | 27 | 9 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | s3 | n/a | s3-census | `pwsh -NoProfile -File scripts/rc-input-reader-census.ps1` | V-4 | exit 0 and under-selected=0 | n/a | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | s3 | n/a | s3-pins | `pwsh -NoProfile -Command '. ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-policy.ps1; $p = Read-NightlyExecutionPolicy -RepoRoot (Get-Location).Path; $d = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $q = [char]39; $m = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), ("PinnedInputDigest = " + $q + "([0-9a-f]{64})" + $q)); if (-not $m.Success) { throw "pin line missing" }; $pin = $m.Groups[1].Value; $quote = [char]34; $legacyPattern = "LegacyExclusionDigest = ${quote}([0-9a-f]{64})${quote}"; $legacy = [regex]::Match((Get-Content -Raw ./tests/Shared/NativeLinuxPolicyWitness.cs), $legacyPattern).Groups[1].Value; Write-Output ("policyHash=" + $p.Hash); Write-Output ("inputDigest=" + $d); Write-Output ("pinnedInputDigest=" + $pin); Write-Output ("legacyExclusionDigest=" + $legacy); if (-not $legacy) { throw "legacy pin line missing" }; if ($d -cne $pin) { throw ("input digest drift computed=" + $d) }'` | V-5 | exit 0, printed digest equals the script pin | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
