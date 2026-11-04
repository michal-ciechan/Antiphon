# CARD-0824 S4a native Windows reds in rc run 766e3df5 group 96

Date: 2026-11-04. Base: master `d4c88ddde554ae9f01fad65f27bc893175271b51` (same S4a source as rc/2026-11-05 cut `1470340559`).
Stage: Investigate (stood in for Debug).

## Verdict

**SAFETY: both cases fail closed.** Neither case removes, forces or files a release for the held or aliased tree.
In both, the policy refuses *earlier* than the test expected. Both reds are **test defects**. No product change
is needed.

| Case | Expected | Actual | Mechanism | Fix |
|---|---|---|---|---|
| `Held_directory_defers_never_forces(open-handle)` | `Refused` / `worktree_remove_failed` | `Held` (reason `source_dirty`, reconstructed) | The fixture writes `bin/held.dll`, but the fixture `.gitignore` does not ignore `bin/`, so the pre-check sees an untracked file and holds before any release is filed. The open handle never comes into play. | Test: put the held file under an ignored, allowlisted-disposable path (`obj/held.dll`). |
| `Windows_aliases_and_junctions_are_refused(short-name)` | `leaf_mismatch` | `outside_root` | On Windows, `Path.GetFullPath` expands 8.3 names. That makes `Normalized(path)` false, so the classifier's G-38 `outside_root` check fires before the leaf is read. | Test: expect `outside_root` when `GetShortPathName` returned a real alias, and keep `leaf_mismatch` for the synthesized `CARD-T~1` fallback. |

## Evidence

### Case 1: open-handle gives Held, not Refused

- Test `tests/Antiphon.Tests/Application/C0824DesktopLegacyReleaseNativeTests.cs:34-37` writes
  `<tree>/bin/held.dll` and holds it with `FileShare.Read`. The assertion is at `:53`.
- The fixture ignore file is `tests/Antiphon.Tests/TestHelpers/LandingGitFixture.cs:45`:
  `.antiphon/ .claude/ bin-*/ obj/`. It does **not** contain `bin/`.
- The pre-check is at `server/Application/Services/DesktopLegacyReleasePolicyService.cs:405-407`. It runs
  `git status --porcelain=v1 -z --untracked-files=all --ignore-submodules=none`, and any output gives
  `source_dirty`. It is recorded through `HoldAsync` (`:282`, `:543-549`) as outcome **Held**. No release is
  filed and `TryRetireAsync` is never reached.
- Linux repro with the same `.gitignore` and the same `git status` command (git 2.47.3):
  - with `bin/held.dll`, an empty `node_modules/x` and `obj/held.dll`, the output is `?? bin/held.dll`;
  - with only an empty `node_modules/x` and `obj/held.dll`, the output is empty.

  This also explains why the sibling `cwd` case (an empty `node_modules/x`) gets past the pre-check and can
  reach `worktree_remove_failed`.
- The plan meant the file to be disposable build output. Test design `2026-11-03-card-0824-s4a-test-design.md`
  §N-1 expects tick 1 to be Refused `worktree_remove_failed` with the release left active. §N-2 says "an
  ignored, allowlisted-disposable build path ... under `bin/`". The allowlist is
  `server/Application/Settings/WorktreeCleanupSettings.cs:14` (`**/bin/**`, `**/obj/**`, ...). It grants
  removal only for *ignored* files (`GuardedWorktreeRemoval.cs:109-111`). The fixture never made `bin/`
  ignored.
- With an ignored held file, the intended path exists. `GuardedWorktreeRemoval.cs:151-162`: the move-aside
  throws `IOException` under a sharing violation, the set-aside record is cleared and the result is
  `worktree_remove_failed`. That is the plan's expected Refused row.
- Safety: Held `source_dirty` files nothing and runs no removal. It is the strictest refusal.

### Case 2: short-name gives outside_root, not leaf_mismatch

- Test `:136-147`: `shortName = GetShortPathName(real.Path)` is written to `AgentTasks.WorktreePath`, and the
  expected reason is `leaf_mismatch`. The fixture root is `Path.GetTempPath()\antiphon-c448-<guid>\trees`
  (`LandingGitFixture.cs:30`, `DesktopLegacyWorld.cs:33`). Both `antiphon-c448-<guid>` and
  `card-task-<id8>` are longer than 8.3, so the real alias has `~` components (for example
  `...\ANTIPH~1\trees\CARD-T~1`).
- Classifier order, `DesktopLegacyReleasePolicyService.cs:450-472`:
  1. `Normalized(path)` (`:463`) gives `outside_root`;
  2. then the leaf pattern (`:469`) gives `leaf_mismatch`;
  3. then `DirectChildOfRoot` (`:471`) gives `outside_root`.

  `Normalized` (`:727-738`) requires `GetFullPath(path) == path`, ignoring case on Windows.
- In .NET on Windows, `Path.GetFullPath` calls `PathHelper.Normalize`. When the `GetFullPathName` result
  contains `~`, `Normalize` calls `TryExpandShortFileName`, which expands it to the long name. See
  dotnet/runtime `src/libraries/System.Private.CoreLib/src/System/IO/PathHelper.Windows.cs` lines 34-35 (main,
  fetched 2026-11-04) and `Path.Windows.cs:143`.
- So for a real 8.3 alias, `GetFullPath(shortName)` returns the long path, `Normalized` is false, and the
  result is `outside_root` at `:464`. The leaf is never read. This is the only route to `outside_root` for this
  input: the `:471` route needs the leaf pattern to pass first, and `CARD-T~1` cannot pass it.
- The synthesized fallback (`:139`, `Path.Combine(w.Root, "CARD-T~1")`, used when 8.3 names are disabled)
  names a path that does not exist. `TryExpandShortFileName` expands only the existing prefix, which has no
  `~`, so the string is unchanged. `Normalized` is true and the result is `leaf_mismatch`. The plan's
  expectation therefore holds only on the fallback branch.
- Is this a product defect? No. G-38's comment (`:461`) says "a relative or non-normalized path is outside
  the root before any name is read". An 8.3 alias is a non-normalized spelling under the platform's own
  normalizer, so `outside_root` is the documented outcome.
- Safety: Classify returns at `:464`, before any pre-check, git command or filing. The test's own later
  assertions (target intact, no release) are consistent with this.

## Recommended fix (one Code task, test-only)

1. `C0824DesktopLegacyReleaseNativeTests.cs:34`: use `Path.Combine(a.Path, "obj", "held.dll")` instead of
   `"bin"`. `obj/` is ignored by the fixture and on the disposable allowlist. Keep every other assertion.
2. `C0824DesktopLegacyReleaseNativeTests.cs:136-147`: set `reason = "outside_root"` when `ShortPath` returned
   a different string (a real alias). Keep `"leaf_mismatch"` only on the synthesized `CARD-T~1` branch. Add a
   comment citing `GetFullPath` short-name expansion and the G-38 order. Also amend test design §N-3
   "short-name" to match.
3. Verification: only the desktop lane can run this (CP-25 of `2026-11-03-card-0824-s4a-native-checkpoints.md`,
   after the live qualification run ends). On Linux the class skips, so only a compile check is possible.

## Remaining uncertainties

- The TRX `C:\Antiphon\releases\candidates\rc-2026-11-05-147034055988\logs\2026-11-04-1018-766e3df5\antiphon-all-group-96.trx`
  is on the desktop and could not be reached from server2. The brief gives the messages, but the Held row's
  reason code (`source_dirty`), the StdOut and which of the 6 cases passed are reconstructed from code, not
  read from the TRX. The two failing messages match the reconstruction exactly.
- The Linux repro covers only the git decision. The Windows sharing violation in the move-aside is from code
  reading (`GuardedWorktreeRemoval.cs:151-162`) and from the brief implying that `cwd` passed.

## Not done, noted

- N-2 `Long_path_never_leaves_an_unregistered_orphan` also writes under the un-ignored `bin/`
  (`C0824DesktopLegacyReleaseNativeTests.cs:80-83`). If it passed, git's status probably did not list the
  file deeper than 300 characters. That would mean the pre-check's "clean" proof can miss untracked files past
  MAX_PATH on Windows. Unverified here: check `core.longpaths` and the git status stderr on the desktop, and
  move N-2's tree under `obj/` too.
