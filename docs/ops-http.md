# Inspecting agents, boards and live sessions over HTTP

CARD-0667 S4b changes only the **orphan sweep** release contract described below:
`POST /api/session-runners/{runnerId}/slots/release-orphans` retains operator authentication
and its required reason, but uses conditional discovery instead of force release. Its response
adds `candidates`, `deferred` and per-seat `dispositions`; `released` counts confirmed
removals only. The sweep processes at most 32 inventory seats per request. With
`TerminalRunnerSeatRelease:AutomaticEnabled=false` (the shipping default), it is a no-op.
Unsupported peers and uncertain evidence stay deferred. Explicit single-seat operator force
release is unchanged. The existing slot job separately recovers legacy intents and attention,
then performs bounded conditional discovery when enabled; no new scheduler is introduced.

For CARD-0719 quota refusals, inspect the task's `SubscriptionQuotaExceeded` failure
code and Blocked event, its session transcript's API-error class and captured
`apiErrorTimeZoneId`, and the `(kind, modelAlias)` availability hold. Automatic
holds are `AutoDetected`; `ResetAtUtc` is the parsed instant and `DisabledUntil` is
the reset plus two minutes. Old runners omit timezone evidence, so dated reset text
gets an explicitly estimated finite hold. A Manual hold retains its deadline.
Clearing or expiring a hold does not requeue a blocked delegate.

The operator's map of the surface an orchestrator actually reaches for: which agents exist, what
they are running, which boards and cards are open, and what a live session is doing. It is
deliberately short and it is not the route map — [antiphon-api.md](antiphon-api.md) is that, and
`server/Api/Endpoints/*.cs` plus `src/Antiphon.SessionRunner/Program.cs` are the authority over
both. Cards are worked through `scripts/card.ps1` (`server/Bundles/board-api.md` for the raw card
API); nothing here replaces that.

For process logs, Hangfire history, server2 container output, deployment-evidence gaps and
transcript retention, see the authoritative [log-source inventory](logs.md).

Use `GET /api/pipeline-definitions` to list shared stage pipelines and
`scripts/pipeline-definition.ps1` to inspect, clone, revise, or select one for a board or project.
Board and project detail responses expose the effective pipeline and its source.
For desktop Postgres query totals, use the
[pg_stat_statements query and reset commands](logs.md#desktop-postgres-query-statistics).

**Do not grep `MapGet` to find a route.** The one route this page cannot give you is a route this
page says does not exist.

Landing timeline events carry nullable `landingOperationId`, `landingPublication`,
`landingCleanup` and `landingMode` snapshots. `LandingCleanup` updates an existing publication
without counting another one. Legacy events have null snapshots and grant no cleanup authority.

## Two processes, two prefixes

Mixing them is how sessions get 404s that look like a broken server.

| Process | Base | Prefix |
|---|---|---|
| Antiphon server (Aspire) | `http://localhost:17202` | `/api/...` |
| Session-runner (production) | `http://localhost:17204` | `/sessions/...` — **no `/api`** |

Simple mode serves the server on `17281`; an E2E run owns its own **random** runner port, never
17204. Resolve the base the way the scripts do — `$env:ANTIPHON_API`, falling back to
`http://localhost:17202` — and send `$env:ANTIPHON_TASK_TOKEN` as the `X-Antiphon-Task-Token`
header when it is set. Inside a running agent session both are already in the environment.

### ChatGPT / Codex as an external orchestrator (CARD-0398)

Run `delegate.ps1 -Capability <name> …` from the approved checkout (add `-Kind Codex` while Claude is held). Do not read capability files, do not dump env looking for tokens, do not edit `Delegation:AllowedRoots`. Reports land on the bound card; poll `delegate.ps1 -Status <id>` or the card thread.

The operator issues the capability with `scripts/capability.ps1 issue -Name <n> -Roots <checkout>`. That script writes a DPAPI blob and prints the name and store path — never the token. `-Orchestrator -Kind Codex` stays 422. Codex remote control stays `409 remote_control_refused`. A Claude usage hold still `409 model_disabled` unless the caller passed `-Kind Codex`; quota-exhausted is `409 subscription_quota_low`. The complementary fallback is a named Codex AlwaysOn plus `POST /api/sessions/{id}/messages` — still do not widen `AllowedRoots`.

### Claude held

While Claude aliases are on a usage hold, a capability caller that wants to keep dispatching must pass `-Kind Codex` (Worker or stage role, not Orchestrator). Default kind is still ClaudeCode and still 409s. The complementary path is the named Codex AlwaysOn session token (kind-blind `MayDelegate`). Neither path edits `Delegation:AllowedRoots`.

## The jobs you have

CARD-0959 adds nullable `codexCliVersion`, `codexCliVersionCheckedAtUtc`,
`codexCliVersionError` and `codexCliVersionStale` to the existing runner catalogue
and remote status responses. These describe the default installed Codex launcher,
independently of build SHA, capacity and connection liveness. Checked-at is the
completed probe attempt time; reads and heartbeats do not renew it. A successful
observation is fresh through exactly fifteen minutes, a fixed display threshold.
CARD-1031 permits a valid stdout version alongside the fixed `stderr_output` or
`output_truncated` advisory in the legacy `codexCliVersionError` field. These two
advisories permit freshness; skew and malformed fingerprints still override them.
Other errors, or absent/invalid versions or attempt times, remain unknown. Only
complete retained stdout lines count after truncation; stderr never supplies a
version and its raw content is never returned or logged. Nonzero exit and timeout
remain failed observations. Wire members and types are unchanged: an old server
can show the new version/advisory with null freshness until its reader is updated.
Old runners' missing fields still mean unknown. This feeds CARD-1023's observation
data and introduces no compatibility or admission policy.
Stale is null without a valid successful observation. A timestamp more than one
minute in the future reports `clock_skew`; a malformed non-null launcher fingerprint
reports `launcher_mismatch`. Unknown peer error text becomes `probe_unavailable`.
Version and attempt time remain the received observations. Disconnect retains
display evidence while dispatch eligibility follows the existing connection rules.
An accepted registration clears CLI evidence from the previous epoch.

The internal runner `POST /capabilities/codex-cli-version` accepts only executable,
resolution cwd, PATH/PATHEXT and recognized `codex.js` prefix. It has a matching
phone-home operation and performs a bounded, auth-free, version-only probe.
Descriptor strings are limited to 32,768 characters each; invalid inputs return
unknown evidence before process start. Default probes run before registration and
every five minutes. `GET /capabilities` reads memory. Launcher fingerprints stay
on internal capability/probe DTOs and are absent from catalogue/status projections.
There is no new server public probe endpoint. No observation refuses create, retry,
dispatch, warm reuse or launch, and those paths start no diagnostic version probes.
Existing auth/model/placement rules remain independent. Activate reporting runners
first, then server readers; older runners display unknown without a CLI gate. See the active
[implementation and verification plan](superpowers/plans/2026-10-03-card-0959-runner-codex-version-plan.md).

CARD-0415 adds `GET`/`PUT /api/agents/{id}/specialist-routing` and
`POST /api/agents/{id}/specialist-routing/revalidate` for the configured standing Check owner.
`scripts/specialist-routing.ps1 inspect|set|revalidate -Agent <guid>` uses these shapes and reads a
fresh revision before writes. `set -Candidates 'ClaudeCode/High,Codex/Low'` declares ordered pairs;
the first pair must match the actual primary. `set -Disable` retains an existing list.
These endpoints expose declared pairs, retained candidate readiness/refusal reasons and durable
logical health. Reconciliation creates separate typed Claude alternate seats. Revalidate rotates
one bounded qualification authorization; the worker still requires verified CLI capability and
an idle authorized generation before its two semantic probes. A successful write does not mean
ready or activated. The production capability catalog is currently empty and authenticated
acceptance remains pending. Codex rows report PendingDependency CARD-0167. See the
[continuation evidence](investigations/2026-09-09-card-0415-continuation-evidence.md) before claiming
the chain is available. Durable `StandingSpecialistHealth` Attention survives incident pruning;
readiness or qualification alone cannot resolve a real-service outage.

| Need | Method | Path |
|---|---|---|
| Every agent, with its live session | GET | `/api/agents` |
| Session-state cache/process diagnostics (CARD-0701) | GET | `/api/diagnostics/session-state` - cache hits/loads/faults/evictions/pressure, ingest calls and committed rows, tagged EF read-attempt counters, process CPU/memory/GC, live/unknown counts, resolved driver/provider versions and sanitized pool flags. Metadata only; does not pull transcripts. |
| One agent | GET | `/api/agents/{id:guid}` |
| Runner catalogue (CARD-0710, CARD-0727) | GET | `/api/session-runners` lists desktop plus every configured `PhoneHomeRunner:Runners` entry (production: `server2` and `server2-temp`, the same values and the same phone-home secret): platform, availability, capacity and occupancy. One entry's failure does not drop the others. An id that is not in the map is 404. |
| Hosts list (CARD-0718) | GET | `/api/hosts/stats` lists desktop and configured runners with their latest sample, 1/5/15/30-minute rollups, Antiphon counts, and `live` / `stale` / `offline` / `unsupported` state. `stale` keeps the last values; unknown values stay null, never zero. The Hosts page subscribes to `HostStatsUpdated` in SignalR group `hosts`. |
| Host budgets (CARD-0654) | GET / PUT | `GET /api/hosts` lists local and configured runner limits, occupancy, availability, revision and last reason. `PUT /api/hosts/{hostId}/budget` takes `{ "maxInFlight": 3, "reason": "reserve capacity" }`; null clears the override, 0 drains new dispatch, and the permitted range is 0–512. An unknown host is 404. Lowering a budget holds new work; it does not stop running sessions. Budget writes are operator actions; an orchestrator reads the limits and does not change them on its own initiative. `GET /api/hosts/{hostId}/occupancy-samples?from=&to=&limit=` is the read-only occupancy audit for that host (CARD-1079): newest first, default window the last 24 hours, `limit` defaults to 500 and caps at 2000. An unknown host is 404. There is no write route. |
| Runner capacity (CARD-0654) | GET / PUT | `/api/session-runners/{runnerId}/capacity` shows the runner's declared capacity, that runner's server bound, effective budget and occupancy. PUT takes `{ "capacity": 6, "reason": "operator adjustment" }`, pushes the value to a live runner, and returns 409 when the runner is unavailable or cannot apply it. The runner persists a successful change across restarts. A timed-out push records a Warning incident; if a later heartbeat reports the applied value, reconciliation records an Info incident for the capacity change. |
| Host series (CARD-0718) | GET | `/api/hosts/{hostId}/stats/series?metric=cpu&window=30m` returns runner-memory points; `metric` is `cpu`, `load`, or `memory`, and `window` is `1m`, `5m`, `15m`, or `30m`. Unknown host is 404; bad metric/window is 400; unavailable runner is 409. Tasks are a current count, not a series metric. |
| Runner defaults (CARD-0710) | GET / PUT | `/api/runner-defaults` is the live global and per-kind host preference. `Delegation:DefaultRunnerId` is import-only after revision 1. PUT needs the current `expectedRevision`, a complete `kindDefaults` array, a reason, and `provenance: Human`. |
| Drain a runner (CARD-0727) | POST | `/api/session-runners/{runnerId}/drain` takes `{ "reason", "redirectTo"?, "retireWhenIdle"? }` and `POST /api/session-runners/{runnerId}/drain/clear` takes `{ "reason" }`. Both require `X-Antiphon-Operator-Token` (403 `operator_token_required` otherwise). The token file is the same owner-only file as the seat routes; `scripts/runner-drain.ps1 status\|drain\|clear` reads it and never prints it. `status` is `GET /api/session-runners/{runnerId}/status`. A response that is not HTTP 200, including 404, is not eligible. Drain is durable and mirrored into the directory. New work is 503 `phone_home_runner_draining` (or `phone_home_runner_retired` once `retiredAt` is set) while session calls on that runner keep using `Resolve`. An unknown, disabled, desktop, self, or draining/retired redirect is 409 `phone_home_redirect_invalid`. Status adds `acceptingNewWork` (false while draining or retired even when `dispatchEligible` is true), `draining`, `drainedAt`, `drainReason`, `redirectTo`, `retireWhenIdle`, `idleObservedAt`, `retiredAt`, `retireReason`, `sessions` (non-terminal bound rows), `queuedTasks` (queued tasks with no session), and `runnerSessions` (last List count of non-Exited sessions). The catalogue row adds `draining` and `acceptingNewWork`, and `unavailableReason` is `draining` when the runner is eligible and draining. After drain/clear, status shows `draining: false` and `drainReason` holds the clear reason. The retire route is not in this round. |
| Phone-home runner status (CARD-0490, CARD-0604, CARD-0679, CARD-0729) | GET | `/api/session-runners/{runnerId}/status` — `available`, `dispatchEligible`, `runnerStoreId`, `platform`, `buildVersion`. `available` alone is not enough to dispatch; wait for `dispatchEligible`. A configured runner that has not connected (production `server2-temp` between upgrades) is **200** with `available: false`, `dispatchEligible: false` and null `runnerStoreId`, `processBootId` and `buildVersion` — that is not another runner's identity. An id that is not in the map, including a typo of a configured id, is **404** with none of those fields, and every script treats that 404 as not eligible (CARD-0729). When not available, `disconnectReason` names why: the recorded end of the last connection (`event_overflow`/`message_too_large` as `phone_home_*` codes, `close_received`, `transport_abort` (the runner connection dropped), `request_aborted` (this host stopping; the runner receives close 1001 `server_stopping`), `superseded`, `receive_fault:<Type>`) or `lease_expired` / `socket_closed`, with `lastDisconnectAtUtc`. `pendingEvents` and `pendingEventBytes` are the live connection's unreleased event backlog; `reconnects` counts connections accepted since the desktop started; `lastCatchUpMs` is the last recovery List latency (null until recorded). Every ended line carries its reason and connection correlation. `WsError` and `SocketError` appear only when a WebSocket exception was captured; socket `none` means that exception has no inner socket error, while cancellation without a captured exception omits both keys (CARD-0996). The matching desktop log lines are the `PhoneHomeLiveConnection` Warning "... ended: <reason> ..." and the 50%/90% pending high-water Warning. |
| Create an agent bound to a runner (CARD-0604, CARD-0727) | POST | `/api/agents` with `runnerId` — only an id in `PhoneHomeRunner:Runners` is admitted (422 otherwise). Its sessions launch in that container. |
| Create a task that runs on a runner (CARD-0604, CARD-0628, CARD-0633) | POST | `/api/agent-tasks` with `runnerId` — Worktree + Grok or Claude Code, no pin/OnAgent/Shared/ReadOnly/SourceLanding (422 otherwise). The desktop worktree stays canonical; the runner gets a mirror. While that mirror is outstanding the task stays Queued and its `Held` detail is one of: `Held: remote workspace preparation is in flight`, `Held: remote workspace preparation for runner '…' failed N time(s) in a row; next attempt not before …`, or `Held: RunnerUnavailable: runner '…' is not dispatch-eligible`. A task whose mirror is recorded launches without the repository mutation lease (CARD-0672 D-1): a runner session never writes the desktop checkout, so its launch waits for no land. Only the first crossing (the desktop worktree cut) and repair, SourceLanding and Interim claims still take the lease. A re-arm whose desktop worktree is already cut, whose mirror is not recorded, and whose progress baseline is already captured also takes no lease (CARD-1076). The preparer push is `git push origin <branch>` with no `-u`, so the desktop task branch does not track origin. One push runs at a time per repository. The budget key is `Delegation:RemotePrepPushBudgetMinutes` (default 20, floor 1). The child-journal purpose `remote-prep-push` does not fence while that push is live; a dead, reused, or unknown record still names `scripts/recover-repository-children.ps1`. `GET /api/agent-tasks/pipeline` queue reasons include `repositoryLease` and `remotePrep`. `remotePrep` `heldBy` is `BehindTaskId`, the enqueue-time snapshot (CARD-1093): with three or more waiters it can name a finished pusher or be empty, and it is not the exact current holder. |
| Runner provider sign-in (CARD-0628, CARD-0647) | GET | `/api/session-runners/{runnerId}/provider-auth/{provider}` — `provider` is `claude` or `grok`. A live probe on the runner returning `loggedIn`, `authMethod`, `subscriptionType`, `checkedAtUtc`, `error`. Grok reports whether `GROK_HOME/auth.json` is present and never returns the file. 409 `phone_home_unavailable` when the runner is not connected, 409 `phone_home_unsupported_operation` for a runner binary older than the probe. |
| Runner seats (CARD-0653) | GET / POST | `/api/session-runners/{runnerId}/slots` lists every session the runner still remembers: `status`, `ageSeconds`, `occupiesCapacity` (live or occupied only; Exited does not count), `orphan`, `custodyBackend`, `custodyExecutionId`. `declaredCapacity` is the runner's registered seat count and `occupied` is how many of the listed sessions count. `POST /api/session-runners/{runnerId}/slots/{sessionId}/release` and `POST /api/session-runners/{runnerId}/slots/release-orphans` take `{ "reason": "..." }` (required) and require the operator token in `X-Antiphon-Operator-Token` (403 `operator_token_required` otherwise, whatever the client address; the public vhost reaches Kestrel as loopback through Caddy and Vite). The token lives in an owner-only file the server creates at startup, `%LOCALAPPDATA%\Antiphon\operator-token` by default (`Operator:TokenPath`, alias `PhoneHomeRunner:OperatorTokenPath`); the script reads it and never prints it. Each kills the process tree, deletes the runner manifest so a restart does not adopt it, stops a live desktop session row, and writes a `RunnerSlotForceReleased` incident. The desktop audit is saved as an intent before the runner call. A runner refusal marks the intent `failed:<detail>`; a lost answer or failed audit save leaves it `pending:`, and the `antiphon:runner-slot-reconcile` Hangfire job (at startup, then `PhoneHomeRunner:SlotReconcileCron`, default every 2 minutes) finishes it by auditing only, once the runner no longer lists the session. It never kills, with one exception (CARD-0679 D-7): a `pending:kill-generation:<runnerId>:<ticks>` intent, recorded when a failed launch's kill could not reach the runner, is sent as a kill conditional on that generation once the runner resolves (the recovery pump enqueues the job on reconnect); a runner holding another generation marks it `failed:` and nothing is killed. An intent from an orphan sweep whose seat was claimed since is marked `failed:` instead. An orphan sweep re-reads the current session and open task and skips a seat whose identity changed. An orphan is a remembered session with no live desktop session, or a live one with no owner task (Queued, Dispatched, Working or Blocked), except a warm pooled delegate; a live Blocked child is not an orphan. Each slot carries `openTaskId` (the owner) and `park` (null, or the owner's current-attempt `parkId`, `state`, `reasonCode`, `releaseId`, `syncState`; display-only, never release authority) (CARD-1124). `scripts/runner-slots.ps1 list|release|release-orphans`. An occupying `orphan=true` slot is a `SlotOrphan` attention row. |
| Operator shutdown (CARD-0716) | POST | `/api/operator/shutdown` requires `X-Antiphon-Operator-Token` (403 `operator_token_required` otherwise, whatever the client address). Body `{ "reason": "restart-apphost" }` is optional and at most 200 characters. 202 `{ "accepted": true, "pid", "drainSeconds", "startingLaunches" }`. The route drains in-flight launches for `Operator:ShutdownDrainSeconds` (default 10; 0 skips the wait), writes one Warning task event for each Starting session that has a Dispatched task, then stops the host. `GET /api/version` advertises `operator-shutdown-v1`. `restart-apphost.ps1` is the caller. |
| Operator dashboard (CARD-0658) | POST / GET | `/hangfire` needs the operator token, whatever the client address: the `X-Antiphon-Operator-Token` header, or the `antiphon-operator-dashboard` cookie. `POST /api/operator/dashboard-sessions` (header required; 403 `operator_token_required` otherwise) answers `{ "loginPath", "expiresAt" }`, a one-time link valid for two minutes. `GET /api/operator/dashboard-login?nonce=` redeems it once, sets the 12-hour `HttpOnly`, `SameSite=Strict`, `Path=/hangfire` cookie and answers 302 to `/hangfire`; an unknown, expired or redeemed nonce is 403 `operator_login_invalid` with no cookie. `scripts/hangfire-dashboard.ps1` does both and opens the browser without printing the token or the link. |
| Start / stop an agent | POST | `/api/agents/{id}/start`, `/api/agents/{id}/stop` — named herdr pin (`herdrTabLabel`) 409s occupancy **before** enqueue; a later runner 409 is an async Failed row. |
| Runner named-tab preflight | POST | `:17204/herdr/placement/check` `{ sessionId, herdr }` — read-only; 200 `create`/`relaunch`/`adopt` or 409 with the launch codes. |
| Inspect one leftover Herdr pane | POST | `/api/herdr/pane-disposals/preview` `{ paneId, expectedSessionId?, expectedNativeSessionId? }`; one exact pane and at least one full UUID. Two-minute, single-consumer preview with sanitized ownership/process evidence; standing owner must be stopped. |
| Explicitly dispose a reviewed pane | POST | `/api/herdr/pane-disposals` `{ operationId, previewId, reason }`; 200 Closed/AlreadyAbsent, 409 refusal, 503 unavailable/Unknown. Best-effort fresh Antiphon checks precede unconditional `pane.close`; external changes after that check can race close. Read status after uncertainty; never replay destruction. |
| Read disposal receipt | GET | `/api/herdr/pane-disposals/{operationId}`; no session row required. Runner equivalents use `/herdr/pane-disposals`. `scripts/herdr-pane.ps1 inspect\|dispose\|status` uses server routes; dispose is dry-run unless `-Execute`. |
| Manually refresh an agent's policy (CARD-0334) | POST | `/api/agents/{id}/refresh-policy` (`{ force?: bool }`) — idle-gated like the sweep: kill+resume, or a `Notify`-lane message, without suspending supervision. `force` skips only the idle-minutes floor and the cooldown; a working session is always 409 `session_working`, and a Codex/unbound-transcript agent is 409 `not_resumable`. Returns `{ refreshed, notified, agent }`; a Notify-lane 200 is `refreshed: false, notified: true`. |
| Delete an agent | DELETE | `/api/agents/{id}` |
| Boards | GET | `/api/boards` (`?includeArchived=true`) |
| One board | GET | `/api/boards/{id}` (`?view=summary`, `?includeArchived=`) |
| A board's columns, name to id | GET | `/api/boards/{id}/columns` |
| Card-file policy and cleanup state | GET | `/api/boards/{id}/card-files/status` |
| Explicit card-file policy update | PUT | `/api/boards/{id}/card-files/settings` (both policy and expected-policy booleans) |
| Reconcile permitted card files and revoked exports | POST | `/api/boards/{id}/card-files/sync` (`?dryRun=true`; HTTP 200 may describe a refusal) |
| A board's cards | GET | `/api/cards?boardId={guid}&limit=&pageToken=&includeArchived=` — bounded, resumable summary pages; `card.ps1 list -Board <name> -Json` exhausts them |
| Search cards | GET | `/api/cards/search?q={literal}&boardId=&status=&limit=&pageToken=&includeArchived=` — all-board search if unscoped; `card.ps1 search '<phrase>' -Board <name> -All` exhausts pages |
| One card | GET | `/api/cards/{id}` — `CARD-0296` resolves; prefer `card.ps1 get` |
| Move a card | PATCH | `/api/cards/{id}` `{ boardColumnId, concurrencyToken, reason?, spawn? }`. A move into a terminal column returns `taskSettlement` (`canceled` and `leftOpen` short ids) when it settled or left a bound task; the property is omitted when there was nothing to settle (CARD-0738). `card.ps1 move` / `close` print `tasks canceled` and `tasks still working` lines from it. |
| Queue a card diagnosis (CARD-0352) | POST | `/api/cards/{id}/diagnose` — 202 `{ queued: true }`; `card.ps1 diagnose CARD-nnnn` (`-NoWait` skips the 120 s poll). 409 `diagnose_disabled` when the seat is off. Shipped `DiagnoseLabelMode` is **Shadow** (ledger only) until flipped to Apply. |
| Diagnoses ledger / stats | GET | `/api/diagnoses?cardId=` (newest first), `/api/diagnoses/stats?since=` |
| Distillations ledger / stats (CARD-0330) | GET | `/api/distillations?since=&outcome=&feedback=&limit=`, `/api/distillations/stats?since=` — `scripts/distiller.ps1 -Stats` / `-List [-Flagged]` |
| Distillation feedback (CARD-0330) | POST | `/api/agent-tasks/{id}/distillation/feedback` `{ verdict: Good\|Lost\|Noisy, note? }` — 409 if the task has no distillation. `delegate.ps1 -Flag <id> -Verdict Lost\|Noisy\|Good [-Note]` |
| Home Tasks rail (cards + unbound delegations) | GET | `/api/home/tasks` |
| What needs a human (fleet-global) | GET | `/api/attention` — fleet-global across every board; there is no board filter. `DispatchHeld` (CARD-0535) is a queued dispatcher hold past `Delegation:DispatchHeldWarningSeconds`; `ConditionKey` `dispatch-held:{id:N}`. Its `evidence` ends with the per-class wait ledger for the current queue stint, summed from the task's `Held` rows (CARD-0672 D-3): `leaseWait=Ns; prepWait=Ns; runnerWait=Ns; capWait=Ns; otherWait=Ns; class=<dominant>`, and `holdClass` is that dominant class (`lease`, `remoteprep`, `runner`, `cap`, `scope`, `agent`, `landing`, `routing`, `other`), so land-lease starvation reads apart from runner capacity. The dispatcher's `HeldAged` rows carry the same fields after `occupants=`. `CompactionContinuationStalled` (CARD-0079) is one open Check compaction episode; `ConditionKey` `compaction-continuation:{id:N}`. |
| Runner and journal alarms | GET | `/api/attention` — `RunnerUnavailable` is one raised outage per runner, keyed `runner-unavailable:{runnerId}` after `Alarms:RunnerGraceSeconds` (180 default). `RepositoryChildJournalStale` is one row per fenced Git common directory, keyed `journal-stale:{commonDirectoryKey}`, with each stale record and the manual recovery command in `evidence`. The loop inspects on startup, a fence signal, and every `Alarms:SweepMinutes` (15 default); `Alarms:JournalStaleMinutes` (5 default) sets the stale-record age. `Alarms:Enabled` and `Alarms:JournalEnabled` gate the loop and journal inspection respectively (both default true). Both rows clear when the source state clears. |
| Seat occupancy (CARD-1079) | GET | `/api/attention` — three detection-only kinds, phone-home hosts only (the local host is samples, not seat rows). `SeatIdle` (57) is a seat that occupies capacity while its task is not Dispatched or Working, keyed `seat-idle:{runnerId}:{sessionId:N}`, Warning at `Attention:SeatIdleWarningMinutes` (30) and Error at `Attention:SeatIdleErrorMinutes` (180). Evidence `age=` is minutes (`age=30min`). `OccupancyDivergence` (58) is one row per host, keyed `occupancy-divergence:{hostId}`, for `inFlight` minus Dispatched/Working when at least one idle seat is behind that gap, on the same ages. `SlotOrphan` (59) is an immediate Warning for an occupying `orphan=true` slot, keyed `slot-orphan:{runnerId}:{sessionId:N}`. A live Blocked owner is not an orphan; it is a `SeatIdle` row from the warning age, and seat evidence carries `park=` (`none`, or the park state and reason). `Attention:SeatWatchEnabled` defaults true; false is the rollback (the sampler writes nothing and these three kinds are omitted). Nothing here stops, kills, releases or dispatches. |
| Runner seat release and park sync (CARD-1065) | GET | `/api/attention` — `SessionDisagreement` keyed `runner-seat-release:{releaseId}` while a release is open or its park sync is pending or held. Evidence carries the park id, sync state, source SHA and reason, and omits report and answer text. Task detail `parkSync` is set when that attempt's sync state is not `NotRequired`. `BlockedTaskParking:Enabled` defaults to false and `ReclaimExisting` defaults to false. `ReclaimIntervalSeconds` defaults to 120. ReclaimHeldBackoffSeconds defaults to 600. `TerminalRunnerSeatRelease:AutomaticEnabled` defaults to false. New publication and Blocked release require Enabled plus automatic release. Legacy discovery additionally requires ReclaimExisting. Disabling does not delete a publication receipt. Nothing here stops a Working session. |
| Settlement sync debt (CARD-1082) | GET | `/api/agent-tasks/{id}` exposes syncDebt (id, state, sourceSha, confirmedSha, reasonCode, attempts, nextAttemptAt, sourceReadyAt) for the current attempt's debt row. Pending is never Confirmed. progressEvidence.remoteSync.state Pending with no syncDebt is the Blocked case: Pending evidence without a debt row means the task stayed Blocked and a reply is required. GET /api/attention adds SessionDisagreement keyed settlement-sync-debt: for Held debt and for Pending debt older than the threshold. The stale headline is desktop sync still pending (lease contention). Delegation:RunnerSyncDebtOnSettlement defaults to true. The only debt-creating reason is runner_sync_lease_busy. The succeeded caller line says synced later. RunnerSyncDebtAttentionMinutes defaults to 30. delegate.ps1 status prints the Pending origin line and a Desktop sync debt line. |
| Delegated work | GET | `/api/agent-tasks?projectId=` — always `{ scope, items, excluded }`. An omitted `projectId`/`boardId` is the whole fleet (`scope` null). `unscoped=exclude|include|only` (default exclude when a scope id is present). Unknown list keys are `400 unknown_query_parameter`. `/summary` takes the same scope. `/pipeline` stays fleet-wide. |
| Closed-card sweep (CARD-0738) | POST | `/api/agent-tasks/closed-card-sweep` `{ apply?: bool }` — `apply` defaults to false. A preview lists open non-specialist tasks whose card is Done, Canceled, or archived and writes nothing. `apply: true` cancels only unstarted rows created before the close, prefixing the reason `Closed-card sweep: `; started rows and rows created after the close are left open and say why. |
| Issue / list / rotate / revoke a Delegation Capability (CARD-0398) | POST / GET / POST rotate / POST revoke | `/api/delegation-capabilities`, `/api/delegation-capabilities/{id}`, `…/rotate`, `…/revoke` — `scripts/capability.ps1`. GET never returns the token. |
| A session's screen | GET | `/api/sessions/{id}/buffer` |
| A session's transcript | GET | `/api/sessions/{id}/transcript?since={sequence}` |
| Type work into a session | POST | `/api/sessions/{id}/messages` |
| Schedules for an agent / card | GET | `/api/schedules?agentId=` / `?cardId=` (`scripts/schedule.ps1`) |
| Kill a session | POST | `/api/sessions/{id}/kill` |
| Worktree residue preview (CARD-0459) | POST | `/api/agent-tasks/worktree-residue/preview` `{ projectId?, boardId? }` — inventory only. `scripts/worktree-residue.ps1 -Action Preview`. Never mutates. |
| Worktree residue run | GET | `/api/agent-tasks/worktree-residue/runs/{runId}?page=` — durable candidate page. |
| Release a settled worktree | POST | `/api/agent-tasks/{id}/worktree-retirement` `{ expectedTaskRevision, sourceSha, reportDigest?, noFurtherWorkspaceUse: true, reason, handoffDispositions? }`. Records release; does not delete. Child task tokens cannot release siblings. |
| Revoke a release | DELETE | `/api/agent-tasks/{id}/worktree-retirement/{retirementId}` — refused after claim or deletion intent. |
| Land a succeeded Worktree task | POST | `/api/agent-tasks/{id}/land/v2` (CARD-0495; same body and handler as `/land`) — `{ expectedSourceSha: string, reviewEvidenceId?: guid, verify?: string }` — 202 `{ status: "queued" \| "requeued" }`. `GET /api/version` advertises `capabilities: ["land-v2"]` with the build SHA. `delegate.ps1 -Land` requires that exact marker (case-sensitive) and a full 40/64-hex SHA, then POSTs only `/land/v2`; missing marker/SHA, 404 version, or timeout is exit 1 with zero POSTs. A 404/405 on `/land/v2` is a loud compatibility failure, never a retry to `/land`. Fresh work requires a full 40- or 64-hex `expectedSourceSha` (422 `expected_source_sha_required` if omitted; 422 `expected_source_sha_invalid` if abbreviated). Optional `reviewEvidenceId` must name a clean Review row whose subject/SHA/ref/repository match (409 otherwise). A pending request cannot change SHA, evidence or filter (409 `land_request_identity_conflict`). 409 `land_running` means a land is running in this server now. Read `Landed` / `AlreadyPresent` / `LandedWithResidue` / `LandRefused` and the structured `landing` / `landRequest` / `reviewEvidence` on the task. `GET /api/agent-tasks/{id}` exposes `landRequestedAt`, `landStartedAt`, `landAttempt`, approved original vs verified SHAs. Re-POST of identical pending fields preserves identity; omitted resume fields inherit stored approval. Schema 3 never moves the local target before publication; a schema-2 row (pre-2026-09-25) may have. Legacy `/land` remains for old clients on an upgraded server. |
| Recover a reviewed Code landing owner | POST | `/api/agent-tasks/{owner}/land/v2` — `{ expectedSourceSha: full-sha, reviewEvidenceId: guid, recoverReviewedSource: true }` for its own pushed branch, or `{ expectedSourceSha: full-sha, reviewEvidenceId: guid, adoptFromTaskId: guid }` for a same-card Code/Worktree task's pushed branch. `delegate.ps1 -Land <owner> -ExpectedSourceSha <sha> -ReviewEvidenceId <guid> -RecoverReviewedSource` or `-FromTask <task-id>` sends this body only to `/land/v2`. The source Review must be Clean Final/Full, current, exact SHA/ref/repository, and not superseded. Owner may be Succeeded, Blocked or Failed; Canceled and repair-source land targets refuse. A NeedsResolution request is superseded with a new row; a live Queued/Held/Running request and an unconfirmed publication are not bypassed. Status and receipt expose `recoveryMode`, `recoverySourceTaskId`, `recoverySourceFullRef`, reviewed SHA, original owner status, owner remote before/after and superseded request ID. Adoption is leased, pinned and uses source `--force-with-lease`; target publication retains its ordinary guard. |

| Record/override a stage finding (CARD-0272) | POST | `/api/agent-tasks/{id}/finding` (`RecordStageFindingRequest`: `stage` name, `found` bool, `detail?`). Writes a `Source=Orchestrator` `StageOutcome` row that supersedes the latest for that (task, stage); `delegate.ps1 -Finding <id> -Stage … -Found "…"` / `-Clean`. |
| Per-stage hit rate vs. cost (CARD-0272) | GET | `/api/stage-outcomes` (`since`, `until`, `stage`, `cardId`, `latestOnly` default true) — rows plus a per-stage summary (runs, found/clean/skipped/failed/unreported, hit %, USD spent, USD per finding, server seconds). `scripts/stage-value-report.ps1` prints it as a table. |
| Host build slots (CARD-0589) | GET | `:17204/build-slots` (desktop; server2: `http://127.0.0.1:8080/build-slots` inside the runner container): budget, occupancy, live memory vs floor, leases with `holderAlive`, FIFO waiters. `POST`/`DELETE :17204/build-slots[/{leaseId}]` are for `scripts/run-checkpoint.ps1`, `scripts/build-slot.ps1` and the land verifier, never by hand; a `BUILD SLOT unleased` line in a report means the runner did not answer. Owner: [testing-and-build.md](testing-and-build.md) Build slots |
| Live runner sessions / rendered screen | GET | `:17204/sessions`, `:17204/sessions/{id}/snapshot` (CARD-0514: snapshot may include `acceptedStartedAt`; automatic RC uses `POST :17204/sessions/{id}/conditional-input` when `/capabilities` lists `conditionalMaintenanceInputV1`, never raw `/input`) |

Runner workspace phone-home (CARD-0779): operation `WorkspacePublish = 32` is sent only when
`GET /api/session-runners` advertises `workspacePublishV1`. Settlement asks the runner to push
its owned mirror branch with an ordinary fast-forward refspec, then re-observes origin before
syncing the desktop. `GET /api/agent-tasks/{id}` exposes `progressEvidence.remoteSync` with
`mirrorSha`, `mirrorRelation`, `mirrorDirty`, `mirrorPushed`, and `mirrorInspection`. Retirement's
`WorkspaceRemove` request includes `PublishedSha` on capable runners; the runner returns 409
`phone_home_unpublished_work` and retains the mirror when its tip is not in the published history.

```powershell
$api = if ($env:ANTIPHON_API) { $env:ANTIPHON_API } else { 'http://localhost:17202' }
$h = @{}
if ($env:ANTIPHON_TASK_TOKEN) { $h['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN }

# PITFALL (CARD-0546): put PARENTHESES around Invoke-RestMethod before piping, or assign it to a
# variable first. On PowerShell 7.6.6 the bare `Invoke-RestMethod ... | Select-Object` form emits
# a JSON array as ONE Object[], and Format-Table then prints a header plus one blank row for ANY
# array, empty or not -- a real list looks empty. `(Invoke-RestMethod ...) | ...` and
# `$r = Invoke-RestMethod ...; $r | ...` enumerate the rows; `@(Invoke-RestMethod ...) | ...` does
# NOT (it wraps the one Object[] it received and prints the same blank row). Verified live
# 2026-09-18 against 7 rows. Invoke-RestMethod has no -NoEnumerate switch.

# who is running what -- liveSession is null when the agent is not up
(Invoke-RestMethod "$api/api/agents" -Headers $h) |
    Select-Object name, status, @{n='session';e={$_.liveSession.id}}

# a board's complete card list (the script follows bounded pages)
pwsh -NoProfile -File scripts/card.ps1 list -Board Antiphon -Json
# duplicate check: search stored full fields, then inspect a candidate's full detail
pwsh -NoProfile -File scripts/card.ps1 search '<distinctive phrase>' -Board Antiphon -All
pwsh -NoProfile -File scripts/card.ps1 get CARD-0846 -Board Antiphon

# delegated work in flight (occupancy) -- always an envelope; read .items. Prefer ?projectId=
# on an Antiphon-board question. Status names are case-insensitive, a comma list unions,
# an unrecognised value is 422 validation_failed
(Invoke-RestMethod "$api/api/agent-tasks?projectId=$($project.id)&status=Dispatched,Working,Blocked" -Headers $h).items |
    Select-Object id, role, status, cardIdentifier, projectName, boardName
# the purpose-built occupancy read: in-flight / queued / blocked / ready rows per stage (CARD-0304);
# GET /api/agent-tasks/summary `byStatus` is the fleet-wide cross-check on the same column
Invoke-RestMethod "$api/api/agent-tasks/pipeline" -Headers $h

# One fleet-wide glance: `investigateBacklog.total/items` gives the first five ranked fresh
# Backlog candidates; `stages` retains formal in-flight, queued, blocked and ready work.
# Candidate links need both boardId and cardId because CARD-nnnn is board-scoped.
# The rank/position/due/created order is advisory; inspect a candidate's thread for
# cross-card landed coverage, WIP and scope before deciding to dispatch.
# CARD-0738: preview open tasks whose card is already closed. apply defaults to false.
Invoke-RestMethod -Method POST "$api/api/agent-tasks/closed-card-sweep" -ContentType application/json -Body '{"apply":false}'

# column name -> column id, without pulling the whole board
Invoke-RestMethod "$api/api/boards/$($board.id)/columns" -Headers $h

# what a session has actually said, newest tail
Invoke-RestMethod "$api/api/sessions/$sessionId/transcript?since=0" -Headers $h

# give a running session work (queued, not typed raw -- see below)
Invoke-RestMethod "$api/api/sessions/$sessionId/messages" -Method Post -Headers $h `
    -ContentType 'application/json' -Body '{"body":"status please","mode":"WhenIdle"}'

# start an agent fresh; the body is required even when it is empty
Invoke-RestMethod "$api/api/agents/$agentId/start" -Method Post -Headers $h `
    -ContentType 'application/json' -Body '{"fresh":true}'
```

## Shapes that bite

- **PLURAL, OR 404.** `/api/boards`, `/api/agents`, `/api/cards`. There is no `/api/board`, and a
  404 from it says nothing about whether the board exists.

- **THERE IS NO `GET /api/sessions`.** The server exposes sessions only by id. To find one, read
  `liveSession` off `GET /api/agents`, or ask the runner: `GET http://localhost:17204/sessions`
  lists every session the runner still knows about, live and exited.

- **AN `Invoke-RestMethod` ARRAY PIPES AS ONE OBJECT (CARD-0546).** On PowerShell 7.6.6 the bare
  `Invoke-RestMethod ... | Select-Object ... | Format-Table` form emits the JSON array as a single
  `Object[]`, and the table prints a header plus one blank row for ANY array, empty or not. Every
  "`?status=Working` returned nothing" observation on CARD-0546 was this idiom; curl and the
  parenthesised form showed the rows on the same server. Put parentheses around the call before
  piping, or assign it to a variable first (`scripts/checkpoint-task.ps1` assigns, then wraps the
  variable in `@()`). Inline `@(Invoke-RestMethod ...)` does NOT fix it: it wraps the single
  `Object[]` it received and prints the same blank row. Read occupancy from
  `GET /api/agent-tasks/pipeline` rather than a hand-filtered list. The status filter itself is
  correct and pinned (`AgentTaskListStatusFilterTests`, `AgentTaskListEndpointTests`).

- **CARD LISTS ARE BOUNDED PAGES.** `GET /api/cards` still requires `boardId`, `status` or
  `updatedSince`; `limit`, `pageToken` and `includeArchived` do not count as scope. The response
  is `{cards,truncated,nextPageToken}` in `updatedAt DESC, id ASC` order. Follow each token until
  `truncated=false` and the token is null. `GET /api/cards/search` requires a nonblank literal
  `q` and returns `{cards,total,truncated,nextPageToken}`; it searches identifier, alias, title,
  full description, decoded labels and full terminal reason. Both endpoints default to live cards;
  `includeArchived=true` includes archived cards. `limit` is a page size capped at 500, never a
  total cap. A continuation with changed matching rows is 409 `card_page_changed`; restart from
  page one. `card.ps1 list` and `search` buffer all pages or fail. A lack of matches in a capped
  list or text preview does not establish absence, and a failed enumeration cannot establish
  "no duplicate". Inspect a search hit with `card.ps1 get` for full text.

- **SNAPSHOT IS RUNNER-ONLY.** `GET :17204/sessions/{id}/snapshot` renders the screen; the server
  has `buffer` and `transcript` and no snapshot at all. Grepping `SessionEndpoints.cs` for it finds
  nothing because it was never there.

- **AGENT, BOARD AND SESSION IDS ARE GUIDS, WITH A ROUTE CONSTRAINT.** Only cards resolve a
  friendly identifier. There is no `GET /api/agents/{name}` and no slug resolver: list, filter by
  `name` or `slug`, then use the guid. A non-guid segment does not 400 with a helpful message — it
  simply does not match the route.

- **`working` IS TRANSCRIPT-DERIVED, NEVER CHANNEL SILENCE.** `GET /api/agents` `working` is
  `IsWorkingAsync`. A channel-bound agent idle between Antiphon notes is waiting; read `working`
  and the transcript, never the chat's silence. Catalog `lastMessageAt` is inbound only;
  `lastReplyAt` is the last outbound reply.

- **`POST /api/agents/{id}/start` REQUIRES A JSON BODY.** `{}` is the minimum and inherits the
  agent's persisted settings; no body at all is a **400** from model binding before the request
  ever reaches the agent. `{"fresh":true}` forces a brand-new conversation — the default resumes
  the agent's previous session so the terminal picks up where it left off. `remoteControl` overrides
  the persisted flag for this launch only. A start can refuse **409** `subscription_quota_low` or
  `model_disabled`; both are refusals, not warnings on a launch that happened.

- **STANDING CONTINUITY HOLD IS A DECISION, NOT A FRESH (CARD-0466 / CARD-0561).** Start
  returns 409 `standing_continuity_held` while `supervision.continuityHeldAt` is set. Reasons:
  `NativeSessionMissing`, `TargetMissing`, `TargetIncompatible`, `OwnershipUnproven`,
  `RepeatedResumeFailure`. Read `supervision.continuityResumeFailures` (non-infrastructure
  supervised resume failures) and `continuityReason` / `continuityEvidence`. Acknowledge with
  exactly one of `retryContinuity:true`, `resumeSessionId`, or `fresh:true`.
  `Supervision:ResumeFailureHoldAttempts` (default 5, 0 disables) trips `RepeatedResumeFailure`;
  it never auto-Freshes.

- **HERDR RETRY HOLD REQUIRES AN EXPLICIT ACKNOWLEDGEMENT (CARD-0388).** A held dead-agent
  Start returns 409 `herdr_supervision_held` before other launch guards or named-placement
  preflight. Read `supervision.herdrConsecutiveFailures`, `herdrFailureHeldAt` and
  `lastHerdrFailureKind` on the agent; Attention remains until acknowledged, even after incident
  pruning or AlwaysOn off. After inspecting and repairing the cause, POST the same start route
  with `{"resetHerdrFailureHold":true}`; `fresh:true` is optional. The UI's **Retry and resume**
  sends this flag. Acknowledgement commits before normal validation, so a later model, rules,
  provider or placement refusal can leave the hold cleared without launching. A live Start is
  idempotent and preserves the hold even with the flag. Stop, attach and automatic callers do
  not clear it. The hold neither kills panes nor reroutes channel input; held inbound uses existing
  drop reporting without a chat notice. See [Herdr supervision and standing ownership](herdr-sessions.md#supervision-hold-and-explicit-recovery-card-0388)
  before repairing or handing over a channel-bound seat.

- **STOP IS THE NAMED-AGENT KILL, AND IT SUSPENDS SUPERVISION.** `POST /api/agents/{id}/stop` kills
  the live session and, on an `alwaysOn` agent, suspends its supervision until a manual start —
  deliberate, so restart supervision never fights a human. An always-on agent that "won't come back"
  was usually stopped.

- **ARCHIVE IS `POST`; `DELETE` MEANS DELETE.** `POST /api/boards/{id}/archive` (reason required) is
  the reversible hide; `DELETE /api/boards/{id}` really removes the board and detaches its agents.
  `DELETE /api/agents/{id}` is a **hard delete** with no archive and no always-on guard — it
  releases the agent's cards and drops its workflow runs, and there is nothing to unarchive
  afterwards. It stops a live (Starting/Running/Stopping) session first, as an operator request, and
  answers 409 `agent_delete_session_live` (row kept) when that session is still live afterwards,
  because the agent row is the session's only owner (CARD-0691). Grepping for `MapDelete` and stopping there is how an archive gets done as a delete.

## Typed input goes through the queue

`GET /api/agent-tasks/{id:guid}/inputs/{eventId:guid}` returns the full LF-normalized
refinement or blocked-reply body as `text/plain; charset=utf-8` with `Cache-Control: no-store`.
It requires `X-Antiphon-Task-Token` for that exact task and recipient session. A missing,
stale or unrelated task credential is refused; an absent, mismatched, non-input or legacy
event is 404. The ordinary task/event DTO does not include this body. A runner pointer
uses its `ANTIPHON_API` base and `ANTIPHON_TASK_TOKEN` header at execution time; it never
embeds the token in the prompt or URL. Confirm runner API reachability during activation.

For a wedged head, inspect `GET /api/sessions/{id}/queue`: `deliveryAttempts` reaches the cap and
`parked: true` means automatic delivery skips that row (CARD-0501).
To clear a reviewed row, use `DELETE /api/sessions/{id}/queue/{messageId}`; the row id comes from
that queue response. Terminal-task briefs with an empty composer are canceled by the next flush.

`POST /api/sessions/{id}/messages` with `{"body":"...","mode":"Now"|"WhenIdle"}` (default
`WhenIdle`, which holds until the agent finishes its turn). That queue owns the delivery contract —
LF, bracketed paste, and a separate Enter — and the delivery verification that goes with it.

For a phone-home session, Mode `Now` and
`POST /api/sessions/{id}/messages/{messageId}/send-now` return retryable HTTP 503 with code
`phone_home_unavailable` while the runner cannot dispatch (before first List, reconnecting,
closed socket or expired lease). This refusal means no body was sent: Now inserts nothing and
send-now preserves the queued row and all previous attempt evidence. A connection loss after
body transmission retains that evidence for queue recovery; do not replay uncertain input as a
new message. Stale inventory alone does not refuse an otherwise dispatchable connection.
`WhenIdle` input remains accepted during an outage. Accepted @mentions use the same durable
queue, one occurrence per turn; their activity event is acceptance, not a delivery receipt.
Pending membership is cached for five seconds; a known active session omitted by that cache is
still unknown until the runner supplies authoritative inventory.

`POST /api/sessions/{id}/input` (`{"input":"..."}`) is a raw keystroke bypass, and the runner's
`POST :17204/sessions/{id}/input` is a further bypass beneath that. Neither is for work bodies:
they skip the paste contract and nothing records whether the prompt landed. CARD-0514 automatic
`/remote-control` and idle Esc use `POST :17204/sessions/{id}/conditional-input` with
`expectedAcceptedStartedAt` and `expectedLastSequence` when the runner advertises
`conditionalMaintenanceInputV1`; a 404 or missing capability is Unsupported, never a retry to
`/input`. See [session-runtime-invariants.md](session-runtime-invariants.md) for why, and treat
transcript-confirmed `UserPrompt` evidence — not a screen redraw — as the delivery verdict.

## Killing

`GET /api/attention` includes four CARD-0691 conditions, computed without taking action:

- `PoolDelegateUnreleased` (44, Error): a live pool delegate with no open task, past twice
  `Delegation:PoolReleaseGraceSeconds`. Warm, standing, sourced and mid-turn owners are excluded.
- `SessionStopStuck` (45, Error): Stopping unchanged for five minutes, or an unresolved
  generation-conditional deferred kill older than five minutes, even for a terminal session row.
  Evidence names the kill intent and distinguishes confirmed live, confirmed absent and unknown.
- `SessionUnowned` (46, Warning): a live session older than ten minutes with no card, standing
  owner, agent pointer or open task. Remote sessions require confirmed live inventory; an
  unknown CARD-0679 remote session is never inferred to be a leak.
- `ZombieCensusReport` (47, Warning): one row per candidate class (PoolExpired, EndedButAlive,
  Unclaimed) in the last successful local OS census, with count, up to five pid/agent/session
  examples and generation time. Absent before the first successful run; a failed run retains
  the previous timestamp. PtyHost surplus remains the existing `PtyHostCensusDiverged` alert
  and `scripts/reap-orphaned-pty-hosts.ps1` surface, not a new census classifier.

The three row-derived conditions include server2. The Windows OS census remains local-only.
`RunnerConsulted` refers to the local list; remote evidence uses CARD-0679's cached inventory.
All four conditions are read-only and offer inspection, never automatic cleanup.
Overlapping session leak conditions produce one row per session: `SessionStopStuck` takes
precedence over `PoolDelegateUnreleased`, then `SessionUnowned`; the other conditions' evidence
is attached to that row. Session-only inspection opens `/attention?session=<id>` and reads the
transcript. Census inspection opens `/attention?census=zombie-census:<class>` and shows the full
`censusCandidates` list from the same dated snapshot, including candidates beyond the five-example
preview and links to identified sessions. A resolved condition disappears on the next feed read.

`POST /api/agents/{id}/stop` is the front door for a named agent; `POST /api/sessions/{id}/kill`
kills one session and records `OperatorRequest` as the termination source. The session summary
now carries `terminationSource`; `Unknown` on a row closed after CARD-0316 ships is a bug to
file, not a state. The runner's
`POST :17204/sessions/{id}/kill` and `POST :17204/sessions/kill-all` bypass all of that
bookkeeping — last resort only, and `kill-all` is scorched earth across every session on the box.

A released process must end in one of three states — killed, pooled warm, or owned by a standing
agent (CARD-0221). A stalled session is a detection and decision state, never an automatic kill.

## Not here

Review/files, tracker sync, workflows, gates, channels, settings and delegation routes are in
[antiphon-api.md](antiphon-api.md), which also explains why there is no OpenAPI document.
Delegation is `scripts/delegate.ps1` ([orchestration-loop.md](orchestration-loop.md)); card writes
are `scripts/card.ps1`. There is deliberately **no `scripts/agents.ps1` wrapper** (CARD-0296): this
surface is almost all GET, and the one write that bites must go through the message queue rather
than a script that could quietly reimplement it. If turning an agent name into a guid keeps
hurting, that is a new card, not a helper smuggled in here.

## Card-file publication (CARD-0408)

[Card-file privacy](card-file-privacy.md) owns opt-in settings/status/sync, the
explicit private-notes boundary, private snapshots, revocation and cleanup.
Boards default off and Unknown repository visibility blocks publication. Private
notes do not appear in ordinary DTOs or generated markdown. Outcome and archive
reasons remain public fields on eligible cards. Cleanup pending is independent of
card/session state; disabling the feature freezes existing exports.

Inspect the agent/session and Stop active work before selecting history. Read
`GET /api/agents/{id}/sessions?take=25&before={session-guid}` for bounded metadata history;
`nextBefore` is the next cursor. GET never adopts ownership and Start revalidates it.
POST `/api/agents/{id}/start` with exactly one of `resumeSessionId`, `retryContinuity:true`,
or `fresh:true`. The first selects an owned Antiphon session GUID; retry uses the current
target after repair; Fresh explicitly creates a new ID and keeps the old row. Acceptance
means queued, not proven recovered. These options do not reset Herdr's independent hold.

Historical ownership is the immutable physical standing-agent ID, or unambiguous legacy
current-pointer, task execution or Crash/RestartScheduled/Recovered incident evidence.
ParentSessionId, cwd, name and knowledge of an ID never prove ownership. Missing or
contradictory evidence refuses `standing_resume_owner_unproven`; a different stamped
owner refuses `standing_resume_not_owned`. Deleting/recreating a name does not transfer
ownership. This cannot reconstruct native history overwritten by the former same-ID fallback.

Selecting older history refuses live/queued sessions, pending card work and open execution
assignments. Only never-attempted pending non-rules input moves, appended in source order
after the target queue. Any delivery baseline, timestamp, verdict or settlement evidence
refuses the switch (including manual Fresh) with `standing_resume_delivery_pending`.
Resolve that input using existing queue controls. Transcript and task history stay separate.

Land detail also includes `landRequest`: request/hold/progress clocks, holder, attempts,
reconciliation disagreement, source SHAs, `sourceRefusalReason`, terminal failure
(`terminalFailureCode`, `failureDiagnosticId`, `failureExceptionType`) and safe source-inspection
metadata (`sourceDiagnosticCommand`, `sourceDiagnosticExitCode`, `sourceDiagnosticCode`,
`sourceDiagnosticExceptionType`). Inspection metadata is a generated command/exit/code, never
captured stderr. POST land returns additive `requestId` and `notification`.
Repeated inactive pending requests preserve age and identity. `delegate.ps1 -Status`
prints delegate, land, candidate/refusal/execution-failure/inspection, publication/cleanup,
`Landing reason:` from the operation, and receipt separately. A land hold's `Reason:` line
is `holdReasonCode; holder …; holdDetail` — including `git_index_lock_stale` /
`git_index_lock_held` with the lock path and `Remove-Item` in the detail.
`repository_lease_yielded_to_dispatch` (CARD-0672 D-2) means the land stood aside at admission,
before taking the repository mutation lease, because a queued dispatch (or a settlement sync) is
waiting for it; the detail names the waiters' short ids and purposes. It resolves within a land
sweep, sends no caller note, and is a `LandHeld` attention item or a `LandAged` row only once the
yield itself is older than `Delegation:LandWarningSeconds`. A land yields for at most
`Delegation:LandYieldToDispatchMaxSeconds` (default 90, 0 disables) and then proceeds with one
`Warning` "yield budget exhausted". Admission (or a terminal outcome) ends that budget, so a retry
of the same request yields again on a fresh one. Missing optional fields from an
older server print no false failure line. The Attention view
projects held and aged requests and unresolved receipts independently of task openness.

## Post-land verification (CARD-0478)

An implementation card may be Done after ordinary V/R, separate Review, confirmed publication,
a durable linked companion obligation and explicit deployment/acceptance conditions. Its close
reason names C/O/L, Review and pending Mutation; it does not claim PC-clean. The ordinary
same-board companion tracks SourceLanding Mutation independently. No new CardStatus is added,
no successful task automatically closes it, and findings never automatically reopen the original.
Use the explicit commissioning/resumption/triage recipe in [orchestration-loop.md](orchestration-loop.md).
Canceled/superseded verification records reason and successor, never Done/Clean. Decisions belong
on existing move/reopen revisions and attention, never an alert sink.

`POST /api/agent-tasks` accepts optional `repairSourceTaskId` (full GUID), exposed by
`delegate.ps1 -RepairSource`. Only a fresh Worker/Code/Worktree task may carry it. The owner must
be an authorized Code/Worktree task in the same project and git common directory.
`GET /api/agent-tasks/{id}` exposes `repairSourceTaskId` and `progressEvidence`
(`assessment`, `reason`, `sources[].origin` / `ownerTaskId` / `commit`). Land on a repair task
returns 409 `repair_source_landing_owner_required`.
For a successful RepairSource with a Failed original owner, use an owner-bound Clean
Final/Full Review and explicit owner self recovery; the exact command and publication
checks are in [RepairSource succeeded; owner Failed](orchestration-loop.md#repairsource-failed-owner-recovery).
Dispatch refuses a `repairSourceTaskId` whose desktop owner ref is behind origin with
`repair_source_owner_remote_ahead`, or diverged with `repair_source_owner_diverged`. Its failure
reason gives both SHAs and directs a fresh `-Worktree -StartRef <remote-sha>` task to the reviewed
`-Land <owner> -FromTask <repair>` adoption route. An unreachable origin leaves the desktop base
in place and emits an `owner remote unobserved` warning.

`POST /api/agent-tasks` accepts optional `worktreeBaseRequestedRef` (CARD-0613), exposed by
`delegate.ps1 -StartRef`. It is the commit-ish the task's OWN fresh worktree branch is cut at:
a branch, a remote-tracking ref, a commit tag or a SHA that the server's repository can already
resolve locally, or a full 40/64-hex SHA only origin has (no remote URL is accepted) - prefer a
full SHA for a reproducible continuation. CARD-0666: a full SHA missing locally is fetched from
origin once, at create, before the row exists, under the repository mutation lease (purpose
`start-ref-fetch`) and one 30s network deadline shared by the lease wait, the fetch and an
`ls-remote` probe; dispatch never fetches. Each refusal has its own code: 422
`worktree_start_ref_not_full_sha` (a short SHA or name missing locally), `_not_commit`,
`_no_origin`, `_not_on_origin`; 503 `_fetch_failed` (origin unreachable or auth), `_fetch_timeout`,
and `_repository_busy` (another operation held the lease for the whole deadline; nothing was
fetched). It requires an explicitly requested Worktree and is refused 422
`worktree_start_ref_mode` alongside `Shared`/`ReadOnly`/an omitted workspace, an agent pin
(`agentId`/`agent`), `followUpOnTask`, `repairSourceTaskId` or `sourceLandingOperationId` - the
last two already carry authoritative structured bases. Blank, outer whitespace, a control
character, a leading `-` or more than 300 characters is 422 `worktree_start_ref_invalid`; the
value is never truncated or normalized. A selector that names no commit is refused at create; one
that no longer resolves at provisioning (`worktree_base_ref_unresolved`) refuses provisioning and
fails the task, never falling back to master. `GET /api/agent-tasks/{id}` exposes
`worktreeBaseRequestedRef` alongside the recorded `worktreeBaseRef`, `worktreeBaseSource`
(`Explicit`) and `worktreeBaseSha`; the requested value is the caller's ask and the recorded ones
are what provisioning actually used, so a reuse cannot relabel the first decision. StartRef never
sets `mergeTargetRef` and never takes over the named branch, which stays checked out wherever it
already is.

CARD-0442: a new card-bound Worktree selects a clean, quiescent same-card unlanded
commit when one eligible tip exists. `POST /api/agent-tasks` returns a `worktreeBase`
preview before dispatch. `worktreeBaseTask` (task GUID or unique short ID) selects a
specific same-card source; `freshWorktree: true` deliberately starts at the target.
They are exclusive and require a fresh Worktree. Divergent eligible tips produce 409
`worktree_base_ambiguous` with candidate identities; an invalid explicit source
produces 422 `worktree_base_source_invalid`. Dispatch resolves again and records the
actual `worktreeBaseTaskId`, `worktreeBaseBranch`, `worktreeBaseSha` and source in task
detail and the creation event. A pending sibling land continues to hold dispatch.

Same-card base inspection skips Git for non-Succeeded rows unless a row has a pending land
request or was explicitly selected with `-BaseTask`. It reads branch tips and target-contained
branches in batches, then checks checkout safety only for tips that can win. The default
inspection budget is 5 seconds. If dispatch cannot finish an Auto inspection, it blocks a
Review or a task whose create preview selected a source; the block names the previewed
source and explicit retry flags. A fresh Code task previewed at the target retains its
target fallback.

Review-evidence land 409 details name the evidence ID, recorded subject/ref/SHA, required
owner or adoption source/ref/SHA, and the fitting `-FromTask` or `-RecoverReviewedSource`
shape. The same detail appears in asynchronous refusal status. A non-Code Worktree task
that settles without pushed progress succeeds with a caller-visible `progress=none` warning
that names the reason, ref and last confirmed tip when available.

`GET /api/agent-tasks/{id}`'s `progressEvidence.sources[]` adds `origin: "PrimaryAlternate"` and
`observedRef` (CARD-0613). That origin means post-dispatch work was proved in the task's own
registered checkout while it was off its expected ref, detached, or on a branch reset into a
divergent lineage; `observedRef` is the ref it was actually on, or null for a detached HEAD.
An alternate positive prevents a false `CompletedWithoutProgress` settlement and authorizes
nothing else - it never merges the branch back, and landing stays the explicit `-Land` path.

`POST /api/agent-tasks` answers 400 `…could not be converted… Path: $.role` only for a name the served build's enum lacks; on master every scripted role binds (`AgentTaskRoleBindingTests`), so that 400 means the served build predates the value: check `GET /api/version` against HEAD and restart (CARD-0493).

CARD-0544 (dormant: `InterimVerification:Enabled=false`, every card `FullOnly`): `POST /api/agent-tasks`
accepts optional `verificationRound` (`Final` | `Interim`), `verificationSubjectTaskId`,
`verificationBaselineOutcomeId` and `verificationSelection` (`artifactPath`, `artifactCommitSha`,
`section`); `delegate.ps1 -VerificationRound/-VerificationSubject/-VerificationBaselineOutcome/-VerificationSelectionFile`.
New Code/Review tasks carry profile v1 (Final unless Interim is requested and admitted). Interim
admission refuses 409 with `verification_round_role`, `verification_round_invalid`,
`verification_interim_disallowed`, `verification_baseline_invalid`, `verification_selection_invalid`,
`verification_owner_landing` or `verification_backstop_unready`; a queued Interim that loses
eligibility is held, never relaunched differently. An admitted Interim latches the owner
(`requiresFinalVerificationReview`); land of a latched owner then needs a Clean Final/Full profile-v1
Review for the exact SHA (409 `final_verification_review_required` /
`review_verification_scope_ineligible`, also rechecked by a recovered landing). Interim scope never
approves a land. `GET /api/agent-tasks/{id}` exposes `verification` (version, round, subject,
baseline and reviewed SHA, selection, final-review pending, owner latch, hold reason, readiness time);
stage outcomes expose `verificationProfileVersion`, `commissionedRound` and `ordinaryScopeCompleted`.
Review evidence and stage-outcome reads also expose nullable `reviewedSourceClean`. A clean
Review finding may supply it explicitly with `delegate.ps1 -ReviewedSourceClean`; omission
stays null. Evidence-backed land refuses false/null with `review_evidence_source_not_clean`,
including unpublished resume. Check checkpoint `source.json` or schema-2 `report.json` with
`scripts/validate-checkpoint-receipt.ps1` before Review declares true.

`POST /api/agent-tasks` accepts optional `sourceLandingOperationId` (full GUID), exposed by
`delegate.ps1 -SourceLanding`. Only fresh Worker/Mutation/Worktree with a distinct same-board
companion, same authorized repository/project and structured confirmed publication is accepted.
`GET /api/agent-tasks/{id}` exposes sourceLandingOperationId, sourceLandingSha,
verificationCleanupResidue, verificationCleanupSealId, verificationExecutionRevision,
directory/registration/branch removal facts, and typed verificationExecutions
(executionId, session generation, custodyReason, receipt digest, runnerStoreId).
Same-O Queued/Dispatched/Working/Blocked admission is serialized;
conflict includes the existing task ID. Provider/capacity refusal retains the companion; no fallback.

`POST /api/agent-tasks/{id}/cleanup-verification` (no body), or
`delegate.ps1 -CleanupVerification <task-id>`, irreversibly seals terminal sourced task admission
and freezes all accepted generations. It imports exact runner-owned receipt bytes before guarded
removal, returns separate directory/registration/branch facts and residue, and never kills or lands.
Missing/unsupported/unknown custody, live owners, dirty source or unknown outputs retain the tree.
Repeat cleanup through this endpoint after resolving evidence; never force-remove or delete the
external verification/runner ledgers. A restored Failed/Canceled run can clean without changing
its verdict. See [testing-and-build.md](testing-and-build.md#verification-restoration-contract-card-0478).

## Expectation watchdog status (CARD-0650)

`GET /api/expectation-watchdog?boardId=<guid>&skip=0&take=20` requires `boardId`. It returns the
configured directive's enabled/expiry state, reference faults and digest, last successful scan
and error, lane counts, paged episodes, nudge attempt/receipt/answer sequences and timestamps,
operator debt/publish state and audit comment IDs. A task or capability token can read only its
authorized board/project. The response omits the operator conversation address, secrets and
transcript bodies. An absent or stale successful scan is not proof that the minute Hangfire job is
protecting the board. The route is read-only; ACKs come from the standing agent transcript.

## Routing exhaustion at task creation (CARD-1021)

An otherwise admissible walked-list request with no available approved candidate
follows normal runner selection, including kind/global defaults for Any.
`POST /api/agent-tasks` returns **201 Created**, `status=Blocked`, a durable task
ID, routing warning and frozen placement fields. Blocked creation launches no
session. Created/Blocked events and a source-linked parent note commit with the
task; a queued note is not a delivery receipt. Grouped RoutingExhausted/Error
attention exposes task/card/board evidence. Explicit card binding does not force
a column move or invent a dispatch timestamp.

Track the returned task instead of resubmitting the work or inventing a provider
fallback. The approved list can recover the same task on its frozen host and
platform. Explicit `refuseIfExhausted: true` (delegate.ps1 `-RefuseIfExhausted`)
retains 409 `routing_exhausted` with no task inserted. Single-candidate holds, pin
conflicts and independent placement failures, including no eligible required-OS
host, retain their existing refusals. `runner_platform_unavailable` still means
placement failed; exhaustion alone no longer causes it when a suitable host exists.
