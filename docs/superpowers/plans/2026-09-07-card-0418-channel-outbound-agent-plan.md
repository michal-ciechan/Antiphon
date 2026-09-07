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
