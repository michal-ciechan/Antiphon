# CARD-0329 — make the nudge postdate gate use enqueue order, not delivery-confirmation order

**Plan pass, 2026-09-02. The timing mechanism is reproduced against the real Testcontainer + fake-Grok/ConPTY/tailer path; no production code is changed by this plan.**

## Verified verdict

CARD-0329 is real, but its wording needs one correction: `SentAt` remains the proof that a WhenIdle nudge was ultimately delivered and the response-window clock. It is the wrong wall-clock boundary for deciding whether a reply came after the nudge was *enqueued*.

An unmarked replay of `GrokDelegateEndToEndTests.a_Kind_Grok_worker_runs_from_the_delegate_script_to_a_grok_priced_settlement` ran the real delegate script, queue, fake process, transcript tailer and settlement path. Its persisted rows after 60 seconds were:

| Durable fact | Observed value |
|---|---:|
| `AgentTask.ReportNudgedSequence` | 3 |
| reply `TurnEnd.Sequence` | 6 |
| nudge `CreatedAt` | 22:34:54.739488Z |
| reply boundary `CreatedAt` | 22:35:20.739174Z |
| nudge `SentAt` | 22:35:20.766889Z |
| task state | still `Dispatched` |

The answer was after enqueue and strictly later than the nudged boundary, but its boundary was 27.715 ms before `SentAt`. `ClassifyReportAsync` rejected it forever at `boundary.CreatedAt <= deliveredAt`; the with-text boundary does not qualify for the deferred text-less sweep, and CARD-0294 only Blocks the original nudged boundary.

## Chosen contract

For a non-legacy, live task that has already been nudged, allow unmarked settlement only when all of the following hold:

1. The recorded nudge row still exists and has non-null `SentAt`; an ask that was never delivered cannot settle anything.
2. The current `TurnEnd.Sequence` is strictly greater than `ReportNudgedSequence`; the same boundary remains inert.
3. The current boundary's persisted `CreatedAt` is strictly later than the nudge row's persisted `CreatedAt`; it is after enqueue rather than stale or backfilled pre-nudge evidence.
4. For a text-less boundary only, the existing response window is still measured from `SentAt`.

This separates two facts that happen at different times: `CreatedAt` is the stable enqueue-order marker; `SentAt` is delivery confirmation. Do not drop the `SentAt` requirement or rely on the sequence gate alone: CARD-0248 deliberately prevents a later boundary that arrived while a WhenIdle nudge was still pending from being treated as an answer.

No migration, new task field, queue-delivery semantic, or CARD-0294 change is needed. The durable `ReportNudgeMessageId` already identifies the row whose two timestamps are needed.

## Slice 1 — load both nudge timings and correct the classifier gate

**File:** `server/Application/Services/AgentTaskReplyService.cs`

Replace the narrowly named `LoadNudgeSentAtAsync` helper with a small private facts shape (or equivalent projection) that loads both `SessionQueuedMessages.CreatedAt` and nullable `SentAt` by `AgentTask.ReportNudgeMessageId`. Keep the query `AsNoTracking`, return no facts when the id or row is absent, and do not infer a nudge from body text, session ordering, or a new heuristic query.

In `ClassifyReportAsync`'s existing non-legacy settle-anyway branch:

- Retain the fail-closed return when the recorded nudge is absent or `SentAt` is null.
- Retain the defensive no-`BoundaryFacts` warning/return and strict sequence comparison.
- Replace `boundary.CreatedAt <= deliveredAt` with `boundary.CreatedAt <= nudge.CreatedAt`.
- Keep `deliveredAt` unchanged as the anchor for `ReportNudgeResponseSeconds` when `turn.FinalMessageMissing` is true.
- Revise the comment and helper XML docs to distinguish “predates enqueue” from “has not been delivered”. `TranscriptEntry.CreatedAt`, not its backdated `Timestamp`, remains the comparison value, matching CARD-0046/CARD-0248's existing rule.

The legacy carve-out remains: a pre-CARD-0248 task with both new task columns null preserves its established behavior. A post-CARD-0248 task with a recorded sequence but no message id remains fail-closed, as it does today.

## Slice 2 — deterministic classifier coverage for both sides of the boundary

**File:** `tests/Antiphon.Tests/Application/AgentTaskDeliveryWatchdogTests.cs`

Add focused siblings to the existing delivered-nudge ladder. Use the real Postgres harness to create the nudge through `SettleDeferredReportsAsync`, then inspect `AgentTask.ReportNudgedSequence`, `ReportNudgeMessageId`, and that exact queued-message row.

1. **Fast confirmed reply:** arrange a later, with-text `TurnEnd` whose persisted time is after the nudge row's `CreatedAt` but before its `SentAt`; set `SentAt` non-null and invoke the normal reply classifier. Assert the durable ordering (`nudge.CreatedAt < boundary.CreatedAt <= nudge.SentAt`, later sequence, matching message id) and `Succeeded` / `UnmarkedAfterNudge` with the second turn's body. This is the formerly permanently-refused state, now expected to settle.
2. **Pre-enqueue protection:** keep the sequence later but seed the boundary at or before the nudge row's `CreatedAt`, then mark the nudge delivered. Assert the task stays `Dispatched` and has no completion event. This pins the safety property the relaxed `SentAt` comparison must not erase.

Leave the existing undelivered-nudge, same-boundary, ordinary delivered-unmarked, and text-less response-window tests intact. Together they prove that neither the delivery barrier nor the response clock moved.

## Slice 3 — preserve the real sub-poll regression

**File:** `tests/Antiphon.Tests/Application/GrokDelegateEndToEndTests.cs`

Keep CARD-0243's marked capstone as its one-turn control. Parameterize `BuildHarness` so the existing capstone continues to set `ANTIPHON_FAKE_REPORT_LINE=1`, while a new test intentionally omits it and exercises the two-turn nudge contract through the same real delegate-script, Testcontainer, ConPTY, queue, runner and `SyncTranscriptAsync` pump path.

The new test must not call `OnTurnEndAsync` itself or fabricate `SentAt`. It waits for the original unmarked turn, the automatic WhenIdle nudge, fake-Grok's immediate unmarked answer, and automatic settlement. Assert:

- `Succeeded`, `UnmarkedAfterNudge`, and the second fake response as the task result.
- A non-null `ReportNudgedSequence` and `ReportNudgeMessageId` resolving to the delegated nudge row.
- A latest reply `TurnEnd` with sequence greater than `ReportNudgedSequence` and a non-null nudge `SentAt`.
- The reproduced ordering, `nudge.CreatedAt < boundary.CreatedAt <= nudge.SentAt`, so the test proves it exercises the confirmation/tailer race rather than only the ordinary post-delivery path.

When locating the original brief row, select it by its spill-pointer body rather than applying `SingleAsync` to every queue row: the unmarked variant correctly has both the brief and the closing-line nudge. Its timeout diagnostic should print the task sequence/id and the three persisted timestamps, making a future timing regression directly actionable.

## Verification

Run the narrow Tests project class once after slices 1–2, then the process-spawning capstone alone; do not run it concurrently with `Antiphon.Agents.Pty.Tests`.

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0329/ -- --treenode-filter "/*/*/AgentTaskDeliveryWatchdogTests/*"
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-card0329/ -- --treenode-filter "/*/*/GrokDelegateEndToEndTests/*"
```

The first run validates the classifier matrix; the second is the Testcontainer/ConPTY confirmation that originally exposed the race. Expect existing repository compiler warnings unless separately fixed; the relevant verdict is the two test classes' totals with zero failures. Use a forward-slash alternate output path and remove only verified `bin-card0329` directories after both runs.

## Rejected alternatives

- **Sequence gate only:** reopens CARD-0248's pre-delivery hole; a later boundary does not prove a queued nudge was ever seen.
- **Keep `SentAt` as the postdate comparison:** permanently rejects the reproduced 27.715 ms interval and any equivalent tailer/confirmation ordering.
- **Use the nudge `UserPrompt` row without a durable link:** `ReportNudgeMessageId` already gives the exact queue row; text matching a transcript prompt would add a race-prone heuristic to resolve an identity the task already stores.
- **Broaden `BlockUnmarkedWaitingAsync`:** that only converts the answered-but-refused task into a later `Blocked` state; it does not restore the correct `UnmarkedAfterNudge` settlement and is outside this causal fix.
