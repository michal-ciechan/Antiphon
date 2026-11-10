# CARD-1505 final review (task 471eab6e)

CLEAN. Collapsed-paste retry is Enter-only, and the same composer reading holds the next row.

- Subject: Code task `78d7c152-0175-4535-b524-b055c8c51b7a`, branch `feat/card-task-78d7c152`
- Reviewed source: `7c6af459fd31e20ffd2529394b9c112f4caebc41`
- Reviewed range: `b8812bcb12ba309092007e216251284466181b5d..7c6af459fd31e20ffd2529394b9c112f4caebc41` (red `45bbbc480e0a97f4fc8b92734eea0d8691a0f534`, fix `7c6af459fd31e20ffd2529394b9c112f4caebc41`)
- Round 1, Final, ordinary scope Full
- Review branch: `feat/card-task-471eab6e`

There is no CARD-1505 plan and no `### Checkpoints` table. The closed list below is the brief's named classes and the guard union Code reported. Positive controls stay pending. The non-empty-box-classified-Clear mutant was not re-applied.

## Receipt

Run `20261110-152955-2552`. `scripts/validate-checkpoint-receipt.ps1` on `report.json` printed `CHECKPOINT SOURCE VALID source=7c6af459fd31e20ffd2529394b9c112f4caebc41 rows=2` (exit 0).

```
CHECKPOINT CP-1 commit=7c6af459fd31e20ffd2529394b9c112f4caebc41 build=ok filter=/*/*/(SessionMessageQueueWedgedHeadTests*)|(SessionMessageQueueInterruptedAttemptTests*)|(ExpectationDirectDeliveryTests*)/* executed=91 passed=91 failed=0 skipped=0 trx=/work/worktrees/task-471eab6e/.antiphon/checkpoints/20261110-152955-2552/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=7c6af459fd31e20ffd2529394b9c112f4caebc41 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=7c6af459fd31e20ffd2529394b9c112f4caebc41 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(ProcessSpawnLimitTests*)/* executed=135 passed=135 failed=0 skipped=0 trx=/work/worktrees/task-471eab6e/.antiphon/checkpoints/20261110-152955-2552/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=7c6af459fd31e20ffd2529394b9c112f4caebc41 sourceState=clean buildSource=verified
```

CP-2 class counts from that TRX: TestClassificationPolicyTests 41, TestClassificationGuardTests 1, SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, NativeLinuxPolicyWitnessTests 3, MechanicalPcContractTests 11, TaskPlatformGuidanceTests 5, C1729LayeringGuardTests 9, TestLaneCategoryGuardTests 1, ProcessSpawnLimitTests 57. The nine-class guard batch inside that union is 78. Code's 135 includes ProcessSpawnLimitTests, and this run reproduced 135.

Impacted-classes on the same range selected only `test-lane-category` / `TestLaneCategoryGuardTests`, which CP-2 ran. No `windowsFilter` line. The migrations row did not match, so the downgrade selector was not invoked (count 0).

Evidence-diff `-BaseRef b8812bcb12ba309092007e216251284466181b5d -HeadRef 7c6af459fd31e20ffd2529394b9c112f4caebc41`: commits=2, entries=0, violations=0, exit 0. An earlier `-Range` invocation is not a script parameter and exited 2 `ref_input`; it is not the result.

`git fetch origin master` then `git merge-tree --write-tree origin/master HEAD`: origin/master is now `f125755c1648533c473ce46311c307788b4a9b65` (the brief's pin `057bf1f0361bb507dd11a818763e9e094fe1d192` has moved). Exit 0, tree `6cc2ad0c2cbbb229b611978155a860890d434704`. Not merged.

## Behaviour

`ClassifyTypedComposerAsync` is the one reading for a Pending retry (`SessionMessageQueueService.cs` around 2740), interrupted Sent recovery (around 3407), and `TypedBodiesBlockComposerAsync` (F1 held-back rows, canceled typed rows, expectation nudges). A non-empty Claude box, including `[Pasted text #N]`, returns `BoxOccupied` immediately and is attributed to the head id. The retry and the interrupted recovery then send Enter only. Any other finding except `Clear` defers: a missing snapshot, an unreadable Claude box, and a one-snapshot empty reading (`GhostEmpty`) leave an interrupted row Sent and return Nothing. `Clear` is two settled empty reads, or a kind with no readable box whose normalised head is absent. Generation change and `DeliveryAttempts >= MaxDeliveryAttempts` (default 3) skip the screen. `RecoverPointerAsync` returns before the reading when the run has no Codex pointer.

Enter-only sends `\r` and waits for a transcript UserPrompt. With an observable baseline, a missing match is `NoTranscriptRecord`. The failure path increments `DeliveryAttempts`, saves, then reverts Sent to Pending. At the cap the row parks. The confirm loop is also bounded by `SubmitAttempts` and `TranscriptConfirmTimeoutSeconds`. A successful confirm finishes the original attempt and sets `Delivered`. The parked head stays in the F1 hold, so the next row is not typed over it. Release is a matching UserPrompt, two empty reads, or a new generation.

## Dangerous directions

Double delivery of this row: a collapsed paste and a fully visible head are Enter-only. The official C1505 witnesses are inside the 91. Diagnostic scratch sequences (deleted before the receipt) showed Enter-only for this row's own collapsed marker and for this row's text when the head is on screen, with verdict Delivered and attempts left at 1.

A previous paste marker or a user's own draft in the Claude box is also Enter-only. Scratch sequences submitted that other text once, left this row Pending with `NoTranscriptRecord`, and charged one attempt (1 to 2). The confirm matcher requires this body's head, so the other text does not mark this row delivered. Repeats stop at the attempts cap. This is the written contract (any non-empty box is occupied). It is an advisory for a follow-up card: Enter-only could defer when the box holds text that is neither this row's head nor a remembered paste index. `ComposerDeliveryEvidence.PlaceholderIndices` is unused by the classifier. It does not hold the land.

Lost delivery: an empty-then-occupied reading is `GhostEmpty` and the retry returns Nothing. `C650_Ghost_empty_composer_on_one_snapshot_keeps_holding_while_the_next_shows_the_body` is in the 91 and passed. A scratch two-frame retry saw Enter because dialog admission (`ReadDialogWithholdAsync` → `ObserveDialogAsync` → one `SnapshotRenderedScreen`) consumed the empty frame before `Classify`, so the classifier's first read was the occupied frame. That is the correct `BoxOccupied` response for the screen it was given.

A swallowed Enter on a collapsed paste charged one attempt and did not retype. The third failed Enter-only recovery is already pinned to park and hold the next row. It does not press Enter forever.

An unreadable Claude frame on interrupted recovery stays Sent and sends nothing. While the row remains inside `InterruptedAttemptWindowMinutes` (default 60), the flush returns Nothing and does not reach later Pending rows. The attempts counter does not climb on an unreadable frame alone, so a row that started under the cap does not self-park from unreadability. Past the window it leaves the interrupted selector and stays Sent with a null verdict. The same cliff already existed for a missing snapshot, and the contract says an unreadable frame defers and leaves the row Sent. Resolvers inside the window: a later readable box (Enter-only if occupied, revert and retype if clear on two reads), a late-confirm UserPrompt, a generation change, or already being at the cap.

Codex has no readable box. A whole visible head is `HeadOccupied` (Enter-only). An absent head is `Clear` (retype), which is the previous whole-screen rule. A frozen override that never shows the typed body still hits the pre-existing composer-evidence withhold; that is not this change.

A generation change skips the screen and may retype. The contract treats a new generation as a new process whose old composer is gone.

## Mutant and live check

The tip returns `BoxOccupied` for a non-empty box. Code's local mutant (non-empty classified `Clear`) was restored before `7c6af459f`: 3 failed, 1 passed, and it is absent here. It was not re-applied. Every PC stays pending for method-scoped SourceLanding Mutation.

A human can paste a long multi-line prompt into a running Claude session until the composer shows `[Pasted text #N +M lines]` and the body head is off screen. If that delivery is unconfirmed, the retry should press Enter only, and the next queued row should stay held until the box is empty on two settled reads, a matching UserPrompt arrives, or the session generation changes. Leaving unrelated text in the composer shows the advisory: the retry submits that text.

## Review evidence

```
subjectTaskId: 78d7c152-0175-4535-b524-b055c8c51b7a
reviewedSourceSha: 7c6af459fd31e20ffd2529394b9c112f4caebc41
reviewedSourceClean: true
ordinaryScopeCompleted: Full
reviewRound: 1
reviewedRange: b8812bcb12ba309092007e216251284466181b5d..7c6af459fd31e20ffd2529394b9c112f4caebc41
```
