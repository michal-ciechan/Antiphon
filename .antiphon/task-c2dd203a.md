# CARD-0680 / CARD-1591 dialog-guard debug (task c2dd203a)

Adopted Claude writes now refuse a working screen, a stale idle flag, and an operator draft. A body is typed only into a current empty idle composer. Enter submits only when that composer holds the body this attempt typed. A durable withhold still counts one attempt and does not stamp a typing time, so shrink and cancel still treat the row as untyped. Known-history Clear, including a working spinner, is unchanged.

Branch `feat/card-task-c2dd203a`, fast-forward from `1f4fab572766fdcfea9f1c17feb8e6c85518172c`.

- `35bcfae6fc60075a6bfc1f850e1346cf493a6edc` test: refuse stale idle, live fence, and draft custody
- `3b8fca0790742b8399717f6b67bfe67fb25c0c45` fix: recheck adopted idle at the write and keep draft custody

Session `b2f1a869-9bc5-4974-b8ae-53686448fd20` and its pending notes were left untouched. The activation plan is documentation only.

## What the write does now

Historical TurnEnd `end_turn` is not enough for an adopted session. An adopted body is allowed only when the screen state is Clear, the screen is not working, the tailer is bound, and the transcript snapshot is caught up: readiness status exactly `Ready`, last row TurnEnd `end_turn`, no open tools. A working spinner refuses as ObservationUnknown / AdoptedSession. That is a charge. It is not the zero-charge BlocksInput dialog reason.

The runner fence re-reads the live screen and the transcript under the screen lock immediately before the bytes. The idle boolean previously captured before preparation is gone. A same-sequence Working paint and a transcript-only advance (AssistantText, no output-sequence change) both refuse at that fence.

A body write requires an empty idle composer. Enter (`\r` or `\n`) requires the composer to hold the body this generation typed. Paste markers are stripped and the match is against composer content (`WholeBodyEquals` or `HeadFragmentIsVisibleWhole`). An operator draft is not appended and is not submitted. Ownership is recorded only after a successful guarded non-Enter write, and cleared on Enter, on write failure, and on `BindAcceptedGeneration`.

`ChargeDurableWithholdAsync` still moves attempts 0 to 1 and keeps the baseline sequence. It does not set `LastDeliveryStartedAt` or `LastDeliveryGeneration`. Shrink, cancel, Enter-only recovery, and the in-memory held-back filter key off those two stamps. One charge does not park at 3.

Kept: distinct withhold reasons (SnapshotUnavailable, GenerationMismatch, BlocksInput, InputUncertain, ScreenUnknown, AdoptedSession, ObservationUnavailable). Maintenance intents still require InputHistoryKnown. Herdr snapshots stay Unknown because the new flags default false. An old runner that omits the flags fails closed on an adopted body. Known-history Clear, including a working spinner, still types.

`ProviderDialogObservation` gained three trailing bools, default false: `CurrentIdleComposer`, `ScreenWorking`, `ComposerHoldsAutomatedBody`. `TranscriptIdleEvidence.FromSnapshot` takes `requireCaughtUp` (default false). The adopted path passes true and also requires a bound tailer. No database column was added.

## Tests

All runs used `pwsh -NoProfile -File scripts/build-slot.ps1` and `--property:OutputPath=bin-c2dd203a/`. Antiphon.Tests also used `--property:UseAppHost=false`. TUnit was `dotnet run --project tests/<Project> --no-build`. Owned `bin-c2dd203a` directories were deleted after the runs.

Red, commit `35bcfae6f`, class `ClaudeDialogInputGuardTests`: 16 total, 7 failed, 9 passed. The failures were the new refusals, each returning `written` where the witness requires `observation-unknown`:

- `AdoptedHistoricalIdleWhileWorkingIsNotTyped`
- `AdoptedScreenBecomesWorkingBeforeWriteIsNotTyped`
- `AdoptedTranscriptAdvancesBeforeWriteIsNotTyped`
- `AdoptedOperatorDraftIsNotTyped`
- `AdoptedBehindTranscriptIsNotTyped`
- `AdoptedEnterOnAnEmptyComposerIsNotTyped`
- `AdoptedEnterDoesNotSubmitAnOperatorDraft`

Green, commit `3b8fca079`, same class: 17 passed, 0 failed, 924ms. The extra method is `AdoptedEnterSubmitsTheOwnedComposer`, added with the fix, so it has no red run.

Green, same commit, after an Antiphon.Tests build with 0 errors and 1198 warnings:

- `AdoptedDialogDeliveryTests`: 12 passed, 0 failed, 1m 12s. These tests were committed in the red slice and were not executed against the pre-fix tip in this session.
- `PolledCompletionNoteShrinkTests`: 9 passed, 0 failed, 38s. Includes the withhold-charged polled note that still shrinks. Same commit timing as the adopted class.
- `AttentionServiceTests.a_pending_message_that_has_attempts_left_is_not_parked`: 1 passed, 0 failed, 33s. The assertion requires the message's only attention item to be `PendingNotesAged` at Warning, and requires it not to be `ParkedMessage`. The warning was not removed. `PendingNoteAlertAgeSeconds` stays 900.

## Left as found

`scripts/test-checkpoint-large-stamp.ps1:92` (C1532) fails at base `9f00008facfc48e75555c08df59887019b3f8dfe`. The script was not edited.

PostgreSQL `53300` appeared in the review's budget-killed `ClaudeDialogDeliveryTests` setup. It did not recur on `AdoptedDialogDeliveryTests` or `PolledCompletionNoteShrinkTests`. It was not rerun at the base commit. Pooling was not changed. Treat it as unresolved and environmental until a base run shows otherwise.

Whole Unit was not run. The seal-reader checkpoint tool was not run (task 378a40c2). `scripts/delegate.ps1 -Status` was not re-run this round; the review text had already been read from `/tmp/review-c3d96767/report.md`.

## Activation (not applied)

`docs/investigations/2026-10-27-card-1591-adopted-backlog-activation.md`

Reconcile receipts first, expire or supersede notes whose obligation is gone, then release the remainder one head run at a time only while the composer is empty and idle, at a bounded rate, waiting for one complete UserPrompt before the next body. Do not point the backlog at an old runner: missing flags fail closed and charge once, which can disable the zero-attempt shrink. The CARD-0680 paragraph in `docs/session-runtime-invariants.md` matches this write rule.

## Review handoff

Fresh Final Review of `feat/card-task-c2dd203a`. The code tip of the fix is `3b8fca0790742b8399717f6b67bfe67fb25c0c45`. This file is the only later addition.
