# CARD-0250 — A follow-up attachment after channel-reply settlement must still reach the human

- **Date:** 2026-08-30
- **Status:** Plan (verified against master `73d62bf`)
- **Card:** CARD-0250 (bug, InProgress) — the 2026-08-30 PredictionMarkets live miss: Slack user
  asked for a PDF, the PDF was written, the delegate's report carried a correct `[[attach:]]`
  marker, and Slack never received the file.
- **Related:** CARD-0067 (durable correlations / no silent loss), CARD-0233 (turn ownership),
  CARD-0245 idea 6 (leftover-correlation matching — overlap resolved in §9), CARD-0059 (generated
  workspace `CLAUDE.md`).

## 1. Verdict up front

1. **The card's mechanism is confirmed — and the exact incident path is even quieter than the card
   says.** When the only correlation was already settled by the ack turn, the `[task done]` turn
   hits the `open.Count == 0` early return (`ChannelReplyDispatcher.cs:184-185`) — a **plain
   `return` with no log line at all**. The "matched NONE … stays owed" Warning the card cites
   (`:268-273`) fires only when *some other* correlation happens to still be owed (the CARD-0245
   seq-33 shape). Both paths drop the attachments; neither extracts them (`PrepareReplyBody`
   at `:339` runs only after a successful match).
2. **Chosen fix: option B — a follow-up `ChannelReply` for machine-triggered turns that carry
   attachments** (§4), gated to turns whose owning prompt is one of Antiphon's own injections
   (Delegation / Check / System queued rows), with the injection's own `SessionQueuedMessage.ChannelReplySettledAt`
   reused as the claim-before-produce idempotency marker — the exact CARD-0067 shape, **zero
   migrations**. Options A and C are rejected with reasons (§3).
3. **New incident `ChannelAttachmentsDropped = 40`** when a channel-bound session's completed turn
   produced attach markers that neither the main path nor the follow-up path delivered (§5).
4. **MIME:** add `.html`/`.htm → text/html` for correctness; do **not** hard-block HTML; the
   "always PDF for Slack/Telegram" rule is an instruction fix, not a code refusal (§6).
5. **All three instruction sources get corrected text** (§7), and the generated workspace
   `CLAUDE.md` gains a channel-bound section (§8) — with the honest caveat that the never-clobber
   rule means it reaches only marker-managed or absent files.
6. **CARD-0245 idea 6 does not collide** — it modifies match/settle of *Channel-origin* rows inside
   `DispatchAsync`; this card adds an additive branch after them and touches no Channel-row
   settlement. This card's build should land first; idea 6 rebases trivially (§9).

## 2. Verified current-code facts (all line refs at `73d62bf`)

- `ChannelReplyDispatcher.OnTurnEndAsync` (`server/Application/Services/ChannelReplyDispatcher.cs:135`)
  runs `DispatchAsync` then, only if the in-memory `_dispatched` watermark survives,
  `DispatchFollowUpAsync` — which delivers **trailing text of the already-answered turn only**
  (window `PromptSeq < seq < nextPromptSeq`, CARD-0068). The `[task done]` UserPrompt is the next
  turn-opening prompt, so it *caps* that window; the answer turn's text is beyond it, and the
  watermark is dropped once the window drains (`:785-786`). The existing "follow-up" cannot carry
  a later turn.
- Settlement: `ChannelReplySettledAt` is claimed before produce (`:323`), un-claimed on produce
  failure (`:369-372`). `OpenCorrelations` (`:124-129`) filters `Origin == Channel` — Delegation
  rows never appear as correlations, so their `ChannelReplySettledAt` column is unused today.
- `PrepareReplyBody` / attach extraction: `:655-705`. `InferMime` `:716-733` — no `.html`, falls to
  `application/octet-stream`. `InferAttachmentKind` `:707-714` — `.html` is `AttachmentKind.File`
  (fine). `MaxAttachmentBytes` = 14 MB cumulative (`ChannelBridgeSettings.cs:32`).
- Completion notes are queued with `QueuedMessageOrigin.Delegation` and
  `CorrelationKey = $"task:{RootTaskId:N}"` (`AgentTaskDispatcher.cs:1337`, `:731`, `:1654`;
  `AgentTaskReplyService.cs:1246`); check-ins are `Origin = Check (= 4)`; system notes
  `System (= 2)` (`QueuedMessageOrigin.cs`). The note body head is
  `[task <8hex> done|failed|blocked|canceled] …` (`DelegationReportFormatter.BuildCompletionNote`,
  `DelegationReportFormatter.cs:291-324`).
- A delegate's `[[attach:]]` reaches the orchestrator **inside the queued prompt** (the completion
  note), i.e. as a `UserPrompt` transcript record — `ExtractAttachments` reads AssistantText only,
  so the marker delivers nothing unless the orchestrator re-emits it in its own reply text.
- Instruction sources, verbatim today:
  - `server/Bundles/orchestrator.md:11-12` — "Do not poll and do not wait — end your turn; the
    report will reach you." True inside Antiphon; silently settles the chat correlation.
  - `DelegationReportFormatter.ReportingContract` (`:228-252`) — "Your final message is the entire
    report the caller receives. Nothing else from this session is forwarded." No word about
    `[[attach:]]`.
  - `ChannelPreamble.BuildPreset` (`ChannelPreamble.cs:74`) — "The final text of each of your turns
    is delivered back to the originating chat" — **false**: only the turn matching the inbound
    prompt is delivered.
  - `AgentWorkspaceProvisioner.RenderBody` (`AgentWorkspaceProvisioner.cs:193-247`) — no channel
    section at all; `Render(agent, directory)` currently has no knowledge of channel bindings.
- Incident plumbing to copy: `ReportLostAsync` (`:503`) — own scope, agent resolved via
  `Agents.PersistentSessionId` then channel-catalog fallback, `AgentSupervisorService.RecordIncidentAsync`.
  Next free `AgentIncidentKind` value is **40**.

## 3. The three candidate designs, evaluated

**A — keep the correlation open while Dispatched/Working child tasks exist.** Rejected. Either the
ack turn is published without settling — then every re-trigger of the same turn (late
AssistantText, closing TurnEnd, backfilled boundary on reconnect) re-matches and **re-answers into
a live chat**, which is precisely the duplicate CARD-0067 built claim-before-produce to prevent —
or the ack is withheld until the child settles, and the human stares at silence for the length of
a build. It also entangles the dispatcher with `AgentTasks` liveness and forces the TTL sweep to
learn "owed but legitimately slow", multiplying states. The never-weaken rule for this file is that
nothing may make "answered" ambiguous; A makes it structurally ambiguous.

**C — `[task … done]` injections do not own the channel-reply turn.** Rejected as the primary fix.
In the incident the channel correlation was **already settled** by the ack turn — redirecting
ownership finds nothing left to match, so C alone fixes nothing unless settlement is also re-opened
(option A's problems again). Worse, loose attribution is exactly what CARD-0233 removed: an
orchestrator ends `[task done]` turns constantly, and attributing them to the newest owed channel
prompt would publish interim task chatter as answers and falsely settle correlations. The narrow
true case C covers (a still-owed correlation plus a task-done turn) is already correctly handled:
the correlation stays owed and the TTL sweep's `TurnUnmatched` classification reports it.

**B — follow-up `ChannelReply` for a machine-triggered turn that carries attachments.** Chosen.
It leaves every settlement/matching invariant untouched, is durable (no dependence on the
in-memory `_dispatched` map), fires only on the explicit signal that the agent wanted the chat to
receive a file (`[[attach:]]` in its own AssistantText), and directly delivers the incident's exact
shape. Its one gap — a *text-only* promise on a later turn still isn't delivered — is deliberate:
text follow-ups with no marker have no machine-readable "this is for the chat" signal, and
publishing every machine-triggered turn's text would spam the conversation. The corrected
instructions (§7) close that gap from the agent's side ("re-emit `[[attach:]]`; plain text
follow-ups are not delivered").

## 4. Design: `DispatchMachineTurnAttachmentsAsync`

New private method on `ChannelReplyDispatcher`, called from `OnTurnEndAsync` after the existing two
dispatch calls (so the main path always wins first claim on the turn):

**Gates, in cheapness order:**

1. **Channel-bound session:** at least one `Origin == Channel` row with a non-null
   `ConversationKey` exists for this session (one indexed query — the only cost added to every
   non-channel session's turn end). The **newest** such row's `ConversationKey` is the follow-up
   target ("the last known conversation"), resolved to provider/handle exactly as `:284-304` does.
2. **Turn not already published:** the turn's owning prompt (`TranscriptTurnWindow.FindOwningPromptAsync`
   over the latest TurnEnd, same as the main path) matched no Channel correlation — implied when
   the main path settled nothing for this turn; implemented by simply checking the owning prompt is
   not a Channel-origin row match. (When the main path DID publish the turn, its attachments went
   with it; nothing to do.)
3. **Machine-triggered turn:** the owning prompt's text matches (same `PromptsMatch` containment,
   same `Normalize`) a queued row on this session with `Origin ∈ {Delegation, Check, System}` and
   `Status == Sent` and `ChannelReplySettledAt == null`. This is the anti-stray-reply gate: an
   operator typing into the bound terminal matches nothing and never triggers a send — the same
   safety property prompt-matching gives the main path. Check (4) is deliberately included: a
   check-in prompt is a legitimate moment for the orchestrator to attach a now-ready file.
4. **The turn carries attachments:** `ExtractTurnResponseAsync` over the owning prompt; bail on
   `containsApiErrorStub` (CARD-0071's rule, unchanged); `ChannelContracts.ExtractAttachments`
   finds ≥ 1 path. A response whose remaining text is `NO_REPLY` still sends — with empty text —
   because the marker is the explicit ask; text otherwise rides along through `PrepareReplyBody` +
   `Truncate` + `ClassifyKind` exactly like the main path.

**Idempotency — the load-bearing decision:** before producing, stamp `ChannelReplySettledAt` on the
matched machine-origin queued row (claim-before-produce); un-stamp it if the produce throws.
`OpenCorrelations` filters `Origin == Channel`, so this reuse is invisible to correlation logic,
costs **no migration and no new column**, survives restarts, and makes the constant re-triggers of
the same turn (late AssistantText, closing TurnEnd, reconnect backfill) natural no-ops. A first
trigger that sees text without markers does **not** claim — so a marker landing in a later
transcript batch of the same turn still sends on the next trigger (the CARD-0068 lesson applied
here from the start).

**What it must never do:** settle, un-settle, or match any `Origin == Channel` row; touch
`PromptsMatch`/`Normalize`; publish a turn whose owning prompt matched nothing machine-origin
(that stays a drop + incident, §5). Never-weaken: no change makes an actually-unanswered Channel
correlation easier to mark settled.

**Interaction with the incident sequence, replayed under the fix:** ack turn publishes + settles
(unchanged) → `[task f7a4165f done]` note delivered (Delegation row, Sent, unclaimed) → orchestrator
turn re-emits `[[attach: …pdf]]` → main dispatch: no open correlations, returns → follow-up
attachments path: channel-bound ✓, owning prompt matches the Delegation row ✓, markers ✓ → claim
the Delegation row → `ChannelReply` with the PDF bytes to `mikeysbot-slack:D0B1VUH2EAK…`. If the
orchestrator does **not** re-emit the marker, §5's incident fires instead of silence — and §7's
instructions are what teach it to re-emit.

## 5. Incident: `ChannelAttachmentsDropped = 40`

Raised when a **channel-bound** session's completed turn produced attach markers that were neither
published on the main path nor deliverable on the follow-up path. Reasons and severities:

- **Owning prompt is not machine-origin and matched no correlation** (operator typed a marker into
  a bound terminal, or an unmatched stray): **Warning**. Detection only — publishing here would be
  the stray-reply bug the matching design exists to prevent.
- **Machine-triggered turn, but no `ConversationKey` resolvable** (no Channel row ever, key
  unsplittable, catalog empty *and* conversation id unusable): **Critical** — the agent explicitly
  tried to send a file and there is a human on the other end of *some* conversation who will not
  get it.

Non-channel-bound sessions never reach this code (gate 1), so a delegate putting `[[attach:]]` in
its own report raises nothing — correct, since its marker is input for the caller, not a send.
Plumbing copies `ReportLostAsync` (`:503`): own scope, owner via `PersistentSessionId` with the
channel-catalog fallback, `RecordIncidentAsync`, `failureReason` naming the branch. Dedupe per
(session, owning-prompt sequence) so transcript re-triggers of one turn raise once. XML-doc the
enum member in the established style, citing this card and the 2026-08-30 live miss.

## 6. MIME / HTML policy

Add `".html" or ".htm" => "text/html"` to `InferMime`. Do **not** add a PDF-only refusal:
Slack renders HTML as a text snippet regardless of MIME (the earlier message in the same thread
proved it with `application/octet-stream`), so the MIME entry is a correctness fix, not a UX fix —
and a hard block would refuse a user who genuinely asked for the `.html` file. The UX fix is
instructional (§7/§8): Slack/Telegram document delivery should be a PDF; HTML's snippet rendering
is documented as intentional behaviour, never called "delivered as a document". Add one line to
`docs/messaging/slack-api-file-upload-brief.md` noting the snippet behaviour.

## 7. Corrected instruction text (three sources)

**`server/Bundles/orchestrator.md`** — append after the reports paragraph (`:11-17`):

> If you are channel-bound (Slack/Telegram), the chat does NOT see every turn. Only the turn that
> answers the inbound chat message is delivered — ending your turn settles that conversation. One
> exception: a later turn of yours that was triggered by an Antiphon note (`[task … done]`, a
> check-in) and puts `[[attach: <absolute path>]]` on its own line is delivered to your most recent
> conversation as a follow-up, files and text. So when a human asks for a document that a delegate
> is still producing: say so in the reply that settles the chat, and when the `[task … done]` note
> arrives, re-emit `[[attach:]]` yourself in that turn — a delegate's own `[[attach:]]` reaches
> only you, as text, never the chat. Plain-text follow-ups without a marker are not delivered.
> Prefer PDF for Slack/Telegram documents; Slack renders HTML as a text snippet.

**Delegate brief** — one sentence added to `DelegationReportFormatter.ReportingContract`, in the
first paragraph after "cannot see your screen":

> If a file is your deliverable, give its absolute path; an `[[attach:]]` marker here reaches only
> your caller as text — it is never sent to any chat.

Kept to one line deliberately: every delegate pays for this text, and the false belief it corrects
("my marker delivers the file") is exactly one sentence wide.

**`ChannelPreamble.cs`** — replace the `:74` sentence in `BuildPreset` with:

> Your reply to each chat message — the final text of the turn that answers it — is delivered back
> to the originating chat, truncated at 4000 characters. A turn started by anything else (a system
> note, a task report, someone typing in your terminal) is not delivered — except that a turn
> triggered by an Antiphon note which puts `[[attach:]]` on its own line is sent to your most
> recent conversation as a follow-up.

…and extend the attach paragraph (`:76`) with: "Prefer PDF for documents — Slack shows HTML files
as a text snippet, not a document." The preamble's "exact text is a compatibility contract"
comment means `ChannelPreamblePresetEndpointTests` (and any fakeclaude scenario quoting it) must be
updated in the same slice — grep for the quoted sentence before assuming the test list is complete.

**Wording rule for all three:** the text must describe what the code does *after* §4 ships, so the
instruction slice lands in the same build as (or after) the dispatcher slice, never before.

## 8. Generated workspace `CLAUDE.md` (CARD-0059)

`AgentWorkspaceProvisioner.Render`/`RenderBody` gain a `boundChannels` parameter (same
`IReadOnlyList<(string Provider, string Title)>` shape as `ChannelPreamble.Render`), populated by
the two call sites (`AgentService.CreateAsync`, `AgentControlService.StartAsync` — both have db
access to `ChatChannels.AgentId`). When non-empty, `RenderBody` appends a section:

> ## You are channel-bound (slack "…")
> To send a file to the chat, put `[[attach: <absolute path>]]` on its own line in the turn that
> answers the chat — or in a turn triggered by an Antiphon note (`[task … done]`, a check-in).
> Up to 14 MB per turn. Always attach a PDF for documents: Slack renders HTML as a text snippet,
> local paths mean nothing to the chat, and a chat user cannot see later file edits. A delegate's
> own `[[attach:]]` is not delivered to the chat — re-emit it yourself.

Because bindings change without relaunch, the content hash changes at the next launch (Start is the
reconcile point — existing design). **Honest limitation, stated here so nobody expects more:** the
never-clobber rule means this reaches only absent or marker-managed files. The PredictionMarkets
orchestrator's workspace already carries its own `CLAUDE.md` → `LeftAlone`; for such agents the
preamble (§7) is the delivery vehicle, and the operator can delete/adopt the file if they want the
generated floor.

## 9. CARD-0245 idea-6 overlap — who touches the shared matching logic

Idea 6 (seq-33 vs 38): after an outage, a turn answers the *new* prompt while an *older* owed row
stays unmatched, later firing a false `ChannelReplyLost`. Its fix will modify how `DispatchAsync`
(and/or `ClassifyTtlLossAsync`) treats **leftover Channel-origin rows** — settle-or-rematch.

This card touches none of that: no change to `PromptsMatch`, `OpenCorrelations`, Channel-row
settlement, or the TTL classifier. It adds (a) a new method called from `OnTurnEndAsync`, (b) a
MIME entry, (c) an enum value, (d) text. Same file, disjoint regions. **Order: build CARD-0250
first** — it is additive and pins new tests around the existing matching behaviour, which idea 6's
future build then changes *under* passing follow-up tests, catching any interaction. The one real
interaction to note in idea 6's plan: if idea 6 settles a leftover row via a machine-turn's
follow-up having "answered enough", it must not double-publish — the claim marker in §4 gives it a
durable fact to consult.

## 10. Test plan (seams verified)

All in `tests/Antiphon.Tests` via `BridgeQueueHarness` (`SeedChannelCorrelationAsync`,
`InsertTurnAsync`, `BindChannelAsync`, `h.Messaging.SentReplies`, `Restarted(h)` per
`ChannelReplyDurabilityTests.cs:48`); run with
`dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0250/ --treenode-filter "/*/Antiphon.Tests.Application/*/*"`.

New class `ChannelFollowUpAttachmentTests` (`[Category("Integration")]`, `[NotInParallel("MessageQueue")]`):

1. **The incident shape, red-first:** seed correlation + ack turn → dispatch (settles, 1 reply);
   insert a Delegation-origin Sent row `[task ab12cd34 done] …` + its `UserPrompt` + a TurnEnd turn
   whose AssistantText carries `[[attach: <temp .pdf written by the test>]]` → `OnTurnEndAsync` →
   a **second** reply to the same conversation whose `Attachments` single item carries the file
   bytes and `application/pdf`. Must FAIL against current code before the fix commit.
2. **Idempotent + restart-safe:** after (1), `OnTurnEndAsync` twice more and once via
   `Restarted(h)` → still exactly 2 replies; the Delegation row's `ChannelReplySettledAt` is set.
3. **Produce failure un-claims:** failing producer → row unclaimed → next trigger sends once.
4. **Operator turn never sends:** unmatched human-shaped prompt + attach markers → 0 new replies,
   `ChannelAttachmentsDropped` incident recorded at Warning (via the `AgentSupervisionTests`-style
   incident query).
5. **Machine turn with no route:** no Channel row ever… (gate 1 short-circuits — assert nothing
   raised for a plain delegate session) and, separately, channel-bound with an unsplittable stored
   key → Critical incident.
6. **NO_REPLY-plus-marker sends files with empty text; API-error stub in the window sends nothing.**
7. **MIME:** the delivered attachment for a `.html` file carries `text/html` (through the reply,
   since `InferMime` is private) — and the plan's documented-snippet stance lives in this test's
   comment, so the behaviour is never called "document delivery".
8. **Text pins:** `InstructionBundleTests` for the orchestrator.md addition;
   `ChannelPreamblePresetEndpointTests` for the corrected preamble sentence (update quoted text);
   `AgentWorkspaceProvisionerTests` for the channel section present when bound, absent when not,
   and `LeftAlone` still untouched; `DelegationUnitTests` (holds the `ReportingContract` pins) for
   the one-line brief addition.
9. **Never-weaken regression:** existing `ChannelReplyDurabilityTests` and `ChannelBridgeTests`
   must pass unchanged — no test asserting Channel-row settlement may need edits; if one does, the
   design was violated.

## 11. Files to change

| File | Change |
|---|---|
| `server/Application/Services/ChannelReplyDispatcher.cs` | `DispatchMachineTurnAttachmentsAsync` + call from `OnTurnEndAsync`; `.html`/`.htm` in `InferMime` |
| `server/Domain/Enums/AgentIncidentKind.cs` | `ChannelAttachmentsDropped = 40` (int on existing column, no migration) |
| `server/Application/Services/ChannelPreamble.cs` | corrected delivery sentence + PDF-preference line |
| `server/Bundles/orchestrator.md` | channel-bound paragraph (§7) |
| `server/Application/Services/DelegationReportFormatter.cs` | one-line attach caveat in `ReportingContract` |
| `server/Application/Services/AgentWorkspaceProvisioner.cs` | `boundChannels` param + channel section |
| `server/Application/Services/AgentService.cs`, `AgentControlService.cs` | pass bindings to `Provision` |
| `docs/messaging/slack-api-file-upload-brief.md` | HTML-snippet note |
| `docs/telegram.md` | mirror the delivery-model correction if it quotes the preamble |
| tests per §10 | new class + 4 updated pinned-text suites |

## 12. Out of scope

- Text-only follow-up delivery (no marker, no signal — instructions cover it).
- A generic `POST /channels/{id}/send` megaphone (rejected in CARD-0171; unchanged).
- CARD-0245 ideas 1-5 and 7 (outage detection — different failure), and idea 6's build (§9).
- KB→bundle sync ("always give me pdf" living only in KB) — worth its own small card if wanted.
- Enforcing PDF-only attachments in code (§6 rejects it).
