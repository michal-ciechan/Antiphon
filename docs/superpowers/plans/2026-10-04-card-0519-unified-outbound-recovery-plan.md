# CARD-0519 — make every agent reply recoverable through the existing outbound path

Plan task `64b6e881`, 2026-10-04. Inspected source:
`1e31612b2735c502cd1c659a94dfe510d77a3ed5` (the assigned master baseline).
Input: full Investigate result `a6e21da6-a689-4b34-a174-ae9e0134172f`, retrieved
through `GET /api/agent-tasks/a6e21da6`. Earlier implementation
`e30eb42aa0d13d16b07668fe51d38c546d57b7fb` is reference material only.

**Complexity: hard. Next: TestDesign, separately dispatched.** This is a change to
durable ownership across database, filesystem and producer boundaries, with
historical discovery and retention implications. The implementation is about
12–15 hours in small Code slices, plus checkpoint runtime and separate Review and
post-land Mutation. The cheaper direct-route-only patch leaves preparation,
discovery and trailing-text losses open. No code, migration or verification result
is delivered by this Plan. The checkpoint allocation below is input to TestDesign,
not an executable verification manifest or permission to skip that stage.

## Outcome and boundary

For main, trailing and eligible machine replies, retain either a recoverable
obligation, committed producer acceptance, deliberate policy suppression, or a
durable visible failure. A crash or refused send must never make an owed reply
disappear merely because its source was marked settled. Recovery runs without a
new user message or completed turn.

Extend `ChannelOutboundDelivery` / `ChannelOutboundService` /
`ChannelOutboundDeliveryPump`. All **agent replies** use that path. Optional
conversion remains a step in it. Server-composed control/proactive notices retain
their existing direct route and are outside this card's durability guarantee.
Publication means Kafka producer acceptance, not Telegram/Slack receipt. There is
no exactly-once claim across broker acceptance and the database outcome commit.

## Ground truth

Paths in this table are repository-relative. D =
`server/Application/Services/ChannelReplyDispatcher.cs`; S =
`server/Application/Services/ChannelOutboundService.cs`; P =
`server/Application/Services/ChannelOutboundDeliveryPump.cs`.

| Card/earlier-plan assumption | What inspected master does | Planning consequence |
|---|---|---|
| Every dispatcher reply needs a new publication journal. | S:106–126 sends directly unless conversion qualifies or an older unresolved delivery exists. Admitted replies already have `ChannelOutboundDeliveries`. | Make admission universal for agent replies; extend this journal. |
| Marking a source settled claims a send safely. | S:284–290 saves settlement before producer entry. Death between them leaves neither a delivery nor an open correlation. | Settlement is an outcome, never ownership. |
| Exception reopening closes the gap. | S:293–299 reopens with `CancellationToken.None`; process death and reopening-save failure defeat it. | Remove the reopen protocol from agent publication. |
| Preparation is covered by the queued journal. | D:410/1357 reads attachments before admission; S:143 stages files before inserting a row. | Capture a recoverable intent before fallible preparation. |
| Pump recovery finds every owed answer. | P:55–62 scans existing delivery rows only; D:265–279 reads the newest completed turn. | Add historical source discovery to the same hosted recovery cycle. |
| A source-key hash owns any overlapping text window. | S hashes both ends of an exact interval. D:1158 advances an in-memory trailing watermark before preparation; a different overlapping interval has a different key. | Add durable root/cursor ownership under the admission transaction. |
| Batch sources require a second membership table. | `SessionQueuedMessage.ChannelOutboundDeliveryId` already links all main/machine sources to one delivery. | Keep that relation and conditionally assign every member; trailing records refer to their root. |
| Retry of an unknown result is automatic. | S:47–62 requires possible-duplicate acknowledgement; P:96–102 makes an expired Publishing lease uncertain. | Preserve explicit uncertain retry. Do not port automatic Unknown retry. |
| A frozen destination authorizes sending after rebinding. | P:366 onward holds disabled/rebound/project-changed bindings; Held/Uncertain heads block later replies. | Freeze identity and also revalidate authorization. |
| Target A is retried when B fails. | D:466–475 already reopens only B; queued targets have independent records. | Retain this behavior and regression evidence. |
| Metadata is independently repairable. | P:344–358 puts catalog/bundle work in the acceptance transaction. Failure leaves Publishing, later Uncertain. Direct code avoids reopening after known acceptance but has no durable metadata repair. | Commit acceptance/source settlement first; repair projections from frozen accepted data. |
| Logging or a best-effort incident is a terminal outcome. | D:380–381 and 706–719 settle before recording loss. D:841–955 uses another scope and cannot record an unowned session. | Commit terminal loss plus incident/alert together; owner may be null. |
| Retention preserves recovery evidence. | `DataRetentionService.cs`:186–205,246–259,343–355 lacks outbound protections; machine queue rows can prune independently. | Protect discovery candidates and unresolved delivery/root evidence. |
| A real size refusal is uncertainty. | Kafka producer passes `MsgSizeTooLarge` through; P catches it as generic uncertainty. | Classify proven size refusal without automatic uncertain replay. |
| The old migration/snapshot can be reused. | Latest inspected migration is `20261004011911_CompletedCardWorktreeCleanup`; master has the CARD-0418 profile/delivery migrations and a newer snapshot. | Generate a fresh incremental migration on the Code checkout's actual latest master. |
| Old green checkpoints qualify this integration. | Earlier branch reported CP-1..5 = 71/18/3/99/40 passing; CP-6 remains owed. Current Herdr parity method explicitly skips Linux. | Historical evidence only; new manifest and fresh receipts. |

## Decisions

These are implementation decisions within the brief, not outstanding operator
choices. No product approval is needed before TestDesign. Numeric settings below
are proposed bounded implementation defaults for that stage to qualify.

- **D-1 — one publication owner.** Extend the existing delivery, file store, pump,
  attention projection and retry/resume endpoints. Remove agent use of
  `PublishDirectAsync` and the dispatcher's optional-service/legacy-send fallbacks.
  Resolve the outbound service as required in production and fixtures. Reject the
  earlier `ChannelOutboundPublications`, `ChannelOutboundPublicationSources`,
  publication worker and parallel producer path: they duplicate ownership and
  bypass CARD-0418 conversion and binding rules.
- **D-2 — capture before preparation.** Append a `Captured` state without changing
  existing persisted enum ordinals. Commit the source turn, text interval, original
  response, target/native handle, original binding, source/member/task IDs and
  preparation descriptors before attachment reads, prompt-file reads or staging.
  This is the same delivery row later converted/published, not a second outbox.
  Crash before capture commit leaves discoverable sources; crash after commit
  leaves a Captured row. A source is not settled in either case.
- **D-3 — immutable after staging.** Reuse the existing frozen reply/files/hashes.
  Before the first successful stage, retain original response and path/task
  descriptors and retry their preparation; file bytes are frozen at successful
  staging, not retroactively at transcript ingestion. After staging, recovery must
  use those bytes and captured route. Recognize a fully staged, validated directory
  after a crash before its DB transition; do not overwrite it with current files.
  Invalid/partial artifacts are never publication evidence. Reject rebuilding a
  Ready reply from today's transcript, profile, catalog or attachments.
- **D-4 — durable interval ownership.** Main/machine deliveries are roots; roots
  own a versioned last-reserved text sequence. Trailing deliveries reference the
  root. Reserve the next disjoint interval and advance the cursor in the same DB
  transaction. A failed preparation owns its interval already, so new tails begin
  after it. Use the destination admission lock plus root version/conditional
  source assignment, not `_dispatched`, for correctness. Keep an in-memory cache
  only as an optional hint. Exact SourceKey uniqueness alone is insufficient.
- **D-5 — discover original turns.** Add a concrete discovery service invoked by
  the existing hosted service on startup and each bounded cycle. Event dispatch
  and discovery share capture/turn-policy code. Search historical owning prompts
  for open Channel and eligible Delegation/Check/System/Scheduled rows, using the
  complete current matcher, delivery floors and next-prompt window. Do not call
  newest-turn-only `OnTurnEndAsync` in a loop. Machine routing must use channel
  context established at/before the injection, never a later chat to authorize
  replay of pre-chat machine output. Native handles come from inbound evidence.
- **D-6 — uncertainty stays manual.** Publishing is committed before entering the
  producer. Cancellation/timeout after entry, unclassified producer faults, lost
  outcome commits and expired Publishing leases become `PublishUncertain`; there
  is no automatic send from that state, including after a pre-entry crash whose
  durable evidence cannot establish non-entry. Keep the existing acknowledgement
  gate. An explicit retry may duplicate a prior accepted send and authorizes a new
  bounded attempt budget; it does not reset the lifetime attempt counter.
- **D-7 — finite definite-refusal recovery.** Replace the in-call queue-full loop
  with persisted due-time retries. Only documented definite non-acceptance is
  retryable automatically: Local_QueueFull gets at most three attempts per
  authorization, 30 seconds apart. MsgSizeTooLarge and local payload-cap refusal
  are definite terminal failures of unchanged bytes, not useful immediate retries.
  Unknown errors are not inferred safe from a test double's accepted count.
  Preparation retries have a separate three-attempt cap and the existing
  `PendingReplyTtlMinutes` age bound from the original obligation. Exhaustion is
  Failed with durable loss evidence; do not redefine Held as retry exhaustion.
- **D-8 — keep CARD-0418 policy.** Conversion qualification, MaxPending,
  converter serialization, frozen prompt revision, deadline/fallback behavior,
  source completeness and profile revocation remain in the current path. Captured
  replies participate in channel ordering before conversion starts. Preserve the
  existing `(CreatedAt, Id)` ordering and destination lock. Held and Uncertain
  block later replies; Failed is a visible terminal outcome and releases the lane
  as today; the new Suppressed state is also terminal for ordering. ResumeHeld
  revalidates the original binding and resumes the correct
  preparation/conversion phase. A missing catalog row cannot use an unchecked
  direct send: record an atomic unroutable loss with its source identity. Do not
  fabricate a catalog/binding during recovery.
- **D-9 — durable loss reporting.** Failed/Uncertain transitions commit their
  Critical ChannelReplyLost incident, Alert and durable episode/dedupe stamp in
  the same transaction. Binding Held retains its existing attention semantics.
  Incident failure cannot clear the obligation or permit a send beyond its budget.
  TTL/unroutable/terminal-provider source settlement also shares a transaction
  with its loss record. Use captured owner identity when available; a missing or
  deleted owner permits nullable AgentId, never log-only settlement. Chat notices
  and event routing run after commit and cannot gate it. Do not recursively admit
  those control notices as agent obligations.
- **D-10 — separate accepted outcome from projections.** Commit Published,
  PublishedAt and all main/machine source settlement together, with the current
  version/lease fence. Then repair LastReplyAt/preview and eligible bundle stamps
  idempotently using the accepted frozen payload. Track repair completion on the
  delivery. A catalog/filesystem/bundle-save failure cannot resend a Published
  reply or erase acceptance. An acceptance-transaction failure remains Uncertain.
  Reject a metadata error sharing the producer-retry catch.
- **D-11 — preserve policy and conservative history.** Preserve full-turn
  NO_REPLY, API-error withholding, attachment markers and MachineTurnTextOrigins.
  Main intentional silence may have a Suppressed root (append enum value) so
  later legitimate fragments retain durable routing; PublishedAt stays null.
  Machine NO_REPLY/disallowed text retains its existing no-publication/no-source-
  settlement behavior. Existing settled rows without delivery evidence are never
  replayed or assigned invented publication timestamps. Existing admitted
  deliveries retain their states/payloads; do not infer old trailing obligations
  from ambiguous historical direct sends. Recover old unsettled eligible sources.
- **D-12 — retain evidence, not accidental immortality.** Share discovery
  eligibility predicates with retention. Protect potentially owed sources,
  sessions/transcripts and referenced task/bundle evidence until classified;
  protect Captured/active/Held/Uncertain/Failed deliveries, incomplete metadata
  repair and open root tail windows. A conclusively ineligible machine turn is
  not an outstanding reply forever. Published/Suppressed roots become ordinarily
  prunable after their next-prompt window is fully examined, or after their
  terminal session has a complete persisted transcript and no possible tail.
  Frozen unresolved files and incident dedupe stamps outlive incident-history
  pruning. Do not add a broad filesystem cleanup job in this card.
- **D-13 — bounded worker, existing host.** Keep one hosted recovery loop. Proposed
  typed settings in ChannelOutbound: scan 5s (current cadence), page size 32,
  maximum 10 pages per cycle, retry delay 30s, send timeout 30s, lease 300s
  (current lease), preparation/publication attempt limits 3. Validate finite
  positive ranges and timeout < lease. Use TimeProvider. Discovery, due sends and
  metadata repair each get a bounded share so withheld prefixes/idle roots cannot
  starve later work; carry keyset cursors across ticks and wrap fairly. Cursors
  may be process-local scheduling hints because rows carry ownership. A process
  restart must not require those cursors to recover an obligation.
- **D-14 — native work has an OS lane, not a host pin.** TestDesign and portable
  Code/checkpoints omit `-Runner` and omit `-Platform`. The native parity
  checkpoint uses `-Platform Windows`, no host id. A skipped Linux run is not
  evidence. Reject the old whole-Herdr-class Linux checkpoint and whole-Unit runs.

## Data and transition design

Add fields to `ChannelOutboundDelivery` rather than importing either old table:

| Addition | Purpose/constraints |
|---|---|
| `CaptureJson` (nullable text, versioned bounded DTO) | Original response, target/handle, preparation/profile descriptors and all source task identities. Null on pre-upgrade rows. No attachment byte duplication in DB. Reject malformed/unsupported capture versions visibly. |
| `RootDeliveryId`, `ReservedThroughSequence`, `TailClosedAt` | Nullable self-reference with restrictive deletion; root cursor and terminal scan marker. Existing rows default null. New roots initialize to their captured interval end; children refer to root. |
| `NextAttemptAt`, `PreparationAttempts`, `PreparationDeadlineAt` | Persisted preparation/publication due time and independent preparation budget. The conversion `DeadlineAt` remains a conversion deadline, not a retry clock. |
| `PublicationAttemptBudgetBase` | Lifetime PublicationAttempts minus this base counts attempts in an explicit authorization. Only initial admission or acknowledged retry establishes a budget. |
| `MetadataAppliedAt` | Published rows lacking this marker are projection-repair work, never send candidates. Initialize historical Published rows conservatively as already applied. |
| `FailureEpisode`, `FailureReportedEpisode` (or equivalently durable episode stamp) | One loss record per failure episode, independent of incident-table retention. Explicit authorized recovery can start a new episode; periodic scans cannot. |

InputPath/InputSha256 may be empty before materialization, including a resulting
Held/Failed state, and for Suppressed; they must be present and validated in every
sendable state. ResumeHeld returns an unmaterialized captured row to Captured,
not Ready. Keep the current source FK and delivery SourceKey index.
Add a root lookup index `(SourceSessionId, PromptSequence, ChannelId)`, a unique
root identity on those columns filtered to `CaptureJson IS NOT NULL`,
`RootDeliveryId IS NULL` and main/machine SendKind, a unique trailing start
identity `(RootDeliveryId, FirstTextSequence)`, and due-work indexes matching actual
queries. A new root's identity is independent of the response's current last text
sequence. Main/machine precedence and conditional source claims prevent competing
roots for the same source.

Add nullable `SessionQueuedMessage.ChannelReplyDiscoveryClosedAt` for permanently
ineligible machine sources. It is a discovery/retention marker, not settlement or
publication. Set it only after the complete historical prompt window is capped
by a next prompt (or terminal, fully ingested session) and policy proves there is
no owed reply. Before that boundary, absent attachments/NO_REPLY can still gain
late text and must be revisited. A missing later chat is never permission to replay
pre-chat history. Channel rows still owed an answer cannot use this marker to
escape TTL/loss recording. Migration leaves the marker null; discovery classifies
legacy open rows. TestDesign must include the premature-close/late-text control.

Root reservation happens under the destination advisory transaction lock, then
the root's version fence. Reload queue members in that transaction; never replace
an existing ChannelOutboundDeliveryId. An existing owner is returned without
repreparing its payload. For each target, source linkage, intent insertion and
cursor advancement are one commit. Follow-up rows never clear or reassign the
root's settled sources. A later prompt caps the old range; it does not discard an
already reserved fragment. Policy-suppressed fragments also advance the cursor
transactionally, with no false Published timestamp.

Preparation is a short leased pump step. Split pure extraction/capture from file
I/O into a concrete `ChannelReplyPreparation` helper; a narrow attachment-reader
I/O seam is justified for failure injection. The file store must validate and
adopt an already complete snapshot for the same delivery after restart. Commit
its paths/hashes before Ready/Pending. Bind a completed staged manifest to the
delivery's capture identity/hash and validate all retained file hashes; the mere
existence of its final directory is not sufficient. Evaluate the frozen conversion policy,
using existing qualification and admission limits; retain existing revocation
checks immediately before converter creation and publication. An expired lease
in Captured is safely preparable again; an expired Publishing lease is not.

```text
open durable source / open root tail
    -> Captured -> Pending -> Converting -> Ready -> Publishing -> Published
           |          existing conversion/fallback --^              |
           +-> Ready (passthrough)                         metadata repair
           +-> Suppressed (intentional main/tail silence only)

Captured/Ready -> Held (binding); resume -> correct prior preparation phase
Publishing + definite queue refusal -> Ready at persisted due time, or Failed
Publishing + unknown outcome / expired lease -> PublishUncertain
PublishUncertain + explicit duplicate acknowledgement -> Ready, new budget
irrecoverable preparation / size refusal / exhausted budget -> Failed + loss record
```

Persist a producer attempt and increment its lifetime count in one fenced commit
before I/O; use a linked send deadline shorter than its lease. Never hold a DB
transaction across producer or filesystem work. Recheck binding and current
lease/version immediately before producer entry. On a durable retryable refusal,
save Ready/NextAttemptAt and the refusal before releasing the lease. If that save
fails, Publishing remains the conservative crash evidence. Source discovery and
TTL processing must serialize with capture, recheck its predicates and prefer
capturing an existing answer over declaring it lost merely because it is old.
If the discovery page budget has not examined a source's possible historical
answer, defer its TTL verdict until that examination completes. A spent retry
budget permits failure-recording repair only, not another preparation/send call.

Projection repair must not regress a channel preview behind a later Published
delivery. Compare accepted timestamps/order when updating it. Stamp every eligible
implied bundle only if the actual accepted attachments satisfy the existing
completeness rules. Capture the evidence needed for repair before publication so
changed/deleted current files cannot invent or invalidate an accepted payload.

## Earlier implementation reconciliation

| Earlier design element | Disposition |
|---|---|
| Durable original response, discovery, bounded retries, tail reservation, retention, atomic failure and projection repair | Still needed; port their invariants and useful fixtures to the existing delivery model. |
| Publications + PublicationSources tables, separate journal service/worker, serialized attachment bytes in DB | Superseded by extended delivery/source FK, existing hosted pump and durable file store. Do not introduce or migrate them. |
| Automatic retry after Unknown/expired Publishing, frozen-address sends despite changed binding | Conflicts with current manual uncertainty and binding holds; rejected. |
| Dispatcher routes bypassing conversion; machine target from today's catalog | Conflicts with CARD-0418 and inbound native handle preservation; rejected. |
| Per-target result, unique source key, lease/version claim, staged hashes, attention, resume/retry endpoints | Reuse master's implementations; strengthen only where capture, tails, retry accounting or repair require it. |
| `20260928081254_AddChannelOutboundPublications`, Designer, old snapshot and test project changes | Obsolete integration artifacts. Do not cherry-pick. Reuse existing ChannelOutbound.Probe project/reference rather than adding a competing crash worker. |
| Old V-1..V-22 and 52-PC list | Acceptance checklist/reference only. TestDesign maps every item to retained behavior, superseded behavior or an exact new test. Unknown-retry PCs must be replaced, not carried forward with incorrect expectations. |

## Migration and release boundary

Generate `<fresh timestamp>_ExtendChannelOutboundRecovery` using the repository's
EF CLI against the Code checkout's current model/snapshot. At this baseline the
predecessor is `20261004011911_CompletedCardWorktreeCleanup`; re-read that ordering
before generation. Run EF/build drivers through `scripts/build-slot.ps1`, with a
task-owned forward-slash `bin-.../` output. Do not edit old migrations or hand-copy
the earlier Designer/snapshot. Review the generated delta for unrelated schema
changes and preserve all CARD-0418 mappings, FKs, enum values and existing data.

Upgrade tests must start at the actual predecessor with existing Pending,
Converting, Ready, Publishing, Published, Held, Failed and Uncertain deliveries,
plus old settled/unsettled queue rows. New nullable fields leave their behavior
intact; existing Published metadata is not reminted. Do not backfill new roots for
historical settled direct replies or retroactively rediscover their tails. Check
an empty-database migrate and predecessor upgrade in isolated Postgres schemas.
Rollback is safe only before new-state rows are admitted; a binary that cannot
interpret Captured/Suppressed must not be activated over those rows. Deployment
is a later operator/orchestrator action, not part of this Plan task.

## Implementation slices

Each slice is one 30–60 minute Code unit, committed and pushed before its named
checkpoint. Estimates exclude waiting for slots and checkpoint runtime. If a
slice exceeds an hour, split its named responsibilities before starting another
broad edit. References to new test classes are proposed names for TestDesign to
finalize. All application tests below are under
`tests/Antiphon.Tests/Application/` unless another path is stated.

| Slice / minutes | Files and concrete result | Tests/checkpoints |
|---|---|---|
| S1 / 60 | `server/Domain/Entities/ChannelOutboundDelivery.cs`, enum, `SessionQueuedMessage.cs`; `Infrastructure/Data/AppDbContext.cs`, CLI-generated migration/Designer/snapshot. Add capture/root/retry/repair data, discovery closure, indexes and compatibility defaults. | New `ChannelOutboundDurabilitySchemaTests.cs`; CP-1. |
| S2 / 60 | `ChannelOutboundService.cs`, new `ChannelReplyPreparation.cs`, narrow attachment-reader seam, dispatcher pure turn descriptor extraction. Transactional root capture and conditional membership; no producer or file reads before capture. Keep wiring dormant until S4. | New `ChannelOutboundCaptureTests.cs`; CP-2. |
| S3 / 60 | `ChannelOutboundDeliveryPump.cs`, preparation helper, `IChannelOutboundFileStore.cs`, `Infrastructure/Files/ChannelOutboundFileStore.cs`. Leased Captured materialization, complete-snapshot adoption, frozen policy and passthrough/conversion transition. | New `ChannelOutboundMaterializationTests.cs`, existing Storage/SendShape tests; CP-3. |
| S4 / 60 | `ChannelReplyDispatcher.cs`, `ChannelOutboundService.cs`, `server/Program.cs`, `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs`. Require the service, switch main and machine capture, remove agent direct/legacy paths, explicitly drain real pump in fixtures. Keep source settlements as outcomes. | `ChannelOutboundDispatchIntegrationTests.cs`, new `ChannelOutboundUnifiedPathTests.cs`, `AgentTaskReplyIntegrationTests.ChannelOutboundRuntime.cs`; CP-4, CP-21. |
| S5 / 60 | New `ChannelOutboundDiscoveryService.cs`, dispatcher historical turn helper, `Infrastructure/Supervision/ChannelOutboundHostedService.cs`, Program registration. Startup/timer source discovery, bounded fair pages, correct old owning prompt and machine eligibility. | New `ChannelOutboundDiscoveryTests.cs`; CP-5. |
| S6 / 60 | Dispatcher follow-up path, discovery/root queries, service reservation. Persist per-target trailing cursor and suppressed main/tail handling; remove `_dispatched` as an owner; cap windows at the next prompt. | New `ChannelOutboundTrailingRecoveryTests.cs`; CP-6. |
| S7 / 60 | Pump plus `ChannelOutboundSettings.cs`/validator; narrow producer error classifier if needed. Persist due times/budgets; bound send by TimeProvider deadline; preserve explicit uncertain retry and binding resume semantics. | New `ChannelOutboundRetryPolicyTests.cs`, existing targeted Recovery tests; CP-7, CP-8. |
| S8 / 60 | New concrete `ChannelOutboundFailureRecorder.cs`, pump, dispatcher terminal/TTL paths, existing incident/alert persistence seams. Atomic loss/episode dedupe, nullable owner, best-effort notices after commit. | New `ChannelOutboundFailureRecordingTests.cs`; CP-9. |
| S9 / 60 | Pump acceptance transaction plus projection-repair helper, attention detail if needed. Fence settlement, repair catalog/all applicable bundle metadata without a producer call; preserve actual-attachment completeness. | New `ChannelOutboundMetadataRepairTests.cs`; existing Publication partial; CP-10. |
| S10 / 60 | `DataRetentionService.cs`, shared discovery eligibility/classification. Protect sessions/transcripts/queue/task bundle evidence and root windows; recheck under existing transcript mutation lock. | New `ChannelOutboundRetentionTests.cs`, exact existing retention methods; CP-11, CP-12. |
| S11a / 60 | Bridge fixture and `ChannelBridgeTests.cs`, `ChannelReplyDurabilityTests.cs`, prompt-correlation tests. Replace synchronous-send expectations with explicit capture/pump/outcome assertions; keep original routing oracles. | CP-13, CP-14. |
| S11b / 60 | `ChannelMachineTurnTextTests.cs`, `ChannelFollowUpAttachmentTests.cs`, `ChannelOutboundGateTests.cs`, policy/integration fixtures. Adapt to the same required pipeline and preserve withholding/attachments; no test-only legacy send bypass. | CP-15, CP-16. |
| S12a / 60 | `tests/Antiphon.ChannelOutbound.Probe/Program.cs`, existing probe copy target only if needed, new `ChannelOutboundUnifiedCrashTests.cs`. Extend owned child protocol for pre-capture, captured, staging, attempt and outcome deaths; include no-new-event restart. | CP-17. |
| S12b / 60 | New `ChannelOutboundUnifiedTransportTests.cs`, reuse isolated broker/adapter fixture and existing recovery fixture. Real refusal, accepted-before-result and durable consumer receipt; preserve conversion/holds/order. | CP-18, CP-19. |
| S13 / 45 | `HerdrAlwaysOnChannelParityTests.cs` fixture only as needed, `docs/telegram.md`, relevant source/entity comments. Document universal agent admission, manual uncertainty, loss visibility, metadata and retention. | CP-20 on Windows. |

Do not activate an intermediate schema-only or partly wired slice. The slices
are reviewable commits; the deployable unit includes discovery, failure handling,
retention and the complete verification manifest. S11 changes may move beside the
earlier slice that first needs them, with checkpoint `After` updated by TestDesign.

### Checkpoint allocation for TestDesign

Each build below is `tests/Antiphon.Tests` into its own `bin-c519-cpNN/`. Each row
is one exact filter and names its lane. Combined-class pipes shown here are the
literal TUnit expression (escaped for Markdown). Native/process rows are serial;
portable database rows use isolated schemas. TestDesign must expand the rosters,
set real result-count floors and create the owner's executable `### Checkpoints`
table under `## Verification design` before Code. Do not infer counts from these
time estimates. Split any class group over 120 methods or an estimated ten-minute
row; never replace it with Unit/assembly/namespace execution.

| CP | After | Lane | Exact proposed filter | Minutes, including build |
|---|---|---|---|---:|
| CP-1 | S1 | Portable PostgreSQL | `/*/*/ChannelOutboundDurabilitySchemaTests/*` | 4 |
| CP-2 | S2 | Portable PostgreSQL | `/*/*/ChannelOutboundCaptureTests/*` | 4 |
| CP-3 | S3 | Portable PostgreSQL/files | `/*/*/(ChannelOutboundMaterializationTests*)\|(ChannelOutboundStorageTests*)\|(ChannelOutboundSendShapeTests*)/*` | 6 |
| CP-4 | S4 | Portable PostgreSQL/runtime | `/*/*/(ChannelOutboundUnifiedPathTests*)\|(ChannelOutboundDispatchIntegrationTests*)/*` | 5 |
| CP-5 | S5 | Portable PostgreSQL/clock | `/*/*/ChannelOutboundDiscoveryTests/*` | 5 |
| CP-6 | S6 | Portable PostgreSQL | `/*/*/ChannelOutboundTrailingRecoveryTests/*` | 5 |
| CP-7 | S7 | Portable PostgreSQL/clock | `/*/*/ChannelOutboundRetryPolicyTests/*` | 5 |
| CP-8 | S7 | Portable PostgreSQL | `/*/*/ChannelOutboundRecoveryTests/Expired_publishing_lease_is_uncertain_until_explicit_retry` | 4 |
| CP-9 | S8 | Portable PostgreSQL | `/*/*/ChannelOutboundFailureRecordingTests/*` | 5 |
| CP-10 | S9 | Portable PostgreSQL | `/*/*/(ChannelOutboundMetadataRepairTests*)\|(ChannelOutboundDeliveryTests*)/*` | 8 |
| CP-11 | S10 | Portable PostgreSQL | `/*/*/ChannelOutboundRetentionTests/*` | 5 |
| CP-12 | S10 | Portable PostgreSQL | `/*/*/DataRetentionServiceTests/Queue_keeps_Pending_and_unsettled_channel_rows_and_deletes_settled_old_ones` | 4 |
| CP-13 | S11a | Portable PostgreSQL/runtime | `/*/*/(ChannelBridgeTests*)\|(ChannelReplyDurabilityTests*)/*` | 8 |
| CP-14 | S11a | Portable PostgreSQL | `/*/*/(ChannelPromptCorrelationTests*)\|(ChannelPromptCorrelationUnitTests*)\|(ChannelMachineTurnMatchTests*)/*` | 7 |
| CP-15 | S11b | Portable PostgreSQL | `/*/*/(ChannelMachineTurnTextTests*)\|(ChannelFollowUpAttachmentTests*)/*` | 7 |
| CP-16 | S11b | Portable PostgreSQL | `/*/*/(ChannelOutboundGateTests*)\|(ChannelOutboundPolicyTests*)\|(ChannelOutboundContractTests*)/*` | 8 |
| CP-17 | S12a | Portable owned child; serial | `/*/*/ChannelOutboundUnifiedCrashTests/*` | 8 |
| CP-18 | S12b | Portable isolated broker/adapter; serial | `/*/*/ChannelOutboundUnifiedTransportTests/*` | 8 |
| CP-19 | S12b | Portable PostgreSQL | `/*/*/ChannelOutboundRecoveryTests/Held_head_blocks_later_reply_until_original_binding_is_repaired_and_resumed` | 4 |
| CP-20 | S13 | **Windows native; serial** | `/*/*/HerdrAlwaysOnChannelParityTests/AlwaysOn_channel_bound_survives_child_death_and_replies` | 8 |
| CP-21 | S4 | Portable PostgreSQL/runtime | `/*/*/AgentTaskReplyIntegrationTests/Deferred_is_durable_and_releases_runtime` | 4 |

CP-20 selects the two current backend argument results, not the entire parity
class. Require both executed, no skips, and a finite row deadline (15 minutes);
retain the owned child/process timeout cleanup. If its runtime cannot fit, stop
and replace it with two bounded backend-specific tests in TestDesign, preserving
both outcomes. Never rerun the old 45-minute Linux hang. CP-21 runs with the S4
group; its numbering does not defer it until S13. Existing conversion deadline/revocation,
queued uncertainty, incoming native-handle and broker tests remain regression
obligations: select exact methods in the touched area and add narrow rows with
costs, rather than treating historical receipts as current evidence.

Read `GET /api/runner-defaults` and `GET /api/session-runners` again before dispatch.
Both returned 200 during this Plan: default revision 2, an eligible Linux lane and
an eligible Windows lane were present. This is a capability observation, not a
fleet-location prescription. Use effective defaults for portable work and
`-Platform Windows` for CP-20; no `-Runner` pin. If inheriting an unwanted OS pin,
`-Platform Any` explicitly unpins it. Capacity at planning time grants no future
dispatch slot.

## TestDesign handoff and acceptance inventory

TestDesign must add `## Verification design` and its closed checkpoint manifest
to this artifact (or a directly linked committed test-design artifact), with exact
methods, assertion witnesses, floors, runtime/slot requirements and cost. One
positive control per independently bypassable behavior is required; each control
has one exact detecting method, an intended assertion red, restoration and that
method green. Parameter cases are coverage, not multiple independent PCs. Do not
reuse the old branch's green status or let one generic retry test stand for
different loss boundaries. No Mutation execution belongs to this Plan dispatch.

The following behavior inventory is the minimum reconciliation checklist. Split
a row if Code introduces separate guards; do not claim a PC in main proves an
independent trailing/machine implementation. Prefer shared production guards.

| Behavior family | Independent controls TestDesign must assign |
|---|---|
| Ownership | Intent committed before preparation; attempt committed before producer; all batch members claimed; unique root/interval ownership; nonoverlapping trailing reservation; current lease required; stale outcome cannot commit. |
| Recovery | Startup discovery without an event; periodic discovery without an event; pre-capture crash rediscovered; captured-before-signal recovered; historical full matcher/session/floors reused; next prompt caps old answer; durable tail survives new prompt/restart; fair bounded scan advances past withheld prefixes. |
| Publication | Producer task awaited; cancellation/timeout never success; uncertainty never auto-replayed; explicit retry acknowledgement required; definite refusal classified correctly; due time enforced; persistent attempt cap; preparation age/cap; frozen staged payload/native handle; A success unaffected by B failure. |
| Existing outbound policy | Conversion still selected; passthrough joins ordering; Held/Uncertain head blocks; disabled/rebound/project-changed binding cannot publish; repaired Held resumes correct phase; profile revocation/fallback and actual bundle completeness preserved. |
| Outcomes | Acceptance and source settlement atomic; metadata repair never calls producer; repair cannot regress preview; loss state/incident/alert atomic; incident dedupe survives pruning; missing owner still recorded; notice failure cannot erase loss; uncertainty wording remains truthful. |
| Lifetime/policy | Session, transcript, queue and referenced bundle/task retention each independently protected; discovery closure cannot discard late eligible text; historical settled direct sources never replay; old open eligible sources recover; main NO_REPLY; machine NO_REPLY/origin policy; API-error whole-window withholding; runtime Deferred is not Published; validated finite settings. |

Crash tests must independently reload PostgreSQL and observe the owned child and
producer/broker receipt, not assert only the same service's return value. Cover
main, trailing and machine at pre-capture, captured, partial/complete staging,
Publishing/attempt commit, producer entry, broker acceptance and outcome commit.
Use method-specific bounded child barriers and guaranteed process-tree cleanup;
no real providers or live messaging broker. The pre-capture case must restart the
actual discovery path without manually re-admitting the answer. Assert no replay
after Published; after uncertain acceptance require visible uncertainty and zero
automatic additional sends. Real transport size refusal must be distinguished
from a local fabricated exception; observe an isolated consumer/adapter receipt
for the valid companion payload without claiming native provider delivery.

Run the checkpoint tool per committed slice group under the documented build-slot
gate; await runs through exit 75 until completed. Commit before long tests and
freeze source throughout each run. Preserve unedited receipt lines and source/build
SHA in the stored report; generated output stays ignored. Remove only the run's
owned alternate outputs. Code/Review run `scripts/check-evidence-diff.ps1` over the
whole task range. Verify an apparent inherited failure with its exact method at
the recorded base; do not widen deadlines or assertions to obtain green.

## Landing and stage handoff

This plan is committed/pushed only to the assigned
`feat/card-task-64b6e881` branch. The caller should land it promptly after this Plan
task settles, using `scripts/delegate.ps1 -Land 64b6e881 -ExpectedSourceSha <pushed-sha>`,
then dispatch TestDesign from landed master. The landing protocol requires a
Succeeded task; a running delegate cannot truthfully report its own plan already
landed. Do not bypass that protocol with a direct master push or rewrite this
fast-forward-only task branch. Implementation and migration generation start
only after TestDesign has made the verification manifest executable.
