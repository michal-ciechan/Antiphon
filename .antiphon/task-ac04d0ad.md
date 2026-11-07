# Merge batch 112 and rc/2026-11-09

Master landed. `rc/2026-11-09` was not created.

origin/master and origin/feat/card-task-ac04d0ad were both `0e7aef0379a073c206e8398dcaed539bc33eaf9b` before this report commit. The group-79 fix `aa22def08257992c6c2200bbfd11f3b48205c3e6` is on that master. The rc ref was still absent at the stop. `Test-ReleaseGateCandidateRef -Ref rc/2026-11-09` returned Ok, Kind=long-lived. No script refused the name. The candidate id that would have been cut is `rc-2026-11-09-0e7aef0379a0`. It was not published.

## Why the cut stopped

`C599A_ExecutionLedger` exceeded its 5-minute harness budget. The run was once, as required. Five sibling methods passed. Four of five required rows in the failed method had already passed (`V-12`, `G-226`, `G-287`, `G-288`); the budget expired before `G-289`. Host slots were full for much of the run. This method was not edited by batch 112. The cut recipe allows the push only when the tree checks pass, so `git push origin 0e7aef0379a073c206e8398dcaed539bc33eaf9b:refs/heads/rc/2026-11-09` was not run.

Durations versus 300s:

| Method | Result | Duration | Versus 300s |
|---|---|---|---|
| C599A_ExecutionLedgerCounts | Passed | 00:01:16.721 | under |
| C599A_ExecutionLedgerJoins | Passed | 00:02:40.389 | under |
| C599A_ExecutionLedgerMarkers | Passed | 00:03:32.985 | under |
| C599A_ExecutionLedgerReceipt | Passed | 00:03:36.313 | under |
| C599A_ExecutionLedgerChunks | Passed | 00:04:07.750 | under |
| C599A_ExecutionLedger | Failed | 00:05:00.202 | over (budget) |

Lease `ef193da2-970c-4b63-b97c-e9fca49d82f3`, waited 0s, held 1222s. Total 6, failed 1, succeeded 5. TRX was `.antiphon/batch112-ledger/batch112-ledger.trx`.

## Part 1

origin/master moved once before the first push, from `6d5531762633dbb9077739cb451386c6c2131255` to `5322f3d2012e60a04afce2e0db9dd5263e47b491` (in-process git, session-runner git, park/seat fixtures, slow-group notes, land-graph re-record for `Antiphon.GitInProcess`). The unpushed first pick was discarded and the same nine sources were cherry-picked `-x` onto `5322f3d2`. That was loop 2 of 3. The fast-forward push was accepted on the first attempt of the replay: `5322f3d20..0e7aef037 HEAD -> master`. The task branch fast-forwarded `6d5531762..0e7aef037`.

Published commits, oldest first:

| Land | Source | Subject |
|---|---|---|
| `aa22def08257992c6c2200bbfd11f3b48205c3e6` | `da3c1fa8ae9c957e9f7d5bd2241831cac2892253` | group-79 Windows card-file git stall uses the product timeout |
| `ecc08e75b0688062aae3b9d55050e65ec17bb0e4` | `ac82170ac87349f7f3d1ea683bcd6da8f0807101` | CARD-1406 compose refusal witness |
| `0853f0266ba1ce65522ecdc4ff22f91eab5584cd` | `b610307c0dd5fc75a78ba5dceb8f1cec9c9fa65c` | CARD-1406 host Postgres compose and drill plan |
| `8ea3df2522b615895fbf1bfeb9fd41240009ce8f` | `282d5b96bd34a6494f4e06561716865015ca36a4` | CARD-1406 scratch drill and network block |
| `2f188806cea4a45f2f7cc09dc782ac8d1f58820c` | `1cdb72fb8cdee224dcf0ebaeea116374f1bbf4e3` | CARD-1406 scratch drill isolated from the production volume |
| `057d141287a4a962ebd0405ed52e1b8cdc1c0736` | `613eaade65dceb9c9784a8aec7caedd5d4e16e60` | CARD-1406 encrypted drill and quoted psql witness |
| `2e43ac0f940b4d61ba8e91da130b536275d20b52` | `800c968adbc23c72e1755495e22f78962b6f8d1f` | CARD-1406 encrypted drill and escaped migrations query |
| `462e3e3b4568a4076d14c3eb9571fe94abb298ea` | `a06d96a5015e08d43810970d1e319e95bb1bfe21` | CARD-1746 broker stays on the existing server2 cluster |
| `0e7aef0379a073c206e8398dcaed539bc33eaf9b` | `12f86542fce96f1fcd2b0a17778bd811ce5e8ee1` | desktop server and session-runner CPU trace |

CARD-1406 was kept. The group-79 whole-commit stable patch-id differed only because `slow-tests-allowlist.txt` auto-merged: master already had the CARD-1734 `CheckpointColdBuildTests` row, so hunk context differs. The added allowlist lines match. The other eight patch-ids matched their sources. Evidence: `EVIDENCE result commits=9 entries=0 violations=0 base=5322f3d2012e60a04afce2e0db9dd5263e47b491 head=0e7aef0379a073c206e8398dcaed539bc33eaf9b`. `tests/test-execution-policy.json` was not in the nine-commit replay, so the hash was not refreshed.

Checks at `0e7aef0379a073c206e8398dcaed539bc33eaf9b`:

- Solution build `dotnet build Antiphon.sln --property:UseAppHost=false`: 0 errors, 1965 warnings, elapsed 00:03:02.46. Lease `2a9ca464-64b0-44df-b764-420e0644a1e1`, waited 0s, maxcpucount 6.
- Batched guards: 217 total, 217 passed, 0 failed, 0 skipped, 1m 37s. `CardFileGitFailureAcceptanceTests` 7, including the Linux timeout case. `C1729LayeringGuardTests` inventory `VIOLATIONS 0 LISTED 168 STALE 0 AMBIGUOUS 3`. Lease `32bdaf8c-b916-40aa-95e6-19f697515c81`.
- `scripts/test-server2-postgres-compose.ps1`: `RESULT passed=279 failed=0`. Lease `d45b88a2-8d2a-4975-8c1e-fb4fcdfe46ea`.
- Land-graph `-Check` exit 0. `digest=7DB56208B8BA199080B89E0F499C5487BC659D761535B5BFB7DFA9E74FEAEEF1 count=70`, phase=pass2, approved. Lease `d970dafe-aa84-482f-b1c7-f092fc53f92a`. `-Update` was not run. Count is 70 because `Antiphon.GitInProcess.csproj` landed on master before this batch. The earlier unpushed tree's digest `206F953DC44801E217D75850575B2A3837E2AED28B6FAAB941ABA2F12803221E` count 69 is not the published tree.

## Part 2 prechecks at the pin

- A1: `git ls-remote origin refs/heads/rc/2026-11-09` was empty immediately before the decision not to push, and still empty after the ledger failure.
- A2: `git cherry 0e7aef0379a073c206e8398dcaed539bc33eaf9b` against `origin/rc/2026-11-08` (`d164e93ae47a79ed175beeb9367212f7a7d5d1d5`), `origin/rc/2026-11-07` (`1038d07e66a54b630a1bc1d394923bc7049b73dd`), and `origin/rc/2026-11-06` (`5a95ae7a5611eedb56c3bea023bb7a4b4fb6683a`) each reported plus=0 minus=0.
- A3: stored `policyHash`, `Read-NightlyExecutionPolicy`, `Get-NightlyPolicyHash -Object`, and `test-live-lane-manifest.ps1 -Case EmitPolicyHash` are all `f7b7f7534ca39270a26e8b7da7b6a171690940fb64d7c6b45f9b0462d99b44e1`. Stored `PinnedInputDigest` and `scripts/test-live-lane-run.ps1 -PrintInputDigest` are both `84003c9c0020549a4ad33e1cf2b480500bb17023fef9952e7794a0287ce57edc`.
- A4: the `-Check` above, exit 0, digest `7DB56208B8BA199080B89E0F499C5487BC659D761535B5BFB7DFA9E74FEAEEF1`, count 70.
- A5: `LinuxOnlyWindowsExclusionPolicy` C487 6 passed, 0 failed, exit 0 (lease `9924300b-a7c6-42d4-8422-d7168744e128`). `C1686_LinuxOnlyCensus` C487 62 passed, 0 failed, exit 0 (lease `92211842-4a94-421e-8abe-796eada0d303`).
- A6: `scripts/test-rc-coverage-admission.ps1` C487 8 passed, 0 failed, 8 rows, exit 0 (lease `9e71bbbe-8fe4-4d08-9dba-3c21ba54b0a2`).

Tree smokes at the same SHA, `--no-build`, `UseAppHost=false`. The solution build above is the build for this pin.

- Part-2 class batch, lease `3502bcb4-276f-4499-85fa-d9c8454d4c89`, waited 15s, held 2745s: total 591, failed 0, succeeded 581, skipped 10, duration 45m 41s. Skips are Windows-only: `C0824DesktopLegacyReleaseNativeTests` 8, `LandingRemovalPolicyControlTests` 2. Passed counts: `AgentTaskLandBoundaryControlledTests` 94, `AgentTaskLandBoundaryTests` 38, `AppDbContextModelTests` 3, `C0824DesktopLegacyReleasePolicyTests` 123, `C0824DesktopLegacyReleaseSettingsTests` 34, `C0824DesktopLegacyReleaseSwitchTests` 30, `C0824WorkspaceSlotServerTests` 24, `C1217ConditionalSkipContractTests` 8, `C1677RetentionServerTests` 71, `CheckpointColdBuildTests` 3, `CheckpointTerminalReceiptTests` 10, `LandingRemovalPolicyControlTests` 132, `ProjectReadinessBatchTests` 2, `RcFailedFirstExecutionTests` 5, `RcFailedFirstPlannerTests` 2, `RcQualificationWatcherScriptTests` 2. S1 validators are `C1217ConditionalSkipContractTests` only.
- SessionRunner CP-4, lease `16e584b2-f9fc-42d1-975c-96e465c20b68`: `NativeLinuxPolicyWitnessTests` and `TestClassificationGuard` total 4, failed 0, duration 1s 342ms.
- Overlapping policy, slow-test, mechanical, guidance, and C1729 classes were already in the 217-guard run at this SHA.

## Changelog versus rc/2026-11-08 `d164e93ae47a79ed175beeb9367212f7a7d5d1d5`

122 paths. No file under a `Migrations/` directory. Hosts apply no new EF migration at startup.

- Product: CARD-1217 strict XML and TRX receipt projection (`src/Antiphon.HostCleanup/Checkpoints/ReceiptFile.cs`, `tools/Antiphon.Checkpoints/*`); CARD-1734 terminal receipts, cold-build custody, PtyHost artifact switches, and the land-graph pin/digest re-record; opt-in workspace-slot rollout; in-process git reads (`src/Antiphon.GitInProcess/*`, server git services, session-runner git); CARD-1406 server2 Postgres compose, drill compose, and runner compose. Land-graph files changed with those product edits.
- Tests: checkpoint and CARD-1734 witnesses, park status config, seat fake-time jump, group-79 card-file timeout, docker-stack contract coverage, and the earlier test-only lands between the rc tip and this pin.
- Scripts: checkpoint, workspace-slot, and `scripts/deploy-server2-postgres.ps1` plus `scripts/test-server2-postgres-compose.ps1`.
- Docs: CARD-1746 broker decision, the CPU trace, docker-stack and testing-and-build drill notes, slow qualification groups 17/126/128, and the CARD-1734 land records.

## Owed

Whole Unit lane. Windows rerun of `CardFileGitFailureAcceptanceTests` after the native lock frees. CARD-1406 operator drill. Positive controls for any new tests. Advisories A-11 through A-24 were named by the brief and were not present on the four subject tips, so this report does not invent their text.

## Next

Do not commission the Windows failed-first run against `rc/2026-11-09`. That ref does not exist. Host activation is a separate decision. The open choice is a rerun of `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` alone, or an explicit decision to cut `rc/2026-11-09` at `0e7aef0379a073c206e8398dcaed539bc33eaf9b` despite that budget miss.
