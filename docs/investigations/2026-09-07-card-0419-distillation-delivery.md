# CARD-0419: orchestrator completion delivery

Investigation completed 2026-09-07 against checkout HEAD `01542062`, including
CARD-0432 S0-S2 `131872f2`. Read the full live CARD-0419, CARD-0392 and CARD-0330
through `scripts/card.ps1 get`, the CARD-0432 evidence, orchestration-loop section
10, and the original CARD-0330 restart investigation. No runtime settings, card
state, source code or live sessions changed. No builds or tests run in this pass.

## Verdict

Apply is wired to the orchestrator's own `[task … done]` terminal delivery. It is
not merely a UI projection. The brief's established live Shadow finding explains
raw delivery without requiring either a wiring bug or a restart. However, switching
to Apply alone does not meet CARD-0419's complete acceptance: the applied pointer
is API-only, file persistence starts at a separate transport ceiling, and optional
distillation deliberately falls back to raw/excerpt delivery. CARD-0392's restart
durability defect remains in the current code.

The live Shadow/zero-Applied measurement is inherited from the supplied brief, not
remeasured here. This investigation establishes current source behavior, not that
this checkout's binaries are loaded in the running server, nor a new occurrence
of the historical restart incident.

## Actual path to the orchestrator's session

1. `server/Application/Services/AgentTaskReplyService.cs:629` stores the settled
   final response in `task.Result`; line 662 resolves `ResultFilePath`.
   `DeliverToParentAsync` (1619) requires `ReplyTo=Session` and a parent session ID.
   It builds the completion note with `DelegationReportFormatter.BuildCompletionNote`
   (formatter line 508). The formatter puts the `[task … done]` header and the
   full report or head/tail excerpt into `Note.Body`.
2. Reply service lines 1648-1661 capture mode and a single deadline, enqueue
   that exact body into the parent session with origin `Delegation`, source task
   ID, raw digest and `NoteHeader`, then enqueue a `DistillRequest` carrying the
   newly created message ID. Apply holds the note until the deadline; Shadow
   does not. `deliverIfIdle:false` allows admission before terminal confirmation.
3. `OutputDistillationHostedService.cs:46` drains the request into
   `OutputDistillationService.RequestAsync`. Service lines 95-136 read the raw
   task result, apply length/poll gates, run the existing specialist, scrub its
   result, evaluate content gates and store `DistilledResult`. Shadow exits with
   `Shadowed`; Apply calls `SessionMessageQueueService.TryApplyDistillationAsync`.
   A populated `DistilledResult` alone is not proof of applied delivery: even a
   gate-rejected result is stamped before the decision.
4. `SessionMessageQueueService.cs:1127` takes the parent's delivery lock and
   database row locks. It checks the exact note/source/raw digest, Delegation
   origin, Pending status, zero delivery attempts, no matching full-report poll,
   and the original deadline. Lines 1166-1175 replace **the queued Body** with
   `NoteHeader + distilled text + OutputDistillation.PointerLine(source)` and
   clear `HoldUntil`. Header/digest and authoritative raw result are preserved.
5. `CompletionNoteWorkHostedService.cs:19` scans persisted unattempted Pending
   completions whose holds have elapsed; four consumers call `FlushIfIdleAsync`
   (line 50). Normal queue flushing excludes active holds (queue line 1351), reads
   `head.Body` / composes a batch (1496), applies the final transport spill guard
   (1510), and passes **that body** to `DeliverAsync` (1561). It does not rebuild
   the raw task result after application.
6. `DeliverAsync` wraps multiline input using `PtyInputEncoding.WrapIfMultiline`
   and calls `_runtime.SendInputAsync(sessionId, payload)` (2362-2365), then sends
   a separate Enter (2413), and invokes transcript confirmation (2421). This is
   the parent session input path; no channel-facing reply is needed for it.

Successful eligible Apply therefore reaches the intended session. Rejection,
held/unavailable/busy specialist, timeout/expiry, already-attempted note, and other
lost application eligibility keep the original body. A matching full-report poll
has its own suppression behavior. Blocked and specialist tasks are excluded by
`ShouldRequest` (distiller service line 54); Succeeded and Failed are eligible.

## Thresholds and file behavior

These are current settings defaults, not a new live effective-configuration read.
All character comparisons use .NET string length, not a tokenizer.

| Mechanism | Default boundary | Purpose / consequence |
|---|---|---|
| Distillation input eligibility | Inclusive 1,200–20,000 characters | `DistillMinChars` / `DistillMaxRawChars`, settings lines 769/772; outside the range records SkippedShort/SkippedLong without a model turn. Admission itself is not length-gated. |
| Distilled output limits | 1,500 characters and 0.6 raw-length ratio | Settings lines 775/778; content/anchor gates also apply. Header and full-report pointer are additional text. |
| Report file backstop and excerpt | Above 3,000 characters on conservative inbox profile; above 14,400 on modern profile | `ResolveSpillFileAsync` line 2174 and formatter `FitReport` line 592. Reply service uses `PtyDeliveryProfile.Ceilings` (line 70), falling back to `ReplyInlineMaxChars`. Herdr's declared reply ceiling is also 14,400, but this reply-service property itself uses the process profile. |
| Raw excerpt | Up to roughly 1,800 head + 900 tail characters, plus banner/path | Settings lines 83/84. This is mechanical excerpting, not cheap-model summarization. |
| Incoming delegate brief spill | 900 UTF-8 bytes conservative; 43,200 modern/Herdr, with provider-specific overrides | `BriefInlineMaxBytes` / modern/Herdr settings and dispatcher `FitBriefForTyping` at 3355. `BuildBriefPointer` points the delegate to its instructions; it is not output summarization. |
| Final composed queue spill | Caller-selected UTF-8 byte ceiling | `SessionMessageQueueService.SpillQueueBodyAsync` and `TypedBodySpill.Fit` line 37. Writes `.antiphon/inbox/...` where possible and tells the recipient to read the message in full. It is a transport safeguard, not a summary. |

There is no exact 1,000-token cutoff. For illustration only, assuming four
characters per token would put 1,000 tokens near 4,000 characters: above the
1,200-character distiller minimum but well below the modern 14,400-character
report spill threshold. The conservative 3,000-character threshold is closer,
but remains a transport choice, not a token policy. Keep these concepts separate;
changing a terminal's safe envelope to express a reading preference is unnecessary.

`ResolveSpillFileAsync` returns null at or below the reply ceiling. Above it, it
uses `<task.WorkingDirectory>\.antiphon\task-<shortid>.md`, reuses an existing file
or writes the report as a backstop. I/O/access failures fall back to the task row.
`.gitignore:48` ignores `.antiphon/`. This does not guarantee a file for an eligible
4,000-character report on the modern profile, or a durable path after a transient
worktree is removed. A delegate-authored short final response pointing to a larger
file is distilled as that response; the distiller does not read the referenced file.

Most decisively, `OutputDistillation.PointerLine` (`OutputDistillation.cs:71`)
always returns `Full report: GET /api/agent-tasks/{id}` plus the delegate status
command. It never reads `ResultFilePath`. Even when a report file exists, Apply
does not explicitly append its path. In contrast, the raw excerpt formatter
prefers `ResultFilePath`, falling back to the API only when it is absent.

## Restart gap versus CARD-0432

The old defect is still present, though S0-S2 improves adjacent behavior:

- `OutputDistillationQueue.cs:18` is still an in-memory bounded Channel.
- `OutputDistillationHostedService.ExecuteAsync` starts its drain directly;
  there is no boot scan/reconciliation of lost source requests.
- The ledger insert remains in `RequestCoreAsync`'s final cleanup (service
  lines 153-205), guarded by `!ct.IsCancellationRequested`. Shutdown can lose
  an active request's ledger; queued requests also disappear. Cleanup failure
  can independently leave a ledger gap.
- The matching `LastPolledResultHash` branch at line 100 still sets
  `writeLedger=false`; CARD-0392's proposed explicit duplicate outcome is absent.
- S0-S2 adds absolute deadlines and persisted specialist-task expiry, held-model
  refusal, bounded explicit admission, and an owned parent-note flush worker.
  The worker's database scan recovers **completion delivery**, not distillation
  intent or eventual ledger/cost accounting. After a lost Apply request's finite
  hold expires, it can deliver the original raw/excerpt note.

Thus CARD-0392 is not superseded by `131872f2`. CARD-0432 evidence explicitly
defers general runtime-action isolation to S3 and eventual outcome/cost accounting
to S4. Coordinate that later work with CARD-0392 before designing duplicate
reconciliation. Do not attribute every observed raw note to restart loss:
confirmed Shadow is sufficient to explain the operator symptom.

## Next slice and acceptance evidence

Recommend Plan for CARD-0419, reusing the existing distiller and queue application:

1. Define the output usefulness cutoff independently of transport limits. If the
   operator's approximate 1,000 tokens is retained, explicitly document the chosen
   character approximation or tokenizer policy; do not silently call 1,200 chars
   1,000 tokens.
2. Ensure qualifying long reports have a gitignored full-report artifact and have
   Apply prefer its usable path, with the API as fallback. Choose storage whose
   lifetime covers worktree cleanup. Preserve header/handoff and required
   deliverable metadata when replacing the body.
3. Resolve the acceptance mismatch for rejected, unavailable, expired and >20,000
   character reports: current optional-work behavior intentionally sends raw or
   an excerpt, so “only a short summary” is not guaranteed even with Apply enabled.
4. Follow orchestration-loop section 10's Shadow evaluation before the Apply
   rollout; do not flip global mode solely because this code trace is correct.
   Keep CARD-0432 reliability work and CARD-0392 reconciliation coordinated.
5. Validate a real long delegate settlement in an explicitly scoped Apply test:
   prove the full file exists, capture the source task/note/ledger identities,
   and pull the **parent session transcript** to prove its UserPrompt contains
   the short result plus usable path and omits the raw report. Exercise fallback
   cases separately. No new live-model dispatch or production mode flip occurred
   in this investigation.

Existing tests inspected, not rerun: `OutputDistillationTests` verifies Apply
body replacement/header/digest and Shadow nonreplacement;
`OutputDistillationApplyRaceTests` tests Apply-first versus SendNow-first through
the queue with a fake adapter (verification disabled in that fixture);
`OutputDistillationProducerTests` covers settlement admission and owned flushing.
The prior CARD-0432 evidence records its own executed counts. These establish
useful regression coverage but do not substitute for CARD-0419's requested real
long-report, parent-transcript acceptance run. The card is not ready to close.
