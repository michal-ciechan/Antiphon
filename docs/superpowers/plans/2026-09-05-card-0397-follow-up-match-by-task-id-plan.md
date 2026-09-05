# CARD-0397 — plan: match machine-turn follow-ups by task id, not 120-char body containment

**Plan pass, 2026-09-05.** Sources verified at `3c41c26c`: `ChannelReplyDispatcher` (`DispatchMachineTurnFollowUpAsync`, `PromptsMatch`, `Normalize`, `ReportAttachmentsDroppedAsync`), `ChannelContracts`, `SessionQueuedMessage.SourceTaskId` / `NoteHeader`, `AgentTaskReplyService.DeliverToParentAsync`, `AgentTaskDispatcher` completion/fail enqueue sites, `AgentTaskCheckService` (`BuildNote`, `HeaderPrefix`, `ConversationKey`), `ScheduleService.BuildPromptBody`, `ChannelPreamble` system bodies, `DelegationReportFormatter.BuildCompletionNote` / `Short` / `TaskMarker`, `PromptSubmissionMatch` (CARD-0080 whitespace-free arm), `AgentIncidentKind.ChannelAttachmentsDropped`, `ChannelFollowUpAttachmentTests`, `ChannelMachineTurnTextTests`, CARD-0250 / CARD-0338 / CARD-0337 plans, `docs/session-runtime-invariants.md` Gotcha #86, `docs/telegram.md` "What the chat sees". No production code is changed by this plan.

**Card:** CARD-0397 (bug, Backlog, High/Normal) — live production regression after CARD-0250 / CARD-0338 / issues #21 / #30, both closed `status:done`. Channel-bound always-on orchestrator received `[task … done]` notes, wrote follow-up turns with whole-line `[[attach: …pdf]]`, chat got silence; `ChannelAttachmentsDropped` Warning `UnmatchedHuman`.

**Related:** CARD-0250 (machine-turn attachments), CARD-0338 (plain-text follow-ups), CARD-0337 (implied bundle), CARD-0233 (owning-prompt window), CARD-0080 / CARD-0024 (Grok newline-join on delivery confirm), CARD-0067 (no silent loss).

---

## 1. Verdict up front

1. **The card's mechanism is confirmed against HEAD, with one naming correction.** `PromptsMatch` is a **private static** on `ChannelReplyDispatcher` (`:1497-1505`), not `ChannelContracts`. It takes the first 120 chars of the *queued* body after `Normalize` (CRLF → `\n` + trim — **newlines are kept**) and asks whether that probe is a contiguous substring of the transcript `UserPrompt`. Live Grok/PtyHost `UserPrompt` text drops those newlines with no separator (CARD-0080: `"line one\nline two"` records as `"line oneline two"`). A completion note is `header\n\nbody`; when the header is shorter than 120 chars the probe includes the `\n\n`, containment fails, candidates.Count == 0, attach markers → `ReportAttachmentsDroppedAsync(..., Warning, "UnmatchedHuman")`. The card's `git=landedWrote developer.` exhibit is exactly that join.

2. **Chosen fix: identity-first matching for machine-turn follow-ups only (D2–D5).** Parse `[task <8-hex>]` / `[check <8-hex>]` out of the owning prompt; match Delegation rows by `SourceTaskId` (already populated on every parent-facing completion path) and Check rows by `SourceTaskId` or `ConversationKey` (`check:{guid:N}`). Keep a containment fallback that probes **only the first line of the queued Body** (the stable header), never the report body. Do **not** change the global `PromptsMatch` used by Channel-origin dispatch and the TTL sweep.

3. **Gate 2 is a second silent-drop path, confirmed.** `:1158` returns with no log and no incident when *any* historical Channel body on the session is 120-char-contained in the owning prompt. A short Channel body (`"done"`, `"landed"`, a fragment that happens to appear in the note) false-positives an injection turn into "main path already owns this". Injection-shaped prompts skip that return (D9).

4. **Severity split (D7):** a prompt that looks like an Antiphon injection and matches no row, when attach markers exist, is `AlertSeverity.Error` / branch `UnmatchedInjection` — broken delivery, not "human typed in the terminal." Operator-typed turns stay Warning `UnmatchedHuman`. Unroutable stays Critical. Text-only injection-shaped miss: `LogWarning`, no new incident kind (D8); the id-match is what delivers CARD-0338 status text.

5. **`SourceTaskId` is reliable for Delegation completion notes, absent on Check/Scheduled/System, and unused on briefs typed into the delegate.** New Check enqueues get `SourceTaskId = task.Id` (one argument). No row backfill. Older Check rows match via `ConversationKey` then header-line fallback (D5).

6. **Test-design is folded (D1).** The card already names the pin test; the matcher is one function; a separate TestDesign dispatch would re-derive §8. `next: code`.

---

## 2. Ground truth — what the card assumes vs what HEAD does

Line refs at `3c41c26c`.

| # | Card / brief assumes | What the code does |
|---|---|---|
| G1 | `ChannelContracts.PromptsMatch` is a 120-char containment check | **`ChannelReplyDispatcher.PromptsMatch` is private static `:1497-1505`.** `ChannelContracts` has `IsNoReply` / `ExtractAttachments` only. Five call sites, all in the dispatcher: DispatchAsync Channel match `:316`; TTL `ClassifyTtlLossAsync` `:711`; Gate 2 `:1158`; machine candidates `:1171`; definition `:1497`. |
| G2 | `Normalize` is enough that a flattened transcript should still match | `Normalize` (`:1507-1508`) is `ReplaceLineEndings("\n").Trim()` — **keeps newlines**. Flattened `UserPrompt` does not contain `\n`, so a probe that includes `\n\n` fails. |
| G3 | Today's tests stay green because they seed the same string as Body and `UserPrompt` | Confirmed. `ChannelFollowUpAttachmentTests` / `ChannelMachineTurnTextTests` `SeedMachineInjectionAsync` + `InsertTurnAsync(note, …)` use the identical `note`. Header < 120 chars with no newline in the first 120 → PromptsMatch succeeds. The live miss is the newline-in-probe case, which **no test covers**. |
| G4 | `SourceTaskId` is already on the queued Delegation row | **Yes for parent-facing completion notes.** `AgentTaskReplyService` `:1648-1651` (`sourceTaskId: task.Id`, `noteHeader: note.Header`); watchdog / `FailAndNotifyAsync` / failure reminders / `AgentTaskService` direct insert `:2471`; waiting notes `:2638`; unlanded-sibling warnings `:2766`. **No** on briefs (`AgentTaskDispatcher` `:2987`, `:3135`, `:4173`), refinements (`:519`), closing-line nudges (`:2552`), blocked-answer bodies (`:336`) — those are typed into the *delegate*, not the parent follow-up path. |
| G5 | `SourceTaskId` is reliable for every dispatch path that this matcher sees | **Check: not set.** `AgentTaskCheckService` `:172-174` enqueues `Origin=Check`, `ConversationKey=check:{taskId:N}` (`:247`), no `sourceTaskId`. **Scheduled: not set** (`ScheduleService` `:300-308`, has `sourceScheduleId` + `noteHeader`). **System: not set** (bootstrap / resume / recovery / policy notes). |
| G6 | Matching the full note by containment is why Grok flattening breaks delivery | Confirmed for **short headers**. `BuildCompletionNote` (`DelegationReportFormatter.cs:510-559`) is `{header}\n\n{body}`. Header is `[task {Short} {status}] {bits joined by · }` including `git=…`. When that header is < 120 chars, the probe crosses `\n\n` into the report. Live exhibit `git=landedWrote developer.` is header-tail glued to body-head. When the header itself is ≥ 120 chars, today's PromptsMatch would still succeed (probe stays on one line) — so this is not "every Grok follow-up", it is "every follow-up whose header is shorter than the probe window". |
| G7 | Gate 2 silent-returns on a Channel-body PromptsMatch with no incident | Confirmed `:1152-1159`: loads **all** Channel bodies for the session (not only open / this-turn), `Any(PromptsMatch)` → `return`. No log, no incident, no candidate matching. |
| G8 | A `[task ` / `[check ` / `[antiphon-` prompt classified `UnmatchedHuman` is a mis-label | Confirmed. `ReportAttachmentsDroppedAsync` `:1346-1348` / `:1402-1404` hard-codes the UnmatchedHuman sentence for every non-`Unroutable` branch. Operator-typed test (`ChannelFollowUpAttachmentTests` `:165-197`) is the only intended Warning path; the live miss reused it. |
| G9 | CARD-0338 already delivers plain text once a machine row matches | Confirmed. After a match, `AdmitsMachineTurnText` (`:1269-1275`) + attach/implied-bundle branch (`:1189-1198`) is unchanged. Fixing the match restores both PDFs (CARD-0250) and status text (CARD-0338) for the live shape. |
| G10 | Delivery confirmation already knows about Grok newline-join | Confirmed, **different layer.** `PromptSubmissionMatch.IsConfirmedBy` / `IsCompleteIn` (CARD-0080 S2 / CARD-0024) has a whitespace-free arm so the *queue* can stamp `Sent`. The channel dispatcher never calls it. A note can be delivery-confirmed (whitespace-free) and still fail follow-up matching (newline-preserving 120-char probe). That is this card. |

---

## 3. Decisions

### D1 — Test-design is folded into this plan (`next: code`)

The card already names the pin (injection `\n\n` vs flattened `UserPrompt`). The change is one matcher plus Gate 2 / incident branch. A separate TestDesign dispatch would re-derive §8. Verification design is in this document so Build can execute it.

**Rejected:** `next: test-design` as the medium-card default. Latency on a live channel-delivery regression is the cost; the section below is complete enough for Code.

### D2 — Do not change global `PromptsMatch` / `Normalize`

Those four Channel-facing call sites (DispatchAsync, TTL classify, Gate 2, definition) are CARD-0233 / CARD-0067 load-bearing. Channel envelopes are single-line (`[Telegram "Family" — Mike 14:32] …`); they do not need a whitespace-free arm. Widening containment to ignore newlines on Channel bodies would let a short Channel fragment match a glued injection prompt even more easily (the Gate 2 false-positive, worse).

**Rejected alternatives:**

- **A — make `PromptsMatch` whitespace-free (reuse `PromptSubmissionMatch`).** Fixes the live exhibit in one line, still probes the *report body*, still false-matches on Gate 2, still classifies a `[task` miss as UnmatchedHuman, blast radius on TTL `TurnUnmatched` vs `StaleTtl`. The queue's matcher answers "did we type this body?"; this path answers "which injection row owns this turn?"
- **B — strip newlines in `Normalize`.** Same Channel-path blast radius. A Channel batched body (`BatchContextMarker` + newlines + `BatchCurrentMarker`) would change match behaviour on the main path.

### D3 — Primary match is the 8-hex id in the header grammar

Parse from the owning prompt (whole text, so batched notes and Grok-joined notes both work):

- `[task <8-hex>` → task-id set (status word is `done|failed|blocked|canceled|waiting` or any `\b` after the hex; waiting notes are parent-facing Delegation rows with `SourceTaskId`)
- `[check <8-hex>` → check-id set

A candidate matches when:

- `Origin == Delegation` and `SourceTaskId` is set and `Short(SourceTaskId)` is in the task-id set
- `Origin == Check` and `Short(SourceTaskId ?? parsed ConversationKey guid)` is in the check-id set

`Short` is `id.ToString("N")[..8]` (`DelegationReportFormatter.cs:19`) — the same 8 chars the notes already print. 8-hex collision inside one session's unclaimed injection rows is treated as "match both"; that is the batched-same-prefix case, astronomically rare across different tasks.

Do **not** treat `[antiphon-task:xxxxxxxx]` as a SourceTaskId match. Briefs on the delegate session usually have `SourceTaskId == null`; header-line fallback covers them if they ever appear as a channel-bound owning prompt (today's PromptsMatch already would, when the first 120 chars survive). Expanding id-match to briefs would not help the live miss and would couple follow-up send to work-turn briefs.

### D4 — Containment fallback probes only the first line of `Body`

`HeaderProbe(row)` = first line of `Normalize(row.Body)` (split on `\n`, skip empty). `NoteHeader` is **not** the primary probe: Scheduled `NoteHeader` is `"Scheduled · {name}"`, which is **not** a substring of the typed banner `[scheduled: {name} · …]`. Delegation `NoteHeader` equals that first line anyway.

Then existing `PromptsMatch(Normalize(headerLine), normalizedTurn)` — first 120 of a **single line**, so Grok's newline-join cannot break it. Never probe the report body, `--- deliverable ---` block, or excerpt banner.

**Rejected:** whitespace-free containment of the full Body as a third arm. The card forbids matching on the report body; distillation / excerpting / nested `[task` quotes in a report would false-claim.

### D5 — Check `SourceTaskId` going forward; no backfill

New Check enqueues pass `task.Id` into `EnqueueAsync`'s `sourceTaskId` (currently omitted at `AgentTaskCheckService.cs:172-174`). Older Check rows: `TryParseCheckConversationKey` then header-line fallback. No SQL backfill, no migration.

Scheduled / System stay header-line-only (no task id in the row that belongs on `SourceTaskId`).

### D6 — Injection-shaped prompt is broader than the card's three prefixes

`ChannelContracts.IsAntiphonInjectionPrompt` is true when the owning prompt contains any of:

| Token | Why |
|---|---|
| `[task ` | completion / waiting notes |
| `[check ` | check notes (`HeaderPrefix`) |
| `[antiphon-` | briefs / report tokens if they ever own a channel-bound turn |
| `[scheduled:` | `ScheduleService.BuildPromptBody` banner (`:488-492`) — card omitted this; without it a flattened scheduled prompt with attach markers is still UnmatchedHuman |
| `[System note from Antiphon:` | resume / recovery / policy System bodies |
| `[session ` | `ChannelPreamble.WithSessionTag` prefix on bootstrap |

Contains, not only StartsWith: a SUPERSEDED check note is `SUPERSEDED …\n\n[check …]` and Grok-joins those onto one line, so StartsWith `"SUPERSEDED"` would miss. Channel envelopes are `[Telegram` / `[Slack` / `[Discord` — they do not collide.

### D7 — Error, not Warning, not Critical, for "looks like ours, matched nothing, had attach markers"

| Branch | Severity | When |
|---|---|---|
| `UnmatchedHuman` | Warning | not injection-shaped, attach markers, no machine row (operator / stray) — unchanged |
| **`UnmatchedInjection`** | **Error** | injection-shaped, attach markers, no machine row — **new** |
| `Unroutable` | Critical | conversation key unsplittable — unchanged |

Error (not Critical): the conversation is routable; the matcher failed. Critical stays "we cannot even name a chat". Error appears on the attention feed; it does **not** go through `IncidentPageNotifier` (that pages `ChannelReplyLost` Critical only). The chat user already missed the file; paging the operator's digest as if a correlation was abandoned would duplicate CARD-0338's pager for a different kind.

Update `AgentIncidentKind.ChannelAttachmentsDropped` xml-doc (currently Warning-or-Critical only) and the canned `ReportAttachmentsDroppedAsync` sentence so UnmatchedInjection does not claim "was not an Antiphon note".

Dedupe key remains `{branch}:{promptSeq}`, so a re-trigger of the same turn does not raise twice; a mis-labelled UnmatchedHuman from before this ship is a different key and is not rewritten.

### D8 — Text-only injection-shaped miss: log, no new incident kind

`ChannelAttachmentsDropped` is the attach-marker kind (CARD-0250). A `[task done]` turn with no markers and no match is the CARD-0338 silent drop. Id-match + header fallback is what stops that for the live shape. Residual text-only miss: `LogWarning` naming the prompt seq and "injection-shaped, no row" — not a new `AgentIncidentKind`, not a lie that attachments dropped.

**Rejected:** raise `ChannelAttachmentsDropped` without markers. The kind's meaning would rot. A `ChannelFollowUpDropped` kind is a follow-up card if the residual shows up in production after S1.

### D9 — Gate 2 does not silent-return on an injection-shaped owning prompt

```
if (!ChannelContracts.IsAntiphonInjectionPrompt(promptText)
    && channelBodies.Any(b => PromptsMatch(Normalize(b), normalizedTurn)))
    return;
```

Main-path Channel turns still skip the follow-up (their owning prompt is `[Telegram …]`, not injection-shaped). The rare collision — a human pastes `[task ab12cd34 done]` into Slack **and** that task still has an unclaimed Delegation row — can double-send; accepted residual, documented. Do not try to detect "DispatchAsync just settled this turn" via `ChannelReplySettledAt == now` (clock races, re-triggers).

### D10 — Never-weaken list (CARD-0250 §4 / CARD-0338 §4.1, still binding)

- Do not settle, un-settle, or match any `Origin == Channel` row from this path.
- Do not change `PromptsMatch` / `Normalize`.
- Do not add `Ui` / `Supervision` to machine candidates.
- Do not publish a turn whose owning prompt matched nothing machine-origin.
- `ChannelBridgeTests` / `ChannelReplyDurabilityTests` stay green unedited; if one needs editing, the design was violated.

### D11 — Helpers live on `ChannelContracts`; dispatcher stays the only caller of row matching

Pure functions (`IsAntiphonInjectionPrompt`, `CollectInjectionShortIds`, `HeaderProbe`) go on `ChannelContracts` (already `partial`, already the frozen channel-contract home, unit-testable without the harness). The row-vs-prompt loop stays in `DispatchMachineTurnFollowUpAsync` so OpenCorrelations / origin filters / claim-before-produce do not leak into Contracts.

---

## 4. Design

### 4.1 Match algorithm (replaces `:1171` only)

After Gate 2 (as amended by D9), with `candidates` still `Sent` + unclaimed + origins Delegation/Check/System/Scheduled:

```
ids = ChannelContracts.CollectInjectionShortIds(promptText)
matches = candidates.Where(row =>
    MatchesByTaskId(row, ids) || PromptsMatch(HeaderProbe(row), normalizedTurn)
).ToList()
```

`MatchesByTaskId`:

- Delegation + `SourceTaskId` → `Short` in `ids.TaskIds`
- Check → `Short(SourceTaskId ?? parse(ConversationKey))` in `ids.CheckIds`
- else false (Scheduled / System / Delegation-without-SourceTaskId fall through to header probe)

Empty matches: existing attach-marker incident path, with D7 branch/severity; D8 log when no markers.

A batched same-root completion prompt that contains two `[task …]` headers matches **both** constituent rows (today's PromptsMatch property, preserved via the id set). Header probe is containment in the whole turn, so row 2's first line still hits a batched body.

### 4.2 Incident path (`ReportAttachmentsDroppedAsync`)

Add `"UnmatchedInjection"` to the `why` / `incidentMessage` switch. Severity is chosen by the caller (`Error` vs `Warning` vs `Critical`), already a parameter — Gate 1's miss site (`:1172-1179`) picks:

```
var injection = ChannelContracts.IsAntiphonInjectionPrompt(promptText);
if (explicitPaths.Count == 0)
{
    if (injection)
        _logger.LogWarning(... injection-shaped, no row, no markers ...);
    return;
}
await ReportAttachmentsDroppedAsync(..., injection ? Error : Warning,
    injection ? "UnmatchedInjection" : "UnmatchedHuman", ct);
```

Gate 2 no longer returns before this, so an injection-shaped Gate-2-false-positive reaches the same miss treatment as Gate 1 (card point 5).

### 4.3 Check enqueue (D5)

`AgentTaskCheckService` `:172-174` gains `task.Id` as `sourceTaskId`. `ConversationKey` unchanged. Implied-bundle collection (`CollectImpliedAttachmentsAsync` `:1291`) stays Delegation-only; Check `SourceTaskId` is identity for matching, not a new attach source.

### 4.4 What this does to the 2026-09-05 incident

Ack turn publishes + settles (unchanged). `[task <id> done]` lands in the parent transcript with newlines joined. Owning prompt still starts with `[task `. Gate 2: injection-shaped → do not return. Candidates: Delegation row with `SourceTaskId` = that task → `Short` hits the parsed id → match. Attach markers (and/or implied bundle) → claim → `ChannelReply` to the last Channel conversation. No `UnmatchedHuman`. Re-triggers see `ChannelReplySettledAt` and stop.

If matching still failed (row missing, parked, already claimed): Error `UnmatchedInjection` instead of Warning `UnmatchedHuman`.

---

## 5. Rejected alternatives (summary)

| Alt | Why not |
|---|---|
| Whitespace-free / newline-stripping `PromptsMatch` | D2. Fixes the exhibit, not Gate 2, not UnmatchedHuman, Channel-path blast radius. |
| Reuse `PromptSubmissionMatch.IsConfirmedBy` | Different question (typed-body identity, 200-char head, weak arm for `"Continue."`). Would still bind to report body. |
| Match only `NoteHeader` | Scheduled `NoteHeader` is not in the typed body. First line of `Body` is the typed header for every origin that matters. |
| Require `SourceTaskId` on every candidate and skip rows without it | Drops live Check / Scheduled / System / older Delegation seeds in tests. Header fallback is the compatibility arm. |
| Backfill `SourceTaskId` on existing Check rows | ConversationKey already carries the guid. A migration for a matcher fallback is cost without benefit. |
| New incident kind for text-only misses | D8. Kind-rot vs a follow-up card if needed. |
| Critical for UnmatchedInjection | Conversation is routable; matcher failed. Critical is Unroutable / ChannelReplyLost. |
| Skip Gate 2 entirely | Would follow-up a main-path Channel turn (duplicate into chat). Injection-shaped skip is the narrow cut. |

---

## 6. Slices

Sequential; S1 is the production fix. Build to `--property:OutputPath=bin-c0397/` (forward slash); delete every `bin-c0397` directory afterwards.

### S1 — Matcher, Gate 2, incident split, Check `SourceTaskId` (~3–4 h)

**Files:**

- `server/Application/Services/ChannelContracts.cs` — `IsAntiphonInjectionPrompt`, `CollectInjectionShortIds` (task vs check sets), `HeaderProbe`
- `server/Application/Services/ChannelReplyDispatcher.cs` — candidate match `:1171`; Gate 2 `:1158`; miss site `:1172-1179`; `ReportAttachmentsDroppedAsync` why/message for `UnmatchedInjection`
- `server/Domain/Enums/AgentIncidentKind.cs` — xml-doc on `ChannelAttachmentsDropped` (Error branch)
- `server/Application/Services/AgentTaskCheckService.cs` — `sourceTaskId: task.Id` on enqueue

**Tests:**

- New unit `[Category=Unit]` class `ChannelMachineTurnMatchTests` (or extend `ChannelContractsTests`) for helpers: flatten shape, batched two ids, SUPERSEDED+join, scheduled banner, first-line probe vs body, Check ConversationKey without SourceTaskId
- `ChannelFollowUpAttachmentTests` — the card's pin (red-first): Body has `\n\n` after header, `UserPrompt` is the same text with newlines stripped, `SourceTaskId` set to a guid whose `Short` is in the header → one follow-up with the PDF; today's PromptsMatch would miss
- Same class — Gate 2 false-positive: a historical Channel body `"done"` (contained in `[task … done] …`) plus the flatten shape still sends; without D9 this returns silent
- Same class — injection-shaped miss with markers, no row → Error `UnmatchedInjection:`; operator-typed `"run the tests please"` stays Warning `UnmatchedHuman:` (existing test unedited)
- `ChannelMachineTurnTextTests` — the flatten shape **without** attach markers still delivers plain text (CARD-0338 restored under Grok join)
- Existing `ChannelFollowUpAttachmentTests` / `ChannelMachineTurnTextTests` / `ChannelReplyDurabilityTests` / `ChannelBridgeTests` stay green (identical-string seeds pass via header probe even when `SourceTaskId` is null)
- One Check-enqueue assertion that the new row has `SourceTaskId == task.Id` (extend the existing sweep/interpreter test that already reads the queued Check row — do not add a harness-only duplicate)

**Not in S1:** docs. `PromptsMatch` body. Main-path Channel matching. Implied-bundle logic. Origins dial.

### S2 — Docs (~30 min)

**Files:**

- `docs/session-runtime-invariants.md` Gotcha #86 — one paragraph: follow-up match is task-id + header-line, not 120-char body; Grok newline-join is why; Gate 2 does not silent-return on injection-shaped prompts; UnmatchedInjection is Error
- `docs/telegram.md` "What the chat sees" — no contract change (the chat still sees the `[task done]` follow-up); optional one sentence that match is by task id so a flattened Grok transcript still delivers
- `AgentIncidentKind` xml-doc is in S1 (code)

---

## 7. Out of scope

- Changing delivery confirmation (`PromptSubmissionMatch`, queue `Sent` stamping). Already whitespace-free.
- Making Grok/PtyHost preserve newlines in `UserPrompt`. The TUI join is measured (CARD-0080); we match the record we get.
- Backfilling historical Check `SourceTaskId` or historical `ChannelAttachmentsDropped` UnmatchedHuman rows.
- A new incident kind for text-only follow-up misses (D8).
- Paging Error `ChannelAttachmentsDropped` through `IncidentPageNotifier`.
- Matching `[antiphon-task:]` briefs by SourceTaskId (D3).
- Main-path Channel `PromptsMatch` (D2, D10).
- Resume / Supervision-origin routing to the chat (CARD-0360/0361 named that as a different card).

---

## 8. Verification design

### Proves it works now

- V-1: Flattened `[task done]` + attach markers delivers the file to the settled conversation · integration · `ChannelFollowUpAttachmentTests` new pin · `SentReplies.Count == 2`, attachment name/bytes match, Delegation `ChannelReplySettledAt` set, Channel row untouched
- V-2: Flattened `[task done]` + plain text (no markers) delivers the status text · integration · `ChannelMachineTurnTextTests` new pin · second reply text equals the assistant status; no attachments
- V-3: Gate 2 Channel-body substring (`"done"`) does not silent-drop an injection-shaped turn · integration · `ChannelFollowUpAttachmentTests` · follow-up still sent
- V-4: Injection-shaped miss with markers is Error `UnmatchedInjection` · integration · `ChannelFollowUpAttachmentTests` · no send; `Severity == Error`; `FailureReason` starts `UnmatchedInjection:`; operator-typed marker turn stays Warning `UnmatchedHuman:`
- V-5: Helpers: newline-in-probe fails today's containment and passes id/header match; batched two `[task` ids; SUPERSEDED+joined `[check`; `[scheduled:` banner; Check id via ConversationKey when `SourceTaskId` is null · unit · `ChannelMachineTurnMatchTests` / `ChannelContractsTests`
- V-6: New Check enqueue stamps `SourceTaskId` · integration · existing Check queue assertion extended
- V-7: Existing attach/text/NO_REPLY/idempotency/System-marker-only/operator-plain-text tests stay green unedited · integration · `ChannelFollowUpAttachmentTests`, `ChannelMachineTurnTextTests`, `ChannelReplyDurabilityTests`, `ChannelBridgeTests`

### Guards the regression

- R-1: Reverting candidate match to `PromptsMatch(Normalize(m.Body), normalizedTurn)` · caught by V-1 / V-2 because the flatten `UserPrompt` does not contain the `\n\n` probe
- R-2: Restoring Gate 2's unconditional `Any(PromptsMatch)` return · caught by V-3
- R-3: Classifying every attach miss as UnmatchedHuman Warning · caught by V-4
- R-4: Probing the report body again (second line / deliverable block) · caught by the unit test that a body-only substring is **not** a match when header and SourceTaskId disagree
- R-5: Omitting `sourceTaskId` on Check enqueue · caught by V-6

### Positive controls  (Build runs each: break, see red, revert, see green — and reports all three)

- PC-1: In `DispatchMachineTurnFollowUpAsync`, match candidates with `PromptsMatch(Normalize(m.Body), normalizedTurn)` only; expect V-1 red
- PC-2: Restore Gate 2 as `if (channelBodies.Any(...)) return;` with no injection-shaped guard; expect V-3 red
- PC-3: Report the flatten+markers miss as `Warning, "UnmatchedHuman"`; expect V-4 red (severity/branch)

### Out of scope

- E2E against a live Grok TUI (CARD-0080 already measured the join; V-1 seeds the recorded shape)
- Re-running the full `Antiphon.Tests` assembly as the local verify loop (Gotcha #18 / #74: class filters)
- `ChannelReplyDurabilityTests` edits (D10: if they go red, the design was violated)
- Pager / `IncidentPageNotifier` behaviour for the new Error (D7: not paged)

### Cost

- suites forced: `tests/Antiphon.Tests` with `--treenode-filter` on `ChannelMachineTurnMatchTests` (or the Contracts unit tests), `ChannelFollowUpAttachmentTests`, `ChannelMachineTurnTextTests`, plus the one Check-enqueue test name; after green, `ChannelReplyDurabilityTests` and `ChannelBridgeTests` as the never-weaken pair
- verification floor ≈ 4 min (the two channel classes are `[NotInParallel("MessageQueue")]`; unit helpers are seconds)
- `-ExpectAbout` for Code: floor + authoring ≈ **12 min** (S1) then S2 docs; whole card ≈ **4 h** authoring + 4 min verify per slice, round to **5 h** if the flatten pin fights the harness

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0397/ -- --treenode-filter "/*/*/ChannelFollowUpAttachmentTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0397/ -- --treenode-filter "/*/*/ChannelMachineTurnTextTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0397/ -- --treenode-filter "/*/*/ChannelMachineTurnMatchTests/*"
```

Forward slash on OutputPath. Delete `bin-c0397` directories after.

---

## 9. Defaults the orchestrator can take without asking

If Code hits a fork the plan already closed:

- Keep `PromptsMatch` 120-char and newline-preserving on the Channel path (D2).
- Error not Critical for UnmatchedInjection (D7).
- No new incident kind for text-only (D8).
- No Check-row SQL backfill (D5).
- Do not match `[antiphon-task:]` by SourceTaskId (D3).

---

## 10. Files (checklist)

| File | Slice | Change |
|---|---|---|
| `server/Application/Services/ChannelContracts.cs` | S1 | injection-shaped + id parse + header probe |
| `server/Application/Services/ChannelReplyDispatcher.cs` | S1 | match loop, Gate 2 guard, miss branch, incident copy |
| `server/Domain/Enums/AgentIncidentKind.cs` | S1 | xml-doc |
| `server/Application/Services/AgentTaskCheckService.cs` | S1 | Check `SourceTaskId` |
| `tests/Antiphon.Tests/Application/ChannelMachineTurnMatchTests.cs` (new) or `ChannelContractsTests.cs` | S1 | unit pins |
| `tests/Antiphon.Tests/Application/ChannelFollowUpAttachmentTests.cs` | S1 | flatten pin, Gate 2 pin, UnmatchedInjection pin |
| `tests/Antiphon.Tests/Application/ChannelMachineTurnTextTests.cs` | S1 | flatten text pin |
| Check enqueue test (existing class) | S1 | `SourceTaskId` assertion |
| `docs/session-runtime-invariants.md` | S2 | Gotcha #86 |
| `docs/telegram.md` | S2 | optional one sentence |

No migration. No client change. No settings change.
