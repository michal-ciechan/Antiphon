# rc top-up 36

Fast-forwarded `origin/rc/2026-10-27` from `16477bad028ad0443ff58babd7d09004a1898524` to `8edda999623a473be2d1970727c172bfb5088d59`. `git ls-remote` matched that SHA on `refs/heads/rc/2026-10-27`. This report commit stays on `feat/card-task-5002b2ef` and is not on rc. No force, no rebase, no other `rc/` ref, no tag, no publish. `origin/master` moved during the pass to `0dbbbe4902fa50efc17c888ab34012640387d37c` and was not touched.

32 files changed against `16477bad` (6748 insertions, 129 deletions).

## Conflicts

Series 2 only. Cherry-pick of `997fea882`, `244aaf9e5`, and `7e067f8a0` hit a modify/delete on `.antiphon/task-b20bf5ac.md` (deleted on rc, modified in the source). Resolved with `git rm`, so the task file stays absent. No product hunk conflict. Those three messages say `cherry picked from commit <sha>` without the parentheses `git cherry-pick -x` writes, because they were finished with `git commit -F` during the conflict. They are already pushed and were not amended. Series 1, 3, 4, and 5 had no content conflicts.

## Integration fix on this branch

`C1210_Roster_closure_matches_source_gates` failed on the combined tree: `c1210-unrostered-gate: Antiphon.Tests.Application.AgentReportStoreLoadTests.Store_samples_record_normal_and_contended_phases`. The same scan also covers `AgentReportStoreBudgetTests.Slow_git_response_does_not_spend_write_budget`. Both call `NativeLinuxGate.Require` with a string literal, so they do not belong on the frozen `Native` array (61 identities, 72 results, first statement `NativeLinuxGate.Require(NativeLinuxReason)`).

`8edda999623a473be2d1970727c172bfb5088d59` adds both methods to `WindowsUnitSkipRoster.NativeSourceOnly` with the literal reasons and `Results=1`. Re-run of `/*/*/WindowsUnitPlatformAdmissionTests/*` on the rebuilt DLL: 5 total, 4 passed, 1 skipped (`C1210_Windows_linux_only_outcomes_are_exact` requires Windows), 0 failed. Build slot `baecdc93-86d7-4593-b4b7-cee4120f2a84`, waited 0s, held 132s, 0 errors.

## Land graph

`-Check` approved. Digest `20DE49F6B177E5A238F20D4F8C7F33EAEE6B511A9163CDB902D9136EF06DECB6`, count 66, SDK `10.0.403`, pins `749d65387b4e9ae7f84fba8c7b6f42658b430dc7`, phase `pass2`. No definition-file change, so `-Update` was not run. `LandGraphEvaluationTests` and `LandNonCodeSkipTests` were not run. Canary removed. Not-built notes: `docs/investigations/evidence/card-0462/Reproduce.csproj`, `scripts/fixtures/nightly/c1375-probe/Probe.csproj`, `scripts/fixtures/nightly/c487-probe/Probe.csproj`, `tests/Antiphon.TimeoutAudit.Tests`. Slot `905e82a0-6535-47ee-8648-9ddeee30c27e`, waited 0s, held 27s.

## Policy

`policyHash` moved from `67fb49932cf43763c6dd0bafd61ccac4d2945e72f8ceeed97c9d1910de35f38d` to `a7c88a0ab7b92daa0e9a72a8b2f377ca4a8678eaa0d469d22d1d97551c676382` in `tests/test-execution-policy.json` only. The old hash has no remaining source match. Census cases recompute the hash and passed. The Linux-only gate witness is the known-weak round-3 witness (CARD-1613 tracks the replacement) and was shipped as reviewed.

## Tests

Slot builds, `UseAppHost=false`, then `dotnet exec`. Counts are succeeded / failed / skipped.

| Run | Result |
|---|---|
| Fixture `slot-int-group` | 20 pass, 0 fail |
| Fixture `slot-driver-deadline` | 20 pass, 0 fail |
| `RemoteScriptContractTests` C849, C1192, C1189, C1191, C1193, C976, C973, C1208 | 138 / 0 / 0 |
| `DockerStackDocumentationTests` | 16 / 0 / 0 |
| `RollingProductionMountTests` | 12 / 0 / 0 |
| `RetiredTempContainerHostTests` | 22 / 0 / 0 |
| `RetiredTempContainerScriptTests` | 16 / 0 / 0 |
| `ReleaseGatePlatformTests` | 4 / 0 / 0 |
| `NightlyScriptsTests` | 5 / 0 / 0 |
| `RcRosterRepairTests` | 3 / 0 / 0 |
| `WindowsUnitPlatformAdmissionTests` before the roster fix | 3 passed, 1 failed (`C1210_Roster_closure_matches_source_gates`), 1 skipped |
| `WindowsUnitPlatformAdmissionTests` after `8edda999` | 4 / 0 / 1 |
| `RollingVolumeRecycleScriptTests` | 77 / 0 / 0 |
| `CheckpointStatePublicationTests` | 2 / 0 / 0 |
| `CheckpointSlotExecutorTests` | 13 / 0 / 0 |
| `ProcessSpawnLimitTests` (Antiphon.Tests) | 21 / 0 / 0 |
| `CheckpointAppTests` | 3 / 0 / 0 |
| `CheckpointAdmissionContractTests` | 3 / 0 / 0 |
| `CheckpointExecutorLogTests` | 6 / 0 / 0 |
| CP-2 `AgentReportStoreTests` | 28 / 0 / 0 |
| CP-2 `AgentReportStoreBudgetTests` | 21 / 0 / 0 (49 together) |
| CP-3 `OutputDistillationDeliveryTests` named methods | 8 / 0 / 0 |
| CP-4 `C544_ReportRegeneratedFromSnapshot` | 1 / 0 / 0 |
| CP-5 load sample with `ANTIPHON_REPORT_STORE_MEASUREMENTS=1` | 1 / 0 / 0 |
| CP-6 classification + lane guards | 2 / 0 / 0 |
| CP-7 `C1605_RegenerationTimeoutAdvancesRetryAndContinuesScan` | 1 / 0 / 0 |
| `ShadowCopyStoreTests` | 13 passed, 2 skipped (Windows file sharing) |
| `TestClassificationGuardTests` in PtyHost | 1 / 0 / 0 |
| `LinuxOnlyWindowsExclusions` | 8 / 0 |
| `C1415_SkipExclusions` | 10 / 0 |
| `LinuxSessionRunnerAdmission` | 13 / 0 |
| `C1323_PlatformPolicy` | 19 / 0 |
| `C1323_PlatformExecution` | 12 / 0 |
| Census C1404, C1577, C1584, C1410, G2865 | each `C487: 1 passed, 0 failed, 1 rows` |

The named PtyHost filter did not run the pre-existing Linux `C478` `DllNotFoundException` case. It was not fixed here.

## Skipped report-only commits

- `d9939bf801b41fbdf92d8cd3122a38495ad8cc74` `.antiphon/task-cd8476f1.md`
- `d6a3c4dae6fa920d41ae7082a56c04c5279e6f91` `.antiphon/task-554a22c6.md` (not an ancestor of `a00670ac`)
- `802ae96118ef7911ecf74fdc96b51d515abd54a3` `.antiphon/task-7a77fb91.md`
- `d68a9d7232acf2840156e48de97ae7c1dc1b7c3f` `.antiphon/task-141e4c6b.md`
- `3b544b9f7939405c2338e131a3916e005217bacc` `.antiphon/task-93cbfd8a.md`
- `4f8ab21a79fffef6acf4f15fd12e5e99968c0b5d` `.antiphon/task-defe39ed.md`
- `2c202cb6b4e32a47b17ed65ddf8c29b81180bfef` `.antiphon/task-9567bbca.md`

## Commits on rc, oldest first

`new` is the commit on `rc/2026-10-27`. `from` is the cherry-picked source. The last row is the roster fix made on this branch.

### Series 1, CARD-0849 fixture gate

- `439cacb6fed7fa7b393780e6aba755ffa256065d` from `cf06c598fce5b19e9060fbd0cf62958e01fbc1d5`
- `999e41488c50f7fa70ed102ab70faabe71a55ab3` from `43c8be4c8cd65bbc804467f50577642159f44be7`
- `66966bfd2c7dc4d6c0c5b2ae9f2699d5012c1c9a` from `f9f3e18153da20560ad59ec02512a0dce3008234`
- `d7f8d2e7f35c05935479d9381993293f2f85a904` from `46f1fb1af1c4b2ee553d82856b7b973ef2176baa`
- `9250d91afe8a402f250034ca05331d4478fb8d07` from `f0b7450bdc425b5d828b9db7ca050af428a4dee4`
- `1679a07332da6c402f35b849f537d3217b775d83` from `bf45b89b49996249f7e5892ccc7ff06672135946`
- `7b882d85709784841d69e9aa5b3d1dbac127fdca` from `d820bce7c0196b3202e475fc854af449890f80c3`
- `77c305e9e8434955658c62cd0b749b0fb4d38b58` from `fc182ab1c3c268d9411b12586a61033fce4f2f77`
- `03908bc8bc139aeb20cc199f181edef6b53583c0` from `ffa201cf25c2fb624e67b0dcbc0a31ad562fb472`
- `5b72ca3a5fa96b893c017caa2b633c5254334c50` from `89923afeb91df00990b21caabd4d0efd5306c08e`
- `53b84e05c9e6836b1dbe8ac77ec732025d34b82d` from `791ef43a4e5f3b9bfea47b375ad740e530b20093`
- `3d8a75db94327e6ea2d1353bcba4536d50d5ebf8` from `e154c74777f5a243966c3621cfc94f08d82bde9a`
- `03d8c6bb1160ab5150ce9031eb550d15568adb1a` from `348af6fca38f3119c67cfc6cd3602325d4949b7b`
- `ac300202c577d0506019a74e951aaad90ca9bf1b` from `3195b66913003efdad9536bc8f5eca8811002525`
- `15906398c4e8524eb1aa211fb3dafa2eba7c5137` from `acb3634d22aa77cbc891988b27590cecaba7ed86`
- `8a64d545b65262c6db7797514d839abc2fed5573` from `a00670ac28bde39df6a2ddce17f876cdc24e45b5`

### Series 2, checkpoint state.json

- `499cac576906761c6eb5b660ca634254be36e529` from `997fea8826f42519283267489f2d2442348bf350`
- `bf5d9ba5047a88d602e45fdaf311babf0f65e781` from `244aaf9e573f8e28367eb01a708c60e25c80a959`
- `bdc4accfa32b56202a8aed05ae955b8edd6e132a` from `7e067f8a0955d2b5dc9d19a66411bf1fe276deb1`

### Series 3, pty-host publication retry

- `8f7303abdcf6092ab16827ecd9d0113db2f11062` from `cfbf06753365ba65cbee4c7cdd99fd77a0fad2ff`
- `a530627ff839532b5d6f348a69227653de2f1d09` from `32bef229940eede3dbe5b0c4541b058e26cbc79b`
- `dd66e3df029b9d2b0ecddc50488aba866179f6ab` from `1985698d5aa56bc2a9a17adb605097a9905e5c49`
- `c01c4d632d9b32fc99976348654d33e04db523f6` from `d08054d7bf35060f19336cab981870c9a6559475`
- `79c8089351c632daed0145f7aaf3d8b47fbad4cc` from `3a99d4acc6d4edeace80c7f40c40fd295eb78e4a`
- `c36d3c803fb4ed1b4619ef2072833be4e1f99866` from `ddd809cb30e47cf56feb3365a417cb8cca7bd048`

### Series 4, CARD-1605

- `407d3ba4a127e3d1ea5a701d09f41ea42895adf4` from `b17bce08bd673f712fa3d649cf4602e06c3fa81f`
- `2b87003316180fb286e8e73df7596ddf66cdc8aa` from `0d517b525121d7f7c6c2766ace2f509079a4328b`
- `ed1b4580cb76cccac3a3763f3b67aa0f8d0aec16` from `e57fcd5c73cae4f1cef619646a767b3836929a20`
- `b9315c9aa3902f4a1536019a80c23018d057fc68` from `8df3d8db4eed85573696fe765fec66149d3ad0a4`
- `9de05307d1315fbfdb4419354283157505c4184f` from `0cd95a876dee9d5bccbb51b1702a5fc1fe0beaed`
- `794a607141981ac349e4c2f4df8b2aceb6a56f62` from `ad9a1e16f23a42ce8967f9a1be6f26d6da017dcf`
- `7c78a9de953f5a71770c44347cd1930f5d8d5bf4` from `3f2a6834eeb32da9d6f2d44d396c62a5fdb94efd`
- `c110960596160687eaeb57dca268cf1ab60cefbd` from `d51349ec90b89d3ebaac1801df86ce0dd77cc4dc`
- `704d4678efb85e3737a5e6167235d53366adabc3` from `ee2130f52ff91823b63c3a35bbae5e03203a7227`

### Series 5, rc gate policy

- `d41d959b32355b4f1417718e89c7ce58a369d47c` from `1c35c653123d1ad5be0d5d86d4facd6383aeebbc`
- `df6eb40900685dab292746c7cb7e9822481a3ade` from `3180cd87d3947fce7c469ec5851d3512a9fb5c13`
- `4ea3b9c591d5b03087bd9b80f42a137ab991a6f3` from `65862ecfdf961619fdac8e1f473d6805fad5a3ab`
- `5f3df45e6b22f1737c9e1369ea3e642e18072499` from `19435beb086327d5c557b299cfb9330c541b47a7`
- `67ee1aba3aedac467978ff6b6b0e72ced0b20b8a` from `d553548e98a9e65f2a526e540d5114aa63c7345e`

### Roster closure

- `8edda999623a473be2d1970727c172bfb5088d59` test: roster CARD-1605 native Linux gates in the C1210 closure
