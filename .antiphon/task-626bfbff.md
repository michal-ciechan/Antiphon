# CARD-1613 implementation report

Linux checkpoint `20261029-210439-b9cf` is GREEN, exit 0, on `aa72491b72b1af57141e7c37ac50a384485755a0`. Ten rows, 73 parents, 0 failed, 0 skipped. All 45 witnessed rows migrated. Windows CP-9, CP-10, and CP-11 remain for a Windows host.

## Parents

executed/passed/failed/skipped (minimum). Source was clean and `buildSource` verified. Wall 18m40s.

| CP | Result | Minimum |
|---|---|---|
| CP-1 | 3/3/0/0 | 3 |
| CP-2 | 10/10/0/0 | 10 |
| CP-3 | 2/2/0/0 | 2 |
| CP-4 | 4/4/0/0 | 4 |
| CP-5 | 4/4/0/0 | 4 |
| CP-6 | 4/4/0/0 | 4 |
| CP-7 | 17/17/0/0 | 17 |
| CP-8 | 23/23/0/0 | 23 |
| CP-12 | 3/3/0/0 | 3 |
| CP-13 | 3/3/0/0 | 3 |

CP-1 passed `Policy_rows_require_pinned_attribute`, `Pinned_attribute_contract_matches_native_platform`, and `Markers_do_not_expand_policy_exclusions`. Receipt: `.antiphon/checkpoints/20261029-210439-b9cf/report.md`. The tool deleted `bin-c1613-ant`, `bin-c1613-runner`, `bin-c1613-pty`, and `bin-c1613-agents`.

## Red proof and earlier runs

RED-1 `20261029-202015-b998` on `5f018bc7bc731dc1c07fe2be19e0b8b6b8172f86`: executed 1, failed 1, skipped 0, expected missing-marker. `Policy_rows` red-to-green count is 1.

`20261029-204647-b15e` exited 2 because `bin-c1613-*` directories already existed (`pre-existing-output`, 0 tests). `20261029-205423-6cce` exited 2 on CS0246 in `NativeLinuxPolicyMatcherParityTests` (`ProcessSpawnLimit` had no `using Antiphon.Tests.TestHelpers`). On that older SHA, CP-4, CP-5, and CP-6 were 4/4. `aa72491b` adds the using. The tip run replaced both attempts.

## Commits

- `1e6e306ac6699fb8df47960d4a289e0ac158fe37` docs: record the plan
- `5f018bc7bc731dc1c07fe2be19e0b8b6b8172f86` S1 witness before annotation
- `4809ad45191363868f5038ee8cb125e39b5d1ace` S2 pin the 45 rows
- `d62cb17d5a32dfc712ad1e911f94cd47a38ddf38` S3 retire the lexer
- `aa72491b72b1af57141e7c37ac50a384485755a0` import `ProcessSpawnLimit`

## Deviations

1. S1 is one commit, then one RED-1 of `Policy_rows_require_pinned_attribute`. The compiled refusal fixtures were green in S1.
2. HostStats keeps the accepted macOS reason narrowing. The Windows reason string stays `Windows`.
3. `*.cs.txt` fixtures are embedded as `NativeLinuxPolicy.*` through `KeepNativeLinuxPolicyFixturesInMainAssembly`, because AssignCulture treats `*.cs.txt` as Czech.

## Seals and publication

Annotation audit: checked=46 fail=0. `policyHash` `bf703bb14839c0e93c6f3516b2d5dd69288eea73063c36e02396af9fbcde61f7`. Attribute pin `31ca5740f4127fe7b3b0fc18639e75262cdb3ae49c3bc545e62a130b99d68ac4` is unchanged. Probe child smoke was 9 passed of 9. CP-3 then passed both parents. Evidence check: 5 commits, 0 violations, base `8edda999623a473be2d1970727c172bfb5088d59` through the implementation tip above. The report commit, if present, is this file only.

`merge-tree --write-tree` against `origin/rc/2026-10-27` `ade54222543381e2e1b31ad79d8ae33a4035f70b` exited 0 (ahead 5, behind 6). This task left the branch at the pushed tip. `merge-tree` against `origin/master` `0e7dd390520a43ab330396a72e9085a8691ac332` exited 1 with 14 conflicts, left for the later master port:

- `.antiphon/task-af2b78c8.md` (add/add)
- `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md` (add/add)
- `docs/release-gates.md`
- `scripts/test-rc-platform.ps1`
- `server/Application/Services/CapacityRecoveryService.cs`
- `tests/Antiphon.Tests/Application/CapacityLockOrderTests.cs` (add/add)
- `tests/Antiphon.Tests/Application/UnixDelegateLaunchArgvTests.cs`
- `tests/Antiphon.Tests/Checkpoints/AnchoredPathDeleteWitnessTests.cs` (add/add)
- `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`
- `tests/Antiphon.Tests/Infrastructure/FileUseCaptureTests.cs`
- `tests/Antiphon.Tests/Scripts/ReleaseGatePlatformTests.cs`
- `tests/Antiphon.Tests/Scripts/RetiredTempContainerScriptTests.cs`
- `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs`
- `tests/test-execution-policy.json`

CP-7 recorded SLOW CLASS `C1514LandingSplitBudgetTests` 87s and `ReleaseGatePlatformTests` 67s. Both rows passed. The CP-9 duplicate-build import warning is the warning the plan expects. Caller authority covered this Linux implementation.

## Next stage

next: review
handoff: Review the implementation tip named above. Linux CP-1 through CP-8, CP-12, and CP-13 are green (73 parents). RED-1 is `20261029-202015-b998`. Windows CP-9, CP-10, and CP-11 remain. All 45 witnessed rows migrated. rc merge-tree is clean. Master merge-tree has the 14 conflicts listed here. Landing rebases.
artifact: `.antiphon/checkpoints/20261029-210439-b9cf/report.md`
