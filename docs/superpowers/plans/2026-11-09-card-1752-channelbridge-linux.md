# CARD-1752: Enable ChannelBridge on the Linux production server

Date: 2026-11-09. Plan task `7cf27fe8`. Base `584a169521a763d682da8313a9bc84f3c24583fb`.
Parent decision: `docs/investigations/2026-11-07-decide-card-1746-broker-placement.md` (option B).
Card: enable the production Linux server's ChannelBridge against the broker that is already live. No new Redpanda service, no move of the bus to the desktop, no managed cluster, no Family message.

Platform read at plan time: `GET /api/runner-defaults` returned `globalRunnerId=server2` and no kind overrides. `GET /api/session-runners` listed `desktop` (windows, accepting), `server2` (linux, accepting), and `server2-temp` (not accepting). Checkpoints below do not pin `-Runner` or `-Platform`. The smoke is a host deploy gate, not a runner canary, and it does not target `server2-temp`.

Impacted-classes table (CARD-1782): none. `tests/impacted-classes.json` has no row for these compose, deploy-script, or messaging-client paths. Code and Review run the checkpoint rows in this plan. Do not add a table row unless a later collision appears.

## Intended behaviour

One partition, one replica, one member of `antiphon-server-bridge`. A second member takes the stream. The Linux server joins only after the current member is gone, and only after a metadata read of the same cluster the staging probe was required to see.

### Before

```
Telegram/Slack -> am-service (server2, host :18090)
                    | produces channels.inbound
                    v
              server2 Redpanda  redpanda.66fe474a-d399-4ea1-8cde-c606e98614a8
              advertised 100.93.77.126:19092
                    ^
                    | consumes as antiphon-server-bridge (manual commit)
                    | and antiphon-server-bridge-ops-unconsumed (auto commit)
              desktop AppHost server (ChannelBridge forced on)
              measured 2026-11-07: one member, client id rdkafka, client host 172.25.0.1

desktop Redpanda (localhost:19092) -> fake gateway only
Linux production compose: ChannelBridge__Enabled defaults false, no bootstrap key
Linux staging compose: ChannelBridge__Enabled literal false
```

Inbound stays on the broker at the last manual commit while the member is absent. `am-service` keeps producing. The Linux server, with the flag false, does not subscribe.

### After

Same topics, same group names, same commit rules, same gateway, same desktop broker, same fake gateway. The only new steady state is:

```
CARD-1744 has stopped the desktop server
        -> antiphon-server-bridge has zero members
CARD-1751 has seen cluster redpanda.66fe474a-d399-4ea1-8cde-c606e98614a8
        from the staging container, and staging still has not joined
operator env file (mode 0600, outside the repo) sets
        CHANNEL_BRIDGE_ENABLED=true
        ANTIPHON_MESSAGING_BOOTSTRAP_SERVERS=<advertised host:port>
deploy script re-reads metadata from network antiphon-server2
        mismatch, local bootstrap, or a live member -> exit 2, no up
match and zero members -> up
Linux server subscribes as the only member
        client id antiphon-server2-server
        both antiphon-server-bridge and antiphon-server-bridge-ops-unconsumed
am-service GET /health/inbound-unconsumed stays Ready
```

`Digest__Enabled` stays the literal `false`. Rollback returns to the before picture: Linux flag false, group empty, then the desktop AppHost bridge is the only member. The broker volume is not deleted.

### Membership states

| State | `antiphon-server-bridge` members | Linux flag | What is allowed next |
|---|---|---|---|
| DesktopMember | 1, client id `rdkafka` | false | CARD-1744 stops the desktop server. This card does not join. |
| Empty | 0 | false | Enable probe. Inbound remains at the last commit. |
| Refused | 0 | true in the env file, but the process has not subscribed | Fix the env or the path. Do not start the desktop member on top. |
| LinuxMember | 1, client id `antiphon-server2-server` | true | Steady state. Monitor Ready. |
| RollbackEmpty | 0 | false again | Start the desktop AppHost bridge, then confirm one `rdkafka` member. |

A direct jump from DesktopMember to LinuxMember is a fault. Staging never has a state in this table.

### Files and lines

| Change | Where | What |
|---|---|---|
| Compose interpolation | `docker-compose.server2-server.yml` server environment, after the Digest line at `:99` | Three keys. Bootstrap and expected cluster interpolate from the env file and default empty. Client id is the literal `antiphon-server2-server`. No live host, no `localhost` substring (the server service test at `Server2ProductionServerContractTests.cs:64` forbids that substring). |
| Compose refusal needles | `scripts/deploy-server2-server.ps1` `Get-ServerComposeRefusal` around `:132-133` | Require the three new lines, the existing ChannelBridge default, and `Digest__Enabled: "false"`. |
| Enable gate | `scripts/deploy-server2-server.ps1` `Invoke-ServerDeploy` `:270-393`, before `Invoke-ServerUp` `:206` | Shape checks, then a host-only metadata probe. `-WhatIf` still calls no docker. |
| Empty bootstrap restore | `src/Antiphon.Messaging.Client/ServiceCollectionExtensions.cs` `:22-26` | Whitespace `BootstrapServers` becomes `localhost:19092` before any client is built. No socket. |
| Client id | `AntiphonMessagingOptions.cs` `:5-30`; `KafkaAntiphonMessagingConsumer.cs` `:57-66`; `InboundUnconsumedEventConsumer.cs` `:29-37` | Copy `ClientId` onto both consumer configs only when it is non-whitespace. Group names, topics, and commit flags stay. Producer `:16-23` stays without a client id. |
| Join permit | `server/Program.cs` `:885-889` next to the existing enabled check; `ChannelBridgeService.ConsumeInboundLoopAsync` `:117-123`; ops consumer before `Subscribe` at `:39-40` | Both consumers wait. A refusal skips `Subscribe` and skips `ConsumeDeliveriesAsync`. The API process stays up. `/health` stays process liveness. |
| Docs | `docs/docker-stack.md` matrix row `:1047`, environment keys `:1746-1748`, new subsection under the CARD-1744 production server section | Key names, the smoke gate, and rollback. |
| Unchanged public DTO | `ChannelConsumerIdentityDto.cs` `:9-15` and `ChannelEndpoints.cs` `:16-19` | Still the four allowlisted fields. Constructing it does not contact Kafka. |

### What stays unchanged

- `server/appsettings.json` `:394-428`: `ChannelBridge.Enabled` false, `BootstrapServers` `localhost:19092`, `ConsumerGroup` `antiphon-server-bridge`, `Digest.Enabled` false.
- `Antiphon.AppHost/Program.cs` `:41-85`: bridge forced on for the desktop server only; bootstrap forwarded only when the AppHost configuration has a value; fake gateway receives no broker override.
- Root `docker-compose.yml` `:48` and `docker-compose.dev.yml`: no broker added to the production file; dev Redpanda stays the fake-gateway broker.
- `docker-compose.server2-staging-server.yml` `:62-63`: literal `ChannelBridge__Enabled: "false"` and `Digest__Enabled: "false"`. No bootstrap key. Staging does not join.
- Topics `channels.inbound`, `channels.outbound`, `channels.ops.inbound-unconsumed`. Manual commit on the inbound group. Auto commit on the ops group. Earliest reset only when the group has no offset.
- `am-service`, its compose project, and the broker volume.
- CARD-0934 runner phases (`deploy-temp`, `drain-old`, `redeploy-old`, `drain-temp`, `retire-temp`). None of them set `CHANNEL_BRIDGE_ENABLED`.

### Must not happen

- No fake-gateway traffic to the live broker. Tests use `broker.invalid` names and fixture JSON.
- No live Telegram or Slack send, and no Family message, from tests or from the enable script.
- No produce, and no consumer-group join, inside the probe or the tests. `rpk cluster info` and `rpk group describe` are read-only.
- No live hostname in AppHost, compose, or source. The cluster id constant is an identity, not a route.
- No second member. No staging member. No new Redpanda service. No broker-volume delete.
- The script never prints `POSTGRES_PASSWORD`, `PHONE_HOME_SHARED_SECRET`, the bootstrap value, or the env file body.

## Ground truth

| The card assumes | What the code does today | This plan |
|---|---|---|
| Put `AntiphonMessaging__BootstrapServers` in an untracked env file | Compose interpolates `CHANNEL_BRIDGE_ENABLED` (`docker-compose.server2-server.yml:98`) and does not pass a bootstrap. Appsettings `localhost:19092` applies inside the image. `--env-file` only fills compose interpolations (`deploy-server2-server.ps1:215`). | Add an empty-default interpolation. The env file supplies the value only for enable. Whitespace restores the tracked local default in process, so a flag-off container does not keep an empty override. |
| Re-read the cluster id at enable time and refuse on a mismatch | No server or deploy-script cluster check. `deploy-am-service.ps1` `Assert-AmServiceBrokerIdentity` `:233-248` already compares `cluster_name`. The fixture id is `scripts/test-deploy-am-service.ps1:25`. | Same literal in the server deploy script. Probe from network `antiphon-server2`. Mismatch exits 2 before `up`. The hosted services also skip subscribe. |
| Do not hard-code the host | AppHost guard is `AppHostBrokerSourceGuardTests`. Production server service must not contain `localhost` (`Server2ProductionServerContractTests.cs:64`). | No Tailscale address in source. Bootstrap default in compose is empty, not `localhost:19092`. |
| Exactly one member, and it is this server | DescribeGroups client host `172.25.0.1` is the userland-proxy shape for any off-network client, including a container that dials the published port. Client id today is `rdkafka`. | Discriminator is client id `antiphon-server2-server`, set only on this compose. Client host is recorded and not matched to an address. |
| `am-service` `/health/inbound-unconsumed` stays Ready | Gateway route already exists. Watched group is `antiphon-server-bridge`. | Read-only verify after up. Not a send. |
| After CARD-1744 and CARD-1751 | Card is blocked by CARD-1744 (Review) and CARD-1751 (InProgress). | Code keeps the default false and can land before the window. The operator enable waits for both gates. |
| Digest stays false. Fake gateway stays on localhost | Both are already true in compose and AppHost. | Pins. No edits to those lines except to keep them. |
| Rolling-rollout smoke | CARD-0934 smoke is a runner Plan canary. It does not join a consumer group. `server2-temp` was not accepting at the plan-time read. | A named gate on `deploy-server2-server.ps1`, written in the same stop/rollback shape as the staged rollout table. It is not a new CARD-0934 phase. |

## Decisions

### D-1. Bootstrap is an env-file interpolation with an empty default

The production server service gains:

```yaml
AntiphonMessaging__BootstrapServers: "${ANTIPHON_MESSAGING_BOOTSTRAP_SERVERS:-}"
AntiphonMessaging__ClientId: "antiphon-server2-server"
AntiphonMessaging__ExpectedClusterId: "${ANTIPHON_MESSAGING_EXPECTED_CLUSTER_ID:-}"
```

`AddAntiphonMessagingCore` post-configures a whitespace bootstrap back to `localhost:19092` and does not open a socket. A non-empty value is kept. The env file is the existing untracked mode-0600 file outside the repo (`deploy-server2-server.ps1:12-13`). The script does not write that file.

Rejected: a tracked `localhost:19092` default inside the server service, because `server.ShouldNotContain("localhost")` would fail and a true flag without a bootstrap would dial the container's own loopback. Rejected: hard-coding the Tailscale address. Rejected: joining the messaging compose network and tracking `am-redpanda:9092`. Kafka still returns the advertised listener, and the decision document requires the server container to open that advertised address itself. Rejected: a new Redpanda service.

### D-2. The expected cluster id is the am-service fixture, not an operator string

The script constant is `redpanda.66fe474a-d399-4ea1-8cde-c606e98614a8`, the same characters as `scripts/test-deploy-am-service.ps1:25`. Enable requires a metadata `cluster_name` equal to that constant and an advertised listener equal to the env bootstrap. If the env file sets `ANTIPHON_MESSAGING_EXPECTED_CLUSTER_ID` to anything else, the script exits 2 `cluster-pin-mismatch` before a probe. After a passing probe the script exports the constant into the compose process so the container receives it. The operator cannot point this server at the desktop cluster `redpanda.b1eca39b-c9a7-45c1-bae3-34ad2ab05d17` by typing a different expected id.

Rejected: an operator-supplied expected id with no script pin.

### D-3. This server proves itself with client id, not client host

`AntiphonMessagingOptions.ClientId` defaults empty. Both consumer builders set `ConsumerConfig.ClientId` only when the option is non-whitespace. The production compose sets `antiphon-server2-server`. The desktop AppHost does not, so its member stays `rdkafka`. Verify requires exactly one member of each group and that client id. `client_host` is logged by the operator on the host and is not a source constant.

Rejected: treating `172.25.0.1` as proof of the desktop or of this container. Rejected: a new consumer group. The group name stays `antiphon-server-bridge`.

### D-4. Refusal does not join and does not stop the API

`ChannelBridgeBrokerAdmission.Evaluate` is pure. Inputs are the effective bootstrap, the expected cluster id, the probe snapshot (cluster id, advertised listeners, member counts, member client ids), and `ChannelBridge:Enabled`. `Join` only when enabled, the bootstrap is a non-local host:port, the cluster id matches, every advertised listener equals that bootstrap, both groups have zero members at enable (or, after up, one member with this client id for verify), and the expected id is the pin.

Before `up`, any member is `bridge-member-present`. The hosted permit uses the same function. On `Refuse`, `ChannelBridgeService` does not call `ConsumeDeliveriesAsync`, and `InboundUnconsumedEventConsumer` does not call `Subscribe`. The process stays up so CARD-1744's API keeps serving. `/health` is unchanged. `-VerifyChannelBridge` is what fails the window.

`CHANNEL_BRIDGE_ENABLED` must be exactly `true` or exactly `false` or absent. Any other value is `bridge-flag-invalid` before `up`, because the ASP.NET binder would otherwise treat `True` or `1` as enabled and skip a gate that only looked for `true`.

Rejected: failing the process on mismatch. Rejected: extending `ChannelConsumerIdentityDto`. That DTO is an allowlist and must not contact Kafka. Existing test `ChannelConsumerIdentityEndpointTests.Returns_effective_overrides_and_only_allowlisted_fields` keeps the four field names.

### D-5. The smoke is a production-server gate in staged-rollout shape

It is not a CARD-0934 phase. Runner drain, temp retirement, and volume recycle do not apply. Do not call `scripts/deploy-server2.ps1` from this card. If a runner rollout is in progress, leave it alone; this gate does not clear or set a drain.

| Gate | Command | Stop |
|---|---|---|
| 0. Preconditions | CARD-1744 desktop server is stopped. CARD-1751 recorded the staging container's metadata match for the pinned cluster id. Staging compose still has literal bridge false. | Either predecessor missing: do not set the flag. |
| 1. Plan | `pwsh -NoProfile -File scripts/deploy-server2-server.ps1 -WhatIf -EnvFile <untracked>` | Bad flag, local bootstrap, or pin mismatch exits 2. No docker. Stdout has no secret and no bootstrap. |
| 2. Probe before up | `-Execute` on the server2 host. The script runs `rpk cluster info` and `rpk group describe` for both groups with `--network antiphon-server2`, using the image of the broker container that is already running. No pull. | `cluster-mismatch`, `broker-advertise-mismatch`, `bridge-member-present`, or `broker-probe-unavailable` exits 2 before `up`. Unparseable output is unavailable, not a pass. |
| 3. Up | Existing `compose --env-file ... up -d --build`, then the existing verify-only restart gate. | Existing refusals (`not-server2-host`, revision, certificate) unchanged. |
| 4. Smoke | `pwsh -NoProfile -File scripts/deploy-server2-server.ps1 -VerifyChannelBridge -EnvFile <untracked>` | Requires one member on each group, client id `antiphon-server2-server`, and HTTP 200 `Ready` from `http://127.0.0.1:18090/health/inbound-unconsumed` with watched group `antiphon-server-bridge`. No produce. |
| 5. Rollback | Set `CHANNEL_BRIDGE_ENABLED=false` (or remove it). `-Execute`. Confirm both groups have zero members. Then start the desktop AppHost bridge and confirm it is the only member. | Do not start the desktop member while the Linux member exists. Do not delete the broker volume. Do not run `restart-apphost.ps1` while the Linux server is still a database writer; CARD-1744 R-1..R-3 still own that order. |

`-VerifyChannelBridge` is exclusive of `-Execute` and `-WhatIf` (`ambiguous-mode`, exit 3). Off the host it returns `not-server2-host` before docker and before HTTP.

Rejected: a runner Plan canary as the membership proof. Rejected: enabling the bridge on the staging compose or on `server2-temp`.

### D-6. Code proceeds with the default false

The operator enable is blocked by CARD-1744 and CARD-1751. The code, tests, and docs are not. Defaults stay false, so a deploy that does not set the flag does not join. Stated defaults are D-1 through D-5. No product hostname is introduced. Next stage is code, starting at S1.

## Verification design

Witnesses are red first: each new test fails on the current tree because the compose line, the option, or the script reason is absent. The production fix lands in the same slice after that red commit. A test that only compares a constant to itself is not a witness.

No test opens port 19092 or 18090, starts a container, or sends a channel message. Probe results are fixture JSON. Bootstrap values in fixtures use `broker.invalid:19092`.

### Witnesses

| Id | Test | Red until |
|---|---|---|
| V-1 | `Production_bootstrap_interpolation_has_no_live_host` | The three compose lines exist, the server service text has no `localhost`, `100.93.77.126`, or `100.79.51.37`, and root `docker-compose.yml` still has `ChannelBridge__Enabled: "false"` and no `ANTIPHON_MESSAGING_BOOTSTRAP_SERVERS`. |
| V-2 | `Production_client_id_is_antiphon_server2_server` | Client id literal is exactly `antiphon-server2-server`. Digest line is still `"false"`. ChannelBridge line is still `${CHANNEL_BRIDGE_ENABLED:-false}`. |
| V-3 | `Staging_bridge_stays_off_without_a_bootstrap_key` | Staging file has `ChannelBridge__Enabled: "false"`, no `CHANNEL_BRIDGE_ENABLED`, no `ANTIPHON_MESSAGING_`. |
| V-4 | `AppHostBrokerSourceGuardTests` (existing, re-run) | AppHost bootstrap argument stays an identifier. Tracked default stays `localhost:19092`. |
| V-5 | `Absent_client_id_omits_the_config_value`, `Client_id_is_copied_to_both_group_configs`, `Inbound_stays_manual_commit_and_ops_stays_auto_commit` | `ClientId` is unset unless the option is non-whitespace. Inbound `EnableAutoCommit` false. Ops group `{ConsumerGroup}-ops-unconsumed` and auto commit true. |
| V-6 | `Whitespace_bootstrap_restores_localhost_19092` | Empty bootstrap becomes `localhost:19092`. A non-empty value is unchanged. No socket type is constructed. |
| V-7 | Admission methods listed under CP-2 | Mismatch, local, loopback, empty, missing pin, and a present member refuse. A matching snapshot with zero members joins. Disabled does not join. |
| V-8 | `Permit_mismatch_does_not_signal_join` and `Permit_is_awaited_before_subscribe` | A fake probe that returns a mismatch leaves the join counter at 0. Source order: the permit wait appears before `ConsumeDeliveriesAsync` in `ChannelBridgeService.cs` and before `Subscribe` in `InboundUnconsumedEventConsumer.cs`. |
| V-9 | CP-3 script methods | Missing, local, and invalid bootstraps, and a bad flag, exit 2 before docker. A valid shape off-host is `not-server2-host` and does not call docker. Stdout omits the fixture password and the bootstrap. The script and `test-deploy-am-service.ps1` contain the same cluster literal. |
| V-10 | CP-4 probe methods | Fixture JSON for the pinned id and an empty group is admitted. The desktop cluster id, a pre-existing member, and client id `rdkafka` are refused. The parser has no produce and no subscribe call. |
| V-11 | `Production_doc_names_bridge_smoke_and_rollback` and the existing `Production_server_on_the_server2_host_is_documented` | The production-server section names the env keys, `-VerifyChannelBridge`, rollback flag false, and "do not delete the broker volume". `CHANNEL_BRIDGE_ENABLED` remains in that section. |

`ChannelConsumerIdentityEndpointTests` is not edited and is not in the table. A diff that changes `ChannelConsumerIdentityDto` fails this plan.

### Mutants

One method-scoped positive control per witness, for the later mutation pass. Ordinary green does not discharge these.

| PC | Mutant | Witness that goes red |
|---|---|---|
| PC-1 | Delete the whitespace bootstrap restore | V-6 |
| PC-2 | Compare cluster id with ordinal ignore-case, or accept any id | V-7 `Cluster_mismatch_refuses` |
| PC-3 | Treat a non-zero member count as joinable | V-7 `Member_already_present_refuses` |
| PC-4 | Always set `ClientId`, including when empty | V-5 `Absent_client_id_omits_the_config_value` |
| PC-5 | Drop the client id assignment | V-5 `Client_id_is_copied_to_both_group_configs` |
| PC-6 | Flip inbound auto-commit to true | V-5 `Inbound_stays_manual_commit_and_ops_stays_auto_commit` |
| PC-7 | Call `Subscribe` before the permit wait | V-8 source-order method |
| PC-8 | Skip the local-bootstrap script refusal | V-9 `Bridge_local_bootstrap_refuses` |
| PC-9 | Echo the env bootstrap on the refusal path | V-9 `Bridge_gate_omits_secret_text` |
| PC-10 | Accept the desktop cluster fixture as the pin | V-10 `Probe_desktop_cluster_is_refused` |
| PC-11 | Add a bootstrap key to the staging compose | V-3 |

### Doc-contract pins

S4 adds these sentences to `docs/docker-stack.md` under `## Production server on the server2 host (CARD-1744)`, and adds the same pins to `DockerStackDocumentationTests.Production_server_on_the_server2_host_is_documented`:

- `ANTIPHON_MESSAGING_BOOTSTRAP_SERVERS`
- `ANTIPHON_MESSAGING_EXPECTED_CLUSTER_ID`
- `AntiphonMessaging__ClientId`
- `-VerifyChannelBridge`
- `antiphon-server2-server` as the bridge client id
- `do not delete the broker volume`

The matrix row at `:1047` is updated so the Linux production server enables only through that gate, staging stays off, and the owning card is CARD-1752. No test currently pins the old CARD-1746 cell text. The production-server section test does pin `CHANNEL_BRIDGE_ENABLED`, which stays.

### V/R rows

| Row | Requires |
|---|---|
| R-1 | No test and no script path opens a broker socket or the gateway HTTP port. Fixture host is `broker.invalid`. |
| R-2 | No test sends a channel message or mentions a Family send as a step this card runs. |
| R-3 | `AppHostBrokerSourceGuardTests` still passes (V-4). |
| R-4 | `ChannelConsumerIdentityDto` property list stays `consumerGroup`, `inboundTopic`, `enabled`, `brokers`. |
| R-5 | Production Digest literal false. Staging bridge literal false. Root compose bridge literal false. |
| R-6 | Script and docs contain no broker-volume delete. `Get-ServerComposeRefusal` still refuses `antiphon_pgdata` and `dind-data`. |
| R-7 | Windows lane: nothing owed. The desktop rollback is an operator action in gate 5, not a test run. `EstimatedMinutesWindows` is omitted. NativeLinuxOnly deploy witnesses run on the Linux lane. |

### Checkpoints

Lane: Linux. No `-Runner`, no `-Platform`. `repeat-proof`: one execution per row; no repeat.

totalTimeoutMinutes: 62

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1752-s1/ | s1-contract | `/*/Antiphon.Tests.Infrastructure/(Server2ProductionServerContractTests*)\|(Server2StagingServerContractTests*)\|(AppHostBrokerSourceGuardTests*)/*` | V-1, V-2, V-3, V-4, R-3, R-5 | all listed, 0 failed | 27 | 8 |
| CP-2 | S2 | tests/Antiphon.Tests -> bin-c1752-s2/ | s2-admission | `/*/Antiphon.Tests.Infrastructure/(ChannelBridgeBrokerAdmissionTests*)\|(ChannelBridgeConsumerConfigTests*)/*` | V-5, V-6, V-7, V-8, R-1, R-4 | all listed, 0 failed | 15 | 4 |
| CP-3 | S3 | tests/Antiphon.Tests -> bin-c1752-s3/ | s3-gate | `/*/*/Server2ProductionServerContractTests/(Bridge_missing_bootstrap_refuses*)\|(Bridge_local_bootstrap_refuses*)\|(Bridge_invalid_bootstrap_refuses*)\|(Bridge_bad_flag_refuses*)\|(Bridge_off_host_valid_shape_does_not_probe*)\|(Bridge_gate_omits_secret_text*)\|(Bridge_cluster_pin_matches_am_service_fixture*)` | V-9, R-1, R-2, R-6 | all listed, 0 failed | 7 | 6 |
| CP-4 | S4 | tests/Antiphon.Tests -> bin-c1752-s4/ | s4-verify | `/*/*/Server2ProductionServerContractTests/(Probe_match_and_empty_group_is_admitted*)\|(Probe_desktop_cluster_is_refused*)\|(Probe_existing_member_is_refused*)\|(Probe_wrong_client_id_is_refused*)\|(Verify_off_host_refuses_before_docker*)\|(Production_doc_names_bridge_smoke_and_rollback*)` | V-10, V-11, R-1, R-2, R-6 | all listed, 0 failed | 6 | 6 |
| CP-5 | S4 | CP-4 | s4-doc | `/*/*/DockerStackDocumentationTests/Production_server_on_the_server2_host_is_documented` | V-11 | all listed, 0 failed | 1 | 2 |

CP-1 floor is the roster at S1's green commit: 13 existing `Server2ProductionServerContractTests` methods, plus `Production_bootstrap_interpolation_has_no_live_host` and `Production_client_id_is_antiphon_server2_server` (15); 9 existing `Server2StagingServerContractTests` methods plus `Staging_bridge_stays_off_without_a_bootstrap_key` (10); 2 existing `AppHostBrokerSourceGuardTests` methods. No `[Arguments]` on those classes at base `584a169521a763d682da8313a9bc84f3c24583fb`. Code re-counts `[Test]` results before the green row and corrects Min in that slice if the count differs.

CP-2 floor is 11 admission methods plus 4 config methods:

- `Match_with_zero_members_joins`
- `Cluster_mismatch_refuses`
- `Empty_bootstrap_refuses`
- `Localhost_bootstrap_refuses`
- `Loopback_bootstrap_refuses`
- `Member_already_present_refuses`
- `Advertise_mismatch_refuses`
- `Missing_expected_cluster_refuses`
- `Disabled_does_not_join`
- `Permit_mismatch_does_not_signal_join`
- `Permit_is_awaited_before_subscribe`
- `Absent_client_id_omits_the_config_value`
- `Client_id_is_copied_to_both_group_configs`
- `Inbound_stays_manual_commit_and_ops_stays_auto_commit`
- `Whitespace_bootstrap_restores_localhost_19092`

That is 11 + 4 = 15. Do not filter `ChannelBridgeTests*`; that class is the existing bridge suite and is not this card.

CP-3 operands are seven methods. None is a prefix of another. CP-4 operands are six methods. CP-5 is one exact method.

Suffix check at plan time: no class begins with `Server2ProductionServerContractTests`, `Server2StagingServerContractTests`, `AppHostBrokerSourceGuardTests`, `ChannelBridgeBrokerAdmissionTests`, or `ChannelBridgeConsumerConfigTests` except the three existing classes themselves. `ChannelBridgeTests` does not match `ChannelBridgeBrokerAdmissionTests*`.

### Cost

Ordinary Code floor is the sum of `EstimatedMinutes`: 8+4+6+6+2 = 26 minutes, plus authoring. `totalTimeoutMinutes: 62` is `max(30, 2*26+10)`. Checkpoints: `docs/superpowers/plans/2026-11-09-card-1752-channelbridge-linux.md@<plan commit sha>` section "### Checkpoints".

## Performance budget

Standards: `docs/test-performance-standards.md` R1-R6 and `docs/testing-and-build.md` performance section. Durations below are provisional. This plan did not run the suites, so there is no measured p95. Code replaces a provisional estimate only with a measured p95 from the checkpoint TRX, and sets the row timeout near 2x that p95. Do not widen a timeout to turn red into green.

| Row | What time is | Provisional estimate | About 2x | Importer row timeout `max(15, 3*estimate)` |
|---|---|---|---|---|
| CP-1 | One `Antiphon.Tests` build plus the existing production deploy witnesses, which spawn pwsh, plus file reads | 8 min | 16 min | 24 min |
| CP-2 | Same project, second build, pure decisions and config objects | 4 min | 8 min | 15 min |
| CP-3 | Build plus seven short pwsh processes | 6 min | 12 min | 18 min |
| CP-4 | Build plus six short pwsh or file witnesses | 6 min | 12 min | 18 min |
| CP-5 | Reused build, one documentation method | 2 min | 4 min | 15 min |

No row's `EstimatedMinutes` exceeds 15, so none crosses the 45-minute importer advisory. The total execution allowance is 62 minutes. That is above one 30-minute selection only as a sum of separately filtered rows. A single row that actually exceeds 30 minutes is an R1 Debug, not a retry.

Phases. Build and slot wait are the checkpoint tool's build phase, recorded apart from test execution (CARD-1531). Test body is file read or one pwsh process. There is no container start, no readiness wait, no migration, no database snapshot, and no web host. External calls in these rows are filesystem and process only. HTTP to the gateway and CLI to rpk exist on the host enable path and are absent from the tests; test-phase HTTP is zero because it is not invoked, and the report says so rather than inventing a measurement. Unattributed time inside pwsh stays unknown on the provisional budget.

Shared setup. None of these witnesses start AppHost, Postgres, Redpanda, or the production server. The repo tree is the fixture. Each script witness follows the existing `RunDeployAsync` pattern: one process, a fake `docker` on `PATH`, a mode-0600 env file outside the repo, and a marker file that must stay absent when docker is forbidden. They do not share a database. Sharing one web host would test a different subject.

R2. The new decision type is a pure function with no host. The script gate stays inside the existing deploy script so the refusal reasons cannot drift from `Get-ServerComposeRefusal`.

R6. The host probe's rpk and curl calls get a 15-second deadline each, matching the am-service monitor read. Tests do not make those calls.

## Migrations

None. Master's latest migration on this base is `20261108121426_Card1773TranscriptPromptStampIndex`. No model change, no new migration, no `Down`.

Public DTOs and routes: none. Operation labels: none. Statement budgets: none. Remote script surface: `scripts/deploy-server2-server.ps1` gains reason codes and `-VerifyChannelBridge`. `scripts/deploy-am-service.ps1` is not edited. The new test only reads its existing cluster literal.

## Slices

Each slice is one commit of the failing tests, then the fix in a following commit if the red run is real, then the checkpoint row. Authoring fits 60-90 minutes. The operator enable is not a slice.

### S1. Compose contract

Edit `docker-compose.server2-server.yml` and the compose-refusal needles. Add V-1, V-2, and V-3. Do not enable the flag. Run CP-1.

### S2. Client id, bootstrap restore, admission

Add the option fields, the post-configure restore, the consumer config helper, `ChannelBridgeBrokerAdmission`, and the join permit. Wire the two consumers. Add the CP-2 tests. No broker. Run CP-2.

### S3. Deploy-script gate before up

Shape checks and the cluster-pin literal. `-WhatIf` and off-host `-Execute` do not probe. Live rpk runs only after the host-lane check, and only when the flag is exactly `true`. Add the CP-3 tests. Run CP-3.

### S4. Verify gate and docs

`-VerifyChannelBridge`, fixture parser tests, `docs/docker-stack.md` gate table and matrix row, documentation pins. Run CP-4 and CP-5.

## Risks and operator-only steps

Operator-only. Nothing in code runs on the desktop, and nothing here deploys.

1. Confirm CARD-1744 has stopped the desktop server and `antiphon-server-bridge` has zero members. If the member at `172.25.0.1` is not that AppHost process, stopping the desktop is not enough. Gate 2's `bridge-member-present` refusal is the stop. The operator finds and stops the other process. This card does not guess it.
2. Confirm CARD-1751 passed from the staging container. Staging hairpin is not the production server's network namespace. Gate 2 repeats the read on network `antiphon-server2`.
3. Write `ANTIPHON_MESSAGING_BOOTSTRAP_SERVERS` into the existing untracked mode-0600 env file outside the repo. Custody matches `docs/agent-credentials.md` for that env file: mode 0600, not in git, not printed. The value is a host:port, not a new secret. Do not put it in AppHost user-secrets for this server. Do not print the file.
4. If the container cannot open the advertised listener, the failure is `broker-probe-unavailable` or `broker-advertise-mismatch`. The fix is host routing, not a source hostname and not a broker advertise change. DNS and the public vhost stay on CARD-1744. This card does not edit Cloudflare.
5. Run gates 1-4 on the server2 host. This worktree must not.
6. Rollback gate 5. Do not delete the broker volume (`am-redpanda` data volume; the 2026-11-07 decision did not list its name, and this card does not need the name in order to avoid deleting it).
7. `rpk group describe --format json` may be unavailable on the installed rpk. That is `broker-probe-unavailable`. Do not fall through to a join. An operator upgrade of the already-local rpk is the remedy, not a new image pull from this card.
8. A raw `docker compose up` that bypasses the script, with the flag true and an empty expected id, hits the in-process permit and does not subscribe. The API stays up and the smoke fails. Use the script.

Windows. Nothing is owed on the native Windows lane. Rollback's desktop AppHost start is the operator's CARD-1744 R-1 path after the Linux member has left. No Windows checkpoint, no Windows estimate.

## Session release

This plan does not add or change a session that waits for input. The CARD-1083 parking checklist does not apply.
