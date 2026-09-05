# CARD-0330 — why `/api/distillations` shows zero records

*2026-09-05. All timestamps UTC unless suffixed "local" (machine is UTC+1).*

## Verdict

**The distiller fired, worked, and produced good output both times. Every attempt so far was
killed by an AppHost restart while `OutputDistillationService.RequestAsync` was inside its
synchronous wait, so no ledger row was ever written.** The gate, the queue, the hosted service,
the seat and the prompt are all fine. What is missing is restart durability:
`OutputDistillationQueue` is an in-memory `Channel` with no boot reconciliation, and the
`OutputDistillations` row is written only at the *end* of `RequestAsync` — so a process death
anywhere inside the request window loses both the pending request and the "we tried" record.

Same defect class as CARD-0331 (in-memory land queue, no boot reconciliation).

## What was ruled out

| Hypothesis | Verdict | Evidence |
|---|---|---|
| `ShouldRequest` returned false | **No** | Both tasks: `ReplyTo=Session (1)`, `Role=Code (2)` (not specialist), `Status=Succeeded (4)`, raw 3,112 / 2,886 chars — inside `DistillMinChars 1200` … `DistillMaxRawChars 20000`. `OutputDistillerEnabled` defaults `true`; no `Delegation:OutputDistiller*` override exists in `server/appsettings.json`. |
| `DistillRequest` enqueued but never drained | **No** | `OutputDistillationHostedService` is registered (`server/Program.cs:604`) and ran: it logged `Provisioned the output distiller 'antiphon-output-distiller'` from `EnsureSeatAsync` at 06:08:07, and both drains created a run task. |
| `RequestAsync` threw before `WriteLedgerAsync` | **No** | The hosted service's catch logs `Distillation of task {TaskId} … failed`; that string appears nowhere in `server/logs/antiphon-20260905.log`. No `AgentIncidents` row for the seat either, so `SpecialistRunOutcome.Timeout` was never reached. |
| Wait budget (45 s) too short for the seat | **No** | Attempt 1 delivered at 06:34:46.7 and the seat's `AssistantText` landed 06:35:00.7 — 17.6 s after the run task was created. Comfortably inside budget. |
| `BundleStamp` overflowing `varchar(80)` | **No** | Stamp is `InstructionBundles.Stamp` = `"{Key} v{Version}"`, ~19 chars. |
| Server was running pre-S3 code | **No** | Migration `20260905070000_AddOutputDistillation` was applied at 06:08:05 by the 06:07 restart; the table and the code were live for both settlements. |

## The actual sequence

Server restarts (from `server/logs/antiphon-20260905.log`, "Now listening on" — Aspire gives the
server a random port and proxies 17202, so those lines *are* the real server's starts):
06:08:07 → **06:36:00** → **07:06:45**.

### Attempt 1 — task `98141237` (CARD-0095)

| Time | Event |
|---|---|
| 06:34:11 | `Task 98141237 settled as "Succeeded" (3,112 chars)` → `DeliverToParentAsync` → `TryEnqueue(DistillRequest)` |
| 06:34:43.1 | Drain runs; distill run task `bfa50057` created (`Distill of task 98141237.`) |
| 06:34:46.7 | Delivered into the seat's live session |
| 06:35:00.7 | Seat's `AssistantText` written (transcript seq 7) — the distillation is **done**, 14 s in |
| ~06:35:05 | **Server killed for the restart** (last log line of that process: 06:35:01.3) |
| 06:36:00.8 | New server process listening |
| 06:36:01.8 | `TurnEnd` seq 8 ingested — `Timestamp=06:35:00.7`, `CreatedAt=06:36:01.8`, a **61 s ingestion lag** that dates the outage exactly |
| 06:36:01.9 | Run task settles `Succeeded`, 1,471 chars — with nobody waiting on it |

`RequestAsync` died between the seat answering and the poll loop observing it. No
`StampDistilledAsync`, no `WriteLedgerAsync`.

### Attempt 2 — task `0f3de8e9` (CARD-0329)

| Time | Event |
|---|---|
| 07:05:12.9 | Task settles `Succeeded` (2,886 chars) → distill request enqueued |
| 07:05:47.6 | `apphost.restart.lock` stamped — restart begins |
| 07:05:48.4 | Distill run task `d61c65dd` created |
| ~07:05:50 | **Server killed**, ~2 s into the 45 s wait |
| 07:06:45 | New server; the in-memory `DistillRequest` is gone |
| 07:06:49.9 | New server's dispatcher picks up the orphaned `Queued` run task and delivers it |
| 07:07:05.9 | Run settles `Succeeded`, 1,519 chars — nobody is waiting; no ledger row |

Both distillations exist and are usable (`AgentTasks.Result` on `bfa50057` / `d61c65dd`); they
were simply discarded. Mode is `Shadow`, so no `HoldUntil` was set and the raw completion notes
went out unaffected — nothing was lost from the parent's point of view.

## Secondary finding: not every path writes a ledger row

`WriteLedgerAsync` is *not* reached on every code path in `RequestAsync`
(`server/Application/Services/OutputDistillationService.cs`):

- disabled / source row missing / ineligible (specialist, unsettled, `ReplyTo != Session`) — returns silently (deliberate);
- **duplicate digest** (`source.LastPolledResultHash == digest`) — returns silently, *not* deliberate-looking;
- and there is no row at all for "requested but the process died", which is exactly this incident.

So "zero rows" cannot currently distinguish *never asked*, *asked and skipped as duplicate*, and
*asked and interrupted*.

## Not a bug: the stray `land this` in the seat's composer

The seat's live buffer showed `land this` sitting above `[Pasted text #2 +58 lines]`. It is
**unsubmitted composer residue, not a delivery corruption**:

- the seat's transcript has exactly three `UserPrompt` entries — the boot prompt, `bfa50057`,
  `d61c65dd` — and both task prompts begin cleanly with `[antiphon-task:…]`; `land this` was
  never submitted and was never prepended to a prompt;
- `SessionQueuedMessages` for that session holds exactly those two rows, `DeliveryAttempts=1`
  each, neither containing the text;
- the string appears in no other session's transcript around that window (the only hits are this
  investigation's own tool output);
- a similar stray (`zz3c3809e8`) is visible in the same buffer at boot, and neither string exists
  anywhere in the repo.

Conclusion: stray keystrokes reaching the seat's pty from outside the server (operator terminal /
pane focus), harmless here. Unrelated to CARD-0330.

## Recommended next slice (not applied)

Not fixed in this pass — the fix is a real slice, not a safe one-liner, and it spends money.

1. **Boot reconciliation** in `OutputDistillationHostedService.ExecuteAsync`, before the drain
   loop: re-enqueue `DistillRequest`s for tasks that are `Succeeded`/`Failed`, non-specialist,
   `ReplyTo=Session`, settled within a short window, and have no `OutputDistillations` row. Must
   pass `queuedMessageId: null` when the note has already been sent, and must be bounded (a count
   cap and a time window) so a long outage cannot fan out into a spend spike.
2. **Write the ledger row up front** with a `Requested`/`Interrupted` outcome and update it in
   place, so an interrupted attempt is visible instead of invisible. Needs a
   `DistillationOutcome` value and a migration.
3. Give the duplicate-digest early return its own outcome (`SkippedDuplicate`) rather than a
   silent return.

Nothing here blocks CARD-0330's landed slices; the pipeline is correct end to end. It only needs
to survive a restart, which on this machine happens several times an hour while cards are landing.
