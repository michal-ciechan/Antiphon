# CARD-1677 S2 review 12ba9a25 round-1 fixes (Code 6f66c43e)

**Outcome: D-1 and D-2 are fixed, and the A-2 swap witness is added. All repaired rows are green and the receipt validates. D-1 was shown red first.**

- Branch `feat/card-task-6f66c43e`, runner mirror `/work/worktrees/task-6f66c43e`. Original Code task (landing owner): 5f63ea92.
- Base `b15f66352a1f6f13f55251875ac5be28fc2f4af3`. Card base, which is master: `f32aed42e58a4f7aed9b012d39c4bf4ae90a24a0`.

| Commit | What |
|---|---|
| `0e52856e6c0679f484a775e9ed19a8a63c31c5d2` | D-1 witnesses, red. Source unchanged from b15f66352. |
| `f55c7d24d825264def1edfb03a41c9b1676cd9a3` | D-1 fix: every family regex ends in `\z`, and control-character names are refused before any match. |
| `37f2eebc2211dd5d878e7aaf5718b64320b308e3` | D-2 and A-2 witnesses, plus the seams they use: `PhoneHomeRuntimeAdapter.TempSweepIsLinux`, `RunnerTempSweep.CreateDefault(retiring, tempRoot)` and `AfterDeleteInventory`. |
| `82d5f8f39ba6be62924fe008aa2ae1efc7606533` | Checkpoint amendment `docs/superpowers/plans/2026-11-03-card-1677-s2-review-12ba9a25-checkpoints.md`. This is the tested tip. |

## D-1: family regexes ended in `$`

### Fix

- All ten patterns now end in `\z`.
- A top-level name, or a `claude-1654/` child, that contains any control character (`char.IsControl`) never reaches a family regex. It is reported as unregistered.
- `Normalize` escapes each control character in the reported prefix as `\xNN`.

### Other S2 regexes checked

- `HexRun` and `DigitRun` are unanchored and only fold names for the report.
- The owner and uid come from statx, not from parsing text.
- The `claude-scratch` id (`name[^8..]`) is read only after a `\z`-anchored, control-free match.
- The server's S1 `LeafPattern` (`^task-[0-9a-f]{8}$`) is followed by an exact equality check with `task-<id8>`, so it cannot pass a trailing newline. It is left unchanged.

### Witnesses

- **V-48c** `Family_regexes_are_anchored_and_exact` now requires, for every family:
  - the pattern ends with `\z` and contains no `$`;
  - none of `Sample+"\n"`, `+"\r"`, `+"\r\n"`, `"\n"+Sample`, `+"x"` or `"x"+` matches.
- The pty-test-root alternation parser now expects `\z`.
- **New `[NativeLinuxOnly]` cases in `C1677TempSweepNativeTests`:**
  - `Family_name_with_a_line_break_is_unregistered_and_kept`: real aged entries for all 40 variants (10 families × 4), with claude-scratch on and the mirror absent. Every variant is intact and unregistered with its control character escaped, and no mirror is probed. One exact `c1514-<hex>` control is removed.
  - `Control_character_name_is_refused_before_any_family_match`: a family matching `^[\s\S]+\z` still never sees `\n`, `\t`, `\x01` or `\x7f` names.

### Red evidence at 0e52856e6 (source = b15f66352)

- One build-slot lease each:
  - build: `waited=0s held=34s`;
  - run: `waited=15s held=4s`, filter `/*/*/(C1677TempFamilyRegistryTests*)|(C1677TempSweepNativeTests*)/*`.
- Result: **total 8, failed 4, succeeded 4**. The failures:
  - `Pty_test_root_alternation_equals_known_prefixes` (pattern ends in `$`);
  - `Family_regexes_are_anchored_and_exact` (`family.Pattern` does not end with `\z`);
  - `Family_name_with_a_line_break_is_unregistered_and_kept`, at line 115: `response.Candidates` contained the `\n` names, so they were swept. The control row on lines 112-113 passed;
  - `Control_character_name_is_refused_before_any_family_match`: 6 candidates, all `removed`.

After the fix, the same build-and-run cycle with C1677TempSweepTests added gave 32/32.

## D-2: no runner-side op 47 or production-composition witness

| Part | Witness |
|---|---|
| (a) | New portable `C1677TempSweepDispatchTests` (10 executions), all through the real dispatcher, and except for the gate case through the real adapter, runtime and `RunnerTempSweep` over a scratch root:<br>• `Sweep_is_refused_without_the_capability(False/True)`: without tempSweepV1 the reply is 501 `unsupported_operation` with 0 sweep calls; the control with the feature is answered.<br>• `Sweep_is_advertised_and_answered_only_on_linux(True/False)`: forces the adapter's OS parameter `TempSweepIsLinux`. The default is `OperatingSystem.IsLinux()`; it is the repo's force-the-OS-parameter seam, not a production bypass.<br>• `Sweep_runs_outside_the_mutation_lock`: an Input holds the lock while the TempSweep completes.<br>• `Mode_reaches_the_sweep_and_dry_run_never_deletes(DryRun/Execute)`.<br>• `Unknown_mode_refuses_the_whole_request("Delete"/"execute"/"")`. |
| (b) | `Production_owner_seam_reads_each_entrys_own_uid` (Linux-only) covers the real statx path that `CreateDefault` composes:<br>• `EffectiveUid()` equals the effective uid in `/proc/self/status` (1654).<br>• A test file, the entry directory and a link to `/` read 1654 (lstat).<br>• `/` and `/proc` read 0. Root-owned paths are only read, because a foreign-owned entry cannot be created without root.<br>• An absent path reads null; the types are `0x8000`, `0x4000` and `0xA000`.<br>• `RunnerTempSweep.CreateDefault(retiring, testRoot)`, which is new and internal (the public `CreateDefault(retiring)` delegates with `/tmp`), reports the owned entry holding the link as `would_remove` in DryRun. |
| (c) | `C1677TempSweepServerTests`, now 7 executions:<br>• `Runner_without_the_capability_gets_no_sweep`: no frame, row `runner_not_eligible`.<br>• `Execute_is_sent_only_outside_the_preview_route(False/True)`: Execute=true sends Execute; the preview route sends DryRun.<br>• `Retention_disabled_sends_no_sweep`: no request at all and no rows. |
| (d) | `scripts/check-os-witnesses.ps1 -BaseRef f32aed42e58a4f7aed9b012d39c4bf4ae90a24a0 -HeadRef 82d5f8f39ba6be62924fe008aa2ae1efc7606533 -EvidencePath .antiphon/c1677-s2r2-os-witnesses.json -Plan docs/superpowers/plans/2026-11-03-card-1677-s2-review-12ba9a25-checkpoints.md` gives **exit 0**: `OS-WITNESS complete syntax/inventory`. The declaration file is gitignored and covers the whole S2 diff (see below). |

The (d) declaration covers the whole S2 diff:
- **Five witnesses:**
  - adapter: portable-forced, CP-14;
  - Program/CreateDefault: native Linux, CP-8;
  - statx: native Linux, CP-8;
  - no-follow `allowLinks`: native Linux, CP-8;
  - the `IsRegular` and off-Linux statx branch on Windows: native, **pending**. Nothing ran on Windows.
- Unchanged older guards (cwd admission, the S1 retention guards, the S1 Windows/Linux delete split) are declared non-behavior.
- The Program.cs composition branch itself is not executed by any test. Only its factory is.

## A-2: the surviving plain-recursive-delete mutant

`Tree_changed_after_the_delete_inventory_is_refused` (Linux-only):
- The `AfterDeleteInventory` seam swaps `inner/` for a symlink to an outside canary directory. The seam runs between the delete-time inventory and `DeleteOwnedAsync`.
- It asserts that the entry is `kept` / `changed_since_inventory`, `Removed == 0`, the link is still there, and the canary is intact.

## Throwaway mutants

All were env-gated in one SessionRunner.Tests build. Each run took its own lease. The source was restored with `git checkout --` and is clean.

| Mutant | Result |
|---|---|
| `DeleteOwnedAsync` replaced by `Directory.Delete(path, true)`, hook kept | **killed**: `Tree_changed_after_the_delete_inventory_is_refused`. The verdict was `removed`, not `kept`; 6/7 of the class otherwise green. |
| Dispatcher tempSweepV1 gate removed | **killed**: `Sweep_is_refused_without_the_capability(False)` |
| Op 47 routed through `MutateAsync` | **killed**: `Sweep_runs_outside_the_mutation_lock` (5 s wait timeout) |
| `TempSweepFeatures` advertises off Linux | **killed**: `Sweep_is_advertised_and_answered_only_on_linux(False)` |
| statx uid read at offset 16 (nlink) | **killed**: `Production_owner_seam_reads_each_entrys_own_uid`. It read 1 for the file, not 1654. |
| statx without `AT_SYMLINK_NOFOLLOW` | **killed**: the same test. The link read 0. |

Server-side mutants (preview ignored, capability check removed) were not run. They are left for Mutation.

## Checkpoint run `20261103-184804-ea85` at 82d5f8f39 (GREEN, exit 0, `unlisted: none`)

`validate --evidence .antiphon/checkpoints/20261103-184804-ea85/report.json --expected-source-sha 82d5f8f39ba6be62924fe008aa2ae1efc7606533` printed `CHECKPOINT SOURCE VALID source=82d5f8f39ba6be62924fe008aa2ae1efc7606533 rows=6`.

```
CHECKPOINT CP-7 commit=82d5f8f39ba6be62924fe008aa2ae1efc7606533 build=ok filter=/*/*/(C1677TempSweepTests*)|(C1677TempFamilyRegistryTests*)/* executed=27 passed=27 failed=0 skipped=0 trx=/work/worktrees/task-6f66c43e/.antiphon/checkpoints/20261103-184804-ea85/rows/CP-7/run.trx slot=granted waited=225s dirty=0 source=82d5f8f39ba6be62924fe008aa2ae1efc7606533 sourceState=clean buildSource=verified
CHECKPOINT CP-14 commit=82d5f8f39ba6be62924fe008aa2ae1efc7606533 build=reused filter=/*/*/C1677TempSweepDispatchTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-6f66c43e/.antiphon/checkpoints/20261103-184804-ea85/rows/CP-14/run.trx slot=granted waited=300s dirty=0 source=82d5f8f39ba6be62924fe008aa2ae1efc7606533 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=82d5f8f39ba6be62924fe008aa2ae1efc7606533 build=reused filter=/*/*/C1677TempSweepNativeTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-6f66c43e/.antiphon/checkpoints/20261103-184804-ea85/rows/CP-8/run.trx slot=granted waited=270s dirty=0 source=82d5f8f39ba6be62924fe008aa2ae1efc7606533 sourceState=clean buildSource=verified
CHECKPOINT CP-13 commit=82d5f8f39ba6be62924fe008aa2ae1efc7606533 build=reused filter=/*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-6f66c43e/.antiphon/checkpoints/20261103-184804-ea85/rows/CP-13/run.trx slot=granted waited=135s dirty=0 source=82d5f8f39ba6be62924fe008aa2ae1efc7606533 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=82d5f8f39ba6be62924fe008aa2ae1efc7606533 build=ok filter=/*/*/C1677TempSweepServerTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-6f66c43e/.antiphon/checkpoints/20261103-184804-ea85/rows/CP-9/run.trx slot=granted waited=90s dirty=0 source=82d5f8f39ba6be62924fe008aa2ae1efc7606533 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=82d5f8f39ba6be62924fe008aa2ae1efc7606533 build=reused filter=/*/*/(C1677RetentionServerTests*)|(C1677RetentionSettingsTests*)/* executed=69 passed=69 failed=0 skipped=0 trx=/work/worktrees/task-6f66c43e/.antiphon/checkpoints/20261103-184804-ea85/rows/CP-5/run.trx slot=granted waited=45s dirty=0 source=82d5f8f39ba6be62924fe008aa2ae1efc7606533 sourceState=clean buildSource=verified
```

CP-5, the S1 retention server lane, was serial and ran alone. It executed 69, as the brief expected.

### Earlier run `20261103-183809-131b` (stopped, no receipt)

- The runner build refused with `refused: tampered`. My earlier native dev builds had left `obj/` folders in `src/Antiphon.PtyHost`, `PtyHost.Protocol`, `Agents.Pty` and `SessionRunner.Contracts`, and the PtyHost child build rewrote them.
- I stopped the run, deleted those native outputs (literal paths) and re-ran.
- No test ran in the stopped run.

## Unlisted builds and runs, all through build-slot with native output and `UseAppHost=false`

- The red build and run (above).
- One green build and run (32/32).
- One D-2 build and run of the new classes (17/17), before committing.
- One mutant build and six mutant runs (above).
- One build of `tools/Antiphon.Checkpoints` to get the tool dll.
- Reason for each: red-first evidence, a pre-commit check, and the mutants the brief asked for.

## Other checks

- **Pinned files.** No pinned file was re-recorded.
  - The new Linux-only methods sit in the existing Linux-only class `C1677TempSweepNativeTests`, so the policy row, census roster and live-lane pin are unchanged.
  - `C1677TempSweepDispatchTests` is portable.
  - CP-13 is green.
  - `tests/test-execution-policy.json` and `scripts/test-live-lane-run.ps1` are not touched by this task. CARD-1686 (`feat/card-task-9e9cf510`) was not merged.
  - The policy row's reason text still names only the original symlink and `/proc` cases. I did not reword it, because that would force a reseal.
- **check-evidence-diff.** `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -Repository . -BaseRef f32aed42e58a4f7aed9b012d39c4bf4ae90a24a0 -HeadRef HEAD` (HEAD = 82d5f8f39) printed `commits=6 entries=0 violations=0`.
- **merge-tree.** `git merge-tree --write-tree origin/master HEAD` is clean against origin/master `e56e4d1b6a727b3127b473307b860b7117980f09`.

## Not run

- **The whole Unit lane.** The brief scoped this round to the CP rows, the new classes and the S1 server lane, in 120 minutes. The Unit lane is about 3 h on server2. So this is not a Final ordinary scope; it stays deferred to Final.
- **S1 rows** CP-1..CP-4, CP-6 and CP-12, and **S3 rows** CP-10 and CP-11.
- **Windows.** Nothing ran on the desktop. The Windows native witness `W-temp-windows` is pending.
- **PCs.** All PCs (PC-85..PC-113, PC-115) stay pending for SourceLanding Mutation.
  - PC-100 (the no-follow primitive) now has a killing witness.
  - The server-side preview and capability mutants were not run.

## Advisories not acted on

A-1, A-3, A-4, A-5 and A-6 from review 12ba9a25 were not acted on.
