# CARD-1572 final eight-red continuation

Six inherited fixture reds are repaired and pass compiled narrow selections. The Windows custody case now skips explicitly on Linux. The legacy composite retains its historical timeout red; all four unchanged driver modes completed separately with 124 assertions and a measured provisional cost of 778 seconds. Rc scratch application and compilation passed.

## Source, scope and red witnesses

Task 4ed75787; branch feat/card-task-4ed75787. Commissioned baseline: be65f12449e0542d9a5cab9e6ad7953dea7fe6bb. Tested code tip: 0a137797cf5a6549e4e025735f2df8cc120559b8. Final report changes are narrative only; no compiled input changed after that tested tip. The earlier [outage recovery report](2026-10-23-card-1572-outage-recovery.md) retains the historical 6024 passed / 43 skipped / 8 red / 0 missing attributed census. This continuation is a narrow repair and cost investigation, not a new full Unit lane or rc qualification.

Before repairs, SHA-validated the eight inherited failed cases in six complete TRX files. Relevant test/helper and production shell inputs were identical between each receipt source and the commissioned baseline. The complete tests/TestHelpers and tests/Scripts directories, test project, nightly owner and legacy harness inputs also have no delta from 30b41c64780f5204102d3133c457fcc1aa287c43 to that baseline. Witness commit: d396dc18a2f4bf315c88bd10c02c664ac01c8922. These are historical reds, not fresh baseline reruns; the expensive 322-second matrix and 601-second timeout were not repeated just for attribution.

Raw evidence root: /work/worktrees/task-4ed75787/.antiphon/1572-final. inherited-reds.json retains exact identities, source equivalence, source SHAs, TRX paths and SHA-256 digests, durations and failure messages. witness.ps1 is its validator. The prior raw TRX files remain in the predecessor evidence roots named by that manifest.

## Repair picks

Apply this task's code picks in this order:

| SHA | Repair and retained assertions |
|---|---|
| 04136b8ea1b6e98ee85388b50752f53cfa2b79dc | Complete the npm extraction's real resource/ownership/cache-identity dependencies; register its current-run cache under an admitted name. Model only container launch, checking its positional run/name boundary. Keep nounset, argument log, exact success/miss diagnosis and reader sentinel assertions. Both parameters pass. |
| 60f0b1677f3bcf11976ec25a62e6b3204271e8ed | Add cold-reuse receipt lookup to the extracted full-seed fixture's explicit fail-closed doubles. Unexpected invocation still exits 97 and is recorded; no no-op success stub. All publication, donor, archive, import, payload and refusal assertions remain. Three formerly red cases and the closure preflight pass. |
| 578179e0c4af6b6447909c56ef4fc4abde92214b | Correct active-land's expected admission per CARD-1353, b0d2a198b2cea824553845d2649c46a66c4134c2 and docs/docker-stack.md. Require all three removals plus census proof of runner=other and NeedsResolution. Preserve every other refusal; add an explicit bound-land refusal. |
| 8f42b887aab127038cba04c0c952a61f42ca12d5 | Guard C1514T_PS51AndRerun before setup on non-Windows. The exact Linux skip reason is: CARD-1514: actual nightly native custody and PowerShell 5.1 rerun require Windows; Linux cannot qualify this owner. All Windows native nine-result/five-artifact and PS5.1 assertions remain. |
| 0a137797cf5a6549e4e025735f2df8cc120559b8 | Give the bound-land control its pending request timestamp, so it reaches and asserts RecycleLandInFlight. This accompanies the off-runner correction above. |

The six former C# fixture failures now pass; the seventh formerly red identity is intentionally skipped on Linux; the eighth remains a retained red with completed diagnostic pricing. No assertion or timeout was weakened. Production shell behavior was unchanged.

## Compiled narrow verification

Host 0c18bb7f591e, Linux; SDK 10.0.402, target runtime 9.0.21, TUnit 1.44.0 and MTP 2.2.2. Each run.json retains exact argv, expected membership, source, clean state, assembly digest, timestamps, host/load and finite collection limit. The checked-in execution policy's safe-environment clearing was applied to execution. Every build/test driver used the host build-slot gate, sequentially. All slot waits were zero; admission was capped at three minutes. Native class/method timeouts remained unchanged. Execution drivers had finite outer watchdogs including admission (for the jq diagnostic modes: 540 seconds total, with a 180-second queue cap).

The initial isolated build passed in 203.043 seconds outer wall (MSBuild 200.92 seconds, 1179 warnings, zero errors). Its inner timeout wrapper prevented the gate from inserting its CPU cap. This was corrected before the final build check: the direct gated build applied maxcpucount=6, passed in 16.215 seconds outer wall (MSBuild 14.19 seconds, one warning, zero errors), and produced the identical assembly SHA-256 74bfe3675b9fee746bb3b2cc618e35e70a11ba8a89fdc7ebb09fc436e10f789f. This extra incremental build was necessary to correct the wrapper; it was not a second repair batch. The npm/seed receipts bind that identical binary; no rerun solely for relabeling was made.

| Selection | Exact TUnit filter | P/F/S | Outer wall seconds | TRX body sum seconds |
|---|---|---|---:|---:|
| npm-green | /*/*/RemoteScriptContractTests/C1066_Fixture_npm_install_uses_argument_log_under_nounset | 2/0/0 | 6.144 | 0.201 |
| seed-green | /*/*/RemoteScriptContractTests/(C849_Seed_publishes_complete_payloads_before_its_marker*)|(C849_Seed_refuses_invalid_donors_and_partial_payloads*)|(C849_Saved_donor_archive_is_checked_and_imported_without_a_container*)|(C849_Closure_preflight_names_a_missing_dependency*) | 4/0/0 | 21.721 | 16.648 |
| summary-green | /*/*/TestPhaseSummaryTests/* | 6/0/1 | 14.523 | 9.739 |
| recycle-green | /*/*/RemoteScriptContractTests/C1008_Recycle_refuses_references_and_unknown_census | 1/0/0 | 351.974 | 346.408 |

Total fresh TUnit: 14 intended, 13 passed, zero failed, one named Windows-only skip, zero missing. Tests were run through dotnet run --project tests/Antiphon.Tests --no-build with OutputPath=bin-c1572final/ and UseAppHost=false. driver.ps1 retains the full commands; the filters above can be reused after a gated isolated build.

TRX remains authoritative. Internal Docker/container/readiness, data preparation, web-host/migration, filesystem/HTTP/CLI and teardown subphases were not separately instrumented and remain unknown, not zero. The outer wall remainder is not assigned to a particular phase. This offline selection used no live stack restart or database setup. Per-test durations, membership and grouped body/wall costs are retained in test-results.json, classes.csv, fresh TRX, diagnostics and each run.json.

| Case | Outcome | TRX seconds |
|---|---|---:|
| C1066_Fixture_npm_install_uses_argument_log_under_nounset(True) | Passed | 0.099 |
| C1066_Fixture_npm_install_uses_argument_log_under_nounset(False) | Passed | 0.101 |
| C849_Closure_preflight_names_a_missing_dependency | Passed | 1.426 |
| C849_Seed_refuses_invalid_donors_and_partial_payloads | Passed | 0.576 |
| C849_Seed_publishes_complete_payloads_before_its_marker | Passed | 10.367 |
| C849_Saved_donor_archive_is_checked_and_imported_without_a_container | Passed | 4.279 |
| C1514T_MergeAndQuality(full-match) | Passed | 3.242 |
| C1514T_MergeAndQuality(trx-only) | Passed | 1.358 |
| C1514T_MergeAndQuality(torn-tail) | Passed | 1.411 |
| C1514T_MergeAndQuality(ambiguous-missing) | Passed | 1.542 |
| C1514T_PS51AndRerun | NotExecuted | 0.044 |
| C1514T_MergeAndQuality(conflicting-duplicate) | Passed | 1.086 |
| C1514T_MergeAndQuality(stale-run) | Passed | 1.055 |
| C1008_Recycle_refuses_references_and_unknown_census | Passed | 346.408 |

## Legacy composite pricing and unchanged red

The original complete rolling-7 TRX at source 30b41c64780f5204102d3133c457fcc1aa287c43 records C1008_Legacy_rolling_and_jq_rosters_remain failing after 601.061 seconds against its unchanged 600-second test timeout. The retained log shows the first three driver modes completing before that timeout. That receipt stays red.

Ran the four unchanged scripts/test-deploy-server2-jq.ps1 modes separately, once each, through the build-slot gate at the tested code tip. present retained -RequireJq and rejected C973_JQ_SKIPPED. Each retained exactly one 31-assertion success summary, its original groups/invocations/assertions, and its owned -KeepTemp fixture tree. Optional T-20 skips in the other modes remain their original branch contract.

| Mode | Wall seconds | Slot wait seconds | Slot held seconds | Driver assertions | Harness groups/invocations/assertions |
|---|---:|---:|---:|---:|---|
| present | 194.194 | 0 | 193 | 31 | 24/66/227 |
| absent | 182.517 | 0 | 181 | 31 | 23/62/218 |
| missing-shell | 196.048 | 0 | 195 | 31 | 23/62/218 |
| failing-shell | 204.471 | 0 | 203 | 31 | 23/62/218 |

Sum of separate-mode outer walls: 777.230 seconds (12m57.2s). Separate gate-held intervals total 772 seconds. The four modes retain 124 driver assertions, 93 harness groups, 252 invocations and 881 harness assertions. These separate diagnostic runs do not substitute for the frozen CARD-1040 composite checkpoint, its source/qualification contract or a fresh green composite receipt.

Pricing through the repository tooling:

    pwsh -NoProfile -File scripts/refresh-nightly-class-costs.ps1 -TrxPath /work/worktrees/task-fb7c4a41/.antiphon/1572-cont/rolling-7/rolling-7.trx -OutputPath .antiphon/1572-final/legacy-timeout-costs.json -Source 'CARD-1572: Linux timeout-censored composite; provisional n=1 lower bound'

The unedited tooling output is an antiphon-nightly-class-costs-v1 Linux row for Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests: nodes=1, seconds=601.1, basis=trx. Its source explicitly marks timeout censoring, provisional n=1 and a lower bound. It is a single-method sample, not a complete class cost, and was not imported into the committed Windows class-cost table.

The completed-mode measurement supplies the method's proposed serial cost row: C1008_Legacy_rolling_and_jq_rosters_remain, nodes=1, seconds=778 (ceiling of 777.230), provisional n=1 per mode. This prices work, not a new timeout. There is no measured p95 and no permission to enlarge a test/checkpoint timeout. price.ps1 validates all four summaries/rosters and writes legacy-mode-costs.json; each mode retains source/host/load, logs and their digests. No policy, frozen selection, retry or deadline was changed.

A bounded optimization candidate is Call-Verifier's C727_TEST_VERIFY_STUB branch in scripts/deploy-server2.ps1: use the established in-process script-double convention already used by its HTTP and host-jq fixture boundaries, preserving manifests, LASTEXITCODE, output, distinct fixture state, every mode and every roster assertion. Keep the real production pwsh/SSH branch and its native boundary tests. The present-mode trace alone contains 54 mock case entries plus 36 lock requests and 66 wrapper invocations; repeated process startup is a plausible contributor, not a separately measured phase. Do not change the lock helper's child lifetime/readline custody boundary. Measure parity and cost before adopting this candidate; no speedup is claimed from this investigation. CARD-1404 owns measured packing, and CARD-1514 owns fixture/phase work.

## Rc applicability

Pinned origin/rc/2026-10-24 at fe84f79f84555f2d8d3d6df13438f164eda994bc. The master-based rc line exists and predates the predecessor's four fixture repairs. The scratch check reused these existing picks first, without reimplementing them:

1. 30b41c64780f5204102d3133c457fcc1aa287c43
2. 4026e85d4f383683ce0f850c1e8a0050f65b5f34
3. 7ce941b313b2ef7f81f491a8de2bea66c77bcd9e
4. 913d9481da2f4d06e75f2fbad1b8c5f182642aed

Then applied this task's five code picks in the table above. Every git apply --check and apply passed. No scratch commit or rc push was made. The older rc/2026-10-20 line was not recompiled because the new line exists; the prior report retains its older-line compile.

A single gated scratch build at maxcpucount=6 used bin-c1572rcfinal/ and passed: 1181 warnings, zero errors, MSBuild 133.57 seconds, 135.558 seconds driver wall, zero slot wait. The base had four deliberate modified source paths; exact patch SHAs, per-patch SHA-256, diff SHA-256 ff7cdcf92915a98ee4108b2153e77cd4d7ddcda7e3eb8d9d7b3894fcc37b4ae1, build argv and source/host/load are retained in rc-check.json, rc-diff.patch and rc-build/run.json. Assembly SHA-256: aa7bac251bd8972add90135bd803347c94d3eeeb56edc9a4deba55529436514f.

This proves patch/compile applicability of the nine-pick combined fixture set, not rc test qualification, deployment or activation. The first cleanup attempt removed historical tracked .antiphon files in the owned scratch and then refused its hidden root. Those scratch files were restored from pinned HEAD and clean status verified before removing the scratch. Compile evidence had already been copied outside it. The recovery is recorded; no recompilation was needed. All rc alternate outputs are gone and the detached worktree is removed. All 30 producer-owned main alternate output directories were removed, with zero remaining. Raw generated evidence stays ignored.

## Remaining acceptance and handoff

Windows CP-8/native custody and actual PowerShell 5.1 evidence remain owed on Windows. The legacy composite's historical timeout remains unresolved acceptance; separate-mode passes and pricing do not make it green. The frozen CARD-1040 qualification and full Unit/rc gates remain incomplete. No Review was run per operator instruction; no tags, release scripts or rc publication were run. All foreground children have been joined. Final publication, evidence-history and merge-tree checks are retained in /work/worktrees/task-4ed75787/.antiphon/task-4ed75787.md and the final-checks receipt.

--- next stage ---
next: land
handoff: Land the fixture repairs using the explicit operator no-Review instruction; retain Windows native and legacy composite qualification obligations. For rc/2026-10-24, use the four predecessor dependencies followed by this task's five code picks as compiled above. Commission the bounded offline-verifier optimization separately if a green frozen composite is required.
artifact: /work/worktrees/task-4ed75787/docs/investigations/2026-10-24-card-1572-final-reds.md
