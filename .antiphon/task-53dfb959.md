# CARD-1412 Code report (task 53dfb959)

Outcome: test-only change; `LandDeliveryWaitRoutingTests` now pins the renewal body and the
comment-stripped owning call, so M2b, M5 and M6 each turn exactly one case red. No product change.

- Branch `feat/card-task-53dfb959`, worktree `/work/worktrees/task-53dfb959` (server2 mirror).
- Base `294fa3d1a81ff97abb285af3180d93ee4d4b25f6`; verified source tip
  `395d343c119ba1eab39776ef92fcc5841dad8640`; this report commit follows it (report-only).
- Plan: `docs/superpowers/plans/2026-10-22-card-1412-wait-routing-renewal-plan.md` (commit `395d343c1`).
- Test: `tests/Antiphon.Tests/LandDeliveryWaitRoutingTests.cs` (commit `52cd17ddb`).

## Change

`NearestWait` strips `//` and `/* */` comments (string, char and verbatim literals kept) and resolves
the nearest preceding `UntilAsync(`/`UntilProtocolAsync(`/`UntilDispatchAsync(` whose parentheses
enclose the evidence string. Every assertion of the existing method is kept. New cases:

| Case | Guards | Pin |
|---|---|---|
| `Plain_waits_pass_no_progress_probe` | M2b | each plain evidence's owning call is `UntilAsync(` with at most 3 top-level arguments and no `progress:` |
| `Renewing_helpers_forward_the_protocol_probe_to_the_shared_wait` | M5 | `UntilProtocolAsync` inner call arg 4 is `ProtocolProgress`; `UntilDispatchAsync` inner call arg 4 is `() => ProtocolProgress() + dispatch`, wrapper refreshes `dispatch` from `DispatchProgressAsync()` |
| `A_comment_naming_another_wait_does_not_route_the_evidence` | M6 | stripper and owning-call resolver on synthetic text; evidence outside a call's parentheses has no owner |

A runtime witness was not chosen: `Antiphon.Tests` does not reference `Antiphon.E2E`, and
`UntilDispatchAsync` needs the fixture's database context; the renewal mechanics themselves are
already behaviourally covered by `ProgressAwareWaitTests`. The card names the renewal-body pin
with comment stripping as the accepted alternative.

## Red witnesses (brief-required, unlisted, one mutant at a time on the working tree, restored with `git checkout` after each)

Build: `scripts/build-slot.ps1 -Label c1412-witness-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c1412w/ --property:UseAppHost=false`
lease=ab752bcd-6b65-402a-963e-605ac351217b waited=0s maxcpucount=6, exit 0 (pre-existing nullable warnings in other projects only).
Each run: `timeout 300 scripts/build-slot.ps1 -Label c1412-witness-<m> -- dotnet exec tests/Antiphon.Tests/bin-c1412w/Antiphon.Tests.dll --treenode-filter "/*/*/LandDeliveryWaitRoutingTests/*" --report-trx --results-directory .antiphon/c1412-witness/<m>`.
The E2E project was not rebuilt (the test reads source text); each mutant compiles by inspection.

| Witness | Mutant | Lease / waited / held | Result | Red case | TRX |
|---|---|---|---|---|---|
| W-M2b | `LandDeliveryFixture.Dispatch.cs` `TwoNotificationScansAsync`: `"two completed notification scans", 60, ProtocolProgress);` | 4a94ffc7-5e4a-4e41-b92c-4b310e44b534 / 0s / 2s | total 4, failed 1, passed 3 | `Plain_waits_pass_no_progress_probe` at `LandDeliveryWaitRoutingTests.cs:81` (argument count) | `.antiphon/c1412-witness/m2b/_0c18bb7f591e_2026-10-22_06_40_19.6448624.trx` |
| W-M5 | `LandDeliveryFixture.Dispatch.cs:187`: `() => ProtocolProgress() + dispatch` -> `() => dispatch` | 89f25ca0-43dd-4d18-8e8f-64c7b3273009 / 0s / 2s | total 4, failed 1, passed 3 | `Renewing_helpers_forward_the_protocol_probe_to_the_shared_wait` at `:103` (probe pin) | `.antiphon/c1412-witness/m5/_0c18bb7f591e_2026-10-22_06_40_32.0762022.trx` |
| W-M6 | `DispatchBaseWarningDeliveryE2ETests.cs:130`: `f.UntilDispatchAsync(` -> `LandDeliveryFixture.UntilAsync(` plus `// UntilDispatchAsync( UntilProtocolAsync(` inserted before the key-retention evidence | 6e6094dd-d139-4962-be06-584cb5a8fb74 / 0s / 2s | total 4, failed 1, passed 3 | `Protocol_bound_waits_renew_on_git_progress_and_scan_waits_do_not` at `:58` (queue-cut pin) | `.antiphon/c1412-witness/m6/_0c18bb7f591e_2026-10-22_06_40_44.6075000.trx` |

`git status` clean after each restore. All 28 `bin-c1412w` directories deleted afterwards.

## Checkpoints (closed list, checkpoint tool, server2)

`dotnet tools/Antiphon.Checkpoints/bin-c1412w/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-22-card-1412-wait-routing-renewal-plan.md --expected-source-sha 395d343c119ba1eab39776ef92fcc5841dad8640 --max-wait 570s` (tool built by the witness build above; the row took its own slot). Run `20261022-064127-55a8`, exit 0 on the first wait.

```
source: 395d343c119ba1eab39776ef92fcc5841dad8640 state=clean buildSource=verified
CHECKPOINT CP-1 commit=395d343c119ba1eab39776ef92fcc5841dad8640 build=ok filter=/*/*/LandDeliveryWaitRoutingTests/* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-53dfb959/.antiphon/checkpoints/20261022-064127-55a8/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=395d343c119ba1eab39776ef92fcc5841dad8640 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=361.4706857s startup=6.17556s testsWall=0.4367053s teardown=1.2550516s hostWall=7.8677465s
unlisted: none (the tool ran no other build or test command)
wall: 6m44s  sequential-equivalent: 0m09s  builds: 1  max-concurrent-builds: 1  rows: 1 green 0 red 0 skipped
outputs: deleted bin-c1412-1/
verdict: GREEN exit=0
CHECKPOINT SOURCE VALID source=395d343c119ba1eab39776ef92fcc5841dad8640 rows=1
```

CP-1 roster (TRX): `A_comment_naming_another_wait_does_not_route_the_evidence`,
`Plain_waits_pass_no_progress_probe`, `Protocol_bound_waits_renew_on_git_progress_and_scan_waits_do_not`,
`Renewing_helpers_forward_the_protocol_probe_to_the_shared_wait`; counters total=4 executed=4 passed=4 failed=0.
Covers V-1, V-2, V-3, R-1. Reruns 0; repeat budget unused. Unlisted runs: the witness build and three
witness runs above (brief-required red proofs), nothing else.

## Pending for Mutation (method-scoped SourceLanding)

PC-1 W-M2b -> `Plain_waits_pass_no_progress_probe`; PC-2 W-M5 ->
`Renewing_helpers_forward_the_protocol_probe_to_the_shared_wait`; PC-3 W-M6 ->
`Protocol_bound_waits_renew_on_git_progress_and_scan_waits_do_not`; PC-4 (not run here) drop
`ProtocolProgress` from `UntilProtocolAsync`'s inner call (`LandDeliveryFixture.cs:368`) ->
`Renewing_helpers_forward_the_protocol_probe_to_the_shared_wait`.

## Landability and RC

- `git merge-tree --write-tree origin/master HEAD` at origin/master `3fa8d7bd480d7df345aea2bb46c042be19f78a76`: exit 0, no conflicts (tree `55ba403a`).
- Evidence guard `check-evidence-diff.ps1 -BaseRef 294fa3d1a -HeadRef 395d343c1`: commits=2 entries=0 violations=0, exit 0 (re-run over the report tip in the final message).
- RC: yes, this belongs on `rc/2026-10-20` as well. The rc tip `ceb6e3d6b78471db6831258699c493b8eb2d619b` already carries CARD-1411 (`806b23084`) and the five pinned files are byte-identical on rc and master, so the test is valid there unchanged. Cherry-pick simulation: `git merge-tree --write-tree --merge-base 294fa3d1a origin/rc/2026-10-20 52cd17ddb` exit 0 (clean) and the plan commit `395d343c1` on top also exit 0. Pick the test commit `52cd17ddb` alone; rc carries no plan docs.
