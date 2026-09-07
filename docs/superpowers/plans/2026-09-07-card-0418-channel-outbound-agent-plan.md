# CARD-0418: source deliverables by default, outbound conversion by channel

Plan, 2026-09-07. Grounded at `a5171a45` and the full live CARD-0418, CARD-0337 and CARD-0410 records. This artifact changes no runtime behavior. Next stage: TestDesign; asynchronous delivery ownership makes this a hard change, even though conversion itself is one agent invocation.

## Outcome

Keep settlement-time source collection and completion-note/implied attachments. Remove PDF generation from settlement entirely. An explicitly selected channel profile may invoke a tool-capable worker in its own project workspace before the server publishes the existing `ChannelReply` to Kafka. Retain the existing renderer as an optional command-line library/tool that the worker can call; the server and gateway do not depend on it.

The original destination was **mav-ref / PredictionMarkets**, gateway **mikeysbot-slack**, Slack conversation **D0B1VUH2EAK**, historical thread **1787842404.752659**. That destination must receive an explicit PDF conversion profile when its server is upgraded. The local catalog does not contain it, so live preservation remains a deployment acceptance gate, not a result demonstrated by this Plan.

## Ground truth

| Card assumption or design risk | Verified behavior and implication |
|---|---|
| PDF is universal at settlement. | `AgentTaskReplyService.cs:668` calls `TryBuildDeliverableBundleAsync` for succeeded tasks. `DeliverableBundleService.TryBuildAsync` gates on a server setting, success and resolvable documents, never a destination/project policy. Both `DeliverablesSettings.Enabled` and `server/appsettings.json:54` default true. |
| Turning Enabled off supplies Markdown instead. | False: the early return skips source copies/zip as well as PDF. Preserve source bundling and remove its rendering dependency; changing the boolean alone fails acceptance. |
| Every successful task gets a bundle. | More precisely: named resolvable `docs/...md` paths, or a Markdown-only worktree diff, supply documents. Plan/Docs are eligible roles, but still need collected files. Mixed code/docs without named docs can produce no bundle. Preserve this eligibility, not a new scan of every project file. |
| Sources already survive worktree removal. | Bundles use `RepoPath` then `WorkingDirectory`, under `.antiphon/deliverables/<taskShort>`, with worktree/disk/branch fallback for reads. Preserve the canonical repository location and completion-note link. |
| Large sets already produce a complete zip. | `WriteSourcesAsync` zips above 5 sources or when any source exceeds 1 MiB. However `BuildCoreAsync` first truncates above `MaxDocuments=40` using `Take`; do not preserve silent omission as the new default. |
| No PDF is an ordinary state. | Currently `FormatNoteBit` returns `N md, pdf failed` whenever a bundle has no PDF. `ListAttachableFiles` scans almost every file and prioritizes historical PDF; a source-only manifest is needed to exclude stale/temporary outputs. |
| Bundle delivered means all files reached the person. | `StampDeliveredBundles` stamps if **any** source path is present in the submitted attachments, after producer completion. This is broker publication evidence, not Slack/Telegram upload or user-open evidence. Do not use it as proof of a converted PDF. |
| GatewayOutboundService publishes outgoing replies. | It **consumes** `ResolveOutboundTopic()`, using `<ConsumerGroup>-outbound`, then invokes `IChannelAdapter.SendAsync`. The producer is `src/Antiphon.Messaging.Client/KafkaAntiphonMessagingProducer.SendAsync`: JSON serialization, key `ConversationId ?? ReplyHandle`, then `ProduceAsync(OutboundTopic, ...)`. |
| There is one server reply caller. | `ChannelReplyDispatcher` has main, trailing-text and machine-note sends. `ChatChannelService` handles proactive/digest/incident sends; `ChannelAlertRouter` also calls `IAntiphonMessagingProducer`. The server-only outbound facade must account for all three services without changing the public messaging producer's meaning. |
| A synchronous producer decorator can wait for an agent safely. | `AgentSessionRuntime.DispatchChannelRepliesAsync` and `FlushQueueOnIdleAsync` await channel routing **before** review/task settlement and queue flushing. Dispatcher correlations are currently stamped before produce and unclaimed on ordinary failure. Adding a minutes-long await inside that region expands the delivery/crash window and delays session work. |
| Existing specialists can do conversion. | `SpecialistSpec`/`StandingSpecialistProvisioner` install a deny-all-tools contract; the Check/Diagnose/Distill seats are not conversion workers. `AgentTaskService.CreateAsync` already supplies normal Worker/Custom tasks, pinned agents, project custody, quota/auth/model gates, dispatch and transcript settlement. Reuse that path. |
| ExecutionDeadlineAt already covers any worker. | It exists, but dispatcher expiry checks currently explicitly select `Role == Distill`. Extend only to the new internal outbound-task purpose; merely setting the field on Custom is insufficient. |
| CARD-0410 added a conversion hook. | No. It fixed `InboundUnconsumedMonitorService` monitoring `antiphon-consumer` while the server used `antiphon-server-bridge`, distinguished unknown evidence from lag, and added startup/readiness/deploy validation. Its false service-down notice was separate from the real answer. Preserve those groups, topics and monitor rules. |
| The historical thread is the adapter's current wire address. | The card records `D0B1VUH2EAK:1787842404.752659`. The current Slack adapter uses `ConversationId=D0B1VUH2EAK`, opaque `ReplyHandle=D0B1VUH2EAK|1787842404.752659`; `ReplyToMessageId` may also carry thread identity. Freeze the actual inbound-derived handle, never parse the card's historical notation into a guessed destination. |
| The original need can be confirmed on this local instance. | A read-only `GET /api/channels` returned 7 rows, with Slack conversations `C0N8YDJH0` and `D0BRT8UJCPQ`; neither is the incident destination. The old CARD-0337 plan also identifies mav-ref as a separate installation. No live messages, bindings or gateway configuration were changed. |
| CARD-0337's recorded follow-up identifier is reliable. | Its closure points remaining S4-S6 to CARD-0367, but the current full CARD-0367 record on this board is an unrelated delegation/card-create investigation. Treat that reference as stale; resolve any outstanding marker work by full card identity/content before coordinating it. |

## Decisions

### D-1. Settlement produces sources, never an automatic PDF

Keep `DeliverableBundleService` as the source collector/packager and keep `Deliverables:Enabled=true` meaning source bundling enabled. This is the card's permitted “otherwise stop” option. Delete its renderer dependency and render invocation: neither legacy `Enabled=true` nor a stale BrowserPath can re-enable PDF. Remove BrowserPath/RenderTimeoutSeconds from server settings and issue one startup deprecation warning if legacy renderer keys are configured. A pre-existing `Enabled=false` remains an explicit source-bundling kill switch; a clean installation gets sources.

For new bundles set `DeliverablePdfPath=null`, `DeliverableRenderError=null`; note text is `N md` or `N md, sources zip`. Retain historical PDF columns for reading existing records, not for routing new output. Introduce a versioned source manifest in the bundle: original relative paths, stored filenames/zip entries, lengths and hashes, omissions/error if any. List only manifest-listed `.md`/source zip files for new bundles. For pre-manifest bundles, discover only `.md` and `*-sources.zip`; existing PDFs remain available through their recorded historical paths, but are no longer implicitly attached. A human/agent's explicit attachment of an already-created PDF still works.

Preserve source contents, including non-ASCII and existing encodings, when copying files; preserve original relative names inside zips and deterministic collision handling for inline names. Keep the 5-file/1-MiB zip thresholds. Remove the rendering-era 40-document truncation: all selected sources go into the large-set zip. Bound collection/zip work using a typed 64-MiB total uncompressed source budget; if exceeded, record which inputs were omitted and surface an incomplete-source warning, never claim the zip is complete. Keep the existing extraction exclusions for generated `docs/cards/`, `.antiphon/` and traversal. Do not expand this card into changing how arbitrary report paths are discovered.

Rejected: Enabled=false alone (loses sources); adding a global PdfEnabled flag (keeps the wrong scope); continuing implicit historical-PDF attachment (turns old bundles into a global opt-in).

### D-2. Explicit channel binding to one project-owned agent profile

Add nullable `ChatChannel.OutboundAgentProfile` and an explicit clear flag to the existing channel PATCH/DTO. Null is passthrough on every existing and newly discovered row. The named profiles live in typed `ChannelOutboundSettings.Profiles`, empty by default; each contains:

- `ProjectId`, dedicated `AgentId`, and a project-local `PromptFile` relative to that agent's working directory;
- `Trigger`: `MarkdownSources` (default) or `EveryAgentReply`;
- `TimeoutSeconds` (default 120, validated 10..300), `MaxPending` (default 8, validated 1..32).

Agent kind/profile/model come from the explicitly selected agent. No automatic provider fallback, always-on specialist creation, PDF format enum, shell command string, or prompt stored per message in channel configuration. The profile is named for its operation/workspace, not a built-in format. Its prompt may call a project script, the optional renderer, or perform another transformation.

Validate opt-in: profile exists; channel is enabled and bound to an agent in the same project; conversion agent belongs to that project, uses a delegatable kind, has a usable dedicated workspace, and is not any channel's inbound agent. Snapshot profile/agent/prompt revision and project when a job is accepted. Rebinding/unbinding the channel clears its profile; reject binding changes that would leave incompatible settings. Revalidate before launching and before publishing. A disabled/rebound channel holds pending work for attention, rather than sending under a new binding.

This first implementation is **per conversation within its project**, which satisfies the requested per-channel option. Existing messaging identities expose no independent gateway-instance id; do not invent a gateway field or interpret a provider name as consent for every Slack/Telegram conversation. Several intended conversations may explicitly bind the same profile. No project-wide or provider-wide inheritance.

The PATCH preview/status shows project, converter identity, prompt revision, trigger, deadline and that enabling it authorizes one metered worker invocation per matching reply. Persist this choice; do not ask again for each automatic invocation. No new scheduled action or spend bypass is introduced.

### D-3. One server-side preparation point, the same Kafka message

Add a concrete `ChannelOutboundService` used by `ChannelReplyDispatcher`, `ChatChannelService` and `ChannelAlertRouter`, immediately above the existing `IAntiphonMessagingProducer`. Do not decorate that public interface to make `SendAsync` secretly mean “queued”. The new server facade returns an explicit `Published`, `Deferred`, or failure result; direct producer completion continues to mean Kafka accepted the publication.

The internal request contains the frozen `ChannelReply`, origin (agent turn versus server control), project/channel ids, stable source identity, matched correlation ids, prompt/text sequence bounds, source task ids and source manifest reference. It is not a new Kafka schema. Agent replies are eligible according to D-2; `MarkdownSources` recognizes source manifests and explicit `.md` attachments, not every arbitrary zip. `EveryAgentReply` also exposes outgoing Markdown text to custom transformations. All main, trailing and machine-turn replies use the same decision. Existing NO_REPLY, API-error withholding and prompt-origin gates run first.

Server-composed alerts, digests, loss notices and proactive text use an explicit control origin and bypass conversion. They cannot recursively start conversion work when a converter fails. Their producer and publication stamps remain unchanged. A future proactive attachment API can opt into the facade's agent-content contract explicitly; it is not needed here.

On the no-profile path publish immediately, preserving existing latency, attachment markers and formatting. On the selected path freeze the input and durably enqueue preparation, returning Deferred promptly. The eventual send is still:

```text
ChannelReplyDispatcher / ChatChannelService / ChannelAlertRouter
  -> ChannelOutboundService
     -> no selected profile/control: existing IAntiphonMessagingProducer.SendAsync
     -> selected agent reply: durable preparation -> one Worker/Custom task
        -> validate resulting files -> existing IAntiphonMessagingProducer.SendAsync
  -> KafkaAntiphonMessagingProducer -> channels.outbound
  -> GatewayOutboundService -> Slack/Telegram IChannelAdapter.SendAsync
```

No conversion topic, gateway callback to the server, agent-owned Kafka credentials, or agent launch in `src/Antiphon.Messaging.Gateway`. That package remains a portable transport/adapter library and keeps CARD-0410 behavior.

### D-4. Durable preparation only for opted-in replies

Add `ChannelOutboundDelivery` plus a hosted preparation/publish pump. This is a local work record for an asynchronous agent step, not a second messaging bus or general workflow engine. Store id, unique source/destination key, immutable input location/hash, policy snapshot, channel/project, state/version, creation/deadline, conversion task id, output manifest, publication attempt/evidence and bounded failure reason. Store file bytes under a server-owned durable `.antiphon/outbound/<id>/` location, outside removable task worktrees; stage atomically before committing the row. File-store I/O uses an Application interface implemented in Infrastructure.

Use a unique key over session, owning prompt sequence, text window, send kind and target for agent sends. Source task id is metadata, not the key: two conversations must not share a converted reply. One transaction inserts/reuses the intent and attaches its id to matched `SessionQueuedMessage` rows using a new nullable FK. A trailing reply stores its own sequence window in the intent; do not rely solely on the current in-memory watermark after restart. Snapshot routing once from the actual reply, not from whatever Slack thread became most recent while conversion was running.

For this branch, do not set `ChannelReplySettledAt` or `DeliverableDeliveredAt` when accepting work. Add `Deferred` to dispatcher outcomes and teach late-confirm warnings that durable preparation is pending. Open-correlation matching/TTL skips a row owned by a nonterminal delivery; the delivery pump owns its deadline and terminal fault. Do not mark an inbound answer lost while its separately observable conversion is within budget. A pending intent survives a later prompt and publishes its frozen reply without reconstructing the latest transcript.

States: `Pending -> Converting -> Ready -> Publishing -> Published`. Conversion failures/deadlines choose `Ready` with the frozen original and fallback annotation. Policy revocation produces `Held`. A publish with uncertain outcome produces `PublishUncertain`; definite exhausted failure produces `Failed`. Never treat any of these as Published.

The pump claims rows with database concurrency control and a recoverable lease; no DB transaction stays open across agent execution, filesystem conversion or Kafka. A periodic tick notices settled tasks and also resumes after restart; no full-turn wait occurs inside `AgentSessionRuntime`. Start at most one conversion per configured agent and two globally. Deadline includes queue time. Queue overflow selects source fallback immediately without dispatching a task. Preserve per-destination ordering for opted-in agent replies, including passthrough agent replies queued behind a conversion; other conversations and control notices remain independent.

After broker acceptance, persist Published and matched correlation/source-delivery stamps together, using the actual published payload. A source bundle is complete only if all required source members (or its complete zip) were included. PDF/transform outcome is recorded separately. Keep `DeliverableDeliveredAt`'s compatibility meaning as publication evidence and label it accordingly; do not claim provider receipt. Handle multi-target outcomes independently instead of retrying a target already published.

Crash recovery before publish reuses the same intent/task/output, never invokes another converter merely because the server restarted. A crash/ambiguous exception after publication began has an unavoidable Kafka/Postgres uncertainty window: record PublishUncertain and surface it for explicit retry, not automatic replay or a false success. Definite pre-acceptance failure can retry the already-validated payload (three attempts with bounded backoff). This card does not claim exactly-once provider delivery or alter gateway auto-commit. Failures appear on the existing attention surface with delivery id, channel and conversion task link, without replaying CARD-0410's service-down text.

### D-5. Invoke an ordinary tool-capable worker through existing delegation

Implement `OutboundConversionTaskRunner` as a small facade over `AgentTaskService`/the normal dispatcher and settlement store. Add an internal creation context, unavailable on public create requests, that links `OutboundDeliveryId` atomically to a Worker/Custom task. Share existing validation rather than copying a new launch implementation. Use the profile's pinned AgentId/working directory, `Workspace=Shared`, no parent/session reply target, no inherited source-task environment, no card inference, no automatic continuation and one attempt. Commissioning project is the profile project, not inferred from the source task's paths.

The purpose link suppresses source bundle creation, output distillation, completion follow-up notes, card binding/transitions and automatic diagnostic/check-driven escalation for these internal jobs. Preserve ordinary usage accounting, transcript completion and process ownership. Enforce expiry for this purpose at queued selection **and immediately before dispatch**, extending the existing Distill-only `ExecutionDeadlineAt` gates without changing Check/Diagnose contracts. Quota/auth/model holds, missing agent, busy seat or blocked/failed task select fallback; never set IgnoreSubscriptionQuota, IgnoreModelDisabled or AllowUnauthenticatedProvider.

Do not reuse `SpecialistTaskRunner` or relax deny-all specialist policy. Do not pin the conversion back onto the source channel agent: that creates a self-wait and mixes channel turns. The worker reads a request file and writes `output/manifest.json`, then ends with its normal report token. Its result is the validated file manifest, not prose parsed for attach markers. It has no channel binding, no outbound credentials and no permission to dispatch child tasks. Treat files and outgoing prose as data, not instructions to change the destination or project.

At deadline cancel a task only if it is still Queued, using a conditional state update. An already running task retains its normal owner and lifecycle; mark its delivery expired, ignore late output and never publish a second reply. Do not kill an active process merely because optional conversion exceeded its publication budget. Block new work on that busy conversion seat and surface its continued activity normally.

### D-6. Generic file contract, original sources retained

Input JSON v1: delivery id, immutable routing metadata for reference, outgoing Markdown text, source attachment descriptors (safe local name, MIME, length/hash), available source manifest and output directory. Materialize from already-authorized attachment bytes/snapshots; do not let the converter fetch arbitrary `Source` URLs or open unrelated source paths. For a generated source zip, safely unpack its manifest-listed Markdown entries into the job input (with containment and expanded-byte checks), avoiding an agent having to guess which files the zip contains or reopening a removed worktree. Stage only bounded inputs.

Output JSON v1: delivery id, disposition `unchanged|converted`, optional replacement text, and **additional** file descriptors relative to `output/`. Do not permit output to set Channel, ConversationId, ReplyHandle, ReplyToMessageId, Kind, RawOverrides, SourceTaskId or policy. Reconstruct those fields from the frozen original. Preserve original source attachments automatically; a transform can add a PDF, image, text or another file, without a PDF-specific server branch.

Validate exact delivery id/version, required fields, counts, path containment, no absolute/traversal/reparse escapes, finite byte limits, file existence and claimed hash/length. Copy accepted files into a sealed server snapshot before publishing; late worker edits cannot change them. Output filenames/MIME do not grant permission to read another file. Preserve the 14-MiB cumulative raw attachment limit and validate the **actual serialized ChannelReply** against configured messaging MaxMessageBytes (20 MiB default), including base64/text/metadata.

Prefer originals in the budget. If all originals plus transformed output do not fit, the converter may compress/produce a smaller additional file within its original deadline; the server does not invoke another agent. Invalid, missing, oversized or timed-out output falls back to original text/sources with a short `Conversion unavailable; source files attached.` annotation only for a requested conversion. This is degraded delivery, never a successful PDF claim. Over-cap sources keep existing visible omission warnings and incomplete-source evidence. A fallback is validated against the bus cap too.

Disable/clear a profile to restore source-only behavior for new messages. Already accepted jobs whose profile was revoked use the frozen original only if the same enabled channel/project binding still exists; otherwise hold for attention. A live task can finish but its obsolete output is never sent.

### D-7. Reuse the renderer as an optional tool, retire its server role

Extract `MarkdownPdfRenderer` and its Markdig dependency to a small optional `tools/Antiphon.MarkdownPdf` executable with a reusable rendering class. Give it its own settings (browser path, bounded render timeout) and a manifest-in/PDF-out command. Keep escaping, multi-document pagination, Unicode handling, browser failure/timeout behavior and process cleanup tests. No server assembly dependency, HTTP render endpoint or new attach-pdf marker.

A configured conversion agent may call this tool with an argument list and explicit output path, or use its workspace's own converter. The sample PDF agent prompt requires one combined PDF plus preserved sources, validates that the PDF opens and contains each source's headings, and returns the generic output manifest. It should not repeat the historical “if you want” ambiguity when the profile was enabled specifically to require PDF.

Remove `MarkdownPdfRenderer` registration and Markdig from the server project, plus browser/render settings from its defaults. Keep `DeliverableBundleService` and source packaging tests. Historical render errors/paths remain readable; do not delete old user's files or database history. Remove universal “always/prefer PDF” instructions in `ChannelPreamble`, `AgentWorkspaceProvisioner` and `server/Bundles/orchestrator.md`; describe source attachments and the selected channel step instead. CARD-0337's deferred S4 attach-pdf proposal is superseded by this generic step; its closure's CARD-0367 reference does not identify that work on the current board, so do not edit the unrelated card.

Rejected: deleting a working renderer and making the incident installation discover replacement tooling; leaving a server-wide render service “available just in case”; hard-coding PDF/Slack in the gateway; building a configurable multi-step agent workflow.

### D-8. Migrate the actual incident destination explicitly

The original CARD-0337 four-source specimen is at commit `7bd8eba0b533e46adb2138720b9d4227eed5c321` in mav-ref:

1. `docs/features/001-kalshi-ref-data-downloader/01-requirements.md`
2. `docs/features/001-kalshi-ref-data-downloader/03-design.md`
3. `docs/features/001-kalshi-ref-data-downloader/04-external-api.md`
4. `docs/features/001c-kalshi-current-first-snapshots/04-external-api.md`

Before switching that installation from its old bundle renderer, stage the conversion worker workspace/tool/profile, resolve its actual project/channel ids, and validate conversion against those source contents (or current agreed successors). Pre-provision its DB profile binding in the coordinated rollout after schema migration and before enabling new outbound processing, so existing work does not pass through during a configuration gap. No automatic mapping from Enabled=true to all channels. The deployment's explicit channel mapping is the opt-in.

Use an appropriate **active mav-ref Slack thread**, discovered from current inbound evidence and the user's intended destination; the old timestamp identifies the incident, not permission to resurrect an old thread. Require one normal docs completion through the bound channel: four source files (or a complete zip), one readable combined PDF, unchanged source hashes, the intended Slack thread, and no unsolicited duplicate/root-channel message. Record delivery/task ids, source/output hashes, running server/tool revisions, gateway upload result/native message evidence, and visual/open validation of the received PDF. A fake-gateway pass is necessary but does not substitute for this acceptance.

Also exercise an unconfigured conversation/project and a non-channel task: source files/zip only, zero converter launches and no browser spawn at settlement. Exercise converter failure: originals arrive with the honest fallback and recorded degraded status.

This Plan performed only local read-only inspection. The remote installation endpoint, active thread, authorized conversion agent/profile and current deployed PDF behavior remain to be resolved at rollout. They do not block implementation/test design. The caller must retain the live acceptance item before closing CARD-0418; do not report it already working. If real-chat authorization is not present in the execution session, finish the concrete isolated test/profile preparation first, then request only the named live send/deploy authorization. Do not send a trial message to an unrelated local Slack or Telegram row.

## Implementation slices and test ownership

All proposed filenames below are new unless described as existing. Add EF migrations through the CLI only. Application exposes I/O seams; Infrastructure owns filesystem/process work; pure Domain entities carry no infrastructure dependencies.

| Slice | Files and behavior | Tests to implement or update |
|---|---|---|
| S1: source-only settlement | Existing `DeliverableBundleService.cs`, `DeliverablesSettings.cs`, `AgentTaskReplyService.cs`, `server/appsettings.json`, Program registration; new source manifest/file-store helper. Preserve note and implied attachments, fix large-set truncation and stale file enumeration. | Existing `DeliverableBundleServiceTests`, `AgentTaskReplyIntegrationTests`, `ChannelFollowUpAttachmentTests`, `ChannelMachineTurnTextTests`; new `SourceBundleManifestTests` for byte identity, >40 sources, collisions, stale PDF exclusion and budget omissions. |
| S2: optional renderer | Move existing renderer into `tools/Antiphon.MarkdownPdf/`; separate tool options/manifest command, update solution/project dependency graph. Remove server Markdig/browser dependency; preserve historical fields. | Move/adapt `MarkdownPdfRendererTests` into dedicated tool tests; new `MarkdownPdfCommandTests` covers malformed manifest, real nonempty multi-section PDF, missing browser and timeout cleanup. Fake process seams for ordinary tests; headed/real browser test isolated. |
| S3: policy and durable preparation | `ChatChannel.cs`, `ChatChannelDtos.cs`, `ChatChannelService.cs`, `AppDbContext.cs`, CLI migration; new `ChannelOutboundSettings.cs`, `ChannelOutboundDelivery.cs`, `ChannelOutboundService.cs`, `ChannelOutboundFileStore` I/O seam/implementation. `client/src/api/channels.ts` and `client/src/features/channels/ChannelsPage.tsx` get nullable profile selector, explicit clear and policy preview. | New `ChannelOutboundPolicyTests`, `ChannelOutboundStorageTests`, `ChannelOutboundEndpointTests`; existing `ChatChannelServiceTests`, `ChannelsPage.test.tsx`. Cross-project binding, new-channel default, rebind/revoke, unique intent and crash-before-commit file staging. |
| S4: one worker and result contract | New `OutboundConversionTaskRunner.cs`, result validator, `ChannelOutboundHostedService.cs`; internal context in `AgentTaskService`, purpose link in `AgentTask`, narrow expiry/purpose gates in `AgentTaskDispatcher`/`AgentTaskReplyService`/distillation admission. Register in Program with default no profiles. | New `OutboundConversionTaskTests`, `OutboundConversionManifestTests`, `ChannelOutboundDeadlineTests`; existing deadline/role/dispatch tests as touched. Holds/auth/quota refusal, no card/note recursion, no tools-denied specialist, one task across restart, expiration before launch and late result ignored. |
| S5: route and publish | Route all existing callers through server facade; integrate deferred correlation claim/outcome in `ChannelReplyDispatcher`, `SessionQueuedMessage`, `AgentSessionRuntime`; worker owns final publication/stamps/ordering. `AttentionService` and its DTO/enum gain the bounded delivery-failure/hold projection. Leave public producer and gateway transport unchanged. | New `ChannelOutboundDeliveryTests`, `ChannelOutboundRecoveryTests`; existing `ChannelReplyDurabilityTests`, `ChannelFollowUpAttachmentTests`, `ChannelMachineTurnMatchTests`, `ChannelMachineTurnTextTests`, `ChannelBridgeTests`, `ChatChannelServiceTests`, `AttentionServiceTests`. Real dispatcher -> in-memory messaging/fake gateway -> Slack adapter assertions. Preserve main/late/machine gates and freeze Slack handle across another inbound thread. |
| S6: documentation and migration acceptance | `docs/telegram.md`, `docs/messaging-standalone.md`, `docs/antiphon-api.md`, agent/session owners as needed; the three instruction sources named in D-7; sample project conversion prompt and profile with placeholders, incident-installation rollout record. | Isolated end-to-end specimen and unconfigured-project control; `GatewayTests`, `SlackChannelAdapterTests`, `TelegramChannelAdapterTests`, relevant CARD-0410 monitor/identity/deploy regression checks. Authorized mav-ref Slack acceptance in D-8 remains separately recorded. |

S1 and S2 may be developed independently, then S3 -> S4 -> S5 -> S6. Ship the complete channel opt-in path and incident migration capability before upgrading the incident installation; landing a plan or passing local stack health is not that migration. Do not restart the shared stack from this worktree. Landing/deployment are subsequent orchestrator operations.

## Required TestDesign handoff

Write the separate `## Verification design` with executable V-n/R-n cases and meaningful red-then-green positive controls. Cover these release invariants, not just constructor/options tests:

- A default Plan/Docs/named-doc task in each of two projects creates exact sources, never PDF/browser/agent work; no-channel and disabled-source cases remain distinct.
- Original four-source completion automatically supplies sources without copied markers; only the explicitly opted-in conversation adds a valid PDF through a real worker contract. Large source sets include every eligible entry or visible omissions.
- All three dispatcher send shapes obey one policy; NO_REPLY, unmatched human, API-error stub and control notices never gain accidental sends/conversion. Same source sent to two destinations has isolated artifacts/policy.
- Opt-in work is durable before releasing runtime queue processing; enqueue is not Published. Another prompt/thread/restart cannot change its destination or lose the frozen body. Race two triggers and two pump instances; at most one converter task/intent wins.
- Force death/restart at input staging, intent commit, task creation, worker completion, Ready, Publishing and broker-ack-before-DB-stamp. Verify replay rules, uncertain evidence and no false settled/delivered stamps. Preserve the Kafka/Postgres limitation explicitly.
- Missing browser, invalid outputs, source/output over budget, absent/held/busy/blocked converter, deadline/late-result, channel rebind/disable/revoke and broker failure have the stated fallbacks or holds. Every path retains a normal process owner and no test launches against production 17204.
- The gateway receives the same schema, byte attachments and exact Slack routing metadata; converted PDF and sources open correctly. CARD-0410's expected/watched consumer groups and unknown-evidence suppression stay intact.

Use `dotnet run --project tests/<Project> --property:OutputPath=bin-c0418/ -- --treenode-filter "/*/*/<NamedClass>/*"`, per `docs/testing-and-build.md`; verify nonzero execution counts/TRX and select only touched classes. Use the client test wrapper for changed channel UI tests. Process-spawning tests need their assembly-local limiter; run Antiphon.Tests and Pty tests sequentially. Any broker needed for adapter/transport evidence is disposable and isolated, never the live broker. A real CLI probe uses an isolated runner and prepared files. Tool PDF tests must inspect the rendered artifact, not merely its extension.

## Plan completion and remaining evidence

This plan resolves the defaults, invocation location, asynchronous ownership, renderer fate and original-incident migration path. No product decision is needed before TestDesign. No source code, live configuration or card state was changed, and no tests/builds/live PDF sends were run in Plan. The live mav-ref acceptance is deliberately not claimed; it is required for final card acceptance after implementation.

## Verification design

TestDesign appended 2026-09-07 against landed plan commit `e5fa7009`. D-1 through
D-8 remain the implementation design. This section is executable work for Code:
the new tests, mutations and live acceptance below have **not** run in TestDesign.
The next stage is **Code**; the earlier Plan-stage handoff is historical.

There are two distinct acceptance gates: deterministic/isolated verification of
S1-S6, and the authorized D-8 migration and delivery on the actual mav-ref
installation. Completing the former permits review/landing; it does not close
CARD-0418 or establish preservation of the original incident destination.

### Fixtures and execution boundaries

**F-1: real source files, independent byte oracle.** Extend
`DeliverableBundleServiceTests` and add `SourceBundleManifestTests` under
`tests/Antiphon.Tests/Application/`. Use disposable Git repositories, linked
worktrees and a persistent fixture-owned primary repository under the checkout's
ignored `.antiphon/test-output/card-0418/<run-id>/`. All deletion targets must
resolve under this root, excluding the checkout being built. Keep the primary
fixture repository and outbound store while removing only its delegate worktree.
Exercise worktree reads, primary-repository reads, and branch-only `git show`
reads independently. Compute expected lengths/SHA-256 from the initial **bytes**,
not from the bundle manifest or decoded/re-encoded strings. Disable Git newline
conversion for the byte fixtures and compare against the committed blob on the
branch-only case. Include UTF-8 with/without BOM, UTF-16LE with BOM, CRLF/LF,
trailing spaces/newlines, Polish text and an emoji.

Create a synthetic four-source specimen at the four D-8 relative paths. Its
headings are `Requirements sentinel`, `Design sentinel`, `External API sentinel`
and `Current snapshots sentinel`; each body includes a unique middle and final
sentinel, a table, fenced code and Unicode text. The two `04-external-api.md`
basenames exercise collisions. Call this **synthetic**, never the historic
mav-ref content. Code commits these fixtures and their independent expected
hashes. For a four-source zip variant, pad one file above 1 MiB with a harmless
Markdown/HTML comment; keep the four visible sections and recompute byte hashes.
The historic commit is not available as local fixture evidence; obtain
its actual bytes/current agreed successors only for the separately scoped live
gate. Add 5/6-file sets, 40/41/64-file sets, sizes 1 MiB and 1 MiB + 1, and a
64 MiB exact/64 MiB + 1 uncompressed set. Use streaming generation and hashing;
these are packaging tests, not giant model prompts or messages.

**F-2: real settlement, routing, database and worker contract.** Extend
`BridgeQueueHarness` via its `HarnessOptions.ConfigureServices` and
`ConfigureDbContext`, using `DelegationTestServices` for the delegation graph.
Reuse the settlement setup in `AgentTaskReplyIntegrationTests` /
`AgentTaskSettlementRaceTests`. Add two fixture projects P and Q; P has inbound
agent A, dedicated converter C, opted-in conversation X and unconfigured
conversation Y; Q has agent B and unconfigured conversation Z. Converter C is
not inbound-bound anywhere. Use the real `AgentTaskReplyService`,
`ChannelReplyDispatcher`, outbound facade, PostgreSQL intent/claim/lease logic,
Infrastructure store, task creation/dispatcher and output validator.

Complete source tasks through transcript settlement with
`DelegationReportFormatter.ReportToken(id, "done")`, then consume their actual
completion notes without copying attach markers into the answering prose.
Do not preseed a successful bundle or a Published delivery for the vertical
cases. The only scripted boundary is model/runner behavior: observe the real
Worker/Custom task, read its actual request file, write its output manifest/files,
and settle it through the normal transcript path. Record dispatch attempts,
submitted input, pinned agent/model/project/workspace, and task/intent ids.
A fake `OutboundConversionTaskRunner` returning success cannot prove V-10/V-24.
Seed prompt submission evidence from the **actual submitted** body through the
existing fake adapter callback, not an expected string.

The producer spy deep-copies the actual reply at invocation and stores its
`MessagingJson.Options` UTF-8 serialization, routing and attachment hashes. It
has independently controlled modes: blocked before invocation; blocked inside
send before acceptance; definite nonacceptance twice then success; definite
nonacceptance always; and accepted-but-throws/never-returns. Keep distinct
counters for method entry, accepted records and publication stamps. A spy entry
is not broker acceptance. Each failure fixture first proves its fault/barrier
was reached, and each negative fixture has a matching valid input that sends.

Use real PostgreSQL, not EF InMemory, for uniqueness, FK, concurrency and atomic
stamp assertions. Scope every query to fixture ids. A test that drives global
pumps/sweeps takes global `[NotInParallel]`, not a named group. Classes starting
Git, browsers or any child take their assembly-local
`[ParallelLimiter<ProcessSpawnLimit>]`. New Antiphon.Tests classes are classified
Unit or Integration. Register an offset-over-real clock for the runtime/queue;
use explicit controlled time for a small isolated deadline component only.
Use barriers with asynchronous continuations, release in `finally`, and bounded
wall-clock watchdogs; no frozen whole-server clock or minute-long sleeps.

**F-3: restart and process-death harness.** Add
`ChannelOutboundRecoveryTests` and a test-only executable
`tests/Antiphon.ChannelOutbound.Probe/`. The probe constructs the F-2 service
graph with production outbound components, a scripted protocol adapter and a
refusing external-message adapter. It does not load production configuration.
The parent owns a disposable Postgres database, durable fixture directory and
producer evidence sink, retaining all three between probe launches. Pass an
explicit generated configuration file; redact it from logs. Introduce narrow
per-instance internal barriers at the boundaries in V-15, null in production.
Each barrier emits its name and fixture nonce only after preceding writes are
committed/flushed. The parent kills only its recorded child PID/process tree
after checking that identity, then starts a fresh probe against the same data.
Do not replace these cases with throwing an exception that runs normal cleanup,
reconstructing a dispatcher alone, or reseeding the expected recovery state.
Parent cleanup always owns leftover fixture children and stays inside its root.
A refusing runner must make every accidental external launch a loud failure.

**F-4: optional renderer.** Move/adapt the existing renderer tests into
`tests/Antiphon.MarkdownPdf.Tests/`, with its own assembly-local process limiter.
Separate `MarkdownPdfRendererTests` (HTML, argument and failure contracts),
`MarkdownPdfCommandTests` (CLI and process cleanup) and
`MarkdownPdfRealBrowserTests` (actual artifact). Add a fake browser executable
that records its argument vector, can write no output/exit nonzero, and can
spawn a known child then wait indefinitely. The timeout test asserts both known
PIDs exited; the old `TestHang` seam alone proves neither spawn nor cleanup.

Expose the optional command as `--manifest <path> --output <path>` with optional
`--browser-path <path> --timeout-seconds <n>`; if Code chooses equivalent names,
update the commands in this section in the same commit. Tool manifest v1 names
the cover/title and ordered document path descriptors, using staged local files.
Run the CLI from an unrelated working directory with space/non-ASCII paths.
Publish the tool alone into a fixture folder and run it there with no server
DLL, host, database, Kafka configuration or agent session. Keep required tool
runtime dependencies with that publication. It may neither read server settings
nor use the server assembly. Use independent PDF parsing/text extraction (for
example a test-only PDF reader) plus page rendering/visual inspection for the
real specimen; `%PDF`, nonzero length, and `.pdf` are insufficient oracles.

**F-5: isolated transport and normal worker launch.** Add
`tests/Antiphon.E2E/ChannelOutboundIsolatedTests.cs`. Use an explicitly configured
`AntiphonAppFixture` with its private database and `IsolatedSessionRunner` on a
random port, no production 17204, and a deterministic fixture CLI registered as
the converter through a supported delegatable kind and the established fake-CLI
protocol, not a Raw-kind or public-validation bypass. That CLI reads the actual
request, invokes the real optional tool
using an argument list, writes the generic output manifest and emits the normal
report token. This proves process/tool capability and orchestration, not a real
provider's ability to obey the prompt. Real model behavior is reserved for V-25.
Use the existing `FakeSlackServer` via a shared/linked test source (currently
`tests/Antiphon.Messaging.Tests/FakeSlack/FakeSlackServer.cs`), not a reference to
the Messaging.Tests executable. Add the needed Gateway/Slack/Redpanda test
references to E2E. Use the disposable Redpanda fixture pattern from
`KafkaConsumerGroupObservationTests`, generated topics/groups, explicit gateway
settings and fake Slack endpoints/tokens. Assert every resolved address belongs
to this fixture before startup. Real `KafkaAntiphonMessagingProducer` -> broker
-> `GatewayOutboundService` -> `SlackChannelAdapter` must carry the produced
payload; manually calling the adapter with a separately constructed expected
reply does not cover this case.

F-5 requires `ANTIPHON_HEADED_TESTS=1`, `ANTIPHON_BROKER_TESTS=1` and the new
`ANTIPHON_CHANNEL_OUTBOUND_ISOLATED=1`. It is globally nonparallel, in the
`Headed` exclusion lane, with the E2E assembly's process limiter. Rebuild
`client/dist` before booting an E2E host that serves it. Fixture-owned services,
processes, browser profiles and containers are the only teardown targets.
No real external chat/model, shared stack restart or live broker is involved.

### Proves it works now

The names below are required new tests unless called out as existing. A table
row with a parameter list means execute every listed case and retain each result.
`Published` below means producer/broker acceptance only. Assertions on source
completeness, conversion outcome and provider upload are always separate.

| ID | Behavior and layer | Test or command | Required oracle |
|---|---|---|---|
| V-1 | Universal sources; integration, S1 | `DeliverableBundleServiceTests.Default_settlement_preserves_sources_without_conversion` and `AgentTaskReplyIntegrationTests.Default_docs_completion_keeps_sources_without_channel` | Cross P/Q with Plan, Docs and Custom explicitly naming docs; include a Markdown-only Code worktree. For each eligible case source bytes/hash/count and actual completion-note path are correct, PDF path/render error null, note `N md` or `N md, sources zip`, no render artifacts or converter task. Repeat with legacy Enabled=true, stale browser/timeout config, and a valid fake browser executable whose invocation log stays empty. No-channel completion still collects sources. Enabled=false produces no new bundle and zero launches; mixed code/docs with no named doc and failed/canceled tasks retain their existing no-bundle eligibility. |
| V-2 | Complete byte-preserving packaging; integration, S1 | `SourceBundleManifestTests.Bytes_names_thresholds_and_budget_are_truthful` | F-1 read sources preserve exact bytes in each read path, inline copy and extracted zip. Five files <=1 MiB each stay inline; six files or one >1 MiB zip; exactly 1 MiB alone stays inline. All 41 and 64 selected paths survive without Take(40). Duplicate basenames have deterministic distinct inline names; zip paths retain original relative paths. At 64 MiB all inputs retained; +1 records exactly which inputs/bytes are omitted and an incomplete warning. Every selected input is either hash-verified present or explicitly omitted; never silently missing or falsely complete. |
| V-3 | Manifest custody and history; integration, S1 | `SourceBundleManifestTests.Only_authorized_source_members_are_implicit` | Plant stale PDF, HTML, render.log, temp file, unlisted MD and unrelated zip beside a new bundle; only manifest-listed MD/source zip is returned. Legacy pre-manifest bundles return MD and `*-sources.zip` only. Corrupt/unknown manifest cannot fall back to unrestricted directory enumeration. Recorded historical PDF/error fields and old file bytes survive; an explicit attach marker still sends that PDF exactly once. Excluded generated docs/cards, .antiphon and traversal inputs are never collected. |
| V-4 | Original incident shape without copied markers; integration, S1/S5 | `ChannelFollowUpAttachmentTests.Four_source_completion_uses_implied_sources_and_conversation_policy` | Real F-2 settlement of synthetic four-source specimen produces an actual task-done note. Reply prose has no attach markers. Y/Z receive four exact sources, zero PDF/converter; X remains Deferred until its real conversion task settles, then receives those same four plus one output PDF. Duplicate basename sources both survive. Before publication all delivery stamps remain null. Repeat X as generated complete-source-zip input and prove the worker request has its manifest-listed Markdown contents. |
| V-5 | Explicit policy, endpoints and client; unit/integration/client, S3 | `ChannelOutboundPolicyTests.Profile_binding_defaults_and_validation`, `ChannelOutboundEndpointTests.Profile_patch_clear_and_rebind`, `ChannelsPage.test.tsx` | Migrated/newly discovered channels default null; settings profiles empty. Unrelated PATCH preserves binding; clear removes it; rebind/unbind clears it; invalid changes are atomic failures. Validate unknown profile, disabled channel, missing inbound binding, project mismatch, missing/nondelegatable converter, unusable/escaping prompt/workspace, converter inbound-bound to any channel. Test timeout 9/10/120/300/301 and MaxPending 0/1/8/32/33. Valid X PATCH round-trips, Y/Z unchanged; status/UI shows project, pinned converter, prompt revision, trigger/deadline and one metered invocation per match. Client tests select/save/clear/refetch and show API validation errors without optimistic false success. |
| V-6 | Trigger decision and all send shapes; integration, S3/S5 | `ChannelOutboundDeliveryTests.Main_trailing_and_machine_use_the_same_policy` | Parameterize main, trailing after interim send, machine task-done. MarkdownSources triggers for source manifest, generated source zip with that manifest, and explicit MD attachment; not arbitrary zip or text alone. EveryAgentReply triggers on Markdown body without attachments. Each eligible shape yields one intent and one task through the same facade. Each no-profile/nonmatching path preserves text, markers, Kind and attachments with zero conversion. Source task id alone must not qualify an arbitrary zip. |
| V-7 | Existing silence/error gates and control bypass; integration, S5 | `ChannelOutboundDeliveryTests.Gates_precede_admission_and_controls_bypass` | NO_REPLY without explicit attachments, unmatched operator turn, API-error stub in the window and disallowed plain-text machine origin produce neither new outbound intent nor converter/send; compare existing gate tests for explicit-marker exceptions and provider-capacity/transport notices. Parameterize real ChatChannelService proactive/digest/incident and ChannelAlertRouter notices as control: exactly their existing send/stamps, zero worker, even with Markdown attachments and X's EveryAgentReply policy. Converter-failure notice never recursively converts. Healthy matched reply beside each withheld sample proves the route is active. |
| V-8 | Durable admission without blocking runtime; integration, S3/S5 | `ChannelOutboundDeliveryTests.Deferred_is_durable_and_releases_runtime` | Pause DB admission immediately before commit: runtime cannot report Deferred before commit. After commit hold the converter indefinitely at the fixture barrier: runtime finishes source settlement/queue flushing and delivers the next queued prompt to its fake transcript within a 5-second watchdog without releasing conversion. From a fresh DbContext read intent, input hash and all correlation FKs; producer entries=0 and ChannelReplySettledAt/DeliverableDeliveredAt/LastReplyAt unchanged. No transaction remains open across model/filesystem/publish waits (second connection can update unrelated rows and acquire released claim row lock). |
| V-9 | Intent identity and race; integration, S3/S5 | `ChannelOutboundStorageTests.Concurrent_triggers_claim_one_intent_and_one_task` | Two dispatcher triggers and two independent pumps synchronize before insert/claim against PostgreSQL: one source/destination intent, one linked task, one accepted reply. Same source to X and Y creates independent outcomes/artifacts; changing prompt seq, text window, send kind or target creates a distinct valid identity. SourceTaskId alone never deduplicates destinations/windows. A second pump cannot reclaim an unexpired lease; after expired lease it reuses the task/output, not a second task. The former lease owner resuming after takeover cannot launch/publish/stamp using its stale version. |
| V-10 | Correct ordinary worker purpose; integration, S4 | `OutboundConversionTaskTests.Real_creation_and_settlement_use_internal_purpose` | Observe normal task Create/dispatch with atomic OutboundDeliveryId, Worker/Custom, Shared, pinned C and profile project/cwd/model. Request contains frozen data, no inherited source env/card/parent reply or continuation. Public create cannot set internal purpose. Ordinary usage/transcript completion remains; internal task creates no source bundle, distillation, completion follow-up, card binding/move, check/diagnose escalation or recursive outbound. Run companion normal Custom and Distill/Check/Diagnose tasks to prove existing behavior remains outside this purpose. Converter is tool-capable, no specialist runner/deny-all bundle is reused; attempt count one. Its composed instructions forbid child delegation, it has no channel binding, and its supplied environment/capabilities do not contain source-task or outbound credentials or a child-dispatch grant; inspect using fixture sentinels, never real secrets. |
| V-11 | Refusals, capacity and deadlines; integration, S4 | `ChannelOutboundDeadlineTests.Refusals_and_expiry_preserve_originals_and_owners` | Missing/busy/unavailable/held converter, quota/auth/model refusal, task blocked/failed, output missing and missing browser each result in original text/sources with honest degraded annotation and reason, no provider reroute or bypass flags. With maxpending=1, overflow falls back without task; default/upper bounds tested in V-5. Barrier-test one active task per converter and two globally across three profiles; queued work consumes the original deadline. At deadline equality, queued task cancels conditionally; expiry while waiting for selection and immediately before actual dispatch prevents launch. Already-running task is not killed/released without owner; late success is ignored, source reply sends only once and busy seat takes no new launch. CAS race Queued -> Working before cancellation must preserve the running task. Text-only EveryAgentReply failure retains text without claiming nonexistent source attachments. |
| V-12 | Generic output and sealed bytes; integration, S4 | `OutboundConversionManifestTests.Valid_results_add_files_and_seal_bytes` | `unchanged` preserves original reply; converted output may add PDF, PNG and TXT with optional replacement text, always retaining originals. Output manifest is used, never report prose/attach markers. Hash-verify accepted files into sealed store, then edit/delete worker output and original task worktree before pump send: broker bytes equal sealed snapshot. Two deliveries cannot reuse each other's output. |
| V-13 | Untrusted data and finite budgets; unit/integration, S4 | `OutboundConversionManifestTests.Invalid_outputs_fallback_without_path_or_route_escape` and `ChannelOutboundStorageTests.Input_staging_is_bounded_and_authorized` | Execute the invalid-input/output matrix below. Each attempted conversion either yields a validated original fallback or a visible terminal storage failure if originals cannot be recovered; zero malicious file/route reaches producer. Valid adjacent input converts. Budget equality passes where the serialized payload fits; +1 fails the relevant limit. No arbitrary Source URL fetch/unrelated path read. |
| V-14 | Frozen destination and ordering; integration, S5 | `ChannelOutboundDeliveryTests.Later_prompt_thread_and_control_cannot_retarget_pending_reply` | Normalize fake inbound Slack thread T1, accept A, then receive T2/change channel's latest reply handle, enqueue B and restart. A's body, exact Channel/ConversationId/ReplyHandle/ReplyToMessageId/Kind/RawOverrides and source metadata remain frozen to T1; B keeps T2. B waits behind A even if B does not trigger conversion. Other conversation and control notices send while A waits. Held/uncertain head does not silently let later agent replies overtake it; resolving that head resumes the order. Do not parse the historical colon notation. |
| V-15 | Hard crash recovery; integration, S3-S5 | `ChannelOutboundRecoveryTests.Process_death_preserves_ownership_at_each_boundary` | Execute each C-1..C-8 below by killing/restarting F-3. Assert task/intent counts, input/output hashes, task process ownership, correlation FKs/stamps, producer acceptance count, state, bounded failure evidence and attention projection from fresh connections. |
| V-16 | Publication outcomes and stamps; integration, S5 | `ChannelOutboundDeliveryTests.Only_acceptance_stamps_complete_actual_payload` | Block producer completion: no Published/settled/delivered stamp. Two definite failures then success use the same sealed payload for three bounded attempts, no new converter. Three definite failures yield Failed/attention, no stamps; ambiguous exception yields PublishUncertain and no automatic replay. On success intent/correlation/source publication stamps commit atomically. Sending only 1 of 4 MD members or an incomplete zip never claims complete source delivery; complete zip/all four do. PDF conversion success is distinct from source completeness and broker success. Multi-target A accepted/B failed never retries A. Existing LastInboundAt/LastSpeaker*/LastMessageId remain unchanged. |
| V-17 | TTL, attention and recovery ownership; integration, S5 | `ChannelOutboundDeliveryTests.Pending_conversion_is_not_an_inbound_lost_reply` and `AttentionServiceTests.Outbound_delivery_states_have_truthful_attention` | Seed an old correlation now owned by within-budget conversion, run matching/TTL/late-confirm: no duplicate intent, stale-loss notice or premature settled stamp. Advance beyond delivery budget: exactly one fallback or terminal delivery fault by the pump, no contradictory old TTL notice. Held/Failed/PublishUncertain expose delivery/channel/task references and bounded reason; none appears Published or is automatically replayed by attention reads. |
| V-18 | Policy changes in flight; integration, S3-S5 | `ChannelOutboundPolicyTests.Revocation_and_rebinding_revalidate_before_launch_and_publish` | Clear/remove profile before dispatch and after worker success with same enabled channel/project binding: original-only payload, obsolete output ignored, no new task. Disable/unbind/rebind/cross-project replacement before each boundary: Held, no send under new binding and no success stamps. Accepted profile/prompt revision stays immutable through a same-name settings/file edit; no newly edited prompt is substituted into an existing request. New messages use the new valid policy. Concurrent binding update at the final validation/claim is ordered consistently with the publish decision; evidence records which version won. |
| V-19 | Standalone renderer function/cleanup; unit/integration, S2 | `MarkdownPdfRendererTests.Html_and_arguments_preserve_document_contract`, `MarkdownPdfCommandTests.Standalone_manifest_and_failures_are_bounded` | Preserve escaped cover/path, Unicode, GFM table/fence/task list and page-break contract. Argument-list probe receives exact space/non-ASCII paths without shell evaluation. Malformed/unknown/missing manifest, missing source, explicitly missing browser, browser nonzero, exit-zero-with-no/empty PDF, timeout and caller cancellation never claim success; explicit missing browser does not auto-detect another. Known spawned process tree exits after timeout/cancel; temp HTML/profile cleaned. A preexisting output PDF must not turn failed/no-output rendering into success. Standalone publish runs without server dependencies. |
| V-20 | Actual readable PDF; isolated real browser, S2 | `MarkdownPdfRealBrowserTests.Four_documents_produce_readable_combined_pdf` | F-4 tool returns zero, PDF independently opens and contains all four ordered headings plus body/end sentinels; each document starts on a new page. Render every page and inspect Unicode, wrapping, table/code and no missing/blank/clipped section; retain PDF, extracted text and page images. Source hashes unchanged. Run outside agent/server; absence of browser/parser is pending evidence, not a pass. |
| V-21 | Schema migration and removal of universal renderer; integration/structural, S1-S3/S6 | `ChannelOutboundMigrationTests.Upgrade_preserves_history_and_defaults` and `ChannelOutboundContractTests.Server_and_gateway_keep_source_and_transport_boundaries` | In private DB migrate from preceding schema with real legacy tasks/channels; old values/files preserved, new nullable profile/FK null, unique intent and task-purpose relationships enforced. Fresh DI with no tool installed settles sources; startup legacy-key warning once per boot, no rendering registration/defaults/Markdig reference in server graph or render endpoint/attach-pdf marker. Optional tool dependency graph has no server. ChannelPreamble, workspace provisioning and orchestrator bundle no longer prescribe universal PDF, while explicit attachment syntax still works. New enum values preserve historical numeric values. |
| V-22 | Existing transport/monitor behavior; integration/unit, S5/S6 | Existing `GatewayTests`, `SlackChannelAdapterTests`, `TelegramChannelAdapterTests`, `ChannelConsumerIdentityEndpointTests`, `GatewayMonitorValidationTests`, `ConsumerLagAssessmentTests`, `InboundUnconsumedMonitorTests`, `GatewayMonitorStatusTests`, `LibrarySufficiencyTests` | Consumer/producer contract and same ChannelReply JSON stay compatible; inline bytes, thread uploads and Telegram formatting/fallback remain. Expected/watched inbound group is `antiphon-server-bridge`, independent outbound group/topic settings unchanged. Unknown observations (absent group/query failed/no partition/no commit) never become lag notice/watermark; committed old lag positive case still notifies. Run existing monitor configuration/deploy-contract assertions; no fake traffic to production. |
| V-23 | Serialized bus boundary; integration/broker, S5 | `ChannelOutboundDeliveryTests.Serialized_payload_budget_includes_all_fields` and `ChannelOutboundIsolatedTests.Kafka_accepts_the_validated_original_or_converted_payload` | Compute UTF-8 bytes with actual MessagingJson.Options, including base64, escaped Unicode, long metadata/text. Cases at 14 MiB raw and +1, and configured MaxMessageBytes exact/+1: validate actual payload, not raw size estimate. Set a smaller configured cap to cheaply hit exact wire boundaries, plus one near-default 20 MiB case. Originals take precedence over additions; oversized output falls back without another invocation. Validate fallback including annotation/omission warnings; if it too cannot fit, retain Failed/attention without a false stamp or an over-cap send. Real broker accepts under-cap payload with matching topic/key; existing ConversationId then ReplyHandle then empty key semantics retained. |
| V-24 | Full isolated four-source delivery and controls; E2E, S1-S6 | `ChannelOutboundIsolatedTests.Four_sources_convert_only_for_the_selected_conversation` | Drive normal source completion -> actual normal worker CLI/tool -> validated frozen reply -> real producer/broker/gateway -> fake Slack upload. Before conversion output there is no outbound broker record; after acceptance X receives four exact MD (or complete zip) plus the independently readable V-20 combined PDF in inbound-derived T1, even after T2. Fake Slack records exact upload bytes/channel/thread; no duplicate/root post. Y and Z and a no-channel task have source-only defaults and no worker/browser settlement spawn. Failing tool sends originals with honest degraded outcome. Restart owned host after durable admission and remove source worktree before send. This proves integration, not real model behavior or actual Slack acceptance. |
| V-25 | Actual incident migration and receipt; authorized live probe, S6 | D-8 procedure expanded below | Pending until the actual mav-ref project, approved converter/profile, deployment and active mikeysbot-slack thread are available. Real normal docs completion delivers actual four-source/current agreed specimen plus a readable combined PDF, preserving hashes and thread. Collect native upload/message/open evidence; broker stamps alone fail this gate. |

**V-13 invalid-input/output matrix.** Give every case its own data-row name and
assert that validation reached the intended guard. Supply a valid adjacent
manifest so a test cannot pass through a dead/unconfigured conversion route.
Test missing/malformed JSON; version other than 1; wrong delivery id; missing
required fields; invalid disposition; too many descriptors (configured finite
limit + 1); duplicate/case-colliding output names; missing file; incorrect length
and SHA; absolute drive path, UNC path, `..` using both separators, sibling prefix
such as `output-elsewhere`, junction/symlink/reparse escape, and a rename/link swap
between inspection and sealed copy. Create links only within the fixture and
point to a fixture-owned sentinel; inability to create a required reparse case
is pending coverage, not passing containment evidence. Test prohibited routing
keys individually: Channel, ConversationId, ReplyHandle, ReplyToMessageId, Kind,
RawOverrides, SourceTaskId, project/policy. Reject attempted routing overrides
as invalid output and use originals; never merge them into the frozen reply.
Also place malicious route instructions/attach markers in Markdown/report prose:
those strings remain data and grant no extra path/recipient capability.

For zip staging, test manifest-only extraction, unlisted/traversal/absolute
entries, case collisions, forged lengths/hashes, and a highly compressible entry
whose expanded bytes exceed the configured finite limit. Check expanded bytes
while streaming, not just zip size/metadata. An explicit attachment with Content
uses those bytes even when Source names a sentinel URL or unrelated file; with
no authorized bytes it must not fetch/read that Source for the worker. Inject
I/O denial/full-disk/partial-write/rename failure at the real file-store seam.
No accepted row may reference a partial/unreadable snapshot. If admission fails,
correlation remains owed and retryable; if accepted originals later corrupt or
vanish, expose terminal storage failure, not fabricated source delivery.

**V-15 crash matrix.** Producer evidence sink survives probe death. A transition
into Publishing is persisted before the first possible producer call. An explicit
pre-invocation failure may be retried only when nonacceptance is demonstrable.
Once a dead process has persisted Publishing, recovery cannot assume whether it
called/finished Kafka; prefer uncertainty even for the conservative C-6 cut.

| Cut | Kill after/before | Expected after fresh-process recovery |
|---|---|---|
| C-1 | Partial/complete temporary input staged, before final rename/intent commit | No accepted intent/correlation FK or publication stamp; retry same source creates one complete snapshot and intent. Orphan temp files are safely ignored/cleaned, never published. |
| C-2 | Atomic input finalized and intent + correlation links committed, before conversion task creation | Reuse exact input hash/intent/frozen routing and create one task; source worktree may already be removed. |
| C-3 | Worker task and OutboundDeliveryId committed, before dispatch/after dispatch (two rows) | Recover same task id; queued may dispatch once; submitted/running uses normal transcript reconciliation and owner, never a replacement task. |
| C-4 | Worker completes/output manifest exists before pump observes settlement | Recover settled task and validate existing output once; no fresh worker. Corrupt output chooses original fallback. |
| C-5 | Sealed output and Ready committed, before Publishing | Publish exact saved payload once, do not reopen worker/source-worktree paths. |
| C-6 | Publishing committed, before first possible producer call | PublishUncertain, no automatic retry/success stamp; attention enables an explicit operator retry decision. The conservative hold is intentional. |
| C-7 | Broker accepted record, before producer returns/DB stamps | One independently observed accepted record; PublishUncertain after restart, stamps null, no second record from repeated ticks/triggers. |
| C-8 | Published + matched/source stamps transaction committed | State/stamps all present; repeat triggers/restart produce zero new tasks/records. Add DB commit-failure injection after ack: no partial stamps, uncertainty as C-7. |

For C-7 use both the durable fault sink (repeatable) and the actual disposable
broker in F-5. Kafka/PostgreSQL cannot share one atomic commit here. Explicit
retry of an uncertain send may duplicate a previously accepted provider message;
that limitation must be visible with the recorded attempt, never hidden behind
an exactly-once claim. No automatic retry is added to the gateway in this card.

### Guards the regression

| ID | Regression | Caught by / decisive assertion |
|---|---|---|
| R-1 | Enabled defaults off or settlement starts rendering again | V-1 exact sources with no browser/worker, V-21 dependency/instruction contract; PC-1/PC-2. |
| R-2 | Text re-encoding, 40-document truncation, stale implicit PDF or silent source loss | V-2/V-3 independent bytes/member accounting and V-16 completeness; PC-3..PC-6. |
| R-3 | Provider/project-wide implicit conversion, stale binding, converter self-wait | V-5/V-6/V-18 X/Y/Z policy and pinned identity; PC-7/PC-8/PC-24. |
| R-4 | One caller bypasses policy or withheld/control traffic creates work | V-6/V-7 all send shapes and real control callers; PC-9/PC-10. |
| R-5 | Enqueue pretends publication succeeded or runtime awaits the converter | V-8/V-16 barriers, durable row and null-stamp oracle; PC-11/PC-12/PC-21. |
| R-6 | Competing triggers, restart or two targets produce duplicate/lost work | V-9/V-15 distinct-key, task-link and restart evidence; PC-13/PC-14/PC-23. |
| R-7 | Optional task recursion, bypassed provider hold, expired launch or lost process owner | V-10/V-11 normal delegation/transcript and purpose-only guards; PC-15..PC-18. |
| R-8 | Worker reads unrelated inputs, escapes output root, retargets, or edits sent bytes | V-12/V-13 staged/sealed byte and forbidden-field oracles; PC-19/PC-20. |
| R-9 | Base64/metadata exceeds bus cap or fallback loses originals | V-13/V-23 actual serialization and degraded original bytes; PC-20/PC-22. |
| R-10 | Latest Slack thread overwrites earlier pending reply or following replies overtake | V-14/V-24 exact inbound-derived handles, ordered records; PC-23. |
| R-11 | Slow conversion is reported as inbound loss, or hold/uncertainty as success | V-17 state/attention and TTL tests; PC-25. |
| R-12 | Tool becomes mandatory server dependency, generates unusable PDF or leaks browser | V-19/V-20/V-21 standalone invocation, parsed/visual content and PID cleanup; PC-2/PC-26..PC-28. |
| R-13 | Gateway changes schema/topics/groups or revives false service-down notices | V-22/V-23/V-24 actual wire and monitor controls; PC-29. |
| R-14 | Fake success, zero tests or absent real destination is accepted as release evidence | V-25 and evidence accounting below; PC-30. A local fake/native-looking marker never fulfills actual Slack receipt. |

### Positive controls

Run each independently on the implemented branch: green baseline, apply the
single guard edit, rebuild and execute its named test/data rows, observe the
expected assertion failure, restore exactly that edit, rebuild and see green.
Do not mutate the assertion, fixture input, expected output, or test filter to
manufacture red. Record compile/discovery/fixture failures separately: they are
not a positive-control red. A passed mutant requires strengthening the oracle
or locating the actual guard, not calling it covered. Code records the final
file/line and exact one-line diff because most guarded code does not yet exist.
Where a PC has variants, execute every variant independently; these are not one
combined mutation. Bound each deadlock mutant with the fixture watchdog and
retain proof it reached the intended barrier before the expected assertion.

| ID | One-line guard edit (independent variants where listed) | Expected red, then restored green |
|---|---|---|
| PC-1 | Change default `Deliverables.Enabled` to false; separately add a settlement render call through the test process boundary | V-1 misses exact sources; second variant increments the fake browser log / violates zero rendering work. |
| PC-2 | Restore server renderer registration/reference; separately restore a universal PDF requirement in each of ChannelPreamble, workspace provisioner and orchestrator bundle | V-21 boundary assertions identify each leaked dependency/instruction independently. |
| PC-3 | Replace raw source-byte copy with UTF-8 decode/re-encode; separately reintroduce `.Take(40)` | V-2 BOM/UTF-16/line-ending hash mismatch; >40 member count/path omission failure. |
| PC-4 | Replace manifest member selection with directory enumeration; separately allow implicit historical PDF | V-3 detects unlisted/stale file or PDF among implied attachments. |
| PC-5 | Remove total-source budget check; separately suppress omission record/warning | V-2 exceeds 64 MiB or selected-input accounting/visible incompleteness fails. |
| PC-6 | Replace all-required-source-members predicate with any-member-present | V-16 partial-four-source/incomplete-zip case incorrectly gets complete publication evidence. |
| PC-7 | Replace nullable conversation profile lookup with same-provider/profile fallback | V-4/V-5/V-6 Y or Z unexpectedly creates a converter/PDF. |
| PC-8 | Bypass one policy guard at a time: same-project, dedicated/non-inbound converter, usable contained prompt/workspace, enabled binding | V-5 corresponding invalid-binding row is wrongly accepted or launches. |
| PC-9 | Replace facade call with direct producer in each main/trailing/machine path separately | V-6 publishes before the blocked converter or fails one-intent/task assertion for that shape. |
| PC-10 | Bypass each existing NO_REPLY/unmatched/API-error/origin gate separately; separately let Control origin qualify | V-7 corresponding withheld row sends/adopts an intent; control row wrongly dispatches conversion/recurses. Existing explicit-attachment exceptions remain green after restore. |
| PC-11 | Return Deferred before admission transaction commit; separately await worker completion in facade | V-8 returns with no durable FK/intent or fails the runtime-progress watchdog while conversion barrier remains closed. |
| PC-12 | Set ChannelReplySettledAt/DeliverableDeliveredAt at enqueue (one field per variant) | V-8 null-stamp assertion fails before any producer acceptance. |
| PC-13 | Remove unique source/destination constraint; separately remove target from the key; separately drop trailing text-window identity | V-9 concurrent insert yields >1 intent, two destinations share one outcome, or distinct trailing reply disappears. |
| PC-14 | Skip lease/version predicate on claim; separately create a task without atomic purpose linkage / ignore existing ConversionTaskId on recovery | V-9 or C-3 creates two active owners/tasks or loses task linkage. |
| PC-15 | Bypass internal-purpose suppression separately for bundling, distillation, completion note, card inference/transition and automatic check/diagnose follow-up; separately accept the internal purpose on public create; leak source-task environment/child-dispatch grant into worker context | V-10 observes each forbidden side effect or privilege in its data row; ordinary-task companion still proves normal path. |
| PC-16 | Set each IgnoreSubscriptionQuota / IgnoreModelDisabled / AllowUnauthenticatedProvider bypass true separately, or choose another agent on refusal | V-11 respective held case launches instead of frozen-source fallback or records wrong identity. |
| PC-17 | Remove expiry gate at queued selection; separately remove immediately-before-dispatch gate; separately change deadline equality from expired to live | V-11 corresponding barrier case dispatches after original deadline. |
| PC-18 | Remove one-per-agent/two-global claim cap separately; ignore MaxPending; unconditionally cancel a Working task at timeout; accept late output (each separate) | V-11 corresponding row exceeds dispatch concurrency, launches overflow, loses active task owner, or publishes a second/obsolete result. |
| PC-19 | Skip version, delivery-id, required-field/disposition, count, length, hash, containment/reparse and zip-expanded-byte guard one at a time; separately allow fetching Source without authorized bytes | V-13 each matching malicious row reaches producer/opens the sentinel or passes validation when it must degrade. Each guard needs its own red result; broad validator bypass is insufficient. |
| PC-20 | Merge each prohibited routing property from output separately; publish worker path instead of sealed bytes; omit automatic preservation of originals (each separate) | V-12/V-13 malicious-route, late-edit or source-retention assertion fails. |
| PC-21 | Stamp Published before SendAsync completes; separately commit matched/source stamps outside Published transaction | V-16 blocked-producer or injected commit-failure case shows false/partial success. |
| PC-22 | Check only raw attachment bytes instead of serialized UTF-8; separately skip raw 14-MiB cap; skip validation of fallback; prefer additions over originals | V-23 individual budget/priority cases publish an over-cap payload or lose valid sources. |
| PC-23 | Recover Publishing as Ready; separately reconstruct routing from latest channel handle; bypass destination predecessor check; include already-published target in retry (each separate) | C-6/C-7 auto-replay; V-14 T1 becomes T2/order fails; V-16 multi-target acceptance count increases. |
| PC-24 | Skip policy revalidation before task launch; separately before publish; reuse revised prompt file instead of captured revision | V-18 disabled/rebound boundary sends or launches, or immutable prompt/body hash changes. |
| PC-25 | Remove nonterminal-delivery exclusion from matching/TTL; separately label Held/Failed/PublishUncertain as Published in attention projection | V-17 duplicate/loss notice/premature stamp or false-success presentation. |
| PC-26 | Remove HTML escaping for cover/path; separately remove document page break or skip the last document | V-19 escaping assertion or V-20 parsed order/content/page-start assertion fails. |
| PC-27 | Treat browser exit 0 as success without checking newly produced output; separately skip timeout process-tree cleanup | V-19 no/empty/stale-output row claims success or known fake-browser child remains alive. Parent fixture still cleans only its owned child. |
| PC-28 | Test-only boundary returns a tiny syntactically valid PDF containing just the first source instead of the tool's complete PDF | V-20/V-24 independently parsed headings/body/end sentinels fail. This fault injection validates the artifact oracle; changing the test's expected headings does not count. |
| PC-29 | Change expected/watched inbound group to `antiphon-consumer`; separately classify unknown offsets as committed zero; remove attachment Content from serialized reply | V-22 identity/unknown-evidence assertions fail; V-24 fake Slack byte/upload assertions fail. |
| PC-30 | Give the execution-evidence validator a zero-test TRX, skipped required case, absent restored-green record, fixture-failure mutant, or V-25 without native receipt evidence (separate fixtures) | Validator refuses each as complete. This is a test of evidence accounting, not manufactured chat/approval evidence. |

### Actual mav-ref acceptance procedure (V-25)

Status at TestDesign: **not run; destination unavailable in this checkout**.
The incident identity is mav-ref / PredictionMarkets, mikeysbot-slack,
conversation D0B1VUH2EAK and historical thread 1787842404.752659. Do not reinterpret
that history as authorization to send to the old thread or a convenient local
Slack/Telegram row. This gate borrows CARD-0419's separation of isolated evidence
from live acceptance; its distiller approval, model choice and flags do not grant
permission for this task.

1. Finish S1-S6 and V-1..V-24/positive controls first. Prepare a project-local
   converter workspace, independently tested optional tool, PDF prompt, explicit
   profile example and a migration record with placeholders for unresolved live
   ids. The prompt requires one combined readable PDF containing all source
   headings and returning the generic manifest; originals remain automatic.
   Record tool/server commit hashes and the synthetic specimen result. Do not
   claim the exact historic four source bytes were tested unless obtained.
2. On the actual installation, resolve current project/channel ids and gateway
   ownership read-only through the documented ops front doors. Obtain actual
   D-8 source bytes at the mav-ref commit or record the agreed successors with
   paths, revision and hashes. Resolve an active intended thread from current
   inbound evidence; freeze its actual ConversationId/ReplyHandle/ReplyToMessageId.
   Verify selected dedicated agent's project, workspace, usable authentication,
   current model availability/quota and explicit profile. Never print credentials
   or choose another provider silently.
3. If the session has no existing authorization for that deployment/live send,
   request only that concrete named action after preparation, naming installation,
   active destination and metered converter. Do not add a new global permission
   mechanism or treat waiting as approval. Record authorization reference and
   scope in the rollout evidence without fabricating an approval artifact.
4. In the coordinated upgrade, apply schema, install tool/workspace/profile and
   pre-bind the exact channel before new outbound processing resumes. Record
   evidence there was no source-only configuration gap for this required-PDF
   destination. Verify the running server/tool revisions directly after deploy;
   a healthy stack alone is insufficient. Shared stack restart is not performed
   from this TestDesign worktree.
5. Send one authorized normal docs completion through that bound conversation.
   Capture source/conversion task ids, intent id and state/attempts, accepted
   Kafka payload identity, exact routing, all source/output hashes, gateway
   upload result and native Slack message/file ids or permalink. Download/open
   the received PDF, verify all four sections and rendered pages visually, and
   hash the actual received source attachments/extracted complete zip. Verify
   no unintended duplicate or root-channel post over subsequent pump/recovery
   ticks. A file on the server or DeliverableDeliveredAt is insufficient.
6. Within the same approved scope run source-only control on an explicitly
   unconfigured conversation/project, a non-channel task, and converter-failure
   fallback. Record zero converter launches for default cases, exact sources and
   honest degraded status for failure. If a required control would exceed the
   authorized destination/scope, mark it pending and request that narrow action;
   do not substitute an unrelated local chat.
7. Retain an evidence table: installation/running revisions, authorization
   reference, project/channel/active inbound routing, specimen provenance/hashes,
   pinned converter/profile/prompt revision, delivery/task ids, publication and
   native upload/receipt evidence, PDF opening/page inspection and controls.
   Verdict is pass only with every required item. Missing/expired authorization,
   inaccessible destination, unavailable model, skipped send or unreadable PDF
   remains pending/failed live acceptance. Caller retains this gate before card
   closure; Code/Review may finish while clearly reporting it pending.

### Commands and evidence accounting

Run from the implementation worktree. New project/class names in this section
are creation requirements, not claims they already exist. Use class filters and
fresh unique TRX names; after each run verify actual executed method/data-row
names and nonzero counters. `--list-tests` and exit zero alone are not execution
evidence on the pinned runner. Do not run an untargeted full assembly to discover
whether a new class was included.

```powershell
# S1, S3-S5 and focused regressions; run sequentially.
$classes = @(
  'DeliverableBundleServiceTests', 'SourceBundleManifestTests',
  'AgentTaskReplyIntegrationTests', 'ChannelFollowUpAttachmentTests',
  'ChannelOutboundPolicyTests', 'ChannelOutboundEndpointTests',
  'ChannelOutboundStorageTests', 'ChannelOutboundDeliveryTests',
  'OutboundConversionTaskTests', 'OutboundConversionManifestTests',
  'ChannelOutboundDeadlineTests', 'ChannelOutboundRecoveryTests',
  'ChannelOutboundMigrationTests', 'ChannelOutboundContractTests',
  'ChannelReplyDurabilityTests', 'ChannelMachineTurnMatchTests',
  'ChannelMachineTurnTextTests', 'ChannelBridgeTests', 'ChatChannelServiceTests',
  'AttentionServiceTests', 'InstructionBundleTests',
  'ChannelConsumerIdentityEndpointTests'
)
foreach ($class in $classes) {
  $trx = "c0418-$class-$([guid]::NewGuid().ToString('N')).trx"
  dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0418/ -- --treenode-filter "/*/*/$class/*" --report-trx --report-trx-filename $trx
  if ($LASTEXITCODE -ne 0) { throw "Failed test execution: $class" }
}

# S2 ordinary renderer tests; never parallel with the server assembly.
dotnet run --project tests/Antiphon.MarkdownPdf.Tests --property:OutputPath=bin-c0418/ -- --treenode-filter '/*/*/MarkdownPdfRendererTests/*' --report-trx --report-trx-filename c0418-renderer.trx
dotnet run --project tests/Antiphon.MarkdownPdf.Tests --property:OutputPath=bin-c0418/ -- --treenode-filter '/*/*/MarkdownPdfCommandTests/*' --report-trx --report-trx-filename c0418-command.trx

# Scoped UI test through the required wrapper.
pwsh -NoProfile -File scripts/test-client.ps1 ChannelsPage.test.tsx

# S5/S6 messaging regressions; generated fixture broker only when requested.
$messagingClasses = @(
  'GatewayTests', 'SlackChannelAdapterTests', 'TelegramChannelAdapterTests',
  'GatewayMonitorValidationTests', 'ConsumerLagAssessmentTests',
  'InboundUnconsumedMonitorTests', 'GatewayMonitorStatusTests', 'LibrarySufficiencyTests'
)
foreach ($class in $messagingClasses) {
  $trx = "c0418-$class-$([guid]::NewGuid().ToString('N')).trx"
  dotnet run --project tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c0418/ -- --treenode-filter "/*/*/$class/*" --report-trx --report-trx-filename $trx
  if ($LASTEXITCODE -ne 0) { throw "Failed test execution: $class" }
}
```

Execute each remaining command individually and check its exit code/TRX before
the next. Use fresh TRX filenames on repeats; the fixed filenames below assume
a fresh run directory. Restore environment flags to their prior values after
the isolated lane.

```powershell
$env:ANTIPHON_HEADED_TESTS = '1'
dotnet run --project tests/Antiphon.MarkdownPdf.Tests --property:OutputPath=bin-c0418/ -- --treenode-filter '/*/*/MarkdownPdfRealBrowserTests/*' --report-trx --report-trx-filename c0418-real-pdf.trx
npm --prefix client run build
$env:ANTIPHON_BROKER_TESTS = '1'
$env:ANTIPHON_CHANNEL_OUTBOUND_ISOLATED = '1'
dotnet run --project tests/Antiphon.E2E --property:OutputPath=bin-c0418/ -- --treenode-filter '/*/*/ChannelOutboundIsolatedTests/*' --report-trx --report-trx-filename c0418-isolated.trx
```

For touched deadline/purpose code also run the affected existing named classes
`TaskDeadlinePolicyTests`, `AgentTaskOverdueDeadlineTests`,
`SpecialistTaskRunnerDeadlineTests`, `OutputDistillationDeadlineTests` and
`PinnedAgentKindTests` using the same Antiphon.Tests command; add a class only if
its guarded path was touched. Run `AgentTaskSettlementRaceTests` when settlement
locking changed. No Pty implementation change is planned; if one becomes
necessary, select its actual touched Pty classes and execute that assembly only
after Antiphon.Tests has exited. Do not use `dotnet test`.

Standalone tool rerun, after publishing/building the tool into the fixture's
output folder and writing its manifest (replace the two fixture paths):

```powershell
dotnet run --project tools/Antiphon.MarkdownPdf --property:OutputPath=bin-c0418/ -- --manifest '<absolute fixture request.json>' --output '<absolute fixture output\combined.pdf>' --timeout-seconds 30
```

Code commits an evidence index (for example
`docs/investigations/2026-09-07-card-0418-verification.md`) linking each V/R/PC to
implemented class/method/data rows, implementation SHA, command, executed/pass/
fail/skip counts, TRX/log path, fixture/intent/task ids, crash cut and actual byte/
state assertions. Retain PDFs/page images and sanitized fault traces under the
ignored fixture evidence directory and report their absolute paths. A small
test-only evidence validator checks required ids/variants, real TRX outcomes,
expected assertion-red/restored-green pair and separate live-gate fields (PC-30).
Do not add secrets, source request bodies from real chats, or deployment config
to the committed evidence index. Report tests not run as pending, not zero-fail.

Existing expectations that must change deliberately: the four-doc
`DeliverableBundleServiceTests` case currently expects a render error/log and
`4 md, pdf failed`; change it to successful source-only evidence. The current
`ChannelFollowUpAttachmentTests.An_over_budget_pdf_is_skipped_with_a_warning_and_sources_still_stamp`
and `All_over_cap_files_send_the_text_but_do_not_stamp` use an implicitly
enumerated legacy PDF. Keep their budget intent by making that PDF **explicit**
or by feeding a requested conversion; add separate legacy-PDF exclusion cases.
Otherwise those tests can stop exercising an over-cap attachment while looking
green. Preserve explicit-marker ordering/dedup and the normal no-profile
claim/retry regressions rather than rewriting them to assume every send defers.

### Out of scope

- Implementing production S1-S6, running their still-unwritten tests, deployment,
  card moves or real messages in this TestDesign stage. This artifact defines
  the verification; it is not evidence that those behaviors already work.
- Exactly-once Slack/provider delivery, Kafka/gateway commit redesign, durable
  storage surviving disk loss, or automatic resolution of PublishUncertain.
  The plan explicitly retains the Kafka/PostgreSQL uncertainty window.
- Fetching historic mav-ref documents, guessing a destination from CARD-0337's
  old thread notation, or using CARD-0419's canary approval for this task.
  Actual destination/model/receipt evidence remains V-25, not an isolated pass.
- A general conversion workflow engine, provider/project-wide inheritance,
  source-discovery expansion, new PDF marker/HTTP API, or unrelated CARD-0367.
- Broad unrelated suite runs and real browser/provider work in ordinary tests.
  Structural checks complement, never replace, the real-path behavioral cases.

### Cost

Suites forced: focused Antiphon.Tests classes above; dedicated MarkdownPdf tests;
focused Antiphon.Messaging.Tests; ChannelsPage Vitest; one isolated outbound E2E
class with a real browser and disposable broker. No full Pty suite or full
Antiphon.Tests run by default.

Estimated verification floor after tests exist: 25-40 minutes for build, focused
normal tests, real PDF inspection, all process-death cuts and isolated broker
path on a provisioned Windows/Docker host; 60-120 additional minutes for separate
mutation variants with cached builds. These are planning estimates, not measured
results; record actual counts/times and optimize slow fixtures without dropping
boundaries. Live V-25 is separately scheduled once authorized access/profile/
thread are available (allow 20-40 minutes excluding deployment/access waits).

TestDesign completion: appended this verification section only; no runtime code,
configuration, tests or live state changed. Code can implement and execute this
section without a product decision. Retain the explicit pending V-25 gate in
handoffs through Review and deployment.
