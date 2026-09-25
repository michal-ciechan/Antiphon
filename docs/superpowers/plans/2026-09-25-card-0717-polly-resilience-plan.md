# CARD-0717: bounded, allowlisted database and HTTP resilience

Date: 2026-09-25. Stage: Plan. Next stage: Code, **Round 1 only**.

## Contract and evidence

The current card, revision 2, supersedes the original five-minute request: **120 seconds is the hard maximum for one logical retry operation, including its first attempt, subsequent attempts, backoff and verification**. Configuration may shorten this, never lengthen it. Transient failure is necessary but insufficient: the operation must also be safe to replay. Existing delivery, launch, kill, landing and runner-command evidence protocols retain control.

Inspected `origin/master` at `39444f915fc104f71b49e2a7a4d269f08d59e8f1`, fetched on 2026-09-25. Task HEAD was `813ac55169ed5546b75ffaf12e38c4f951674d7b`; the differences were five unrelated test files, with no difference in `server/`, `src/`, or the consulted owner documents. All source locations below refer to that master snapshot, not a running deployment. No production requests were issued other than reading CARD-0717. No code, configuration, tests, or deployment changed during Plan.

Owners: [project conventions](../../project-context.md), [test/checkpoint contract](../../testing-and-build.md), [session invariants](../../session-runtime-invariants.md), [Telegram/gateway](../../telegram.md), [orchestration](../../orchestration-loop.md). This is a delegate's plan, not a request to dispatch subdelegates.

### Ground truth

* `server/Program.cs:104-114` registers scoped `AppDbContext` with `UseNpgsql(connectionString)`, migrations assembly and PostgreSQL 16. There is **no** `EnableRetryOnFailure`, custom execution strategy, explicit `NpgsqlDataSource`, `TransactionScope`, `UseTransaction`, or manually opened EF connection in production server code. Npgsql's normal internal pooling still exists; do not confuse absence of an explicitly registered data source with absence of pooling.
* `server/Program.cs:824-834` explicitly opens an administrative `NpgsqlConnection`, queries `pg_database`, then conditionally executes `CREATE DATABASE`. `Database.Migrate()` at 843 and `DatabaseSeeder.SeedAsync` follow. These are distinct from request-time EF work. `AddNpgSql` at 316 is a health probe; Hangfire uses **InMemory** storage at 751, not PostgreSQL.
* `src/Antiphon.Messaging.Service/Program.cs:16-17` separately registers `MessagingDbContext` with `UseNpgsql`, and runs `MigrateAsync` at startup. Its design-time factory is tooling, not a runtime retry registration. The watchdog's `Ledger` is SQLite, outside the Npgsql scope.
* There are 108 `BeginTransaction` mentions in 46 server files, including two helper declarations; the complete grouped list is below. Many transactions implement claims, attempt identities, receipts and generation fences. A global EF retry switch would change these contracts immediately.
* The HTTP registration and send census below covers the server, runner, messaging service/adapters, and the two auxiliary direct-client hosts. Desktop-to-gateway message delivery uses **Kafka**, not an undiscovered HTTP gateway client. Phone-home registration uses HTTP; its commands, replies, heartbeats and events use WebSockets.
* Existing server telemetry registers OpenTelemetry **traces**, including HTTP instrumentation, at `Program.cs:776-780`. It does not yet subscribe to a resilience metrics meter. Merely creating counters will not make them exported.

| Card premise | Observed implementation | Planning consequence |
|---|---|---|
| Shared DB retry can be added centrally | Scoped EF context, no retry strategy; 46 files initiate transactions | Choose explicit replay units; do not flip a global strategy switch. |
| Runner, phone-home and gateway calls are HTTP | Local runner uses HTTP/SSE; phone-home commands use WebSocket; desktop gateway delivery uses Kafka | Inventory and preserve each transport's recovery owner. |
| All transient errors can be retried | Lost commit/send acknowledgement can hide a completed effect | Safety admission precedes the transient classifier. |
| Standard HTTP handler matches the requested policy | Default classification/method coverage is broader | Override predicates and isolate named read clients. |
| Two minutes is a backoff setting | Existing owners have 3/5/10/15-second limits; bodies and paging outlive a handler send | One inherited total deadline, with narrower owner limits preserved. |

## Decisions

### Database: opt-in Polly units, not global EF retry

Use a DI-owned Polly v8 pipeline for explicitly admitted database units. Keep the normal non-retrying EF strategy. Do not wrap `SaveChangesAsync` globally, add a retrying command interceptor, replay ASP.NET requests, or decorate every application service.

EF's execution strategy is appropriate when every retriable transaction can be run as a whole through `CreateExecutionStrategy().ExecuteAsync(...)`. With user transactions it must replay **the complete unit**, including its begin/query/write/commit sequence; retrying just the failed statement inside an aborted PostgreSQL transaction cannot work. Commit acknowledgement can also be lost after a successful commit. Enabling retries additionally changes query buffering. These tradeoffs are documented in [EF connection resiliency](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency). The [Npgsql retry strategy](https://www.npgsql.org/efcore/misc/other.html) uses `IsTransient`, but that alone does not establish replay safety or the card's strict SQLSTATE policy.

For this repository, changing over 100 transaction boundaries at once is not a safe first round. Explicit Polly units give one retry owner, the same budget/classifier/telemetry as HTTP, and incremental coverage without changing protected transactions. This is an intentional coverage rollout, not a claim that Round 1 covers every database operation.

`DatabaseResilienceExecutor` belongs in `server/Infrastructure/Resilience/`. It opens a fresh async DI scope and `AppDbContext` **inside each attempt**, materializes the complete read/DTO before returning, and disposes the failed scope before any delay. It must not capture an existing context, tracking entries, transaction, stream, or `IQueryable` across attempts. Use EF directly inside the delegate; do not introduce repositories. Pure reads use `AsNoTracking` where applicable. No side effects, service resolution with side effects, event publication or lazy work may escape inside the delegate.

For a later admitted write unit: freeze operation ID, object IDs, intended values, expected concurrency version and timestamps before entering; begin and commit the entire transaction inside each attempt; use a fresh scope each time. A failed/unknown commit first invokes a **read-only verification by that same operation identity** within the original deadline. Confirmed committed returns the recorded result. Authoritatively not committed may replay the same identity. Unknown or conflicting identity returns an explicit uncertain outcome to its owner; it never creates a replacement identity or reruns an external effect. `40001`/`40P01` permit restarting a DB-only transaction only under these rules. Existing outer transactions cannot be joined by the executor: leave their operations unwrapped or refactor the complete DB unit first. Never sleep while holding row/advisory locks.

One integration trap must be tested: the default Npgsql EF strategy wraps transient exceptions in `InvalidOperationException`. [Npgsql 9 source](https://github.com/npgsql/efcore.pg/blob/v9.0.4/src/EFCore.PG/Storage/Internal/NpgsqlExecutionStrategy.cs) shows this behavior. At the database seam allow only the recognized EF wrapper chain (`InvalidOperationException` directly wrapping the approved provider/`DbUpdateException` shape, and `DbUpdateException` wrapping the approved provider exception). The outer type alone is never retryable. Do not scan arbitrary aggregate/inner-exception trees or match exception messages. The real-provider test must exercise the resolved 9.x version; do not rely solely on that reference source or synthetic exception tests.

### Shared implementation and HTTP routing

Create a small `src/Antiphon.Resilience` infrastructure library (`net9.0`) for typed options, strict classifiers, budget/deadline support, retry telemetry and HTTP registration. It has no dependency on server Domain/Application or Npgsql. Keep the Npgsql classifier and EF executor in server Infrastructure. Application/Domain get no Polly types. Bind options in composition roots through `IOptions<T>` and `ValidateOnStart`; no injected `IConfiguration`, static mutable pipeline caches, or application repository wrapper.

Pin `Microsoft.Extensions.Http.Resilience` **10.10.0**, the latest stable verified on 2026-09-25, plus `Microsoft.Extensions.Resilience` 10.10.0 when directly using its APIs. The package supports the repository's net9.0 target and Polly v8; record the resolved Polly versions in Code's report. No preview, `Microsoft.Extensions.Http.Polly`, or v7 policy API. [HTTP package](https://www.nuget.org/packages/Microsoft.Extensions.Http.Resilience/10.10.0), [general package](https://www.nuget.org/packages/Microsoft.Extensions.Resilience/10.10.0).

Use `AddStandardResilienceHandler` on **named read clients**. The standard order is limiter, total timeout, retry, breaker, attempt timeout. Override both retry and breaker `ShouldHandle`; framework defaults are broader than the requested allowlist and retry all methods. No hedging. [Microsoft HTTP resilience guidance](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience).

Keep existing command and event clients unwrapped. Mixed clients (`SessionRunnerHttpClient`, `GitHubService`, `GitHubIssuesTracker`) route their enumerated reads through a named read client obtained from `IHttpClientFactory`; their injected/command client retains existing behavior. This avoids shortening commands to the new attempt timeout or rejecting a kill because the read circuit is open. Retain addresses, authentication, headers and response/error mapping on both paths. Tests must exercise the real DI registration, not just a separately constructed test pipeline.

Round 1 names: `Resilience.RunnerRead`, `Resilience.GitHubRead`, `Resilience.GitHubIssuesRead`, `Resilience.JiraRead`, `Resilience.ProviderProbeRead`, `Resilience.GitConnectivityRead`. Do not add resilience through `ConfigureHttpClientDefaults` or the unnamed `AddHttpClient()`. Keep `SessionRunnerHttpClient.EventStreamClientName` at infinite timeout with its existing idle watchdog.

Method GET is not a sufficient classifier (`Telegram deleteWebhook` is a counterexample). Admission is a fixed operation registry, with method plus operation identity and dependency, set by trusted code, not a caller header. Round 1 permits only the table's GET/HEAD reads. Round 2 may explicitly admit the fixed Linear query POSTs and Slack read-only API POSTs with buffered, replayable content; it must not admit arbitrary GraphQL mutations or arbitrary POSTs. Unknown operation, method, client or classification fails closed to **one attempt**.

## Failure, budget and overload policy

### Exact allowlist

The safety gate and cancellation check run before this classifier. Everything not listed fails fast. Use the same classifier for breaker failure accounting, except the DB breaker counts dependency availability failures and excludes transaction contention (`40001`/`40P01`). A constraint conflict must not open the shared database circuit.

| Outcome | Handling |
|---|---|
| `PostgresException` | Only `08000`, `08001`, `08003`, `08006`, `57P01`, `57P02`, `57P03`, `53300`, `40001`, `40P01`. Match this subclass before `NpgsqlException`. No `08*` wildcard and no implicit union with every provider-transient SQLSTATE. |
| Non-Postgres `NpgsqlException` | Require `IsTransient == true`. The provider's explicit transient contract is the allowed exception type; do not generalize it to naked `IOException`, `Exception` or `DbException`. Log only type and a safe reason, never connection strings. |
| `TimeoutException` | Allowed at an admitted I/O boundary, while caller/total-deadline cancellation has not fired. Not permission to replay a timed-out mutation. |
| HTTP transport `HttpRequestException` | Require a causal `SocketException` with `SocketError` in `ConnectionRefused`, `ConnectionReset`, `ConnectionAborted`, `TimedOut`, `NetworkDown`, `NetworkUnreachable`, `HostDown`, `HostUnreachable`, `TryAgain`. Traverse only the expected transport `IOException` wrapper if present. No other HRE, certificate/TLS/auth/proxy/configuration/protocol failure, `HostNotFound`, `NoData` or arbitrary inner exception. |
| HTTP responses | Only 408, 429, 502, 503, 504. In particular **500 and 501 fail fast** despite standard-handler defaults. A successful HTTP status with malformed JSON or a provider error body is not automatically retriable. |
| `OperationCanceledException`, `TaskCanceledException` | Fail fast, including caller shutdown and total-budget expiry. Do not turn user cancellation into transient failure. |
| Polly `TimeoutRejectedException` | Fail fast in this revision: it is not `System.TimeoutException` in the operator allowlist. Attempt timeout still bounds hanging attempts. Do not silently inherit the standard HTTP predicate which retries it. |
| Everything else | No retry: includes EF optimistic concurrency, unique/FK/check/not-null violations, SQL syntax, authentication/permission, statement cancellation `57014`, lock-not-available `55P03`, unknown SQLSTATEs, broken-circuit and rate-limit rejection. Existing domain duplicate-resolution branches remain separate. |

For listed SQLSTATEs use the exact list as authority, not `IsTransient` OR a wildcard: different Npgsql versions can broaden their SQLSTATE classification. Tests pin both the approved states and representative provider-transient states outside the list. Configuration may **remove** approved entries but not add new ones; widening requires a reviewed code change.

### Time and retry shape

One immutable `ResilienceBudget` captures monotonic start/deadline using injected `TimeProvider`. Default 120 seconds; effective budget is the minimum of that, the configured profile budget, and the owner's existing operation deadline. Pass the linked token through HTTP send, response-body reading/deserialization, database open/query/commit/verification and delay. Pagination and multi-call read operations share their parent's deadline. Do not reset it for fallback, nested calls, commit verification, a new context or a new retry loop.

Default backoff starts at 250 ms, doubles with jitter and caps **the final delay** at 5 seconds. Default `MaxRetryAttempts=6` means at most seven physical attempts; it is an additional ceiling, not a promise to run for two minutes. Configure up to 100 retries only within the same deadline. Use Polly's jitter with a capped delay generator and test its final bounds. No delay or next attempt starts after exhaustion. [Polly retry behavior](https://www.pollydocs.org/strategies/retry.html) explains why a custom delay generator must enforce its own cap.

Honor a valid HTTP `Retry-After` delta/date as a minimum wait, measured against the injected clock. If it exceeds the per-delay cap or the remaining budget, stop and return the terminal response; never shorten it and hit the server early. Malformed headers use the normal jitter schedule. The response being retried must be disposed before waiting. HTTP buffering/replay of approved POST content is bounded; never re-read a non-seekable upload stream automatically.

Attempt timeout is 10 seconds by default, never greater than the remaining budget. A provider call which ignores cancellation cannot be made safely preemptive by Polly; all production async operations must consume the token. Await/dispose the failed attempt before starting another; no detached `WhenAny` task continues to mutate a context. A bounded cancellation-cleanup tail may outlast the deadline, but no retry/work is admitted after it. [Polly timeout contract](https://www.pollydocs.org/strategies/timeout.html).

The outer deadline must cover bodies too: an HTTP delegating handler generally finishes at response headers, before the caller's deserialization. It also covers any limiter wait. Set the standard limiter's queue to zero in this design. Use a separate overall call token, and set read-client `HttpClient.Timeout` consistently (infinite only when the enclosing deadline is guaranteed); otherwise the default 100-second timeout silently defeats a 120-second configured budget. Do not change command or stream timeouts. Preserve runner list's 3-second limit, capability probe's 5 seconds, git connectivity's 10 seconds, and tracker state push's 15 seconds; none becomes a two-minute wait.

### Circuit and observability

Cache pipelines in DI, not per call. Default breaker: 50% qualifying failures, minimum throughput 10, sampling window 30 seconds, break 15 seconds, then a limited half-open probe. DB circuit is keyed by a **logical database name**; HTTP circuit by admitted client/dependency and normalized configured authority. A GitHub outage must not open the runner/Jira circuit. Dynamic provider/git-connectivity targets need a bounded authority registry; do not allocate a permanent circuit per arbitrary URL. Evict inactive dynamic entries (cap 128), never produce unbounded telemetry labels. The protected command path has no read-circuit dependency. [Polly circuit behavior](https://www.pollydocs.org/strategies/circuit-breaker.html).

Jitter spreads callers; the shared breaker prevents most new calls during a sustained outage. A zero-queue limiter caps simultaneous **admitted read attempts**, default HTTP 32 and DB 8 per dependency. Acquiring a DB connection happens inside that limit and each failed context is disposed before backoff. Retain existing database pool limits. Multiple processes retain separate circuits; this is not a distributed breaker and does not require a new coordinator.

Every retry produces one structured Warning with `operation`, logical dependency, 1-based retry number, maximum retries, reason type/code, elapsed/remaining milliseconds and chosen delay. Emit one final exhausted/canceled/failed outcome, and Information on circuit transitions. Do not duplicate every Polly event through a second logger. No exception message/stack dump by default, SQL text, request/response body, raw URL/query, bot token, API key, credentials, session prompt or connection string. Trace correlation may include an existing operation ID, but IDs and hostnames are not metric labels.

Add singleton meter `Antiphon.Resilience`: counters `retry.attempts`, `operations.completed` (outcome label), `circuit.transitions`, `circuit.rejections`, `budget.exhausted`; histograms `retry.delay` and `operation.duration` (seconds). Labels are bounded operation family, logical dependency/profile, and approved reason code. Register `.WithMetrics(...AddMeter("Antiphon.Resilience")...)` with the host's exporter; retain existing trace instrumentation. `MeterListener` tests prove events are emitted once and that sensitive marker strings never enter tags/logs. Breaker-open maps to unavailable/unknown at the existing caller boundary, not an empty successful result or proof that a session disappeared.

### Settings

One `Resilience` section, environment form `Resilience__...`, validated at startup. Defaults are the same across hosts; only the profiles explicitly registered by that host are active. R1 configuration is loaded at startup (restart to apply), avoiding live replacement of a circuit during a failure burst.

| Key | Default / validation |
|---|---|
| `Enabled` | `true`; `false` bypasses new retry/breaker behavior, retaining existing caller timeouts and protocols. |
| `TotalTimeoutSeconds` | `120`; integer 1..120. Above 120 is invalid, not silently accepted. |
| `AttemptTimeoutSeconds` | `10`; positive and <= total. |
| `BaseDelayMilliseconds` | `250`; positive and <= max delay. |
| `MaxDelayMilliseconds` | `5000`; positive and <= total duration. |
| `MaxRetryAttempts` | `6`; 1..100. To disable use `Enabled`, rather than invalid Polly zero-retry options. |
| `UseJitter` | `true`; production registration rejects false (test seams may control randomness). |
| `CircuitBreaker:FailureRatio` | `0.5`; greater than 0 and <=1. |
| `CircuitBreaker:MinimumThroughput` | `10`; >=2. |
| `CircuitBreaker:SamplingDurationSeconds` | `30`; positive and compatible with the attempt timeout's standard-handler validation. |
| `CircuitBreaker:BreakDurationSeconds` | `15`; positive, <=120. No sleeping through an open circuit. |
| `Http:MaxConcurrentAttempts`, `Database:MaxConcurrentAttempts` | `32`, `8`; positive. Queue length stays zero. |
| `Http:AllowedStatusCodes`, `Http:AllowedSocketErrors`, `Database:AllowedSqlStates` | The exact lists above; optional subsets, unknown entries invalid. |
| `Http:HonorRetryAfter` | `true`; final delay/budget rules above still apply. |
| `Http:MaxAuthorityPipelines` | `128`; bounded positive integer. |
| `Profiles:<fixed-name>:TotalTimeoutSeconds`, `...:AttemptTimeoutSeconds`, `...:Enabled` | Optional narrower per-profile values. Names come from code; no configuration can admit a mutation. Parent budget and all existing owner deadlines remain upper bounds. |

## Call-site classification

**S** = safe to retry at the stated boundary. **K** = needs a durable idempotency/commit-verification contract first; no generic retry today. **N** = never generically retried; existing evidence/protocol owns recovery. A row classified S does not mark an enclosing service, workflow or transaction safe. Unlisted new operations are single-attempt until classified. R1/R2 are rollout rounds, not automatic blanket activation.

### HTTP registration and physical operation table

Paths under `server/Infrastructure/` abbreviated `I/`; `server/Application/Services/` abbreviated `A/`; other paths are explicit. Method names identify shared send helpers' callers, so one helper is not mistaken for one kind of effect.

| Site / registration | Physical operation(s) | Class and treatment |
|---|---|---|
| `server/Program.cs:282`; `I/Agents/SessionRunner/SessionRunnerHttpClient.cs` | `GetCapabilitiesAsync`, `GetHealthAsync`, `ListAsync`, `GetAsync`, `GetBufferAsync`, `GetSnapshotAsync`, `GetTranscriptAsync`, `ObserveCompactionAsync`; capability-probe helpers ultimately use these | **S, R1** named runner read client; shorter list/probe budgets preserved; a failed observation is unknown. |
| Same client, `ReadVerificationCustodyAsync` | GET custody (133) versus POST seal (132) | GET **S, R1** with same binding/generation; seal **N**, custody protocol. |
| Same client, `InspectHerdrPaneAsync`, `GetHerdrPaneDisposalAsync`, `GetHerdrPaneDisposalPreviewAsync` | GET inspect/receipt/preview | **S, R1**; repeat observation, never synthesize permission to dispose. |
| Same client, `StartAsync`, `AttachHerdrAsync` | POST sessions / attach | **N**; accepted generation, ownership and launch/reattach protocol (CARD-0679). |
| Same client, `SendInputAsync`, `SendConditionalInputAsync` | Input / conditional maintenance input | **N**; CARD-0693 first-write refund and transcript evidence. No retry-to-raw-input fallback on unsupported conditional input. |
| Same client, `ClearLiveBufferAsync`, `ResizeAsync` | Clear / resize commands | **N**; do not infer retry admission from apparent idempotence. |
| Same client, `KillAsync`, `KillGenerationAsync`, `StopCompactionContinuationAsync` | Stop/kill POSTs | **N**; generation and conditional stop evidence. |
| Same client, `CheckHerdrPlacementAsync` | POST placement check, read-only | **S but R2 explicit operation opt-in**; buffered request, same placement criteria. POST default remains off. |
| Same client, `PreviewHerdrPaneDisposalAsync` | POST allocates a single-consumer preview | **K**; stable preview request identity/result required before replay. |
| Same client, `DisposeHerdrPaneAsync` | POST disposal | **N**; read operation receipt after uncertainty, never generic destructive replay. |
| `server/Program.cs:298`; `StreamEventsAsync` | Named SSE GET /events, `ResponseHeadersRead` | **N** for whole stream; existing reconnect/idle watchdog owns continuation and event sequence. No finite standard handler. |
| `server/Program.cs:697`; `I/GitHub/GitHubService.cs` | GET PR comments (issue and review), combined status/check-runs, PR detail, user connectivity, paged repos, branches, PR-by-branch lookup | **S, R1** named GitHub read client; share operation deadline across pages/subrequests and preserve auth/cache behavior. |
| Same, `CreatePullRequestAsync` | POST /pulls | **K**; repository/head/base plus durable operation record and result reconciliation required first. Checking for a PR once is not an atomic idempotency guarantee. |
| Same, `PushBranchAsync` | `git push` process, not HTTP client | **N**, landing/git protocol; never enclose in a read retry delegate. |
| `server/Program.cs:698`; `I/IssueTrackers/GitHubIssuesTracker.cs` | `FetchCandidatesAsync`, `FetchByStatesAsync`, `FetchByIdsAsync`, `FetchCommentsSinceAsync`: GET via `SendAsync` | **S, R1** named GitHub issues read client; complete paginated operation stays under parent deadline. |
| Same, `PostCommentAsync` | POST comment | **K**; stable publication identity plus provider/result reconciliation. |
| Same, `AddLabelsAsync`, `RemoveLabelAsync`, `ReplaceLabelsAsync`, `SetStateAsync` | POST/DELETE/PATCH labels or state | **K**; repeated values can still overwrite concurrent edits or duplicate notifications. Reconcile desired version/state before admitting; preserve existing 15-second card-push bound. |
| `server/Program.cs:699`; `I/IssueTrackers/LinearTracker.cs` | Fixed GraphQL query POST from all three `Fetch*` methods through `PostGraphQlAsync` | **S, R2** explicit query operation; bounded immutable body; mutations never inherit admission. |
| `server/Program.cs:700`; `I/IssueTrackers/JiraTracker.cs` | All `Fetch*` methods use GET `SearchAsync` | **S, R1** named Jira reads, partition configured authority. |
| `server/Program.cs:758` unnamed factory; `A/ProjectService.cs:347-380` | `TestGitConnectivityAsync` actually sends GET `/info/refs?service=git-upload-pack` despite HEAD comment | **S, R1** named connectivity reads; retain 10-second deadline and per-request credentials. |
| Unnamed factory; `A/LlmProviderService.cs:261-290` | `TestOpenAiAsync` GET models, `TestOllamaAsync` GET tags | **S, R1** named provider probe reads. |
| Same, `TestAnthropicAsync` (253) | POST /messages (billable inference) | **K**; provider-supported idempotency/spend evidence first, one attempt today. |
| Unnamed factory; `I/Agents/AgentDraftGenerator.cs:153,200` | OpenAI-compatible chat completion and Anthropic message POSTs; candidate fallback loop | **K**; logical generation/spend identity first. Do not add generic retries underneath candidate fallback. |
| Factory; `I/Agents/LlmClientFactory.cs:77-95` | OpenAI SDK client; `llm-{provider}` factory clients feed a placeholder that throws without HTTP | SDK inference **K**, placeholder has no I/O to retry. SDK bypasses `IHttpClientFactory`; inventory/disable SDK-owned automatic retries before claiming shared coverage in R3. No assumed transport coverage from unnamed DI registration. |
| `src/Antiphon.SessionRunner/Program.cs:45`; `PhoneHomeConnectionService.cs:98` | Named HTTP POST registration, returns new epoch/ticket | **N**, reconnect protocol. Same boot ID alone is not proof registration is side-effect-free. No standard handler around register/connect loop. |
| `PhoneHomeRunnerClient`, `PhoneHomeLiveConnection`, `PhoneHomeCommandDispatcher` | WebSocket RPC: list/read/auth/custody and launch/input/kill/release/workspace commands; heartbeat/event frames | **N** for generic transport replay, including reads under the existing catch-up pump. Retain request/epoch/generation and before-send versus in-flight outcomes. HTTP pipeline does not intercept these frames. |
| `src/Antiphon.Messaging.Service/Program.cs:27,34` factory `telegram`; `TelegramChannelAdapter.cs:104` | `getUpdates` long poll with offset | **N** for nested HTTP retry; existing ingress/offset loop owns polling. Budget an individual poll, not the lifetime stream. |
| Same adapter, `HydrateAttachmentsAsync` flow (201,223) | GET getFile, GET file bytes | **S, R2** named attachment reads; use download/body budget and redact token-bearing URI. |
| Same adapter (589,494) | POST sendMessage, sendDocument; HTML-to-plain fallback and send loops | **N**; provider delivery protocol, no reliable generic duplicate suppression. R2 tightens existing owner-loop classification/budget, never layers a handler. Unknown acceptance remains unknown. |
| Same adapter (675) | **GET deleteWebhook** | **N**, mutation despite GET; startup protocol only. |
| `src/Antiphon.Messaging.Service/Program.cs:45` factory `slack`; `SlackChannelAdapter.cs` | `auth.test`, `users.info`, `conversations.info` POST through `SendApiAsync`; GET attachment bytes (536) | **S, R2** fixed read-operation opt-in, replayable small bodies; ordinary POST remains off. |
| Same, `OpenSocketUrlAsync` | POST apps.connections.open then WebSocket connect/ack | **N**, socket reconnect/identity protocol. |
| Same, `SendTextAsync`, `SendPostMessageWithRetryAsync`, `SendSourceAsLinkAsync` | POST chat.postMessage | **N**; owner-loop delivery evidence and uncertainty; R2 narrows existing loop, no generic layer. |
| Same, `TryUploadOnceAsync` | POST files.getUploadURLExternal, upload URL, files.completeUploadExternal | **N** for whole workflow; reservation/file-ID/complete-result protocol required, no restarting upload sequence after ambiguous completion. |
| `src/Antiphon.Messaging.Gateway/HttpAppHostHealthProbe.cs:13,21` | Static direct HTTP GET /health | **S, R2** migrate to factory; maximum existing 3-second observation budget. Not evidence for message receipt or retrying a send. |
| `Antiphon.AppHost/Supervisor/DaemonProcessService.cs:117,197` | Direct HttpClient health probes, 5-second timeout | **S but auxiliary follow-up**, outside server rollout; supervisor's cadence/health verdict must not be hidden behind long retries. No change in R1/R2. |
| `src/Antiphon.NightlyWatchdog/Program.cs:65`; `WindmillHttpApi.cs:108` | Direct client GET health/schedule/workers/jobs/result/logs | **S but auxiliary follow-up**, retain configured watchdog probe deadline. Separate executable, no server DI coverage claim. |
| Same client; `TelegramBotTransport.cs:45` | POST watchdog notification | **N**; watchdog ledger/qualification/retry policy owns delivery. SQLite ledger is outside Npgsql policy. |

### Database boundaries and startup

| Boundary | Classification and round |
|---|---|
| `A/LlmProviderService.GetAllAsync/GetByIdAsync`, `A/WorkflowTemplateService.GetAllAsync/GetByIdAsync` | **S, R1**: four materialized DTO reads, fresh context each attempt. No mutation methods on those services are wrapped. |
| Other independent EF queries materialized without locks, transaction or external effects | **S only after extraction/admission in R2**. Use the companion source census to account for every site; it is not safe to decorate the parent service or replay already-streamed results. |
| `PendingRunnerSessionInventory.Read` | **N as presently shaped**: synchronous loader under its single-flight lock, five-second failure throttle; do not add two-minute sleeps under that lock. A later asynchronous redesign belongs with CARD-0701. |
| `SaveChanges[Async]`, `ExecuteUpdate[Async]`, `ExecuteDelete[Async]`, raw DML and create/commit operations outside the explicit table | **K** unless already inside an N protocol. Stable ID alone is insufficient; prove commit result and concurrency semantics before admission. Never automatically retry an affected-row CAS to turn a conflict into success. |
| `FOR UPDATE`, advisory locks, `SET LOCAL` and raw SQL helpers | **N in isolation**. Retry only an admitted complete DB transaction; never retry a locking query independently of the owner claim. |
| `Program.cs` admin open + SELECT pg_database | **S, R2** fresh explicit connection per read attempt, fixed admin-probe budget. |
| Conditional CREATE DATABASE; server/gateway migrations and seeding | **N** generically. Existing startup flow/recovery; a partially completed DDL batch needs migration history/existence reconciliation, not replay of the startup delegate. Convert sync migration only in separately scoped startup work, not as an incidental retry change. |
| Npgsql health check | **N** for hidden retries; liveness/readiness must answer its existing probe timeout. An outage must remain observable. |
| Gateway `EfInboxReceiptStore.GetOverdueAsync`, inbox list/detail GET queries | **S, R2** extracted materialized reads; fresh `MessagingDbContext`. |
| Gateway receipt `RecordAsync`, MarkAcknowledged/MarkOperationalEventPublished/ScheduleAckRetry, InboxConsumer persistence | **K**; existing unique-message handling is domain deduplication, not a general unknown-commit proof. Freeze receipt identity and monotonic update intent before admission. |
| Gateway reply endpoint (Kafka produce followed by SaveChanges) and outbound consumer | **N** whole operation; producing again is an external duplicate. An outbox/idempotent publication protocol is a separate prerequisite. |

The [database source census](2026-09-25-card-0717-database-call-sites.md) lists all server/gateway files with recognized EF execution sites and their exact line numbers. Its conservative K/N baseline closes the inventory without falsely marking every query in a mutating service safe. Explicit S entries above are the only initial opt-ins. Before widening coverage, update that census with the extracted unit and round/test IDs. A read before an effect is not automatically a retryable unit; admission must keep a consistent authorization/claim decision.

### User-transaction census

All paths below are under `server/`; `A/` is `Application/Services/`, `D/` is `Infrastructure/Data/`. Each listed location is classified; helper declarations are noted. **No row is enabled in R1.** K rows require an identity plus commit-verification design before R3 can admit their DB-only portion. N rows continue through their named owner protocol even where that protocol has a DB-only phase.

| File | BeginTransaction locations | Classification / owner boundary |
|---|---|---|
| `A/AgentControlService.cs` | 524, 1117 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/AgentPinnedInstructionService.cs` | 85, 216, 287 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/AgentService.cs` | 597, 797, 889, 941, 995 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/AgentSessionRuntime.cs` | 193 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/AgentSessionService.cs` | 379 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/AgentSupervisorService.cs` | 127 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/AgentTaskDispatcher.cs` | 4059, 4791 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/AgentTaskLandMonitorService.cs` | 21, 55 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/AgentTaskLandRequestWriter.cs` | 113, 140 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/AgentTaskLandService.cs` | 80, 198, 271, 350, 448, 612, 701, 741, 819, 862, 1090, 1108, 1205 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/AgentTaskLandingProtocol.cs` | 427, 627, 649 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/AgentTaskService.cs` | 1332 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/AgentTuiProfileImporter.cs` | 75, 307, 311 | **K** — Freeze import/revision identities; existing conflict loop owns import until whole-unit commit verification exists. Helper declaration at 307 is included. |
| `A/AgentTuiProfileService.cs` | 282, 349, 443, 520, 614, 703, 1413, 1527, 2399, 2403 | **K** — Profile/revision and operation-run identity plus expected version; helper declaration at 2399 is included. |
| `A/AgentTuiSecretMigrator.cs` | 63 | **K** — Freeze protected-secret migration intent; verify stored result without reprotecting/replacing secrets on replay. |
| `A/ApiErrorRecoveryService.cs` | 101 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/BoardService.cs` | 192 | **K** — DB cascade delete only; stable deletion result and external card-file/event boundary first. |
| `A/CapacityRecoveryService.cs` | 137, 247, 344, 402, 456, 644, 1178 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/CardService.cs` | 581, 740 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/ChannelBridgeService.cs` | 140 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/DataRetentionService.cs` | 313 | **K** — Freeze eligible IDs/cutoff and verify completed batch/counts; never recompute a broader delete set on retry. |
| `A/DispatchBaseWarningIntentService.cs` | 85, 179 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/ExpectationLedger.cs` | 95, 150, 269, 397, 465, 517 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/GrokRulesRefreshService.cs` | 183, 237 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/HerdrLabelFollowService.cs` | 61 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/HerdrSupervisionStateService.cs` | 24 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/OrchestratorService.cs` | 665 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/ProjectService.cs` | 217 | **K** — DB cascade only; keep file/runner/event work outside and verify the intended delete result. |
| `A/ProjectSetupService.cs` | 290 | **K** — Project/board/agent creation identities and postcommit effects require a durable setup operation. |
| `A/RunnerSlotService.cs` | 251, 324 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/ScheduleService.cs` | 141 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/SessionMessageQueueService.CheckPublication.cs` | 21 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/SessionMessageQueueService.Expectations.cs` | 350 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/SessionMessageQueueService.cs` | 654, 1542, 2185 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/SessionReconciliationService.cs` | 185, 588 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/SpecialistRequestService.Qualification.cs` | 42, 92 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/SpecialistRequestService.cs` | 29, 181, 295, 435, 453 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/StandingSpecialistHealthService.cs` | 26 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/StandingSpecialistRoutingService.cs` | 32, 139 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/StandingSpecialistSeatService.cs` | 52 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/TaskWorktreeRetirementService.cs` | 86, 189 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/VerificationCleanupService.cs` | 23, 71 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `A/VerificationExecutionService.cs` | 72 | **N** — Existing claim/attempt/generation/receipt protocol; replay only through that owner, never the enclosing operation. |
| `D/RetirementCommandJournal.cs` | 12 | **N** — Command intent ID/retirement evidence; journal owns reread and command authority. |
| `D/WorkspaceReservationJournal.cs` | 48, 84 | **N** — Reservation/retirement generation and ownership protocol. |
| `D/WorktreeRemovalEvidence.cs` | 175 | **N** — Verification seal and removal evidence protocol; no filesystem work in retry closure. |

## Existing retry owners: consolidate without multiplication

| Existing owner | Finding and disposition |
|---|---|
| `AgentTuiProfileImporter.ImportAsync:43` | Bounded concurrency loop reuses/clears context, catches unique/serialization/deadlock failures. R3 replaces only transient DB handling with fresh-scope whole-unit execution after frozen import identity; unique collision remains domain reconciliation, never global 23505 retry. |
| `AgentTuiOperationCoordinator.RecoverAsync:139` | Three finalization attempts with a shared recovery deadline, currently catches every exception. R3 narrows to approved DB failures and the same existing deadline; run ID/terminal-state verification must precede replay. Do not add a second retry layer. |
| `ExpectationLedger:93,148`, `AgentService:428`, `RetryScheduler.GetOrCreateScheduleAsync` | Unique-row/name races with explicit reread or a different generated name. These are domain conflict resolution, not transient transport retries. Preserve them as N owners; do not count 23505 as transient. |
| `WorktreeCleanupJournal.UpdateAsync:161`, initial-create uncertainty path | Catches uncertainty and reads whether the intent committed. This is evidence verification, not blanket retry. Keep its read-result verdict and exact command IDs. |
| `AgentSessionService:488,1180`, runner adapters; `SessionMessageQueueService` and `VerifiedPromptSubmitter`/`EchoGatedLineSender`/`CodexSubmitConfirmation` | Generation-aware reattach/resume and evidence-aware delivery; no generic wrapping. First-write refusal can refund only its matching attempt; lost Enter or in-flight body cannot. |
| `SessionMessageQueueService:1585` | `57014` is an intentional bounded claim/lock-wait path. Preserve domain handling; do not put it in the retry allowlist. |
| `PhoneHomeConnectionService:53-73`, `PhoneHomeRecoveryPump:113-122`, SSE event pumps | Lifecycle reconnect/catch-up schedules, not replay of one HTTP operation. R2 narrows the HTTP registration-failure branch so permanent auth/config/protocol errors do not spin as transient; socket recovery keeps its epoch/request protocol. Never add an inner retry budget to each iteration. |
| Telegram sendMessage/sendDocument loops (438,474), Slack message/upload/link loops (698,766,865) | Existing broad exceptions and all-5xx treatment exceed the new policy. R2 shares strict transient classification and one logical-send deadline, but retries only where provider evidence proves nonacceptance (e.g. structured rate-limit refusal). Transport loss, timeout or ambiguous 502/503/504 after submission cannot authorize duplicate sends. Formatting rejection can allow plain fallback, under the **same** deadline, without a fresh retry budget. |
| Telegram receive, Slack socket receive, gateway ingress restart | Long-lived polling/reconnect supervision. Do not wrap an endless enumerable in a 120-second policy. Narrow permanent failure handling in its owner; each admitted HTTP read has a bounded child budget. |
| `RemoteWorkspacePreparer.Backoff`, `RemoteWorkspaceService` lease wait, `LandingGit` CAS loops (474,517), `TaskProgressGit:85`, guarded worktree deletion | Repository leases/CAS/filesystem evidence protocols, not generic database/HTTP retries. Preserve N. |
| `RetryScheduler`, `ApiErrorRetrySchedule`, supervision/restart policies, land sweeps, tracker sync | Durable business schedules remain the owner of later attempts. The 120-second cap applies to one admitted I/O operation, not all future independent observations. Never relabel a continuation, new model candidate or new job as a retry solely to bypass a deadline. |
| `GitHubRepoCacheWarmupService` | Failure logs then later request tries again; not an inner retry loop. New read pipeline replaces transport retry responsibility, preserving cache fill only on success. |
| Watchdog `RetryPolicy`, gateway receipt acknowledgement scheduling, SDK retry defaults | Separate owners. Watchdog not changed here; gateway/SDK require explicit R2/R3 reconciliation and no nested automatic retries before claiming complete coverage. |

The broad send/finalization loops above are concrete migration work in this plan, not permission to preserve catch-all retry in a newly covered boundary. Existing evidence protocols may catch broad exceptions **to record uncertainty**, which does not make those exceptions retryable.

## Rounds and implementation slices

### Round 1 — safest broad win (next Code dispatch)

**S1:** Add the shared library, validated settings, budget and strict HTTP predicate, bounded pipeline registry, telemetry, server Npgsql predicate and read executor. Add named read registrations to `server/Program.cs`; pin the packages and add the project reference/solution entry. Keep production connection string, pooling, EF strategy and all transaction behavior unchanged. There is no new schema or migration.

**S2:** Route only R1 S HTTP rows through the read clients and the four specified DTO reads through the DB executor. Keep all command/SSE/unnamed SDK paths as they are. Preserve error/null/unknown mapping when the new pipeline returns exhausted, timed-out or circuit-open. Make the deadline span response bodies and combined read operations. A client-factory/registration test must prove writes still use the original path.

**S3:** Complete real-Postgres and fake-handler red/green evidence below; document configuration and the active/deferred census in `docs/project-context.md` (configuration conventions) and a focused `docs/resilience.md` owner linked from the index. Run CP-1 through CP-3. Report Round 1 complete, CARD-0717 still open for remaining rounds; do not describe unconverted DB calls as protected.

| Slice | Concrete production footprint | Test footprint |
|---|---|---|
| S1 | New `src/Antiphon.Resilience/{Antiphon.Resilience.csproj,ResilienceSettings.cs,ResilienceBudget.cs,HttpTransientFailureClassifier.cs,ResilienceTelemetry.cs,ResilienceRegistration.cs}`; new `server/Infrastructure/Resilience/{NpgsqlTransientFailureClassifier.cs,DatabaseResilienceExecutor.cs}`; `Antiphon.sln`, `server/Antiphon.Server.csproj`, `server/Program.cs`, documented defaults in `server/appsettings.json` | V-1 through V-5 classes below; bounded dynamic-authority lifecycle belongs in the circuit tests. |
| S2 | `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs`, `server/Infrastructure/GitHub/GitHubService.cs`, `server/Infrastructure/IssueTrackers/{GitHubIssuesTracker.cs,JiraTracker.cs}`, `server/Application/Services/{ProjectService.cs,LlmProviderService.cs,WorkflowTemplateService.cs}` | V-6/V-7 and named R-1/R-2 regressions. Do not change `LinearTracker` or messaging code in R1. |
| S3 | `docs/resilience.md`, its owner link in `AGENTS.md`, `docs/project-context.md`, update this census with implementation evidence | Complete V-1–V-7 and CP-1–CP-3; no extra broad build/test lane. |

### Round 2 — remaining safe reads and existing HTTP loops

**S4:** Admit Linear's fixed queries, placement-check POST, Slack's fixed read-only API calls, Telegram metadata/downloads, gateway health and inbox reads, admin database existence probe. Add runner/gateway references to the shared library as actually needed, preserving their separate process configuration. Extract additional independent read units from the census by cohesive feature (settings/catalog, board/project/card listing, diagnostic projections), never a global EF interceptor. Operations in claim transactions or the synchronous inventory lock stay N. Each extraction gets an explicit method row and affected integration-class filter before Code dispatch.

**S5:** Replace overlapping provider send-loop mechanics with the common deadline/classifier where **owner evidence** allows another attempt. No generic retries for uncertain sends, registration, commands, or uploads. Preserve provider structured errors and the HTML fallback's definitive-rejection semantics. Classify permanent phone-home registration/auth errors separately from reconnectable transport loss without replaying the command stream. No live broker/provider credentials in tests.

Round 2 must be refined into its own committed checkpoint manifest when S4's concrete read roster is selected against then-current master. This is deliberate staged planning: the following R1 manifest does not authorize hundreds of inferred read edits. Required R2 evidence: fixed-query POST 503 recovery; GraphQL mutation/Telegram deleteWebhook exclusion; gateway read transient recovery; HTTP registration auth fail-fast; dropped send response produces one external send; rate-limit refusal respects one budget; formatting fallback shares elapsed budget; streams reconnect with their existing cursor/epoch.

### Round 3 — durable write identities and completion of coverage

**S6:** Work K rows by feature, beginning with DB-only TUI import/finalization and receipt recording. Write down uniqueness, expected-version semantics, replay result and unknown-commit verifier before enabling the shared executor. Any needed migration is created using `dotnet tool restore` then `dotnet ef migrations add ... --project server`; no hand-written migration generation. Existing land, kill, launch, message and runner-command protocols remain N. Their isolated database evidence reads may be separately admitted, but their mutation closures never enter the generic pipeline.

**S7:** Resolve tracker/PR/comment/LLM K contracts (provider idempotency or durable publication/reconciliation), and the SDK's internal retries. If a provider lacks a reliable acknowledgement/idempotency mechanism, settle that row N with the uncertainty behavior documented; do not manufacture an idempotency header that the server ignores. Account for every census row as admitted S, identity-protected K promoted with proof, or explicit N protocol. Auxiliary AppHost/watchdog work is tracked separately if wanted; it is not silently represented as part of server coverage.

R3 needs its own feature-specific Plan/TestDesign checkpoints before Code. Required evidence includes lost-commit acknowledgement with one stored effect, serialization rollback/replay of the complete unit, conflict fail-fast, no duplicate external send from an after-commit fault, same operation ID across recovery, and cancellation during verification preventing replay. CARD-0717 closes only after its server/runner/gateway rows have an explicit final disposition and ordinary Review evidence; R1 alone does not satisfy the original breadth.

No additional operator approval is required for the requested retry policy or these safety exclusions. Subsequent rounds require concrete technical manifests, not another go-ahead on the already-authorized work.

## Verification design

Use `FakeTimeProvider` through the actual Polly registrations and executor. Scripted HTTP handlers count physical sends, capture disposal, tokens and immutable request identities, and block until canceled where necessary. Never wait two real minutes. Every new test must have an outcome assertion that fails when its guarded production predicate/registration/deadline is removed or broadened. A test of a duplicate model of the policy does not qualify.

The following is the **closed Round 1 ordinary Code/Review scope**. All new classes live under `tests/Antiphon.Tests/Infrastructure/Resilience/`, in `Antiphon.Tests.Infrastructure.Resilience`; unit classes carry `Category("Unit")`. Counts below are minimum non-parameterized test methods to author, not counts of internal assertions. Parameter expansion may increase execution counts; report actual TRX counts.

| ID | Class, minimum methods | Required observable behavior / red control |
|---|---|---|
| V-1 | `ResiliencePredicateTests`, 8 | Separate positive/negative methods for exact SQLSTATEs, non-Postgres Npgsql IsTransient, known EF wrapper, unapproved wrappers/permanent constraints, HTTP five statuses, socket allowlist, TLS/DNS/unclassified HRE, cancellation/TimeoutRejected. Table-driven assertions within these methods exercise every approved and representative denied code. Broaden to all 5xx/all HRE or remove cancellation check: relevant method fails. |
| V-2 | `HttpReadResilienceTests`, 8 | 503 then 200 performs two sends and measured delayed retry; nonallowlisted 500 sends once; 429 Retry-After delay; oversized hint terminates without early retry; failed response disposed; immutable body for any admitted read helper; backoff cap/jitter bounds across multiple operations; original response/exception surfaced after exhaustion. Remove registration/retry predicate: recovery fails. |
| V-3 | `ResilienceBudgetTests`, 7 | Persistent 503 uses a high retry count and fake clock to hit exactly the configured 120-second **total** boundary; slow first attempt consumes budget; body read included; caller cancellation during delay sends no more requests; narrower 3/5/10-second parent wins; nested/pages share one deadline; hanging attempt honors attempt timeout with no overlapping physical work. Remove total timeout or reset deadline: timeout test fails. |
| V-4 | `ResilienceCircuitTests`, 5 | Shared circuit opens after configured qualifying throughput; open circuit sends zero requests; only bounded half-open probes and recovery; other dependency remains callable; non-transient/CAS errors do not count. Include zero-queue limiter rejection/no retry and DB pool-context disposal in these scenarios. Replace singleton pipeline with per-call build: open-circuit test fails. |
| V-5 | `ResilienceOptionsAndTelemetryTests`, 5 | Valid defaults; >120/unknown codes/invalid timing rejected; disable retains single attempt; retry+terminal+circuit counters/log once with bounded labels; sensitive marker strings absent in tags/logs. Remove export meter registration: composition/meter test fails. |
| V-6 | `HttpResilienceRegistrationTests`, 6 | Actual service registrations route runner/GitHub/GitHubIssues/Jira/probe reads into handlers; commands remain original even when read breaker is open; unnamed and event clients have no standard handler; existing address/auth are preserved; shorter timeouts retained; unknown operation/method cannot opt in. Fake handler accepts a protected command's effect then drops its response: one send, owner sees uncertainty. Test Start/Input/Kill/Attach/dispose/seal and tracker mutations as explicit cases, not source-string checks. |
| V-7 | `DatabaseReadResilienceTests`, 7 | Real isolated PostgreSQL + injected one-shot command/open fault in the actual read executor: transient read succeeds on a new context; actual EF wrapper handled; non-transient constraint/syntax path once; context disposed before retry; both services' four R1 getter paths exercised; active-transaction/non-read admission rejected; cancellation/total deadline produces no new DB command. Use fixture-owned rows/IDs. The successful read must hit PostgreSQL after fault removal. |
| R-1 | Existing `GitHubIssuesTrackerWriteTests`, `IssueTrackerAdapterTests` | Preserve tracker request shape/auth/parser/state behavior; all listed classes execute, zero failures. |
| R-2 | Existing `SessionRunnerGenerationWireTests`, `SessionRunnerHttpClientHerdrWireTests`, `SessionRunnerCapabilityGateTests`, `SessionMessageQueuePhoneHomeDropTests` | Preserve generations, conditional/input/refusal protocol and before-write versus in-flight recovery. These are bounded handler/protocol regressions, not an instruction to launch live runners. |

V-7 also contains a **test-only** DB unit showing a transaction rollback and complete fresh-scope replay after `40001`, plus a counter proving an external-effect delegate is never admitted. It is a contract test for the executor boundary, not rollout of a production write wrapper. If R1 exposes only reads, keep transactional execution as a test harness pattern, not an unused public write API; production write admission remains R3.

Red evidence is scoped: in an isolated temporary verification source/output, disable only the relevant production registration/predicate/budget and run the owning exact test method. Assert the intended assertion failure, restore, then run the normal checkpoint on clean source. Such positive controls are additional test runs: report their exact method/count/reason separately, as required by the checkpoint owner. No requirement for a broad mutation suite in Code, and no full-suite/namespace test run for this card.

All R1 implementation slices are committed before CP-1, allowing CP-2/CP-3 to reuse that exact build. Run sequentially. Use `pwsh -NoProfile -File scripts/run-checkpoint.ps1`; it supplies Linux `UseAppHost=false`. Set `TUNIT_MAX_PARALLEL_TESTS=1` for the Postgres row. Real Program boots use the existing `ProductionRunnerGuard`/refusing runner; never connect tests to production 17204. No client/E2E/native process changes require their suites. This plan itself ran **0 tests and 0 builds** because it changes documentation only.

Example CP-1 command (use each table row's exact filter, Min and comma-separated Expect class list):

```powershell
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.Tests -OutputPath bin-card0717-r1/ -Filter '/*/*/(ResiliencePredicateTests*)|(HttpReadResilienceTests*)|(ResilienceBudgetTests*)|(ResilienceCircuitTests*)|(ResilienceOptionsAndTelemetryTests*)/*' -MinExecuted 33 -Expect ResiliencePredicateTests,HttpReadResilienceTests,ResilienceBudgetTests,ResilienceCircuitTests,ResilienceOptionsAndTelemetryTests -ResultsRoot .antiphon/card0717-r1-checkpoints
```

CP-2/CP-3 add `-NoBuild` and retain the same output path. Use fresh results roots for reruns. Every row reports `CHECKPOINT CP-n commit=<sha> build=<ok|reused|failed> filter=<filter> executed=N passed=N failed=N skipped=N trx=<path>` and `reruns=k` when applicable. Report any unlisted build/test command and its reason; no passing count may be inferred from a zero exit or assertion count.

### Cost

R1 ordinary verification floor: **26 minutes** (12 + 8 + 6), plus estimated 90–150 minutes authoring. Use roughly 120–180 minutes for a Code dispatch, calibrated to runner load. Package restore or a known environment failure is reported separately, not a reason to broaden the selection. R2/R3 cost follows their committed manifests; no estimate presented as a test count.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | R1 S1-S3 | `tests/Antiphon.Tests -> bin-card0717-r1/` | policy-unit | `/*/*/(ResiliencePredicateTests*)\|(HttpReadResilienceTests*)\|(ResilienceBudgetTests*)\|(ResilienceCircuitTests*)\|(ResilienceOptionsAndTelemetryTests*)/*` | V-1–V-5 | All five classes, >=33 executed, 0 failed/skipped | 33 | 12 |
| CP-2 | R1 S1-S3 | CP-1 | wiring-postgres | `/*/*/(HttpResilienceRegistrationTests*)\|(DatabaseReadResilienceTests*)/*` | V-6–V-7 | Both classes, >=13 executed, 0 failed/skipped | 13 | 8 |
| CP-3 | R1 S1-S3 | CP-1 | affected-regression | `/*/*/(GitHubIssuesTrackerWriteTests*)\|(IssueTrackerAdapterTests*)\|(SessionRunnerGenerationWireTests*)\|(SessionRunnerHttpClientHerdrWireTests*)\|(SessionRunnerCapabilityGateTests*)\|(SessionMessageQueuePhoneHomeDropTests*)/*` | R-1–R-2 | All six classes, >=6 executed, 0 failed/skipped; actual expanded count reported | 6 | 6 |

The backslashes before table pipes are Markdown escaping only; actual TUnit arguments use ordinary `|` as in the CP-1 command. Later rounds require their own exact method/class roster and checkpoint rows before dispatch; they are not silently appended to this closed R1 list.
