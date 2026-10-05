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


## Verification design

TestDesign task `86fa9c01`, 2026-10-04, inspected landed plan/source
`0e9ce38484148e38428da540d901b29f4a0796e5`. This appendix is the
executable verification contract; the fix design above is unchanged. The earlier
checkpoint allocation is superseded by the table here. New method names below are
Code deliverables, not claims of existing tests or passing results. Code runs V/R;
ordinary Review judges the diff and pending controls before land; SourceLanding
Mutation runs the controls after land.

### Inspection

Paths below are under `tests/Antiphon.Tests/` unless qualified.
“Bodies” identifies the inspected boundaries; source rosters are counts, not execution evidence.

| Bodies read | Boundaries -> verification or exclusion |
|---|---|
| `Application/ChannelOutboundDispatchIntegrationTests.cs` dispatcher/converter body; `ChannelOutboundSendShapeTests.cs` complete eight-argument body; `ChannelOutboundGateTests.cs` admission/control bodies and `OutboundGateWorld` | Optional outbound registration, immediate-send assumptions, qualifying/nonqualifying shapes and control recursion -> V-3/V-4, R-1/R-5. |
| `ChannelOutboundDeliveryTests.cs` staging-failure setup, admission barriers, MaxPending race and expired-lease takeover; `ChannelOutboundPolicyTests.cs` frozen-prompt and revocation/rebinding bodies; `ChannelOutboundDeadlineTests.cs` Held-resume body and `ChannelOutboundDeadlineMatrixTests.cs` complete Fixture | Commit/lease/order/converter boundaries -> V-2/V-3/V-7/V-9, R-5/R-6. Partial file names are not TUnit classes. |
| `ChannelOutboundStorageTests.cs` partial/complete/rename/disk/access failure, frozen bytes and exact wire-budget bodies; `ChannelOutboundMigrationTests.cs` upgrade body; `ChannelOutboundContractTests.cs` both bodies | Filesystem versus DB transition, generated migration and existing schema constraints -> V-1/V-3, R-5/R-7. |
| `ChannelOutboundRecoveryTests.cs` Held ordering, native Slack route restart, broker-accepted crash, definite-refusal and expired Publishing bodies; `tests/Antiphon.ChannelOutbound.Probe/Program.cs` admission/pump/dispatch modes, file evidence and commit interceptor | Fresh process, independent PostgreSQL/consumer observations, manual uncertainty and native handles -> V-7/V-11/V-12/V-13, R-8. File evidence is not an adapter receipt. |
| `ChannelOutboundComposedTransportTests.cs` complete body; `AgentTaskReplyIntegrationTests.ChannelOutboundRuntime.cs` complete conversion settlement and Deferred bodies | Existing transport starts at Ready; runtime test holds real conversion observation and sees next UserPrompt -> V-4/V-13, R-1/R-9. |
| `TestHelpers/BridgeQueueHarness.cs` complete DI, attach/preserve, receipt insertion, seeding and disposal; `TestHelpers/TestDbFixture.cs` complete body | Required production graph, restarts, real queue, isolated DB, clocks -> all new database tests. `IsolatedTestSchema` now creates a cloned database, not SearchPath isolation. |
| `ChannelBridgeTests.cs` complete HarnessAsync/Harness; `ChannelReplyDurabilityTests.cs` fixture and runtime late-confirm success/failure bodies; `ChannelPromptCorrelationTests.cs` fixture/normalizer replay and joined-reply, common-head and pre-attempt bodies; `ChannelMachineTurnMatchTests.cs` complete body | Required service absent in old bridge graphs, full prompt receipt versus screen evidence, historical matcher reuse -> V-4/V-5/V-13, R-2/R-3. |
| `ChannelMachineTurnTextTests.cs` incident/check/flattened-turn setup and machine/bundle/producer helpers; `ChannelFollowUpAttachmentTests.cs` incident/flattened/quoted-channel setup and complete machine/bundle/producer helpers; `ChannelBatchingTests.cs` complete body | All-member capture, tail/machine policy, implied bundles, absent catalog fixtures and generic-exception retry assumptions -> V-2/V-4/V-6/V-9, R-4/R-10. |
| `DataRetentionServiceTests.cs` queue-retention body and CreateService/session/task/queue/transcript/cleanup helpers | Independent pruning and removal of unrelated protections -> V-10, R-11. |
| `HerdrAlwaysOnChannelParityTests.cs` AlwaysOn_channel_bound_survives_child_death_and_replies, ConfirmHerdrDeliveryAsync, complete BuildHarness/Harness/cleanup/clock helpers | Two backend arguments; Linux skip; Herdr transcript inserted by fixture and PtyHost arm uses fake adapters -> R-12, explicitly limited evidence below. |
| Server `ChannelOutboundService` and `ChannelOutboundDeliveryPump` full bodies; landed D-1..D-14 and transition/slice inventory; old e30eb42 test-design V/PC inventory; owner docs `project-context.md`, outbound `telegram.md`, session delivery and Herdr delivery/restart contracts; `testing-and-build.md` manifest/slot/mutation sections; checkpoint tool row-timeout/selection code | Shared ownership and every safety guard listed below. Operational rollout, untouched native transport internals and provider acknowledgement are excluded explicitly, not inferred from green application tests. |

Missing setup to deliver in Code:

- Add `TestHelpers/ChannelOutboundRecoveryFixture.cs` using the inspected
  BridgeQueueHarness/OutboundGateWorld/Deadline Fixture patterns. Register the required
  outbound service, concrete preparation/discovery/failure/pump services, file store,
  settings and the real hosted loop exactly as production composes them. Every
  context/interceptor/restarted provider receives the same isolated connection string.
  Add explicit bounded drain helpers; never restore a legacy producer fallback.
- Adapt all existing selected fixtures to capture -> pump -> outcome. Seed legitimate
  catalog/binding/project context where older batching fixtures used a conversation
  string alone. Missing-catalog tests must keep the catalog absent. Shared-store cleanup
  removes test-owned source/converter references, children and roots before channels.
  The new fixture owns its clone and all scratch directories.
- Use a controllable outbound clock for due times/deadlines. Keep the queue confirmation
  clock system/scaled, or advance all its timers deliberately; a frozen shared clock
  must not hang its confirmation loop. Await observable cycle/barrier completion,
  not arbitrary sleeps. No transaction remains held across file or producer I/O.
- Extend the existing ChannelOutbound.Probe, not a new crash executable. Add capture,
  preparation and hosted-discovery modes plus per-delivery barriers. Record child
  PID/start identity, assembly MVID and source/build SHA. Parent independently reads
  the committed DB and receiver evidence, kills only its owned child, awaits process
  exit and drains both streams before restart. Each cut wait/restart is at most 30s,
  cleanup at most 10s, each scenario at most 120s. Process tests retain
  `ParallelLimiter<ProcessSpawnLimit>`.
- Reuse Redpanda/FakeSlackServer/production Kafka producer/GatewayOutboundService and
  SlackChannelAdapter from the composed test. Use one owned broker per test method,
  distinct topics/groups per internal case, bounded 30s receive/start/stop waits and
  a method deadline. Keep gateway/fake Slack in the surviving parent; flush its
  complete API/upload observations to a parent-owned receipt file before the
  corresponding child-kill assertion. Producer counters never stand for receipt.
- Add fault injection only at attachment-reader I/O, EF commit/command interceptors,
  existing per-instance barriers and producer I/O. Every fault must assert it fired.
  Inject the specified statement/transaction, not all SaveChanges. Keep all production
  policy in concrete services. A transport-shaped fake exception does not establish
  broker behavior; V-13 supplies the real refusal.
- Each new unique class/method in the PC table is a required test. All are one
  unparameterized TUnit execution except the seven X methods, each with explicit
  main/trailing/machine arguments (three executions). Internal matrices below are
  mandatory assertions, not additional Min executions. W adds the three methods
  specified in V-13: three path arguments for Queue_to_adapter; one result each for
  Size_refusal and Converter_handoff. No dynamic/unbounded data sources.
- No native program, live messaging broker or production runner is used by portable
  tests. R-12 runs with `-Platform Windows`, no host pin; portable dispatch omits
  both Platform and Runner. Preserve its Linux skip but never count it as execution.

### Delivery inventory

Durable join K is delivery ID + SourceSessionId + owning PromptSequence + captured
provider/conversation/native reply handle + all queue/task member IDs; a tail adds
RootDeliveryId and its reserved interval. Before capture, source queue IDs and the
complete owning transcript identify K's future obligation. Include K in receipt
metadata or a fixture-only unique payload sentinel and hash-to-ID ledger; do not
change public message wire contracts merely for testing.

| Producer -> destination | Persistence boundary / recovery | Recipient evidence joined to K |
|---|---|---|
| ChannelBridge/real SessionMessageQueueService -> source agent | Inbound and queue commit before submit; busy WhenIdle waits; eligible recipient submits now; failed queue insert retains durable inbound for its real recovery path | V-13 Queue_to_adapter: complete matching UserPrompt, same session/generation and after original attempt floor, exact full body including middle/tail; then answer goes through dispatcher, capture, pump, real Kafka, gateway and fake Slack. Queue/Sent/input call alone fails the oracle. |
| Main answer -> channel adapter | Source remains open before capture; Captured owns preparation; materialized payload owns publish; committed attempt owns uncertainty | V-11/V-12 kill at every handoff; V-13 receives full text/kind/route/attachment hashes at fake Slack, for both already-running and delayed gateway. |
| Trailing fragment -> original adapter route | Root reservation + cursor + child insertion one commit; startup/timer rediscovers open roots and committed children | Same matrix, baseline initial reply separately counted. Receipt contains only reserved fragment; initial reply/source settlement unchanged, newer prompt excluded. |
| Eligible Delegation/Check/System/Scheduled answer -> captured adapter route | Original injection and prior channel context; all machine member IDs captured before preparation | Same matrix; K includes source task IDs and inbound native handle. No later chat can authorize pre-chat history. |
| Captured qualifying reply -> conversion worker -> Ready -> adapter | Materialization and frozen policy; conversion task linked by OutboundDeliveryId; dispatch/launch and worker result are separate committed handoffs | V-13 Converter_handoff uses actual task service/dispatcher/launch queue with a fake protocol adapter and real session queue where input is delivered. Require complete worker UserPrompt containing the frozen goal/request identity, linked worker result, then final fake Slack receipt. Creation, task status and launch ack alone are insufficient. |
| Accepted delivery -> catalog/bundle projections | Published + all source settlements commit first; MetadataAppliedAt marks later idempotent repair | V-9 reloads accepted state and repaired projections independently; V-13 receiver saw original envelope. Projection completion is not a second delivery receipt. |
| Terminal/uncertain obligation -> durable incident + Alert; optional originating-chat notice | State/source outcome + incident + alert + episode stamp one transaction, with nullable owner; only recording repair can continue at spent budget | V-8 independent DB queries establish durable attention data. V-13 transport companion receives enabled control notice via gateway; disabled/refused notices are best effort and do not promise receipt. No session notification is introduced. |

Required handoff matrix (each applies to main/tail/machine unless stated):

| Cut/failure | Persisted fact and recovery verdict | Evidence |
|---|---|---|
| Upstream queue insert fails; queue committed before submit; submit before confirm save | Inbound/queue remains durable; real inbound/queue recovery runs. Never seed a Sent row to stand for this leg. After partial receipt only, obligation stays unconfirmed. | W.Queue_to_adapter runs idle/busy cases, exact complete UserPrompt and one eventual adapter receipt. Reuse the inbound recovery service, do not expand its contract. |
| Before capture commit; capture commit before signal | Before commit: no delivery visible, open source survives. After commit: one Captured owner. Fresh hosted discovery, no OnTurnEnd call or manual re-admission. | X.Before_capture / X.Captured, independent store and adapter receipt. |
| Attachment/prompt read; partial stage; complete stage before DB transition | Retry retained descriptors within prep age/cap; complete validated directory adopted; partial/mismatched data never sendable. | C/M plus X.Partial_stage / X.Complete_stage; reader and stage refusal subcases. |
| Converter task creation commit; dispatch/launch enqueue; completed output before Ready commit | Same OutboundDeliveryId/task identity recovered, not a second conversion. Failed launch enqueue remains recoverable through real dispatch recovery; complete matching worker UserPrompt required. | W.Converter_handoff, with busy/eligible adapter and owned child death at task-committed/dispatch-committed/result-committed barriers; final adapter receipt. |
| Publishing commit; attempt commit; producer entry | Expired Publishing => Uncertain even when test knows no acceptance. No automatic replay, no source settlement, visible loss episode. | X.Attempt_death, B attempt/fence/uncertainty controls. Acknowledged retry then receives the original payload. |
| Definite queue refusal; refusal-state save fails | Persist due Ready only on known queue refusal; failed save leaves conservative Publishing/Uncertain. Budget never resets. | B retry matrix and W.Queue_to_adapter real transport after scripted definite refusal. |
| Broker accepts before return/outcome; outcome commit fails | Consumer/adapter may already have one receipt. Uncertain, zero automatic additional sends. Only explicit duplicate acknowledgement allows another identical attempt. | X.Accepted_death covers both acceptance and outcome-commit-refusal cuts with real broker consumption and adapter receipt. |
| Published commit; projection commit/notice fails | No resend. Repair metadata/attention without re-entering agent publication. | X.Published_death, P/F, W receipt count remains unchanged. |

Substitutes: the fake protocol adapter writes a complete UserPrompt through the
fixture's transcript path; that proves server queue/correlation behavior, not real
Claude/Grok/Codex input. FakeSlackServer proves the real gateway and adapter issued
the complete API request and uploads, not native Slack receipt or human reading.
The probe file producer proves deterministic I/O entry/acceptance only; it is
insufficient alone for V-11/V-12 final delivery, which must also observe the real
broker/gateway recipient. R-12's Herdr receipt is synthesized by
ConfirmHerdrDeliveryAsync and its PtyHost arm uses fake adapters; Windows execution
qualifies those backend integration paths, not an additional native typing claim.
No assertion of exactly-once delivery crosses broker acceptance/outcome commit.

### Proves it works now

Aliases expand to exact class names below, all under Application. `C519_`
is part of every new method name. The unique methods in the positive-control
table are the exact per-class rosters; their red assertions also define required
ordinary assertions. Passing names/counts without those bodies do not qualify.

| Alias | Exact class |
|---|---|
| S | `ChannelOutboundDurabilitySchemaTests` |
| C | `ChannelOutboundCaptureTests` |
| M | `ChannelOutboundMaterializationTests` |
| U | `ChannelOutboundUnifiedPathTests` |
| D | `ChannelOutboundDiscoveryTests` |
| T | `ChannelOutboundTrailingRecoveryTests` |
| B | `ChannelOutboundRetryPolicyTests` |
| F | `ChannelOutboundFailureRecordingTests` |
| P | `ChannelOutboundMetadataRepairTests` |
| L | `ChannelOutboundRetentionTests` |
| X | `ChannelOutboundUnifiedCrashTests` |
| W | `ChannelOutboundUnifiedTransportTests` |

| ID | Layer / exact test selection | Required behavior and decisive expected result |
|---|---|---|
| V-1 | Schema/migration; `ChannelOutboundDurabilitySchemaTests`, roster below | fresh empty migration and actual predecessor upgrade; all eight legacy states, settled/unsettled sources, nullable fields, enum ordinals, unique constraints with different SourceKeys, restrictive root FK. No old migration edited. |
| V-2 | PostgreSQL capture/race; `ChannelOutboundCaptureTests`, roster below | main/tail/machine; reader, prompt-read, capture-commit and stage failures; three-member batch and overlapping batches; independent readers at barriers; loser returns existing owner without re-preparation. |
| V-3 | PostgreSQL/files/conversion; `ChannelOutboundMaterializationTests`, roster below | capture versions invalid/valid; partial/full/wrong-identity/tampered snapshots; source mutation/removal after staging; preparation attempts 0/1/2/3; TTL minus-one/equality/after; profile edited/revoked; MaxPending and converter/global seats raced using independent contexts. |
| V-4 | Dispatcher/runtime; `ChannelOutboundUnifiedPathTests`, roster below | all three paths with no profile, MarkdownSources and EveryAgentReply; main/tail NO_REPLY versus surrounding prose; machine origins default/custom plus explicit/implied attachments; API stub anywhere in window; A accepted/B fails; nullable/missing catalog; Deferred does not block next complete queue receipt. |
| V-5 | Real hosted discovery/clock; `ChannelOutboundDiscoveryTests`, roster below | old answer behind newer turns; startup with timer held, timer after empty startup; each mismatch alone plus valid companion; prior/absent/later channel context; open/next-prompt/terminal-incomplete/terminal-complete machine closure; 321 withheld sources then one eligible source, and separately 321 idle roots then one new tail: page size 32 / maximum 10 pages permits at most 320 examined candidates per work kind per cycle, and target must be reached by cycle two (earlier is valid); due-send and metadata-repair work each make progress; TTL race and original-age answer preferred. |
| V-6 | PostgreSQL trailing ownership; `ChannelOutboundTrailingRecoveryTests`, roster below | reserve failed interval, append additional text before recapture so overlapping intervals have different keys; recreate providers, then newer prompt in same batch; concurrent callers; suppressed main followed by valid tail; suppression advances cursor without PublishedAt. |
| V-7 | Pump/service/clock; `ChannelOutboundRetryPolicyTests`, roster below | attempt commit, claims, pre-entry and outcome fences; cancellation before/after entry/after acceptance; held producer completion; timeout with host live; queue refusal versus generic failures/size; persisted due/cap; explicit retry and all original-binding holds; CreatedAt ties resolved by Id. |
| V-8 | EF transaction/fault/attention; `ChannelOutboundFailureRecordingTests`, roster below | incident insert and alert insert faults separately for Failed/Uncertain/TTL/unroutable/terminal-provider routes; captured/current/deleted/no owner; notice disabled/refused; prune incident history and restart; spent budget forbids more I/O while recording repairs. |
| V-9 | Acceptance/projection; `ChannelOutboundMetadataRepairTests`, roster below | acceptance/member-save fault; catalog/filesystem/bundle-save faults independently; multiple main/machine members and implied tasks; complete/missing/hash-mismatched/over-cap/partial-zip actual attachments; delete current files after acceptance; old repair after newer published preview. |
| V-10 | Real DataRetentionService passes; `ChannelOutboundRetentionTests`, roster below | remove all incidental persistent-agent/task/inbound protections; stale sessions/transcripts/queue/task trees and files across Captured/Pending/Converting/Ready/Publishing/Held/Uncertain/Failed, incomplete repair and open roots; closed ineligible/fully examined companions prune; transcript-lock race. |
| V-11 | Owned process death before publish; `ChannelOutboundUnifiedCrashTests`, roster below | Before_capture_death_is_discovered, Captured_death_needs_no_wake_signal, Partial_stage_death_retries_preparation, Complete_stage_death_preserves_snapshot; each main/tail/machine. Fresh process starts real hosted recovery; exact original adapter receipt. |
| V-12 | Owned process death during/after publish; `ChannelOutboundUnifiedCrashTests`, roster below | Attempt_death_stays_uncertain (Publishing, attempt and producer-entry cuts), Accepted_death_stays_uncertain (acceptance and outcome-commit-failure cuts), Published_death_never_replays; each main/tail/machine. Receiver count 0 or 1 before manual retry, never automatic duplicate. |
| V-13 | Real queue -> broker -> recipient; `ChannelOutboundUnifiedTransportTests`, roster below | C519_Queue_to_adapter has explicit main/tail/machine arguments, each looping idle/busy recipient x gateway already eligible/late, with named upstream/capture/definite-refusal handoff faults above. C519_Size_refusal uses broker max.message.bytes below serialized envelope, actual MsgSizeTooLarge, Failed+loss, no automatic retry; distinct valid companion reaches fake Slack. C519_Converter_handoff exercises task/create/dispatch/result crash and enqueue refusal recovery, worker full UserPrompt and resulting converted adapter receipt. Counts: 3+1+1=5. |

The matrices are mandatory within the named tests, even when implemented as internal
loops. Isolate every negative from masking guards: different SourceKeys for root/start
constraint tests; same-project rebind for owner tests; unchanged owner moved to a
different project for project tests; valid matching body but wrong session/floor for
matcher tests; all other binding/profile/lease predicates valid when mutating one.
One valid sibling confirms the path was exercised in every refusal/policy test.
Cancellation before durable attempt need not be uncertain; any ambiguity after
committed Publishing must remain manual. Generic fake exceptions in older tests
must change their expectations to Uncertain/explicit retry, never automatic replay.

### Guards the regression

| ID | Existing selected tests | Decisive retained assertion / required adaptation |
|---|---|---|
| R-1 | ChannelOutboundDispatchIntegrationTests (4); AgentTaskReplyIntegrationTests.Deferred_is_durable_and_releases_runtime (1) | Qualifying conversion/source bytes/native handle survive, with activation default-off and enabled arguments. Activation_captures_before_source_reads_and_publishes_the_staged_bytes covers missing-at-dispatch sources and frozen bytes; Activation_preserves_machine_silence_and_origin_policy_before_capture covers main/machine silence, held System text and eligible Check text. Nonqualifying reply also has capture and explicit pump drain. Held conversion leaves settlement/LastReplyAt null; next prompt has one complete UserPrompt. |
| R-2 | ChannelBridgeTests (40); ChannelReplyDurabilityTests (25) | Routing/late-confirm/attachment/TTL/API-withhold/terminal-provider behavior remains observable after pump. Inspect stored delivery outcome, not old immediate return. Incident/alert assertions strengthen to atomic outcome. |
| R-3 | ChannelPromptCorrelationTests (24), ChannelPromptCorrelationUnitTests (8), ChannelMachineTurnMatchTests (10) | Complete joined prompt and batch/spill membership; wrong marker/tail/session/floor/next turn cannot own answer; channel receipt cannot be borrowed by quoted machine output. |
| R-4 | ChannelMachineTurnTextTests (19), ChannelFollowUpAttachmentTests (26) | All allowed origins, NO_REPLY, flattened headers, marker/implied attachments and actual bundle completeness preserved. Generic failure remains uncertain until acknowledged. |
| R-5 | ChannelOutboundDeliveryTests (38 across five partial files); ChannelOutboundPolicyTests (29); ChannelOutboundContractTests (2) | Existing capture/lease/publication/shape/gate/conversion/binding/source guards remain; rewrite direct-route and “no delivery for passthrough” assertions for universal admission. Control notices remain outside agent capture. |
| R-6 | ChannelOutboundDeadlineTests (13 across two partial files) | Original conversion deadline, held resume, final create/dispatch checks, converter/global capacity and fallback original bytes unchanged. |
| R-7 | ChannelOutboundStorageTests (32) | Atomic staging, byte/hash integrity, wire size, manifest/zip/path/link validation and frozen routing retained. CARD-1059 adds four source-read tests for allowed roots/traversal, file/directory links, pre-read finite byte budgets, and regular files (including Linux FIFO/device refusal). V-3 adds capture-aware adoption rather than treating directory presence as success. |
| R-8 | ChannelOutboundRecoveryTests: Expired_publishing_lease_is_uncertain_until_explicit_retry; Held_head_blocks_later_reply_until_original_binding_is_repaired_and_resumed; Restart_preserves_two_inbound_slack_routes_behind_an_uncertain_head; Broker_ack_before_process_death_remains_uncertain_without_replay (4) | No send from Uncertain without acknowledgement; Held order; native inbound Slack handles after restart; independent consumer observed accepted message and no automatic replay. |
| R-9 | ChannelOutboundComposedTransportTests.Sealed_four_source_pdf_crosses_pump_broker_gateway_and_fake_slack (1) | Fake Slack receives original four source byte arrays and PDF on exact native thread; one publication attempt. This Ready-seeded test supplements V-13, not a substitute for it. |
| R-10 | ChannelBatchingTests (10) | Same-chat members share real queue delivery and one answer; mixed origin/chat boundaries; intentional silence and batching kill switch. Add bound catalog to positive reply fixtures. |
| R-11 | DataRetentionServiceTests.Queue_keeps_Pending_and_unsettled_channel_rows_and_deletes_settled_old_ones (1) | Pending/owed/unpurged-inbound retention still works; old classified non-obligations remain prunable. |
| R-12 | HerdrAlwaysOnChannelParityTests.AlwaysOn_channel_bound_survives_child_death_and_replies (2 backend arguments, Windows only) | Restart/adoption/death recovery, complete matching source UserPrompt, required outbound capture/pump and expected channel reply. Deadline 15 minutes for row including build; 0 skips. No whole parity-class/Linux attempt. |

Reference reconciliation: old V-1/2/4/13 -> V-2/3/7/8/11/12; old V-3/12 ->
V-5; old V-5/6/16 -> V-1/2/7/4; old V-7 -> V-8/9/12; old V-8/9 -> V-3/6/13;
old V-10 -> V-8; old V-11 -> V-1/5; old V-14 -> V-4; old V-15/20 -> V-13;
old V-17/18 -> V-11/12; old V-19 -> V-10; old V-21 -> V-4/R-1/R-2;
old V-22 -> V-7. Old PC-1..52 behaviors are accounted for by those replacements.
Their separate publication/membership schema, automatic Unknown retry, send despite
rebind, Held-as-exhaustion and publication-time-ranked tail scan are superseded by
D-1/4/6/7/8/13. No old branch code, 71/18/3/99/40 results or Linux hang qualifies here.


### Guard inventory

Every row names one independently bypassable safety behavior and its distinct PC.
The destination lock plus root version are redundant enforcement of the single
reservation invariant in G-99; that control removes both, so one surviving fence
cannot mask the defect. Existing native transport internals are not changed by this
card and are not claimed as newly qualified guards. Shared production helpers must
serve all main/tail/machine callers; if Code creates separate guards, split and
budget their PCs before Review.

| Guard | Plan reference + safety-critical invariant | Positive control |
|---|---|---|
| G-1 | D-2/D-11: Persisted enum ordinals and legacy states survive upgrade. | PC-1 |
| G-2 | D-4: One root per session/prompt/destination, independent of current interval end. | PC-2 |
| G-3 | D-4: One reservation per root/start. | PC-3 |
| G-4 | D-12: A retained child prevents root deletion. | PC-4 |
| G-5 | D-10: Historical Published rows do not become fresh metadata repair work. | PC-5 |
| G-6 | D-2: Commit capture before attachment/prompt/staging I/O. | PC-6 |
| G-7 | D-2: Capture commit failure cannot enter preparation. | PC-7 |
| G-8 | D-4: Capture claims every batch member atomically. | PC-8 |
| G-9 | D-4: Conditional source assignment never overwrites an existing owner. | PC-9 |
| G-10 | D-4: A growing main response does not remint its root. | PC-10 |
| G-11 | D-4: Delivery insertion and root cursor advancement share a transaction. | PC-11 |
| G-12 | D-2: Malformed/unsupported capture cannot become sendable. | PC-12 |
| G-13 | D-3: Adopt staged data only for the same delivery and capture hash. | PC-13 |
| G-14 | D-3: Every retained staged file must validate. | PC-14 |
| G-15 | D-3: Validated staged bytes are immutable after a crash. | PC-15 |
| G-16 | D-3/D-8: Materialized prompt revision and profile parameters stay frozen. | PC-16 |
| G-17 | D-7: Preparation cap persists independently of publication count. | PC-17 |
| G-18 | D-7: Preparation age is measured from original obligation. | PC-18 |
| G-19 | D-1: Main, tail and machine have no direct/optional-service escape. | PC-19 |
| G-20 | D-8/D-9: Missing catalog never authorizes an unchecked send. | PC-20 |
| G-21 | D-11: Whole main NO_REPLY deliberately suppresses without publication. | PC-21 |
| G-22 | D-11: Machine NO_REPLY does not publish or settle. | PC-22 |
| G-23 | D-11: MachineTurnTextOrigins gates plain text independently of attachments. | PC-23 |
| G-24 | D-11: Any API stub withholds its complete answer window. | PC-24 |
| G-25 | D-5/D-13: Hosted startup performs discovery and recovery. | PC-25 |
| G-26 | D-5/D-13: Periodic discovery works after an empty startup. | PC-26 |
| G-27 | D-5: Historical discovery uses complete marker/body matcher. | PC-27 |
| G-28 | D-5: Historical evidence must belong to source session. | PC-28 |
| G-29 | D-5: Sequence and native-time attempt floors exclude old receipts. | PC-29 |
| G-30 | D-5: Next UserPrompt or submitted QueuedUserPrompt caps historical answer. | PC-30 |
| G-31 | D-5: Later chat cannot authorize pre-chat machine history. | PC-31 |
| G-32 | D-11: Settled direct history never gets invented receipts/roots. | PC-32 |
| G-33 | D-12: Ineligible machine closure cannot discard late text. | PC-33 |
| G-34 | D-13: Source scans obey page size and maximum pages. | PC-34 |
| G-35 | D-13: Withheld rows and idle roots cannot starve later work. | PC-35 |
| G-36 | D-7/D-9: TTL cannot settle a source before its historical answer is examined. | PC-36 |
| G-37 | D-4/D-9: TTL classification rechecks source ownership under the capture lock. | PC-37 |
| G-38 | D-4: Reserve from durable last-reserved cursor, not last-published end. | PC-38 |
| G-39 | D-4/D-5: Restart enumerates roots and reserved tails without in-memory cache. | PC-39 |
| G-40 | D-4/D-5: New prompt caps old tail but never discards a reserved fragment. | PC-40 |
| G-41 | D-4/D-11: Suppressed tail advances durable cursor with honest outcome. | PC-41 |
| G-42 | D-6: Publishing and lifetime attempt commit before producer entry. | PC-42 |
| G-43 | D-4: An unexpired lease excludes competing owners. | PC-43 |
| G-44 | D-6: Final producer entry requires current owner/version/unexpired lease. | PC-44 |
| G-45 | D-10: Outcome commit is fenced against stale owner/version. | PC-45 |
| G-46 | D-6/D-10: Producer Task completion precedes accepted outcome. | PC-46 |
| G-47 | D-6: Cancellation/unknown fault after entry remains uncertain. | PC-47 |
| G-48 | D-13: Send timeout completes while host remains uncanceled. | PC-48 |
| G-49 | D-6: Unknown and expired Publishing require explicit authorization. | PC-49 |
| G-50 | D-6: Explicit uncertain retry requires acknowledgement. | PC-50 |
| G-51 | D-6/D-7: Acknowledged retry starts a new bounded authorization without resetting lifetime count. | PC-51 |
| G-52 | D-7: Only documented non-acceptance permits automatic retry. | PC-52 |
| G-53 | D-7: Unchanged over-cap payload is Failed, not uncertain/retryable. | PC-53 |
| G-54 | D-7: No automatic send before persisted due time. | PC-54 |
| G-55 | D-7/D-9: Three attempts per authorization; loss-save failure does not grant a fourth. | PC-55 |
| G-56 | D-8: Disabled binding cannot publish. | PC-56 |
| G-57 | D-8: Rebound/unbound agent cannot publish. | PC-57 |
| G-58 | D-8: Changed project cannot publish. | PC-58 |
| G-59 | D-8: Revocation immediately before converter creation sends original via policy. | PC-59 |
| G-60 | D-8: Revocation immediately before publication excludes converted output. | PC-60 |
| G-61 | D-8: Captured/Held/Uncertain heads block later passthrough in CreatedAt/Id order. | PC-61 |
| G-62 | D-8: Resume revalidates binding and selects Captured/Pending/Ready correctly. | PC-62 |
| G-63 | D-8: MaxPending remains a hard per-destination conversion limit. | PC-63 |
| G-64 | D-8: One active conversion per converter. | PC-64 |
| G-65 | D-8: Two global conversion seats. | PC-65 |
| G-66 | D-8: Original conversion deadline still governs final creation and late output. | PC-66 |
| G-67 | D-9: Failed/Uncertain/TTL/unroutable/provider outcome cannot outlive missing incident. | PC-67 |
| G-68 | D-9: Alert persistence shares the loss transaction. | PC-68 |
| G-69 | D-9: Critical ChannelReplyLost names durable source, target, stage and attempt. | PC-69 |
| G-70 | D-9/D-12: Failure dedupe survives incident retention. | PC-70 |
| G-71 | D-9: Captured owner used when available, nullable when deleted/unowned. | PC-71 |
| G-72 | D-9: Best-effort notice occurs after loss commit and never recursively admits itself. | PC-72 |
| G-73 | D-6/D-9: Unknown wording acknowledges possible acceptance. | PC-73 |
| G-74 | D-10: Acceptance and every main/machine member settle atomically. | PC-74 |
| G-75 | D-10: Projection faults cannot resend or erase accepted state. | PC-75 |
| G-76 | D-10: Older repair cannot overwrite newer accepted preview. | PC-76 |
| G-77 | D-10: Only complete actual accepted attachments authorize bundle stamp. | PC-77 |
| G-78 | D-10: All eligible implied bundles get their accepted stamp. | PC-78 |
| G-79 | D-12: Unresolved/discoverable session cannot cascade away. | PC-79 |
| G-80 | D-12: Transcript evidence retained through mutation-lock recheck. | PC-80 |
| G-81 | D-12: Independent queue prune keeps eligible machine and Channel sources. | PC-81 |
| G-82 | D-12: Task tree/bundle evidence retained while preparation/discovery/repair needs it. | PC-82 |
| G-83 | D-12: Captured/active/Held/Uncertain/Failed and incomplete repairs preserve journal/files. | PC-83 |
| G-84 | D-12: Root windows close only after complete next-prompt/terminal evidence; closed history prunes. | PC-84 |
| G-85 | D-13: Worker/retry limits validated before worker starts. | PC-85 |
| G-86 | D-13: Timeout strictly below lease. | PC-86 |
| G-87 | D-8/D-10: A's committed acceptance survives B's refusal/unroutable failure. | PC-87 |
| G-88 | D-1/D-10: Deferred is durable ownership, not Published or a runtime wait. | PC-88 |
| G-89 | D-6/D-10: A committed Published outcome is final across process death. | PC-89 |
| G-90 | D-2/D-5: Pre-capture process death recovers original source without re-admission. | PC-90 |
| G-91 | D-2/D-13: Committed Captured row recovers without notification. | PC-91 |
| G-92 | D-3: Partial staging is not successful preparation. | PC-92 |
| G-93 | D-3: Complete staging before DB transition adopts frozen bytes. | PC-93 |
| G-94 | D-6: Publishing/attempt-commit death is uncertain even with no observed entry. | PC-94 |
| G-95 | D-6/D-10: Accepted-before-outcome crash never automatically duplicates. | PC-95 |
| G-96 | D-8: A committed converter task survives dispatch/launch enqueue failure without a second task. | PC-96 |
| G-97 | D-13: Discovery cannot monopolize due-send or metadata-repair capacity. | PC-97 |
| G-98 | D-13: Each query is bounded independently of maximum page count. | PC-98 |
| G-99 | D-4: Destination lock and root version jointly prevent competing reservations. | PC-99 |
| G-100 | D-9: An explicitly authorized retry may report one new failure episode. | PC-100 |

### Positive controls

Each row means **break G-n by the compiling production defect shown; expect the
exact method red at the specified assertion**. Prefix aliases expand through the
class table above. Run only `/*/*/ClassName/ExactTestMethod`;
parameterized X and W.Queue_to_adapter use that method's complete three-result
selection, never a whole class. If TUnit needs its documented argument-name suffix,
append `*` to the exact method name only, retain all three expected argument results,
and record the resolved filter. No test/fixture/assertion mutations.

Mutation runs baseline green, break/red, exact restore/fresh build/green. Compiler
errors, timeout, missing fixture, zero results or failure at a different assertion
are invalid controls, never red evidence. All PCs are executable specifications:
defect site, method, state setup and decisive assertion are supplied; the methods
and supporting seams are Code deliverables. Review checks this inventory before
land; execution is explicitly pending until the landed SourceLanding snapshot.

| PC | Compiling defect | Exact detecting method | Intended assertion red |
|---|---|---|---|
| PC-1 | insert Captured before Pending in the enum. | S.`C519_Upgrade_preserves_ordinals_and_history` | old numeric states equal their pre-upgrade meanings. |
| PC-2 | remove only root uniqueness from the model and regenerated migration. | S.`C519_Database_rejects_duplicate_roots` | second root insert fails at the root unique constraint; distinct target succeeds. |
| PC-3 | remove only trailing-start uniqueness from model and regenerated migration. | S.`C519_Database_rejects_duplicate_tail_starts` | duplicate start with a different end fails at the trailing unique constraint. |
| PC-4 | change only the root FK from Restrict to Cascade in model and regenerated migration. | S.`C519_Database_preserves_root_references` | direct root delete fails and child still exists. |
| PC-5 | leave MetadataAppliedAt null for historical Published rows in the new migration. | S.`C519_Upgrade_preserves_ordinals_and_history` | old preview and delivered stamps remain byte-for-byte unchanged after recovery. |
| PC-6 | invoke attachment reader before capture commit. | C.`C519_Capture_precedes_all_preparation` | first reader callback independently sees Captured plus every source link. |
| PC-7 | continue to preparation after a failed capture commit. | C.`C519_Capture_precedes_all_preparation` | capture-commit fault yields zero reader/stage calls and open sources. |
| PC-8 | link only the first member. | C.`C519_Batch_members_share_one_owner` | all three member IDs reload with the same delivery ID. |
| PC-9 | assign ChannelOutboundDeliveryId unconditionally. | C.`C519_Existing_source_owner_is_not_replaced` | racing overlapping batches preserve the original member owner; loser has no orphan root. |
| PC-10 | dedupe roots by the full start/end SourceKey alone. | C.`C519_Growing_main_window_reuses_its_root` | two captures with different end sequences yield exactly one root. |
| PC-11 | commit cursor advancement separately before inserting child. | C.`C519_Reservation_and_cursor_commit_together` | insertion failure leaves cursor at original value and later recovery receives the fragment. |
| PC-12 | treat an unsupported version as an empty valid capture. | M.`C519_Malformed_capture_is_visible_failure` | invalid JSON/version/oversize DTO produces Failed plus loss evidence and zero sends. |
| PC-13 | omit capture-identity comparison during adoption. | M.`C519_Complete_stage_requires_matching_capture` | another delivery or capture's complete directory is rejected, never published. |
| PC-14 | skip retained attachment hash validation during adoption. | M.`C519_Complete_stage_requires_valid_file_hashes` | one tampered retained file yields no send; unchanged companion adopts. |
| PC-15 | restage a complete directory from current source paths. | M.`C519_Complete_stage_is_adopted_without_reopening_sources` | recovered envelope contains original bytes after original files change/disappear. |
| PC-16 | reload same-name prompt text/settings when preparing an already materialized delivery. | M.`C519_Frozen_prompt_and_policy_survive_restart` | worker goal/hash/deadline use original revision while a new capture uses edited revision. |
| PC-17 | reset PreparationAttempts on reconstructing the pump. | M.`C519_Preparation_budget_survives_restart` | three failed preparations, zero fourth I/O call, Failed and one loss episode. |
| PC-18 | refresh PreparationDeadlineAt on each retry. | M.`C519_Preparation_deadline_uses_original_obligation` | at original TTL equality preparation stops and loss persists. |
| PC-19 | restore passthrough PublishDirectAsync for nonqualifying agent replies. | U.`C519_Every_agent_shape_is_captured` | for each shape producer entry sees committed delivery; pre-pump sent count is zero. |
| PC-20 | send directly when channel lookup returns null. | U.`C519_Missing_catalog_records_loss` | zero producer calls and atomic unroutable incident/alert with original source identity. |
| PC-21 | admit exact NO_REPLY as Ready. | U.`C519_Main_silence_keeps_a_suppressed_root` | zero sends; Suppressed root, settled sources, PublishedAt null; prose companion sends. |
| PC-22 | settle machine sources when response is exact NO_REPLY. | U.`C519_Machine_silence_leaves_source_unsettled` | machine settlement and delivery link remain null before definitive discovery closure. |
| PC-23 | allow default System plain text. | U.`C519_Machine_origins_and_attachments_keep_policy` | System text produces zero replies; explicit attachment and configured System companions succeed. |
| PC-24 | filter error rows out and send remaining prose. | U.`C519_Api_error_withholds_the_whole_window` | main/tail/machine mixed window yields zero agent replies and no Published timestamp. |
| PC-25 | remove startup discovery call while retaining timer loop. | D.`C519_Startup_recovers_without_signal` | with timer held, old completed answer reaches receiver. |
| PC-26 | omit discovery from timed cycles. | D.`C519_Timer_recovers_without_signal` | source inserted after startup reaches receiver on observed scheduled cycle. |
| PC-27 | replace full matcher with first-120-character containment. | D.`C519_Historical_match_requires_complete_prompt` | same-head/different-tail and wrong-marker cases never borrow the answer; joined valid receipt succeeds. |
| PC-28 | drop session restriction from discovery prompt lookup. | D.`C519_Historical_match_requires_source_session` | identical receipt in another session produces no capture. |
| PC-29 | ignore attempt floors in discovery's matcher call. | D.`C519_Historical_match_obeys_attempt_floors` | at/below sequence floor or native time before attempt produces no capture; above-floor companion does. |
| PC-30 | remove next-prompt upper bound. | D.`C519_Historical_answer_stops_at_next_prompt` | later answer text is absent from the old delivery. |
| PC-31 | resolve machine destination from newest current channel context. | D.`C519_Machine_context_must_predate_injection` | pre-chat output has zero captures after a later chat; prior-context companion recovers. |
| PC-32 | include settled unowned sources in discovery. | D.`C519_Legacy_settled_sources_are_not_replayed` | old settled source remains untouched with zero new root/send; old open eligible source recovers. |
| PC-33 | stamp ChannelReplyDiscoveryClosedAt for open NO_REPLY/no-attachment window. | D.`C519_Discovery_closure_waits_for_complete_window` | late eligible text before next prompt still gets a delivery; source was not prematurely closed. |
| PC-34 | remove the maximum-pages break. | D.`C519_Discovery_has_a_finite_cycle_budget` | counted rows/pages stay within 32 per page and 10 pages per cycle. |
| PC-35 | restart every tick at the first page. | D.`C519_Fair_cursors_reach_work_behind_idle_prefixes` | candidate beyond withheld prefix and tail beyond idle roots are examined by the calculated wrap bound. |
| PC-36 | allow TTL loss on candidates not yet examined this cycle. | D.`C519_Ttl_waits_for_discovery_and_serializes_with_capture` | beyond-budget old answer survives TTL pass and is later captured. |
| PC-37 | skip locked ownership recheck in TTL path. | D.`C519_Ttl_waits_for_discovery_and_serializes_with_capture` | capture/TTL barrier race yields capture or atomic loss, never settlement of a live captured answer. |
| PC-38 | use published watermark while a preparation-failed interval exists. | T.`C519_Pending_intervals_never_overlap` | two intervals with different ends are disjoint and every fragment is received once. |
| PC-39 | enumerate only in-memory dispatched roots. | T.`C519_Restart_recovers_reserved_tail` | late fragment recovers after provider reconstruction with no new event. |
| PC-40 | drop pending tail when a newer prompt exists. | T.`C519_Next_prompt_keeps_an_already_reserved_tail` | same-batch tail is received; initial and next-turn texts are not replayed. |
| PC-41 | return on tail NO_REPLY without advancing reservation cursor. | T.`C519_Suppressed_tail_advances_cursor_without_publication` | cursor advances exactly to silent fragment end; later legitimate fragment survives; PublishedAt stays null. |
| PC-42 | move PublicationAttempts save after SendAsync. | B.`C519_Attempt_commit_precedes_producer` | entry observer sees Publishing and incremented count; commit refusal yields zero entries. |
| PC-43 | omit lease-expiry predicate in claim update. | B.`C519_Only_one_live_lease_can_claim` | held winning producer has exactly one entry after a second independent pump tick. |
| PC-44 | remove final lease/version/state check after entry barrier. | B.`C519_Final_entry_checks_current_lease` | old owner sends zero messages after takeover. |
| PC-45 | reload latest version and commit old owner's success without owner check. | B.`C519_Late_outcome_cannot_overwrite_new_owner` | late accepted result cannot replace new owner's Uncertain state or settle sources. |
| PC-46 | fire-and-forget SendAsync then commit Published. | B.`C519_Producer_completion_is_awaited` | while producer task is held PublishedAt and settlement are null. |
| PC-47 | map OperationCanceledException after entry to Published. | B.`C519_Cancellation_is_never_success` | before-entry zero attempts; after-entry cancellation/lost result => Uncertain, no automatic extra calls. |
| PC-48 | remove linked send deadline. | B.`C519_Send_deadline_bounds_the_attempt` | after advancing send timeout attempt task is complete and Uncertain while host token is live. |
| PC-49 | include PublishUncertain in automatic Ready send candidates. | B.`C519_Uncertain_never_automatically_retries` | two timer cycles and restart add zero calls to an uncertain delivery. |
| PC-50 | remove acknowledgePossibleDuplicate rejection. | B.`C519_Retry_requires_duplicate_acknowledgement` | false acknowledgement throws validation and state/attempt budget stay unchanged. |
| PC-51 | set PublicationAttempts to zero in RetryUncertainAsync. | B.`C519_Explicit_retry_keeps_lifetime_attempts` | lifetime 3 becomes 4 on next entry; new budget base remains 3. |
| PC-52 | classify generic IOException/timeout as queue refusal. | B.`C519_Only_definite_queue_refusal_is_automatic` | sync/async/serialization/accepted-then-fault all become Uncertain; Local_QueueFull becomes due Ready. |
| PC-53 | route MsgSizeTooLarge through generic uncertain handling. | B.`C519_Size_refusal_is_terminal` | broker size refusal is Failed with one loss episode and zero automatic retry. |
| PC-54 | remove NextAttemptAt comparison. | B.`C519_Retry_waits_until_persisted_due_time` | at due-minus-one tick producer entries unchanged; equality adds one attempt after restart. |
| PC-55 | reset budget base after each refusal. | B.`C519_Publication_cap_survives_restart` | three queue refusals then no fourth call across failed incident save and restart. |
| PC-56 | omit Enabled comparison in final binding validation. | B.`C519_Binding_disabled_holds_at_final_entry` | disable after early validation => Held and zero sends. |
| PC-57 | omit inbound-agent comparison in final binding validation. | B.`C519_Binding_owner_holds_at_final_entry` | rebind to same-project agent after early validation => Held and zero sends. |
| PC-58 | omit current-project comparison in final binding validation. | B.`C519_Binding_project_holds_at_final_entry` | move same agent to different project after early validation => Held and zero sends. |
| PC-59 | skip final profile revalidation before creating task. | M.`C519_Revocation_prevents_converter_creation` | revoked capture creates zero converter tasks and later sends original bytes. |
| PC-60 | skip final profile revalidation before publication. | B.`C519_Revocation_prevents_converted_publication` | revoked Ready delivery sends original only; converted PDF absent. |
| PC-61 | exclude those states from older-head query. | B.`C519_Captured_held_and_uncertain_heads_preserve_order` | later Ready has zero sends while each head blocks, including equal timestamps. |
| PC-62 | always set resumed Held to Ready. | B.`C519_Resume_held_restores_the_original_phase` | unmaterialized Held resumes Captured; unconverted staged resumes Pending; converted resumes Ready. |
| PC-63 | omit destination admission lock around conversion admission count. | M.`C519_Concurrent_materialization_respects_max_pending` | two competing qualifying captures with MaxPending=1 yield one Pending and one annotated overflow. |
| PC-64 | remove same-converter active check. | M.`C519_Converter_capacity_is_serialized` | second delivery remains Pending while first owns converter; no second task. |
| PC-65 | remove global Converting count limit. | M.`C519_Global_conversion_capacity_is_bounded` | third distinct converter remains Pending while first two are active. |
| PC-66 | replace frozen DeadlineAt with NextAttemptAt or refresh it on preparation retry. | M.`C519_Conversion_deadline_is_not_retry_time` | deadline equality creates no worker; late output ignored; original annotated fallback sends. |
| PC-67 | commit state/settlement before incident transaction. | F.`C519_Loss_and_source_outcome_are_atomic` | injected incident insert failure rolls back loss transition and settlement; retry records once. |
| PC-68 | commit incident/state before Alert insertion. | F.`C519_Loss_requires_its_alert` | alert insert failure leaves no committed terminal transition/incident stamp. |
| PC-69 | emit Warning instead of Critical. | F.`C519_Loss_is_critical_and_identifiable` | independent incident/alert query reads Critical and original delivery/source identity. |
| PC-70 | dedupe only by querying existing incident rows. | F.`C519_Failure_episode_survives_history_pruning` | prune incidents then scan/restart twice: no new alert or episode. |
| PC-71 | return without recording when current owner lookup fails. | F.`C519_Missing_owner_still_records_loss` | deleted/unowned cases still have one incident and alert with nullable AgentId and source identity. |
| PC-72 | await control notice before committing loss and return on its error. | F.`C519_Notice_failure_cannot_erase_loss` | notice refusal/disabled channel retains incident+alert and creates zero agent obligations. |
| PC-73 | format Uncertain with the definite no-answer loss wording. | F.`C519_Uncertainty_message_does_not_claim_nondelivery` | accepted-before-result message says acceptance unknown/may have published and not no turn completed. |
| PC-74 | save Published before source-settlement transaction. | P.`C519_Acceptance_and_all_settlements_commit_together` | member-save failure leaves no Published commit, all member settlements null, then Uncertain without resend. |
| PC-75 | move catalog/bundle repair into send-retry catch and requeue Published. | P.`C519_Projection_repair_never_reenters_producer` | catalog/filesystem/bundle-save failures repair after restart with exactly one accepted envelope. |
| PC-76 | apply old preview without accepted-order comparison. | P.`C519_Projection_repair_cannot_regress_preview` | newer LastReplyAt/preview remain after old repair. |
| PC-77 | stamp whenever a SourceTaskId/manifest exists. | P.`C519_Bundle_stamp_uses_frozen_complete_actual_payload` | omitted/wrong-hash/partial-zip payloads have null delivered stamps; complete actual payload stamps despite later file deletion. |
| PC-78 | repair only delivery.SourceTaskId and ignore other captured members. | P.`C519_Projection_repairs_every_implied_task` | two complete implied tasks stamp; omitted third remains null. |
| PC-79 | remove outbound exclusion from session pruning. | L.`C519_Session_retention_preserves_recovery_evidence` | stale terminal session survives without persistent-pointer/task/inbound protections. |
| PC-80 | omit outbound predicate in locked transcript-delete recheck. | L.`C519_Transcript_retention_rechecks_under_lock` | capture/retention race keeps original prompt/window rows. |
| PC-81 | protect Channel only, dropping machine predicate. | L.`C519_Queue_retention_keeps_open_machine_and_channel_sources` | old eligible machine queue rows survive; conclusively ineligible closed companion prunes. |
| PC-82 | drop outbound-reference exclusion from task-tree pruning. | L.`C519_Task_and_bundle_retention_keeps_referenced_inputs` | all referenced task rows/bundle paths remain readable while unrelated stale tree prunes. |
| PC-83 | treat Failed or Published-with-pending-repair as immediately prunable. | L.`C519_Delivery_and_file_retention_keeps_unresolved_ownership` | each unresolved delivery and hash-validated staged file survives retention. |
| PC-84 | mark root TailClosedAt merely because session is terminal. | L.`C519_Closed_roots_eventually_become_prunable` | terminal-but-incompletely-ingested tail remains open and later text recovers; fully examined companion prunes. |
| PC-85 | return success from finite-positive recovery-settings validation. | B.`C519_Settings_require_finite_positive_bounds` | individually invalid scan/page/pages/retry/send/lease/preparation/publication settings are rejected with property names. |
| PC-86 | change timeout >= lease rejection to timeout > lease. | B.`C519_Send_timeout_must_be_less_than_lease` | timeout==lease rejected; timeout one second shorter accepted. |
| PC-87 | requeue all target deliveries when any target fails. | U.`C519_Accepted_target_is_independent_of_failed_sibling` | A has exactly one receipt across B retry; only B remains unresolved. |
| PC-88 | translate Deferred to Published in dispatcher/runtime outcome. | U.`C519_Deferred_runtime_releases_without_claiming_publication` | held converter leaves source unsettled/preview null and next queued prompt has complete UserPrompt. |
| PC-89 | include Published rows in recovery send selection. | X.`C519_Published_death_never_replays` | after published-committed kill and two fresh recoveries recipient count remains one. |
| PC-90 | discovery enumerates delivery rows only. | X.`C519_Before_capture_death_is_discovered` | after capture-before-commit kill, fresh hosted recovery finds original answer and receiver gets it. |
| PC-91 | drain only in-memory capture notifications. | X.`C519_Captured_death_needs_no_wake_signal` | captured-before-signal kill still yields one recovered receiver receipt. |
| PC-92 | treat existing incomplete directory as staged and skip preparation. | X.`C519_Partial_stage_death_retries_preparation` | partial-stage kill recovers complete original attachment receipt, never empty/partial payload. |
| PC-93 | delete completed directory before recovery preparation. | X.`C519_Complete_stage_death_preserves_snapshot` | after complete-stage kill and source replacement receipt retains original bytes. |
| PC-94 | convert expired Publishing to Ready. | X.`C519_Attempt_death_stays_uncertain` | after attempt-committed or producer-entered kill, zero automatic sends and visible Uncertain. |
| PC-95 | automatically retry expired Publishing after acceptance. | X.`C519_Accepted_death_stays_uncertain` | real consumer saw one; two recovery processes and gateway observe no second receipt before acknowledged retry. |
| PC-96 | exclude already-linked queued conversion tasks from dispatch recovery. | W.`C519_Converter_handoff` | after failed enqueue/restart, exactly the original task receives its complete UserPrompt and final converted payload reaches adapter. |
| PC-97 | spend the whole tick budget on discovery before considering other work. | D.`C519_Each_work_kind_gets_a_bounded_share` | with a continuously full discovery prefix, due reply and pending metadata repair each complete within one cycle's reserved share. |
| PC-98 | remove Take(PageSize) from source discovery query. | D.`C519_Each_discovery_page_is_bounded` | reader/interceptor records at most 32 returned candidates per page. |
| PC-99 | remove both destination lock and root version comparison on tail reservation, retaining exact-key uniqueness. | T.`C519_Concurrent_reservation_has_one_winner` | barrier-raced different-end intervals have one owner for overlapping text and no duplicate fragment receipt. |
| PC-100 | leave FailureEpisode unchanged on acknowledged retry. | F.`C519_Acknowledged_retry_starts_a_new_failure_episode` | second authorized failed attempt yields a second episode/alert; repeated scans add no third. |

For schema controls, change only the named model/enum/migration behavior. Generate
migration changes through the EF CLI and use a freshly migrated isolated database
for every phase; different SourceKeys prevent the older exact-key constraint from
masking a missing root/start index. Restore all generated tracked files exactly.
For guarded early returns, keep valid companion setup and assert the target barrier
was reached before the decisive negative assertion. Mutation is serial for shared
production files; optional method-level batching is permitted only for independent
files/methods and still needs separate per-PC receipts.

Inventory: **guards=100, mapped=100, missing=0, duplicate PC maps=0**.
All guards are mapped; none is exempted for inconvenience. All 100 PCs have an exact
detecting method and compiling defect. No PC execution is claimed by TestDesign.

#### S3 prerequisite controls (CARD-1059)

The original PC-1 through PC-100 remain unchanged and pending. S3 adds six
independent source-read controls below; all are also pending for post-land
method-scoped SourceLanding Mutation. These are additive controls, not ordinary
Code mutant runs. Their ordinary methods are the four new CP-3 results.

| PC/variant | Compiling defect | Exact detecting method | Intended assertion red |
|---|---|---|---|
| PC-1059-1 / roots | Bypass the allowed-root membership predicate in ChannelReplyAttachmentReader.ValidatePath. | ChannelOutboundStorageTests.`C1059_Source_reads_require_captured_roots_without_traversal` | An existing regular file with only a nonmatching sibling root is refused; authorized companion reads the original bytes. |
| PC-1059-2 / traversal | Remove the explicit dot/dot-dot component rejection, leaving canonical root membership intact. | ChannelOutboundStorageTests.`C1059_Source_reads_require_captured_roots_without_traversal` | The path containing ../allowed/source.txt is refused even though its normalized path is within the allowed root. |
| PC-1059-3 / file and directory links | Remove reparse rejection in ValidatePath and handle inspection; remove Linux O_NOFOLLOW and Windows OPEN_REPARSE_POINT flags for source directory/leaf opens. These are redundant fences for the same no-link invariant and must be removed together. | ChannelOutboundStorageTests.`C1059_Source_reads_reject_file_and_directory_links` | File link, directory link and linked allowed root are refused; unlinked companion reads successfully. |
| PC-1059-4 / pre-read budget | Remove the stream.Length > maxBytes pre-read refusal, retaining the streamed byte counter. | ChannelOutboundStorageTests.`C1059_Source_length_is_checked_before_reading_with_a_finite_budget` | A 1 GiB sparse file with a 256 KiB budget must produce the length refusal before touching its canceled read token, rather than entering ReadAsync. The exact-size companion reads. |
| PC-1059-5 / Linux regular type | Bypass the statx regular-file mode comparison and remove the redundant FileAttributes.Device precheck, retaining root and no-follow guards. Run on Linux. | ChannelOutboundStorageTests.`C1059_Source_reads_refuse_nonregular_files_without_blocking` | Device read is refused, never accepted as an empty attachment; FIFO also refuses promptly and the ordinary-file companion succeeds. |
| PC-1059-6 / Linux growing-file budget | Remove the streamed read > maxBytes - output.Length check, keeping the pre-read length check. Run on Linux. | ChannelOutboundStorageTests.`C1059_Source_length_is_checked_before_reading_with_a_finite_budget` | A file grows from eight to nine bytes after the valid eight-byte length check; the I/O barrier fires and the reader refuses the ninth byte. Windows holds a handle without write sharing instead. |

Supplemental inventory: guards=6, mapped=6, pending=6. Windows handle behavior
is implemented but S3's portable Linux receipt does not claim Windows execution.
Budget the six serial controls at three minutes per isolated build/method phase:
6 x (baseline 3 + red 3 + restore/green 3) = 54 additional minutes, 18 additional
phase runs. The combined post-land control budget is 106 controls / 318 phase
runs / 780 execution minutes, plus the existing 25-minute Mutation setup/report
allowance and slot waits. No repetition is required after each restored green.

### Out of scope

- Native provider acknowledgement, gateway crash/offset-loss durability and human
  reading: D-1 promises producer acceptance. Real queue-to-adapter receipt remains
  mandatory evidence for this card; no test may stop at enqueue/ack/Published.
- Paid agents, live broker/runner, operational deployment, UI changes and native
  transport implementation changes. Windows R-12 is retained at its actual fixture
  evidence boundary, not relabeled as real provider input.
- Automatic uncertainty retry, reconstruction of settled direct-send history, a
  second publication journal, new filesystem cleanup service, and backward binary
  activation after admitting Captured/Suppressed: excluded by the landed decisions.
- The Cartesian product of every filesystem fault with every origin/profile is
  not required: C/M cover common capture/preparation guards on all three send paths;
  U plus existing shape/gate tests cover profile/origin combinations. X covers every
  persistence cut on all three paths. Boundary/binding mutants isolate each predicate.
  A Code branch that bypasses a shared helper invalidates this reduction.
- Whole Unit/namespace/assembly, whole Herdr class and repeated Linux parity hang:
  no additional changed invariant justifies their cost. Existing selected classes
  still run in full where the shared fixture or dispatcher contract changes.


### Checkpoints

This is the closed ordinary scope. Every row owns one isolated build and exactly
one filter; no reused build or whole-Unit run. All rows are serial (including
builds); keep process assemblies sequential. Run on the slice commit named by
After, once its dependencies exist. Materialization assertions that need atomic loss wait for S8;
discovery/tail/retry/unified assertions that need projection work or outcome fencing
wait for S9. This schedules verification after its prerequisites, without rewriting the
implementation slices. Move fixture adaptation next to its first consuming slice
as the plan already permits.

All invocations must pass `--row-timeout 15m --total-timeout 60m --serial`.
Fifteen minutes includes each row's build and test; a timeout is incomplete, never
a pass. No automatic deadline widening. Estimates are warm-cache elapsed minutes,
not a timeout. If a method cannot fit, stop and revise this manifest before another
run. CP-29 alone requires Windows; run CP-1..CP-28 without a host/OS pin. Do not
select CP-29 in a Linux invocation. A missing Windows lane leaves R-12 outstanding.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c519-cp01/` | schema | `/*/*/ChannelOutboundDurabilitySchemaTests/*` | V-1 | all 4 listed results, 0 failed/skipped | 4 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c519-cp02/` | capture | `/*/*/ChannelOutboundCaptureTests/*` | V-2 | all 5 listed results, 0 failed/skipped | 5 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c519-cp03/` | storage | `/*/*/ChannelOutboundStorageTests/*` | R-7, CARD-1059 | all 32 listed results, 0 failed/skipped | 32 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S4 | `tests/Antiphon.Tests -> bin-c519-cp04/` | dispatch | `/*/*/ChannelOutboundDispatchIntegrationTests/*` | R-1 | all 10 listed results, 0 failed/skipped | 10 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S4 | `tests/Antiphon.Tests -> bin-c519-cp05/` | runtime | `/*/*/AgentTaskReplyIntegrationTests/Deferred_is_durable_and_releases_runtime` | R-1 | all 1 listed results, 0 failed/skipped | 1 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S8 | `tests/Antiphon.Tests -> bin-c519-cp06/` | materialize | `/*/*/ChannelOutboundMaterializationTests/*` | V-3 | all 12 listed results, 0 failed/skipped | 12 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | S8 | `tests/Antiphon.Tests -> bin-c519-cp07/` | loss | `/*/*/ChannelOutboundFailureRecordingTests/*` | V-8 | all 8 listed results, 0 failed/skipped | 8 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | S9 | `tests/Antiphon.Tests -> bin-c519-cp08/` | discovery | `/*/*/ChannelOutboundDiscoveryTests/*` | V-5 | all 14 listed results, 0 failed/skipped | 14 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-9 | S9 | `tests/Antiphon.Tests -> bin-c519-cp09/` | tails | `/*/*/ChannelOutboundTrailingRecoveryTests/*` | V-6 | all 5 listed results, 0 failed/skipped | 5 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | S9 | `tests/Antiphon.Tests -> bin-c519-cp10/` | retry | `/*/*/ChannelOutboundRetryPolicyTests/*` | V-7 | all 22 listed results, 0 failed/skipped | 22 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-11 | S9 | `tests/Antiphon.Tests -> bin-c519-cp11/` | projections | `/*/*/ChannelOutboundMetadataRepairTests/*` | V-9 | all 5 listed results, 0 failed/skipped | 5 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-12 | S9 | `tests/Antiphon.Tests -> bin-c519-cp12/` | unified | `/*/*/ChannelOutboundUnifiedPathTests/*` | V-4 | all 8 listed results, 0 failed/skipped | 8 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-13 | S10 | `tests/Antiphon.Tests -> bin-c519-cp13/` | retention | `/*/*/ChannelOutboundRetentionTests/*` | V-10 | all 6 listed results, 0 failed/skipped | 6 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-14 | S10 | `tests/Antiphon.Tests -> bin-c519-cp14/` | old-queue-retention | `/*/*/DataRetentionServiceTests/Queue_keeps_Pending_and_unsettled_channel_rows_and_deletes_settled_old_ones` | R-11 | all 1 listed results, 0 failed/skipped | 1 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-15 | S11a | `tests/Antiphon.Tests -> bin-c519-cp15/` | bridge | `/*/*/ChannelBridgeTests/*` | R-2 | all 40 listed results, 0 failed/skipped | 40 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-16 | S11a | `tests/Antiphon.Tests -> bin-c519-cp16/` | reply-durability | `/*/*/ChannelReplyDurabilityTests/*` | R-2 | all 25 listed results, 0 failed/skipped | 25 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-17 | S11a | `tests/Antiphon.Tests -> bin-c519-cp17/` | correlation | `/*/*/(ChannelPromptCorrelationTests*)\|(ChannelPromptCorrelationUnitTests*)\|(ChannelMachineTurnMatchTests*)/*` | R-3 | all 42 listed results, 0 failed/skipped | 42 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-18 | S11b | `tests/Antiphon.Tests -> bin-c519-cp18/` | machine-attachments | `/*/*/(ChannelMachineTurnTextTests*)\|(ChannelFollowUpAttachmentTests*)/*` | R-4 | all 45 listed results, 0 failed/skipped | 45 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-19 | S11b | `tests/Antiphon.Tests -> bin-c519-cp19/` | outbound-partials | `/*/*/ChannelOutboundDeliveryTests/*` | R-5 | all 38 listed results, 0 failed/skipped | 38 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-20 | S11b | `tests/Antiphon.Tests -> bin-c519-cp20/` | profile-contract | `/*/*/(ChannelOutboundPolicyTests*)\|(ChannelOutboundContractTests*)/*` | R-5 | all 31 listed results, 0 failed/skipped | 31 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-21 | S11b | `tests/Antiphon.Tests -> bin-c519-cp21/` | batching | `/*/*/ChannelBatchingTests/*` | R-10 | all 10 listed results, 0 failed/skipped | 10 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-22 | S11b | `tests/Antiphon.Tests -> bin-c519-cp22/` | deadlines | `/*/*/ChannelOutboundDeadlineTests/*` | R-6 | all 13 listed results, 0 failed/skipped | 13 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-23 | S12a | `tests/Antiphon.Tests -> bin-c519-cp23/` | crash-preparation | `/*/*/ChannelOutboundUnifiedCrashTests/(C519_Before_capture_death_is_discovered*)\|(C519_Captured_death_needs_no_wake_signal*)\|(C519_Partial_stage_death_retries_preparation*)\|(C519_Complete_stage_death_preserves_snapshot*)` | V-11 | all 12 listed results, 0 failed/skipped | 12 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-24 | S12a | `tests/Antiphon.Tests -> bin-c519-cp24/` | crash-publication | `/*/*/ChannelOutboundUnifiedCrashTests/(C519_Attempt_death_stays_uncertain*)\|(C519_Accepted_death_stays_uncertain*)\|(C519_Published_death_never_replays*)` | V-12 | all 9 listed results, 0 failed/skipped | 9 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-25 | S12b | `tests/Antiphon.Tests -> bin-c519-cp25/` | queue-recipient | `/*/*/ChannelOutboundUnifiedTransportTests/C519_Queue_to_adapter*` | V-13 | all 3 listed results, 0 failed/skipped | 3 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-26 | S12b | `tests/Antiphon.Tests -> bin-c519-cp26/` | transport-handoffs | `/*/*/ChannelOutboundUnifiedTransportTests/(C519_Size_refusal*)\|(C519_Converter_handoff*)` | V-13 | all 2 listed results, 0 failed/skipped | 2 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-27 | S12b | `tests/Antiphon.Tests -> bin-c519-cp27/` | composed-files | `/*/*/ChannelOutboundComposedTransportTests/*` | R-9 | all 1 listed results, 0 failed/skipped | 1 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-28 | S12b | `tests/Antiphon.Tests -> bin-c519-cp28/` | manual-recovery | `/*/*/ChannelOutboundRecoveryTests/(Expired_publishing_lease_is_uncertain_until_explicit_retry*)\|(Held_head_blocks_later_reply_until_original_binding_is_repaired_and_resumed*)\|(Restart_preserves_two_inbound_slack_routes_behind_an_uncertain_head*)\|(Broker_ack_before_process_death_remains_uncertain_without_replay*)` | R-8 | all 4 listed results, 0 failed/skipped | 4 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-29 | S13 | `tests/Antiphon.Tests -> bin-c519-cp29/` | windows-parity | `/*/*/HerdrAlwaysOnChannelParityTests/AlwaysOn_channel_bound_survives_child_death_and_replies*` | R-12 | all 2 listed results, 0 failed/skipped | 2 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-30 | S5 | `tests/Antiphon.Tests -> bin-c519-cp30/` | discovery-s5 | `/*/*/ChannelOutboundDiscoveryTests/*` | V-5 (S5 subset) | all 16 S5 results, 0 failed/skipped | 16 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-31 | S5 | `tests/Antiphon.Tests -> bin-c519-cp31/` | dispatch-s5 | `/*/*/ChannelOutboundDispatchIntegrationTests/*` | R-1 (dispatcher) | all 10 results, 0 failed/skipped | 10 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-32 | S5 | `tests/Antiphon.Tests -> bin-c519-cp32/` | correlation-s5 | `/*/*/(ChannelPromptCorrelationTests*)\|(ChannelPromptCorrelationUnitTests*)\|(ChannelMachineTurnMatchTests*)/*` | R-3 | all 42 results, 0 failed/skipped | 42 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-33 | S5 | `tests/Antiphon.Tests -> bin-c519-cp33/` | capture-s5 | `/*/*/ChannelOutboundCaptureTests/*` | V-2 (capture regression) | all 5 results, 0 failed/skipped | 5 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-34 | S6 | `tests/Antiphon.Tests -> bin-c519-cp34/` | tails-s6 | `/*/*/ChannelOutboundTrailingRecoveryTests/*` | V-6, V-5 root subset | all 8 S6 results, 0 failed/skipped | 8 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-35 | S6 | `tests/Antiphon.Tests -> bin-c519-cp35/` | dispatch-s6 | `/*/*/ChannelOutboundDispatchIntegrationTests/*` | R-1 dispatcher | all 10 results, 0 failed/skipped | 10 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-36 | S6 | `tests/Antiphon.Tests -> bin-c519-cp36/` | discovery-s6 | `/*/*/ChannelOutboundDiscoveryTests/*` | V-5 source subset | all 16 S5 results, 0 failed/skipped | 16 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-37 | S6 | `tests/Antiphon.Tests -> bin-c519-cp37/` | capture-s6 | `/*/*/ChannelOutboundCaptureTests/*` | V-2 capture regression | all 5 results, 0 failed/skipped | 5 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-38 | S6 | `tests/Antiphon.Tests -> bin-c519-cp38/` | outbound-s6 | `/*/*/ChannelOutboundDeliveryTests/*` | R-5 ordering/legacy regression | all 38 results, 0 failed/skipped | 38 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-39 | S6 | `tests/Antiphon.Tests -> bin-c519-cp39/` | policy-s6 | `/*/*/(ChannelOutboundPolicyTests*)\|(ChannelOutboundContractTests*)/*` | R-5 binding/conversion regression | all 31 results, 0 failed/skipped | 31 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-40 | S6 | `tests/Antiphon.Tests -> bin-c519-cp40/` | deadlines-s6 | `/*/*/ChannelOutboundDeadlineTests/*` | R-6 conversion deadlines | all 13 results, 0 failed/skipped | 13 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-41 | S7 | `tests/Antiphon.Tests -> bin-c519-cp41/` | retry-s7 | `/*/*/ChannelOutboundRetryPolicyTests/*` | V-7 S7 subset | all 22 results, 0 failed/skipped | 22 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-42 | S7 | `tests/Antiphon.Tests -> bin-c519-cp42/` | recovery-s7 | `/*/*/ChannelOutboundRecoveryTests/(Expired_publishing_lease_is_uncertain_until_explicit_retry*)\|(Held_head_blocks_later_reply_until_original_binding_is_repaired_and_resumed*)\|(Resume_held_after_conversion_returns_to_ready*)` | R-8 S7 manual recovery | all 3 results, 0 failed/skipped | 3 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-43 | S7 | `tests/Antiphon.Tests -> bin-c519-cp43/` | outbound-s7 | `/*/*/ChannelOutboundDeliveryTests/*` | R-5 default-off publication/ordering | all 38 results, 0 failed/skipped | 38 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-44 | S7 | `tests/Antiphon.Tests -> bin-c519-cp44/` | policy-s7 | `/*/*/(ChannelOutboundPolicyTests*)\|(ChannelOutboundContractTests*)/*` | R-5 binding/conversion | all 31 results, 0 failed/skipped | 31 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-45 | S7 | `tests/Antiphon.Tests -> bin-c519-cp45/` | deadlines-s7 | `/*/*/ChannelOutboundDeadlineTests/*` | R-6 conversion deadlines | all 13 results, 0 failed/skipped | 13 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-46 | S7 | `tests/Antiphon.Tests -> bin-c519-cp46/` | dispatch-s7 | `/*/*/ChannelOutboundDispatchIntegrationTests/*` | R-1 dispatcher | all 10 results, 0 failed/skipped | 10 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-47 | S7 | `tests/Antiphon.Tests -> bin-c519-cp47/` | tails-s7 | `/*/*/ChannelOutboundTrailingRecoveryTests/*` | V-6 trailing retry regression | all 8 results, 0 failed/skipped | 8 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-48 | S7 | `tests/Antiphon.Tests -> bin-c519-cp48/` | materialization-s7 | `/*/*/ChannelOutboundMaterializationTests/*` | V-3 S3 preparation subset | all 12 results, 0 failed/skipped | 12 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-49 | S7 | `tests/Antiphon.Tests -> bin-c519-cp49/` | discovery-s7 | `/*/*/ChannelOutboundDiscoveryTests/*` | V-5 existing S5 subset | all 16 results, 0 failed/skipped | 16 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Floors come from the specified new roster and inspected source attributes:
S=4, C=5, M=12, U=8, D=14, T=5, B=22, F=8, P=5, L=6, X=21, W=5.
The seven X methods each expand to three path results; cut variants remain internal
assertions. Existing Delivery has 18 methods / 38 results across its five partial
files, including SendShape and Gate. Existing Deadline has nine methods / 13 results
across its two files. All other existing floors are shown in R-1..R-12.
Table total = **404 minimum executed TUnit results**; no loop/child/assertion counts
are smuggled into Min. If Code adds/removes arguments, update the explicit roster,
floor and cost before running; do not lower a floor to excuse skipped tests.

Run the checkpoint tool per committed slice group through the build-slot gate.
The first group command is concrete:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c519-s1 -- dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-04-card-0519-unified-outbound-recovery-plan.md --after S1 --expected-source-sha (git rev-parse HEAD) --row-timeout 15m --total-timeout 60m --serial
```

Use the corresponding literal After value for later groups; select CP-29 only on
the Windows Code dispatch. Await every exit-75 run with the checkpoint tool's
wait command (`--max-wait 50s`) until terminal, also through the gate.
Never end a task with its executor still running. Slot refusal/timeout is reported,
never bypassed. Tool bootstrap and EF migration generation are the only planned
non-CP build/setup drivers; gate them and report them separately.

Freeze source during every run. Preserve unedited CHECKPOINT lines, method roster,
actual source/build SHA and counts; validate clean receipts for the exact reviewed
SHA. Early slice receipts retain their own SHA. Final qualification cannot relabel
them as the final candidate: if later source changes invalidate their build binding,
rerun those CP rows on the final frozen candidate and report that required rerun
cost separately. Keep generated receipts/TRX ignored. Code/Review run
`scripts/check-evidence-diff.ps1` over the full task range. Remove only the
manifest-owned alternate outputs. Diagnose inherited red at the recorded base
using the failing exact method, never a whole assembly.

### Cost

All numbers below are **estimates**, not measured test results. The ordinary
Code V/R floor is **178 minutes**, exactly the sum of CP-1..CP-29
EstimatedMinutes. It includes **58 build minutes** (29 isolated warm builds at
2 minutes each) and **120 test minutes**. Setup/tool bootstrap and EF migration
generation add **12 minutes**. No additional whole-suite run is budgeted.

The exact ordinary filters are in the table; by group:
schema/capture 8; storage 5; dispatch/runtime 9; materialize/discovery/loss 17;
tails/retry/projections/unified 23; retention pair 9; bridge/durability/correlation
20; machine-attachments/outbound-partials/profile-contract/batching/deadlines 33;
crash preparation/publication 16; queue-recipient/transport-handoffs/composed-files/
manual-recovery 30; Windows parity 8. Sum **178**.

The 178-minute floor is one execution of every row. It can qualify one final frozen
candidate if Code defers these full rows until all source is ready. If Code uses
the intended per-slice checkpoints before later source edits, commission a final
frozen-candidate qualification of the same 29 rows as well: **178 additional
minutes**, **354 ordinary minutes total**. This is a source-provenance rerun, not
discretionary broadening. Record both receipts at their real SHAs. Changed/failing
rows may add further measured repair runs; never quietly count them as zero.

The original Mutation method filters are the 100 class/method mappings in the PC table;
the six CARD-1059 controls above add their exact method-scoped filters and 54-minute budget:

| PC filters | Controls | Per-control baseline/build + red/build + restore/build/green | Estimated minutes |
|---|---:|---|---:|
| PC-1..PC-5 (S methods; schema regeneration and fresh migration each phase) | 5 | 4 + 4 + 4 = 12 | 60 |
| PC-89..PC-95 (X methods; three path results plus internal crash cuts per phase) | 7 | 6 + 6 + 6 = 18 | 126 |
| PC-96 (W.C519_Converter_handoff; real queue/broker/recipient per phase) | 1 | 6 + 6 + 6 = 18 | 18 |
| PC-6..PC-88 and PC-97..PC-100 (the remaining exact methods) | 87 | 2 + 2 + 2 = 6 | 522 |
| **Mutation execution floor** | **100** | **300 method-scoped phase runs; no class/suite PCs** | **726** |

Each phase estimate includes its isolated warm incremental build and method run;
the restore/green phase includes byte restoration and fresh build. Mutation
discovery, schema tooling, receipt inspection and external restoration report add
**25 minutes**: commission Mutation for at least **751 minutes** plus slot waits.
No parallel-shard saving is assumed; most controls touch the same production files.

Verification execution total = setup 12 + ordinary builds 58 + ordinary tests 119
+ PC cycles 726 = **916 minutes**. With intended early slices and one required final
source qualification, that becomes **1,094 minutes**. Add Code authoring **885**
(the landed 30–60 minute slices), ordinary Review **30**, and Mutation setup/report
**25**: end-to-end planning floor **1,856 minutes**, or **2,034 minutes** with the
explicit final qualification pass. Slot waits, cold image pulls, native capacity
waits and repair are additional measured costs, not passing evidence.

Assumed savings are **0 minutes**: every checkpoint owns a build, every independent
guard gets a control and the native row remains. Narrow filters prevent the old
whole-Unit/whole-parity cost without pretending an unmeasured hang is saved runtime.
If a row exceeds its estimate, record actual time and split its named scope within
the same bounded manifest; do not expand timeout or replace an assertion.

TestDesign handoff audit: inspected bodies and nearest new-file fixtures recorded;
**guards=100, mapped=100, missing=0, duplicate PC maps=0; all PCs executable** as
specified Code deliverables; **29 CP rows, 401 ordinary result floor, 177 ordinary
minutes, 726 PC minutes**. No build/test/PC result is claimed by this documentation
stage. Commit/push this appendix on the assigned TestDesign branch; caller lands
the Succeeded task promptly, then commissions Code with this exact artifact.

TestDesign validation: append-only comparison against the landed plan, whitespace
check, table/coverage/count audit and guard-map audit passed. The repository's real
checkpoint importer built through the host slot gate and imported all **29 rows**
successfully (exit 0; tool-only build, 7 seconds holding the slot). It emitted one
existing CS8602 warning in TaskOwnerGuard.cs; no application tests or PCs ran.
Generated YAML remains ignored and the owned alternate tool output is removed.

### S4 activation and checkpoint roster amendment (2026-10-05)

`ChannelOutbound:UnifiedRecoveryEnabled` defaults to false. Program registers the
captured preparation reader/helper only when enabled. The required outbound service
owns all dispatcher sends; default-off preserves the existing service's direct or
conversion admission decision. Enabled main/machine paths describe then capture,
without attachment or bundle-manifest reads in dispatch. Materialization resolves
only undelivered implied bundle task descriptors, through the captured-root reader.
Trailing sends use the existing service admission path when enabled; durable root
reservation/discovery is still S5/S6. The switch must remain off operationally until
the full deployable recovery unit is qualified. AppHost activation is caller-owned.
No migration is needed in S4.

The executable S4 selection is `After=S4`: CP-4 and CP-5. The older allocation's
CP-21 is superseded by the appended manifest (where CP-21 now belongs to S11b).
CP-4's exact roster is Dispatcher_defers_only_the_bound_conversation_and_preserves_source_bytes
(false/default and true arguments), Activation_captures_before_source_reads_and_publishes_the_staged_bytes,
and Activation_preserves_machine_silence_and_origin_policy_before_capture: four
native results. CP-5 retains its single Deferred_is_durable_and_releases_runtime
result, now exercising captured machine intent and explicit materialization before
the held converter. S4 total: five results, nine estimated minutes (two isolated
builds); the full table becomes 404 results / 178 minutes. The prior TestDesign
audit above is historical. V-4's complete matrix stays scheduled at S9; this slice
qualifies only R-1 and the named activation checks. No PC is discharged here.

| PC/variant (all pending SourceLanding Mutation) | Deliberate guarded defect | Exact ordinary witness |
|---|---|---|
| PC-S4-1 / default off | Change UnifiedRecoveryEnabled's default to true. | ChannelOutboundDispatchIntegrationTests.Dispatcher_defers_only_the_bound_conversation_and_preserves_source_bytes: false argument omits assigning the switch; passthrough remains immediate and conversion is already Pending. |
| PC-19 / S4 main and machine activation | Bypass capture and publish the activated dispatcher reply directly. | ChannelOutboundDispatchIntegrationTests.Activation_captures_before_source_reads_and_publishes_the_staged_bytes; AgentTaskReplyIntegrationTests.Deferred_is_durable_and_releases_runtime: capture/member commit, zero producer calls and zero preparation attempts precede any source reads. |
| PC-15 / S4 staged source | Read current sources again on Ready publication. | ChannelOutboundDispatchIntegrationTests.Activation_captures_before_source_reads_and_publishes_the_staged_bytes: replace the source after staging; receiver retains original bytes and reader count stays one. |
| PC-22 / S4 silence | Admit exact machine NO_REPLY as ordinary text. | ChannelOutboundDispatchIntegrationTests.Activation_preserves_machine_silence_and_origin_policy_before_capture: silent machine member stays unowned/unsettled; eligible Check companion publishes. |
| PC-23 / S4 origin | Admit System plain text despite the Check-only policy. | ChannelOutboundDispatchIntegrationTests.Activation_preserves_machine_silence_and_origin_policy_before_capture: System stays unowned/unsettled; eligible Check companion publishes. |

Original PC-1..PC-100 and PC-1059-1..PC-1059-6 remain pending unchanged. These
S4 variants supplement their eventual complete witnesses; no deliberate mutant
runs belong to this Code task. Method-scoped cycles remain Mutation-owned.

### S4 catalog-less Review repair (Code task 530ed557, 2026-10-05)

Review `aa7cf6db` found that enabling unified recovery broke the dispatcher's
supported conversation-id route when there was no ChatChannels row. This repair
brief overrides D-8's missing-catalog loss proposal for S4: main, machine and
trailing replies without a catalog row must retain one PublishDirectAsync call,
ordinary body/attachment preparation and direct source settlement, with no capture
or queued delivery. Catalog-backed enabled replies retain capture/admission.
Default-off behavior and operational activation remain unchanged.

CP-4 adds Missing_catalog_main_publishes_once_and_settles_without_capture,
Missing_catalog_machine_publishes_once_and_settles_without_capture, and
Missing_catalog_trailing_publishes_once_without_capture, each with false/true
switch arguments. Each observes complete text/attachment bytes, exactly one
producer receipt across redispatch/pump, settled queue sources, no delivery link
or delivery row, and no fabricated catalog. The trailing test starts with NO_REPLY
so a main-path defect cannot prevent it from reaching its own send guard.
The existing activation witnesses retain catalog-backed capture coverage.
CP-4 now requires ten results; CP-5 still requires one. Run these two rows once
at the repair tip, serially, with the existing deadlines. No whole Unit or Linux
Herdr selection belongs to this repair brief. One CP-4 run on committed baseline
production plus the new tests is allowed solely to establish the real regression
red before fixing it; it is not a deliberate mutant or a PC discharge.

R-1 is the ordinary repair scope. V-4 is covered only for these named S4 activation
and missing-catalog cases; its full S9 matrix stays deferred, as do V-1..V-3,
V-5..V-13 and R-2..R-12. No manual activation is required; restart is caller-owned
and the switch stays off. The CP-5 flake budget is unchanged: wait for the actual
converter-held condition before timing runtime release, excluding setup and
materialization from that five-second check.

| PC/variant (all pending SourceLanding Mutation) | Deliberate guarded defect | Exact ordinary witness |
|---|---|---|
| PC-S4-2 / catalog-less main | Always select capture for enabled main replies, even without a catalog row. | ChannelOutboundDispatchIntegrationTests.Missing_catalog_main_publishes_once_and_settles_without_capture (true): Published outcome, exactly one complete receipt and settled unowned source. |
| PC-S4-3 / catalog-less machine | Always select capture for enabled machine replies, even without a catalog row. | ChannelOutboundDispatchIntegrationTests.Missing_catalog_machine_publishes_once_and_settles_without_capture (true): exactly one complete receipt and settled unowned injection. |
| PC-S4-4 / catalog-less trailing | Refuse direct send from SendAsync when enabled and the catalog row is absent. | ChannelOutboundDispatchIntegrationTests.Missing_catalog_trailing_publishes_once_without_capture (true): exactly one complete trailing receipt and no delivery row. |

All original PC-1..PC-100, PC-1059-1..PC-1059-6 and S4 variants remain pending.
PC-20's original atomic-loss witness is not implemented or claimed by this repair;
the revised S4 catalog-less contract above governs its new variants. Mutation and
the caller own reconciliation of the later full-plan missing-catalog control.

### S5 source-discovery checkpoint amendment (Code task 395283bc, 2026-10-05)

The original allocation's S5/CP-5 is superseded by the executable manifest,
where CP-5 belongs to S4 and full discovery CP-8 waits for S9 prerequisites.
S5 now runs only After=S5: CP-30, CP-31, CP-32 and CP-33, serial, once on its committed
tip, with unchanged 15-minute row and 60-minute total deadlines. This adds
22 estimated minutes; no whole Unit/assembly or Windows parity run.

CP-30 has sixteen single-result methods in ChannelOutboundDiscoveryTests:
C519_Startup_recovers_without_signal; C519_Timer_recovers_without_signal;
C519_Default_off_does_not_discover; C519_Historical_match_requires_complete_prompt;
C519_Historical_match_requires_marker; C519_Historical_match_requires_source_session;
C519_Historical_match_obeys_attempt_floors; C519_Historical_native_time_obeys_original_attempt;
C519_Historical_withholding_does_not_hide_a_later_receipt;
C519_Historical_answer_stops_at_next_prompt; C519_Machine_context_must_predate_injection;
C519_Discovery_closure_waits_for_complete_window; C519_Legacy_settled_sources_are_not_replayed;
C519_Fair_cursors_and_finite_source_budget; C519_Closed_machine_source_cannot_be_captured;
C519_Complete_machine_batch_has_one_owner_for_every_member. The receipt is the complete fake producer
envelope after the real capture/materialization/pump path, plus independently
reloaded source/journal outcomes. It is not Kafka/gateway/provider receipt.

This covers the S5 part of V-5: startup and timer with an observed hosted-cycle
barrier and manual clock; historical full-body/marker/session/attempt matching;
original next-prompt windows; prior versus later machine context; complete
next-prompt policy closure and open late text; legacy settled history; and a
321-source withheld prefix, at most 320 examined source candidates per cycle,
with the eligible companion reached by cycle two. Prompt lookup is independently
paged at 32 per candidate with process-local cursors. Capture remains the only
durable ownership authority. Event dispatch and discovery reuse turn extraction,
policy and capture; enabled machine dispatch now requires complete delivery evidence.
CP-31 adapts its two enabled machine fixtures to supply that evidence; its
default-off argument stays unchanged. CP-32 retains every selected matcher result.

Full V-5 remains pending CP-8/S9: root/tail discovery (S6), TTL capture/loss
serialization (S8), metadata fairness (S9), terminal transcript-completeness
closure (requires an authoritative ingestion-complete contract), and the full
per-page interceptor census. S5 conservatively leaves terminal machine windows
open; neither session terminal state nor a last transcript row proves ingestion
complete. No later slice is implemented by this amendment. R-1's runtime case
is unchanged and retains its S4 qualification; only its dispatcher class is rerun
here. V-1..V-4, V-6..V-13 and R-2/R-4..R-12 remain at their owning slices.
No manual activation is required. The switch stays false; restart is caller-owned.

All PC-1..PC-100, PC-1059-1..PC-1059-6, PC-S4-1..PC-S4-4 and their S4
variants stay pending for post-land SourceLanding Mutation. S5 supplies ordinary
witnesses for PC-25..PC-32 and the open/next-prompt part of PC-33; its combined
finite/fair method supplies partial witnesses for PC-34/PC-35/PC-98. Exact
full-witness methods and missing-control discovery remain Mutation's responsibility.
CARD-1061's hard-link reader gap remains unchanged and is not claimed fixed.

S5 repair group 1: the first CP-32 run on 8e44520a7f2b786e8cd3dd228cc7ee43bc7b98fa
found C584_RestartAndProducerFailure red (41/42 passed). A method-only isolated
run at task base 90cd74bb5289c438ae0201b6a2dfae7f86aaccaa reproduced the same
settlement assertion (0/1 passed): S4 had moved the producer into the service
while this fixture still injected through the unused dispatcher constructor.
The repaired fixture injects the producer into DI, explicitly observes the failed
entry, then enables its real fake receiver; all original reopen/retry assertions
remain. This diagnostic baseline is the sole non-manifest test, justified to
classify inherited red; no timeout/assertion relaxation or retry was added.

The same repair group makes policy closure authoritative at event dispatch and
inside the capture transaction (reload under source-row lock plus conditional
claim). CP-33 runs the full five-result capture class for that changed shared
service. Complete machine batches retain all members: the opening complete
receipt, common persisted attempt and entire composed batch are required, so
quoted headers alone cannot add members. The two additional CP-30 methods and
closure method extension exercise these guards with eligible companions.
One final After=S5 run on the next committed tip requalifies all four rows;
no rebuild per individual fix and at most one further repair round if necessary.

| PC/variant (pending SourceLanding Mutation) | Deliberate guarded defect | Exact ordinary witness |
|---|---|---|
| PC-S5-1 / event closure | Remove MatchMachineSources' closed-source exclusion. | ChannelOutboundDiscoveryTests.C519_Discovery_closure_waits_for_complete_window: later attachment turn cannot even enter capture for the closed System source; observed capture-admission member IDs exclude it, while the fresh attachment companion enters capture and publishes. |
| PC-S5-2 / transactional closure | Remove both closed-source rejection and the redundant closure predicate on conditional member assignment in CaptureAsync. | ChannelOutboundDiscoveryTests.C519_Closed_machine_source_cannot_be_captured: Conflict code, zero orphan/root/member assignment; fresh eligible companion publishes. |
| PC-S5-3 / complete machine batch | Keep only the opening machine source when the complete shared-attempt composed batch is present. | ChannelOutboundDiscoveryTests.C519_Complete_machine_batch_has_one_owner_for_every_member: three independent members have the same root before preparation and all settle after one complete envelope. |

PC-S5-1's event witness observes the existing service probe's new capture-admission
barrier (before source validation) so the transactional fence cannot mask removal
of the event exclusion. Its valid companion proves that barrier is wired to the
real scoped service. Mutation owns deliberate mutants, red/restore/green and
missing-control discovery. Every pre-existing PC/variant and all three new S5
variants remain pending.

S5 repair group 2: the intermediate ebed8ead6c24e05bdff7a881b575448e573c265d
run passed all sixteen discovery results, then was explicitly stopped and awaited
before changing source. It is superseded, not final qualification; its remaining
rows are not claimed passed. Channel context now ranks its original
LastDeliveryStartedAt before the late-confirm SentAt fallback, matching the
original-attempt contract. C519_Machine_context_must_predate_injection advances
the manual clock and stamps a later SentAt on its prior-context companion: that
companion must still recover, while a genuinely later chat remains refused.
The sixteen-result roster/floor and row/total deadlines remain unchanged. Run
After=S5 once on the final committed tip; this exhausts the two repair-group
budget. No additional source edits or broad repetitions without a new brief.

| PC/variant (pending SourceLanding Mutation) | Deliberate guarded defect | Exact ordinary witness |
|---|---|---|
| PC-S5-4 / original context time | Prefer current SentAt over LastDeliveryStartedAt when checking prior channel context. | ChannelOutboundDiscoveryTests.C519_Machine_context_must_predate_injection: prior original-attempt context survives late confirmation; genuinely later context produces no capture. |

All PC-1..PC-100, PC-1059-1..PC-1059-6, PC-S4-1..PC-S4-4, named S4
variants and PC-S5-1..PC-S5-4 remain pending post-land SourceLanding Mutation.

### S6 trailing-recovery checkpoint amendment (Code task 7d285cb5, 2026-10-05)

The allocation's S6/CP-6 is superseded by the executable manifest, where CP-6
belongs to S8 and CP-9 waits for S9. S6 now selects only After=S6, CP-34..CP-40:
seven serial isolated builds/filters, 121 result floor, 42 estimated minutes,
unchanged 15-minute row / 60-minute total deadlines. No Unit, namespace, assembly
or Linux Herdr run. The pump/service terminal-order predicate now includes
Suppressed; full Delivery, Policy/Contract and Deadline classes qualify the
shared ordering/conversion/binding impact beside the dispatcher/discovery/capture
classes. The eight trailing methods are C519_Pending_intervals_never_overlap,
C519_Restart_recovers_reserved_tail, C519_Next_prompt_keeps_an_already_reserved_tail,
C519_Suppressed_tail_advances_cursor_without_publication,
C519_Concurrent_reservation_has_one_winner,
C519_Fair_root_budget_reaches_tail_behind_idle_roots,
C519_Tail_commit_failure_does_not_advance_root, and
C519_Machine_tail_policy_and_api_withholding_survive_restart.

Catalog-backed enabled main/machine roots and their per-target tails are owned
only by the delivery journal. Event dispatch examines at most 32 roots; historical
discovery has an independent keyset cursor and 32 x 10 root budget. A restart
needs no event or in-memory watermark. Preparation failures retain their reserved
interval while later text starts after that interval. Next UserPrompt or submitted
QueuedUserPrompt caps extraction; a version-fenced closure never removes a child.
Main NO_REPLY captures a Suppressed root and settles members in that same
transaction. Suppressed tails advance the cursor with no preparation/publication
or PublishedAt. Machine NO_REPLY remains unowned/unsettled; existing machine
origin and attachment policy still governs tails. Captured route/profile/root
identity survives rebinding; the existing pump revalidates authorization.
Catalog-less routes and default-off dispatch retain their legacy path. No schema
change, new producer path, activation, TTL/loss repair or S7-S9 work is included.

V-6's ordinary PostgreSQL/reservation/provider-reconstruction matrix is supplied
here; independent fake receiver envelopes plus reloaded journal/member data are
its receipt boundary. Real Kafka/gateway/process-death proof remains V-11..V-13
at S12. V-5 gains its root/tail and 321-idle-root fairness subset; the remaining
S8/S9/terminal-ingestion-completeness parts remain pending. V-2 capture regression,
R-1 dispatcher, R-5 and R-6 run on this tip. R-1 runtime keeps its prior S4 receipt
and is unchanged. V-1/V-3/V-4/V-7..V-13 and R-2..R-4/R-7..R-12 remain deferred
to their owning slices; no manual operational activation is required. The switch
stays false; restart is none, activation owner is the caller.

PC-11/PC-38..PC-41/PC-99 gain their named ordinary trailing witnesses. PC-99's
race holds the first root read under its destination lock and overlaps a second
caller with a different start/end, leaving exact-start uniqueness unable to mask
an overlapping reservation if both lock and version guards are bypassed.
All PC-1..PC-100, PC-1059-1..PC-1059-6, PC-S4-1..PC-S4-4 and all S4 variants,
and PC-S5-1..PC-S5-4 remain pending post-land SourceLanding Mutation.

| PC/variant (pending SourceLanding Mutation) | Deliberate guarded defect | Exact ordinary witness |
|---|---|---|
| PC-S6-1 / silent main root | Settle main NO_REPLY without a captured root. | ChannelOutboundTrailingRecoveryTests.C519_Suppressed_tail_advances_cursor_without_publication: linked Suppressed root, null PublishedAt, and a later legitimate tail after provider reconstruction. |
| PC-S6-2 / root fair budget | Reset the root keyset cursor each tick or remove its page bound. | ChannelOutboundTrailingRecoveryTests.C519_Fair_root_budget_reaches_tail_behind_idle_roots: 320 then 2 examined roots, target reserved on cycle two, one tail receipt. |
| PC-S6-3 / machine trailing policy | Publish System plain trailing text without attachments. | ChannelOutboundTrailingRecoveryTests.C519_Machine_tail_policy_and_api_withholding_survive_restart: silent cursor-owning child, no receiver message; valid attachment companion received. |
| PC-S6-4 / trailing API withholding | Reserve/publish a trailing window containing an API-error stub. | ChannelOutboundTrailingRecoveryTests.C519_Machine_tail_policy_and_api_withholding_survive_restart: cursor unchanged and no additional receipt after the stub beside a valid attachment. |
| PC-S6-5 / terminal ordering | Leave Suppressed in the pump's unresolved-head predicate. | ChannelOutboundTrailingRecoveryTests.C519_Suppressed_tail_advances_cursor_without_publication: legitimate child publishes behind its silent root/child. |

Mutation owns deliberate mutants, red/restore/green, and missing-control discovery;
no PC is discharged by these ordinary results. PreparationDeadlineAt's CreatedAt
basis and CARD-1061's hard-link reader gap remain unchanged.

S6 repair group 1: run 20261005-053259-37bd at
be96960c25689cfe8d4e229aa12f7a8bcea64f0a failed its first isolated build because
ChannelOutboundTrailingRecoveryTests omitted the TranscriptKinds namespace import.
No test executed. The run was stopped and its waiter returned terminal exit 6;
all owned children were gone before source repair. The missing import is added,
and the legacy-only watermark comments now match S6 ownership. Run After=S6 on
the next committed tip; no per-fix rebuild, timeout widening or assertion change.

S6 ordinary qualification: run 20261005-053654-b508 at
635baa68fe6d67f5a0eb0014dc9cc77ccc1999b1 completed exit 0 in 20m11s.
CP-34..CP-40 executed 8/10/16/5/38/31/13 results respectively: 121 passed,
zero failures/skips. Fresh TRX rosters were inspected per intended class/method;
all seven clean-source/build receipts validated for that exact SHA. Each selected
build and row had slot=granted, waited=0s; no automatic repetitions or additional
application builds/tests ran. One repair group was used, solely for the missing
namespace import; no second repair was needed. Full receipts and the cumulative
V/R/PC disposition are in .antiphon/task-7d285cb5.md. This qualification applies
only to the S6 closed scope, not later-slice or full-card qualification.

PC-S6-2 has three independent pending variants, all detected by the same exact
fair-root method: reset the root cursor every tick; remove Take(PageSize) from
the root query; remove the MaximumPages root-loop limit. Mutation must execute
each separately and budget each red/restore/green cycle. All prior pending PC
IDs/variants remain pending; ordinary green does not discharge any control.

### S7 retry checkpoint amendment (Code task 28194b17, 2026-10-05)

The proposed allocation's S7/CP-7 and CP-8 are superseded by the executable
manifest. S7 selects only After=S7, CP-41..CP-49, serial, once at its final
committed tip: 153 result floor, 56 estimated minutes, unchanged 15-minute row /
60-minute total deadlines. No whole Unit, namespace, assembly or Windows parity
run belongs to this slice. CP-10 remains the later S9 complete retry qualification.

The 22 exact B methods remain the original roster (PC-42..PC-58, PC-60..PC-62,
PC-85..PC-86). Their S7 oracles independently reload PostgreSQL and observe complete
fake producer envelopes. They qualify attempt-before-entry, commit refusal,
unexpired single-owner claims, each independently isolated owner/version/expiry/state
entry and outcome fence, awaited completion, cancellation before attempt/after entry/
after acceptance, a cancellation-ignoring producer bounded by the manual-clock
send deadline, uncertainty without replay, duplicate acknowledgement, lifetime
counts and a fresh explicit budget, synchronous/asynchronous/serialization/accepted-
then-fault classification, queue-full persisted due times at minus-one/equality,
three-refusal cap across reconstruction, refusal-save failure, broker-shaped and
local size refusals with a valid companion, all binding holds with explicit repair/
resume, final profile revocation, equal-CreatedAt Id order, resume phase and finite
settings including strict timeout-less-than-lease. These are application receiver
observations, not Kafka/gateway/provider receipts. Real transport and process death
remain S12.

Enabled publication takes one attempt per due tick and applies the configured
send timeout, lease, preparation/publication limits and retry delay. Explicit
uncertain retry alone moves the publication budget base to the lifetime count.
The default-off publication path keeps its existing in-call refusal loop, clocks,
manual retry and binding/conversion behavior. UnifiedRecoveryEnabled remains false
by default. No migration, activation, new outbound path, deadline-basis repair,
loss transaction, metadata separation or retention work is included. ScanIntervalSeconds
and MaximumPages are validated scheduling inputs whose loop adoption remains S9;
S7 uses PageSize only for pump candidates, retaining the existing bounded discovery.

V-7's S7 matrix is ordinary qualification here; its atomic incident/alert and
acceptance/projection failure extensions remain CP-10/S9 after S8 prerequisites.
V-3 retains S3 preparation oracles only, without claiming S8 loss outcomes. V-5
retains the existing S5 subset; V-6 reruns its full S6 reservation matrix. R-1
reruns the dispatcher only; the unchanged runtime result retains its prior S4
qualification. R-5 and R-6 rerun in full. R-8 runs the three named portable manual
recovery methods; its native-route/broker/process proofs remain S12. V-1/V-2/V-4/
V-8..V-13 and R-2..R-4/R-7/R-9..R-12 remain at their owning slices. The task-specific
brief explicitly excludes the generic Final whole-Unit lane. No operational manual
acceptance is required; restart is none and activation remains caller-owned.

All PC-1..PC-100, PC-1059-1..PC-1059-6, PC-S4-1..PC-S4-4 and their named
S4 variants, PC-S5-1..PC-S5-4 and PC-S6-1..PC-S6-5 (including the three independent
PC-S6-2 variants) remain pending post-land SourceLanding Mutation. PC-42..PC-58,
PC-60..PC-62 and PC-85..PC-86 gain their exact ordinary B witnesses. PC-53's
atomic loss and PC-55's incident-save variants remain S8/S9 prerequisites; S7's
refusal-state-save variant is independently exercised. Mutation owns every deliberate
mutant, red/restore/green cycle and missing-control discovery. No PC is discharged.
PreparationDeadlineAt still uses enqueue CreatedAt; CARD-1061 remains unfixed.

S7 repair group 1 strengthens the lease-claim witness after inspection identified
that the candidate query could mask removal of the claim-update expiry predicate.
The per-instance before-claim barrier installs a live foreign lease after candidate
selection without changing Version, so the claim predicate alone must reject it;
the genuine two-pump held-producer race and a resumed valid companion remain.
The due-time witness also requires Version to remain unchanged before the due time,
so an entry guard cannot mask removal of candidate admission's due predicate.
No mutant was executed. Run 20261005-065158-fcfb at
472b67b5278ca63d7fa91fb319a49f78856230af passed CP-41 (22) and CP-42 (3), then was
explicitly stopped and awaited (terminal exit 6, executor/children gone) before
source edits. Its CP-43 build and all later rows are incomplete, and these earlier
receipts are superseded. Run all nine After=S7 rows once on the next committed tip;
no per-fix rebuild, assertion relaxation or timeout change is authorized.
