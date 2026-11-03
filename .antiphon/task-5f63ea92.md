# CARD-1677 S2 (temp sweep): Code task 5f63ea92 report

Outcome: S2 implemented red-first per the plan and test design. The S2 checkpoint group is GREEN at
`b15f66352a1f6f13f55251875ac5be28fc2f4af3`. Ships OFF and dry-run. Nothing was landed.

- Branch `feat/card-task-5f63ea92`, runner mirror `/work/worktrees/task-5f63ea92`, original Code task 5f63ea92 (landing owner).
- Base: master `f32aed42e58a4f7aed9b012d39c4bf4ae90a24a0`. `git merge-tree --write-tree origin/master HEAD` is clean (origin/master == base at check time).
- Commits:
  - red `5ecdb7bb9f26db508e840913007d676fbd2c74da`: tests, contracts and pins, plus a red shim.
  - green `b15f66352a1f6f13f55251875ac5be28fc2f4af3`: implementation.
  - This report is committed separately on top. It is docs only and was not re-verified.
- Plan: `docs/superpowers/plans/2026-11-03-card-1677-runner-worktree-retention-plan.md` (§3.5, S2).
  Test design: `docs/superpowers/plans/2026-11-03-card-1677-test-design.md` (V-40..V-51c). Its `### Checkpoints` table was the closed list, `--after S2`.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef f32aed42e -HeadRef HEAD` reported `commits=2 entries=0 violations=0`.

## Checkpoints (closed list, S2 group, checkpoint tool)

### Green run 20261103-165735-2816 at b15f66352 (verdict GREEN exit=0; unlisted: none)

Unedited lines:
```
CHECKPOINT CP-7 commit=b15f66352a1f6f13f55251875ac5be28fc2f4af3 build=ok filter=/*/*/(C1677TempSweepTests*)|(C1677TempFamilyRegistryTests*)/* executed=27 passed=27 failed=0 skipped=0 trx=/work/worktrees/task-5f63ea92/.antiphon/checkpoints/20261103-165735-2816/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=b15f66352a1f6f13f55251875ac5be28fc2f4af3 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-7 waited=0s executed=3.870s wall=3.911s
CHECKPOINT CP-8 commit=b15f66352a1f6f13f55251875ac5be28fc2f4af3 build=reused filter=/*/*/C1677TempSweepNativeTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-5f63ea92/.antiphon/checkpoints/20261103-165735-2816/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=b15f66352a1f6f13f55251875ac5be28fc2f4af3 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-8 waited=0s executed=3.285s wall=3.318s
CHECKPOINT CP-13 commit=b15f66352a1f6f13f55251875ac5be28fc2f4af3 build=reused filter=/*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-5f63ea92/.antiphon/checkpoints/20261103-165735-2816/rows/CP-13/run.trx slot=granted waited=0s dirty=0 source=b15f66352a1f6f13f55251875ac5be28fc2f4af3 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-13 waited=0s executed=3.321s wall=3.355s
CHECKPOINT CP-9 commit=b15f66352a1f6f13f55251875ac5be28fc2f4af3 build=ok filter=/*/*/C1677TempSweepServerTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-5f63ea92/.antiphon/checkpoints/20261103-165735-2816/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=b15f66352a1f6f13f55251875ac5be28fc2f4af3 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-9 waited=0s executed=21.502s wall=21.531s
```
Every Min floor is met: CP-7 27 (Min 27), CP-8 3 (3), CP-13 3 (3), CP-9 3 (3).

### Red runs at 5ecdb7bb9 (red shim)

- Run 20261103-164709-064f: CP-9 RED (2 of 3 failed). `Disabled_sweep_sends_nothing` is green by construction, because the shim sends nothing. CP-7, CP-8 and CP-13 were `refused: tampered`. The cause was the ignored `obj/` outputs left by my earlier native compile builds: the checkpoint build rewrote them. I deleted them with `git clean -fdX -- src server tests tools scripts/fixtures` (literal pathspecs; I listed them first and they were only bin/obj and the run's kept `bin-c1677-*`) and re-ran the three rows.
- Run 20261103-165439-f8ed: CP-7 RED (24 of 27 failed). The 3 green cases are the registry witnesses, which test data, as designed. CP-8 RED (2 of 3 failed). `Inner_symlink_is_not_followed` is green under the shim, as designed: `Directory.Delete` does not follow links, so this is the design's equivalent case for PC-100. CP-13 3 of 3 green: the policy row and hash were already sealed in the red commit.

Unedited lines:
```
CHECKPOINT CP-7 commit=5ecdb7bb9f26db508e840913007d676fbd2c74da build=ok filter=/*/*/(C1677TempSweepTests*)|(C1677TempFamilyRegistryTests*)/* executed=27 passed=3 failed=24 skipped=0 trx=/work/worktrees/task-5f63ea92/.antiphon/checkpoints/20261103-165439-f8ed/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=5ecdb7bb9f26db508e840913007d676fbd2c74da sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=5ecdb7bb9f26db508e840913007d676fbd2c74da build=reused filter=/*/*/C1677TempSweepNativeTests/* executed=3 passed=1 failed=2 skipped=0 trx=/work/worktrees/task-5f63ea92/.antiphon/checkpoints/20261103-165439-f8ed/rows/CP-8/run.trx slot=granted waited=30s dirty=0 source=5ecdb7bb9f26db508e840913007d676fbd2c74da sourceState=clean buildSource=verified
CHECKPOINT CP-13 commit=5ecdb7bb9f26db508e840913007d676fbd2c74da build=reused filter=/*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-5f63ea92/.antiphon/checkpoints/20261103-165439-f8ed/rows/CP-13/run.trx slot=granted waited=0s dirty=0 source=5ecdb7bb9f26db508e840913007d676fbd2c74da sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=5ecdb7bb9f26db508e840913007d676fbd2c74da build=failed filter=/*/*/(C1677TempSweepTests*)|(C1677TempFamilyRegistryTests*)/* executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=15s slot-reason=refused: tampered dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-8 commit=5ecdb7bb9f26db508e840913007d676fbd2c74da build=failed filter=/*/*/C1677TempSweepNativeTests/* executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=15s slot-reason=refused: tampered dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-13 commit=5ecdb7bb9f26db508e840913007d676fbd2c74da build=failed filter=/*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=15s slot-reason=refused: tampered dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-9 commit=5ecdb7bb9f26db508e840913007d676fbd2c74da build=ok filter=/*/*/C1677TempSweepServerTests/* executed=3 passed=1 failed=2 skipped=0 trx=/work/worktrees/task-5f63ea92/.antiphon/checkpoints/20261103-164709-064f/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=5ecdb7bb9f26db508e840913007d676fbd2c74da sourceState=clean buildSource=verified
```

## Unlisted builds and runs (all through build-slot, one lease each)

- **Compile builds.** SessionRunner.Tests ×2 and Antiphon.Tests ×1, before the red commit.
- **Iteration runs.** One iteration run of TS/TF/TN plus NativeLinuxPolicyWitnessTests found one test bug (the control's removal was counted by `KeptIntact`). Its `policy-hash a != b` line gave the resealed hash. One iteration run of TV plus ST was 17 of 17 green.
- **Affected runner regression** (the shared no-follow primitive and the dispatcher), native build on the green source:
  - Filter: `(CompletedCardWorkspaceS5Tests*)|(CompletedCardWorkspaceRemovalTests*)|(C1677RetentionEngineTests*)|(C1677RetentionInventoryTests*)|(C1677RetentionDispatchTests*)|(C1677RetentionNativeProcessTests*)|(PhoneHomeCommandDispatcherTests*)|(RunnerWorkspaceServiceTests*)`.
  - Result: 246 total, 245 passed, 0 failed, 1 skipped. The skip is `C1032_RemovalLateFileContentRefuses`, an inherited CARD-1669 `Skip.Test` on non-Windows that runs after its portable assertions.
- **Affected server regression**, on a clean Antiphon.Tests build:
  - R-3 `PhoneHomeConnectionTests/Supported_operations_preserve_contracts`: 1 of 1 passed (op 47 included).
  - S1 `(C1677RetentionServerTests*)|(C1677RetentionSettingsTests*)`, unbounded parallelism: 69 total, **40 timed out** at the 45 s class timeout. Load average was 33-35 on 24 cores and all 69 rigs started together.
  - The same build re-run with `--maximum-parallel-tests 4`: **69 of 69 passed** in about 1 minute.
  - The S1 cases leave `TempSweep.Enabled=false`, so they never reach the new code. I read this as co-start load. Review can re-run CP-5 (S1 row) if it wants receipt-grade evidence.
- **Mutant builds.** One env-gated SessionRunner.Tests build and one env-gated Antiphon.Tests build, followed by 20 method-scoped runs (below). Both files were restored with `git checkout --`, and the tree was clean afterwards.

## Not run (deferred to Final; NOT passed)

- **The whole Unit lane.** It is about 3 h on loaded server2, against this task's 150-minute budget. A Final-profile round needs it, so the caller or Review decides whether to schedule it.
- S1 and S3 checkpoint rows (CP-1..CP-6, CP-10..CP-12), except the affected classes listed above.
- Windows: nothing ran on the desktop, as the brief requires. The portable TS/TF classes will run in Windows qualification. Their deletion takes the primitive's existing Windows handle path.

## What S2 does (seams and defaults)

**Runner.**
- New file `src/Antiphon.SessionRunner/RunnerTempSweep.cs`. Seam S-6 is the constructor `(tempRoot, families, IWorkspaceHolderScan, ownerUid, runnerUid, TimeProvider, TempSweepLimits, mirrorPresent, retiringRoot)`.
- `RegisteredFamilies` holds 10 families: c487-probe, c487 (`scripts/lib/c487-harness.ps1`), pty-test-root, c514-pty, c502-exit-cap, c1514, land-verify, bridge-queue, rcspeed, and claude-scratch.
- claude-scratch matches at depth 2 under `claude-1654`. It has no producer pin, is off by default, and needs both `/work/worktrees/task-<id>` and `<retiring>/task-<id>.*` to be absent.
- Production composition: `CreateDefault` uses `/tmp`, the real `/proc` scan, statx `AT_SYMLINK_NOFOLLOW` for the uid, `geteuid`, and default limits of 200 entries, 20 GiB, 100,000 entries per entry, 1 GiB per entry and a 10-minute budget.
- Wired in the runner `Program.cs` only on a Linux phone-home runner, beside retention. It advertises `tempSweepV1` only on Linux.

**Order of checks.**
1. Deny list on the top-level name: the plan's list plus CARD-0826's `IsDenied` names.
2. Family flag.
3. Mirror probe (claude-scratch only).
4. A no-follow walk:
   - the top level must not be a symlink;
   - the entry and every descendant must have the runner uid, and an unreadable owner counts as `owner_unknown`;
   - the 100k entry and 1 GiB limits;
   - socket, FIFO or device means `unproven_content`;
   - age is the newest regular-file write. Only when there is no regular file at all does the newest directory mtime count.
5. Age.
6. Sort oldest first, then bound by entries and bytes. Once a bound is hit, everything after it is `tick_bound`.
7. A holder scan right before each action: an incomplete scan keeps the entry as `holder_scan_unknown`, a holder as `live_process`.
8. DryRun stops at `would_remove`. Execute deletes through `RunnerWorkspaceNoFollowDelete` (Inventory, then DeleteOwned).

**Other runner behaviour.**
- Unregistered names are reported as `{prefix, count, bytes, bytesComplete}`. Hex runs are normalized to `<hex>`, and the byte walk is bounded at 200k files.
- The kept-candidate list is capped at 500, with `Truncated`.
- An unknown mode or a non-positive bound refuses the whole request (`request_invalid`).

**Dispatcher and contracts.**
- `PhoneHomeOperation.TempSweep = 47` runs OUTSIDE the mutation lock.
- It is refused with 501 unless the surface advertises `tempSweepV1`.
- The interface member's default refuses.
- The server sets a 15-minute request timeout.

**Server** (`RunnerWorktreeRetentionService.SweepTempAsync`):
- It runs after the mirrors and inventory of an enabled tick, so `RunnerWorktreeRetention:Enabled=false` stops it too.
- `TempSweep:Enabled`, the kill switch and the tick budget are re-read right before the send.
- It sends only to a runner that advertises `tempSweepV1`.
- Mode is DryRun unless `TempSweep:Execute`. The preview route is always DryRun.
- `Families` always carries `claude-scratch`, which is false unless set.
- Rows go to Lane `RunnerTemp`:
  - per candidate: `Branch=temp:<family>;bytes=<n>`, outcome Removed, Preview or Held(reason);
  - per unregistered prefix: `temp:unregistered;count=;bytes=`, with `+` when the byte count is incomplete;
  - Deferred `unknown` on a transport loss or `request_invalid`, plus `temp_scan_incomplete` and `temp_report_truncated`.
- No migration (D-8).

**Validator.** `TempSweep:MinAgeHours >= 1`, `MaxEntriesPerTick` 1..1000, `MaxBytesPerTick >= 1`.

**Defaults as shipped** (`server/appsettings.json` unchanged, type defaults unchanged): TempSweep `Enabled=false`, `Execute=false`, `Families:claude-scratch=false`, `MinAgeHours=48`, 200 entries, 21474836480 bytes. The card's operator decision ("Enable this on by default") covers the worktree retention only. The plan, its D-15 and the brief keep the temp sweep off, and V-36 (S1) already pins these values.

**Docs.**
- `docs/docker-stack.md` has a new subsection "Runner temp sweep (CARD-1677 S2)" covering behaviour, rows, the enabling steps (dry run first; claude-scratch last) and rollback.
- `docs/ops-http.md` preview-route note.

## Decisions taken without a question (each conservative or forced by a witness)

1. **Inner symlinks.** V-49b expects an entry holding an inner symlink to be removed with the link target intact. The existing primitive refused any symlink at inventory. I added an opt-in `allowLinks` parameter (default false) to `InventoryAsync`, `ValidateRemainingAsync` and `DeleteOwnedAsync`:
   - Inventory records a link as itself and never opens it.
   - Delete unlinks it with `unlinkat` and refuses if the link's identity changed.
   - The existing callers (CARD-1032 S5) do not pass it and behave as before, which the 245 green regression cases cover.
2. **pty-test-root includes `c1653-sock` and `c1653-peer`.** `KnownPrefixes` gained them since the plan was written (CARD-1653 S2), and V-48b requires set equality, as the test design's land-order note says. Their roots may hold sockets, which keeps them as `unproven_content`.
3. **Extra kept reasons**, all fail closed: `owner_unknown`, `age_unknown`, `unproven_content`, `changed_since_inventory`, `delete_failed`, `tick_budget`, `request_invalid`.
4. **Top-level regular files** (`rcspeed-<hex>.ps1`) are removed with `File.Delete` after a no-follow re-check of type, size and mtime. The primitive handles directory roots only.
5. **The runner clamps request bounds** to its own ceilings, which equal the plan defaults.

## Pins and policy

All follow the repo's own procedures.

- `tests/test-execution-policy.json`: a new `C1677TempSweepNativeTests` windows-platform Linux-only row. The `policyHash` was resealed to `382fe6bcbef3cd9c468d498d9501c8e345b8c06e143c0950f2b6665668a40ffc` from the witness output. CP-13 is green.
- `scripts/test-rc-platform.ps1`: the rc census roster lists the class (inventory row and documentClasses). This is the 0ca1a0ffe precedent.
- `scripts/test-live-lane-run.ps1`: `$script:PinnedInputDigest` was re-recorded to `a3025fcdf6912bca2ba75b6262b38de3ce66cf3e47db604137962fc17c1687e7` with `-PrintInputDigest`, because the policy file is a census input. This is the c28a27087 precedent.
- No csproj or land-graph change.

## Mutants run (safety guards; NOT CARD-0478 evidence)

All 20 were env-gated, method-scoped, one lease per run. Sources were restored and the tree was clean afterwards.

| Mutant | Guard / PC | Result |
|---|---|---|
| M85 | unregistered name eligible (PC-85) | killed: Unregistered_name_is_reported_and_kept |
| M86 | directory mtime decides (PC-86) | killed: Newest_file_write_decides_age (both cases) |
| M87 | top-level files only (PC-87) | killed: (new-nested-file) |
| M88 | ignore holders (PC-88) | killed: Holder_keeps_the_entry(holder) and native Real_cwd_holder_keeps_the_entry |
| M89 | incomplete scan treated as empty (PC-89) | killed: (scan-unknown) |
| M90 | deny list skipped (PC-90..97 together) | killed: all 8 Denied_names cases |
| M98 | uid check removed (PC-98) | killed: Foreign_owned_descendant_keeps_the_entry |
| M99 | follow a top-level symlink (PC-99) | killed: Symlinked_entry_is_refused_and_its_target_kept |
| M100 | `Directory.Delete(entry,true)` instead of the no-follow primitive (PC-100) | **survived, equivalent** (.NET 9 does not follow links; predicted by the design note). Mutation should use the "walk follows links" variant the note names |
| M101 / M102 | entry / byte bound ignored (PC-101/102) | killed: Per_tick_bounds(entries) / (bytes) |
| M103 | inventory limit ignored (PC-103) | killed |
| M104 / M105 / M106 | claude flag / mirror probe / retiring probe ignored | killed: (flag-off) / (mirror-present) / (retiring-present) |
| M107 | recurse for family names (PC-107) | killed |
| Mdry | DryRun deletes | killed: Old_registered_entry_is_swept(False) |
| M111 / M112 / M113 | server: Enabled not read / Mode always Execute / claude-scratch default true | killed (V-51 / V-51b / V-51c) |

## Pending for SourceLanding Mutation

All S2 PCs stay pending: PC-85..PC-113 and PC-115. Neither this round nor my mutants discharge any of them. PC-90..PC-97 are separate per-name mutants; I ran only one combined mutant. The new `allowLinks` link-identity check in `DeleteLinuxAsync` has no PC; it is a candidate for missing-control discovery.

## Restart

None for this task: nothing is landed, and activation is operator-owned.
