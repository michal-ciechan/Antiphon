# CARD-0824 S1 review round 2 (diff-only) — task f4c293bc

Verdict: not clean. Tip `009759428ccd73994b3241345dcf970858edefed` (code `fa2531861329f57505886dd03e24db1888033692`). Two defects. D-1 ownership and D-2 cold reset, as tested, are closed. The drain `slot_unknown` path and the default handoff digest are not.

Range reviewed: `41e63343b6449c6a97ee7c8df3bfe52da0354ea9..fa2531861329f57505886dd03e24db1888033692`. The tip commit is the worker report only. Subject branch `feat/card-task-00abf1bf`. This review did not commit to that branch. Flags stay default off.

## Defects

### D-1. Drain `slot_unknown` does not stop, and an operator cannot stamp the release

Where: `server/Application/Services/AgentTaskDispatcher.cs` `ReleaseSlotBeforeRebindAsync` 1212-1241 (catch 1221-1225, hold at 1170-1173). `server/Application/Services/WorkspaceSlotReleaseService.cs` `EligibleAsync` 110-113 and the stuck warning at 68-76. The only assignments of `RemoteWorkspaceSlotReleasedAt` are `AgentTaskDispatcher.cs:1230` and `WorkspaceSlotReleaseService.cs:93`, both after a confirmed `released` / `already_free` / `quarantined` reply.

Failure: A queued slot-bound task whose draining runner answers 409 `slot_unknown` stays on that runner. `PhoneHomeRunnerClient.ThrowIfError` (630) turns that 409 into `ConflictException` whose message is the runner detail `slot_unknown` (`RunnerWorkspaceSlotPool.Refuse` sends detail `slot_unknown` at status 409). The drain catch is every exception and returns false. There is no attempt counter. Each dispatcher tick writes the same `workspace_slot_release_pending` hold. `EligibleAsync` requires Succeeded, Failed, or Canceled, so a Queued drain task never increments the reconciler's three-strike count and never gets `workspace_slot_release_stuck`. No endpoint writes the release stamp. The stuck sentence tells an operator to record the release by hand.

Why: Open question 2 asked to accept a hold unless it can deadlock a drain, and to check that the three-strike record lets an operator resolve it. It does not. The drain rebind of this task does not finish. Canceling the task leaves the drain queue; `RecoverAsync(null)` still keeps the Leased slot, and the stuck warning appears only after the task is terminal, still without a stamp. Clearing the drain puts the task back on a runner that does not know the slot. Generation mismatch is a different answer and should keep holding. This path is specifically unbounded `slot_unknown`.

Fix: Bound drain-path `slot_unknown` the way the terminal reconciler does (three attempts, then `workspace_slot_release_stuck`), and give the operator a real way to stamp `RemoteWorkspaceSlotReleasedAt` so the task can move. Do not guess a stamp from a lost reply.

CP-21 (32/32) covers the drain witness's `released` and generation-mismatch arguments. CP-4 (23/23) covers the terminal three-strike stop. Neither is persistent `slot_unknown` on a queued drain.

### D-2. A default create-request digest is not the pre-field digest

Where: `server/Application/Services/AgentTaskService.cs` `HandoffRequestDigest` 1921-1924 serializes the whole `CreateAgentTaskRequest` with `JsonSerializer.SerializeToElement` (default options, no naming policy, default values kept) and SHA-256 of the canonically ordered JSON. `server/Application/Dtos/AgentTaskDtos.cs:225` is `bool WorkspaceColdReset = false` with no `JsonIgnore`. `PipelineHandoffActionService.cs:134` refuses `request_changed` when the stored digest differs.

Failure: The same serializer, on a one-goal record, emits `{"Goal":"goal","WorkspaceColdReset":false}`. Digest with the field `E9DA6ECB01590D95186BB917C129EB5DF15A38FA7257D09A61E16F47151DE8EF`. Digest of the record without the property `0866E7AC35C859F7DDE67D4039509DD4AB6714AB5B03EEABF63FD2222344AD24`. DIFFERENT. The production digest includes every property that serializer emits, and this one is emitted for a default request. The field is not in the pre-S1 contract. The worker report does not mention omit-when-default or a witness. An in-flight handoff stored before the land fails `request_changed` after it.

Fix: Omit `WorkspaceColdReset` from the digest when it is false, and add a witness that a default request's digest is byte-identical to the same request without the property.

## Open questions

1. Cold reset quarantines ignored files outside the keep-list (late `.antiphon` evidence) and wipes keep-list build output when that is the only residue. Accepted. `CleanArguments(cold: true)` is `git clean -ffdx` with no `-e`. `PreflightFreeAsync` (ownership, free invariant, unpushed reflog, no fetch) runs at `ColdResetAsync` 1064, then `ProveOwnership` again at 1067, then the clean at 1069. A late untracked file, a non-keep-list ignored file, or a reflog-only commit quarantines with the bytes in place (CP-9, 12/12, including `C0824_ColdResetQuarantinesWhatALeaseWould`). `C0824_ColdResetRemovesDeletedProjectResidue` in that same class shows a cold reset removes warm-kept `bin` output when it is the only residue. This does not make cold reset unable to remove the build output it must remove.

2. Ruled above. The hold is what the code does. It is not accepted.

3. Other `RunnerId` rewrites for a slot-bound task, outside the drain re-arm, were not audited. The worker reported the same.

4. The whole Unit lane, the 74 positive controls, V-42, and V-56 stay out of this round. Positive controls remain pending for method-scoped SourceLanding Mutation.

## D-1 ownership (closed for the tested attacks)

`ProveOwnership` (`RunnerWorkspaceSlotPool.cs` 553-610) is read-only. It requires a canonical `<worktrees>/<slot leaf>`, no reparse point on the directory, the `.git` file, the registration, `<primary>/.git`, or `.git/worktrees`, a registration whose parent is `<primary>/.git/worktrees` (never the primary `.git`), a `gitdir` back-pointer that canonicalizes to this directory's `.git`, and an owner stamp naming this slot id and `directoryGeneration`. Failure quarantines. Deletes use `owned.AdminDir`, not `rev-parse --git-dir`.

Destructive commands and the guard directly in front of them:

- `ResetForLeaseAsync` 253 proves, then abort, `checkout --force -B` (258), clean (261), and `ClearWorktreeState` `update-ref -d` / `DeleteFile` (264). `StampLease` 287 proves again before rewriting the stamp.
- `ResetForFreeAsync` 493 proves, then abort, `checkout --force --detach` (499), clean (502), `update-ref -d` of the task branch and its origin ref (509-510), `DeleteFile` of `owned.AdminDir/logs/HEAD` (513), and the `ClearWorktreeState` deletes (535).
- `SettleAsync` 379 quarantines on proof failure before any git in the slot. `ResetForFreeAsync` proves again before its own commands.
- `ColdResetAsync` 1064-1069: preflight, then a second proof, then clean.
- `RetireIdleAsync` 1173 preflight, then `worktree remove --force` of `candidate.Path` (1176). Same string the proof read. No directory fd is held.
- `ReleaseQuarantineAsync` 962-963 `worktree unlock` then `worktree remove --force --force` of the recorded quarantine path. No `ProveOwnership`. This is the operator disposition after Pushed, AcceptLoss, or Removed. Git refuses a path that is not a registered worktree. Advisory, not a data-safety defect.
- `PruneRetained` 800 `Directory.Delete` deletes old retained evidence under `RetainedRoot` and skips a directory named `quarantine`. It is not a slot worktree.

What remains on TOCTOU: the proof returns path strings. It does not hold an `O_NOFOLLOW` directory fd. Inside `ResetForFreeAsync` the proof runs once, then abort, checkout, clean, `update-ref -d`, and `DeleteFile` follow on that string. A local replacement of the slot directory after the proof and before those commands is not re-checked.

Witnesses: CP-8 is 14/14 at the tip and includes `C0824_ReleaseProvesOwnershipBeforeAnyDestructiveGit` (primary, sibling, symlink). At `95b8ff5dd9689c1d4f88b6f32fab87bff4a005cc` (tests present, fix absent) that method was 3/3 red. Primary failed the snapshot: HEAD was detached, the reflog was absent, and the task branch was gone. Sibling and symlink came back `released`.

Component mutants, restored with `git checkout -- src/Antiphon.SessionRunner/RunnerWorkspaceSlotPool.cs` after each run. The tree was clean afterwards.

- Primary-`.git` check removed (`IsLink` of `<primary>/.git` and the registration-parent `PathsEqual`): the ownership method stayed 3/3 green. The planted primary `.git` is a normal clone directory with no `gitdir` file, so the later "no gitdir back-pointer" check still quarantines. The witness does not isolate that component.
- Back-pointer `PathsEqual(realBack, realDotGit)` removed: the ownership method stayed 3/3 green (build lease `ebb19708-c6da-4c0c-9586-c202ce2f4580`, run lease `a6bf4a08-ef37-46dd-b903-4006493ca744`, duration 4s 802ms). The owner stamp names the other slot id, so the stamp check still quarantines.
- M-D1, the requested mutant: `ProveOwnership` returned `Owned` immediately. The ownership method went 3/3 red (build lease `3009d0b8-5ac5-48f4-80d0-36853ba91f65`, run lease `55fff84d-2779-42cf-a0b4-0e495539981d`, duration 2s 003ms). The failure was `entry.Class` `custody` rather than `filesystem`. The victim snapshot assertion passed: `SettleAsync` reached `UnpushedAsync` / `DirtAsync` and quarantined as custody before `checkout --force`. The proof as a whole can go red. The two components do not have isolating witnesses. Advisory. The planted attacks still fail closed.

M-D2 and M-D3 were not re-run. The brief asked for one of the three.

## D-3 items that hold

A confirmed `released`, `already_free`, or `quarantined` reply stamps the release and then the rebind proceeds. Anything else, including a lost reply, keeps the old runner id. The pending release lives on the task row. `RecoverAsync(null)` keeps Leased slots. Ad-hoc `C0824_RestartKeepsLeasedSlotForTheServersRelease`: 1 passed, 0 failed. `RemoveMirrorAsync` skips removal only while the slot id is set and the release stamp is null. The enablement gate is in `docs/session-runtime-invariants.md` and plan §3.5b.

## Checkpoints

Tool run `20261102-101400-07d9`. Plan `docs/superpowers/plans/2026-11-02-card-0824-S1-review-fix-checkpoints.md`. Rows CP-1, CP-8, CP-9, CP-4, CP-21. `--total-timeout 70m --hard-wall-timeout 75m`. Exit 0. Wall 10m23s. Five green, zero red, zero skipped. Unlisted inside the tool: none. `validate-checkpoint-receipt.ps1` exited 0: `CHECKPOINT SOURCE VALID source=009759428ccd73994b3241345dcf970858edefed rows=5`. CP-8 and CP-9 were added to the brief's CP-1 / CP-4 / CP-21 list because (a) and (b) are those classes. V-12r is inside CP-1. V-12 is inside CP-4.

CHECKPOINT CP-1 commit=009759428ccd73994b3241345dcf970858edefed build=ok filter=/*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotResetTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-f4c293bc/.antiphon/checkpoints/20261102-101400-07d9/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=009759428ccd73994b3241345dcf970858edefed sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=009759428ccd73994b3241345dcf970858edefed build=reused filter=/*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotRehomeTests/* executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-f4c293bc/.antiphon/checkpoints/20261102-101400-07d9/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=009759428ccd73994b3241345dcf970858edefed sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=009759428ccd73994b3241345dcf970858edefed build=reused filter=/*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotColdResetTests/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-f4c293bc/.antiphon/checkpoints/20261102-101400-07d9/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=009759428ccd73994b3241345dcf970858edefed sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=009759428ccd73994b3241345dcf970858edefed build=ok filter=/*/Antiphon.Tests.Application/C0824WorkspaceSlotServerTests/* executed=23 passed=23 failed=0 skipped=0 trx=/work/worktrees/task-f4c293bc/.antiphon/checkpoints/20261102-101400-07d9/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=009759428ccd73994b3241345dcf970858edefed sourceState=clean buildSource=verified
CHECKPOINT CP-21 commit=009759428ccd73994b3241345dcf970858edefed build=reused filter=/*/Antiphon.Tests.Application/PhoneHomeRollingRunnerTests/* executed=32 passed=32 failed=0 skipped=0 trx=/work/worktrees/task-f4c293bc/.antiphon/checkpoints/20261102-101400-07d9/rows/CP-21/run.trx slot=granted waited=0s dirty=0 source=009759428ccd73994b3241345dcf970858edefed sourceState=clean buildSource=verified

Red at the pre-fix commit, detached worktree `/tmp/c0824-red-f4c293bc` at `95b8ff5dd9689c1d4f88b6f32fab87bff4a005cc`, build lease `74d4c0e5-7ac5-4da9-b280-fa80e5d6ec44` held 98s, then removed. `C0824_ReleaseProvesOwnershipBeforeAnyDestructiveGit` 3 failed / 3 (lease `ca5ab178-12d8-4596-afe7-d81b1e3b7155`). `C0824_ColdResetQuarantinesWhatALeaseWould` 3 failed / 3, outcome `cold_reset` (lease `dd9f205a-9e98-4aaa-a03d-57f0967727eb`).

## Land, evidence, merge

`scripts/audit-land-verification-graph.ps1 -Check` (no `-Update`): `digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE count=69` approved. Five not-built projects sit outside the recorded graph.

`check-evidence-diff.ps1` `41e63343b6449c6a97ee7c8df3bfe52da0354ea9..fa2531861329f57505886dd03e24db1888033692`: commits=3 entries=0 violations=0. Same base through the tip: commits=4 entries=1 violations=0. The one entry is the worker report.

`git fetch origin master rc/2026-11-01`. `origin/master` is `3a49b60133327f4441f06698ef3369b1d277d049` (later than `10c8c2471`). `git merge-tree --write-tree origin/master HEAD` exit 0, tree `44aa15898a770afb5e474b55bb0389ae5f8bbb42`. `origin/rc/2026-11-01` is `e114313d36ef0892676c4e0f4be2c956808c7978`. Merge-tree exit 0, tree `fff104137028037980496270911da74a06af49c3`. The feature stays master-only.

## Advisories

- The two proof-component mutants stay green. The isolating witness is the whole-proof short-circuit, which fails on the failure class while the victim snapshot still matches. A card, not a second review.
- `ReleaseQuarantineAsync` removes the recorded quarantine path without `ProveOwnership`.
- `git clean -ffdx` is wider than the keep-list, so a file created after the preflight returns and before the clean can be deleted.
- The terminal stuck warning says to record the release by hand, and no API does that. That gap is part of D-1's fix.
- CP-21's class was the slow one in the tool run (the row still passed inside the 70 minute budget).
- Positive controls stay pending.

## Evidence block facts

subjectTaskId `cf298805-851e-4b69-bc3e-ed5ae03816d1`. reviewedSourceSha `009759428ccd73994b3241345dcf970858edefed`. reviewedSourceClean true: the five-row receipt validated, dirty=0, sourceState=clean, buildSource=verified. ordinaryScopeCompleted Full for this round's commissioned list (a)-(d), which caps the Unit lane. The Unit lane was not run. Positive controls were not run.
