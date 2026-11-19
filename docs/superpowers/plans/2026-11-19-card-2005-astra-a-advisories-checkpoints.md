# CARD-2005 Astra A advisories: checkpoints

Code task `fb6a2e49`. Review subject `docs/reviews/2026-11-19-card-2005-astra-fixes-a-review-r1.md` at `601d0db45293a8255f6ae55d0ca4cfe8c25b8fc7` (CLEAN; advisories are not land holds). Base `5be178005f06aa868bbb038e561c2ef22a654cb5`.

A-1: `ReadDiscovery` decodes a UTF-16 document that starts with FF FE or FE FF, and still refuses a UTF-16 document without a BOM as `utf-16`. A-2: the hash cache prefers the 128-bit `FileIdInfo` id and falls back to the 64-bit index; unknown is never equal, including unknown versus itself and a 128-bit id versus the 64-bit fallback. A-3: the seam is `CacheFileIdentity.Decide` / `TestOverride`. The desktop command that observes real `ReadWindows` is `pwsh -NoProfile -File scripts/test-nightly-core.ps1 -Case DiscoveryCacheAgrees`. This Linux runner does not execute it.

`PinnedInputDigest` on this tree is `89fec158ad8dca3bd17b4baf1284822552938069e2e772ad6b88f5821cc2f4a6` (`d6947de222f2e635927b2ad9876b5fdcc1b03131`). `policyHash` stays `0396d6a543917cd112d2f28dd0adfe8bd80ae549f2803df2bb866a751b261542`. No PC-n is declared. Mutation execution is waived (operator 2026-11-12) and is not discharged.

## Verification design

- V-1. `DiscoveryParserTolerance` accepts a producer UTF-8 document, a UTF-8 document, a UTF-8 BOM, a trailing comma, a UTF-16 LE document with a BOM, a UTF-16 BE document with a BOM, an empty UTF-16 BOM as `empty discovery`, and a UTF-16 BOM comment as `comment`. It refuses UTF-16 without a BOM as `utf-16`. Ten rows, zero failures.
- V-2. `DiscoveryCacheAgrees` still hits the Linux lstat cache. Two rows, zero failures. The Windows `FileIdInfo` path is not executed here.
- V-3. `DiscoveryCacheFileId` exercises `CacheFileIdentity.Decide`: same 128-bit id, different id, different volume, 64-bit fallback, kind mismatch, unknown versus unknown, malformed id, `HistoryNodeIdentity.Read` returning a known cache id, a different id missing the hash cache, the same id hitting it, and unknown missing it. Eleven rows, zero failures.
- V-4. `CoreMatchesFallback` stays at 38 rows after the cache-identity change, including `Test-NCHistoryLinks`.
- V-5. `-PrintInputDigest` equals the resealed `PinnedInputDigest`.
- R-1. `TestClassificationPolicyTests` once. Floor 42 executed results.
- R-2. Linux guard batch, one reused build: classification guard, slow-registry merge, slow tripwire, native Linux policy witness, mechanical PC contract, task platform guidance, C1729 layering, lane category. Floor 39, the executed count from run `20261119-193243-06fd` (39 passed, 0 failed). `RunnerDefaultGuidanceTests` is a sibling class and is outside `TaskPlatformGuidanceTests*`. `WindowsUnitPlatformAdmissionTests` is not selected on Linux. `DerivedImpactIndexTests` is omitted because `tests/derived-impact-index.json` is unchanged.
- R-3. `scripts/audit-land-verification-graph.ps1 -Check` exits 0. There is no `scripts/land-graph.ps1` and this run does not pass `-Update`.
- R-4. `scripts/check-evidence-diff.ps1` over `5be178005f06aa868bbb038e561c2ef22a654cb5..HEAD` exits 0.
- R-5. `scripts/check-os-witnesses.ps1` exits 0 against `.antiphon/card-2005-os-witnesses.json`. Exit 0 is syntax and inventory only. Native `FileIdInfo` stays pending.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | advisories | n/a | cache | `pwsh -NoProfile -File scripts/test-nightly-core.ps1 -Case DiscoveryCacheAgrees` | V-2 | exit 0, 2 passed, 0 failed | n/a | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | advisories | n/a | parser | `pwsh -NoProfile -File scripts/test-nightly-core.ps1 -Case DiscoveryParserTolerance` | V-1 | exit 0, 10 passed, 0 failed | n/a | 2 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | advisories | n/a | fileid | `pwsh -NoProfile -File scripts/test-nightly-core.ps1 -Case DiscoveryCacheFileId` | V-3 | exit 0, 11 passed, 0 failed | n/a | 2 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | advisories | n/a | pin | `d=$(pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest \| tr -d '\r'); printf '%s\n' "$d"; grep -F "89fec158ad8dca3bd17b4baf1284822552938069e2e772ad6b88f5821cc2f4a6" scripts/test-live-lane-run.ps1 >/dev/null && test "$d" = "89fec158ad8dca3bd17b4baf1284822552938069e2e772ad6b88f5821cc2f4a6"` | V-5 | exit 0 and the digest line | n/a | 1 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | advisories | n/a | core | `pwsh -NoProfile -File scripts/test-nightly-core.ps1 -Case CoreMatchesFallback` | V-4 | exit 0, 38 passed, 0 failed | n/a | 4 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | guards | `tests/Antiphon.Tests -> bin-card-2005/` | policy | `/*/*/TestClassificationPolicyTests/*` | R-1 | 0 failed | 42 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | guards | CP-6 | guards | `/*/*/(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-2 | 0 failed, C1729 VIOLATIONS 0 LISTED 162 | 39 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-8 | advisories | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-3 | exit 0 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-9 | advisories | n/a | evidence | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 5be178005f06aa868bbb038e561c2ef22a654cb5 -HeadRef HEAD` | R-4 | exit 0 | n/a | 2 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-10 | advisories | n/a | os-witness | `pwsh -NoProfile -File scripts/check-os-witnesses.ps1 -BaseRef 5be178005f06aa868bbb038e561c2ef22a654cb5 -HeadRef $(git rev-parse HEAD) -EvidencePath .antiphon/card-2005-os-witnesses.json -Plan docs/superpowers/plans/2026-11-19-card-2005-astra-a-advisories-checkpoints.md` | R-5 | exit 0, syntax and inventory only | n/a | 2 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |

### Cost

Ordinary floor is 15 + 2 + 2 + 1 + 4 + 15 + 8 + 5 + 2 + 2 = 56 minutes. CP-1 uses the 15-minute advisory ceiling; the case is two Linux cache rows. CP-6 is the one `Antiphon.Tests` build. CP-7 reuses it. No repeat proof after green.
