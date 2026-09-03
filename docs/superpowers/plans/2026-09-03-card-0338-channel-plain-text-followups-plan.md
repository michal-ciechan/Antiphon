# CARD-0338 — plan: a channel-bound orchestrator's progress must reach the chat without a file to attach

**Plan pass, 2026-09-03. Sources verified at `2083129b` (CARD-0337 S3 landed): `ChannelReplyDispatcher`,
`ChannelContracts`, `ChannelPreamble`, `AgentWorkspaceProvisioner`, `server/Bundles/orchestrator.md`,
`AgentSessionRuntime` (dispatch triggers), `AgentTaskReplyService` / `AgentTaskCheckService` /
`ScheduleService` (note origins), `AgentSupervisorService.RecordIncidentAsync`, `AlertService`,
`ChannelAlertRouter`, `AttentionService` (RecentCriticalIncident), `BlockedTaskNotifier` /
`DecisionCardNotifier` / `AwayDigestHostedService` (the existing pager), `SessionMessageQueueService.IsWorkingAsync`,
`ChatChannelService`, `client/src/features/channels/ChannelsPage.tsx`, `useAlertToasts.ts`, the CARD-0250,
CARD-0233, CARD-0281, CARD-0245, CARD-0036 and CARD-0337 plans, GitHub #30 (this card), #28 (CARD-0324),
#25 (CARD-0281). No production code is changed by this plan.**

**Where the evidence lives.** The incident happened on the mav-ref (PredictionMarkets) deployment. This
box has no `PM-Orchestrator-Grok` agent and no `D0B1VUH2EAK` catalog row, and the running SHA the card
names (`7f8b0e37`) is not in this repository's object store (a local merge on that machine). Everything
below is grounded in code and in master's history; the two facts that can only be confirmed on mav-ref are
called out as such in §5.

## 1. Verdict up front

1. **The card's mechanism is confirmed, and it is quieter than the card says.** A machine-triggered turn
   (owning prompt = a Delegation / Check / System injection) whose AssistantText carries no `[[attach:]]`
   and whose task has no undelivered CARD-0337 bundle returns from
   `DispatchMachineTurnAttachmentsAsync` **before any claim, with no log line and no incident**
   (`ChannelReplyDispatcher.cs:1070-1071`). All five status turns in the incident (23:30, 23:31, 23:52,
   00:21, 00:25 UTC) were exactly that shape: `[task … done]`, `[check …]` and `[task … failed]` turns
   with plain text. CARD-0337 S3 (`2083129b`) only changes the outcome for tasks that produced a
   document bundle; a "CARD-0003 landed, review blocked" turn still vanishes today.
2. **Why the attach-only rule exists, and which half of the reason still holds.** CARD-0250 §3 chose
   option B (attachments-only follow-up) for two stated reasons: (a) plain text has *no machine-readable
   "this is for the chat" signal*, and (b) *publishing every machine-triggered turn's text would spam the
   conversation*. It was deliberate — the instructions were rewritten to match the code rather than the
   code to match the instructions. Reason (a) is already answered by the machinery CARD-0250 itself built:
   the origin gate (owning prompt must match a **Sent** Antiphon injection row — an operator typing in the
   terminal can never trigger a send) plus the frozen `NO_REPLY` contract give a turn-level opt-out that
   is exactly the signal the preamble already tells channel agents about. Reason (b) is real but
   *origin-shaped*: the spam is the bootstrap's "READY", restart/compaction housekeeping (System origin)
   and, at worst, check-in chatter (Check origin, 5→60 min ramp, ≤10 per task). It is not the
   `[task … done|failed]` turns the human is waiting for.
3. **Point 1 — chosen: deliver a machine-triggered turn's plain text as a follow-up `ChannelReply` for
   Delegation, Check and Scheduled origins, opt-out with `NO_REPLY`; System stays marker-only** (§4, S1).
   Same conversation, same claim-before-produce idempotency row, same never-weaken list as CARD-0250.
   A status PDF (the card's "or") is rejected — a two-line status rendered to a document is worse on a
   phone than the two lines. An opt-in `[[reply]]` marker is rejected for the reason CARD-0337 §3 A
   recorded: the rule the model forgot is not fixed by a second rule to forget.
4. **Point 2 — yes, with a correction to the card.** The running build (merged 2026-09-02 08:33Z) is
   *after* CARD-0233 S3 (`92ccee13`, 08-29), so the 15:16:44Z `ChannelReplyLost` (`TurnIncomplete`)
   did produce the targeted "[Antiphon] A reply this chat was owed was never delivered: a matching prompt
   was recorded but no turn completed within 30 minutes." send to that Slack DM — top-level in the DM,
   because `ChannelSendOptions.ReplyHandle` landed only in `4e11756d` (09-03 00:43Z). "Got silence" is
   the incident's own wording, not an observed fact; §5 says how to confirm it on mav-ref. What is
   genuinely missing is a page to the **operator's** pager (the digest channels) when an always-on
   channel agent loses a reply, so the operator hears even when the asker's chat is not theirs and the
   app is closed. S3 adds that on the existing `WakeOnBlocked` / `WakeOnDecision` pattern — no new alert
   sink, no auto-restart (a stall is a decision, never an automatic kill).
5. **Point 3 — no server projection miscounts Slack silence as agent idleness.** `working` is
   transcript-derived (`IsWorkingAsync`: activity outranks the last turn end), `AgentActivityBadge` reads
   it, `PastExpectedIdle` is task-scoped, `AgentOutlivedTask` excludes channel-bound agents. The one
   surface that *reads* as a dead channel is the Channels catalog row: `LastMessageAt` / `LastAuthor` are
   stamped only from inbound (`ChatChannelService.cs:159-161`), and neither an agent reply nor a
   server-composed notice touches the row, so the page shows "Mike Ciechan · 6h ago" while the agent
   replied minutes ago. S4 stamps outbound. The human misreading in the incident came from the Slack
   thread itself, which S1 fixes at the source.
6. **#28 and #25 are not conflated.** #28 (CARD-0324, Grok sign-in screen) is *why* review `54ba9d37`
   failed — S1 is what makes that `[task … failed]` turn reach Slack. #25 (CARD-0281) is the provider
   wall; its S3 already sends a capacity notice and settles with `LossReason.ProviderCapacity`, and S3
   here deliberately does not page for that reason twice (§5).

## 2. Verified current-code facts (line refs at `2083129b`)

**The follow-up path today.** `OnTurnEndAsync` (`:140`) runs `DispatchAsync` (Channel rows), then
`DispatchFollowUpAsync` if the in-memory `_dispatched` watermark survives, then
`DispatchMachineTurnAttachmentsAsync` (`:996`). Gates in that method: newest Channel-origin
`ConversationKey` (channel-bound); latest TurnEnd → `FindOwningPromptAsync`; `ExtractTurnResponseAsync`
(the returned `MaxSeq` is discarded at `:1023`); bail on an API-error stub or empty text; gate 2 — the
owning prompt matches no Channel body; candidates are `Sent`, unclaimed rows with origin Delegation /
Check / System (`:1044-1047`); no match → return (Warning incident only when markers were present,
`:1053-1060`); implied bundle (CARD-0337 S3); exact `NO_REPLY` with no explicit markers → hold
(`:1067`); **no explicit and no implied → return, no claim** (`:1070-1071`); otherwise claim
(`:1093`), `PrepareReplyBody`, one `ChannelReply`, un-claim on produce failure. Scheduled-origin rows
(`QueuedMessageOrigin.Scheduled = 6`, CARD-0057, `ScheduleService.cs:300-306`) are not candidates.

**Triggers.** Dispatch runs on the TurnEnd flush (`AgentSessionRuntime.cs:467-473`) and on every
AssistantText arrival (`:275-276`), because Claude can write the stop marker before the text. The
main path handles text that lands after its send via `_dispatched` + `DispatchFollowUpAsync`
(`:335`, `:906`); the machine path has no equivalent, which S1 must provide.

**Note origins and cadence.** Completion notes (`done|failed|blocked|canceled`) are one enqueue
(`AgentTaskReplyService.cs:1505-1508`): `Origin = Delegation`, `ConversationKey = task:{RootTaskId}`,
`SourceTaskId = task.Id`, batched per root. Check notes are enqueued on **every** check unless
superseded (`AgentTaskCheckService.cs:176-180`), `Origin = Check`, cadence
`CheckMinIntervalMinutes` 5 → `CheckMaxIntervalMinutes` 60 ramp, `CheckMaxCount` 10 per task
(`DelegationSettings.cs:516-526`). System rows are `BootstrapBody` ("reply READY"),
`RestartResumeBody` / `RecoveryNoteBody` ("Reply NO_REPLY unless you have something for the user") and
CARD-0337 S5's Done-time note. Scheduled rows are a clock-driven prompt to a standing agent.

**Lost-reply plumbing.** `ReportLostAsync` (`:641`) → Error log → Critical `ChannelReplyLost`
incident via `RecordIncidentAsync` (`AgentSupervisorService.cs:295`, which also raises an `Alert` with
dedup key `supervisor:ChannelReplyLost:{agentId}`) → `NotifyOriginatingConversationsAsync` (`:745`)
for every reason except `Unroutable` and `ProviderCapacity` (`:731`). Surfaces: the alert pipeline
(`ChannelAlertRouter` — every `AlertMinSeverity` is null, so nothing routes); the attention feed
(`RecentCriticalIncident`, 24-hour window, grouped per agent and kind, `AttentionService.cs:1209-1285`);
`useAlertToasts.ts` (in-app toast for Error+, desktop `Notification` for Critical only when the tab is
open and hidden). Landing order on master: targeted notice `92ccee13` 2026-08-29; CARD-0250 machine
attachments `b2a5315f` 2026-08-30; notice `ReplyHandle` + Grok 402/403 stamping `4e11756d` 2026-09-03
00:43Z; CARD-0337 S1-S3 2026-09-03. The card's running build was merged 2026-09-02 08:33Z.

**The existing pager.** `AwayDigestHostedService` (`:17`) is gated on `Digest:Enabled`; each tick runs
`BlockedTaskNotifier` and `DecisionCardNotifier`, which send a "loud ping" to every
`ChatChannel.DigestEnabled` row through `ChatChannelService.SendAsync` and stamp a marker
(`AgentTaskEventType.HumanNotified`, `Card.DecisionNotifiedAt`) so a ping goes once. On this box no
channel is `DigestEnabled`; on mav-ref the bound Slack thread has `digestEnabled=false` (card).

**Idle.** `AgentService.cs:129` sets `working` from `IsWorkingAsync`; `AgentActivityBadge.tsx` shows
"Idle" only as "live session, not mid-turn". `ChatChannel.LastMessageAt/LastMessagePreview/LastAuthor`
are written in `UpsertFromInboundAsync` only (`ChatChannelService.cs:159-161`); `SendAsync`
(`:95-127`) and both dispatcher send sites touch no catalog column; `ChannelsPage.tsx:113-114`
renders `lastAuthor · relativeTime(lastMessageAt) · N msg`.

**Instruction sources and their pins.** `server/Bundles/orchestrator.md:53-61`
(`InstructionBundleTests.cs:246-247` pins two sentences); `ChannelPreamble.BuildPreset` delivery
sentence (`ChannelContractsTests.cs:31` pins it verbatim); `AgentWorkspaceProvisioner.cs:256-265`
(`AgentWorkspaceProvisionerTests.cs:265`); `DelegationReportFormatter.ReportingContract` one-line
attach caveat (unchanged here).

## 3. Point 1 — designs considered

**A — keep the boundary, fix the instructions again.** Rejected. The 2026-09-02 turns were written by
an orchestrator whose bundle already said "plain-text follow-ups without a marker are not delivered";
it wrote the status anyway, for nobody. CARD-0337 §3 A recorded the lesson: a rule the harness cannot
enforce is how this class recurs.

**B — opt-in `[[reply]]` marker on the turn.** Rejected as the primary fix, for the same reason as A:
it converts "forgot `[[attach:]]`" into "forgot `[[reply]]`". Noted because it is a five-line change
(one regex alternation in `ChannelContracts`, one `||` in the gate) if the operator prefers explicit
opt-in per turn; S1's setting can express that preference instead (§4.3).

**C — opt-out delivery for machine-triggered turns of Delegation / Check / Scheduled origin (chosen).**
The dispatcher already knows the moment (the note's turn ended), the conversation (newest Channel
key), the idempotency row (the injection's `ChannelReplySettledAt`) and the opt-out (`NO_REPLY`). The
turn text becomes a deliberate message the moment the instructions say it is delivered — which is the
same contract the main path has always run on. System origin stays marker-only: "READY" and
housekeeping must not reach a chat, and the CARD-0337 S5 note's answer carries markers anyway.

**D — auto-attach a status PDF so the existing exception fires.** Rejected. It renders two sentences
into a document a phone must open, and it bends CARD-0337's renderer (built for specs) into a
delivery trick.

**E — server-composed status push at task settlement, independent of the orchestrator's turn.**
Rejected for CARD-0337 §3 B's reasons: it arrives before the orchestrator's narrative, duplicates when
the orchestrator does speak, and bypasses the orchestrator's one legitimate control (reading the
report first, sending a bad result back for rework without showing it). The scheduled digest already
exists for a server-composed summary.

## 4. Design

### 4.1 S1 — `DispatchMachineTurnFollowUpAsync` (rename of `DispatchMachineTurnAttachmentsAsync`)

Same method, same gate order, three changes:

1. **Candidates gain `QueuedMessageOrigin.Scheduled`** (`:1044-1047`). A schedule is the operator's own
   choice to prompt a standing agent on a clock; on a channel-bound orchestrator it is the durable
   "periodic status" CARD-0281 §5 asked for (the Grok-owned 30-minute schedule that died with its
   process), and it has no other route to the chat.
2. **After the implied-bundle step, replace the "no explicit and no implied → return" (`:1070-1071`)
   with a text branch:**
   - exact `NO_REPLY` and no explicit markers → return without claiming (unchanged: hold the bundle);
   - explicit or implied attachments → today's path, unchanged;
   - else, if `MachineTurnTextDelivery` admits the matched rows' origin (§4.3) and `responseText` is
     non-empty → **claim the matched rows, send `responseText` as a text-only `ChannelReply`**
     (`Truncate`, `ClassifyKind`, same target resolution and catalog-row Warning as today), un-claim on
     produce failure;
   - else → return without claiming (a System-only turn with plain text, or delivery switched off) at
     Debug, never an incident: it is the chosen boundary, not a loss.
3. **Record the watermark so trailing text follows.** On a successful send, set
   `_dispatched[sessionId] = new DispatchedTurn(userPrompt.Sequence, maxTextSeq, [target])` — use the
   `MaxSeq` that `:1023` currently discards. `DispatchFollowUpAsync` then carries text that lands after
   the send (Claude's stop-marker-before-text ordering; a final sentence after a last tool call) to the
   same conversation, exactly as it does for the main path, including its `NO_REPLY` and API-stub
   rules. The main path overwrites the entry when it next settles a Channel row, which is the existing
   "last turn we replied for" semantics.

**Idempotency, unchanged in shape:** the injection row's `ChannelReplySettledAt` is claimed before the
produce. Re-triggers of the same turn (a late AssistantText, the closing TurnEnd, a reconnect backfill)
find the row claimed and stop. An AssistantText that arrives *before* its own TurnEnd is attributed to
the previous turn (whose row is already claimed) and is a no-op; the TurnEnd trigger then sends the
whole window. A TurnEnd that precedes its text sees empty `responseText`, does not claim, and the text's
arrival sends. A batched Delegation prompt matches every constituent row and all are claimed by the one
send.

**What S1 must never do** (CARD-0250 §4's list, still binding): settle, un-settle or match any
Channel-origin row; touch `PromptsMatch` / `Normalize`; publish a turn whose owning prompt matched
nothing machine-origin (operator turns stay silent). `ChannelBridgeTests` and
`ChannelReplyDurabilityTests` must pass unchanged; if one needs editing, the design was violated.

**Interaction with CARD-0337 S3:** implied attachments are computed before the branch; a docs task's
`[task done]` turn now sends text *and* bundle by the existing path, and the `DeliverableDeliveredAt`
stamp is untouched. Composes as CARD-0337 §8 predicted.

**The incident replayed under S1:** 23:30:52Z `[task 3f4a6029 done]` → the orchestrator's "CARD-0002
cleanup landed…" text reaches `mikeysbot-slack:D0B1VUH2EAK…` in the thread (bundle attached if S1-S3 of
CARD-0337 rendered one); 23:50:59Z `[task 15ed2644 done]` → "CARD-0003 implemented, 665 tests pass,
dispatching review"; 00:18:36Z `[check 27b19b2f #1]` → "review looping on claude-fable-5, canceling";
00:24:37Z `[task 54ba9d37 failed]` → "Grok review died on the sign-in screen (StoppedBeforeFirstPrompt);
choose another kind?" — and that question, `ClassifyKind = Question`, is answered by Mike's next Slack
message through the main path. Four Slack messages in an hour, then honest silence.

### 4.2 Text shape and length

Parity with the main path: the joined AssistantText of the turn, truncated at `MaxReplyChars` (4000).
Interim narration between tool calls rides along today on the main path and will here; the
instruction text (§4.4) asks for one or two lines. No harness prefix — the orchestrator's words are the
message, and a `[task …]` header in the chat would be Antiphon narrating instead of the agent.

### 4.3 Settings — the spam dial lives in configuration, not in code

`ChannelBridgeSettings.MachineTurnTextOrigins` : `List<QueuedMessageOrigin>`, default
`[Delegation, Check, Scheduled]`, validated to exclude `Channel` (the main path owns it), `Ui` and
`Supervision`. An empty list is today's behaviour (attachments only). An operator who finds check-in
chatter noisy on a given deployment removes `Check`; one who wants opt-in per turn sets the list empty
and uses design B later. `System` is accepted if explicitly listed, so a deployment that never
bootstraps with "READY" can opt in — the default excludes it.

### 4.4 S2 — instruction text (must land with or after S1, never before)

Two sources also carry CARD-0337 S6's pending rewrite. Whichever card lands its slice second owns the
merged paragraph; the text below is the merged version so either order produces it.

`server/Bundles/orchestrator.md`, replacing the channel-bound paragraph (`:53-61`):

> If you are channel-bound (Slack/Telegram), the chat sees two kinds of turn. (1) The turn that answers
> an inbound chat message — ending that turn settles the conversation. (2) Your reply to an Antiphon
> note — a `[task … done|failed|blocked|canceled]` report, a `[check …]` note, or a scheduled prompt —
> delivered as a follow-up to your most recent conversation, text and any `[[attach:]]` files, unless
> your whole reply is exactly `NO_REPLY`. Write those replies for the human: one or two lines on what
> changed, what happens next, and any question you need answered. Reply `NO_REPLY` to a check note
> that changes nothing. A bootstrap, restart or compaction note is never delivered unless it carries
> `[[attach:]]`. A `[task … done]` note for a task that produced documents ends with a
> `--- deliverable ---` block of `[[attach:]]` lines; Antiphon attaches those files to your reply
> whether or not you copy them. A delegate's own `[[attach:]]` reaches only you, as text. Prefer PDF
> for Slack/Telegram documents; naming a SHA or a path in prose sends nothing.

`ChannelPreamble.BuildPreset` delivery sentence (the exact text is a compatibility contract;
`ChannelContractsTests.cs:31` updates in the same slice):

> Your reply to each chat message — the final text of the turn that answers it — is delivered back to
> the originating chat, truncated at 4000 characters. Your reply to an Antiphon note (a task report, a
> check-in, a scheduled prompt) is delivered to your most recent conversation as a follow-up, text and
> any [[attach:]] files, unless the whole reply is exactly NO_REPLY. A turn started by anything else (a
> system note, someone typing in your terminal) is not delivered — except that a system-note turn which
> puts [[attach:]] on its own line is sent as a follow-up. Keep replies phone-sized. Use plain Markdown
> only — no tables. To say nothing this turn, reply with exactly NO_REPLY and nothing else.

`AgentWorkspaceProvisioner` channel section: add one sentence — "Your reply to a `[task …]`, `[check …]`
or scheduled note is delivered to the chat as a follow-up unless it is exactly `NO_REPLY`; a delegate's
own `[[attach:]]` is not — re-emit it or let the `--- deliverable ---` block do it." — keeping the
never-clobber caveat CARD-0250 §8 recorded (the PredictionMarkets workspace carries its own `CLAUDE.md`,
so the preamble is the vehicle there).

`InstructionBundleTests.cs:246-247`, `AgentWorkspaceProvisionerTests.cs:265` and the
`OrchestratorContract` pin in `DelegationUnitTests` update to the new sentences. The system-note bodies
(`RestartResumeBody`, `RecoveryNoteBody`) already say "Reply NO_REPLY unless you have something for
the user" — they stay as they are; with System excluded from the text origins the sentence is harmless
and still correct for the marker case.

## 5. Point 2 — `ChannelReplyLost` as a page, not only a row

### 5.1 What already reaches a human, and what does not

| Surface | Reaches | Gap |
|---|---|---|
| Targeted notice to the originating conversation (CARD-0233 decision 2) | the person who asked, ~30 min late (`PendingReplyTtlMinutes`) | not the operator when the asker is a family member or a colleague; top-level on Slack until `4e11756d` |
| Attention `RecentCriticalIncident` | whoever opens the app inside 24 h | expires, no acknowledgement, no push |
| `AlertRaised` toast / desktop notification | an open tab | nothing when the app is closed |
| Alert sinks (`AlertMinSeverity`) | nobody — every sink is null; filling one dumps every Critical into a chat (CARD-0233 §9) | — |
| Digest pings (`WakeOnBlocked`, `WakeOnDecision`) | the operator's `DigestEnabled` channels | `ChannelReplyLost` is not one of the wake conditions |

### 5.2 S3 — `IncidentPageNotifier` on the digest sweep

- **Predicate:** `AgentIncidents` rows with `Kind ∈ DigestSettings.WakeOnIncidentKinds` (default
  `[ChannelReplyLost]`), `Severity == Critical`, `HumanNotifiedAt == null`, `CreatedAt` inside 24 h,
  whose agent is `AlwaysOn` **and** has an enabled `ChatChannels.AgentId` binding — the card's
  "always-on channel agents", and the population whose owed replies are a real person's silence.
- **Send:** `AwayDigestFormatter.FormatIncidentPing(agentName, kind, message, failureReason,
  sinceUtc, settings)` — phone-sized: "🔕 PM-Orchestrator-Grok owed slack "…" a reply and never sent it
  (no turn completed within 30 minutes). 15:16 UTC." plus `PublicBaseUrl/agents/{id}` when configured —
  to every `DigestEnabled` channel through `ChatChannelService.SendAsync`, the `BlockedTaskNotifier`
  shape (send, then stamp; a failed send leaves the stamp unset for the next tick).
- **Dedupe:** new nullable `AgentIncident.HumanNotifiedAt` (one migration; backfill existing rows
  with `CreatedAt` so a deploy never pages history). Per row, not per (agent, kind): two lost replies
  an hour apart are two pages, which is what a pager is for; the dispatcher already groups one sweep's
  losses per (session, reason) into one incident.
- **Exclusions:** `FailureReason == ProviderCapacity` is not paged (CARD-0281 S3 already sent the
  capacity notice and the hold is on the attention feed as `ModelAvailabilityHold`); `Unroutable` is
  paged (the notice could not be sent anywhere, so this is the only human-facing signal).
- **Gating, stated honestly:** the sweep runs only when `Digest:Enabled` is true and at least one
  channel is `DigestEnabled` (`AwayDigestHostedService.cs:17`, `BlockedTaskNotifier.cs:28-30`). That
  is the operator's pager configuration, not a new switch; on mav-ref the operator must enable the
  digest and mark their own DM `DigestEnabled` for S3 (and the existing Blocked/Decision pings) to
  reach them. Documented in §8 as the deploy step.

### 5.3 Not built, with reasons

- **Auto-restart or re-type on `ChannelReplyLost`.** A stall is a detection/decision state
  (AGENTS.md); `TurnIncomplete` on an always-on agent is a wedge or a dead turn, and the wedge
  detectors (CARD-0292/0294) plus the page carry the decision to a human.
- **Acknowledgement-persisted incidents** ("stays on the feed until someone clears it"). Worth a card
  of its own: it needs an incident ack column, an attention action and a client control, and it
  changes every Critical kind's lifetime, not this one's. `HumanNotifiedAt` is not an ack and must not
  be read as one.
- **Lowering `PendingReplyTtlMinutes` in code.** It is per-deployment configuration; a Slack-facing
  orchestrator deployment can set 15. The 30-minute default stays.
- **Routing through `ChannelAlertRouter`.** CARD-0233 §9 and CARD-0281 §6 both rejected it; unchanged.

### 5.4 Two facts only mav-ref can confirm

1. Whether the 2026-09-02 ~16:16 BST notice landed in the DM: search that Slack DM for
   "[Antiphon] A reply this chat was owed" and the mav-ref server log for `ChannelReplyLost notice`
   (a `channel_disabled` or missing-catalog Warning would be the two ways it did not).
2. Whether `Digest:Enabled` is on there. If it is off, S3 pages nobody until it is on.

## 6. Point 3 — "no Slack inbound" versus "agent idle"

Nothing server-side derives agent idleness from channel traffic: `working` is
`IsWorkingAsync` (last activity outranks the last TurnEnd / restart / manual-compact boundary),
`PastExpectedIdle` is a task-estimate condition, `AgentOutlivedTask` explicitly excludes channel-bound
agents, and Herdr corroboration compares herdr's own status with the same transcript signal. A
channel-bound orchestrator that is idle at the prompt between notes *is* idle — waiting, not dead — and
the badge saying so is correct. The 00:25Z → 04:48Z gap in the incident was that state.

**S4 — stamp outbound on the catalog row.** Add `ChatChannel.LastReplyAt` and `LastReplyPreview` (one
migration, two columns; `ChatChannelDto` gains both), written by a single `ExecuteUpdateAsync` after a
successful produce in the three send sites (`DispatchAsync`, `DispatchMachineTurnFollowUpAsync`,
`ChatChannelService.SendAsync`) — no tracking, no change to inbound dedupe (`LastChannelMessageId`) or
to the list ordering. `ChannelsPage.tsx:113-114` adds "↩ {relativeTime(lastReplyAt)}" after the inbound
stamp. Also one sentence in `docs/ops-http.md` beside the `working` field: a channel-bound agent idle
between Antiphon notes is waiting; read `working` and the transcript, never the chat's silence.

## 7. Slices, tiers, verification

All server tests via `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0338/`
(forward slash), class filters as named; delete every `bin-c0338` directory afterwards.

| Slice | Change | Tier | Verify |
|---|---|---|---|
| S1 | `ChannelReplyDispatcher`: rename, Scheduled candidate, text branch, `_dispatched` watermark; `ChannelBridgeSettings.MachineTurnTextOrigins` + validator; `appsettings.json` | Coder (High) | new `ChannelMachineTurnTextTests` (below); `ChannelFollowUpAttachmentTests`, `ChannelReplyDurabilityTests`, `ChannelBridgeTests` unchanged |
| S2 | `orchestrator.md`, `ChannelPreamble`, `AgentWorkspaceProvisioner`; pinned-text tests | Coder (Low) | `InstructionBundleTests`, `ChannelContractsTests`, `AgentWorkspaceProvisionerTests`, `DelegationUnitTests` |
| S3 | `AgentIncident.HumanNotifiedAt` + migration; `IncidentPageNotifier`; `AwayDigestFormatter.FormatIncidentPing`; `DigestSettings.WakeOnIncidentKinds`; hosted-service call | Coder (Medium) | new `IncidentPageNotifierTests` beside the `BlockedTaskNotifier` tests |
| S4 | `ChatChannel.LastReplyAt/LastReplyPreview` + migration; three `ExecuteUpdateAsync` sites; DTO; `ChannelsPage` line; `ops-http.md` sentence | Coder (Low) | `ChannelBridgeTests` (stamp after main-path send), `ChannelMachineTurnTextTests` (stamp after follow-up), `ChatChannelServiceTests`; `scripts/test-client.ps1` for the page |
| S5 | Docs: `docs/session-runtime-invariants.md` (new gotcha under #54's family: machine-turn text delivery, its origin gate and opt-out), `docs/telegram.md` "What the chat sees" section, `docs/antiphon-api.md` (settings + `ChatChannel` fields + `HumanNotifiedAt`), `docs/messaging/build-your-own-gateway.md:268-275` (a third server-composed send) | Coder (Low) | — |

Order: S1 → S2 (same build or later, never before) → S3 → S4 → S5. S2 coordinates with CARD-0337 S6
(§4.4). S3 and S4 are independent of S1 and of each other.

### Tests to pin (red-first where marked)

1. **The incident shape, red-first (S1):** ack turn settles the only Channel row; a Delegation-origin
   Sent row `[task 15ed2644 done] …` and a turn whose AssistantText is "CARD-0003 implemented, 665 tests
   pass; review dispatched." with no markers → `OnTurnEndAsync` → a second reply to the same
   conversation carrying that text, `Attachments` empty, `ReplyHandle` = catalog handle, the Delegation
   row's `ChannelReplySettledAt` set, the Channel row untouched. Fails today at `:1070`.
2. **Check and Scheduled origins deliver; System does not** (parametrised): a `[check …]` turn and a
   scheduled-prompt turn each send once; a System row whose turn says "READY" sends nothing and claims
   nothing, and the same System row with a `[[attach:]]` still sends (CARD-0250 unchanged).
3. **`NO_REPLY` is silence:** exact token → nothing sent, row unclaimed; "Noted — NO_REPLY" (prose
   around it) is delivered, per `IsNoReply`.
4. **Idempotent and restart-safe:** two more `OnTurnEndAsync` and one `Restarted(h)` → still one
   follow-up.
5. **Trailing text follows:** after the send, insert a later AssistantText for the same turn →
   `OnTurnEndAsync` → a third reply with only the new text; an API-error stub in the trailing window
   → nothing, per `DispatchFollowUpAsync`'s rule.
6. **Stop marker before text:** TurnEnd first (empty window) → no claim; the AssistantText arrival →
   one send.
7. **Produce failure un-claims** so the next trigger sends once (`ToggleFailProducer`).
8. **Origins dial:** `MachineTurnTextOrigins = []` → the incident turn sends nothing and claims nothing
   while a marker turn still sends; the validator rejects `Channel`.
9. **Operator turn:** an unmatched human-shaped prompt with plain text → nothing sent, no incident (the
   Warning incident stays marker-only).
10. **Implied bundle plus text (S1 × CARD-0337 S3):** text and bundle in one reply, stamp set.
11. **Never-weaken:** `ChannelReplyDurabilityTests` (all of them), `ChannelBridgeTests`,
    `ChannelFollowUpAttachmentTests` pass unchanged.
12. **S3:** Critical `ChannelReplyLost` on an AlwaysOn channel-bound agent → one ping to each
    `DigestEnabled` channel, `HumanNotifiedAt` set; a second tick sends nothing; a non-AlwaysOn agent,
    a `ProviderCapacity` reason, a Warning-severity row and a row older than 24 h each send nothing; a
    failed send leaves the stamp null and the next tick retries; no `DigestEnabled` channel → nothing
    and nothing stamped.
13. **S4:** after a main-path send the catalog row's `LastReplyAt` is set and `LastMessageAt` /
    `LastAuthor` / `LastChannelMessageId` are unchanged; same after a follow-up and after `SendAsync`.
14. **Text pins (S2):** the three sources and `ChannelContractsTests.cs:31`.

## 8. Operator decisions and deploy notes

1. **`Check` in the default origins.** Recommended **yes**: the incident's most useful turn was a
   check-note reply ("review looping, canceled"), the instruction says `NO_REPLY` when nothing changed,
   and `MachineTurnTextOrigins` is the per-deployment dial. Say if you would rather ship `[Delegation,
   Scheduled]` and let deployments opt into Check.
2. **Your pager on mav-ref.** S3 pages `DigestEnabled` channels under `Digest:Enabled`. Decide which
   conversation is your pager there (the DM with the orchestrator, or a separate ops DM) and set both
   flags at deploy; otherwise S3, `WakeOnBlocked` and `WakeOnDecision` all page nobody.
3. **`PendingReplyTtlMinutes` on Slack-facing deployments.** Configuration only; 15 is reasonable for an
   orchestrator whose asker is the operator. No code change proposed.

## 9. Relationship to open work

- **CARD-0337 (Review).** S3 shipped and composes (§4.1). S5 (Done-time System note) and S6
  (instruction text) are pending; S2 here and S6 there rewrite the same paragraph — merged text in §4.4.
- **CARD-0324 / #28.** The Grok sign-in death is the *content* of the `[task … failed]` turn S1
  delivers; nothing here touches launch or `ProviderSignInRequired`.
- **CARD-0281 / #25.** Its S3 notice and `ProviderCapacity` settlement stand; S3 here excludes that
  reason from paging. Its "durable status schedule" ask is met by Scheduled-origin text delivery.
- **CARD-0245 idea 6 / CARD-0233.** No Channel-row match or settle changes; the TTL classifier is
  untouched.
- **CARD-0057.** Schedules become the periodic-status vehicle for channel-bound agents with no
  schedule-side change.
- **CARD-0036.** The digest sweep gains a third notifier; the digest body is unchanged.

## 10. Out of scope

- A `POST /api/channels/{id}/send` megaphone (CARD-0171, unchanged).
- Delivering System-origin plain text by default (§3), or a per-agent delivery toggle (the token and
  the origins list are the controls; add per-agent only if a real deployment needs two policies).
- Acknowledgement-persisted incidents (§5.3).
- A sub-orchestrator whose root is channel-bound (CARD-0337 §9's gap; unchanged here).
- Rate-limiting or coalescing follow-ups across turns. Delegation notes already batch per root at the
  queue; if check chatter proves noisy in practice the dial is §4.3, and a per-conversation
  minimum interval is the next step, not this one.

## 11. Files to change

| File | Slice | Change |
|---|---|---|
| `server/Application/Services/ChannelReplyDispatcher.cs` | S1, S4 | rename; Scheduled candidate; text branch; watermark; outbound stamp |
| `server/Application/Settings/ChannelBridgeSettings.cs` (+ validator, `appsettings.json`) | S1 | `MachineTurnTextOrigins` |
| `server/Bundles/orchestrator.md`, `ChannelPreamble.cs`, `AgentWorkspaceProvisioner.cs` | S2 | merged channel-bound text |
| `server/Domain/Entities/AgentIncident.cs` + `AppDbContext` + migration | S3 | `HumanNotifiedAt` |
| `server/Application/Services/IncidentPageNotifier.cs` (new), `AwayDigestFormatter.cs`, `Settings/DigestSettings.cs`, `Infrastructure/Supervision/AwayDigestHostedService.cs`, `Program.cs` | S3 | the page |
| `server/Domain/Entities/ChatChannel.cs` + `AppDbContext` + migration, `ChatChannelDtos.cs`, `ChatChannelService.cs` | S4 | `LastReplyAt`, `LastReplyPreview` |
| `client/src/api/channels.ts`, `client/src/features/channels/ChannelsPage.tsx` | S4 | reply stamp |
| `docs/session-runtime-invariants.md`, `docs/telegram.md`, `docs/antiphon-api.md`, `docs/ops-http.md`, `docs/messaging/build-your-own-gateway.md` | S4/S5 | owners |
| tests per §7 | all | new `ChannelMachineTurnTextTests`, `IncidentPageNotifierTests`; four pinned-text suites; `ChannelBridgeTests` / `ChatChannelServiceTests` stamps |
