# rc run ae84478d: session-runner all-group-1, StandingRunnerCwdTests red on Windows

- Run: `ae84478d` on `rc/2026-11-17` = `3db1a43b02246706dc28075fce7e7b5e84804ddb`, desktop (DESKTOP-KTLKPIF), Windows 10.0.19045, .NET 9.0.23, TUnit 1.44.0.
- Chunk: `session-runner-all-group-1`, started 2026-11-12T03:41:39Z, completed 03:47:13Z. Total 1230 tests: 1228 passed, 2 failed, 0 skipped. Run wall time 334 s; build 475 s.
- Read-only sources: `session-runner-all-group-1.trx`, `session-runner-all-group-1-tests.log`, `session-runner-all-group-1.timing.json` and the C:\tmp debris the two tests left behind. No live process, lock or runner was touched.

## Verdict

**Deterministic Windows defect in the test cleanup, not a product defect and not load.** Both tests remove a
*directory* symlink with `File.Delete` in their `finally` block. On Windows a directory symlink is a directory
reparse point. `DeleteFile` refuses it with ERROR_ACCESS_DENIED, which surfaces as `UnauthorizedAccessException`.
On Linux, `File.Delete` unlinks it, so both tests were green on server2. The exception comes from
`System.IO.FileSystem.DeleteFile`, inside `finally`, after 7 to 13 ms. There is no poll, timeout, wait or sharing
violation.

## Failures

| Test | Duration | Start (UTC) | Message | Top frames |
|---|---|---|---|---|
| `Symlinked_or_non_directory_standing_checkout_is_refused_at_launch` | 13.0 ms | 03:44:45.9142724 | `UnauthorizedAccessException: Access to the path 'C:\tmp\c1825-link-2c7d3e4393374cf89a353eb2fc753e0a\agents\linked-agent' is denied.` | `System.IO.FileSystem.DeleteFile(String fullPath)` then `StandingRunnerCwdTests.cs:line 206` (`finally`: `File.Delete(linked)`) |
| `Standing_symlink_swapped_before_start_is_refused` | 7.3 ms | 03:44:45.9377384 | `UnauthorizedAccessException: Access to the path 'C:\tmp\c1834-swap-3b371eccb36e4a158035611c9a0bd87f\agents\real-agent' is denied.` | `System.IO.FileSystem.DeleteFile(String fullPath)` then `StandingRunnerCwdTests.cs:line 291` (`finally`: `File.Delete(leaf)`) |

These are not assertion failures, so there is no expected-versus-actual value. The thrown exception is the
whole failure. The TRX `RunInfo` has outcome `Error` only because of the platform exit code 2 that follows the
two failures.

At the same moment, only this class's sibling unit tests ran, in sequence, between 03:44:45.910 and 03:44:45.951.
Neither test spawns a process. The tests log has no stderr or other lines near either failure, apart from the two
failure blocks.

## Passing siblings (same class, same chunk)

All 22 other `StandingRunnerCwdTests` passed. The lexical admission set is
`Agents_checkout_is_admitted`, `Agents_root_nested_and_traversal_are_refused`,
`Agent_name_allow_list_refuses_metacharacters_leading_marks_and_dot_runs`,
`Windows_device_names_are_refused_only_on_the_windows_flag`, `Worktree_names_keep_the_wider_segment_rule`,
`Windows_mixed_case_standing_leaf_is_refused`, `Worktree_and_workspace_root_stay_admitted`,
`Dot_and_parent_segments_are_refused`, `Double_slash_empty_segment_is_refused`,
`Trailing_slash_on_the_checkout_is_refused`, `Linux_agents_directory_case_difference_is_refused`,
`Very_long_segment_beyond_name_max_is_refused`, `Very_long_single_segment_within_name_max_is_admitted`,
`Nul_and_control_characters_in_the_segment_are_refused`, `Unicode_separator_and_dot_lookalikes_are_refused`,
`Absolute_looking_segment_is_refused`, `Windows_style_separator_in_the_segment_is_refused`,
`Parent_segment_is_not_resolved_into_an_admitted_worktree` and
`Absent_checkout_is_admitted_without_following_a_symlink`. The settings set is
`Filesystem_root_allowed_cwd_is_refused_at_startup` and `Root_normalising_allowed_cwd_spellings_are_refused`. The
dispatcher message test is `Dispatcher_message_names_the_agents_checkout`.

These siblings bound the cause. Every sibling that does not create a symlink on disk passed, including those that
construct the dispatcher with the Windows flag. Only the two tests that create a directory symlink failed, and
both failed on the line that deletes it.

## Masking caveat: body outcome on Windows not yet proven

The exception in `finally` replaces any exception from the test body. The leftover debris in C:\tmp shows how far
each body got:

- `C:\tmp\c1825-link-2c7d...\agents\` holds `real-agent`, `file-agent` and `linked-agent -> .../outside`.
  `C:\tmp\c1825-agentslink-4637...\agents` links to `target-agents\ok-agent`. The body therefore reached at least
  line 196, the agents-directory link, so the earlier `Should.NotThrow` and `Should.Throw` checks on lines
  175-190 ran without an exception. Whether the last refusal assertion, lines 197-201, passed is masked.
- `C:\tmp\c1834-swap-3b37...\agents\real-agent` links to `.../outside`, so `SwapLeafProbe` ran and the dispatch
  reached the provider-auth probe. Whether the recheck refused (`StartCalls == 0`, and an error mentioning a
  symlink) is masked.

The native Windows run of the fixed tests is what proves the body, including CARD-1834 plan item A-4 (native
Windows junction and refusal), which that plan kept pending.

The debris above is still in C:\tmp. This task left it alone because it is read-only on the desktop. It is
harmless. Any session may delete those three `C:\tmp\c1825-*` and `c1834-*` folders.

## Mechanism reproduced natively (no build, no runner)

Run in pwsh on .NET 10.0.15 in this task worktree, then cleaned up:

```
File.Exists=False Directory.Exists=True LinkTarget=...\outside
File.Delete: UnauthorizedAccessException: Access to the path '...\linked-agent' is denied.
Directory.Delete(link): ok; link exists after=False target exists after=True
```

## History

- Run `6591e378` (final-result note `7a3c45c3f`, cut `ac6d00022`) could not show this failure. Neither test
  existed at `ac6d00022`, where the class has 16 tests and no symlink cleanup.
- Changes from `ac6d00022` to `3db1a43b0` on the class and its runner code, all authored by
  `antiphon-server2-runner` (Linux) on 2026-11-10:
  - `b4a66d255` and `9654d17a4` (CARD-1825) add the standing-name allow-list, `StandingCheckoutLink`, and the
    test `Symlinked_or_non_directory_standing_checkout_is_refused_at_launch`, which has the `File.Delete(linked)`
    and `File.Delete(agentsLink)` cleanup.
  - `dd00c2829`, `af82395db` and `6c5cfdab9` (CARD-1834) add the pre-start link recheck in
    `PhoneHomeCommandDispatcher` and the test `Standing_symlink_swapped_before_start_is_refused`, which has the
    `File.Delete(leaf)` cleanup.
  - `fb5aba571` (CARD-0919) is unrelated drive-prefix masking.
- The suspected families do not apply: path and cwd spelling, 8.3 names, case, the CARD-1230 temp-root guard,
  the CARD-1832 taxonomy and load. The failing call is the BCL delete itself.

## Fix (this branch)

`tests/Antiphon.SessionRunner.Tests/StandingRunnerCwdTests.cs` gets a private `DeleteDirectoryLink(path)`. It
returns when the path is not a link. Otherwise it removes the link with `Directory.Delete(path)` on Windows and
`File.Delete(path)` elsewhere, so it never removes the target. Linux behaviour is unchanged. Both `finally`
blocks use it. No assertion changed.

This is the pattern already used in `tests/Antiphon.Tests/Application/PostLandMutationCustodyTests.cs:98`.
`derived-impact-index.json` needs no refresh, because there is no new class and no new source dependency.

Related latent issue, not fixed here: `RunnerWorkspaceServiceTests.TryDeleteLink` calls `File.Delete` on
directory links too, but it swallows `UnauthorizedAccessException`. On Windows it leaks the link instead of
failing.

## Why it was missed before the rc run

1. Both tests were written, made red, made green and reviewed only on server2 (Linux), where `File.Delete` on a
   directory symlink succeeds.
2. The class is an ordinary `[Category("Unit")]` class, not on the derived-impact `windowsOnly` roster, so no
   pre-land lane runs it on Windows. The first Windows execution was this rc chunk.
3. The CARD-1834 plan stated that native Windows proof was pending ("A-4, the native Windows junction and `C:/`
   refusal, stays pending"). Nothing turned that pending item into a Windows run before the cut.
4. The defect sits in `finally`, so a review that reads the assertions does not see it. Platform-specific BCL
   semantics, such as a directory symlink being a directory on Windows, are not part of any review checklist.

Owed: a native Windows run of
`--treenode-filter "/*/*/StandingRunnerCwdTests/*"` on the fixed branch. It should show 24 passed and no
debris left in C:\tmp.
