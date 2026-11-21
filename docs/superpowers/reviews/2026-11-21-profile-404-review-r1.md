# Review r1: profile-404 desktop legacy registration retry

Verdict: blockers. Do not land `1a48bee0aa136a051043dfad533603cd1d9a1e4f`.

Reviewed source is Debug task `e6f4c3dd-482e-4128-99d4-05fbc4a45e5a` on `feat/card-task-e6f4c3dd`, tip `1a48bee0aa136a051043dfad533603cd1d9a1e4f`. Range `bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0..1a48bee0aa136a051043dfad533603cd1d9a1e4f` is three files: `server/Infrastructure/Git/LandingGit.cs`, `tests/Antiphon.Tests/Application/C0824DesktopLegacyReleasePolicyTests.cs`, and `docs/investigations/2026-11-21-why-missed-rc-run-3528e6b1-profile-404.md`. `reviewedSourceClean` is false: these runs are leased build-slot rows, not checkpoint-tool receipts, and the touched class failed. Positive controls stay pending. Native Windows sharing and read-only disposition stay pending. This review did not rebase.

## Blockers

### 1. POSIX unreadable directory is Refused

Where: `tests/Antiphon.Tests/Application/C0824DesktopLegacyReleasePolicyTests.cs:536`, `Status_that_cannot_read_a_posix_directory_holds_it` (`AssertStatusReadHoldsTree("unreadable-directory")`).

Failure: at the reviewed tip, the class run was 141 executed, 140 passed, 1 failed, 0 skipped, 2m 30s 937ms (lease `2092cbac-3b13-45d0-89f9-16aed5956419`, start load 12.61, `--no-build`, `--timeout 570s`). Outcome was `Refused`. The test expects `Held` and then `status_untrusted`. The assertion stops on Outcome, so ReasonCode was not printed. The method body is not in the four-commit diff.

Why: `Held`/`status_untrusted` is the pre-check in `DesktopLegacyReleasePolicyService.PrecheckAsync` when `StatusVerdictAsync` reports stderr (`WroteStandardError`). A mode-000 untracked directory makes real `git status --porcelain=v1 -z --untracked-files=all` exit 0 with empty stdout and a permission warning on stderr, which holds before a release is filed. `Refused` is the later mapping: a release was filed and `TryRetire` returned a non-clean residue with `DirectoryGone`, `Unregistered`, and `BranchDeleted` all false (`DesktopLegacyReleasePolicyService` around the non-clean branch). `InProcessGitStatus.TryAnswer` returns `Ok("")` with no stderr when libgit2 lists zero status entries, which lets the pre-check treat the tree as clean and file. A later pre-move refusal (`cleanup_inspection_error`, `source_changed`, or `status_untrusted` seen only inside removal) then maps to `Refused`. A delete after move-aside would be `Partial` and would leave the original path absent. The new registration retry is inside `UnregisterWorktreeAsync` after move-aside, so it does not produce this Outcome. The author's CP-1 at witness `bb905d240412a7ab648c72bc3ff7bd12e15f79ed` already saw the same `Refused` before the product retry. CP-2 did not include this test. This review did not rebuild `bd34a7ff6`.

Fix: make the unreadable POSIX directory hold before filing, and keep the `status_untrusted` assertion. Do not treat the registration retry as that fix.

### 2. Derived impact index is stale at the tip

Where: `tests/derived-impact-index.json`.

Failure: regeneration at the tip added `C0824DesktopLegacyReleasePolicyTests` under `server/Domain/Entities/WorktreeResidueRun.cs` and `server/Infrastructure/Git/WorktreeRenames.cs` (2 insertions). First run printed `derived-impact-index changed` (lease `12555b29-cc9c-43df-ba53-85c89eebfbd9`, start load 5.75). Second run with `-NoBuild` printed `derived-impact-index unchanged` (lease `ceaf4ea8-23ec-4418-ae5a-a65d18523824`). The working tree was restored to the committed bytes. This review does not commit the index.

Fix: commit that regeneration on the source branch.

### 3. Merge-tree conflicts with current origin/master

Where: `tests/Antiphon.Agents.Pty.Tests/CodexAccountBootstrapTests.cs`.

Failure: after `git fetch origin master`, `origin/master` is `ce0eb1c5fbccdf8b2918643a319b88386435b761`. The brief's `adce1900111e85c0e437e2caf7c2f98b8c9a203f` is an ancestor of that tip and is not an ancestor of HEAD. Merge-base is `493bee0ee3af2052575bc7a26e9a313669b2fc2d`. `git merge-tree --write-tree origin/master HEAD` exits 1. The only conflict is that file (base `df64b190319a1f55d266f6d03389abcf0db0a842`, master `cf2b717884fc8bf28b8a8b0291e0a0504c3fa1ba`, HEAD `768c4516c90f00b32dd6943ac9df87785b2713a5`). It is outside the four-commit registration range. The branch still carries the earlier Codex 0.170 commits, so the tip is not landable.

Fix: resolve that file against current `origin/master` on a code task that is allowed to replay. This review does not rebase.

## Hypothesis

The Windows Full failure was `Double_run_is_idempotent(concurrent)`: `Directory.Exists(handle.Path)` stayed true. The candidate row and the refusal reason were not in the extract.

The sharing-restore hypothesis is plausible and is the best match for "the original path still exists." `UnregisterWorktreeAsync` moves the checkout aside, then moves the registration under the common `worktrees/` directory, then deletes the set-aside tree. A failed registration `Directory.Move` used to return once; the caller restores the checkout, so the original path exists again. A failed set-aside delete leaves the original path absent and does not match the assertion. A pre-check hold is `Held` and never starts removal. A lease loser is `Deferred` / `repository_lease_required` and does not delete; the winner should still remove the tree unless that pass also fails. A read-only loose-object failure during the set-aside delete is after the checkout has left the original path.

The change retries only that registration move, at most `WorktreeRenames.SharingAttempts` (5) times, pausing `SharingPause` (100 ms) and calling `RefuseMoveAsync` before each retry. Production `Windows` is `OperatingSystem.IsWindows()`, so Linux production does not retry. A lasting sharing conflict still fails, restores, and records a non-clean removal. The retry does not mask a persistent conflict. It only turns a conflict that clears inside the pause budget into success, which is the same bound the checkout move already uses. Four pauses are 400 ms. That can be shorter than an overlapping git status, so this loop may not be what the 13.5 s Windows failure needed. The candidate residue still collapses an unsuccessful unregister to `worktree_remove_failed` (`GuardedWorktreeRemoval`), so the new concurrent assertion text (Outcome and ReasonCode) cannot by itself prove the registration move was the step that failed.

Windows evidence that would confirm the hypothesis: both candidate rows `Removed` / `removed` with the trees absent, or, if a tree survives, `Refused` / `worktree_remove_failed` with the registration still live and the run log showing HResult `0x80070020`, `0x80070021`, or `0x80070005` on the registration move. Evidence that would refute it: `Deferred` / `repository_lease_required`, `Held` / `status_untrusted` or `source_dirty`, or `Partial` with the original path already gone. Do not treat the incident as fixed until that Windows pass exists. The Linux blockers above are why the next stage is code rather than a hold that only waits for Windows.

## Retry safety

The loop is bounded. Removing the `RefuseMoveAsync` recheck makes the disabled witness move a second time and miss `LegacyReleaseDisabled` with `moves == 1`. A cap other than 5 fails `moves.ShouldBe(SharingAttempts)`. An unbounded persistent conflict never reaches that assertion and dies at the 570 s session cap. The disabled arm is the switch-off witness. Those mutants were read, not executed. The three retry witnesses, `Path_escapes_are_refused` (including the absolute-outside case), and `Registration_move_leaves_a_late_directory_links_target_intact` passed inside the 140.

Delete confinement is unchanged. Windows open uses `FILE_FLAG_OPEN_REPARSE_POINT` and does not walk a junction or symlink. `MarkDeleted` sets `FILE_DISPOSITION_DELETE | POSIX_SEMANTICS | IGNORE_READONLY_ATTRIBUTE` (`0x1 | 0x2 | 0x10`) on the open entry. The classic fallback does not set the read-only flag. Linux unlinks a symlink with `O_NOFOLLOW` and does not descend. A path outside the allowed root is still refused before removal. The Linux read-only attribute on a tracked file does not prove the Windows disposition. Native Windows remains owed, including `C0824DesktopLegacyReleaseNativeTests.Held_directory_defers_never_forces`.

`TryRetireCoreAsync` holds `landing.lock` (`FileShare.None`) across the pauses. A conflicting acquire returns null and becomes `repository_lease_required` / `Deferred`; it does not wait out the pause. The holding pass blocks its own thread for at most 400 ms, and only when each attempt before the last is a sharing conflict. The happy path adds no pause. No session seat is held. `DesktopLegacyReleasePolicyJob` has `[DisableConcurrentExecution(timeoutInSeconds: 600)]`; the concurrent test calls the service directly and bypasses that attribute.

## Verification

One `Antiphon.Tests` build: lease `92f52a5b-b923-4924-8fd5-faa15aa03475`, start load 9.69, `UseAppHost=false`, native output, 0 errors, `BUILD_EXIT:0`, held 218 s. Later rows were `--no-build`.

Guards, parenthesized nine-class filter, lease `3636ca8e-d76b-46ff-8c88-d3bc64695508`, start load 7.22: 84 executed, 84 passed, 0 failed, 0 skipped, 1m 09s 480ms, `GUARD_EXIT:0`.

Evidence diff: four-commit range commits=4 entries=0 violations=0. Merge-base..HEAD commits=13 entries=0 violations=0.

Impact selector on the four-commit range does not select `migrations`, so the downgrade selector count is 0. Kinds: `repository-lease-reflected-constructors`, `repository-lease-windows-boundary`, `server-git-start-sites`, `test-lane-category`. Windows filter owed and not run: `RepositoryLeaseWindowsBoundaryTests`. `LandingGit.cs` is capped at 40 classes. This review ran the commissioned class and guard batch, not that capped set. The seven nightly pin inputs are outside the range; pins were not recomputed.

An isolated re-run of the POSIX method was started after this review printed `REFUSING` at load 35.00. That start violated the stop rule. It failed the same assertion (1 executed, 1 failed) and is not a gated result.

Placement was read. This review stays unpinned and does not name a runner or a platform.

## Evidence

```text
subjectTaskId: e6f4c3dd-482e-4128-99d4-05fbc4a45e5a
reviewedSourceSha: 1a48bee0aa136a051043dfad533603cd1d9a1e4f
reviewedSourceClean: false
ordinaryScopeCompleted: Full
reviewRound: 1
reviewedRange: bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0..1a48bee0aa136a051043dfad533603cd1d9a1e4f
```
