# CARD-0123 — Surface decision-needed cards where they can't be missed

**Date:** 2026-08-27
**Status:** planned (design only — nothing here is implemented)
**Card:** CARD-0123 (`e8b1f5ea-2a87-4e5a-900f-475e43286a89`), board Antiphon
**Builds on:** CARD-0122 (`ef91470`, **shipped**: `CardStatus.NeedsDecision`, the "Needs decision"
column on every board, the reason gate, `AttentionKind.CardNeedsDecision`) and CARD-0036
(shipped 2026-08-26: the away digest, the Blocked-task ping, `ChatChannel.DigestEnabled`).
**Model followed:** `docs/superpowers/plans/2026-08-26-card-0036-away-digest-plan.md` — what exists
verified against the code first, then the design, then what it costs its neighbours.

## Verdict, in one screen

The card names three candidate surfaces and asks them to be weighed. Weighed against what the
code already does today (§1), the answer is:

| Candidate | Finding | Do it? |
|---|---|---|
| **Count/badge** mirroring the `AgentRail` "Review" treatment | A badge already exists (`NeedsAttentionBadge`, home header) and already counts decision cards — but it counts **36 open rows today**, 34 of them parked messages (§1.3), so it is permanently red and a decision is invisible inside it. | **Yes, as a separate chip.** A `DecisionsBadge` that counts `CardNeedsDecision` rows only, renders nothing at zero, sits in the **app header on every page** (not just home), and links to the decisions surface. Same Mantine `Badge variant="light"` idiom as the rail's `Review`, in the `danger` colour + `TbHelpCircle` icon CARD-0122 already assigned to the kind. |
| **Dedicated cross-board list** | `AttentionPanel`'s "Needs you now" group is already cross-board and already lists them — with the question clamped to two lines, no board name, no way to *decide* without leaving. | **Yes, as an Orchestrator tab.** `?tab=decisions`: every `CardNeedsDecision` row from the existing `/api/attention` feed, grouped by board, the whole question visible, and a **Decide…** verb that records the decision as the move-out reason (CARD-0122's own model). Client-only over data the server already sends; no new endpoint. |
| **Push / notification** | CARD-0036 built the exact machinery — a once-per-block Telegram ping plus a twice-daily digest, on a per-channel opt-in — and it covers **Blocked tasks only**. `ChannelAlertRouter` is the wrong path for the same reasons 0036 §1.4 and CARD-0171 gave. Live, nothing is switched on: `Digest:Enabled=false`, no channel has `DigestEnabled`, and the operator DM channel is disabled. | **Yes, by extension not invention.** `DecisionCardNotifier` beside `BlockedTaskNotifier` (one loud ping per parking, idempotent on a new `Card.DecisionNotifiedAt`), and a `❓ Decisions` section in every digest while any is still waiting. Inert until the operator flips the two switches 0036 already defined. |

Two defects found on the way, both fixed in slice 1 because the acceptance scenario hits them:
**a card reopened straight into Needs decision is invisible to the feed** (the builder filters
`Kind == Move`; reopen writes `Kind == Reopen` — §1.5), and **a card move does not invalidate the
attention query**, so the badge lags a move by up to 15 s (§1.4).

What the CARD-0010 acceptance case actually looks like today: it was **decided and closed on
2026-08-21**, twelve days after filing, with the three answers in its `TerminalReason` — the exact
gap the card describes, resolved by the human noticing rather than by anything surfacing it. §5
uses it as the live verification round trip.

---

## 1. What exists today (verified against the code and the live stack, 2026-08-27)

### 1.1 The signal is real, queryable, and already rendered in four places

- **Producer.** `AttentionService.BuildCardNeedsDecisionAsync`
  (`server/Application/Services/AttentionService.cs:233-261`): every card with
  `Status == NeedsDecision && ArchivedAt == null`, one row each, `Severity = Critical`,
  `Title = "{Identifier} — {Title}"`, `Headline = "Needs a decision — nobody can move this but you."`,
  `Evidence` = the `Reason` of the latest `CardRevision{Kind: Move, ToStatus: NeedsDecision}`,
  `SinceUtc` = that revision's `CreatedAt`, `Actions = [OpenCard]`, `CardId`/`BoardId` set.
  Wired at `:142`, sorted with everything else severity-desc then oldest-first (`:164-170`), so a
  decision card is always in the top band of `GET /api/attention`.
- **Client model.** `client/src/api/attention.ts:55` (`'CardNeedsDecision'`), `:78-79`
  (`cardId`/`boardId`); `client/src/features/attention/attentionVisuals.ts:114-119` (label
  "Needs decision", `danger`, `TbHelpCircle`); `groupOf` puts Critical in `'now'` (`:166-175`);
  `targetOf` sends a card row to `/boards/{boardId}?card={cardId}` (`:189-195`).
- **Four surfaces render it with no CardNeedsDecision-specific code, because each filters by
  severity or by "not RecentFailure":**

  | Surface | Where | What a decision card gets |
  |---|---|---|
  | Desktop home header badge | `client/src/features/home/HomePage.tsx:344-365` (`NeedsAttentionBadge`, mounted `:174`) | counted in `Needs attention (n)` → `/orchestrator?tab=attention` |
  | Orchestrator tab badge | `client/src/features/orchestrator/OrchestratorPage.tsx:27`, `:45-58` | counted in the red circle on the "Needs attention" tab |
  | `AttentionPanel` | `client/src/features/attention/AttentionPanel.tsx:69-195`; row `:214-300`; `OpenCard` → navigate `:396-398` | a row in **Needs you now**, evidence `lineClamp`ed, "Open card" verb |
  | Mobile home band 1 | `client/src/features/home/MobileHomePage.tsx:62-71` (Critical/Error), `NeedsYouRow` `:205-300` | a tappable card in "Needs you · n", evidence clamped to 2 lines, tap → board |

  Pinned today by `AttentionServiceTests.A_needs_decision_card_is_a_critical_row_whose_evidence_is_the_move_reason`
  (`tests/Antiphon.Tests/Application/AttentionServiceTests.cs:45`) and, on the client, only
  transitively (`attentionVisuals.test.ts` checks the visual map is total).

### 1.2 The card itself: the question is recorded, but not shown where the card opens

- The move-in requires a reason (`CardService.MoveAsync`, `server/Application/Services/CardService.cs:284`,
  422 `"A move into Needs decision must say what decision is needed."`); the client asks for it with
  "What decision is needed?" in both move dialogs (`client/src/features/board/MoveMenu.tsx:54-`,
  `client/src/features/thread/CardThreadPanel.tsx:340-`). A move **out** needs no reason
  (`CardCorrectionIntegrationTests.A_move_into_needs_decision_records_the_reason_and_a_move_out_needs_no_reason`, `:234`).
- The reason lives **only** on the `CardRevision` (`server/Domain/Entities/CardRevision.cs:72`).
  `CardDto` (`server/Application/Dtos/BoardDtos.cs:40-70`) has no field for it, `CardThreadDto`
  (`CardThreadDtos.cs:28-36`) carries `TerminalReason` but not a move reason, and `BoardCard.tsx`
  renders priority and labels only (`:51-53`). On the board the column node wears a
  `needs a human` badge (`StateNode.tsx:66`) and `CardModal` shows the state label (`:119`) — the
  question is reachable through the lazy **History** tab (`CardModal.tsx:193`, `CardHistory`), and
  nowhere else on the card. So "Open card" from the attention row lands the human on a card that
  does not say what it is waiting for.

### 1.3 The existing badge is drowned — measured

`GET /api/attention` on the live stack at 2026-08-27T01:36Z: **41 rows** — 34 `ParkedMessage`
(Error), 5 `RecentFailure`, 0 Critical. Every count in §1.1 excludes only `RecentFailure`, so the
home header reads `Needs attention (36)` on a day with nothing a human needs to decide. That is
CARD-0091's pile (parked messages with no path to a terminal state), and it is why "the badge
already counts them" is true and useless: a one-row change in a 36 is not a signal. Fixing the pile
is CARD-0091; this card must not depend on it, so the decision count has to be its own chip (§2.1).

### 1.4 Liveness: a move is not an attention invalidation

`useAttention` polls every 15 s (`client/src/api/attention.ts:113-119`) and otherwise relies on
SignalR. `useSignalRInvalidation.ts` invalidates `['attention']` on `AgentChanged` (`:91-92`) and
`AgentTaskChanged` (`:132-134`) — **not on `CardChanged`** (`:68-76`: boards + thread only). A
card parked from the board therefore reaches the badge and the panel on the next poll, not the
next paint. `CardService` publishes `CardChanged` on every move and reopen (`:321`, `:482`), so
the fix is one client-side key (§2.4); no new event, honouring CARD-0035 §D5.

### 1.5 Reopen into Needs decision is invisible to the feed

`CardStateMachine` has `[CardStatus.Done] = []` (`server/Domain/StateMachine/CardStateMachine.cs`):
the only way out of Done is `ReopenAsync` (`CardService.cs:447`), which accepts a target column
(`ResolveReopenTarget`, `:485-512`; default = the Backlog column) and always requires a reason. A
reopen **into the Needs decision column** is therefore legal, reason-bearing, and lands the card in
`Status == NeedsDecision` — but it records `CardRevisionLog.AppendReopen` with
`Kind = CardRevisionKind.Reopen, ToStatus = NeedsDecision` (`CardRevisionLog.cs:81-88`), and the
builder (§1.1) filters `r.Kind == CardRevisionKind.Move`. Result: the card sits in the column with
the `needs a human` badge, `IsSpawnable` correctly refuses it, and `GET /api/attention` never lists
it. CARD-0010, the acceptance case, is Done — this is precisely the path it must take (§5).

### 1.6 Push: the machinery exists (CARD-0036), covers tasks only, and is switched off live

- **Ping.** `BlockedTaskNotifier.SweepAsync` (`server/Application/Services/BlockedTaskNotifier.cs`):
  for each `DigestEnabled` channel, each `Blocked` task whose latest `Blocked|Conflicted` event has
  no later `HumanNotified` event (`AgentTaskEventType.HumanNotified = 17`, `AgentTaskEnums.cs:139`)
  gets `AwayDigestFormatter.FormatPing(item)` sent loud via `ChatChannelService.SendAsync`, then the
  event is written — send-then-record, so a failed send retries next tick. Gated by
  `DigestSettings.WakeOnBlocked`. Runs from `AwayDigestHostedService` every `SweepSeconds`
  (`server/Infrastructure/Supervision/AwayDigestHostedService.cs:26-27`), which returns at once
  when `Digest:Enabled` is false (`:17`).
- **Digest.** `AwayDigestProjection.ComputeAsync` takes `NeedsYou` from the attention feed filtered
  to `Kind == BlockedQuestion` (`AwayDigestProjection.cs:36-39`) — a decision card is not in it.
  `AwayDigestFormatter.FormatDigest` (`:13-38`) renders sections in urgency order, five rows then
  `+ N more`, `MaxChars = 3500`, optional `PublicBaseUrl` footer.
- **Transport.** `ChatChannelService.SendAsync(id, text, ChannelSendOptions?, ct)`
  (`ChatChannelService.cs:95`, options `:187`: `Silent`, `ReplyToMessageId`) — provider-agnostic, so
  a Slack channel with `DigestEnabled` would get the same message.
- **The other outbound path is wrong for this, again.** `ChannelAlertRouter.RouteAsync`
  (`ChannelAlertRouter.cs:37-61`) fans a persisted `Alert` to every channel whose
  `AlertMinSeverity` admits it, throttled and grouped into an `"Antiphon alerts:"` digest by
  `AlertDigestFlusher`. A decision is not an incident; a sink chosen by severity alone would also
  receive every quota and stall alert; the grouping loses the one-message-per-question shape; and
  a card in Needs decision is a *state*, not an event to raise once. 0036 §1.4 and CARD-0171
  (`TrackerSyncNotifier.cs:15-18`) rejected this route for the same reasons. `docs/telegram.md` has
  no alert or digest section at all (its headings are formatting, inbound, settings, tests).
- **Live state.** `server/appsettings.json:147-150` → `Digest.Enabled = false`.
  `GET /api/channels`: no channel has `digestEnabled`; the operator DM (`Mike`, telegram,
  `2d2f6c45-0189-406f-9af7-24c9d0540b32`) is `enabled = false`; `Family` and `AZ Care` are live and
  agent-bound. So today **no push reaches anyone for anything**, Blocked tasks included. That is an
  operator switch, not a build item (§6 decision 3).

### 1.7 CARD-0010, the acceptance case, as it actually is

- Two cards carry the identifier: Antiphon's (`83588c84-1430-4588-bd16-75dfaa3d3816`) and one on
  the **Gym Stat** board — so `scripts/card.ps1 get CARD-0010` answers 409 `matches more than one
  card`; every command in §5 uses the guid.
- The Antiphon card is **Done**, `completedAt 2026-08-21T18:01:38Z`, `terminalReason` beginning
  `Decided (2026-08-21, user direct, all three): …` — the three questions were answered twelve
  days after filing (2026-08-09), by the operator reading the card, not by anything putting it in
  front of them. Its labels already include `decision`.
- Not archived, so reopen is allowed (`CardService.cs:458-462`).

### 1.8 What does not exist (the build list, before design)

No decision-only count anywhere; no surface that shows the whole question with the board it
belongs to; no verb that records a decision from the list; no callout on the card itself; no
attention invalidation on a card move; no listing of reopen-into-decision; no ping or digest line
for a parked decision; no idempotency store for such a ping (cards have no event table — the
revision kinds are ContentEdit/Move/Archive/Unarchive/Reopen and are user-facing history, not a
place for a "notified" marker).

---

## 2. Design

### 2.1 The chip: decisions only, everywhere, nothing at zero

`DecisionsBadge` (`client/src/features/attention/DecisionsBadge.tsx`) — `useAttention()` filtered
to `kind === 'CardNeedsDecision'`; **returns `null` at zero** (the `NeedsAttentionBadge` rule at
`HomePage.tsx:336-339`: a permanent chip is a control nobody sees after a week); otherwise
`<Badge size="sm" variant="light" color="danger" leftSection={<TbHelpCircle/>}>Decisions (n)</Badge>`
wrapped in a `Link` to `/orchestrator?tab=decisions`.

**Placement: the app header, beside `NavLinks`** (`client/src/shared/Layout.tsx:87-118`), so it is
on every desktop route — the board the operator is looking at, the agents page, settings — not
only home. The `Review` badge the card cites (`AgentActivityBadge.tsx`, `AgentRail.tsx:140-145`) is
the visual idiom: `Badge variant="light"`, one word, a colour that means one thing. `NeedsAttentionBadge`
on the home header stays exactly as it is; the two chips sit side by side there and say different
things (`Needs attention (36)` = the diagnostic list; `Decisions (1)` = you, now).

On the phone (`< 48em`) the header collapses into the drawer; band 1 of the mobile home already
carries the row (§1.1) and needs no change.

### 2.2 The list: an Orchestrator tab, client-only over the existing feed

`OrchestratorPage.tsx` grows a fourth tab, `decisions` (`TABS` at `:9`), with its own badge counting
`CardNeedsDecision` rows only (the `attention` tab badge keeps its "everything open" count — two
badges, two meanings, matching §2.1). `DecisionsPanel` (`client/src/features/attention/DecisionsPanel.tsx`):

- **Rows** = `useAttention().data.items.filter(kind === 'CardNeedsDecision')`, in server order
  (oldest waiting first). No new endpoint: the brief's constraint — surface the feed, do not
  invent a second signal — and CARD-0035's non-widening rule (`attention.ts:9-13`).
- **Grouped by board**, board name and project name from `useBoards()` (`client/src/api/boards.ts:403`),
  joined on `item.boardId`. A row whose board is not in the list (deleted between polls) shows under
  "Unknown board" rather than vanishing.
- **Row content:** identifier + title (from `item.title`), age (`ageSeconds`, `formatDuration`),
  and the **whole question** — `item.evidence` in `whiteSpace: pre-wrap`, **no `lineClamp`**. The
  question is the row; clamping it is what the attention panel already does.
- **Verbs:** `Decide…` and `Open card`. `Open card` is `targetOf(item)`. `Decide…` opens the
  existing `MoveMenu` for that card (it already loads the board's columns and knows the
  NeedsDecision rules) with the reason field **required and labelled "Your decision"** and the
  target defaulting to Backlog. The decision is thereby recorded where CARD-0122 put the
  question: as the move revision's `Reason`, readable in History and in `card.ps1 history`. Nothing
  new is persisted for it.
- **Empty state** one line: "No decisions waiting." — the `NothingIsStuck` shape, shorter.

Why a tab and not a section at the top of `AttentionPanel`: the panel's job is triage across
fourteen conditions and it clamps evidence for that reason; a decision needs the full question, a
board name, and a verb that closes the loop. A separate URL is also what the ping links to (§2.3).
`AttentionPanel` keeps its `CardNeedsDecision` rows unchanged — same data, two altitudes, no
duplication of state.

### 2.3 The push: extend CARD-0036, do not touch the alert path

**Ping — `DecisionCardNotifier.SweepAsync`** (`server/Application/Services/DecisionCardNotifier.cs`,
modelled line-for-line on `BlockedTaskNotifier`): if `DigestSettings.WakeOnDecision` (new, default
`true`) and at least one `DigestEnabled` channel exists, take the attention feed's
`CardNeedsDecision` rows and, for each whose card has `DecisionNotifiedAt == null || DecisionNotifiedAt < item.SinceUtc`,
send `AwayDigestFormatter.FormatDecisionPing(item)` loud to every digest channel, then stamp
`Card.DecisionNotifiedAt = now`. Send-then-stamp, same as 0036: a failed send leaves the stamp and
the next tick retries; a duplicate is a question asked twice, a lost one is this card's whole
subject. A card parked, decided, and parked again has a newer `SinceUtc` than its stamp → pinged
again, which is a second question. A card decided before the sweep is no longer in the feed →
never pinged.

*Why a column and not an event:* cards have no event table; `CardRevision` is user-visible history
(`RevisionCount` is shown on the card, `CardModal.tsx:194`) and a "notified" row there would count
as an edit. One nullable `timestamptz` on `Cards` (`Card.DecisionNotifiedAt`, migration
`AddDecisionNotifiedAtToCards`) is the minimum honest store, and comparing it to the feed's
`SinceUtc` makes it self-resetting without a second write on the move-out path.

The ping's shape (`FormatDecisionPing`, first line is the key, as 0036 §2.4):

```
❓ CARD-0010 needs a decision — E2E failures needing a product decision (13)
Not flakes - each needs a call, not a retry: - Session-dependent tests: should the E2E… (parked 14:02)
https://<PublicBaseUrl>/orchestrator?tab=decisions
```

Body = the first `SentenceChars` of the reason (the formatter's existing `Clean`), the parked
time from `SinceUtc`; the footer link only when `Digest:PublicBaseUrl` is set. No cost line — a
card has no spend. **Deciding from Telegram is out of scope** (§4): a decision needs a target
column and free text, not a one-line answer, and the 0036 inbound branch keys on `task <8hex>`;
extending it is a follow-up card if lived-with use wants it.

**Digest — a `Decisions` section that stays until decided.** `AwayDigestDto` gains
`IReadOnlyList<AwayDigestCardDto> Decisions` (all current `CardNeedsDecision` rows, oldest first,
`IsNew` when `SinceUtc > sinceUtc`); `FormatDigest` renders `❓ Decisions (n)` **immediately after
`❗ Needs you`** with the identifier, title, and first sentence of the question, five rows then
`+ N more`. Unlike the settled sections it is **not windowed**: a decision is a state, and the
whole point of the card is that a state nobody re-surfaces is a state nobody sees. The quiet line
(`FormatQuiet`) is unchanged — a digest with a decision waiting is never quiet, because the section
makes `lines.Count > 1`.

**Hosted service:** one added line in `AwayDigestHostedService` (`:26`) calling the new sweep
between the Blocked sweep and the scheduled digest. Register `DecisionCardNotifier` beside
`BlockedTaskNotifier` in `server/Program.cs`.

**Not the alert path.** No `IAlertService.RaiseAsync`, no `AgentIncidentKind`, no change to
`ChannelAlertRouter` — §1.6 gives the reasons; the decision is recorded here so nobody adds a
Critical alert "for good measure" later and gets every decision into the ops digest voice.

### 2.4 Two feed fixes the acceptance path depends on

1. **Reopen into Needs decision is listed.** `BuildCardNeedsDecisionAsync` filters
   `(r.Kind == Move || r.Kind == Reopen) && r.ToStatus == NeedsDecision`; the "latest by
   `RevisionNumber`" selection is unchanged, so a reopen-then-move pair still yields one row with
   the newest reason. `Reason` on a reopen is mandatory already, so `Evidence` is never the
   fallback text on this path.
2. **A card move invalidates the attention query.** `useSignalRInvalidation.ts` `CardChanged`
   keys gain `['attention']` (`:68-76`). The map is module-private; the pin is the
   `DecisionsPanel` test that dispatches a `CardChanged` through the hub mock and asserts a
   refetch — the same shape the existing `AgentTaskChanged → attention` coupling is trusted on.

### 2.5 The card says what it is waiting for

`CardModal` (`client/src/features/board/CardModal.tsx`, next to the state badge at `:119`): when
`card.status === 'NeedsDecision'`, a `danger`-coloured `Alert` titled **"Waiting on a decision"**
showing the question and a **Decide…** button that opens the same `MoveMenu` configuration as
§2.2. The question comes from the attention feed row for this `cardId` (already in the query
cache on any page that shows a badge), falling back to the latest `Move|Reopen → NeedsDecision`
revision from `useCardRevisions` (`boards.ts:561`) when the feed has not caught up. No DTO change:
the `CardDto` stays as it is, and the fallback is the same data the History tab renders.

---

## 3. What this costs the surfaces it shares screen with

- **The app header** gains one chip that is absent on a normal day. It does not move the nav
  links; on narrow desktop widths it wraps with them.
- **The Orchestrator page** gains a fourth tab. The `attention` tab and its badge are untouched;
  a decision card appears in both lists by design (different altitudes, one source).
- **The digest** gains one section, present only while something is waiting. The DM gets one
  extra loud message per parking — on a day the orchestrator parks three cards that is three
  questions, which is what the DM is for (0036 §5).
- **`Cards`** gains one nullable column. No DTO exposes it; nothing reads it but the notifier.
- **`AttentionService`** widens one predicate. `AwayDigestProjection` already calls `GetAsync`
  once per compute and keeps doing so — the new section reuses that call.
- **Mobile home** is untouched. **`AttentionPanel`** is untouched. **`BoardCard`** is untouched
  (the column node already flags the state; the question has a home in §2.5).

---

## 4. Non-goals

- Fixing the drowned general count — the 34 parked messages are **CARD-0091**; this design
  routes around it with a second chip rather than reinterpreting the first.
- Backlog-by-priority on the orchestrator screen — **CARD-0094**.
- A new SignalR event, a new `GET /api/decisions`, or any second derivation of "needs a decision"
  outside `AttentionService` (CARD-0035 §D5, and the brief).
- Routing decisions through `ChannelAlertRouter` / raising an `Alert` or `AgentIncident` (§1.6).
- Deciding a card by replying in Telegram (§2.3) — a follow-up card, keyed on the same excerpt
  mechanism 0036 §2.4 uses, once the target-column question has an answer.
- Changing the server rule that a move **out** of Needs decision needs no reason. The Decide
  dialog requires one on the client; bookkeeping moves through `card.ps1 move` stay free (§6.2).
- Enabling the digest in production — an operator switch (§6.3), not a code change.
- Slack-specific work: `SendAsync` is provider-agnostic; a Slack channel with `DigestEnabled`
  simply gets the same text.

---

## 5. Acceptance: the CARD-0010 round trip

CARD-0010 is Done with its decision recorded (§1.7). The brief asks that it be the test case, so
the verification is a **reopen into Needs decision, then a decide back to Done that restores the
terminal reason verbatim** — it exercises §2.4(1) (the reopen path) and leaves the card's record
richer (two more revisions: the reopen with the questions, the move-out with the answers) rather
than rewritten. The current `terminalReason` must be copied out first and pasted back as the
Decide reason; a move into a terminal column makes the reason the new `TerminalReason`
(`BoardDtos.cs:120-127`).

```powershell
$id = '83588c84-1430-4588-bd16-75dfaa3d3816'      # CARD-0010 on Antiphon (the identifier is ambiguous — Gym Stat has one too)
pwsh -File scripts/card.ps1 get $id                # copy terminalReason to .antiphon\card-0010-decided.txt
# reason file = the card's three open questions, verbatim from its description
pwsh -File scripts/card.ps1 reopen $id -To 'Needs decision' -ReasonFile .antiphon\card-0010-questions.txt -By 'CARD-0123 verify'
```

Then, in order, each of which is a checkbox in the verify slice:

1. `GET /api/attention` lists a `CardNeedsDecision` row for `83588c84…` with the questions as
   `evidence` (§2.4(1); fails on master today).
2. Within one paint of the reopen, the header shows `Decisions (1)` on `/`, `/boards/8988ca03…`,
   `/agents` (§2.1, §2.4(2)); `Needs attention (n)` is unchanged in kind.
3. `/orchestrator?tab=decisions` shows the card under **Antiphon**, the three questions unclamped,
   `Decide…` and `Open card` (§2.2).
4. `Open card` lands on a modal whose top reads **Waiting on a decision** with the questions (§2.5).
5. With `Digest:Enabled=true` and the DM channel enabled + `DigestEnabled` (§6.3): within
   `SweepSeconds` the DM receives `❓ CARD-0010 needs a decision — …`; a second sweep sends nothing;
   `POST /api/digest/send` produces a digest whose second section is `❓ Decisions (1)`.
6. `Decide…` → target Done, reason = the saved terminal reason. The card is Done again,
   `terminalReason` byte-identical to before, `revisionCount` +2, the row gone from the feed, the
   chip gone from the header, and `card.ps1 history $id` reads: parked with the questions, decided
   with the answers.

If the operator prefers not to touch the real record (§6.4), the identical sequence on a fresh
card on the `Catalog Test` board with the same reason file proves everything except that the
reopen path is exercised on a card whose history matters — which is the point of using the real one.

---

## 6. Slices, tiers, tests

Each slice leaves the app shippable. S1 is the smallest and unblocks the acceptance path; S2 and
S3 are independent of each other after S1; S4 is independent of S3.

| Slice | Contents | Tests | Tier |
|---|---|---|---|
| **S1** feed fixes | §2.4: builder accepts `Reopen`; `CardChanged` → `['attention']` | `AttentionServiceTests.A_card_reopened_straight_into_needs_decision_is_listed_with_the_reopen_reason`, `…A_card_reopened_then_moved_within_needs_decision_shows_the_newest_reason_once` (scoped to the seeded `CardId`, per the shared-Postgres rule); client pin lands with S2 | Codex terra |
| **S2** chip + tab + card callout | §2.1 `DecisionsBadge` in `Layout`; §2.2 `decisions` tab + `DecisionsPanel` + Decide via `MoveMenu`; §2.5 `CardModal` callout | `DecisionsBadge.test.tsx` (`renders nothing when no card needs a decision, even with 36 parked messages open`, `counts only decision rows and links to the decisions tab`); `DecisionsPanel.test.tsx` (`groups decision cards by board, oldest first, with the whole question unclamped`, `Decide opens the move dialog with the reason required and records it as the move reason`, `Open card goes to the board with the card selected`, `a CardChanged hub event refetches the feed`, `reads as calm with no decisions waiting`); `OrchestratorPage.test.tsx` (`the decisions tab badge counts decision cards and nothing else`); `CardModal.test.tsx` (`a card in Needs decision leads with its question and a Decide button`, `falls back to the revision history when the feed has no row yet`) | Grok |
| **S3** push | §2.3: migration `AddDecisionNotifiedAtToCards`; `DecisionCardNotifier`; `FormatDecisionPing`; `Decisions` section in projection + formatter; `WakeOnDecision`; hosted-service line; `Program.cs` registration | `DecisionCardNotifierTests` modelled on `BlockedTaskNotifierTests` (`A_newly_parked_card_is_pinged_exactly_once`, `A_card_parked_again_after_being_decided_is_pinged_again`, `A_card_decided_before_the_sweep_is_not_pinged`, `A_throwing_send_leaves_the_stamp_unset_so_the_next_sweep_retries`, `Wake_on_decision_false_sends_nothing`); `AwayDigestFormatterTests` (`Decision_ping_first_line_carries_the_card_identifier`, `Digest_lists_waiting_decisions_after_needs_you_and_folds_past_five`, `A_waiting_decision_makes_a_digest_that_is_never_quiet`); `AwayDigestProjectionTests` (`Decisions_are_not_windowed_and_flag_is_new_by_since`) | Codex terra |
| **S4** docs | `docs/antiphon-api.md:269` (list the kinds and the `OpenCard` action; note the decisions tab URL); a "Digest channel" subsection in `docs/telegram.md` (both ping shapes, the two switches, `PublicBaseUrl`) — 0036 §3.6 asked for this and it was never written; one AGENTS.md gotcha bullet: *a card's decision question lives on its move/reopen revision and the attention feed — never add a column for it, and never route a decision through the alert sinks* | — | Codex luna |
| **Verify** | §5 on the live stack, then the two switches back to whatever the operator chose | — | Codex luna |

Build to `--property:OutputPath=bin-<name>/` while the daemons hold `bin/`; client suite via
`pwsh -File scripts/test-client.ps1`; `Antiphon.Tests` chunked by namespace
(`--treenode-filter "/*/Antiphon.Tests.Application/*/*"`).

---

## 7. Decisions that are the operator's — each with a recommendation

1. **Where the chip lives: app header (every page) vs home header only.** Recommend the **app
   header** (§2.1). The home header already has a chip that cannot be seen (§1.3); the card's
   title is "can't be missed", and the operator spends most of the day on a board or the agents
   page, not on `/`. Cost: one more element in `Layout`'s desktop header.
2. **Decide requires a reason on the client while the server keeps move-out free.** Recommend
   **yes, client-only.** The dialog's whole purpose is to record the decision; `card.ps1 move`
   bookkeeping (a card parked by mistake, moved back) should not be forced to invent one, and
   CARD-0122's shipped rule and test say move-out is free. If lived-with use shows decisions being
   recorded as empty moves, the server gate is one line at `CardService.cs:284`.
3. **Which channel gets the pings, and flipping `Digest:Enabled`.** Recommend **enable the `Mike`
   DM channel (`2d2f6c45…`, currently disabled) and set `DigestEnabled` on it; set `Digest:Enabled=true`
   in `server/appsettings.json`.** This also switches on the Blocked-task pings and the 08:00/18:00
   digest that 0036 built and nobody has yet turned on — three surfaces for one switch. A bound
   family group must not be the digest channel (0036 §2.4's inbound-ambiguity argument).
4. **Verify on the real CARD-0010 (reopen → decide, terminal reason restored verbatim) vs a
   throwaway card.** Recommend the **real card** (§5): the brief asks for it, it is the only path
   that exercises §2.4(1) on a record that matters, and the round trip adds two honest revisions
   rather than rewriting anything. The throwaway alternative is one command different.
5. **Decisions in every digest until decided (not windowed).** Recommend **yes** (§2.3). It is the
   one place in the design that nags on purpose; if it grates, `Digest:DecisionsInEveryDigest=false`
   is a one-line gate, and the ping still fires once.
6. **Whether to widen `NeedsYouBand` on the phone with a Decide verb.** Recommend **no** for this
   card: the tap already lands on the board, and §2.5 puts the question and the verb on the card
   itself, which is where the phone arrives.
