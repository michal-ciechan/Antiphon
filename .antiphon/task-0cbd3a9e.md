Master top-up 42 port completed; all requested Linux merge checks passed. Ready for the caller's land.

Assigned branch: `feat/card-task-0cbd3a9e`.
Verified source tip: `7ba9c8625e68d9f40c00ffe95e700bbed581a5a6`. The final pushed branch tip includes this report-only successor and is supplied in the caller-facing progress receipt.
Base and fetched master: `bce716158d9a3fcca5bf548a07fdb257de637f1f`; rc source: `335b4fbb3b1eac700cb8d3c64ea032d9579c6b5f`. Master did not advance; no rebase, branch switch, merge, reset, amendment, force-push, master/rc write, tag, publication or deployment was performed. Only the assigned branch was pushed, after every cherry-pick.

## Picks and scope

Selected the `+` patch identities from `git cherry -v origin/master origin/rc/2026-10-27` within the brief's exclusive range `80b3a59c4..335b4fbb3`. All 25 scoped commits were absent and picked with `-x`. Excluded the range's lower boundary CARD-1610 witness `80b3a59c40cd18dea905829e428d35844cb89b8d`, commits outside that range, and report-only `335b4fbb3b1eac700cb8d3c64ea032d9579c6b5f`. No source land-graph regeneration commit was picked.

Series: S-C2 = first two rows; S-F = next two rows; aggregate Debug creation = fifth row; failed-first S1 = rows 6..25; final aggregate Debug receipt update = last row. The final update `a71d6e608a90cc4f742fc0cb24a607b5da503f19` changes only `docs/investigations/2026-10-30-card-1618-sf-aggregate-run-debug.md`; no `.antiphon` change existed in that source commit. The complete 35/35 Linux aggregate record and completed timings/receipts are now carried, with all its pending native/Full/R-2/S-F-cost limitations preserved.

| Source full SHA | Port full SHA | Subject |
|---|---|---|
| 6900106a0e5688e4612b2a8c3d252da2fccac594 | 73a06c6c12ed51dcb1682161865b9a8082fc174e | test: capture S-C2 shape fixture red witnesses |
| 4888ffd26dd8c83af0da2916a7ef6ac6ba5369e9 | 96399c2d761b581ee9a67d7fc3a84b33529f4a5d | test: port staged shape replacements to PowerShell fixtures |
| e3aa2d0220f362d8ad55e53355146a51187d836f | d70fcbce5a91a8df413a04b33fb614623799e1bf | test: pin CARD-1199 final closure documentation |
| 59b7e18bd4330e3fde315f4428b3606360e1d698 | 522ec3e32e985dfb1495fa691e70c92358724c70 | docs: close CARD-1199 runtime census and bind final audit checks |
| fbeef58c18b272f7f92668d24563d548922c3857 | b154687cd3774416214080583ab544bc627b44ff | docs: open CARD-1618 aggregate Debug record |
| 8c1bf2d192a2d5923e8dd0f2cca81ed42c7e8337 | c2141e7abf5154b782927a133829cf56bdb7f5ce | docs: carry reviewed failed-first qualification ordering plan |
| ff234cf16c213253958d37d416b055840224c520 | dad84fb0949134c52cba58ccc1b38af807f01afa | test: witness retained failure union against terminal-only baseline |
| fae697814c23857143777833e720b603a5a3f0f9 | d1fea3bef98f9d329c6b377e91b13692216f999e | test: witness fail-closed history resolution and provenance refusals |
| 2fe30509972cfd7bf1060cca5b112e0a9fcf7cb8 | 41cf06b4940b78009d730457395d8338776b1070 | test: bind synthetic diagnostic records to pinned MTP grammar |
| 5cf844859789fde76a5c0ded91249b5d50c10227 | be84cf01b0167ea541c33ff6d00352fd7f8d050c | perf: index baseline failure-history resolution by ordinal class identity |
| 326134dd8d7a5831e06a94a9e48d0e8eb05f4174 | 0af9029fde6e873d6372115d14221838827170f2 | feat: export bound prior failures and refuse incomplete partial resolution |
| 75520598fc8ba2b4c75b378294cce15a1d54b08c | 7bb086babb99a74614585c1950c05a4a4c889d5c | fix: refuse hard-linked evidence and retain coverage-incomplete fallback |
| b7bbfe53d22489a3e7c7cedfaad2fe13004d9aed | 1bc5a5925ca8ab9eadfbbcecbb81212e1d6e458a | test: retain failure-history phase and fixture evidence before harness cleanup |
| 7bc441c845f3ff8eefdbe812dac1fe0d18233ef8 | 2a8450e990cce53eebceb2773794c6c92db89d8c | docs: carry round-four failed-first qualification plan |
| 5a4901cc37073a022082bd31f2a7b7923a268385 | 14070c419331a2f30a6a01b8af3ec10cb1c2a878 | test: witness real history-profile exclusions and mismatches |
| d1c9651b2b3a6400640ce9a07f8f84a0808e5dfe | fedb83eaf40a4abd9ab40e99e7790a811b3b83fc | fix: bind partial history resolution to current profile exclusions |
| 55ea03faea9a402a0c4989df709d0c94e262edf9 | e6f83daa613a195242b379a0891730f2e3596720 | test: keep history control executable under Linux rc exclusions |
| f74ee7e4bfe4d5a36a3248877a8da99b5dc87b4a | 4c21f45a4475d22bee4663befe64e67d24a1ad22 | test: witness failed summary markers and short green TRX chunks |
| 232d9e406caffcc9a34f93947b6b395ac65436e2 | 7f1e29341a6cd22b5578655f9e61a22f9694f7d4 | fix: retain incomplete summary and short TRX chunk membership |
| a434cffb4c168136fdbe4b842f0765bc249a45a6 | 8f63c487029c93003c5c81d626b647cbe2980c8c | test: isolate missing TRX and each historical provenance comparison |
| 846e479f2f53b94c12b0e2ec8539e15203d9ca0d | ceb8c21e3116f071654fcdaeb2715a0d71fae574 | test: bind exclusion metadata for dictionary and object contexts |
| 29082754906b512277d3ae931b8d3f4d3953f81a | 3006ac946ce734707091d7c2e2727d6dad3df98b | fix: validate exclusion context bindings in both PowerShell object shapes |
| d2f98de40f240b0b7adda47e596d8aa40f9cd57d | b0af2d2d0cd24b3dd6e8d96f97929d2ff326c070 | test: expose ordinary failed chunk refusal with real exit codes |
| aa21b1a9bad3aca17a13c54cc617290cacb4ff31 | 92f9be5b78fe7aec4ab4e69218279c4400870487 | fix: reconcile failed chunk summaries against observed test identities |
| ceca4aee8bdabcb696e56dca0c52fd7c21c91616 | 5cff2cc07dcbd7c7806c8fd6d4b8a23a3692024e | fix: expose resolver warning capture through common parameters |
| a71d6e608a90cc4f742fc0cb24a607b5da503f19 | 7ba9c8625e68d9f40c00ffe95e700bbed581a5a6 | docs: record CARD-1618 S-F aggregate timings and receipts |

Changed source/document paths:

- ``docs/investigations/2026-10-30-card-1618-sf-aggregate-run-debug.md``
- ``docs/release-gates.md``
- ``docs/superpowers/plans/2026-10-30-card-1199-windows-runnable-plan.md``
- ``docs/superpowers/plans/2026-10-30-card-1514-failed-first-qualification-ordering-plan.md``
- ``docs/superpowers/plans/2026-10-30-failed-first-qualification-ordering-plan.md``
- ``scripts/export-nightly-prior-failures.ps1``
- ``scripts/lib/checkpoint-usage.ps1``
- ``scripts/lib/nightly-failure-history.ps1``
- ``scripts/test-nightly-failed-first.ps1``
- ``scripts/test-rc-platform.ps1``
- ``tests/Antiphon.Tests/Application/ReleaseBranchModelDocumentationTests.cs``
- ``tests/Antiphon.Tests/Checkpoints/CheckpointShapeConflictTests.cs``
- ``tests/Antiphon.Tests/Checkpoints/CheckpointShapeWindowsFixture.cs``
- ``tests/Antiphon.Tests/Scripts/RcFailureHistoryTests.cs``
- ``tests/test-execution-policy.json``
- ``tests/test-selection-policy.json``

## Conflicts and preservation

Actual conflicts: **zero**, across all 26 picks. The brief's full-branch merge probe predicted ten conflict paths, but the scoped patch picks applied cleanly. There are therefore no manual conflict resolutions to enumerate. Existing master roster/checkpoint logic and unrelated policy rows remain intact. The two policy-file deltas are exactly one script-census row, the three P-AUTHORITY paths and their derived hashes. `_PtyHostNativeProps` was not restored; `tests/Antiphon.Tests/Antiphon.Tests.csproj` remains byte-identical to master.

## Land-graph approval

Fresh evaluator built through `scripts/build-slot.ps1`, Release, `OutputPath=bin-port42audit/`, `UseAppHost=false`; 0 warnings, 0 errors. Ran the real `scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin-port42audit/Antiphon.LandGraphAudit.dll` through the gate with `TEMP=/tmp`.

Outcome: **approved**, exit 0, exactly one whole `approved` output line, including real evaluation and `phase=pass2`.

- Old digest: `7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99`, count **68**.
- New computed digest: `7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99`, count **68**.
- Pins blob: `d7513d002e3b9cd3ed95faf82ee26d037c9604a1`, unchanged; recorded/evaluated SDK `10.0.403`.
- No `-Update` was needed or run. No approval record or allow-list changed.

## Policy hashes

Recomputed on the final source tree with the owner functions `Get-TestSelectionCatalogHash` (`scripts/lib/test-selection.ps1`) and `Get-NightlyPolicyHash` (`scripts/lib/nightly-policy.ps1`, using `nightly-common.ps1`). Both independently reproduced the carried values; `Assert-TestSelectionCatalog` and `Read-NightlyExecutionPolicy` passed. No extra hash rewrite was needed.

- Base execution policy: `4bf17609dbf771a840e6af9f64b343ecd86c0439f8f4ca6dc3f40351c5cec148`.
- New execution policy: `1f68aa2002aee99fe38115ff620bdaa2830497009ed6b15b4a3c2b353dbdbee6`.
- Base catalog: `622274b4d95a49bb26d277fcee23085ab61339cc1372ab83799f79835a100c24`.
- New catalog and policy catalog binding: `00387e84f79db4135cf29345883c459e717d60427a7cf0345b326734248f3776`.

## Narrow Linux validation

One isolated `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-port42tests/ --property:UseAppHost=false -nodeReuse:false` under a granted build slot succeeded: **0 errors, 1,395 warnings**, elapsed **5m09.95s**. No base build was performed solely to attribute warnings. All test drivers were sequential and independently gated, with `TEMP=/tmp`, using `dotnet exec tests/Antiphon.Tests/bin-port42tests/Antiphon.Tests.dll` (inherently no build), exact requested filters, TRX and detailed logs. No full suite, extra mutation cycle or native Windows test was run. All source stayed committed and unchanged during the builds/tests.

Build provenance (SHA-256 before output cleanup):

- `42de5fcc425e5a6e5d605ab7150d9d02632d8e39cabc18f040b574d9cff8f495  tests/Antiphon.Tests/bin-port42tests/Antiphon.Tests.dll`
- `a20f0b5d09ad0e15e32aa2992be740e76475c3e0d50222e063f14445135a0ac2  tools/Antiphon.LandGraphAudit/bin-port42audit/Antiphon.LandGraphAudit.dll`

| Selection | Exit | Passed | Failed | Skipped | Expected | Wall seconds including slot wait |
|---|---:|---:|---:|---:|---:|---:|
| CP-36 | 0 | 3 | 0 | 0 | 3 | 184.18 |
| CP-45 | 0 | 7 | 0 | 0 | 7 | 314.51 |
| CP-54 | 0 | 1 | 0 | 0 | 1 | 70.23 |
| FF-CP-1 | 0 | 1 | 0 | 0 | 1 | 113.41 |
| FF-CP-2 | 0 | 1 | 0 | 0 | 1 | 229.93 |
| C1323 | 0 | 1 | 0 | 0 | 1 | 78.25 |
| C1313-Universe | 0 | 1 | 0 | 0 | 1 | 79.23 |
| C1313-Pinned | 0 | 1 | 0 | 0 | 1 | 75.51 |
| Roster | 0 | 3 | 0 | 0 | 3 | 271.29 |
| WindowsAdmission-Linux | 8 | 0 | 0 | 0 | 4 | 41.12 |
| Census | 8 | 0 | 0 | 0 | 5 | 70.33 |
| WindowsAdmission-Linux-corrected | 0 | 4 | 0 | 0 | 4 | 78.23 |
| Census-corrected | 0 | 5 | 0 | 0 | 5 | 84.06 |

Per-class execution counts include CP-36 methods intentionally repeated in CP-45:

| Class | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Antiphon.Tests.Application.LandNonCodeSkipTests | 1 | 0 | 0 |
| Antiphon.Tests.Application.ReleaseBranchModelDocumentationTests | 1 | 0 | 0 |
| Antiphon.Tests.Checkpoints.CheckpointFixtureGitEnvironmentTests | 3 | 0 | 0 |
| Antiphon.Tests.Checkpoints.CheckpointNamespaceCensusUsageTests | 2 | 0 | 0 |
| Antiphon.Tests.Scripts.C1514LandingSplitBudgetTests | 5 | 0 | 0 |
| Antiphon.Tests.Scripts.RcFailureHistoryTests | 2 | 0 | 0 |
| Antiphon.Tests.Scripts.RcRosterRepairTests | 3 | 0 | 0 |
| Antiphon.Tests.Scripts.ReleaseGatePlatformTests | 5 | 0 | 0 |
| Antiphon.Tests.Scripts.TestSelectionCatalogTests | 1 | 0 | 0 |
| Antiphon.Tests.Scripts.TestSelectionTests | 1 | 0 | 0 |
| Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests | 4 | 0 | 0 |

Verification correction (introduced by this pass, resolved): the initial `WindowsAdmission-Linux` and `Census` filters used exact-name OR operands without the documented wildcard suffix. Both returned exit 8 and zero tests; neither supplied acceptance evidence. They are retained in the table and raw receipts. Ran each missing selection once using the documented wildcard OR form for the four admission methods and the five-method census class. Both corrected runs supplied the required nonzero counts. No source, assertion or timeout was changed.

WindowsUnitPlatformAdmissionTests selects only its four portable/Linux cases, omitting the Windows child-launch case. Census covers exactly C1404, C1577, C1584, C1410 and G2865. Failed-first CP-1 and CP-2 each ran their exact method; the receipt counts above represent TUnit results, with the harness assertions inside those methods. No failure was hidden or relabeled, and no assertion retry, timeout widening or assertion loosening was made.

## Final checks, artifacts and handoff

`git diff --check` passed. `scripts/check-evidence-diff.ps1 -BaseRef bce716158d9a3fcca5bf548a07fdb257de637f1f -HeadRef HEAD` passed with zero violations. Fresh `git fetch origin master` followed by `git merge-tree --write-tree origin/master HEAD` was conflict-free; its exact output is in `.antiphon/port-42/merge-tree.log`. Final remote-tip verification and the full final branch SHA are supplied in the caller-facing progress receipt.

All task-created `bin-port42tests` and `bin-port42audit` directories were removed by their exact literal paths after all children finished. Raw logs, TRX, summary JSON, hash evidence and mapping TSV remain gitignored under `/work/worktrees/task-0cbd3a9e/.antiphon/port-42/`; this Markdown is the committed durable summary. No deletion outside task scratch/output paths was performed.

Standing authority covered this server2 port and its narrow checks; no additional approval was needed. The Windows rc qualification remains the acceptance gate. The caller owns landing via `delegate.ps1 -Land 0cbd3a9e-ac14-4f13-9846-e29c5ffc9821 -ExpectedSourceSha <final-pushed-tip>`; this task has not activated any host.

--- next stage ---
next: land
handoff: Land the pushed assigned branch using the final SHA in the caller-facing progress receipt; preserve the Windows rc qualification gate and the aggregate record's pending native work.
artifact: final pushed branch SHA supplied in the caller-facing progress receipt
