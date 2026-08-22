# CARD-0132 — shrink a completion note already read through status: plan

**Date:** 2026-08-22 · **Card:** CARD-0132 (`e43131a4-6e23-40b2-8a5e-21068a8e6c75`) ·
**Status:** plan (no implementation in this pass) · **Verified against:** master `093fc6a`.

---

## Verdict

This is a content-confirmed courtesy reduction, not duplicate-delivery suppression. A successful
`delegate.ps1 -Status` by the *same parent session* will display and record the immutable canonical
completion note. At the existing CARD-0132 S1.3 last-look in
`SessionMessageQueueService.DeliverNextLockedAsync`, a pending `Delegation` row is shortened only
when its exact canonical body hashes to the value that recipient previously polled. A changed,
stale, unpolled, unrecognised, or non-recipient poll leaves the full notification unchanged.

The current paths are not already byte-identical: GET returns `AgentTask.Result` untouched
(`AgentTaskService.GetAsync`), and `delegate.ps1` adds its own one-line summary; the queued text is
instead `DelegationReportFormatter.BuildCompletionNote`, whose header includes status, title, tier,
settled duration, cost, workspace note, warning, and fitted report. Hashing raw `Result`, or a
poll's occurrence, would therefore be an unsafe acknowledgement mechanism. The implementation
must make one persisted canonical completion-note body the string shown by status and enqueued for
delivery.

## Storage and ownership

Add these nullable fields to `AgentTask`:

| Field | Database type | Purpose |
|---|---|---|
| `CompletionNote` | `text` | The immutable, fully composed notification body for a settled task. It is the evidence copy and the status display source; `Result` remains the delegate's untouched original report. |
| `LastPolledResultHash` | `varchar(64)` | Lowercase SHA-256 hex digest of the exact canonical completion-note body the parent session was returned. |
| `LastPolledResultAt` | `timestamp with time zone`, nullable | UTC audit stamp for that successful content-specific poll. |

Use **`AgentTask`, not `SessionQueuedMessage`,** for the poll state. `AgentTaskReplyService`
saves settlement before its separate-scope `DeliverToParentAsync` queues the `WhenIdle` row, so a
real GET can fall in the save-to-enqueue interval. Queue-row-only state cannot record that poll.
Task-level state also survives a queue retry, cancellation, or a poll made while no queue row yet
exists. It remains correctly scoped at flush by querying the message's `task:{RootTaskId:N}`
conversation key and a task in that root whose stored poll hash matches that message body.

`CompletionNote` is deliberately task-owned rather than duplicated in an event detail: event
details have a 4,000-character ceiling while reports do not. The existing `Result` keeps the full
delegate report, `CompletionNote` keeps the exact full caller-facing presentation, and the new
timeline event below states when the queue was shortened. The short delivery must replace the queue
row body before it is marked Sent, so existing delivery confirmation compares the text actually
typed; the original remains retrievable from the task rather than disappearing with that mutation.

## Canonical composition and hash

1. At every settled delivery path in `AgentTaskReplyService` (ordinary final report, unbound-session
   recovery, and terminal failures), build `DelegationReportFormatter.BuildCompletionNote(...)` once,
   after the task's final status, completion time, merge/workspace note, and warning are known.
   Persist `note.Body` to `task.CompletionNote` in the same save that persists settlement, then pass
   that exact body to `DeliverToParentAsync`; do not recompose it later. This closes the status-poll
   gap and avoids volatile duration formatting drifting between reads.
2. Extend `AgentTaskDetailDto` with nullable `CompletionNote`. For settled tasks with one, the GET
   route returns it and `scripts/delegate.ps1 -Status` prints it instead of independently composing
   `summary` plus `result`. For legacy rows with no canonical note, retain the current script output
   and do not stamp a hash.
3. The GET route obtains the optional caller via its existing task-token resolution. `GetAsync` stamps
   only when all of these hold: the task is settled; `CompletionNote` is non-empty; and the resolved
   caller session equals `task.ParentSessionId`. A tokenless UI/read-only request or another task's
   session can still read the task, but cannot alter what the true recipient will receive. Save the
   digest and UTC time before returning the DTO.
4. Define one small pure formatter/hash helper beside `DelegationReportFormatter` (or a narrowly
   named `CompletionNotePoll` helper): normalise line endings with
   `ReplaceLineEndings("\n")`, encode UTF-8, calculate SHA-256, and render lowercase hexadecimal.
   Do **not** trim, collapse whitespace, or substitute timestamps. The canonical body has stable
   settled timestamps/duration because it was persisted at settlement; preserving every other byte
   makes equality honest. Use that same helper over `SessionQueuedMessage.Body` at flush.

The substituted body is exactly:

```text
[task {short-id} {status-word}] — full report already returned by your status poll; see above.
```

`status-word` is the same `done` / `failed` / `blocked` / `canceled` vocabulary used by
`BuildCompletionNote`; move or expose that formatter detail rather than copying another status
switch. The message retains identity, says why no report follows, and makes no claim that a bare
poll alone acknowledged anything.

## Delivery-time change

In `DeliverNextLockedAsync`, immediately after the existing CARD-0132 S1.3 Check-origin
supersession loop and before choosing `head`/forming a batch:

1. Examine each still-pending `Origin == Delegation` message independently. Parse only the existing
   `task:{root:N}` conversation-key shape; unparsable rows remain unchanged.
2. Hash its current full body with the canonical helper. Read the root's task rows for
   `LastPolledResultHash == bodyHash` (and, defensively, non-null `CompletionNote` with that same
   hash). A match is content proof; timestamps are audit-only and never a predicate.
3. On a match, replace that queued row's `Body` with the formatter's short body, add an
   `AgentTaskEventType.CompletionNoteShrunk` event on the matching task with the poll time and body
   digest, and save in the same existing pre-delivery save. The full body remains at
   `AgentTask.CompletionNote`; no event attempts to duplicate it into a capped detail field.
4. Continue the existing batching, spill, Sent stamp, transcript-baseline, and verification flow
   unchanged. Because the stored queue body now equals the short text actually typed, all retry and
   confirmation paths continue to compare the real delivery. A mismatch receives none of these
   mutations and uses today's full body.

Extend `AgentTaskEventType` with `CompletionNoteShrunk` (append-only enum value) and configure the
new `AgentTask` fields in `AppDbContext`. Generate one EF CLI migration, for example
`AddAgentTaskPolledCompletionNote`, after stopping the server as required by the repository. It
adds nullable `CompletionNote`, nullable `LastPolledResultHash` with `maxLength: 64`, and nullable
`LastPolledResultAt`; no index is needed because the flush first narrows by one root and this is not
a general lookup key. Update the EF model snapshot through the generated migration only.

On retry, clear all three fields in `AgentTaskService.RequeueAsync`: they describe the previous
settlement and must never shrink a new attempt's result. This is a fresh status snapshot, not an
acknowledgement that survives attempts.

## Tests

Add focused integration coverage using the existing real-DB queue harnesses; do not fake the
last-look outside `DeliverNextLockedAsync`.

1. **Poll then matching flush shrinks.** Seed a settled task owned by the harness parent session
   with its canonical full note and matching pending `Delegation` row; call the status service as
   that recipient, then flush. Assert the adapter receives the exact short line, the queue row is
   Sent with that short body, `CompletionNote` and `Result` still retain the full material, and a
   `CompletionNoteShrunk` event names the hash/poll.
2. **Poll then different content flushes in full.** Stamp the task through the real GET/status
   path, then change the pending row's body (including a realistic changed report portion) before
   flush. Assert the adapter gets the complete changed body, the row was not shortened, and no
   shrink event exists. This is the positive guard against "a poll happened" regressions.
3. **Never-polled remains today.** A pending matching Delegation completion row with no poll hash
   must type its full body and create no shrink event.
4. **Recipient and lifecycle guards.** A tokenless/non-parent GET must not stamp; the matching
   parent-session GET stamps the SHA-256 and UTC time; running and legacy/no-`CompletionNote` tasks
   do not stamp; retry clears the prior canonical body and poll fields.
5. **GET/status presentation.** Pin that a settled canonical note is returned by the endpoint and
   printed byte-for-byte by `delegate.ps1 -Status`; pin newline normalisation and a changed body
   produces a different hash. Keep existing raw `result` output as the legacy fallback.
6. **Settlement producer coverage.** Extend `AgentTaskReplyIntegrationTests` for normal success,
   recovery, and a terminal failure to prove each persists exactly the same canonical string it
   enqueues, including the rare warning/workspace-note shape that motivated persistence rather than
   recomposition.

Run the focused task-reply, task-service, delegate-script, and queue suites first, then the full
`tests/Antiphon.Tests` suite. The feature is server/script only: restart the Aspire AppHost after
migration; no session-runner or client rebuild is required.

## Out of scope

- Suppressing, cancelling, or treating any GET as an acknowledgement. The hash equality is the
  only shrink condition.
- Altering CARD-0132 S1 Check-origin supersession, completion-delivery retries, queue batching, or
  transcript confirmation rules.
- Backfilling historical settled tasks. They have no persisted canonical note and safely retain
  current full delivery behaviour.
