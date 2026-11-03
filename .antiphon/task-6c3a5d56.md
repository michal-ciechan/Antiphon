# CARD-1677 S1 hardening: anchor retention name patterns with \z (Code 6c3a5d56)

Branch `feat/card-task-6c3a5d56`, worktree `/work/worktrees/task-6c3a5d56` (server2 runner mirror), base master
`e56e4d1b6a727b3127b473307b860b7117980f09`. Original Code task (landing owner): 6c3a5d56.

Commits:
- `762eda216b1b195997307c0793becc1b6da8f876` test: witnesses (red) + `name_control_character` constant only
- `fa57351c47008a41d054ba2da901024f232c3026` test: U+2028 suffix spelled as an escape (red; the first commit did not compile)
- `58a2a23e976ad8ec0127d1e6addd5876edeec892` fix: `\z` anchors + control-character refusal (tested tip)
- this report commit (docs only)

## Exposure per pattern (red first)

Red run at `fa57351c` (master behaviour plus the witnesses), SessionRunner.Tests through the build slot:
total 17, failed 13, succeeded 4.

| Pattern | Where | Exposed at master? | Evidence |
|---|---|---|---|
| Residue `^<leaf>\.[0-9a-f]{8}$` | `RunnerWorkspaceRetention.Residues` | **YES, real deletion.** A real, clean, registered worktree `task-a1677001.0badf00d\n` in the retiring root (leaf absent) was taken as this task's residue. | `End_suffixed_retiring_residue_is_kept(lf, Execute)`: Verdict `removed` (phase B ran `git worktree remove` on it). `(lf, DryRun)`: `would_remove`. |
| Same, other suffixes | same | Not deleted, but answered `already_absent`. The server then clears `RemoteWorktreePath` and records Removed, while a residue-shaped entry is still on disk. | `(cr, Execute)`, `(ls, Execute)`, `(space, Execute)`, `(space, DryRun)`: Verdict `already_absent`. |
| `RunnerWorkspaceIgnoredContent` `^(?:...)$` (CARD-1032 completed-card classifier) | runner | **YES.** A protected-by-default name became Disposable, so it would go with the tree. | `.antiphon/inbox/brief.md\n`, `.antiphon/task-1234-brief.md\n` and `bin/cache.dat\n` were Disposable. `src/obj/cache.dat\t` was also Disposable: `.*` matches TAB, so this is a control-character exposure that `\z` alone does not close. |
| Runner `LeafPattern` `^task-[0-9a-f]{8}$` | `Confine` | No. The exact `task-<id8>` equality that follows rejects it (`outside_scope`). | `End_suffixed_task_leaf_is_kept(lf/cr/ls)`: red only on the new reason (`outside_scope`, expected `name_control_character`). `(space)` was green at master. |
| Runner `BranchPattern` `$` | `Confine` | No. Later exact checks (`symbolic-ref` equality, `DelegateBranchName`) refuse it. | Anchored anyway; no dedicated witness. |
| Runner `ShaPattern` `$` | `CheckAsync` | No. Its input is `Trim()`med or split on `\n`/`\t`. | Anchored anyway. |
| Server `LeafPattern` `$` | `ClassifyAsync` | No. The exact `task-<id8>` equality that follows rejects it (`outside_scope`; nothing sent). | The new `Each_ineligible_task...(leaf-newline/-carriage-return/-line-separator)` cases fail at master only on the reason. `leaf-trailing-space` passes at master. **I did not run the server witnesses at master.** The patterns are not exposed, and the only red is the new reason literal. |
| Server `OwnerPrefix` `(?:$\|[^0-9a-f])` | inventory | No. It only reports: every inventory row is Held and none is sent. `\n` also matches `[^0-9a-f]`. | At master `Control_character_inventory_names_are_reported_not_sent` would have recorded `identity_unknown` with an owner hint. Not run at master, for the same reason. |

Advisory, not changed:
- `DelegateBranchName.Pattern` (Contracts) and `RunnerWorkspaceSlotPool.SlotLeaf` still end in `$`.
  - DelegateBranchName is a fleet-wide parser. In retention it runs only after `BranchPattern`, which now uses `\z` and rejects control characters.
  - SlotLeaf is a keep decision: a match keeps the tree.
  - Both are worth a consistency card.
- `RunnerWorkspaceIgnoredContent` (CARD-1032), pre-existing: with an *embedded* newline, a `.*`-ending protected pattern cannot match, so an `*.trx` evidence pattern can classify a `.claude/` name as Evidence (admission-gated) rather than Protected. Unchanged by this task.

## Change
- Contracts gets `WorkspaceRetentionCodes.NameControlCharacter = "name_control_character"` and `WorkspaceRetentionNames.HasControlCharacter`, which is `char.IsControl` plus U+2028/U+2029. It also gets `Escape` for report detail.
- Runner `RunnerWorkspaceRetention`:
  - Every pattern now ends in `\z`.
  - `Confine` refuses a request path that holds a control character, before any pattern: kept with `name_control_character` and the escaped detail.
  - `Residues` treats any retiring entry beginning `<leaf>.` that is not exactly `<leaf>.<8 hex>` as a doubt. A control character gives `name_control_character`; anything else gives `workspace_retiring_or_unknown`. The answer is kept with `RetiringResidue=true`: such an entry is never taken as the residue and never read as `already_absent`. A foreign leaf's residue is still ignored (CARD-1684 R33 green).
- `RunnerWorkspaceIgnoredContent` anchors with `\z`. A path that holds a control character is never Disposable; it is Protected, checked after the Evidence check so CARD-1032's report+LF stays Evidence.
- Server `RunnerWorktreeRetentionService`:
  - `LeafPattern` and `OwnerPrefix` end in `\z`.
  - `ClassifyAsync` holds a path that holds a control character as `name_control_character` before `LeafPattern`.
  - The inventory holds a control-character name as `name_control_character` with no owner lookup.

## Witnesses (new)
- `C1677RetentionNativeProcessTests` (already a whole-class `[NativeLinuxOnly]` row; each new method carries the marker):
  - `End_suffixed_task_leaf_is_kept` (lf, cr, ls, space): 4 cases.
  - `End_suffixed_retiring_residue_is_kept` (lf/Execute, lf/DryRun, cr, ls, space/Execute, space/DryRun): 6 cases.
  - Both use real git worktrees under a test temp root and read registration with `worktree list --porcelain -z`.
- `C1677RetentionEngineTests` (portable): `Ignored_content_with_an_end_suffix_is_never_disposable`, 7 cases. Each case has a bare-name Disposable control under both the default and an explicit policy.
- `C1677RetentionServerTests`:
  - 4 new `Each_ineligible_task_alone_is_recorded_and_never_sent` cases.
  - `Control_character_inventory_names_are_reported_not_sent`: the exact leaf is known, its 5 suffixed twins are held, and the control task is still sent.
- NativeLinux policy, census and live-lane pin: nothing re-recorded. The new methods sit in a class that already has a whole-class row (`m=@()` in the hand-kept census in `scripts/test-rc-platform.ps1`, until CARD-1686 lands), and `tests/test-execution-policy.json` is unchanged. CP-4 `NativeLinuxPolicyWitnessTests` is green (3/3).

## Verification at 58a2a23e (all through the build slot or the checkpoint tool, server2/Linux)
Direct build-slot runs (native default output, UseAppHost=false):
- The four C1677 runner classes (Engine, Dispatch, Inventory, NativeProcess): **84/84** passed (67 before plus 17 new), 0 skipped.
- `CompletedCardWorkspaceRemovalTests` (the CARD-1032 classifier consumer; unlisted, run because the classifier changed): 99 total, 98 passed, 0 failed, 1 skipped.
  - The skip is the inherited by-design `C1032_RemovalLateFileContentRefuses` Linux skip ("CARD-1669 ... need Windows delete-disposition sharing").
- Server lane `C1677RetentionServerTests` + `C1677RetentionSettingsTests`, `--maximum-parallel-tests 4`: **74/74** (69 before plus 5 new).

Checkpoint tool, plan `docs/superpowers/plans/2026-11-03-card-1677-test-design.md`, rows CP-1..CP-5. The brief named CP-1, CP-3 and CP-5. I added CP-2 because it holds the new native methods, and CP-4 because it is the NativeLinux marker witness.
- Run `20261103-204337-acb6`:
  - CP-1..CP-4 were `refused: tampered`. My earlier native-default builds had left `obj/` restore files for Agents.Pty, PtyHost, PtyHost.Protocol and Contracts, and the checkpoint build rewrote them.
  - CP-5 was green, 74/74. The run is RED, and validate refuses it as a whole (`report_failed`).
  - I deleted my own native bin/obj (`git clean -fdX` on exact paths) and reran.
- Run `20261103-205256-7821` (CP-1..CP-4): GREEN, `CHECKPOINT SOURCE VALID source=58a2a23e976ad8ec0127d1e6addd5876edeec892 rows=4`.
- Run `20261103-205543-2ba6` (CP-5): GREEN, `CHECKPOINT SOURCE VALID source=58a2a23e976ad8ec0127d1e6addd5876edeec892 rows=1`.

Unedited CHECKPOINT lines:
```
CHECKPOINT CP-1 commit=58a2a23e976ad8ec0127d1e6addd5876edeec892 build=ok filter=/*/*/(C1677RetentionEngineTests*)|(C1677RetentionInventoryTests*)|(C1677RetentionDispatchTests*)/* executed=70 passed=70 failed=0 skipped=0 trx=/work/worktrees/task-6c3a5d56/.antiphon/checkpoints/20261103-205256-7821/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=58a2a23e976ad8ec0127d1e6addd5876edeec892 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=58a2a23e976ad8ec0127d1e6addd5876edeec892 build=reused filter=/*/*/C1677RetentionNativeProcessTests/* executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-6c3a5d56/.antiphon/checkpoints/20261103-205256-7821/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=58a2a23e976ad8ec0127d1e6addd5876edeec892 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=58a2a23e976ad8ec0127d1e6addd5876edeec892 build=reused filter=/*/*/RunnerWorkspaceServiceTests/* executed=35 passed=35 failed=0 skipped=0 trx=/work/worktrees/task-6c3a5d56/.antiphon/checkpoints/20261103-205256-7821/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=58a2a23e976ad8ec0127d1e6addd5876edeec892 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=58a2a23e976ad8ec0127d1e6addd5876edeec892 build=reused filter=/*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-6c3a5d56/.antiphon/checkpoints/20261103-205256-7821/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=58a2a23e976ad8ec0127d1e6addd5876edeec892 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=58a2a23e976ad8ec0127d1e6addd5876edeec892 build=ok filter=/*/*/(C1677RetentionServerTests*)|(C1677RetentionSettingsTests*)/* executed=74 passed=74 failed=0 skipped=0 trx=/work/worktrees/task-6c3a5d56/.antiphon/checkpoints/20261103-205543-2ba6/rows/CP-5/run.trx slot=granted waited=60s dirty=0 source=58a2a23e976ad8ec0127d1e6addd5876edeec892 sourceState=clean buildSource=verified
```

Gates:
- `scripts/check-evidence-diff.ps1 -BaseRef e56e4d1b6 -HeadRef 58a2a23e`: commits=3, violations=0.
- `git merge-tree --write-tree origin/master HEAD` (origin/master `c2f1f83a3`): clean.
- merge-tree against `origin/feat/card-task-6f66c43e` (S2): clean, and the two branches touch no common file. Not merged.

Not run / deferred to Final:
- The whole Unit lane. On loaded server2 it takes about 3 h (the 2-lane, 20-shard plan), past this task's 90-minute budget.
- The change touches only the S1 retention engine, the CARD-1032 classifier, the server retention service and one new Contracts static class, and every class that consumes them was run.

PCs pending for SourceLanding Mutation (method-scoped):
- PC-A: each `\z` reverted to `$` (runner residue → `End_suffixed_retiring_residue_is_kept(lf,*)`; classifier → `Ignored_content...(*, "\n")`).
- PC-B: the `Confine` control check removed (→ `End_suffixed_task_leaf_is_kept(lf/cr/ls)`).
- PC-C: the `Residues` doubt branch removed or reordered (→ residue cr/ls/space cases).
- PC-D: the classifier control check removed (→ `src/obj/cache.dat\t`).
- PC-E: the server `ClassifyAsync` control check removed (→ `leaf-newline/-carriage-return/-line-separator`).
- PC-F: the server inventory control check removed (→ `Control_character_inventory_names...`).
- PC-G: U+2028/U+2029 dropped from `HasControlCharacter` (→ the `ls` cases and the inventory ` `/` ` twins).
