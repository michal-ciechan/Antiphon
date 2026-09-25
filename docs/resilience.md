# Admitted read resilience

Owner for the Round 1 Polly pipelines on admitted HTTP reads and four database DTO reads (CARD-0717). This is not a global retry policy. Delivery, launch, kill, landing, and runner commands keep their existing evidence protocols.

Packages: `Microsoft.Extensions.Http.Resilience` 10.10.0 and `Microsoft.Extensions.Resilience` 10.10.0 (Polly v8). Do not add `Microsoft.Extensions.Http.Polly` or a v7 policy. The library is `src/Antiphon.Resilience` (no Domain, Application, or Npgsql types). PostgreSQL classification and the database executor live in `server/Infrastructure/Resilience/`.

## What Round 1 retries

Named read clients, registered one by one (not `ConfigureHttpClientDefaults`, not an unnamed `AddHttpClient()`):

| Client | Admitted GETs |
|---|---|
| `Resilience.RunnerRead` | capabilities, health, list, get, buffer, snapshot, transcript, compaction observation, verification-custody GET, Herdr pane inspect, disposal GET, disposal-preview GET |
| `Resilience.GitHubRead` | comments, combined status, check-runs, pull-request detail, user, paged repositories, branches, pull request by branch. Pages share one budget. |
| `Resilience.GitHubIssuesRead` | issue-tracker `Fetch*` GETs |
| `Resilience.JiraRead` | `Fetch*` search GETs, partitioned by configured authority |
| `Resilience.ProviderProbeRead` | OpenAI and Ollama model-list probes |
| `Resilience.GitConnectivityRead` | `GET /info/refs?service=git-upload-pack` |

Database, fresh scope per attempt, `AsNoTracking`, materialized before return:

- `LlmProviderService.GetAllAsync` and `GetByIdAsync`
- `WorkflowTemplateService.GetAllAsync` and `GetByIdAsync`

Admission is a code registry stamped on `HttpRequestMessage.Options`. An unknown operation or method fails closed to one attempt. There is no caller header that opts a request in.

## What stays a single attempt

Runner commands stay on the typed client with no standard handler: start, input, conditional input, kill, kill-generation, stop-compaction, attach, clear, resize, placement check, disposal preview POST, dispose, and verification-custody seal. Disposal still asks the read client whether the runner advertises the capability; an open read circuit is "no evidence" and does not become a command POST.

Also unchanged: GitHub pull-request creation and git push, GitHub issue and Jira mutations, the Anthropic probe POST, the tracker card-push 15-second bound, phone-home and SSE, Linear, and messaging. `SessionRunnerHttpClient.EventStreamClientName` keeps its infinite timeout and idle watchdog. EF Core `EnableRetryOnFailure` stays off. `SaveChanges` is not wrapped.

The other database call sites in [the census](superpowers/plans/2026-09-25-card-0717-database-call-sites.md) are not protected. Rounds 2 and 3 are still open.

## Budget, attempts, and allowlists

One logical read has a hard total of 120 seconds, including the first attempt, retries, backoff, and verification. Configuration may shorten that budget and may remove allowlist entries. It cannot lengthen the budget past 120 seconds or add statuses, socket errors, or SQLSTATE codes. `UseJitter: false` is invalid. Tests substitute `IResilienceJitter`; they do not turn jitter off.

Defaults in `server/appsettings.json` (`Resilience`, environment `Resilience__`): total 120s, attempt 10s, base delay 250ms, max delay 5s, 6 retries (at most 7 physical attempts), decorrelated jitter, circuit failure ratio 0.5, minimum throughput 10, sampling 30s, break 15s. HTTP permits 32 concurrent attempts per dependency. Database permits 8 per logical database. The limiter queue is zero. Sampling must be at least twice the attempt timeout.

Owner caps that configuration cannot widen: `runner-list` 3s (also `SessionRunner:ListTimeoutSeconds`, default 3), `runner-capability` 5s on the capability probe only, `git-connectivity` 10s. Unknown profile names are invalid. A shared `ResilienceBudget` covers a paged read; later pages do not get a fresh 120s.

Retryable HTTP outcomes are only 408, 429, 502, 503, and 504, plus `HttpRequestException` whose cause is one `SocketException` (optionally under a single `IOException`) with `SocketError` in ConnectionRefused, ConnectionReset, ConnectionAborted, TimedOut, NetworkDown, NetworkUnreachable, HostDown, HostUnreachable, TryAgain. 500 and 501 fail fast. TLS, DNS, auth, and a deeper wrap fail fast. `OperationCanceledException`, `TaskCanceledException`, and Polly `TimeoutRejectedException` fail fast. The pipeline does not use the framework HTTP predicate, which would retry those.

A valid `Retry-After` delta or HTTP-date is the wait when it fits under the max delay and the remaining budget. A longer hint returns the terminal response. Malformed headers use the jitter schedule. The failed response is disposed before the wait.

Retryable PostgreSQL outcomes are only SQLSTATE `08000`, `08001`, `08003`, `08006`, `57P01`, `57P02`, `57P03`, `53300`, `40001`, and `40P01`, matched on `PostgresException` before `NpgsqlException`. `IsTransient` is not an OR for those. A non-Postgres `NpgsqlException` retries only when `IsTransient` is true. `TimeoutException` retries only while the caller has not canceled. The recognized EF wrapper is one `InvalidOperationException` around the provider exception or a `DbUpdateException`, and one `DbUpdateException` around the provider exception. The outer type alone is not retryable. The database breaker counts availability failures and excludes `40001` and `40P01`, so a constraint or serialization conflict does not open the circuit.

Strategy order, outer to inner, with no hedging: rate limiter, total timeout, retry, circuit breaker, attempt timeout. Pipelines are cached in a singleton `ResiliencePipelineCache`. Provider-probe, git-connectivity, Jira, and GitHub-issues authorities are an LRU of `Http:MaxAuthorityPipelines` (default 128); an evicted authority does not leave its circuit for the next host. Fixed runner and GitHub pipelines are not in that LRU. Hostnames are not metric labels.

When `Enabled` is true, each named read client's `HttpClient.Timeout` is infinite so the pipeline owns the deadline, including response bodies. When `Enabled` is false, that timeout stays the normal client timeout and the handler makes one attempt. A test factory whose client timeout is not infinite keeps the primary handler, which is how existing wire tests avoid the new pipeline. Dynamic settings are read when the client is created and on each request. R1 does not reload a live circuit; a restart applies configuration.

Circuit-open and limiter rejection surface as `HttpRequestException` for HTTP, so Application and Domain do not reference Polly. A pipeline timeout becomes `TaskCanceledException` when the caller token is not canceled. A database circuit-open throws `ResilienceAdmissionException` (`circuit-open`). A database deadline throws `TimeoutException`. Neither returns an empty successful result. An ambient `Transaction.Current`, an unknown database operation, or a delegate that leaves a transaction or unsaved changes is rejected. The ambient and unknown cases are rejected before the delegate runs.

## Telemetry

Meter `Antiphon.Resilience`, registered with `.WithMetrics(metrics => metrics.AddAntiphonResilienceMetrics())`. Counters: `retry.attempts`, `operations.completed`, `circuit.transitions`, `circuit.rejections`, `budget.exhausted`. Histograms: `retry.delay` and `operation.duration` (seconds). Tags are the bounded family, dependency, and reason (64 characters). Logs and tags do not carry exception messages, SQL, bodies, URLs, tokens, or connection strings. One Warning per retry (1-based retry number) and one Warning for the terminal exhausted, canceled, or failed outcome. Circuit transitions are Information.
