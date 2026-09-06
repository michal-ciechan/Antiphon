# CARD-0410 implementation and verification report

S1-S5 implementation and all required verification pass; required build-output cleanup is blocked by tool policy.

Implementation branch: `feat/card-task-6ec07844-implementation`, based exactly on supplied plan/test-design commit `7368bd60`. The initially provisioned task branch diverged, so it was preserved and a new implementation branch was created at the required base.

No production deployment, server2 command, live gateway/broker mutation, customer message, local stack restart, offset reset, or receipt replay was performed. Remote-deploy verification used fake HTTP/SSH/SCP runners exclusively. Real Kafka tests used only disposable, randomly mapped Testcontainers Redpanda and unique c0410 test topics/groups.

## Result and implementation

- Service watched/expected groups are `antiphon-server-bridge`; portable SDK/Gateway defaults and gateway outbound/inbox group identities are preserved.
- `GET /api/channels/consumer` returns only effective consumerGroup, inboundTopic, enabled and parsed broker host/port pairs. The DTO performs no broker I/O; the HTTP host test retains ProductionRunnerGuard and RefusingSessionRunnerClient.
- Typed group and partition observations distinguish absent groups, query failures and no-commit partitions. Only a successful nonnegative commit on the receipt partition can authorize a notice; no cached or cross-partition offset is used.
- Startup validation, empty-inbox probing, ten-second observation budget, cancellation, readiness freshness (130 seconds at defaults), degradation/recovery logging and all unknown-evidence side-effect gates are implemented. Service requires the expected group through PostConfigure.
- Deploy derives identity from the running server, compares broker metadata, persists a marked default Compose override, preserves unrelated defaults, checks the effective merge, backs up managed configuration/source, verifies readiness payload/offsets/image identity and detects concurrent server identity changes.
- Owner documentation covers monitoring, custom application configuration, coordinated group changes, separately authorized production rollout/traffic verification and rollback.

## Final test and build results

| Scope | Passed | Failed | Skipped |
|---|---:|---:|---:|
| ConsumerLagAssessmentTests | 18 | 0 | 0 |
| InboundUnconsumedMonitorTests | 18 | 0 | 0 |
| GatewayMonitorValidationTests | 15 | 0 | 0 |
| GatewayMonitorStatusTests | 5 | 0 | 0 |
| GatewayTests | 6 | 0 | 0 |
| LibrarySufficiencyTests | 6 | 0 | 0 |
| EchoGatewaySampleTests | 5 | 0 | 0 |
| KafkaConsumerGroupObservationTests (Docker opt-in) | 9 | 0 | 0 |
| ChannelConsumerIdentityEndpointTests (isolated PostgreSQL HTTP host) | 2 | 0 | 0 |
| deploy-am-service PowerShell contract checks | 54 | 0 | 0 |

Final total: **82/82 messaging tests, 2/2 server identity tests, 54/54 script checks**. Gateway and Service builds each completed with **0 warnings and 0 errors**. The FakeGateway and EchoGateway dependents compiled in the messaging test graph. The deploy script remains ASCII and its embedded Python projections parse successfully. `git diff --check` passed. Full server/Pty/client suites were not run; the plan requires only these targeted classes.

## Disposable-broker findings

- Resolved dependencies: `Confluent.Kafka/2.6.0`, `Testcontainers.Redpanda/4.14.0`. Image pinned to `docker.redpanda.com/redpandadata/redpanda:v25.3.4`, random host port, test-only groups/topics.
- Final V-35a raw description: **State=Dead, Error=NoError**. Raw list-offset result: **Offset=-1001, Error=NoError**. A second description was also **Dead/NoError**. This maps to Absent/group_absent, never lag.
- An earlier fresh-cluster probe temporarily returned Unknown/NotCoordinatorForGroup before settling. The reader treats this as QueryFailed; it never interprets it as proof of lag or group absence.
- A joined member without commits is Present/NoCommit/no_committed_offsets. A committed group after its member leaves is Present with MemberCount=0 and the retained numeric commit. Backlog, caught-up and mixed committed/uncommitted partition cases pass.
- The module default v22.2.1 run ended without a completed test summary; it is not counted as green. The subsequent pinned-image runs captured the test process exit code explicitly.
- An added configuration assertion found that the startup WithCommand flag alone left auto_create_topics_enabled=true on this image. The fixture now also applies `rpk cluster config set auto_create_topics_enabled false` inside its disposable container before any test, and independently reads back false. The absent-topic case remains green with that setting verified.
- The actual rpk metadata response is tested for its `cluster_name` field (Kafka cluster identity); the deployment comparison uses that field, not equality of internal/external listener text. Primary reference: https://github.com/redpanda-data/redpanda/blob/v25.3.4/src/go/rpk/pkg/cli/cluster/info.go

## V-1 through V-36 coverage

Every planned V case has an executable passing check. The descriptions below identify the planned case; the final class/script totals above are observed results, not planned counts.

- V-1: original defect (A1): watched group Absent, one overdue receipt (offset 10, age 6 min) | `InboundUnconsumedMonitorTests.Absent_group_with_overdue_receipt_sends_nothing_and_degrades_readiness` — **green**.
- V-2: every unknown evidence kind stays Unknown (A2) | `ConsumerLagAssessmentTests.Assess_is_unknown_for` with `[Arguments]`: partition `QueryFailed`; partition `NoCommit`; receipt partition missing from snapshot; group `Absent`; group `QueryFailed` with each of `broker_unreachable`, `query_timeout`, `authorization_failed`, `query_failed`; `CommittedOffset` carrying -1 or -1001; record offset -1 — **green**.
- V-3: legacy wrapper never authorises a send on missing evidence (A2) | `ConsumerLagAssessmentTests.Legacy_IsUnconsumed_is_true_only_for_confirmed_lag` plus the existing test renamed to `Absent_group_offset_is_unknown_not_unconsumed` — **green**.
- V-4: numeric boundary (A3) | `ConsumerLagAssessmentTests.Assess_boundary` — **green**.
- V-5: reader failure and cancellation semantics (A2) | `KafkaConsumerGroupObservationTests.Unreachable_broker_is_query_failed_within_budget` (bootstrap `127.0.0.1:1`, budget 2 s) and `.Cancellation_propagates_and_is_not_an_observation_failure` (token cancelled after 200 ms) — **green**.
- V-6: known group with zero members and a retained commit still detects lag (A4) | `InboundUnconsumedMonitorTests.Known_group_with_no_members_and_retained_commit_still_detects_lag` (Present, `MemberCount` 0, p0 `CommittedOffset` 10, receipt offset 10) — **green**.
- V-7: absent group versus no-commit partition are distinguishable and neither sends (A4) | `InboundUnconsumedMonitorTests.Absent_group_and_no_commit_partition_have_distinct_reason_codes_and_neither_sends` — **green**.
- V-8: partial partition failure isolates partitions (A5) | `InboundUnconsumedMonitorTests.Partial_partition_failure_acts_only_on_confirmed_partitions` (receipts on p0 offset 10 and p1 offset 10; p0 `CommittedOffset` 10, p1 `QueryFailed`) — **green**.
- V-9: no stale-success fallback (A5) | `InboundUnconsumedMonitorTests.Unknown_after_success_does_not_reuse_the_previous_offset` (tick 1 `CommittedOffset` 11, tick 2 `QueryFailed`) — **green**.
- V-10: no cross-partition borrowing (A5) | `InboundUnconsumedMonitorTests.Receipt_partition_outside_snapshot_is_unknown` (receipt on p1; snapshot has only p0 `CommittedOffset` 99) — **green**.
- V-11: unknown evidence gates before every side effect (A8, D-5) | `InboundUnconsumedMonitorTests.Unknown_observation_gates_before_acknowledge_health_and_publish` (health fake counts calls) — **green**.
- V-12: empty inbox still probes and reports (A6) | `InboundUnconsumedMonitorTests.Empty_inbox_still_observes_the_group_and_updates_readiness` — **green**.
- V-13: one broker read per pass (S2) | `InboundUnconsumedMonitorTests.Three_overdue_receipts_share_one_observation` — **green**.
- V-14: options validator rules (A6, D-4) | `GatewayMonitorValidationTests.Validator_rejects` with `[Arguments]`: blank watched group; blank inbound topic; watched == outbound `ConsumerGroup`; expected `antiphon-consumer` vs watched `antiphon-server-bridge`; expected `Antiphon-Server-Bridge`; expected ` antiphon-server-bridge` (leading space); expected null with `RequireExpectedAntiphonConsumerGroup` true and monitor enabled — **green**.
- V-15: validation runs before any hosted loop, through both overloads (A6) | `GatewayMonitorValidationTests.Startup_fails_before_any_hosted_service_when_expected_group_mismatches` (a sentinel `IHostedService` registered before `AddAntiphonGateway`) and `.Configuration_overload_validates_the_same_rules` (`Kafka:` keys via `AddInMemoryCollection`) — **green**.
- V-16: disabled and no-store hosts need no broker and are visibly Disabled (A6) | `GatewayMonitorValidationTests.Disabled_monitor_host_starts_without_broker_and_reports_disabled` (monitor off, no store) and `.Enabled_monitor_without_store_is_disabled_not_ready` — **green**.
- V-17: state transitions and log cadence (A7) | `GatewayMonitorStatusTests.Absent_then_established_then_lag_then_caught_up_transitions` — **green**.
- V-18: cached Ready expires (A7) | `GatewayMonitorStatusTests.Cached_ready_expires_after_two_polls_plus_budget` (poll 60, budget 10) — **green**.
- V-19: fresh partition first commit (A7) | `GatewayMonitorStatusTests.New_partition_no_commit_then_first_commit` (p0 committed, p1 `NoCommit`, receipt on p1 offset 3) — **green**.
- V-20: inbox query failure degrades readiness (S3) | `GatewayMonitorStatusTests.Inbox_query_failure_is_degraded_not_ready` (store throws) — **green**.
- V-21: readiness mapping and payload allowlist (D-4) | `GatewayMonitorStatusTests.Snapshot_maps_to_http_status_and_allowlisted_payload` — **green**.
- V-22: standalone Service wiring (D-4, D-2) | `LibrarySufficiencyTests.Service_maps_monitor_readiness_and_requires_expected_group` and `dotnet build src/Antiphon.Messaging.Service/Antiphon.Messaging.Service.csproj --property:OutputPath=bin-c0410/` — **green**.
- V-23: existing monitor behaviours retained (A8) | existing `Overdue_unconsumed_acknowledges_once_and_publishes_one_event`, `Failed_send_retries_and_does_not_duplicate_a_successful_send`, `Committed_past_produces_neither_message_nor_event`, `Under_budget_age_produces_neither_message_nor_event`, `Health_outage_is_diagnostic_only_lag_still_decides` — **green**.
- V-24: legacy reader projection (S2) | `ConsumerLagAssessmentTests.Legacy_adapter_projects_only_committed_offsets` (adapter over a scripted observation) — **green**.
- V-25: repository profiles agree (A9) | `scripts/test-deploy-am-service.ps1` case `repository profiles agree on the server inbound group` — **green**.
- V-26: runtime identity endpoint (A9) | `ChannelConsumerIdentityEndpointTests.Returns_effective_overrides_and_only_allowlisted_fields` with overrides `AntiphonMessaging:ConsumerGroup=c0410-override-group`, `AntiphonMessaging:BootstrapServers=c0410-a.invalid:19092, kafka://c0410-b.invalid:19093,noport.invalid`, `ChannelBridge:Enabled` unset — **green**.
- V-27: identity DTO for the enabled bridge (A9) | `ChannelConsumerIdentityEndpointTests.Dto_reflects_enabled_flag_without_touching_kafka` — **green**.
- V-28: preflight is read-only and prints the four identities (A10) | new case `preflight reads server identity and gateway settings without writing` (fake `HttpRunner`: identity `antiphon-server-bridge`/`channels.inbound`/enabled true/brokers `am-redpanda:9092`; fake SSH: gateway projection watched `antiphon-consumer`, expected null, outbound `antiphon-messaging-service`) — **green**.
- V-29: preflight refusals (A10) | cases `identity endpoint 404 refuses`, `disabled bridge refuses`, `inbound topic mismatch refuses`, `identity change between preview and write refuses` (HttpRunner returns group A on the first call and group B on the re-read) — **green**.
- V-30: server override honoured, never a guessed fallback (A10) | case `server override is shown and used` (identity `family-bridge-custom`) — **green**.
- V-31: explicit deploy persists the managed override (A10) | case `deploy writes the managed override and verifies the merge before recreate` (success SSH fake returning base file `docker-compose.yml`, no existing override) — **green**.
- V-32: conflicting and marked overrides (A10) | cases `unmarked existing override refuses before any write` and `marked existing override is backed up and reported` — **green**.
- V-33: postdeploy verification reads the payload (A10) | cases over a fake `curl` matrix with `/api/channels` always OK: (a) 200 Ready, groups match, `ageSeconds` 12, all partitions `CommittedOffset` -> ok; (b) 503 Degraded -> fail; (c) 200 Ready but `watchedGroup` `antiphon-consumer` -> fail; (d) 200 Ready, `ageSeconds` 400 -> fail; (e) 200 Ready, one partition `NoCommit` -> fail; (f) route 404 -> fail; (g) merged `docker compose config` lacking `Kafka__ExpectedAntiphonConsumerGroup` -> fail; (h) `rpk group describe` output without numeric offsets -> fail — **green**.
- V-34: secrets and narrow output (A10) | case `identity, compose, and container output never leak` (seed `never-log-this` in the identity body extra field, compose config, and container env) — **green**.
- V-35: disposable broker mapping (A11) | `KafkaConsumerGroupObservationTests` (opt-in): (a) `Never_created_group_is_absent`; (b) `Joined_member_without_commit_is_present_no_commit` (consumer subscribed, auto-commit off, assignment awaited, kept open); (c) `Committed_then_left_is_present_with_offset` (commit next 2, `Close()`); (d) `Real_backlog_assesses_unconsumed_past_the_commit` (3 records, commit next 1); (e) `Caught_up_commit_assesses_consumed` (commit next 3); (f) `Two_partitions_commit_only_one` ; (g) `Absent_topic_is_unknown_evidence` — **green**.
- V-36: public API baseline and dependents compile | `dotnet build src/Antiphon.Messaging.Gateway/Antiphon.Messaging.Gateway.csproj --property:OutputPath=bin-c0410/` plus `EchoGatewaySampleTests` and `LibrarySufficiencyTests` — **green**.

## Positive controls: actual red assertion evidence and restored green

All **15 controls** completed break -> named failure -> restoration -> green. PC-1 also separately mutates the nullable compatibility wrapper (PC-1b), because changing Assess alone cannot exercise that independent wrapper. PC-8 has the two required variants (HTTP status mapping and status-only deploy verification). Thus there are 17 distinct mutation variants. All mutations were reverted; no mutant is committed.

Two harness details make the controls meaningful: the missing-partition receipt offset is 100 against another partition's 99, so a borrowing mutant actually sends; the stale-cache test adds a newer receipt at offset 12 after the previous commit 11, so cached evidence actually authorizes a false notice. The no-store test checks reader calls before checking Disabled state.

### PC-1

Red exit **2**; restored green exit **0**. Actual assertion text:

```text
failed Absent_group_with_overdue_receipt_sends_nothing_and_degrades_readiness (459ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: await h.TickAsync()
      should be
  0
      but was
  1
```

Rerun (restored source):
```powershell
dotnet run --project tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c0410/ -- --treenode-filter /*/*/InboundUnconsumedMonitorTests/Absent_group_with_overdue_receipt*
```

Raw red/green logs: `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-1-red.log` and `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-1-green.log`.

### PC-1b

Red exit **2**; restored green exit **0**. Actual assertion text:

```text
failed Legacy_IsUnconsumed_is_true_only_for_confirmed_lag (62ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: foreach (var commit in new long?[] { null, -1001, -1, 11 }) ConsumerLag.IsUnconsumed(commit, 10)
      should be
  False
      but was
  True
```

Rerun (restored source):
```powershell
dotnet run --project tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c0410/ -- --treenode-filter /*/*/ConsumerLagAssessmentTests/Legacy_IsUnconsumed*
```

Raw red/green logs: `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-1b-red.log` and `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-1b-green.log`.

### PC-2

Red exit **2**; restored green exit **0**. Actual assertion text:

```text
failed Validator_rejects(space, ExpectedAntiphonConsumerGroup) (183ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: result.Failed
      should be
  True
      but was
  False

failed Validator_rejects(case, ExpectedAntiphonConsumerGroup) (183ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: result.Failed
      should be
  True
      but was
  False

failed Validator_rejects(mismatch, ExpectedAntiphonConsumerGroup) (183ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: result.Failed
      should be
  True
      but was
  False

failed Startup_fails_before_any_hosted_service_when_expected_group_mismatches (534ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: Task `host.StartAsync()`
      should throw
  Microsoft.Extensions.Options.OptionsValidationException
      but did not

failed Configuration_overload_validates_the_same_rules (545ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: Task `host.StartAsync()`
      should throw
  Microsoft.Extensions.Options.OptionsValidationException
      but did not
```

Rerun (restored source):
```powershell
dotnet run --project tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c0410/ -- --treenode-filter /*/*/GatewayMonitorValidationTests/*
```

Raw red/green logs: `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-2-red.log` and `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-2-green.log`.

### PC-3

Red exit **1**; restored green exit **0**. Actual assertion text:

```text
FAIL deploy writes the managed override and verifies the merge before recreate - Merged docker compose config lacks matching watched/expected groups.

FAIL deploy writes the managed override and verifies the merge before recreate - Merged docker compose config lacks matching watched/expected groups.

FAIL deploy writes the managed override and verifies the merge before recreate - expected persistent override write, then merged config, then ordinary compose up

FAIL deploy writes the managed override and verifies the merge before recreate - Merged docker compose config lacks matching watched/expected groups.
```

Rerun (restored source):
```powershell
pwsh -NoProfile -File scripts/test-deploy-am-service.ps1
```

Raw red/green logs: `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-3-red.log` and `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-3-green.log`.

### PC-4

Red exit **2**; restored green exit **0**. Actual assertion text:

```text
failed Unknown_observation_gates_before_acknowledge_health_and_publish (152ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: h.Adapter.Sent
      should be empty but had
  1
      item and was
  [ChannelReply { Channel = slack, Kind = Progress, ReplyHandle = thread-1, ConversationId = C123, ReplyToMessageId = m-1, Text = [Antiphon] I received this message, but the service is unavailable. It is queued and will be processed when the service returns., Attachments = Antiphon.Messaging.OutboundAttachment[], RawOverrides =  }]
```

Rerun (restored source):
```powershell
dotnet run --project tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c0410/ -- --treenode-filter /*/*/InboundUnconsumedMonitorTests/Unknown_observation_gates*
```

Raw red/green logs: `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-4-red.log` and `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-4-green.log`.

### PC-5

Red exit **2**; restored green exit **0**. Actual assertion text:

```text
failed Receipt_partition_outside_snapshot_is_unknown (194ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: await h.TickAsync(); h.Adapter.Sent
      should be empty but had
  1
      item and was
  [ChannelReply { Channel = slack, Kind = Progress, ReplyHandle = r, ConversationId = c, ReplyToMessageId = missing, Text = [Antiphon] I received this message, but the service is unavailable. It is queued and will be processed when the service returns., Attachments = Antiphon.Messaging.OutboundAttachment[], RawOverrides =  }]
```

Rerun (restored source):
```powershell
dotnet run --project tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c0410/ -- --treenode-filter /*/*/InboundUnconsumedMonitorTests/Receipt_partition_outside*
```

Raw red/green logs: `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-5-red.log` and `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-5-green.log`.

### PC-6

Red exit **2**; restored green exit **0**. Actual assertion text:

```text
failed Empty_inbox_still_observes_the_group_and_updates_readiness (253ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: await h.TickAsync(); h.Offsets.Calls
      should have single item but had
  0
      items and was
  [] (System.Collections.Generic.List`1[System.ValueTuple`2[System.String,System.String]])
```

Rerun (restored source):
```powershell
dotnet run --project tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c0410/ -- --treenode-filter /*/*/InboundUnconsumedMonitorTests/Empty_inbox_still*
```

Raw red/green logs: `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-6-red.log` and `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-6-green.log`.

### PC-7

Red exit **2**; restored green exit **0**. Actual assertion text:

```text
failed Cached_ready_expires_after_two_polls_plus_budget (346ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: h.Clock.Advance(TimeSpan.FromSeconds(2)); h.Status.GetSnapshot().State
      should be
  MonitorState.Stale
      but was
  MonitorState.Ready
```

Rerun (restored source):
```powershell
dotnet run --project tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c0410/ -- --treenode-filter /*/*/GatewayMonitorStatusTests/Cached_ready_expires*
```

Raw red/green logs: `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-7-red.log` and `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-7-green.log`.

### PC-8

Red exit **2**; restored green exit **0**. Actual assertion text:

```text
failed Snapshot_maps_to_http_status_and_allowlisted_payload (398ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: snapshot.HttpStatusCode
      should be
  503
      but was
  200
```

Rerun (restored source):
```powershell
dotnet run --project tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c0410/ -- --treenode-filter /*/*/GatewayMonitorStatusTests/Snapshot_maps*
```

Raw red/green logs: `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-8-red.log` and `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-8-green.log`.

### PC-8b

Red exit **1**; restored green exit **0**. Actual assertion text:

```text
FAIL postdeploy verification refuses wrong-group - did not throw

FAIL postdeploy verification refuses stale - did not throw

FAIL postdeploy verification refuses no-commit - did not throw

FAIL postdeploy verification refuses no-numeric - did not throw
```

Rerun (restored source):
```powershell
pwsh -NoProfile -File scripts/test-deploy-am-service.ps1
```

Raw red/green logs: `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-8b-red.log` and `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-8b-green.log`.

### PC-9

Red exit **2**; restored green exit **0**. Actual assertion text:

```text
failed Cancellation_propagates_and_is_not_an_observation_failure (246ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: Task `Reader("127.0.0.1:1").ObserveAsync("c0410-cancel", "c0410-topic", cancel.Token)`
      should throw
  System.OperationCanceledException
      but did not
```

Rerun (restored source):
```powershell
dotnet run --project tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c0410/ -- --treenode-filter /*/*/KafkaConsumerGroupObservationTests/Cancellation_propagates*
```

Raw red/green logs: `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-9-red.log` and `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-9-green.log`.

### PC-10

Red exit **2**; restored green exit **0**. Actual assertion text:

```text
failed Joined_member_without_commit_is_present_no_commit (2s 267ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: await ObserveAsync(c); o.GroupStatus
      should be
  ConsumerGroupStatus.Present
      but was
  ConsumerGroupStatus.Absent
```

Rerun (restored source):
```powershell
dotnet run --project tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c0410/ -- --treenode-filter /*/*/KafkaConsumerGroupObservationTests/Joined_member_without_commit*
```

Raw red/green logs: `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-10-red.log` and `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-10-green.log`.

### PC-11

Red exit **2**; restored green exit **0**. Actual assertion text:

```text
failed Returns_effective_overrides_and_only_allowlisted_fields (7s 564ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: root.EnumerateObject().Select(p => p.Name).Order()
      should be
  ["brokers", "consumerGroup", "enabled", "inboundTopic"]
      but was (case sensitive comparison)
  ["brokers", "consumerGroup", "enabled", "inboundTopic", "outboundTopic"]
      difference
  ["brokers", "consumerGroup", "enabled", "inboundTopic", *"outboundTopic"*]
```

Rerun (restored source):
```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0410/ -- --treenode-filter /*/*/ChannelConsumerIdentityEndpointTests/Returns_effective*
```

Raw red/green logs: `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-11-red.log` and `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-11-green.log`.

### PC-12

Red exit **1**; restored green exit **0**. Actual assertion text:

```text
FAIL postdeploy verification refuses degraded - did not throw

FAIL postdeploy verification refuses wrong-group - did not throw

FAIL postdeploy verification refuses stale - did not throw

FAIL postdeploy verification refuses no-commit - did not throw

FAIL postdeploy verification refuses readiness-404 - did not throw

FAIL postdeploy verification refuses no-numeric - did not throw
```

Rerun (restored source):
```powershell
pwsh -NoProfile -File scripts/test-deploy-am-service.ps1
```

Raw red/green logs: `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-12-red.log` and `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-12-green.log`.

### PC-13

Red exit **1**; restored green exit **0**. Actual assertion text:

```text
FAIL repository profiles agree on the server inbound group - 
```

Rerun (restored source):
```powershell
pwsh -NoProfile -File scripts/test-deploy-am-service.ps1
```

Raw red/green logs: `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-13-red.log` and `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-13-green.log`.

### PC-14

Red exit **2**; restored green exit **0**. Actual assertion text:

```text
failed Unknown_after_success_does_not_reuse_the_previous_offset (198ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: h.Adapter.Sent
      should be empty but had
  1
      item and was
  [ChannelReply { Channel = slack, Kind = Progress, ReplyHandle = thread-1, ConversationId = C123, ReplyToMessageId = m-2, Text = [Antiphon] I received this message, but the service is unavailable. It is queued and will be processed when the service returns., Attachments = Antiphon.Messaging.OutboundAttachment[], RawOverrides =  }]
```

Rerun (restored source):
```powershell
dotnet run --project tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c0410/ -- --treenode-filter /*/*/InboundUnconsumedMonitorTests/Unknown_after_success*
```

Raw red/green logs: `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-14-red.log` and `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-14-green.log`.

### PC-15

Red exit **2**; restored green exit **0**. Actual assertion text:

```text
failed Enabled_monitor_without_store_is_disabled_not_ready (571ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: reader.Calls
      should be empty but had
  1
      item and was
  [(antiphon-consumer, channels.inbound)]
```

Rerun (restored source):
```powershell
dotnet run --project tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c0410/ -- --treenode-filter /*/*/GatewayMonitorValidationTests/Enabled_monitor_without_store*
```

Raw red/green logs: `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-15-red.log` and `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls\PC-15-green.log`.

## Commits

```text
44538895 fix(messaging): align gateway profile and expose consumer identity (S1)
e83806d2 fix(messaging): classify bounded broker observations distinctly (S2)
3a839254 fix(messaging): gate notices on confirmed lag and expose readiness (S3)
5100ed65 fix(deploy): persist and verify gateway consumer identity (S4)
fa5828c5 docs(messaging): document monitor evidence and coordinated deployment (S5)
c54212c8 fix(deploy): validate projections and preserve unrelated image defaults
aedb46fb test(messaging): verify disposable broker configuration and probe format
```

## Changed files

- `C:\Antiphon\worktrees\card-task-6ec07844\docs\antiphon-api.md`
- `C:\Antiphon\worktrees\card-task-6ec07844\docs\messaging-standalone.md`
- `C:\Antiphon\worktrees\card-task-6ec07844\docs\telegram-bot-ops.md`
- `C:\Antiphon\worktrees\card-task-6ec07844\docs\telegram.md`
- `C:\Antiphon\worktrees\card-task-6ec07844\scripts\deploy-am-service.ps1`
- `C:\Antiphon\worktrees\card-task-6ec07844\scripts\test-deploy-am-service.ps1`
- `C:\Antiphon\worktrees\card-task-6ec07844\server\Api\Endpoints\ChannelEndpoints.cs`
- `C:\Antiphon\worktrees\card-task-6ec07844\server\Application\Dtos\ChannelConsumerIdentityDto.cs`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Messaging.Gateway\AntiphonGatewayOptions.cs`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Messaging.Gateway\AntiphonGatewayOptionsValidator.cs`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Messaging.Gateway\ConsumerGroupObservation.cs`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Messaging.Gateway\ConsumerLag.cs`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Messaging.Gateway\IConsumerGroupOffsetReader.cs`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Messaging.Gateway\InboundUnconsumedMonitorService.cs`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Messaging.Gateway\InboundUnconsumedMonitorStatus.cs`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Messaging.Gateway\KafkaConsumerGroupObservationReader.cs`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Messaging.Gateway\KafkaConsumerGroupOffsetReader.cs`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Messaging.Gateway\PublicAPI.Unshipped.txt`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Messaging.Gateway\README.md`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Messaging.Gateway\ServiceCollectionExtensions.cs`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Messaging.Service\Program.cs`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Messaging.Service\README.md`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Messaging.Service\appsettings.json`
- `C:\Antiphon\worktrees\card-task-6ec07844\tests\Antiphon.Messaging.Tests\Antiphon.Messaging.Tests.csproj`
- `C:\Antiphon\worktrees\card-task-6ec07844\tests\Antiphon.Messaging.Tests\Gateway\CapturingLogger.cs`
- `C:\Antiphon\worktrees\card-task-6ec07844\tests\Antiphon.Messaging.Tests\Gateway\ConsumerLagAssessmentTests.cs`
- `C:\Antiphon\worktrees\card-task-6ec07844\tests\Antiphon.Messaging.Tests\Gateway\GatewayMonitorStatusTests.cs`
- `C:\Antiphon\worktrees\card-task-6ec07844\tests\Antiphon.Messaging.Tests\Gateway\GatewayMonitorValidationTests.cs`
- `C:\Antiphon\worktrees\card-task-6ec07844\tests\Antiphon.Messaging.Tests\Gateway\InboundUnconsumedEvidenceTests.cs`
- `C:\Antiphon\worktrees\card-task-6ec07844\tests\Antiphon.Messaging.Tests\Gateway\InboundUnconsumedMonitorTests.cs`
- `C:\Antiphon\worktrees\card-task-6ec07844\tests\Antiphon.Messaging.Tests\Gateway\KafkaConsumerGroupObservationTests.cs`
- `C:\Antiphon\worktrees\card-task-6ec07844\tests\Antiphon.Messaging.Tests\Gateway\LibrarySufficiencyTests.cs`
- `C:\Antiphon\worktrees\card-task-6ec07844\tests\Antiphon.Tests\Application\ChannelConsumerIdentityEndpointTests.cs`

## Rerun the verification

Run from `C:\Antiphon\worktrees\card-task-6ec07844`. Cleanup is pending, so existing binaries are currently present. Without those outputs, omit --no-build on the first command.

```powershell
dotnet run --project tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c0410/ -- --treenode-filter "/*/*/ConsumerLagAssessmentTests/*"
dotnet run --project tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c0410/ --no-build -- --treenode-filter "/*/*/InboundUnconsumedMonitorTests/*"
dotnet run --project tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c0410/ --no-build -- --treenode-filter "/*/*/GatewayMonitorValidationTests/*"
dotnet run --project tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c0410/ --no-build -- --treenode-filter "/*/*/GatewayMonitorStatusTests/*"
dotnet run --project tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c0410/ --no-build -- --treenode-filter "/*/*/GatewayTests/*"
dotnet run --project tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c0410/ --no-build -- --treenode-filter "/*/*/LibrarySufficiencyTests/*"
dotnet run --project tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c0410/ --no-build -- --treenode-filter "/*/*/EchoGatewaySampleTests/*"
$env:ANTIPHON_BROKER_TESTS='1'
dotnet run --project tests/Antiphon.Messaging.Tests --property:OutputPath=bin-c0410/ --no-build -- --treenode-filter "/*/*/KafkaConsumerGroupObservationTests/*"
Remove-Item Env:ANTIPHON_BROKER_TESTS
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0410/ -- --treenode-filter "/*/*/ChannelConsumerIdentityEndpointTests/*"
pwsh -NoProfile -File scripts/test-deploy-am-service.ps1
dotnet build src/Antiphon.Messaging.Gateway --property:OutputPath=bin-c0410/
dotnet build src/Antiphon.Messaging.Service --property:OutputPath=bin-c0410/
```

Log directory: `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\verification`.
Mutation driver retained locally: `C:\Antiphon\worktrees\card-task-6ec07844\.antiphon\positive-controls.py`. It restores source in a finally block and records assertion output and exit codes for each variant. Do not run build-producing commands concurrently with it.

## Remaining action and production boundary

**Required local cleanup is blocked.** Automatic approval review rejected both the guarded bulk recursive deletion and an explicit-literal-path deletion of the verified bin-c0410 directories. Its only stated reason was **blocked by policy**. No further deletion workaround was attempted. The output directories remain; an operator/caller with appropriate tooling must remove them. Source work and verification are complete, and the tracked worktree is otherwise clean.

Cleanup targets (all owned by this task under the supplied worktree; no process or shared-stack directory is targeted):

- `C:\Antiphon\worktrees\card-task-6ec07844\samples\EchoGateway\bin-c0410`
- `C:\Antiphon\worktrees\card-task-6ec07844\server\bin-c0410`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Agents.Pty\bin-c0410`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.FakeClaude\bin-c0410`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.FakeGrok\bin-c0410`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.FakeLlmApi\bin-c0410`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Messaging\bin-c0410`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Messaging.Client\bin-c0410`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Messaging.Client.Testing\bin-c0410`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Messaging.FakeGateway\bin-c0410`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Messaging.Gateway\bin-c0410`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Messaging.Service\bin-c0410`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Messaging.Slack\bin-c0410`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.Messaging.Telegram\bin-c0410`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.PtyHost\bin-c0410`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.PtyHost.Client\bin-c0410`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.PtyHost.Protocol\bin-c0410`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.SessionRunner\bin-c0410`
- `C:\Antiphon\worktrees\card-task-6ec07844\src\Antiphon.SessionRunner.Contracts\bin-c0410`
- `C:\Antiphon\worktrees\card-task-6ec07844\tests\Antiphon.Messaging.Tests\bin-c0410`
- `C:\Antiphon\worktrees\card-task-6ec07844\tests\Antiphon.Tests\bin-c0410`
- `C:\Antiphon\worktrees\card-task-6ec07844\tools\Antiphon.Messaging.SchemaGen\bin-c0410`

Next engineering stage: Review. Production server/gateway deployment and real-chat acceptance still require the separate operator authorization specified by the plan. This report grants neither. No product/default decision remains unresolved.
