# Astra fixes A checkpoints

Code task `a994b73f`. Base `d86d0f39422b152faf8c39e362a58b745eb95267`, fast-forwarded to `a12ff2eefdbcec889353802f50fb11a76a06390c` before the task commits. Red witnesses `640cdfb293a57f6e93ca4eeec4d25ed1430e2784`. Fix `ea9e076e50035992e8abe35f9c49d77bd1dedaa7`. Pin reseal `93ec1faa561a66ff5131a4b6c28d03046731df80`.

## Verification design

- V-1: a timestamp-preserving replacement of the discovery document changes required uid `u1` to `u2`. The cached discovery index and a fresh read agree on `u2`. The cache hit requires length, last-write time, and file identity (device and inode). An identity that cannot be read is not cached.
- V-2: `Read-NightlyDiscoveryDocument` accepts a UTF-8 BOM and a trailing comma. It refuses UTF-16 and comments. `discovery-unreadable` names the file and the reason (`utf-16`, `comment`). Empty input stays `empty discovery`.
- V-3: `DiscoveryWriter.WriteFile` is the producer. It writes UTF-8 with no BOM, commas only between values, and no comments. The witness checks those bytes. `NightlyJson.ReadFile` stays strict. There is no `ConvertFrom-Json` fallback on this path.
- V-4: `scripts/test-nightly-core.ps1 -Case CoreMatchesFallback` still passes 38 rows.
- V-5: `NightlyCoreTests` passes that same case.
- R-1: `TestClassificationPolicyTests` once. `policyHash` stays `0396d6a543917cd112d2f28dd0adfe8bd80ae549f2803df2bb866a751b261542`.
- R-2: `DerivedImpactIndexTests` alone. The driver project is built before this table, through `scripts/build-slot.ps1`, because `regenerate_script_reports_changed_then_unchanged` runs `scripts/regenerate-derived-impact-index.ps1 -NoBuild`. Building `tests/Antiphon.Tests` does not produce that driver. That build is unlisted for that reason.
- R-3: the Linux guard batch below. `C1729LayeringGuardTests` stays `VIOLATIONS 0 LISTED 162`.
- R-4: `scripts/audit-land-verification-graph.ps1 -Check` exits 0.
- R-5: `PinnedInputDigest` stated in `scripts/test-live-lane-run.ps1` equals the digest printed by `-PrintInputDigest`, and `LiveLaneRunnerTests` passes.
- R-6: `scripts/check-evidence-diff.ps1` over `d86d0f39422b152faf8c39e362a58b745eb95267..HEAD` exits 0.
- R-7: `ReleaseImpactScriptTests` passes. The impact selector for `a12ff2eef..HEAD` names `nightly-live-lane` plus this class.

Red, already observed at `640cdfb293a57f6e93ca4eeec4d25ed1430e2784`, before the fix. Cache lease `5687b63c-3ce8-48ba-b65f-bf5549aed18c`, waited=0s, held=15s: cached uid `u1`, fresh uid `u2`. Parser lease `d8a2f58d-fb93-455a-99f0-aecf25dbf902`, waited=0s, held=4s: trailing comma, UTF-16, and comments threw `discovery-unreadable` with no reason. The producer row, plain UTF-8, and UTF-8 BOM passed. The same two cases passed after the fix (cache lease `4a308141-df51-477d-ab46-1eba282fe7d1`, 2 passed; parser lease `0ac2edd4-90e1-41b5-972b-c7fe58d4ce88`, 6 passed). CP-1 and CP-2 re-run them on the committed tree.

Same-inode rewrite that also restores last-write time is outside the audit case. The production writer replaces the file.

`WindowsUnitPlatformAdmissionTests` is not a row. On Linux its skip is not a pass. Windows behaviour of this change is unobserved.

Not in this closed list, under the short-test bound (operator 2026-11-18): `scripts/test-nightly-tests.ps1`, `scripts/test-nightly-run.ps1`, `scripts/test-nightly-health.ps1`, `scripts/test-nightly-failed-first.ps1`, `scripts/test-nightly-stopped-seed.ps1`, and `scripts/test-nightly-probe-supervisor.ps1`. `test-nightly-tests.ps1` C1044 parses the `--list-tests` diagnostic, which is not the discovery document. No positive controls. Mutation is waived (operator 2026-11-12).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | all | n/a | discovery-cache | `pwsh -NoProfile -File scripts/test-nightly-core.ps1 -Case DiscoveryCacheAgrees` | V-1 | exit 0; 2 passed, 0 failed | n/a | 15 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-2 | all | n/a | discovery-parser | `pwsh -NoProfile -File scripts/test-nightly-core.ps1 -Case DiscoveryParserTolerance` | V-2, V-3 | exit 0; 6 passed, 0 failed | n/a | 5 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-3 | all | n/a | core-fallback | `pwsh -NoProfile -File scripts/test-nightly-core.ps1 -Case CoreMatchesFallback` | V-4 | exit 0; 38 passed, 0 failed | n/a | 10 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-4 | all | n/a | pin-digest | `grep -F "\$script:PinnedInputDigest = 'ab041a1cb0e999549e95391e38520161e5fd31c93af0a481e67fc79060b42cac'" scripts/test-live-lane-run.ps1 && pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest > /tmp/astra-a-pin-computed.txt && computed=$(tr -d '[:space:]' < /tmp/astra-a-pin-computed.txt) && test "$computed" = ab041a1cb0e999549e95391e38520161e5fd31c93af0a481e67fc79060b42cac && rm /tmp/astra-a-pin-computed.txt && echo pin-ok` | R-5 | exit 0; stated digest equals computed digest | n/a | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-5 | all | `tests/Antiphon.Tests -> bin-astra-a/` | nightly-core | `/*/*/NightlyCoreTests/*` | V-5 | all listed, 0 failed | 1 | 15 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-6 | all | CP-5 | live-lane | `/*/*/LiveLaneRunnerTests/*` | R-5 | all listed, 0 failed | 4 | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-7 | all | CP-5 | release-impact | `/*/*/ReleaseImpactScriptTests/*` | R-7 | all listed, 0 failed | 10 | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-8 | all | CP-5 | policy | `/*/*/TestClassificationPolicyTests/*` | R-1 | all listed, 0 failed | 42 | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-9 | all | CP-5 | derived-index | `/*/*/(DerivedImpactIndexTests*)\|(DerivedImpactIndexInvariantTests*)/*` | R-2 | all listed, 0 failed | 27 | 12 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-10 | all | CP-5 | linux-guards | `/*/*/(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-3 | all listed, 0 failed | 39 | 10 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-11 | all | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-4 | exit 0 | n/a | 3 | true | n/a |
| CP-12 | all | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -Repository . -BaseRef d86d0f39422b152faf8c39e362a58b745eb95267 -HeadRef HEAD` | R-6 | exit 0 | n/a | 3 | true | n/a |
