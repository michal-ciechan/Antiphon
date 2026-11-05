# Mutation sweep: S4a status_untrusted and CARD-1714

SourceLanding sweep of the landed fixes. No product code or tests were left behind.

- L = `d95d3f2cdce02fd95bf2145ecee979769cfc4c3c` (brief base, `master` tip, this worktree HEAD before and after the sweep).
- Branch `feat/card-task-613f2230`. Tracked tree clean at the start and after `git checkout` of every edited path plus deletion of the temporary switch file.
- Code task `6dfad742` (report `4d30e0226`, tested `a670e82a9`). Review `99e23919` advisory A-1. Debug `52410842`.
- The brief names L. It does not name an operation id. Binding used here is HEAD = L and a clean tracked tree before any mutant.
- Git 2.47.3. A committed tree with an unreadable untracked directory yields empty porcelain and `warning: could not open directory 'src/hidden/': Permission denied`.

## Method

One temporary `ANTIPHON_MUTANT` switch, compiled into the server assembly, one build of `tests/Antiphon.Tests` (`UseAppHost=false`, one build-slot lease). Each mutant was a separate process. The switch was removed and the project rebuilt before the restored run.

Discriminating witnesses, every mutant (nonzero):

| Filter | Base count |
|---|---|
| `LandingGitTests/C1714*` | 3 passed |
| `SettledWorktreeRemovalTests/C1714*` | 4 passed |
| `Status_that_cannot_read_the_whole_tree_holds_it` | 2 passed |
| `Path_past_the_delete_limit_holds_the_tree_not_partial` | 1 passed |

Those 10 cases are the 8 CARD-1714 rows plus the two S4a status shapes.

Named class pack, switch off and every survivor, then again after restore:

`C0824DesktopLegacyRelease*`, `LandingRemovalPolicyControlTests*`, `LandingGitTests*`, `WorktreeGuardedCleanupTests*`, `SettledWorktreeRemovalTests*`.

489 total, 476 passed, 0 failed, 13 skipped. The skips are Windows-only (`Long_path_*`, `Windows_aliases_and_junctions_*`, `Held_directory_defers_never_forces`, `C665_LockedFile*`, `C665_EmptyUnlistedJunctionTargetSurvivesRemoval`, `C721_HeldHandleDuringCleanupStaysRegisteredOrRecorded`, `C459_ManagedRootRequired(junction)`).

Mutants already red on the 10-case set were not repeated on the 489. A red witness is the kill.

## Killed

| Mutant | Control | Red witness | What failed |
|---|---|---|---|
| `drop-longpaths-inspect` | PC-1 | `C1714_Clean_status_that_wrote_stderr_is_untrusted` (both scopes) | Status argv must start `-c core.longpaths=true`. The real unreadable case stayed green: Linux git still warns without the flag. |
| `drop-longpaths-precheck` | S4a pre-check | `Status_that_cannot_read_the_whole_tree_holds_it` (both shapes) | Same argv pin on the policy pre-check. |
| `drop-stderr-hold-inspect` | PC-2 | `C1714_Clean_*` both scopes, `C1714_Status_that_cannot_open_a_directory_is_untrusted`, `C1714_Removal_refuses_a_status_that_could_not_read_the_whole_tree` readings 1 and 2 | Expected `status_untrusted`. |
| `drop-stderr-hold-precheck` | S4a pre-check | `Status_that_cannot_read_the_whole_tree_holds_it(unreadable-directory)` | Outcome `Held` vs `Refused`. The removal re-check still refuses, so the tree is not deleted. `long-path-config` stayed green: that shape is `source_dirty` from the listing. |
| `wse-nonzero-only` | | `C1714_Status_that_cannot_open_a_directory_is_untrusted` and the policy unreadable shape | `WroteStandardError` set only when exit is non-zero. Injected results that set the flag themselves stay green. |
| `wse-force-false` | | same two real-git witnesses | RunAsync forces the flag false. Injected results stay green. |
| `length-original` | PC-4 | `C1714_Path_past_the_delete_limit_refuses_before_the_move(over-once-set-aside)` | Length taken on the original path. The entry fits until the set-aside name is applied. |
| `limit-261` | PC-5 | same, `over-once-set-aside` | Boundary `>= max` widened to `>= max+1` (`>`). A path of exactly the limit is accepted. |
| `limit-259` | | same, `under` | Boundary tightened to `>= max-1`. A path one under the limit is refused. |
| `suffix-length-wrong` | | same, `over-once-set-aside` | Growth taken as 26. The real `.{name}.removing-{16}` growth is 27, so the boundary entry is treated as fitting. |
| `drop-path-limit` | PC-3 | `over-once-set-aside` and `Path_past_the_delete_limit_holds_the_tree_not_partial` | The length refusal is skipped. |
| `refuse-after-move` | PC-7 | both `C1714_Path_*` shapes and `Path_past_*` | The check runs after `MoveAside`. The walk then enumerates a path that is gone: `under` is not clean (`cleanup_inspection_error`); `over` returns `cleanup_inspection_error` rather than `worktree_path_too_long`. |
| `held-to-partial` | | `Path_past_the_delete_limit_holds_the_tree_not_partial` | Outcome `Held` vs `Partial`. |
| `held-to-refused` | PC-6 | same | Outcome `Held` vs `Refused`. |
| `backoff-removed` | | same | `RetryCursor` is null. The test throws `NullReferenceException` at `C0824DesktopLegacyReleasePolicyTests.cs:511` on the missing cursor. |
| `untrusted-but-release` | | `Status_that_cannot_read_the_whole_tree_holds_it(unreadable-directory)` | Pre-check does not hold, a release is filed, the removal re-check refuses. Outcome `Held` vs `Refused`. The delete does not proceed. |

## Survived

Each of these was 476 passed, 0 failed, 13 skipped on the class pack above. Same counts with the switch off.

| Mutant | Class | Missing witness |
|---|---|---|
| `hold-english-phrase` | survived (Review A-1) | Stderr that does not contain `could not open directory`. RunAsync sets `WroteStandardError` only when the text contains that phrase. The real git warning on git 2.47.3 contains it, and the injected tests set the flag without going through RunAsync, so both stay green. A non-English locale, or a warning such as `unable to access`, would be trusted. |
| `retry-skip-recheck` | survived | A reopened attempt (command already started, nothing moved) whose status exits 0 with a stderr warning. The flag is set only on that reopen path inside `TryRetireCoreAsync`. First-pass removal and the `source_dirty` retry stay guarded. |
| `length-guard-on-linux` | survived | `WorktreePathLimit.System` uses 260 on Linux. The witnesses inject their own limit or use short temp paths, so nothing reaches 260. A Linux tree whose set-aside path is 260 characters or longer would be refused and left in place. |
| `refuse-after-slot` | survived | The length refusal runs after the command slot is consumed, and still before the move. `Path_past_*` still sees `Held`, an intact tree, and a backoff cursor. Nothing asserts that the slot was not spent. |
| `follow-links` | blocked by the reparse guard | `FirstTooLong` descends into reparse points. `GuardedWorktreeRemoval` refuses `ignored_reparse_point` before the walk, so the named classes never reach the mutant. Missing witness: a link the content gate does not refuse before the length walk. |
| `skip-unreadable` | blocked by the stderr hold | `IgnoreInaccessible=true` on the length walk. An unreadable directory is already `status_untrusted` from git status before the walk. Missing witness: an entry the walk cannot read and git status does not already warn on. |
| `wse-default-true` | survived, latent | The property defaults true. RunAsync and the test doubles that care assign it, so the default is never read on these paths. Missing witness: a status `LandingGitResult` left at the default and then treated as clean. |

## Restored green

Edited paths restored with `git checkout --` of each file. `server/Application/Dtos/MutationSwitch.cs` deleted. HEAD still `d95d3f2cdce02fd95bf2145ecee979769cfc4c3c`, tracked diff empty.

Rebuild of `tests/Antiphon.Tests`, then the same class pack: 489 total, 476 passed, 0 failed, 13 skipped.

PC-1 through PC-7 are all killed. The seven survivors above are the open findings. CP-25 (the Windows native class) was not run; the desktop qualification is live.
