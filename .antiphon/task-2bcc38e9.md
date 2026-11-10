# CARD-1454 Review round 1 (Final) — task 2bcc38e9

Subject: origin/feat/card-task-7df80f0a tip `11c802c1d00082766513d11dda69279afe8e4adb` (Code owner 7df80f0a, Grok Code author), range `72fada66545c2dc96a0710827fbc57558700dd61..11c802c1d00082766513d11dda69279afe8e4adb` (red `daba6d52d`, fix `11c802c1d`).

Verdict: CLEAN — no blockers. Advisories A-1, A-2 below; they do not hold the land.

## Checks

| Check | Result |
|---|---|
| (a) both settings, two chats | `C1454_machine_turn_debt_follows_the_chat_it_publishes_to(False|True)` both Passed. True: note waits behind owed chat-A reply even though chat B is newest; debt row key = A; retry order [owed answer, machine note] all to A. False: debt keyed A at resolve, chat B arrives and its reply flows past; retry sends note to A, not newest B: [B, A, A]. |
| (b) persistence | No schema change: `SessionRuntimeAction.ConversationKey` already exists (CARD-1434). The carried key is the existing debt-row column; no migration. Rows with a null key fall back to `NewestConversationKeyAsync` (pre-CARD-1454 behaviour), no crash. |
| (c) resolve once | `ResolveActionRouteAsync` and `DispatchMachineTurnFollowUpAsync` now share `MatchUnsettledMachineRowsAsync` and `FindOriginalMachineContextAsync`. Both coordinator publish sites pass the resolved key (direct phase-4 dispatch `SessionRuntimeActionCoordinator.cs:343` passes `key`; debt retry `:243` passes `action.ConversationKey`); the dispatcher uses the carried key in preference to its own resolution (`ChannelReplyDispatcher.cs:1527`, `:1576`). |
| (d) neighbours | Give-up/expiry (`TryAbandonExhaustedChannelDebtAsync`), retry DueAt push, cancellation rethrow, and the late-confirm phase are untouched. Touched classes `SessionRuntimeAction*`/`ChannelMachineTurn*` 128/128 green incl. C1433/C1434/C1435 debt witnesses. |
| (e) mutants | M1 publish ignores carried key (newest/context always): `(False)` Failed on `producer.Sent.Select(r => r.ConversationId)`. M2 unified resolver returns newest: `(True)` Failed on `producer.Sent should be empty`. Both killed; mutations restored, `git diff --quiet HEAD` clean. |
| (f) OS / categories | No OS branches in the diff. New test is in `SessionRuntimeActionRecoveryTests` (`[Category("Integration")]`, `[Category("Slow")]`). Guard union 53/53. |
| evidence-diff | `check-evidence-diff.ps1` commits=2 entries=0 violations=0, exit 0. |
| merge-tree | `git merge-tree --write-tree origin/master HEAD` clean (tree `fc4590a3d`); origin/master 72fada665 is an ancestor. |

## Runs (all through build-slot, native output, UseAppHost=false, TEMP=/tmp, at 11c802c1d)

- build Antiphon.Tests: exit 0, 279 s.
- touched `/*/*/(SessionRuntimeAction*)|(ChannelMachineTurn*)/*`: total 128, failed 0, skipped 0 (4m14s).
- guards `(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(TestLaneCategoryGuardTests*)`: total 53, failed 0, skipped 0; all six classes present in TRX.
- M1 / M2 mutant builds + C1454 runs: 1 failed / 1 passed each, as described above.
- Checkpoint tool receipt: see the receipt section.

## Advisories

- A-1 (late-confirm merge): when a machine-turn phase-4 debt row also receives late-confirmed sources for a different chat, `ExecuteLateConfirmPhaseAsync` nulls `ConversationKey`; the retry then falls back to the newest chat (pre-CARD-1454 retargeting). Ordering still holds (a null key waits for every destination). Narrow corner; card it if wanted.
- A-2 (upgrade transition, UnifiedRecoveryEnabled=true only): a machine-turn debt row written before this change carries the then-newest chat, which now overrides the pre-injection context at retry, while the reply handle still comes from `originalContext`. Only rows live across the deploy (give-up 10 min default) and only with the non-default setting.

PCs: all pending for method-scoped SourceLanding Mutation.

## Checkpoint receipt

Review-only extract `.antiphon/review-2bcc38e9-plan.md` (gitignored), run `20261110-043023-4ee7`, verdict GREEN exit 0, wall 17m21s, 1 cold build, unlisted: none.

```
CHECKPOINT CP-1 commit=11c802c1d00082766513d11dda69279afe8e4adb build=ok filter=/*/Antiphon.Tests.Application/(SessionRuntimeAction*)|(ChannelMachineTurn*)/* executed=128 passed=128 failed=0 skipped=0 slot=granted waited=0s dirty=0 source=11c802c1d00082766513d11dda69279afe8e4adb sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=11c802c1d00082766513d11dda69279afe8e4adb build=reused filter=/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(TestLaneCategoryGuardTests*)/* executed=53 passed=53 failed=0 skipped=0 slot=granted waited=60s dirty=0 source=11c802c1d00082766513d11dda69279afe8e4adb sourceState=clean buildSource=verified
CHECKPOINT SOURCE VALID source=11c802c1d00082766513d11dda69279afe8e4adb rows=2
```

No CARD-1454 plan `### Checkpoints` table exists in the repo; scope came from the brief (touched classes + guard union), all executed.
