# Why missed: run 34d41b7e group 58, CodexCliObservationGapTests

Debug task 31c683a8. The rc is rc/2026-11-20 (`b559bfba2e9a420495122d27171352fe9b7e1ba4`). Master was
at the same SHA when the fix was made. Failure extract: task 71cec1b8,
`docs/investigations/2026-11-14-run-34d41b7e-g58-codexcliobservationgap-windows-failure.md`
(commit `60380b9bd`).

## Failure and classification

`CodexCliObservationGapTests.C1029_Unobservable_timestamp_floor_is_original` failed at :359 on its
first iteration (ClaudeCode, `older`). It expected `Pending` and got `Sent`.

**Classification: stale test expectation, a TEST FIXTURE defect. It is deterministic, not
timing or load.** It is not a product defect and not a Windows skip.

- The test loses the guarded `ConditionalInput` reply (`HoldPrompts(w, loseInputReply: true)`).
  The phone-home Error frame becomes `ConditionalInputOutcomes.Unknown`, then the
  `RequireWritten` `IOException`, then `InputDeliveryUncertainException.Is`. Since CARD-1892 that
  counts as delivery-unknown: the claimed attempt stays `Sent` with a null verdict. Before
  CARD-1892 it went to the generic catch, then `RevertRunAsync`, then `Pending`.
- No clock, poll or deadline takes part. The assertion reads one DB row after `JoinLaunchAsync`.
- It was reproduced red on the Windows desktop at `b559bfba2`, in an isolated single-method run with
  no rc chunk beside it. Same assertion, same line.
- Codex body writes are not guarded. `AgentSessionRuntime.SendInputAsync` routes non-Claude kinds
  through plain `Input`, where a lost reply is a definitive failure, so Codex still reverts to
  `Pending`. Only the ClaudeCode expectation changed.
- The test is OS-neutral. It would fail the same way on Linux. Windows saw it first only
  because group 58 runs this class in the rc roster, and no Linux selection ran it after CARD-1892.

## Fix

In `tests/Antiphon.Tests/Application/CodexCliObservationGapTests.cs` the post-launch assertion is
now kind-aware. ClaudeCode expects `Sent`, a null `DeliveryVerdict` and `DeliveryAttempts == 1`,
which is the CARD-1892 contract in `docs/session-runtime-invariants.md`. Codex keeps `Pending`.
The floor, recreation, no-false-settle and settled-floor assertions are unchanged. On the aged
recreate (+2 min > `InterruptedAttemptAge`), the ClaudeCode `older`/`null` branch still proves
that no stale settle happens. Interrupted recovery late-confirms nothing, reverts the row on a
clear composer, and the next claim hits the `RejectRetry` fault once. Product code is unchanged.

## Introducing change

- Commit `5b80144d3` *fix(card-1892): retain live sessions and attempts after ambiguous Input*,
  CARD-1892, a cherry-pick of `0b2c08ae7`. It touched `InputDeliveryUncertainException.cs` (new),
  `AgentSessionService.cs`, `GrokRulesRefreshService.cs` and `SessionMessageQueueService.cs`.
- It reached master in the batch that `1fde603cc chore(merge-213)` closes, Merges 212-214. The first rc
  that contains it is rc/2026-11-20. rc/2026-11-19 (`ab7af12c6990`) does not contain it, and the
  same test passed in group 58 of run 97150a0b.

## Approving Reviews and their selections

| Gate | Selection run | Contained CodexCliObservationGapTests? |
|---|---|---|
| CARD-1892 Code rounds 1-3 (`docs/superpowers/plans/2026-11-13-card-1892-*.md`) | Closed checkpoint rows: C1677RetentionDispatch, PhoneHomeCommandDispatcher, C1722GitTimeoutBudget, GrokRules*, PhoneHomeLaunchTransport, SessionMessageQueueInterruptedAttempt, RunnerTerminalSession*, AgentSessionCancelledTurn, AgentSessionInterruptedLaunchResume, AgentSessionLaunchFailure, SessionMessageQueuePhoneHomeDrop, Attention*, SessionMessageQueueCodexPointer, guard union | No |
| Review round 1 (`docs/investigations/2026-11-13-review-card-1892.md`) and Review caeec914 round 2 (`2026-11-13-card-1892-review-caeec914.md`) | Re-ran the author's rows. Run `20261113-180544-d72a` covered CP-1 (C1677, PhoneHomeCommandDispatcher, C1722) and CP-3 (guards). The findings were about Grok composer-less retyping. | No |
| Review d552541f (diff-only round) | The diff since `01edf03f6`, with the author's rows | No |
| Merges 212-214 | Selected classes only (see `2026-11-14-session-runtime-action-delivery-fixture-debug.md`, which records that their selections also missed the SessionRuntimeAction* recovery interactions) | Not shown in any local record; treated as no |

## Why the failing test was outside the selections

1. **The impacted-classes selector had no row for the uncertainty seam.** For CARD-1892,
   `tests/impacted-classes.json` matched only `test-lane-category`. No kind keys on
   `InputDeliveryUncertainException.cs`, `ProviderDialogObservationService.cs` or the phone-home
   `ConditionalInput` error mapping, even though the change reclassifies those outcomes.
2. **The derived closure was computed but not run.** `tests/derived-impact-index.json` lists this
   class under `GrokRulesRefreshService.cs`, which `5b80144d3` touched. The CARD-1892 evidence
   records the wider closure (100/101, then 38+21 owed classes) as "not represented as passed".
   `SessionMessageQueueService.cs` is a capped path, so any class reached only through the queue
   service is not even listed there.
3. **Author and Reviewers chose classes by the behaviour they changed** (Grok rules, launch
   retention, interrupted attempts). They did not choose by the tests that *assert the old
   behaviour*. C1029 is a Codex-observation class by name, so the name did not suggest a Claude
   guarded-input expectation inside it.

## Cheapest pre-land check (impacted-classes row)

This is the proposed `tests/impacted-classes.json` kind. It is not added in this Debug, because the
row is a selector-policy change for the owner of that file:

```json
{
  "id": "guarded-input-uncertainty",
  "globs": [
    "server/Application/Exceptions/InputDeliveryUncertainException.cs",
    "server/Application/Services/ProviderDialogObservationService.cs",
    "server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs"
  ],
  "classes": [
    "CodexCliObservationGapTests",
    "SessionMessageQueueInterruptedAttemptTests"
  ]
}
```

`5b80144d3` added `InputDeliveryUncertainException.cs`, so this row would have selected the class
in CARD-1892 Code. The cost is one class of about 6.5 minutes on Windows (7 tests; the per-kind
receipt test takes most of it). The other C1029 tests that lose an Input reply would also be
covered. A grep shows that only this method uses `loseInputReply: true` today.
