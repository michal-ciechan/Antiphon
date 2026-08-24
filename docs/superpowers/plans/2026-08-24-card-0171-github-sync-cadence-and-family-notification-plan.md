# CARD-0171 — GitHub-sync cadence (3-hourly) + Family-channel change notification — plan

**Date:** 2026-08-24 · **Card:** CARD-0171 (`eb2c88bd-e5a9-4b23-bde5-b521282e3c0f`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** `master` @ `3d82003`. Every file:line below was re-read out of the code on
that commit; every live fact was queried this pass (2026-08-24 15:25Z).

**Established facts (Investigate, this pass — LIVE-VERIFIED where marked):**

- **The sync response shape** (`server/Application/Dtos/TrackerSyncDtos.cs`):
  `TrackerSyncRunResult(Boards, ConcurrentRunSkipped)`; per board
  `TrackerSyncBoardResult(BoardId, BoardName, IssuesPulled, CommentsIn, CommentsOut,
  LabelsChanged, StateChanges, Creates, Skips, Error)`. `IssuesPulled` is the raw count of
  issues the read-side upsert fetched (`ExternalTrackerSyncService.SyncAsync`, `:43`), not a
  delta; every other counter is a genuine this-run write/insert count (CARD-0166 sync5 measured
  all five at 0 on a steady-state run). Counters are produced in
  `TrackerBidirectionalSyncService.SyncBoardAsync` (`:93-170`).
- **Two changes the counters do NOT see** (`TrackerBidirectionalSyncService.cs`):
  `ApplyExternalReopens` (`:172-221`) moves a terminal card back to the first active column when
  GitHub reopened the issue — and increments **nothing**; `PushExportTitleBodyAsync` (call at `:325`)
  pushes an edited export-origin title/body to GitHub — also uncounted. Both are real changes
  that would send nothing under the card's sum.
- **Change detection for GitHub→card creates is structurally out of reach of a per-run count.**
  New GitHub issues become cards on the orchestrator tick's read-only sync every
  `Orchestrator:TrackerSyncIntervalMinutes` (30, `OrchestratorSettings.cs:21`), so by the time
  a 3-hourly run fires the tick has almost always already created the card and the run's own
  `IssuesPulled` cannot tell. §9.
- **The alert path is a severity-threshold broadcast, not an addressable send.**
  `ChannelAlertRouter.RouteAsync` (`server/Application/Services/ChannelAlertRouter.cs:37-61`)
  fans every persisted alert to **every** `ChatChannel` with
  `AlertMinSeverity != null && alert.Severity >= AlertMinSeverity`; there is no per-alert
  target, no source filter. `AlertDigestFlusher` (`:69-169`) then sends ONE digest per sink per
  `MinMinutesBetweenSends` (5, `AlertsSettings.cs:13`) headed `Antiphon alerts:` with a
  severity emoji per line and `Detail` truncated at 200 chars (`:151-168`). Alert rows also
  toast in the client (`client/src/hooks/useAlertToasts.ts`) and prune after 30 days.
- **LIVE: the Family channel is not an alert sink and is agent-bound.** `GET /api/channels`:
  `caee9d25-b751-4401-a295-3b7e242842aa` telegram `-5052370282` "Family", `enabled=true`,
  `alertMinSeverity=null`, `agentId=a7647365…` (the Family agent). No channel in the catalog is
  an alert sink today. `Antiphon-Family (retired test)` `5fec07ce…` and `Mike` DM `2d2f6c45…`
  are both `enabled=false`.
- **LIVE: the Windmill schedule** `u/lndcobra/antiphon_github_sync`: cron `0 0 18 * * *`
  (6-field, seconds first), `Europe/London`, enabled, `edited_at 2026-08-24T11:35:40Z`,
  `no_flow_overlap=false`, `cron_version=v2`. Script hash `f1d3e091670baf24`, tag `desktop`,
  language bash: one `ssh … lndco@host.docker.internal 'powershell -NoProfile -ExecutionPolicy
  Bypass -File C:\src\Antiphon\scripts\github-sync.ps1'` line. The first 18:00 run has not yet
  happened since it was edited (now 16:25 BST); there is no scheduled-run history to learn from.
  Token-minting mechanism: the windmill skill's temp superadmin DB token
  (`C:\Users\lndco\.claude\skills\windmill\SKILL.md`, "Driving the API") — used this pass to
  read the schedule and script, works as documented.
- **`scripts/github-sync.ps1`** POSTs `/api/tracker-sync/run` (or the per-board endpoint with
  `-BoardId`), prints the summary, and **exits 0 even when a board reports `error`** (`:90`),
  so a failed sync is a green Windmill job today.
- **The draft** (`3d82003`): `ChatChannelService.SendAsync(Guid, string, ct)`
  (`server/Application/Services/ChatChannelService.cs:92-109`) — lookup, `ConflictException
  channel_disabled` on `Enabled=false`, `IAntiphonMessagingProducer.SendAsync(new ChannelReply
  { Channel, ConversationId, Text })`; `POST /api/channels/{id}/send` +
  `SendChannelMessageRequest(Text)`; two `ChannelBridgeTests` pinning the service.
- **Outbound envelope:** `ChannelReply` (`src/Antiphon.Messaging/ChannelReply.cs`) — `Kind`
  defaults `Answer` (`Progress`/`Question` may be rendered with a prefix by adapters), plain
  `Text`, optional `RawOverrides` for Telegram `parse_mode`. The server API has **no
  authentication** anywhere (`Program.cs` — no `AddAuthentication`/`RequireAuthorization`);
  17202 is an Aspire-published localhost endpoint (`Antiphon.AppHost/Program.cs:69`), with no
  Caddy vhost (the links dashboard lists it as `http://localhost:17202` only). `POST
  /api/tracker-sync/run` — which writes to GitHub — and `POST /api/sessions` — which launches
  agents — sit behind exactly the same (absent) auth. ClaudeBot's `tg-send.cs` already produces
  arbitrary text to `channels.outbound` with no auth at all (telegram skill).
- **Config extension point:** unknown scalar keys under `tracker:` are retained in
  `IssueTrackerConfig.Options` (`docs/workflow-tracker-block.md`); `sync_out_create` and
  `export_since` are read that way (`TrackerBidirectionalSyncService.cs:683-689`). No parser
  change is needed to add a key.

**Related:** CARD-0166 (the sync and its triggers — this card only adds a notification to its
trigger surface), CARD-0067 (channel-reply durability — the reason a lost send must never be a
silent `return`), the 2026-07-20 alerting spec Q5/Q6 (why the alert path is shaped the way it is).

---

## Verdict up front — the nine decisions

1. **Mechanism: a targeted channel send, not the alert path — the draft's `SendAsync` is
   RATIFIED as the primitive; the draft's HTTP endpoint is DISCARDED.** The alert router has no
   notion of "this alert, that channel": making Family a sink means Family receives every
   `Info+`/`Warning+` alert the system raises (stalled tasks, quota, reconciliation…), in the
   `Antiphon alerts:` digest voice, 5-minute-windowed, detail-truncated. Adding a per-source
   filter to sinks is a bigger feature than this card and still the wrong voice for "cards moved,
   FYI". `ChatChannelService.SendAsync` stays exactly as drafted (the disabled-channel refusal is
   correct: `Enabled=false` is the operator's off switch and must not have a side door). `POST
   /api/channels/{id}/send` and `SendChannelMessageRequest` are deleted: once the server composes
   and sends the summary itself (D2) nothing calls them, and a text-to-any-channel megaphone with
   no audit row deserves its own card if it is ever wanted. The two `ChannelBridgeTests` stay.
   §1.
2. **Diff + format + send live SERVER-SIDE, in the sync trigger surface, opt-in per trigger.**
   New `TrackerSyncNotifier` composes from the run result and sends via `SendAsync`; the two
   endpoints call it only when the caller passes `?notify=true`. Not in the script (a second
   parser of the same JSON, PowerShell string-building, counts-only content, and the button/
   per-board runs could never notify); not unconditionally inside `RunAsync` (every "Sync
   tracker now" click would ping Family). The script becomes a thin trigger that forwards
   `-Notify`. §2.
3. **The target is board config, the opt-in is the trigger's.** `tracker.notify_channel:
   <channel guid | exact channel title>` in the board's `tracker:` block (read from
   `Options`, zero parser change) says who hears about *this board's* changes, next to
   `token_key` and `sync_out_create`. `?notify=true` / `-Notify` says whether *this run*
   announces. Windmill passes it; the board button does not. Multi-board runs group boards by
   resolved channel — one message per channel per run. §3.
4. **Change signal = the card's per-run sum, extended to close the two blind spots, plus a
   change LIST so the message can name cards.** `TrackerSyncBoardResult` gains
   `ExternalReopens` (the `ApplyExternalReopens` gap) and `Changes:
   IReadOnlyList<TrackerSyncChange>(Kind, CardIdentifier, ExternalKey, Url)` with kinds
   `CommentIn | CommentOut | LabelsChanged | ClosedOnGitHub | ReopenedOnGitHub |
   ReopenedFromGitHub | Created | ContentPushed`. "Changed" ≡ `Changes.Count > 0` ≡ the card's
   sum + reopens + content pushes. `IssuesPulled` never enters the gate. Existing counters keep
   their meaning (script/UI unchanged). §4.
5. **Message: plain text, one per channel per run, only nonzero lines, cards named, capped.**
   Exact template in §5. `ChannelReplyKind.Answer`, no `parse_mode`, no per-issue links (one
   repo issues link per board when `base_url` is the github.com default). ≤5 identifiers per
   line then `+N more`; ≤3 500 chars total (Telegram's 4 096). §5.
6. **Cadence: a Windmill-console/API edit in this build's ops slice, not code.** Nothing in the
   repo deploys schedules; CARD-0166 registered the schedule at build time via the windmill
   skill and this card changes it the same way: `0 0 */3 * * *` Europe/London. The same slice
   republishes the Windmill script content with `-Notify` and a truthful summary, and fixes the
   header comment in `github-sync.ps1`. §6.
7. **No independent notification throttle.** The change gate plus the 3-hour cadence IS the
   throttle: ceiling 8 messages/day, realistic ≪ that; an extra "at most once per N hours" would
   only delay news that is by construction already batched. GitHub API budget is trivial (§7
   numbers). The alert pipeline's 5-minute window is inapplicable by D1, deliberately. The one
   known flap (a human re-adding a managed label every cycle → `labelsChanged=1` every run) is
   documented with its mitigation, not pre-built. §7.
8. **Failures: never to Family, never silent, never fail the sync.** A per-board `error` is not
   a change and is never announced to Family (that is what alerts are for); the script now exits
   1 when any board reports `error` so Windmill shows the failure. A notification that cannot be
   sent (unset/unresolvable/ambiguous/disabled target, producer throw) logs a Warning and is
   reported in the response under `notifications[]` with a reason — the sync has already
   committed and stays 200. §8.
9. **Out of scope, explicitly:** GitHub→card creates in the change signal (needs a watermark
   digest over persisted rows, sketched in §9 as the follow-up if wanted); the generic send
   endpoint; alert-sink source filtering; Telegram formatting. §9.

---

## 1. Decision 1 — mechanism

**Why not the alert path, precisely.** Three facts, each sufficient:

- *Targeting.* `ChannelAlertRouter.RouteAsync` selects sinks by severity only (`:47-51`). To
  reach Family you set `Family.AlertMinSeverity = Info` (or `Warning`), and from that moment
  every alert at or above it — `TaskProgressStalled`, `subscription_quota_low`, census alerts,
  the log tap if ever enabled — lands in the family group chat. There is no way to say "only
  source `tracker-sync`".
- *Voice.* The digest is built for an ops sink: `Antiphon alerts:` header, 🔴🟠🟡🔵 per line,
  `[source] Title ×N — detail…` with detail cut at 200 chars (`AlertDigestFlusher.Format`). A
  "2 comments came in from GitHub on CARD-0170" FYI does not belong in that frame, and the
  card-naming content in §5 does not fit a 200-char detail.
- *Semantics.* Alerts are rows that toast in the UI, count as "something to look at", and are
  swept/pruned as incidents. A successful sync is not an incident.

**What the draft got right.** `SendAsync` is the minimal correct primitive: the same
`IAntiphonMessagingProducer` → `channels.outbound` → am-service → Telegram path the alert
flusher uses, addressed by catalog channel id so the provider/external id never leak into
callers, refusing on `Enabled=false`. Ratified verbatim, including the two tests.

**What the draft got wrong.** The endpoint. Not because of auth — the API has none anywhere and
this endpoint would be no more exposed than the GitHub-writing sync endpoint beside it — but
because it is a generic capability nothing in this card needs once D2 holds, and one that
should carry an audit row and a card of its own if it is ever wanted. Delete
`ChannelEndpoints.cs:31-39` and `SendChannelMessageRequest`.

## 2. Decision 2 — where the logic lives

`server/Application/Services/TrackerSyncNotifier.cs` (scoped), two parts:

- `TrackerSyncSummaryFormatter` — **pure, static**: `Format(IReadOnlyList<(BoardResult,
  IssueTrackerConfig)>) → string?` (null when no board has changes). Unit-tested without a DB.
- `TrackerSyncNotifier.NotifyAsync(TrackerSyncRunResult run, ct) →
  IReadOnlyList<TrackerSyncNotificationResult>`: for each board with `Changes.Count > 0`,
  re-parse its tracker config (`IssueTrackerConfigParser.TryParse`, same call the sync makes),
  resolve `notify_channel` (§3), group by resolved channel, format once per channel, call
  `ChatChannelService.SendAsync`, catch everything into a reason (§8).

The endpoints (`TrackerSyncEndpoints.cs`) gain `[FromQuery] bool notify = false` and, after a
successful `RunAsync`, `if (notify) result = result with { Notifications = await
notifier.NotifyAsync(result, ct) }`. The per-board endpoint's concurrent-run 409 fires before
any notify. `TrackerBidirectionalSyncService` does not learn about channels — it only records
what it changed (D4).

## 3. Decision 3 — target and opt-in

`tracker:` block, documented in `docs/workflow-tracker-block.md`:

| Key | Required | Notes |
|---|---|---|
| `notify_channel` | no | Catalog channel to receive a change summary when a sync is triggered with `notify=true`. A channel GUID (recommended) or an exact, case-insensitive `Title` match that is unique in the catalog. Unset ⇒ notify is a no-op for this board. |

Resolution (`TrackerSyncNotifier.ResolveChannelAsync`): `Guid.TryParse` → by `Id`; else
`ChatChannels.Where(c => c.Title == value)` (ordinal-ignore-case, evaluated client-side if
needed): 0 ⇒ `channel_not_found`, >1 ⇒ `channel_ambiguous`. `Enabled=false` is discovered by
`SendAsync`'s `ConflictException` ⇒ `channel_disabled`. The live config for the Antiphon board
uses the GUID `caee9d25-b751-4401-a295-3b7e242842aa` (titles are editable).

Opt-in surfaces: `POST /api/tracker-sync/run?notify=true`, `POST
/api/boards/{id}/tracker/sync?notify=true`, `scripts/github-sync.ps1 -Notify`. The board
button keeps calling without it. A sync triggered with `notify=true` on a board whose block has
no `notify_channel` reports `notify_channel_unset` and sends nothing — the per-board config is
the consent, the flag alone is not enough.

## 4. Decision 4 — the change signal

Additions to `TrackerSyncDtos.cs`:

```csharp
public enum TrackerSyncChangeKind
{ CommentIn, CommentOut, LabelsChanged, ClosedOnGitHub, ReopenedOnGitHub, ReopenedFromGitHub, Created, ContentPushed }

public sealed record TrackerSyncChange(
    TrackerSyncChangeKind Kind, string CardIdentifier, string ExternalKey, string? Url);

// TrackerSyncBoardResult: + int ExternalReopens, + IReadOnlyList<TrackerSyncChange> Changes
// TrackerSyncRunResult:   + IReadOnlyList<TrackerSyncNotificationResult> Notifications = []

public sealed record TrackerSyncNotificationResult(
    Guid BoardId, bool Sent, Guid? ChannelId, string? Reason);
```

Recording sites in `TrackerBidirectionalSyncService.cs`, one `changes.Add(...)` each, next to
the existing `++`: `PullCommentsAsync` insert (`:274`, `CommentIn` — one per inserted row, echo
closes excluded because they `continue` before the insert), `PushDiscussionCommentsAsync`
post (`:365`) and `PushContentEditCommentsAsync` post (`:412`) (`CommentOut`),
`SyncLabelsAsync` (`:457`/`:472`/`:478`, one `LabelsChanged` per issue that had any label
write, so `Changes` for labels counts issues while `LabelsChanged` keeps counting writes as
today), `SyncStateAsync` close (`:518`, `ClosedOnGitHub`) and reopen push (`:550`,
`ReopenedOnGitHub`), `ApplyExternalReopens` (`:219`, `ReopenedFromGitHub` **and**
`ExternalReopens++` — the gap fix), `CreateMissingIssuesAsync` (`:580`, `Created`),
`PushExportTitleBodyAsync` (`ContentPushed`). `SyncBoardAsync` threads a `List<
TrackerSyncChange>` through those calls (or the service holds it per board — either; the list
is per `SyncBoardAsync` invocation and must not be shared across boards). `CardIdentifier` =
`Card.Identifier` (already used in the close body, `:509`), `ExternalKey` =
`ExternalIssueRef.ExternalKey`, `Url` = `ExternalIssueRef.Url`.

Gate: `Changes.Count > 0`. Equivalent to the card's `commentsIn + commentsOut + labelsChanged +
stateChanges + creates > 0` plus the two previously-invisible kinds. `IssuesPulled` and `Skips`
are not changes; a board with only an `Error` is not a change.

## 5. Decision 5 — the message

Plain text, `ChannelReplyKind.Answer` (the default — `Progress` may be prefixed by adapters as
"still working"), no `RawOverrides`. One message per resolved channel per run; boards with no
changes omitted; boards separated by a blank line.

```
Antiphon <-> GitHub sync: Antiphon board
- 2 comments in from GitHub: CARD-0170, CARD-0171
- 1 comment posted to GitHub: CARD-0166
- 1 issue closed on GitHub: CARD-0166 (#14)
- 1 issue reopened from GitHub: CARD-0150 (#9)
- 1 issue created on GitHub: CARD-0172 (#16)
- labels updated on 3 issues
https://github.com/michal-ciechan/Antiphon/issues
```

Rules: line order as above (`CommentIn, CommentOut, ClosedOnGitHub, ReopenedFromGitHub,
ReopenedOnGitHub` ("reopened on GitHub from Antiphon"), `Created, ContentPushed` ("content
updated on GitHub"), `LabelsChanged` last, count-only, no identifiers — it is the least
interesting kind and the flap-prone one, §7); singular/plural by count; identifiers
deduplicated per line, in first-seen order, ≤5 then `, +N more`; `(#key)` only where
`ExternalKey` is short (≤12 chars); the trailing link only when the board's `base_url` is unset
or `https://api.github.com` and `repository` is known; hard cap 3 500 chars with a final
`…` (Telegram rejects >4 096). ASCII except the identifiers/keys — nothing here is a daemon
script, but there is also nothing an emoji would add.

## 6. Decision 6 — cadence

Operator/build-time action against the Windmill API (windmill skill mechanism, verified this
pass), performed in S3 so it lands with the notification and the docs stay truthful:

- `POST /api/w/mc/schedules/update/u/lndcobra/antiphon_github_sync` with
  `{ "schedule": "0 0 */3 * * *", "timezone": "Europe/London", "script_path":
  "u/lndcobra/antiphon_github_sync", "is_flow": false, "args": {} }` — 00:00, 03:00 … 21:00
  London, eight runs a day. Verify with `GET /api/w/mc/schedules/get/…`.
- `POST /api/w/mc/scripts/create` at the same path with the ssh line ending
  `…github-sync.ps1 -Notify` and summary `Antiphon: 3-hourly GitHub Issues bidirectional sync +
  Family change summary (CARD-0171)`; the schedule follows the path, so it picks up the new
  hash. (Script versions are immutable per hash — this is how the skill's worked example
  updates a script.)
- `scripts/github-sync.ps1` header: "Recurring run … every 3 hours (`0 0 */3 * * *`)".

The read-only tick keeps its 30-minute interval; nothing else in the schedule changes
(`no_flow_overlap` stays false — runs take ~5 s and the per-board 409 guard already refuses
overlap).

## 7. Decision 7 — rate and noise

- **GitHub budget.** Per bidirectional run on the live board: 1 issues list (≤100/page), 1–2
  repo-level comments-since calls, plus one request per actual write. Eight runs/day ≈ 24 read
  requests + the tick's 48 = ~72/day against a PAT's 5 000/hour. Not a concern.
- **Family ceiling.** ≤8 messages/day by construction; a message needs an actual change in the
  preceding 3 hours. Expected real volume: a handful per week.
- **No second throttle.** A per-channel "not more than once per N hours" would suppress a
  genuine 09:00 change because a 06:00 one was announced, for no gain — the batching is
  already the cadence. Concurrent double-fire is already a 409 (no double notify).
- **The one flap.** `docs/workflow-tracker-block.md` states the sync rewrites human edits of
  managed `status:*`/`priority:*` labels on the next run. A human who re-adds such a label every
  cycle produces `labelsChanged=1` on every run ⇒ a Family message every 3 hours saying "labels
  updated on 1 issue". Mitigation is documented, not built: if it happens, drop label-only runs
  from the gate with a `Tracker:NotifyOnLabelOnlyChanges=false` setting (one line in the gate).

## 8. Decision 8 — failures

- Per-board `Error` (`TrackerSyncBoardResult.Error`, set at `:76-85`) → not a change, never in
  the Family message. `github-sync.ps1` prints it (already does) and **exits 1** when any board
  has one, so the Windmill job history is red — today it is green.
- Notification outcomes, one `TrackerSyncNotificationResult` per board with changes:
  `Sent=true` with `ChannelId`; or `Sent=false` with `Reason ∈ { notify_channel_unset,
  channel_not_found, channel_ambiguous, channel_disabled, send_failed }`. `send_failed` wraps a
  producer throw (broker down) — logged `Warning` with the channel id and message length via
  `ILogger<TrackerSyncNotifier>`, never rethrown: the sync has already committed and the caller
  needs its summary. Boards without changes get no entry. The script prints each entry.
- Nothing here raises an alert: a sync that could not announce itself is visible in the Windmill
  log and the response; paging the ops sink for it would be the D1 mistake from the other side.

## 9. Decision 9 — out of scope, and the watermark alternative

- **GitHub→card creates (and read-side title/state updates) are not in the change signal.** The
  tick creates the card first (§ facts). Catching it requires a *watermark digest*: a
  `Board.TrackerNotifiedAt` stamp and a summary computed from persisted rows since it —
  `ExternalIssueRef` rows created after the stamp (needs a `CreatedAt` column; the entity has
  only `LastSyncedAt`), `CardComment` rows with `Origin=External` imported after it (needs an
  `ImportedAt`; `CreatedAt` is GitHub's time), `Origin=Antiphon` rows with `SyncedAt` after it,
  `CardRevision` rows by actor `external-tracker`, `LastOutboundSyncedAt` after it. That design
  also survives a failed send (unannounced changes carry to the next run) and is trigger-
  independent. It is a different, larger card; this one implements the card's own per-run
  definition. If Family is meant to hear "Ola's new issue became CARD-0172", file that card.
- The generic `POST /api/channels/{id}/send`. Alert-sink source filtering. Telegram
  `parse_mode`/formatting. Slack is not excluded — the send is provider-agnostic — but is not
  smoke-tested here.

---

## 10. Verification / test design

- `TrackerSyncSummaryFormatterTests` (pure): no changes ⇒ null; each kind renders its line with
  the right singular/plural; label line is count-only; identifier dedup + ≤5 + `+N more`;
  multi-board ⇒ blank-line-separated blocks, zero-change board omitted; link only for github.com
  default; 3 500-char cap.
- Existing bidirectional sync tests (`tests/Antiphon.Tests/Application/` CARD-0166 S4–S6
  files) gain: `Changes` carries the card identifier for each kind; **an external reopen
  increments `ExternalReopens` and appears in `Changes`** — red on `3d82003`, the gap pin.
- `TrackerSyncNotifierTests` (DB fixture + `FakeAntiphonMessagingClient`): every `Reason` in
  §8 including a throwing producer ⇒ `send_failed` with the run result intact; two boards on
  the same channel ⇒ one `SentReplies` entry containing both blocks; two boards on different
  channels ⇒ two.
- `TrackerSyncEndpointTests` (existing factory, `FakeAntiphonMessagingClient` registered): a
  board with a `notify_channel` and a change + `?notify=true` ⇒ exactly one reply to that
  channel's provider/external id with the formatted text; same without the flag ⇒ none; flag
  with no change ⇒ none; response carries `notifications[]`. Concurrent 409 still fires before
  any send.
- `ChannelBridgeTests.A_proactive_send_*` (2): kept as-is.
- `GithubSyncScriptTests` under `tests/Antiphon.Tests/Scripts/` following
  `CheckpointTaskScriptTests` (`[ParallelLimiter<ProcessSpawnLimit>]`): against a local
  `HttpListener`: `-Notify` sends `?notify=true`; a body with a board `error` ⇒ exit 1; clean
  body ⇒ exit 0 and the notification lines printed.
- Live smoke (S3): point `notify_channel` first at the `Mike` DM channel `2d2f6c45…`
  (temporarily `enabled=true`), make one real change (a discussion comment on a live card ⇒
  `commentsOut=1`), run `github-sync.ps1 -Notify`, confirm `[outbound] sent via telegram` in
  `am-service` logs and the message on the phone, then switch the block to the Family GUID and
  re-disable the DM channel. **Decision for the operator:** skip the DM detour and let the first
  real 3-hourly change be the smoke — the message is benign — or keep the detour.

## 11. Build order

1. **S1 — server.** DTO additions (§4), change recording + `ExternalReopens` in
   `TrackerBidirectionalSyncService`, `TrackerSyncNotifier` + formatter, `?notify` on both
   endpoints, delete the draft endpoint + DTO (keep `SendAsync` + its two tests), DI
   registration, tests in §10 (formatter, sync additions, notifier, endpoint). Migration: none
   (no schema change).
2. **S2 — script + docs.** `github-sync.ps1 -Notify`, exit 1 on any board `error`, header
   cadence text, notification lines; `GithubSyncScriptTests`; `docs/workflow-tracker-block.md`
   (`notify_channel` row + triggers note); CLAUDE.md gotcha line under the CARD-0166 entry
   (targeted send ≠ alert path; `notify` is per-trigger, `notify_channel` per-board; label
   flap).
3. **S3 — ops (not code).** `PUT /api/boards/8988ca03…/workflow` (or the Monaco editor) adding
   `notify_channel: caee9d25-b751-4401-a295-3b7e242842aa` to the Antiphon board's `tracker:`
   block; Windmill script republish with `-Notify` + schedule update to `0 0 */3 * * *` (§6);
   live smoke (§10); record the schedule `edited_at` and the first scheduled run's job id on the
   card.

S1 and S2 are independently shippable; S3 depends on both and is the only step that touches
anything live.
