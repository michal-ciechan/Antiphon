# CARD-1994 Final review, round 1

Verdict: CLEAN. No blocker and no regression that holds the land.

Reviewed source: `b8bd8536ac0414c8bb621d48c7f63aaa79b64d64` on `origin/feat/card-task-449a31e0` (Code task `449a31e0-fbca-43ac-a8b5-8f9b3f7c5281`). Range `3c388417552651de6f2800e7b09ecea361a00f94..b8bd8536ac0414c8bb621d48c7f63aaa79b64d64` (4 commits). `origin/master` is `5b8e7ab8fb02d53b62e40b56f6e2ee85a541ef8a`. `git merge-tree --write-tree origin/master HEAD` is clean, tree `a8e5e81c4b7988898a89cf7e790818dd043d8b35`.

This is server code. It runs only after the next rc cut and an AppHost restart. This review did not restart anything and did not land.

## What the change does

A normal MSBuild exit (0 or 1) marks the land-verify generation `ready` as soon as the build child returns, before tests. Any other exit, a null exit, cancellation, or a kill that throws leaves the generation `building`. The next Prepare writes `quarantined` and starts a new generation. A build-slot timeout that returns before any build child also marks the generation `ready`. Prune keeps the two newest set-aside generations and deletes only exact generation paths inside that root. `current` stays the master and no-target pointer, `current-rc` is shared by every `rc/*` branch, and any other branch gets `current-` plus 16 hex characters. Prepare reads the state file before the tree scan.

## Judgment

1. Exit 0 or 1 cannot publish a broken tree on that land. `ProcessResult.Ok` is exit 0, so exit 1 returns `Failure("build", ...)` and tests never start (`AgentTaskLandService` only runs tests when `build.Ok`). A normal compile error does not emit, so the previous DLL keeps its older timestamp and the next build of a changed project recompiles it. A later land of the same broken sources fails again. A later land of fixed sources recompiles because the sources are newer than that DLL. The Windows case where killing the root `dotnet` process reports exit 1, and MSB4166 when a worker node dies, can also exit 1 and can leave a newer partial output. Requiring the summary line `Build FAILED.` does not separate MSB4166, because that failure prints the summary and still exits 1. That edge is unobserved here and is an advisory below. It is not a false pass of a normal compile error.

2. Prune is bounded. Candidates are direct children whose name is a 32-hex Guid `N`, not named by the active generation or by any `current*` pointer, with no link on the path, this repository's `owner.json`, and state `quarantined` or `building`. The parent check requires `GetDirectoryName(GetFullPath(path))` to equal the root. Delete is `Directory.Delete` of `Path.Combine(root, id)` only. A generation-named symlink is retained. A foreign owner, a non-guid name, and the active generation are retained. `C1994_PruneKeepsTwoNewestSetAsideAndNeverTheActiveOrAnEscape` ran on Linux and kept the outside target. The land holds `LandExecutionLease` across `RunLeasedAsync`, so a second land of the same repository gets `land_execution_busy` instead of pruning concurrently. A prune exception is a diagnostic and does not force a cold land.

3. Pointers. Empty, null, and `master` (after stripping `refs/heads/`) keep `current`, so an existing warm master generation survives deploy. The first `rc/*` land after deploy is cold and later `rc/*` lines share `current-rc`. Other branches use a 64-bit prefix of SHA-256. A collision at that width is not a practical land failure. The match is ordinal and case-sensitive; production refs are `refs/heads/master` and `refs/heads/rc/...`. Both verify call sites pass the target: `TargetRef: op.TargetFullRef` and `TargetRef: ticket.Identity.TargetRef`.

4. The ready mark is read before `RequireUnlinkedTree`. A `building` or `quarantined` generation is set aside without the scan, so a link inside it cannot force a cold land. `C1994_SetAsideReadsTheMarkBeforeScanningTheGeneration` covers that.

5. The reversed assertions still test the old obligations. `C1380_StaleTrxCannotPassASecondRun` fails the second run, keeps two result directories, keeps the old TRX, and has no new TRX. The third successful run reuses the same build path. `C1380_FailedBuildRefusesPublicationAndReleasesExecutionLock` still requires verification false, publication `Refused`, reason `verification_failed`, an unchanged target SHA, and a reacquired execution lock. The state expectation is now `ready`.

6. Red then green. `57fee22d9504808d7b22787e0d938381bf7fc911` adds the witnesses before the production change in `d7a6cf3eb30d9b1bf410f794443fa3acec2826c4`. This review did not re-execute the red expectation at the tip, because those tests pass there. The fresh green run is CP-2.

## Fresh execution

Checkpoint run `20261118-161101-cfc9` at `b8bd8536ac0414c8bb621d48c7f63aaa79b64d64`, source state clean, build provenance verified. One build (`bin-c1994-proof/`), slot granted, load 1.52 before the run. Tool verdict RED only because the subject-SHA CP-5 row failed.

| Row | Result | Wall |
|---|---|---|
| CP-2 `C1994_*` | executed 7, passed 7, failed 0, skipped 0 | tests 0.41s, build 271s, wall 10.3s (budget 15 min) |
| CP-3 warm + slot classes | executed 33, passed 33, failed 0, skipped 0 | tests 14.1s, wall 33.9s |
| CP-4 layering + lane category | executed 12, passed 12, failed 0, skipped 0 | tests 1.0s, wall 21.3s |
| CP-6 guard batch | executed 69, passed 69, failed 0, skipped 0 | tests 52.5s, wall 62.0s |
| CP-5 `-Check` on the subject SHA | exit 1 in 2.4s | committed digest `7B94…` versus live `98B4ECF0E3E8C7F4F710C5576356C825E0BABC52EC746F30F76791AD70BA8ABD` count 80 |

CP-6 is `TestClassificationPolicyTests`, `TestClassificationGuardTests`, `SlowTestRegistryMergeTests`, `SlowTestTripwireTests`, `NativeLinuxPolicyWitnessTests`, `MechanicalPcContractTests`, and `TaskPlatformGuidanceTests`. That 69 is not the nine-class union. Total checkpoint wall 445.7s, one build.

The subject-SHA CP-5 miss is the digest master already refreshed in `5b8e7ab8` (`98B4…`, count 80, SDK pin unchanged). This branch does not change graph inputs: the subject evaluation already produced that digest. On the merged tree (dangling commit `6fd3c79e2e986e77f7ec23928aa1f7ff81b428d3`, not a branch and not pushed), `-Check` with the existing tool DLL printed `approved` and exited 0. Lease `0530e780-8065-434c-a6ae-02b3426aceeb`, held 26s, load 0.63. No `-Update`.

`scripts/check-evidence-diff.ps1` over the range: commits 4, entries 0, violations 0, exit 0. `scripts/check-os-witnesses.ps1` exit 0. The OperatingSystem lines in the changed files are unchanged path and custody guards (line numbers moved in `LandVerificationArtifacts.cs` only) and are inventoried as non-behavior. No startup path changed. Runner defaults place portable work on the Linux runner; this review omitted `-Runner` and `-Platform`.

Merge-subset selector: `migrations=false`, kinds `temp-family-registry` and `test-lane-category`. Downgrade-selector impacted rows: 0. `windowsFilters` owes `AgentSessionServiceIntegrationTests` on the Windows lane. That row was not run and is not a pass. The selector's capped remainder (`docs/orchestration-loop.md` 166, `AgentTaskLandService.cs` 9) is outside this plan's closed rows and was not executed.

No new input wait. With parking disabled there is no automatic release deadline (CARD-1083). No PC rows. Mutation stays waived (operator 2026-11-12). Positive controls remain pending.

## Advisories (cards, not another review)

- A worker-node failure that exits 1 (MSB4166), or a Windows kill of the root `dotnet` process that surfaces as exit 1, is marked `ready`. A partial newer output can then be treated as up to date on the next land. Quarantine when the build log lacks `Build FAILED.` or contains MSB4166. The summary line alone does not catch MSB4166.
- The slot-timeout ready mark is implemented (`built` stays false, then `Complete()`) and has no test. The timeout hold's lease id is null, so the return before `ReleaseAsync` does not drop a granted slot. A warm generation is restored to `ready` with the outputs Prepare already accepted. A brand-new generation marked ready by a timeout is empty, so the next build compiles it.

## Land

Land the Code owner at `b8bd8536ac0414c8bb621d48c7f63aaa79b64d64`. The landable tree is the merge with `origin/master` above, and that tree's land-graph check passed.
