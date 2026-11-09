# 2026-11-09 am-service deploy on server2 (CARD-1799)

Task 75432ebf. Operator authority 2026-11-08: "Once fixed deploy to am and recut/restart host
whatever is needed".

## Outcome

am-service (the Telegram/Slack gateway, compose project `/home/mc/antiphon-messaging`) was
rebuilt and recreated on server2 from commit `653f1145de5b72c2617a018cd606e0e8ba3cfe6e`
(rc/2026-11-13, includes the CARD-1799 commits `3b0ae86a0` and `bbf43863d`). It started cleanly,
`scripts/deploy-am-service.ps1` reported `REMOTE DEPLOY VERDICT: ok`, and no test message was
sent to any chat.

## Procedure

- Used `scripts/deploy-am-service.ps1 -SshTarget mc@server2`: first the read-only preflight, then
  `-Deploy -Confirm:$false -TimeoutSec 300`. The script runs from the task worktree. The worktree
  was clean at `653f1145`, and `git status --ignored` showed no untracked or ignored files under
  the messaging paths, so the archive held exactly the commit's tracked files (with
  `core.autocrlf=true` line endings).
- Manifest, taken from the Dockerfile COPY lines. The remote and local Dockerfiles had the same
  SHA-256, `3ee16542ae3eec012e53921c3efb9875c344546f9101947722190d0f300cdfc3`:
  `Messaging.Pack.props, Antiphon.Messaging, Antiphon.Messaging.Gateway,
  Antiphon.Messaging.Telegram, Antiphon.Messaging.Slack, Antiphon.Messaging.Service`.
  The archive had 76 paths, with bin/obj/bin-* excluded.
- The script staged the upload, moved the old `build/src` to a backup, then ran
  `docker compose build messaging-service` and `docker compose up -d --no-deps messaging-service`.
- Preflight identity checks: server/gateway consumer group `antiphon-server-bridge`, outbound
  group `antiphon-messaging-service` (unchanged), broker cluster identity matched, server version
  provenance `d164e93ae47a79ed175beeb9367212f7a7d5d1d5`.
- The managed `docker-compose.override.yml` was rewritten with identical content. A diff against
  its backup `docker-compose.override.yml.bak-20261109T043327Z-7330b51e` showed no change.

## Before / after

| | Before | After |
|---|---|---|
| Container | `2dd00be497c9...311c` | `df98e5a9d3a7d35cd0aa61aff3ef0cdca6024da247984d006a4c38c542cf335a` |
| Image | `sha256:5662b342b2d1518077a2e9b9962b2702679c391297f59553d09c94ee951a5d22` (built 2026-10-14 06:23Z) | `sha256:e520662203aad58fdba4aee30703b0c36e5e712923f53c3d95a059c549a305d0` (built 2026-11-09 04:34:20Z) |
| Started | 2026-10-14T06:24:42Z | 2026-11-09T04:34:23Z, restarts=0 |
| Source backup | | `/home/mc/antiphon-messaging/build/src.bak-20261109T043326Z-0b11015e` |

The previous image was tagged `am-service:rollback-2026-11-09` (`5662b342b2d1`) before the deploy.
The container has no Docker healthcheck, so its status is `Up` with no `(healthy)` qualifier.

Last log lines before the deploy (redacted; content only):
- `[inbox] stored telegram 1790 channels.inbound/0:518`, `[outbound] sent via telegram -> 1791`
- Repeated Slack `disconnect requested (warning); reopening` / `socket mode connection is live`
- Just above these: the tail of an EF Core `SaveChangesAsync` stack trace (Npgsql execution
  strategy) in the old container.

## Verification

- Script checks passed: container running; `/api/channels` lists the adapters `telegram, slack`;
  migrations 3 in source, 3 recorded; monitor `/health/inbound-unconsumed` Ready
  (`ageSeconds=2.4`, numeric offsets); running image ID changed; no
  `Unhandled exception|fail:|crit:` in the 100-line startup scan.
- `docker compose logs messaging-service --tail 120` (excerpt):
  ```
  Messaging channel adapters registered: telegram, slack
  [ingress] starting channel telegram
  [ingress] starting channel slack
  [outbound] consuming channels.outbound
  [inbox] consuming channels.inbound
  Now listening on: http://[::]:8080
  Application started.
  [slack] socket mode connection is live
  [inbound-unconsumed] monitor ready group antiphon-server-bridge topic channels.inbound
  ```
  The only warning is the standard ASP.NET `Overriding HTTP_PORTS` notice. There were no
  `[telegram]` lines at +2 min. The adapter logs only when getUpdates fails (backoff, 409
  Conflict, non-JSON), so silence means the long-poll is healthy.
- Source provenance: the Dockerfile sets no revision label. Every one of the 69 files in remote
  `build/src` (excluding bin/obj) matched the commit's blob byte-for-byte after CRLF
  normalisation, and matched the clean worktree checkout exactly. Remote
  `Antiphon.Messaging.Telegram/TelegramChannelAdapter.cs`: SHA-256
  `d7c6b1e95c7bf2c0d69fd29b2bdf28686bdd70b5f46162e38156a2f2bc3ddc9a` as deployed (CRLF);
  LF-normalised `aea5607c1b97c1af8084265f41298be704f965c98784ac254bb4b96a7f0ef546`, equal to
  `git show 653f1145:src/Antiphon.Messaging.Telegram/TelegramChannelAdapter.cs | sha256sum`.

## Migration history: one new migration (not CARD-1799)

The brief expected no change in `__EFMigrationsHistory`. It changed from 2 rows to 3:

- Before: `20260619223142_InitialCreate`, `20260830190000_AddInboxLagMetadata`
- After: the same two, plus `20261014172813_ScopeInboxIdentityToConversation` (ProductVersion 9.0.4)

CARD-1799 adds no migration, as the brief said. The new one comes from CARD-1248 (`564a64aec`,
2026-10-14, "transactional inbox index swap"). It was on master but missed the 2026-10-14 06:23Z
image by about 11 hours, so this deploy is the first to carry it. The service applied it at
startup with no error. The pre-deploy EF `SaveChangesAsync` exception may be the inbox-identity
collision that this migration fixes (not verified). A plain image rollback would leave the new
index in place.

## Rollback

```
ssh mc@server2
cd /home/mc/antiphon-messaging
docker tag am-service:rollback-2026-11-09 antiphon-messaging-messaging-service:latest
docker compose up -d --no-deps messaging-service
# optionally restore source: mv build/src build/src.failed-<ts> && mv build/src.bak-20261109T043326Z-0b11015e build/src
```

The old image predates the `ScopeInboxIdentityToConversation` migration. Before relying on a
rollback, check that the old code works with the new inbox unique index, or revert the migration
deliberately.

## Not done

- No live Telegram test message, by design. The operator verifies with the next over-cap
  attachment from the Family chat, which should get the named over-cap note and one automatic
  reply to the sender.
- AppHost, the production runner, the rc qualification run and the other server2 stacks were not
  touched.
