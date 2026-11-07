# CARD-1746 decision: keep the messaging broker on server2

Date: 2026-11-07. Investigate task `e7d7d78b`. Documents only. No product code, no compose edit, no deploy, no Family message, no consumer join.

**Keep the broker that is already running on server2. That is option B, and it is production today.** The desktop Redpanda stays the fake-gateway broker. A managed cluster is not the bus. ChannelBridge on a Linux server turns on only after the current `antiphon-server-bridge` member has stopped.

## Plain English

- Telegram and Slack for the live gateway already run on server2, in `am-service`, against a Redpanda on that same machine.
- The Antiphon server's channel bridge is already a member of that same server2 broker. The desktop Redpanda is a second, separate broker, and the only thing using it right now is the fake gateway.
- The desktop broker tells every client to reconnect to `localhost`. A Linux server cannot use it by pointing at the desktop's Tailscale address.
- While the desktop is down, server2's gateway and broker keep running. Live replies stop only if the bridge member was the desktop server. Messages stay on the broker until that member returns.
- Putting the server on server2 (topology B) should use this broker. Adding another broker would split the stream or replay it.
- A cloud Kafka service would send family chat off the tailnet and would start a new offset history. That is a larger change than the placement needs.
- The staging server must not join this broker's consumer group. One partition means a second member takes the stream.
- Turn the Linux server's bridge on at cutover, after the current member is gone, with the broker address in an env file.

## Who talks to whom

| Piece | Where it runs | What it does on the bus |
|---|---|---|
| `am-service` | server2, compose project `/home/mc/antiphon-messaging`, container `am-service`, host port 18090 (`docs/telegram-bot-ops.md:60-64`, `scripts/deploy-am-service.ps1:237`) | Gateway. Produces `channels.inbound`. Consumes `channels.outbound` as `{ConsumerGroup}-outbound` with auto-commit (`GatewayOutboundService.cs:30-35`). Inbox group `{ConsumerGroup}-inbox`. Tracked group name `antiphon-messaging-service` (`src/Antiphon.Messaging.Service/appsettings.json:14-21`). |
| Channel bridge | Hosted only when `ChannelBridge:Enabled` is true (`server/Program.cs:878-880`). AppHost forces true (`Antiphon.AppHost/Program.cs:73`). Root `docker-compose.yml:48` and tracked `server/appsettings.json:391-392` set false. | Consumes `channels.inbound` as `antiphon-server-bridge` (`server/appsettings.json:420-424`). Commit is manual, after a durable disposition, reset Earliest when the group has no offset (`KafkaAntiphonMessagingConsumer.cs:9`, `:57-63`). Ops group `{ConsumerGroup}-ops-unconsumed` auto-commits (`InboundUnconsumedEventConsumer.cs:29-34`). |
| Fake gateway | AppHost Windows exe on `:17208` (`Antiphon.AppHost/Program.cs:41-52`). Dev and test only. | Group `antiphon-fake-gateway` (`src/Antiphon.Messaging.FakeGateway/Program.cs:57`), so outbound is `antiphon-fake-gateway-outbound`. AppHost does not forward a live broker override to it (`Antiphon.AppHost/Program.cs:59-60`). |
| Desktop Slack sidecar | Scheduled Task `Antiphon MikeysBot Slack Gateway`, recorded 2026-09-12 on local Redpanda (`docs/slack-bot-ops.md:124-130`). | Not a live group at the 2026-11-07 reading. |
| `school_revision` | A separate server2 instance in the 2026-07-21 spec (`docs/superpowers/specs/2026-07-21-telegram-bot-agents.md:34`). | Not a member of either broker's group list today. |

Topics, from `docs/messaging/contract/v1/CONTRACT.md:18-21`: `channels.inbound` (gateway to server), `channels.outbound` (server to gateway), `channels.ops.inbound-unconsumed` (gateway to server). One shared pair for every provider.

AppHost reads `AntiphonMessaging:BootstrapServers` from its own user-secrets or gitignored development overlay and forwards that value only to the server (`Antiphon.AppHost/Program.cs:54-85`). Tracked default is `localhost:19092`. Source must not contain a live hostname. That rule is already the code, from CARD-0185.

## What was measured on 2026-11-07

From the phone-home runner container (hostname `6f03ea7dd3f9`, default route `172.30.0.1`). TCP connect, three samples, 3 second timeout, no payload, 12:15:19Z. Then a Kafka metadata request with an empty topic list, and `DescribeGroups` / `ListGroups`. No produce, no join.

| Dial | Result |
|---|---|
| Desktop Tailscale `100.79.51.37:19092` | Open. Handshake min 0.95 ms, median 1.02 ms. Cluster `redpanda.b1eca39b-c9a7-45c1-bae3-34ad2ab05d17`. Advertised broker `localhost:19092`. |
| Desktop `100.79.51.37:17202` | Timeout. Same shape as 2026-10-21 (`docs/investigations/2026-10-21-card-1401-postgres-server2-and-snapshots.md:64`). |
| Desktop `100.79.51.37:17280` and `:443` | Open, median 1.17 ms and 1.40 ms. |
| server2 Tailscale `100.93.77.126:19092` | Open. Median 0.59 ms. Cluster `redpanda.66fe474a-d399-4ea1-8cde-c606e98614a8`. Advertised broker `100.93.77.126:19092`. |
| server2 LAN `192.168.4.34:19092` | Open. Median 0.44 ms. Same cluster id. Same advertised Tailscale address. |
| server2 `:9092` on both addresses | Connection refused. The in-compose listener is not published. |
| server2 `:18090` on both addresses | Open. Median 0.33 ms Tailscale, 0.36 ms LAN. `GET /health` returned `{"status":"ok"}`. |
| `127.0.0.1` ports 19092, 9092, 18090 | Connection refused. This container is not the host network namespace. |

`GET http://100.93.77.126:18090/health/inbound-unconsumed` at 12:18:45Z returned HTTP 200, state `Ready`, watched group `antiphon-server-bridge`, topic `channels.inbound`, partition 0 status `CommittedOffset`, committed next offset 510, observation age 31 seconds.

Metadata for the three channel topics, both clusters: 1 partition, 1 replica, leader 0, error 0. The server2 cluster id equals the fixture string in `scripts/test-deploy-am-service.ps1:25`.

| Cluster | Groups ListGroups returned | State |
|---|---|---|
| server2 `redpanda.66fe474a-…` | `antiphon-server-bridge`, `antiphon-server-bridge-ops-unconsumed` | Stable, one member each, client host `172.25.0.1`, client id `rdkafka` |
| server2 | `antiphon-messaging-service-outbound`, `antiphon-messaging-service-inbox` | Stable, one member each, client host `172.25.0.5` |
| desktop `redpanda.b1eca39b-…` | `antiphon-fake-gateway-outbound` | Stable, one member, client host `172.21.0.1` |

`172.25.0.5` matches a container on the am-service network. `172.25.0.1` is the bridge gateway address Docker's userland proxy presents for a client that is not on that network. The desktop API port did not answer, so this reading does not name the process. It is one off-network process in both server groups, which is the shape of the AppHost server with the bridge forced on. The desktop cluster has no live production group.

An unauthenticated metadata request succeeded on both external listeners. SASL is not required to read metadata there today. The 2026-08-23 `rpk` reading (`docs/superpowers/plans/2026-08-23-card-0150-public-messaging-contract-plan.md:156-168`) reported `enable_sasl=false`, no ACLs, `retention.ms=604800000`, and external advertise `100.93.77.126:19092`. Retention and the SASL config keys were not read again.

Desktop-origin handshakes were not remeasured. This task does not run on the desktop. The 2026-10-21 desktop-to-server2 port-22 median was 3.83 ms on LAN `192.168.4.34` and 6.72 ms on Tailscale `100.93.77.126` (`docs/superpowers/plans/2026-10-21-card-1401-postgres-to-server2-plan.md:26`).

## Options for the topology B server

Topology B puts the server, Postgres, and the phone-home runner on the server2 host daemon (`docs/docker-stack.md:910`). Gateway processes stay where they are. CARD-1742 already requires `ChannelBridge` false on the staging server.

### A. Broker stays on the desktop, Linux server connects over the tailnet

The desktop broker is reachable: `100.79.51.37:19092` answered in about 1 ms. Its metadata then says the broker is `localhost:19092`, which is the single listener in `docker-compose.dev.yml:64-65`. A comment on `:52-54` records that a second listener broke consumer-group coordination. A Linux server that bootstraps to the desktop address is sent back to its own localhost.

Latency of a working remote client would be the tailnet RTT after a listener change. On 2026-10-21 that desktop-to-server2 handshake was a few milliseconds; the reverse path that day was 72–127 ms to Postgres and a timeout to `:17202`. Today's reverse handshake to `:19092` was about 1 ms, so the old reverse-path number is not the current path.

When the desktop is down, this broker is down. `am-service` on server2 loses the bus even though the gateway process is up. Offsets live on the desktop volume `redpanda` (`docker-compose.dev.yml:69`), a different cluster from the one that holds commit 510.

The live broker address would have to be hardcoded or copied into the Linux server while AppHost's rule says a live hostname stays out of source. The smallest code change is a listener and advertise edit on the desktop dev broker. That edit fights the fake gateway, which is the one live member of that cluster and is supposed to stay local.

### B. Broker container on server2

This is the current production bus. `am-service` is already on it. The bridge member is already on it. The advertised address is already the server2 Tailscale IP, and both the Tailscale and LAN published ports returned that same cluster.

Latency for the gateway is in-compose (`172.25.0.5` to the broker). Latency for an off-network client is the published-port hop measured here at about 0.4–0.6 ms from the runner container. A future server container must open the advertised address `100.93.77.126:19092` itself; the runner's hairpin is not that proof.

When the desktop is down, the broker and `am-service` stay up. The bridge stops only while its member is the desktop. Inbound remains on the single replica from the last manual commit (next offset 510 at this reading) until retention drops it. The 2026-08-23 retention was 7 days. Earliest applies only when the group has no committed offset, so a returning member continues at 510 rather than replaying the log.

Offsets and the only replica are this cluster's data volume. The volume name was not listed. Recycle and a container recreate have to keep that volume. Nested dockerd and `dind-data` are the wrong place for it, same rule as CARD-1406's Postgres.

No new secret. The external listener still accepts plaintext metadata. The address goes in an untracked env file on the Linux server, the same custody as the server2 Postgres password. AppHost source stays free of the hostname. The fake gateway is not given this bootstrap.

Smallest compose change: no new Redpanda service. Root `docker-compose.yml` stays without a broker and with `ChannelBridge__Enabled` false. The production server's env file sets `AntiphonMessaging__BootstrapServers` to the advertised listener after the cluster id is checked.

### C. Managed or other

A hosted Kafka would give both sides a public bootstrap and a working advertise address. Latency becomes an internet round trip instead of the sub-millisecond in-compose hop. A desktop outage would not take the bus down.

The new cluster has none of today's offsets. Copying the log and starting at Earliest replays `channels.inbound` into agents, because the server resets Earliest and commits only after a durable disposition. Starting at the end drops whatever was not yet committed. Family message bodies would leave the tailnet. SASL credentials would be a new secret, and the AppHost rule still forbids putting that hostname in source. The compose change is smaller than a new local broker only if the env file is the whole change, and the custody change is larger than either local option.

## Recommendation

Option B, using the cluster that answered today. Do not stand up a second production broker for the Linux server. Do not retarget `am-service` at the desktop. Do not enable the bridge on the staging server.

Two follow-up cards, both parented to CARD-1746:

| Card | Importance / urgency | What it does | Gate |
|---|---|---|---|
| CARD-1751 | High / Soon, platform Linux | From the CARD-1742 staging container, metadata-only probe of `100.93.77.126:19092`. Pass means cluster `redpanda.66fe474a-d399-4ea1-8cde-c606e98614a8` and advertise `100.93.77.126:19092`. No produce, no join, bridge stays false. | Blocked by CARD-1742. |
| CARD-1752 | High / Normal, platform Linux | At cutover, set the Linux server's bootstrap from an untracked env file, re-check the cluster id, then set `ChannelBridge__Enabled` true. Digest stays false. Fake gateway stays on localhost. | Blocked by CARD-1744 and CARD-1751. |

CARD-1742 already says `ChannelBridge` false, scratch database, host daemon, and no second writer. CARD-1751 is the broker proof that card does not include. CARD-1744 stops the desktop server before the Linux server opens the production database; CARD-1752 uses that same window so `antiphon-server-bridge` has one member. One partition makes a second member a cut of the stream, not a copy.

## Not done, noted

- Desktop user-secrets were not read, and `:17202` did not answer, so the bridge member is identified by group shape rather than by process.
- `retention.ms` and the SASL config keys were not re-read. Plaintext metadata succeeded.
- The am-redpanda volume name was not listed. No SSH and no `docker inspect`.
- No listener change, no env file, no `ChannelBridge` edit.

## Uncertainties

- If the Stable member at `172.25.0.1` is not the desktop AppHost server, CARD-1752 has to stop that other process before it joins. CARD-1744's desktop stop would then be insufficient on its own.
- Slack sidecar and `school_revision` were absent from both group lists. A later start on the desktop broker would not reach the Linux server. A later start on this server2 cluster would.
- Staging-container hairpin to `100.93.77.126` can still fail when the runner's hairpin succeeded. CARD-1751 is that test.
