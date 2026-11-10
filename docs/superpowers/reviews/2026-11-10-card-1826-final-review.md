# CARD-1826 Final Review

Verdict: CLEAN. Round 1, Final, ordinary scope Full. No product defect.

Reviewed source `f72a8a583e9c24148e47cef44396b84755dae130` (owner Code task `0a99359a-16c5-4c14-9cf9-f6942b75ba67`, branch `feat/card-task-0a99359a`). Base `ac6d00022e940ee3b90584c7d313567b4491c498`. Red witness `72747c3bb902f756e8c090d7defe7a7317e6d3c9` adds only `C1826_mixed_source_machine_note_keeps_its_own_chat`. The fix commit is the reviewed tip. Receipt `scripts/validate-checkpoint-receipt.ps1` on run `20261110-123050-d198` accepted both rows for that SHA (`CHECKPOINT SOURCE VALID`).

Positive controls stay pending for method-scoped SourceLanding Mutation.

## Behavior

A machine-note debt row keeps the chat it publishes to when a late-confirmed source for another chat merges onto it. Before this change, `debt.ConversationKey != key` cleared the key to null, and a null key waits for every destination and, on retry, publishes to the newest chat (unified recovery off) or the pre-injection chat (unified recovery on, when the machine row matches). The carried key is now left as stored. Head selection, the failure due-at push, and both owed checks also treat a late-confirmed message's destination as a destination of that row.

What became less conservative: an unrelated chat, one that shares neither the carried key nor a late-confirmed destination, no longer blocks the note, and the note no longer blocks that chat. The note also no longer retargets onto another chat at retry.

What stayed in force:

- The note still waits for the late source's own chat, so that source cannot publish ahead of an owed reply to that chat.
- A null carried key is still every destination.
- A new debt row whose late sources span more than one chat still stores a null key. Only an existing carried key is preserved.
- Retry calls `ExecuteActionChannelReplyAsync` with the stored key. The failure update does not assign `ConversationKey`. The machine-note send uses that carried key and does not replace it with the newest chat.
- Late sources publish through `OnLateConfirmedAsync` on their own correlations, not on the note's key.
- Give-up is unchanged (`ReplyDebtGiveUpAttempts` default 20, `ReplyDebtGiveUpMinutes` default 10, and only after a failure). A note held by another chat is released when that chat's debt completes or is abandoned. It is not held by an unrelated chat, and it is not held past that chat's own clock.
- Rows written before CARD-1454 are documented in `docs/telegram.md` and `docs/session-runtime-invariants.md`. Nothing rewrites them. With unified recovery on, a retry uses the key already stored on the row.

## Clause reading

Sequence bounds are the pre-existing ones. Head selection and the channel-reply owed check use earlier rows only (`Sequence <`). The due-at push uses later rows only (`Sequence >`). The late-confirm owed check still uses `Sequence <=`, so the same-turn debt row counts. The new overlap sits inside those bounds. A row does not block itself.

A queued message with a null `ConversationKey` is excluded from the overlap (`ConversationKey != null`). A null message key is not a chat: `NewestConversationKeyAsync` already ignores it. If the only distinct late key is null, the late-confirm owed check still sees key null and waits for every earlier debt at that decision. After a merge onto an existing keyed row, that null message does not link other chats.

A failure on chat B does push the note's due-at when the note's key is chat A and the note carries a late-confirmed message for B. That matches head selection: the note still owes B. The push keeps the note from being woken before B's retry. Head selection would refuse the note anyway while B's earlier row exists. An unrelated chat C is not pushed.

The four sites use the same destination predicate. The owed checks are the one-direction form (this route's key against an earlier row's key or late messages). At late-confirm time the computed key is the late destination, or null when the late sources span more than one chat, so that form still waits for the other chat before the ids are merged.

## Witness and mutants

`C1826_mixed_source_machine_note_keeps_its_own_chat` runs with unified recovery off and on. It asserts the stored key stays the note's chat, the note does not publish while the other chat is owed, a later reply to that other chat waits, and the final send order is owed A, owed B, the note to A, the later reply to B.

M1, restoring `if (debt.ConversationKey != key) debt.ConversationKey = null`, fails the key assertion: the late key is the other chat and the stored key is the note's chat. M2, removing the overlap clauses, lets the note run once its due time is past while the other chat is still owed, which fails the "note waits" send assertion. Both mutants are caught by that witness. This review did not apply them. They stay pending.

`C1259_shutdown_joins_and_restart_reconciles_submission(reconnect-cohort)` passed in this run (TRX duration 15.7s). The diff does not touch `SessionRuntimeActionHostedService.StopAsync`. CARD-1422 recorded the same variant failing a 10-second wait while checkpoint rows overlapped, and passing alone, on a path that does not reach channel debt. It is not a defect of this change.

## Verification

No migration files. `scripts/impacted-classes.ps1 -Range <base>..<tip> -MergeSubset` reported `migrations=False`, so the downgrade selector did not expand. Downgrade caller count: 0. The diff-selected impacted row is `TestLaneCategoryGuardTests`, which is inside CP-2. Policy JSON is unchanged (not in the diff). No OS-gated lexeme (`OperatingSystem`, `RuntimeInformation`, `IsWindows`/`IsLinux`) in the changed production files, so there is no OS-branch witness. Platform stays unpinned.

`scripts/check-evidence-diff.ps1` over the base..tip range: `entries=0 violations=0`.

`git merge-tree --write-tree origin/master <tip>` exit 0, tree `9ae0c36f1ad65937f87671577d4cbedf05f6f48a`. `origin/master` is `ac6d00022e940ee3b90584c7d313567b4491c498`, which contains `b067f912ae01234d744c5862cc90285b5859dc1a`.

The canonical guard filter executed 78, not the brief's 135. That 78 is the same roster the 2026-11-09 lane note recorded for this filter after its fix. All nine class tokens matched, including `TestLaneCategoryGuardTests`, `MechanicalPcContractTests`, `TaskPlatformGuidanceTests`, and `C1729LayeringGuardTests`.

CHECKPOINT CP-1 commit=f72a8a583e9c24148e47cef44396b84755dae130 build=ok filter=/*/*/(SessionRuntimeAction*)|(ChannelMachineTurn*)/* executed=130 passed=130 failed=0 skipped=0 trx=/work/worktrees/task-e9643d27/.antiphon/checkpoints/20261110-123050-d198/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=f72a8a583e9c24148e47cef44396b84755dae130 sourceState=clean buildSource=verified

CHECKPOINT CP-2 commit=f72a8a583e9c24148e47cef44396b84755dae130 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)/* executed=78 passed=78 failed=0 skipped=0 trx=/work/worktrees/task-e9643d27/.antiphon/checkpoints/20261110-123050-d198/rows/CP-2/run.trx slot=granted waited=150s dirty=0 source=f72a8a583e9c24148e47cef44396b84755dae130 sourceState=clean buildSource=verified

Run wall 20m22s. CP-1 test wall 184s (build 549s). CP-2 test wall 96s. `C1826` uses the class's existing isolated schema per case and adds no timeout. No selection exceeded 30 minutes.

R1: the new test does not poll; it drains and asserts the full sent list. R2: no generated line-ending text. R3: this is not a terminal-state change; the touched `SessionRuntimeAction*` and `ChannelMachineTurn*` classes are CP-1. R4: no limiter change. R5: the reconnect-cohort case passed here, so there is no new passes-alone red to file.
