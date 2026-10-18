# rc/2026-10-18 Linux lane

Status: **roster red, one inherited failure**. This is not an rc-profile complete-green receipt and not a publication.

Candidate: `a1d24b6fc92d45ae5274b418bf54c6b8819a8697` (`rc/2026-10-18`, also `feat/card-task-dcba76bd` at the start of this pass).
Previous rc tip used for the inherited check: `61117dd1663d2f47ce4708c18b609b8936491986` (`rc/2026-10-15`).
Host: Debian 12 container `a6457bc89508`, pwsh 7.5.4, .NET SDK 10.0.402, test host .NET 9.0.21, TUnit 1.44.0 / Microsoft Testing Platform 2.2.2, node v22.23.3, npm 10.9.9. Build-slot broker `http://127.0.0.1:8080/build-slots`. Production session-runner sessions were not used; the suite cleared the runner base URL to `http://127.0.0.1:1`.

## Why the profile entry was not the runner

`profiles.rc` is the automated Windows profile. `docs/release-gates.md` lines 113-119 say `rc` means that Windows profile, and Linux-only lanes (CARD-0590) are outside it. Line 180 sends `nightly-run.ps1 -Profile rc` at `C:\Antiphon\releases\checkout`. Lines 267-268 leave Linux coverage with CARD-0590 / CARD-0605.

The entry cannot start on this host. `scripts/lib/nightly-tests-impl.ps1` line 340 launches client install as `"$env:ComSpec"` `/c` `npm.cmd` `ci` (build and lint at lines 346 and 358). Here `ComSpec` is empty and `npm.cmd` is absent (`npm` is `/usr/local/bin/npm`). Line 485 then launches each native suite as `tests\{0}\bin\Debug\net9.0\{0}.exe`. `docs/investigations/2026-10-15-release-candidate.md` lines 43-50 record the same split: this kind of Linux host can run the CARD-1287 roster, and it cannot produce the Windows eight-suite profile.

CARD-1313 selection from `61117dd1663d2f47ce4708c18b609b8936491986` to `a1d24b6fc92d45ae5274b418bf54c6b8819a8697` is `mode=full`, `activation=unqualified`, `executionEvidence=false`, digest `3d75bf6b1c2fa0a9c548249f7e7bcc99732bd6958798140901590ac1cff7bdac`. Fallbacks: `baseline-catalog-unavailable`, `rule-full:P-SERVER`, `selection-authority-changed`, and 161 `unknown-path` entries (164 total). Prerequisites named `app-db`, `broker`, `browser`, `client-build`, `native-child`. ChangeAware is not admitted. The run below is the historical Linux roster, not a narrowed selection and not Full profile credit.

## What ran

The reproduction in `docs/investigations/2026-10-15-card-1287-linux-unit.md` lines 13-17. One isolated build, then one no-build execution of the frozen filter. Filter SHA-256 `9ba95d1db683438f6c382422c9ca4a596d2280930f100da9973b657596020ad7` matches that qualification note. `UseAppHost=false`. Output path `bin-rc1018-dcba76bd/`. Headed, distiller, broker, and task-token variables were unset. `TUNIT_MAX_PARALLEL_TESTS` was unset.

| Piece | Slot | Wait | Hold | Result |
|---|---|---:|---:|---|
| `dotnet build tests/Antiphon.Tests` | `02fbf757-05ea-45d9-b8f4-003400c0035e` | 0s | 196s | 1083 warnings, 0 errors. Start `2026-10-18T17:29:31Z`, end `2026-10-18T17:32:49Z` |
| Frozen roster | `cba8fedc-08f7-4a90-b8ec-93723301762f` | 0s | 1939s | exit 2. Outer `2026-10-18T17:33:26Z` to `2026-10-18T18:05:47Z` |

Assembly informational version `1.0.0+a1d24b6fc92d45ae5274b418bf54c6b8819a8697`. Assembly SHA-256 `df7b97a4df9611ad4c4e0c46bed1b5ab8656eb566e83a0c17d7df538bddc3735`. Checkout HEAD during the run was the candidate. No product or test file was edited.

TRX `fcbf960f-55cc-4086-9017-fa934182e647`. Times start `2026-10-18T17:33:30.9778455Z`, finish `2026-10-18T18:05:46.4642065Z` (32m 15.952s in the TUnit summary). SHA-256 `07e9067b37d61509c5bbf12c4f16a80d96502ce171ed86cb72f09b2fe13cb8d9`. Path: `/tmp/rc-linux-dcba76bd/roster/run.trx`. Class identities equal all 373 roster names, missing 0, extra 0.

| Suite | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| antiphon, frozen Linux Unit roster (373 classes) | 4528 | 4527 | 1 | 0 |
| session-runner | not run | | | |
| pty-host | not run | | | |
| agents-pty | not run | | | |
| messaging | not run | | | |
| client | not run | | | |
| scripts | not run | | | |
| e2e | not run | | | |
| markdown-pdf | not run | | | |

The eight suites other than this roster were not started. The profile entry refuses on this host before those suites, and the published Linux gate for the previous candidates was this 373-class roster only (`docs/investigations/2026-10-16-card-1314-test-duration-buckets.md` records that TRX as Antiphon.Tests alone).

## The red

`Antiphon.Tests.Checkpoints.CheckpointNamespaceCensusUsageTests.namespace_census_matches_compiled_checkpoint_cases` (2.756s). `Get-NamespaceCensus` `selected` should be 600 and was 594. The candidate literal is `scripts/lib/checkpoint-usage.ps1` line 115, `selected = 594`. Message: `scripts/lib/checkpoint-usage.ps1 Get-NamespaceCensus is stale against the compiled checkpoint cases`.

Classification: **inherited**. The same method at `61117dd1663d2f47ce4708c18b609b8936491986` failed in a scratch worktree. There the compiled count was 589 and the literal was 586 (`selected = 586` at that blob's line 115). The literal moved 586 to 594 and remains behind the compiled count.

| Baseline piece | Detail |
|---|---|
| Build | slot `631939fb-d1d0-439d-84d1-d145ebc02c9f`, waited 0s, held 138s, 0 errors. Assembly `1.0.0+61117dd1663d2f47ce4708c18b609b8936491986` |
| Test | slot `bdfea7d1-41d7-4c94-8091-a1d45108ed04`, waited 0s, held 8s. `2026-10-18T18:09:30Z` to `2026-10-18T18:09:40Z`. Exit 2. 1 executed, 0 passed, 1 failed, 0 skipped |
| TRX | `b8f42b93-2f5e-4726-b7e0-6e691159061f`, SHA-256 `a2e0ecd36a6a449601246d7f378f4d7255858f9326e62bddc4ba758648897b17` |

Filter: `/*/*/CheckpointNamespaceCensusUsageTests/namespace_census_matches_compiled_checkpoint_cases`. Worktree: `/tmp/rc-baseline-61117dd` (detached). No fix was applied.

## Not in this roster

The frozen filter does not contain `RemoteScriptContractTests`, `ReleaseGateRunTests`, `RollingVolumeRecycleScriptTests`, or `C1008PlatformContractTests`. The named inherited Linux reds CARD-1216 (transfer, locks, barriers, partial), `C1192_Npm_volume_sentinel_and_empty_cache_miss_are_observed`, `C1191_Fixture_failure_records_unhandled_exit_without_seed_receipt`, `C1105_Git_audit_resume_preserves_proof`, `C599_CloneOwnership`, and `C599_ReportLane` were therefore not executed and were not reclassified here. E2E was not run. `libglib` is not on the loader path in this container, which is the CARD-1305 D-5 environment, not a new E2E result.

## Limits

No tags, no publish, no `release-cut.ps1`, no product or test edit. Generated TRX stays outside Git. This pass does not discharge Full rc coverage, ChangeAware admission, or the Windows lane.

--- next stage ---
next: decide
handoff: The Linux CARD-1287 roster at a1d24b6fc92d45ae5274b418bf54c6b8819a8697 is 4527 passed, 1 failed, 0 skipped. The failure is the checkpoint namespace census (594 vs 600), already red at rc/2026-10-15 61117dd1663d2f47ce4708c18b609b8936491986 (586 vs 589). The rc profile entry cannot run on this Linux host. The other eight required suites were not started.
artifact: docs/investigations/2026-10-18-rc-linux-lane.md
