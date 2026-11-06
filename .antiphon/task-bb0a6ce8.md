# CARD-1708 round-2 repair Code report (Review 39f7bfe1 B-4, dangling symbolic salvage ref)

Task bb0a6ce8, branch feat/card-task-bb0a6ce8, worktree /work/worktrees/task-bb0a6ce8 (server2 runner mirror).
Original Code landing owner: f822e84d-ef5c-44d0-ba24-05580ee5fadf. Stacked on 3dcd5965b4ec15bf9773b066b197846bc3da42e6.

## Change

- `server/Infrastructure/Git/WorktreeReflogSalvage.cs`: before the live check and any write, every salvage name is
  probed with `git symbolic-ref -q <name>` (read-only in the LandingGit profile). Exit 0 (symbolic, dangling or not)
  returns `reflog_salvage_collision`; any exit other than 1 (direct or absent) returns `reflog_salvage_failed`.
  `for-each-ref` omits a dangling symref and `update-ref --no-deref <name> <oid> <zero>` replaced it; the probe does
  not depend on either. Symbolic refs are always loose (files backend) or native (reftable), and symbolic-ref reads
  both. Prune is unchanged in code: `for-each-ref` never lists a dangling symref, so prune never sees or deletes one
  (doc comment says so); the new control test pins that. B-1..B-3 code untouched.
- `docs/orchestration-loop.md`: salvage paragraph names the dangling case and that prune never deletes a symbolic one.

## Commits

| SHA | What |
|---|---|
| 37988b2de8a4ee09bf5d6ce3388d32d96908df34 | red: two witnesses |
| 7468bd32610abcecc9c771dc08b9a51dda4c95ff | fix + docs |
| fa9b794b9c9799cf467608aadf4702f9df6973d4 | test: the new witness's all-refs compare excludes the release's own `refs/antiphon/retirement/` observation pins (written by `ObserveRetirementAsync` before salvage on every release); tested tip |

## Witnesses

- `Dangling_symbolic_salvage_collision_refuses_and_keeps_the_symbolic_ref` (release path, real tick): salvage name is
  a symref to absent `refs/heads/c1708-missing`. Asserts row `reflog_salvage_collision`, loose ref file bytes
  identical, `symbolic-ref` target unchanged, every ref outside `refs/antiphon/retirement/` unchanged, lost commit
  still unreachable, tree snapshot, registration, branch and origin intact.
  - RED at 37988b2 (production = 3dcd5965b): reason `removed` (15 total, 1 failed, 14 passed).
  - GREEN at fa9b794b9: class 15/15.
  - Mutant M-A (remove the whole symbolic-ref probe loop; native build, method filter): RED, reason `removed`.
    A first, narrower mutant (only the `== 0 -> Collision` arm removed) was red as `reflog_salvage_failed`, because
    the `!= 1 -> Failed` arm still fails closed. Both restored with `git checkout --`; status clean.
- `Prune_keeps_a_dangling_symbolic_salvage_ref` (control, green at base too): a finished aged scope's real salvage
  ref is pruned while a dangling symref in the same scope stays byte-identical. Not a defect witness: Git omits a
  dangling symref from the prune listing, so no prune mutant can go red; it guards against a later listing change.

## Checkpoints (one run, three rows, tip fa9b794b9)

Plan: copy of the Review's `/work/evidence/task-39f7bfe1/code-checkpoints.md` with output `bin-c1708r3/`
(`.antiphon/c1708r3-checkpoints.md`, ignored). Run 20261106-014505-8730, serial, UseAppHost=false, TEMP=/tmp.

```text
run: 20261106-014505-8730   manifest: /work/worktrees/task-bb0a6ce8/.antiphon/checkpoints/20261106-014505-8730/manifest.resolved.yaml
commit: fa9b794b9c9799cf467608aadf4702f9df6973d4  branch: feat/card-task-bb0a6ce8  worktree: /work/worktrees/task-bb0a6ce8  host: Debian GNU/Linux 12 (bookworm) cores=24
source: fa9b794b9c9799cf467608aadf4702f9df6973d4 state=clean buildSource=verified
CHECKPOINT CP-1 commit=fa9b794b9c9799cf467608aadf4702f9df6973d4 build=ok filter=/*/*/(C1708ReflogSalvageTests*)|(C0824DesktopLegacyReleaseSwitchTests*)|(C0824DesktopLegacyReleaseNativeTests*)|(C0824DesktopLegacyReleaseSettingsTests*)|(C0824DesktopLegacyReleasePolicyTests*)/* executed=201 passed=201 failed=0 skipped=8 trx=/work/worktrees/task-bb0a6ce8/.antiphon/checkpoints/20261106-014505-8730/rows/CP-1/run.trx slot=granted waited=105s dirty=0 source=fa9b794b9c9799cf467608aadf4702f9df6973d4 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=105s executed=94.979s wall=200.022s
PHASES CP-1 slotWait=180s build=270.3115244s startup=8.8581017s testsWall=80.0838691s teardown=5.4949079s hostWall=94.4371164s
CHECKPOINT CP-2 commit=fa9b794b9c9799cf467608aadf4702f9df6973d4 build=reused filter=/*/*/(TaskWorktreeRetirementTests*)|(WorktreeResidueSweepTests*)|(WorktreeGuardedCleanupTests*)|(SettledWorktreeRemovalTests*)|(LandingGitTests*)|(LandingRemovalPolicyControlTests*)|(WorktreeCleanupJournalTests*)|(WorktreeCleanupPresentationTests*)|(WorktreeCleanupSettingsValidatorTests*)|(CardDoneWorktreeCleanupTests*)|(CardWorktreeCleanupRecoveryTests*)|(CompletedCardWorktreeRemovalTests*)|(AgentTaskLandPublicationTests*)/* executed=570 passed=570 failed=0 skipped=6 trx=/work/worktrees/task-bb0a6ce8/.antiphon/checkpoints/20261106-014505-8730/rows/CP-2/run.trx slot=granted waited=690s dirty=0 source=fa9b794b9c9799cf467608aadf4702f9df6973d4 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=690s executed=325.965s wall=1016.178s
PHASES CP-2 slotWait=690s build=0s startup=19.5433576s testsWall=298.3012193s teardown=6.1101706s hostWall=323.9547489s
CHECKPOINT CP-3 commit=fa9b794b9c9799cf467608aadf4702f9df6973d4 build=reused filter=/*/*/(C1677RetentionServerTests*)|(C1677RetentionSettingsTests*)|(PhoneHomeConnectionTests*)|(C0824WorkspaceSlotServerTests*)|(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(MechanicalPcContractTests*)/* executed=186 passed=106 failed=80 skipped=0 trx=/work/worktrees/task-bb0a6ce8/.antiphon/checkpoints/20261106-014505-8730/rows/CP-3/run.trx slot=granted waited=75s dirty=0 source=fa9b794b9c9799cf467608aadf4702f9df6973d4 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=75s executed=177.198s wall=252.407s
PHASES CP-3 slotWait=75s build=0s startup=8.9342295s testsWall=165.204047s teardown=2.5329218s hostWall=176.6711981s
BUDGET wall=1847.922s execution=902.738s admissionOnly=945.184s
wall: 30m47s  sequential-equivalent: 24m28s  builds: 1  max-concurrent-builds: 1  rows: 2 green 1 red 0 skipped
verdict: RED exit=1
```

CP-1 rose 199 -> 201 (the two new methods); the 13 prior salvage cases plus 2 new all passed. CP-3 by class (TRX):
C1677RetentionServerTests 58 TimeoutException / 3 passed; C0824WorkspaceSlotServerTests 21 TimeoutException / 3
passed (the known timeout family, repair on master batch 87, not on this old S4a base; Review had 82, the split
varies with load). PhoneHomeConnectionTests 28 passed, 1 failed:
`Send_on_a_closed_connection_is_the_same_typed_error` (`live.SocketOpen` still true after its 2 s wall-clock wait,
CP-2 had waited 690 s for a slot: loaded host). Unrelated to this diff (salvage only) and green at 3dcd5965b in
Review; one method-scoped rerun from the kept checkpoint output at fa9b794b9: 1/1 passed (build-slot lease
0f1bb7bd, waited 105 s). Guards 47/47 (policy 39, guard 1, registry merge 5, tripwire 2), mechanical 11/11,
retention settings 14/14.

Validator: `CHECKPOINT SOURCE INVALID reason=report_failed` (exit 2) because CP-3 is red; receipt itself is
state=clean, buildSource=verified, dirty=0. reviewedSourceClean therefore cannot be true from this run.

## Unlisted builds/runs (all under build-slot, native default output, UseAppHost=false)

1. red build (lease 516fd1c3, 142 s) + red class run (83ff7b16, 30 s) at 37988b2.
2. fix build (ac24536d) + class run (323a5fce) at 7468bd3: 1 red on my own new all-refs assertion (retirement
   observation pins), calibrated in fa9b794b9 (the defect assertion, the reason code, was already green).
3. fix2 build (abeea98f) + class run (28bfddf6) at fa9b794b9: 15/15.
4. mutant builds/method runs: narrow (6a5bb250 / 99dbec09), full M-A (ca93f209 / 32a6da58).
5. PhoneHome single-method rerun (0f1bb7bd) from kept checkpoint output.

## Other gates

- check-evidence-diff 88e9fc746..fa9b794b9: commits=11 entries=2 violations=0 (rerun to include this report commit
  is the reviewer's; the report adds one Markdown path).
- merge-tree vs origin/master aa98ee426: the same three inherited S4a conflicts (DesktopLegacyReleasePolicyService,
  RemoteWorkspaceService, TaskWorktreeRetirementService); docs/orchestration-loop.md and this diff merge clean. Not
  merged, not resolved.
- Deferred, never marked passed: whole Unit lane (Debug 5ae98d31/CARD-1720 owns its cost), 14 native Windows skips,
  required manual acceptance, every PC (SourceLanding Mutation). PCs pending: the dangling-symbolic detection (M-A
  above is a Code-side mutant, no PC credit), plus all earlier CARD-1708 PCs.
- Platform: no new OS-selected branch. Restart: none.
