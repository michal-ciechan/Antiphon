# CARD-0410: gateway consumer-group configuration and trustworthy lag notices

Plan complete, 2026-09-06. Base: `567f9e95`. Implementation and production deployment have not been performed. Complexity: medium; next stage: TestDesign, then Code.

Correct the deployed gateway to `Kafka__AntiphonConsumerGroup=antiphon-server-bridge`, make the deployment derive that identity from the server, and allow customer outage acknowledgements only with a successful, numeric committed-offset observation. Unknown evidence becomes a visible monitor fault.

## Evidence and ground truth

The incident findings are inherited from the complete report of Investigate task `5172b16f` on CARD-0410. This plan does not repeat the live investigation. Source inspection here establishes implementation seams only.

| Card assumption / brief requirement | Established behavior and code owner |
|---|---|
| The real answer was replaced by an outage message | Investigate confirmed two independent sends. `InboundUnconsumedMonitorService.AcknowledgementText` supplied the false notice directly through `IChannelAdapter`; the real invoice reply also reached Telegram. |
| am-service might be an external Node component | It is this repository's separately deployed .NET `src/Antiphon.Messaging.Service`, using `Antiphon.Messaging.Gateway`; its image is rebuilt from source. |
| Fix the monitored group | Live container settings: `antiphon-consumer`, no environment override. Actual server: `antiphon-server-bridge`, one member, committed next offset 182, zero lag at investigation time. These numbers are historical evidence, not deployment acceptance constants. |
| Defaults are already aligned | `server/appsettings.json` uses `AntiphonMessaging:ConsumerGroup=antiphon-server-bridge`. Service `appsettings.json` and `AntiphonGatewayOptions.AntiphonConsumerGroup` use `antiphon-consumer`. The portable client SDK also defaults to `antiphon-consumer`; that default has legitimate users. |
| The gateway's own ConsumerGroup is the setting to change | `Kafka:ConsumerGroup` belongs to outbound delivery. `Kafka:AntiphonConsumerGroup` is the server's inbound group. Never rename the outbound or inbox groups to fix this incident. |
| Missing offset proves lag | `KafkaConsumerGroupOffsetReader` returns null for an empty result, missing partition, partition error, `Offset.Unset`, and exceptions. `ConsumerLag.IsUnconsumed(null, record)` currently returns true. |
| Startup or health detects bad scoping | `AddAntiphonGateway` only binds options; the monitor checks offsets only when overdue receipts exist. Service `/health` always returns `{ status: "ok" }`. No startup identity check exists. |
| AppHost health prevents a false notice | The monitor acknowledges first and probes health afterward for event diagnostics. Preserve health as diagnostic information, not lag evidence. |
| Existing tests protect null behavior | `InboundUnconsumedMonitorTests.Absent_group_offset_is_unconsumed` explicitly pins the incorrect result. There is no real-broker offset-reader coverage in this test project. |
| A source deploy fixes persistent settings too | `scripts/deploy-am-service.ps1` replaces `build/src` and rebuilds/recreates the service. It currently neither corrects remote Compose environment nor compares the monitored identity or broker offsets. |

Other relevant seams: `EfInboxReceiptStore.GetOverdueAsync` returns all old receipts with topic/offset, including historical acknowledged ones; preserve their acknowledgement/event watermarks. `KafkaAntiphonMessagingConsumer` logs its effective topic/group/broker and enables auto-commit. Committed offset is evidence of Kafka consumption, not completion of an agent turn or delivery of an answer.

## Decisions

**D-1: Correct the application profile; preserve portable SDK identity.** Change `src/Antiphon.Messaging.Service/appsettings.json` to the actual server group. Keep the portable client and gateway library defaults compatible with each other; do not globally replace every `antiphon-consumer` string. The source-built am-service deployment must persist the explicit environment override even after the image default is fixed. Other deployments, such as school_revision, must supply their own application's inbound group or disable this monitor; they are not deployment targets for this card.

**D-2: Derive deployment identity from the running server, with checked repository defaults.** `server/appsettings.json` remains the repository's default. Add a small read-only server endpoint, `GET /api/channels/consumer`, returning only the effective inbound `consumerGroup`, `inboundTopic`, `enabled` flag, and broker host/port list from the same `IOptions` values used by the consumer and bridge. Allowlist broker addresses as parsed host/port values; never serialize arbitrary client configuration. No settings dump, connection strings, tokens, or user message data. The deploy script reads it using the normal API base/token convention and records `/api/version` separately for provenance.

The runtime value includes environment overrides and is the deployment authority. A contract test compares the checked-in service profile with `server/appsettings.json`, catching source drift. Preflight prints the repository default, running server identity, current gateway identity, and proposed change. A missing/unavailable identity endpoint, disabled bridge, unexpected inbound-topic difference, or identity change between preview and write refuses deployment; do not silently fall back to a guessed group. An intentional server override is shown explicitly and used for the proposed group.

Add `ExpectedAntiphonConsumerGroup` to gateway options. The standalone Service profile supplies it; the deploy script generates both it and `AntiphonConsumerGroup` from the single server observation. It is an independent startup guard against a stale/manual override of the watched setting, not a second operator-maintained source of truth. The portable gateway permits it to be omitted; if present it must match. The standalone Service requires it when this monitor is enabled.

The new server endpoint is a justified small server change: broker existence alone cannot distinguish the real server group from a different valid group with offsets. This costs a server deployment before the full gateway deployment. Future server group renames require a coordinated gateway deployment; offset migration/replay semantics for a rename are outside this fix. Document that rule beside the server setting and in the deploy owner. No continuous dependency on the server HTTP endpoint is introduced into the gateway monitor: it must still work when the server is down.

**D-3: Fail closed for customer notices, loudly for operators.** Use three lag outcomes: `Unknown`, `Consumed`, `Unconsumed`. A successful nonnegative committed-next offset `C` compared with valid receipt offset `R` means `C > R` is consumed and `C <= R` is unconsumed. Null and all negative/sentinel offsets are unknown. Keep `IsUnconsumed(long?, long)` as a compatibility wrapper that returns true only for confirmed unconsumed; expose the richer assessment to the monitor.

For unknown observations: no adapter send, no `InboundUnconsumedEvent`, no acknowledgement watermark, no operational-event watermark, and no retry scheduling. Re-evaluate on a later successful poll. Do not substitute zero, a high watermark, a previously cached offset, health failures, or member count. A known group with zero members and real offsets must still detect backlog: that is a normal server-outage case. Missing commits on a fresh group or new partition deliberately suppress notices until there is evidence. The cost is a possible missed outage acknowledgement, preferable to confidently telling users a working service is unavailable.

**D-4: Separate configuration failure from unavailable broker evidence.** Synchronous options validation rejects blank group/topic, watched group equal to the gateway's outbound group, and expected/watched mismatch before hosted loops start. Kafka group names are case-sensitive; compare exactly and reject accidental surrounding whitespace. Preserve monitor-disabled/no-inbox-store hosts without imposing live-broker startup requirements.

Perform a bounded broker validation on monitor startup, before its first receipt pass, even if the inbox is empty. A positively absent group or a group with no relevant committed offsets must fail *monitor readiness* immediately, with an Error diagnostic, not silently start as healthy. Connectivity/authorization/query failures are distinguishable from an absent group. Keep the gateway ingress/outbound loops running and retry validation at the configured poll cadence; killing the whole gateway because its diagnostic monitor cannot observe Kafka would worsen an outage. Empty membership by itself is never an unknown-group verdict. This is a loud degraded startup, not an exception/restart loop for transient broker state.

Add a dedicated monitor status endpoint, `GET /health/inbound-unconsumed`, to the standalone Service. Return 503 for validating/degraded/stale monitoring and 200 for ready or explicitly disabled, with a distinct state in the payload. Keep `/health` as existing process liveness. The dedicated endpoint is a deployment/operator readiness check, not a new automatic process-kill signal. Expose only state, reason code, expected/watched group, topic, observation time, and per-partition offset/status. Log a structured Error on entering an unknown state, rate-limited reminders (five minutes), and an Information recovery event. No customer chat or new incident/alert sink is used for these faults. Log/health polling remains available if the broker itself is broken.

**D-5: Preserve wire behavior and idempotency for proven lag.** Keep acknowledgement text, five-minute age threshold, poll cadence, reply addressing, retry backoff, and existing event schema. Gate *before* `TryAcknowledgeAsync`, health probing, publishing, or any receipt mutation. Retain the existing logical deduplication/retry tests; do not claim network-exactly-once delivery. Do not reset offsets or backfill/replay customer receipts. Old falsely acknowledged receipts are not reopened.

**D-6: Bound the change and production authority.** No attachment matching, transcript, invoice-delay, or unexplained restart work. Plan/TestDesign/Code may implement and verify isolated code, but cannot deploy production. Production config correction, server rollout, am-service rebuild/recreation, rollback, and real-chat smoke require a separately authorized Deploy/operator step. No approval is needed to finish this plan or implementation work.

## Code slices

### S1. Correct profile and add identity contract (independently reviewable)

Files: `src/Antiphon.Messaging.Service/appsettings.json`, `server/Api/Endpoints/ChannelEndpoints.cs`, a small DTO if useful under `server/Application/Dtos/`, and new `tests/Antiphon.Tests/Application/ChannelConsumerIdentityEndpointTests.cs`.

- Set the Service's watched and expected groups to `antiphon-server-bridge`; leave server consumption, gateway outbound, and SDK default groups unchanged.
- Implement the allowlisted endpoint from D-2, using typed options, without starting a consumer, connecting to Kafka, or creating a new configuration service abstraction.
- Test that an effective options override is returned, disabled is represented, and unrelated secret-bearing settings are absent. Booting real server Program must use the existing `ProductionRunnerGuard` / `AntiphonWebAppFactory` isolation.
- Add a repository-profile alignment assertion in `scripts/test-deploy-am-service.ps1`: server default equals Service watched/expected defaults. Tests must allow a genuinely different portable SDK default.

Exit: the current profile mistake is fixed in source and the deployment has an authoritative read-only identity source. S1 alone is not the complete production fix.

### S2. Model broker evidence without conflating failures with lag

Files: `src/Antiphon.Messaging.Gateway/ConsumerLag.cs`, `KafkaConsumerGroupOffsetReader.cs`, `IConsumerGroupOffsetReader.cs`, new observation types/I/O seam under that project, `ServiceCollectionExtensions.cs`, `PublicAPI.Unshipped.txt`; tests under `tests/Antiphon.Messaging.Tests/Gateway/`.

- Introduce a typed group/partition observation: group present/absent/query-failed; partition committed-offset/no-commit/query-failed. Only committed-offset carries a nonnegative numeric value. Include safe reason codes, not exception messages containing arbitrary broker output.
- Use broker group description and topic/partition metadata plus committed-offset queries. Do not infer group absence from null offsets or zero members. An ambiguous broker response is unknown evidence, not confirmed group absence.
- Read the relevant topic's partition offsets once per monitor pass; include all current partitions for startup/readiness, and classify any receipt partition outside that snapshot as unknown. A partial query failure must not invalidate valid observations on other partitions or borrow their offsets.
- Bound each admin query and the whole pass, honor cancellation, and dispose/reuse the admin client with an explicit DI lifetime. Target a 10-second total observation budget with cancellation-aware waiting; no unbounded Kafka call during startup/shutdown. Do not swallow caller cancellation as an observation failure.
- Preserve the existing public nullable reader signature as a compatibility adapter. Add a richer external-I/O interface implemented by the Kafka reader and consumed by the monitor; the legacy API projects unknown to null, whose documented meaning becomes unknown. Do not silently break published implementers. Update API baselines for additive types/constructors as required.
- Change the pure lag assessment and its existing null test. Negative sentinel and invalid receipt offsets must never authorize a send.

Implementation reference: Confluent exposes group description and committed-offset operations through [IAdminClient](https://docs.confluent.io/platform/current/clients/confluent-kafka-dotnet/_site/api/Confluent.Kafka.IAdminClient.html). Verify the concrete options/error mapping against this repo's 2.6.0 test dependency; current online API docs alone do not establish pinned-version or Redpanda absent-group behavior. TestDesign must include a disposable-broker check for that mapping.

### S3. Validate startup and gate notifications, with operator visibility

Files: `AntiphonGatewayOptions.cs`, `ServiceCollectionExtensions.cs`, `InboundUnconsumedMonitorService.cs`, new validator and concrete monitor status holder under Gateway, `src/Antiphon.Messaging.Service/Program.cs`; `InboundUnconsumedMonitorTests.cs` plus new validation/status tests.

- Register pure `IValidateOptions<AntiphonGatewayOptions>` with `ValidateOnStart`; enforce Service-only expected-group requirement at its composition root. Ensure validation applies to both `AddAntiphonGateway` overloads, with disabled/no-store behavior tested.
- Run the startup observation before processing receipts. Refresh observations/status even with no overdue rows; otherwise an empty inbox falsely appears healthy indefinitely.
- Consume only the current pass's typed evidence. Unknown partitions skip every customer/event/store side effect; known partitions retain existing lag behavior. Receipt-query failure also degrades monitor readiness rather than leaving a misleading successful status.
- Add the read-only readiness endpoint and state-change logging from D-4. A snapshot older than two poll intervals plus the query budget is stale; do not report cached Ready indefinitely if the loop fails. Explicitly disabled/no-store is visible as Disabled, never Ready.
- Retain recoverability: absent group -> correct group established -> real commits -> Ready without gateway restart; known lag -> one notice/event -> committed past -> no more action. The expected group itself is fixed for the process and changes by validated restart.
- Preserve existing public construction seams where feasible via overloads; use DI-selected richer dependencies in production. Update fake/sample hosts and tests only where registration compatibility requires it; do not route them to the live broker or require an inbox they do not have.

Exit: an obsolete/unknown group produces an operator-visible monitor fault, and no false customer notice or operational lag event.

### S4. Make the approved deploy persist and verify the correct configuration

Files: `scripts/deploy-am-service.ps1`, `scripts/test-deploy-am-service.ps1`, and, if useful, a tracked non-secret Compose override template under `deploy/am-service/`. Keep PowerShell ASCII-only. Extend the existing fake HTTP/SSH/SCP seams; do not add an ad hoc second deploy command.

- Default and `-WhatIf` remain read-only remote preflight. Read repository defaults and the server identity endpoint; inspect only allowlisted gateway settings. For the old gateway lacking readiness, extract only these non-secret keys from container configuration/appsettings inside the remote projection. Never print full Compose config or container environment.
- Preview the concrete change: watched/expected group become the server's effective group (`antiphon-server-bridge` here), with outbound group unchanged. Bootstrap remains this deployment's existing broker listener. Refuse unexplained topic/broker-target differences; internal/external listener text is not itself proof of different brokers. Resolve broker identity with read-only metadata from the server's reported host/port list and the gateway's effective listener, using a testable probe invoked from the appropriate network context. No shell interpolation of unchecked endpoint data; validate or pass structured arguments. Never use the production group in a consuming client for verification.
- Under the existing `-Deploy`/ShouldProcess gate, persist a small managed Compose override for `messaging-service` containing the watched/expected group settings. Back up any previous managed file, verify its effective merged settings before recreating, and report the retained backup alongside the existing source backup. Do not edit secret `.env` files or overlay arbitrary remote source.
- Choose an override filename loaded by normal `docker compose` from the deployment directory. If a default override already exists with unrelated content, preserve it and refuse an unsafe overwrite; Code/TestDesign must cover that case. Make the script's build/up/config checks and documented future operator invocations load the identical persisted configuration. An override used only in one ephemeral command is insufficient.
- Re-read server identity before mutation and after verification; a concurrent rename fails the operation visibly. The script must validate the actual running gateway's readiness payload and sampled offsets, not just the staged JSON or container state. Missing new endpoint, wrong effective group, stale observation, or unknown offsets cannot produce a successful monitor verification.
- Keep the existing fixed target, Dockerfile-derived archive, migration/adapters checks, secret-safe output, `REMOTE DEPLOY VERDICT`, and explicit human traffic check. Include current image/container identity and a build marker for the new code in evidence, since health alone does not prove the new binary loaded.

Exit: a redeploy cannot silently retain `antiphon-consumer` through an environment override, and a different valid group cannot pass by merely existing on Kafka.

### S5. Owner documentation and verification handoff

Update `docs/telegram.md` (monitor semantics), `docs/telegram-bot-ops.md` (deploy/verification/rollback), `docs/messaging-standalone.md`, Service/Gateway READMEs (watched versus outbound groups, custom application configuration, required expected group in Service), and `docs/antiphon-api.md` (new server route). Put detailed behavior in its owner, not AGENTS.md. Describe coordinated server-group changes next to deployment instructions.

No database migration, client UI, session-runner rollout, or Kafka wire-schema change is expected. The existing gateway database migration procedure still runs during deployment.

## Verification design input for TestDesign

TestDesign is a separate stage because this crosses nullable API semantics, startup ordering, broker metadata, public package seams, and production deployment behavior. It must provide executable V/R/PC cases before Code; the matrix below is the minimum acceptance contract, not a claim that tests ran.

| ID | Required proof |
|---|---|
| A1 | Original defect: obsolete watched group with no commits plus overdue receipts sends zero notices/events and mutates no receipt; Error + degraded readiness identify the group. |
| A2 | Null, empty report, missing partition, unset/negative offset, partition error, authorization failure, timeout, and thrown query each stay unknown; cancellation terminates promptly. |
| A3 | Numeric next offsets immediately below, equal to, and above the receipt boundary yield unconsumed, unconsumed, consumed; zero is a valid commit. |
| A4 | Known group with zero members and a retained commit still detects genuine lag. Unknown group and known/no-commit partition have distinguishable diagnostics and neither sends. |
| A5 | Partial partition failure permits only confirmed partitions to act. No cross-partition values, stale-success fallback, or retry/watermark mutation on unknown. |
| A6 | Startup with empty inbox still probes; mismatch/blank/self-group fails options validation before loops; known alternate group rejected when expected differs. Disabled/no-store hosts require no broker. |
| A7 | Transitions unknown -> committed past, unknown -> confirmed lag, failure -> recovered, and fresh partition -> first commit update readiness and notification behavior correctly. Old cached Ready expires. |
| A8 | Existing acknowledgement addressing, retry backoff, successful-send deduplication, one operational event, age gate, and health-is-diagnostic tests remain meaningful. |
| A9 | Profile defaults align with server JSON; runtime endpoint reflects overrides, uses no broker and emits only allowed fields; generic SDK defaults remain supported. |
| A10 | Deploy preflight/WhatIf makes no remote writes; explicit deploy persists override across ordinary recreation; conflicting managed file refuses; secrets never enter output. Effective mismatch/stale/unknown offsets fail postdeploy verification even when `/health` and `/api/channels` pass. |
| A11 | Disposable real broker: never-created group, empty/no-commit group, committed group after member exits, real backlog, later caught-up commit. Confirm the chosen 2.6.0/Redpanda result/error mapping. Use unique topics/groups and test-owned containers, never production runner/broker. |

Use the existing in-memory receipt/adapter/publisher harness and an offset-over-real-clock or correctly driven fake timer. Add `KafkaConsumerGroupObservationTests`, `GatewayMonitorValidationTests`, and `GatewayMonitorStatusTests` as appropriate; keep exact class-filter commands in the TestDesign artifact. Positive controls should restore null-as-lag, bypass expected-group validation, and omit the persisted deploy override one at a time and prove the corresponding acceptance test fails.

Expected targeted commands (new class names finalized in TestDesign):

```powershell
dotnet run --project tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c0410/ -- --treenode-filter "/*/*/InboundUnconsumedMonitorTests/*"
dotnet run --project tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c0410/ -- --treenode-filter "/*/*/GatewayTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0410/ -- --treenode-filter "/*/*/ChannelConsumerIdentityEndpointTests/*"
pwsh -NoProfile -File scripts/test-deploy-am-service.ps1
dotnet build src/Antiphon.Messaging.Service/Antiphon.Messaging.Service.csproj --property:OutputPath=bin-c0410/
```

Also verify the public API baseline and existing library/sample sufficiency classes if registrations/signatures change. Do not run the full server or Pty suites for this localized change. Process-spawning tests take the assembly-local limiter; real Program hosts retain production-runner isolation. TestDesign must specify the disposable-broker command and cleanup ownership rather than assume local port 19092 is safe.

## Production deployment and acceptance (separately authorized)

1. Land the completed/tested implementation through normal delegation landing. From the main checkout, verify the intended commit is present. Deploy the server identity endpoint through `pwsh -NoProfile -File scripts/deploy-local.ps1` under the orchestrator's deploy decision; verify `/api/version` and `/api/channels/consumer` reflect the new code and actual group. No server consumer-group rename is needed for this incident.
2. Run `pwsh -NoProfile -File scripts/deploy-am-service.ps1` for the concrete read-only preview. Confirm the new effective setting will be `Kafka__AntiphonConsumerGroup=antiphon-server-bridge`; expected group matches; inbound topic is `channels.inbound`; gateway outbound group is unchanged. Verify server/gateway broker listeners refer to the same broker/cluster using narrowly projected broker metadata, not equality of internal and external hostnames. TestDesign/Code must incorporate that check into the preflight evidence.
3. Only an explicitly authorized production operation runs `pwsh -NoProfile -File scripts/deploy-am-service.ps1 -Deploy -Confirm` (or a Deploy-role brief that explicitly permits noninteractive confirmation). This rebuilds/recreates am-service and persists the override. Publishing a package or restarting AppHost alone does not deploy this fix.
4. Require the actual gateway status endpoint to show fresh evidence, the expected group and inbound topic, and real committed offsets for relevant existing partitions. Independently query `am-redpanda` group offsets through the deploy script's read-only broker projection and compare group/topic/partition observations. Values can advance between reads; do not demand exact equality across time. Old offset 182 is not a required value. Ready means observable, not necessarily zero lag; backlog is an honest diagnostic, not an automatic rollout failure.
5. With explicit traffic authorization in the Deploy brief or from the operator, use only the Antiphon-Family test group (`-5370465377`), not Family or AZ Care. Record one new receipt's partition/offset, successful real reply, and a later server commit strictly past that receipt. Observe at least the configured age threshold plus two poll intervals (normally seven minutes): no outage acknowledgement, no new lag operational event, fresh monitor observations throughout. Check the receipt identifiers, not global event counts. Do not stop production consumption to manufacture lag; A11 provides that proof in isolation.
6. Save deploy SHA/build marker, container/image ID, effective group/topic, sampled broker offsets/timestamps, monitor readiness, and test receipt/reply IDs. Never save secrets or full message bodies for this evidence. A healthy process, silent log, or delivered reply alone is insufficient. If the traffic check is pending, report technical deployment separately and leave end-to-end acceptance pending.

Rollback is an explicit operator decision using the retained source and managed-config backups, followed by the same verification. Do not automatically restore the bad group. If old binaries must return, keep the corrected group, or explicitly disable only the unconsumed monitor (`Kafka__InboundUnconsumedMonitorEnabled=false`) to prevent recurring false notices while normal ingress/outbound continues. Do not clear receipt state or manipulate Kafka offsets as rollback.

## Exclusions and remaining decisions

Excluded: 06:58/06:59 `ChannelAttachmentsDropped` at sequences 911/920; cause of the 06:53:01 gateway restart; invoice publication delay; auto-commit/answer-completion semantics; group rename/replay migrations; new pager infrastructure; non-family instance deployments.

No product decision blocks TestDesign or Code. D-1 through D-6 are the implementation defaults. The later operator decision is authorization of the concrete server/am-service deployment and the test-group traffic check. This Plan stage changed only this document; no builds, tests, stack changes, production writes, or messages were performed.
