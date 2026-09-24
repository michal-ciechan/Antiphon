# CARD-0658: Operator endpoint auth without loopback trust

Card: CARD-0658 (High/Normal). Base inspected: `origin/master` 57543b85 (2026-09-24).
Plan task 7558d5cb, written on the server2 phone-home runner. Next stage: Code.

## Outcome and boundaries

The Hangfire dashboard stops trusting the client address and requires the same operator
credential the CARD-0653 force-release routes already require. Scripts send it as the
`X-Antiphon-Operator-Token` header; the desktop browser gets a short-lived dashboard cookie
bootstrapped from that token by one script, never by typing or pasting the token. No
`IsLoopback` / `RemoteIpAddress` / `LocalRequestsOnlyAuthorizationFilter` check remains on
any operator surface, and no `ForwardedHeaders` middleware is added.

In scope: `/hangfire` (authorization filter, login bootstrap, cookie), the desktop access path
(`scripts/hangfire-dashboard.ps1`, `scripts/logs.ps1 -Source hangfire`), proxy-shaped red tests
for both `/hangfire` and the CARD-0653 routes, the test factory's token path, docs.

Out of scope, named so Code does not drift into them: the rest of `/api` being unauthenticated
(the documented single-operator trust model, `docs/antiphon-api.md:14-22`); Caddy or Vite
routing changes (the dashboard is not proxied today and this plan does not proxy it);
renaming `PhoneHomeRunner:OperatorTokenPath`; the E2E fixture's use of the machine default
token path (pre-existing, follow-up F-2).

## Ground truth

| Card/brief assumption | What the code does at 57543b85 | Design consequence |
|---|---|---|
| The public vhost goes Caddy -> Vite (17203) -> 17202, so Kestrel sees every request as loopback. | Confirmed for everything Vite forwards: `client/vite.config.ts:38-49` proxies only `/api` and `/hubs`, both `changeOrigin: true` (Host rewritten to `localhost:17202`), `ws: true`. No `ForwardedHeaders` / `UseForwardedHeaders` / `X-Forwarded-*` anywhere in `server`, `tests` or `client` (repo grep, 0 hits). Also: even a direct `http://localhost:17202` request arrives through Aspire's DCP proxy (`Antiphon.AppHost/Program.cs:77`, `WithHttpEndpoint(port: 17202, env: "ASPNETCORE_HTTP_PORTS")`), so Kestrel's real listener never sees a non-loopback peer for any request that matters. | An address check cannot distinguish the operator from the tailnet. The credential must be explicit (D-1); the address is removed from the decision entirely (D-2). |
| `/hangfire` is effectively unauthenticated through the proxy today. | Half right. **Measured 2026-09-24 from server2**: `GET https://antiphon.desktop.codeperf.net/hangfire` -> 200 `text/html`, 1284 bytes, the SPA shell (`<title>client</title>`, `id="root"`); `/hangfire/stats` identical. Vite has no `/hangfire` proxy entry, so nobody outside the desktop reaches the dashboard through the vhost right now. But the guard `LocalRequestsOnlyAuthorizationFilter` (`server/Program.cs:942-945`) is satisfied by every request Kestrel can receive (previous row), so it enforces nothing: the first Caddy path route, Vite proxy entry or tunnel that covers `/hangfire` opens the dashboard with no code change and no test going red. | The fix stands unchanged; the premise is restated as "the guard protects nothing", not "the dashboard is open now". Not an Investigate redirect: the measurement is done and the remedy is the same either way. The new red test is a loopback, proxy-shaped request, which is exactly what any future route would deliver. |
| The CARD-0653 force-release routes are effectively unauthenticated. | Wrong at this base. `RequireOperator` (`server/Api/Endpoints/SessionRunnerEndpoints.cs:133-141`) requires `X-Antiphon-Operator-Token` on both release routes whatever the address, comparing through `OperatorTokenFile.Matches` (SHA-256 of both sides then `CryptographicOperations.FixedTimeEquals`, `server/Infrastructure/Security/OperatorTokenFile.cs:74-81`). `Force_release_without_the_operator_token_is_forbidden_even_from_loopback` (`tests/Antiphon.Tests/Application/RunnerSlotEndpointTests.cs:101-128`) pins it with `ClientAddress = IPAddress.Loopback`. | No production change on those routes. The existing test gains the proxy shape (`X-Forwarded-For`, `X-Forwarded-Host`, rewritten `Host`) so the pinned invariant is the one the card names (V-5). `RequireOperator` moves into a shared helper so the dashboard uses the identical comparison (D-7). |
| Hangfire's filter is the only loopback guard; there may be other `IsLoopback` / `RemoteIpAddress` checks. | Repo grep for `IsLoopback`, `RemoteIpAddress`, `LocalIpAddress`, `LocalPort`, `Connection.Local` in `server`: the only production hit is `AuditMiddleware` storing `RemoteIpAddress` into `Items["ClientIp"]` for audit rows (`server/Api/Middleware/AuditMiddleware.cs:19-21`); it gates nothing. `PlanEndpoints` / `FileSystemEndpoints` rely on the documented whole-API localhost trust model, not on a check. Tests set addresses only in `PhoneHomeTestHost.cs:89` and `HangfireStartupSafetyTests.cs:143-158`. | One production filter to replace. The audit field stays (it is a record, not a decision). |
| `ForwardedHeaders` may be configured. | It is not, anywhere. | Keep it that way (D-3). |
| The operator token file and its tests. | `OperatorTokenFile.DefaultPath()` is `%LOCALAPPDATA%\Antiphon\operator-token` on Windows, else `$XDG_DATA_HOME/antiphon/operator-token` (`OperatorTokenFile.cs:23-35`); `PhoneHomeRunner:OperatorTokenPath` overrides and must be absolute (`PhoneHomeRunnerSettings.cs:105`); the server creates it at startup and never logs it (`Program.cs:878-888`). **`AntiphonWebAppFactory` sets no `PhoneHomeRunner:OperatorTokenPath`** (`tests/Antiphon.Tests/TestHelpers/AntiphonWebAppFactory.cs:83-120`), so every Program boot in `Antiphon.Tests` today reads or creates the machine's real operator token. `PhoneHomeTestHost` uses its own temp path (`:38-39`). | S1 gives the factory its own path under its scratch directory and exposes it; the dashboard tests read the token from there and never touch the real file. |
| How the operator reaches the dashboard today. | Docs: `http://localhost:17202/hangfire` "loopback only" (`docs/logs.md:21`, `docs/bootstrap.md:474`). `scripts/logs.ps1 -Source hangfire` does `Invoke-WebRequest "$api/hangfire"` and reports the status (`scripts/logs.ps1:62-65`); it would get 401 after this change. | New `scripts/hangfire-dashboard.ps1` is the browser path (D-5); `logs.ps1` sends the header (D-10); both docs lines change (S3). |
| Hangfire dashboard API. | `Hangfire.AspNetCore` 1.8.25 (`server/Antiphon.Server.csproj:61`). `DashboardOptions.Authorization` is an `IDashboardAuthorizationFilter` list; `Authorize(DashboardContext)`; `context.GetHttpContext()` exposes headers, cookies and `RequestServices`. Filters are AND-ed; an empty list allows everyone. On refusal the middleware answers 401 because no ASP.NET identity is ever set (`CurrentUserMiddleware` hardcodes an admin without `UseAuthentication`, `docs/antiphon-api.md:14-16`); the existing test accepts 401 or 403 (`HangfireStartupSafetyTests.cs:162`). | Replace the list with exactly one filter. Dashboard assertions keep `ShouldBeOneOf(401, 403)` so they pin our refusal, not Hangfire's choice of code; our own endpoints assert exact 403 with a code. |
| Existing dashboard tests. | `Loopback_request_reaches_the_Hangfire_dashboard` (`HangfireStartupSafetyTests.cs:138-150`) asserts 200/302 for loopback **without** a credential: it pins the defect. `Non_local_request_is_rejected_by_the_built_in_local_only_filter` (`:152-166`) stays true after the change (no credential -> 401) but for a different reason. 9 `[Test]` methods in the class today. | The first test is deleted and replaced by V-1 (its inverse); the second is renamed to say why it is refused (no credential, not the address). |
| Clock and logging. | `TimeProvider.System` is registered (`server/Program.cs:524`); tests reference `Microsoft.Extensions.TimeProvider.Testing` 9.5.0. `AuditMiddleware` records only the client address and start time; there is no `UseSerilogRequestLogging`; `ExceptionMiddleware` logs the exception message and trace id, not the request path or query (`ExceptionMiddleware.cs:45-52`). | The session store takes `TimeProvider`; expiry tests use `FakeTimeProvider`. Refusals throw `ForbiddenException` with a fixed message, so a nonce in a query string is never logged (D-8). |
| Runner where Code will verify. | server2 runner image: git 2.39.5 (CARD-0665 plan), Testcontainers Postgres works. Nothing in this card drives `git show-ref --exists`; `AntiphonWebAppFactory` and `TestDbFixture` rows run here. A cold isolated `tests/Antiphon.Tests` build measured 2m32s-3m13s with `--property:UseAppHost=false` (CARD-0664/0665). | Every checkpoint below runs on server2 or the desktop; no row needs git >= 2.43. |

## Decisions

- **D-1: One credential for every operator surface: the CARD-0653 operator token.** The
  dashboard, the force-release routes and any future operator route accept the same file-backed
  value compared in constant time. Rejected: a second Kestrel listener bound to `127.0.0.1` on a
  port Vite does not proxy, gated on `Connection.LocalPort`. Calling `Listen` in
  `ConfigureKestrel` makes Kestrel drop the `ASPNETCORE_HTTP_PORTS` binding Aspire assigns
  ("Overriding address(es)"), so the DCP-proxied 17202 would have to be re-derived from the
  environment by hand; it is still an address-based decision, which is the class of check the
  card refutes; every test host and the E2E stack would need the second port; and it protects
  nothing against the desktop's own agent processes, which run under the server's account and can
  read the token file anyway. The credential design needs no port, no Aspire change and works for
  scripts on the vhost as well.
- **D-2: The dashboard's only filter is `OperatorDashboardAuthorizationFilter`.** It authorizes
  when the request carries a live dashboard session cookie (D-4) or a header token that matches
  the file; otherwise it refuses, whatever `RemoteIpAddress` says. `LocalRequestsOnlyAuthorizationFilter`
  is removed, not kept alongside: filters are AND-ed, so keeping it would refuse a future direct
  non-loopback operator request while never stopping a proxied one, and it would keep the false
  belief in the code. Rejected: `IDashboardAsyncAuthorizationFilter` (nothing awaits here).
- **D-3: No `ForwardedHeaders` middleware.** Rejected: `UseForwardedHeaders` with
  `KnownProxies = { loopback }` to recover the tailnet address and refuse it. Vite's proxy does
  not set `X-Forwarded-For` unless `xfwd: true`, Caddy's headers are set outside this repo, and
  any client on the desktop can forge the header toward a listener that trusts loopback; the
  design makes the address irrelevant instead of trying to reconstruct it. Tests send
  `X-Forwarded-For` and `X-Forwarded-Host` precisely to prove they change nothing in either
  direction (V-1, V-2, V-5).
- **D-4: Browser access is a one-time login link that sets a server-side session cookie.**
  `POST /api/operator/dashboard-sessions` (header token required, 403 `operator_token_required`
  otherwise) answers `{ "loginPath": "/api/operator/dashboard-login?nonce=<64 hex>", "expiresAt" }`.
  `GET /api/operator/dashboard-login?nonce=` redeems the nonce (single use, 120 s), creates a
  session (32 random bytes, hex, 12 h), sets cookie `antiphon-operator-dashboard=<session>` with
  `HttpOnly`, `SameSite=Strict`, `Path=/hangfire`, `Max-Age=43200`, `Secure` when the request is
  HTTPS, and answers 302 to `/hangfire`. An unknown, expired or already-redeemed nonce is 403
  `operator_login_invalid` with a fixed message and no cookie. The store keeps only SHA-256 hashes
  of nonces and session ids, so a memory dump yields no live cookie. Rejected: (i) the cookie is
  the token itself (a 12 h browser copy of the root credential; a stolen cookie is a stolen token;
  nothing short of rotating the file revokes it); (ii) an HTML form where the operator pastes the
  token (the operator would open and copy the file every time; the link path never shows the
  value to anyone); (iii) ASP.NET cookie authentication (`AddAuthentication().AddCookie()` +
  `UseAuthentication`) - the app has no auth stack and `CurrentUserMiddleware` hardcodes an admin
  for every request, so adding a scheme for one dashboard changes the pipeline globally for a
  forty-line need; (iv) an HMAC-signed stateless cookie keyed on the token - more crypto surface
  than a dictionary, and losing sessions on restart is acceptable because re-login is one command.
- **D-5: The desktop path is `scripts/hangfire-dashboard.ps1`.** ASCII-only, Windows PowerShell
  5.1-parsable. It reads the token exactly as `scripts/runner-slots.ps1` does
  (`ANTIPHON_OPERATOR_TOKEN_FILE`, else `%LOCALAPPDATA%\Antiphon\operator-token`), POSTs
  `dashboard-sessions` to `$env:ANTIPHON_API` or `http://localhost:17202`, joins the returned
  `loginPath` to that base and hands the absolute URL to `Start-Process`, which opens the default
  browser already logged in. It prints neither the token, nor the nonce, nor the URL; a refusal is
  reported by its problem `code` only. Through the vhost nothing changes for the browser: Vite
  does not proxy `/hangfire`, so remote dashboard access does not exist and is not added; if a
  route is ever added, it meets a credential-gated dashboard. Rejected: printing the URL for
  manual paste (the nonce is a credential for two minutes and this repo's terminals are captured
  into transcripts); a `dashboard` verb on `runner-slots.ps1` (that script is about seats, and
  `logs.ps1` needs one name to point at). The ten-line `Add-OperatorToken` helper is duplicated
  with a comment naming the twin; a shared dot-sourced module is not worth a third file.
- **D-6: Lifetimes are constants, not settings.** `OperatorDashboardSessions.NonceLifetime`
  = 2 min, `SessionLifetime` = 12 h. Expired entries are swept on every call; only header-
  authenticated callers can create entries, so the store is bounded by operator activity.
  Rejected: `Hangfire:DashboardSessionHours` (no operator need shown; a setting needs validation,
  docs and a docker-stack contract row; re-login is one command).
- **D-7: `PhoneHomeRunner:OperatorTokenPath` keeps its name; the comparison moves to one helper.**
  New `OperatorCredential` (Infrastructure/Security) exposes `HeaderMatches(HttpContext,
  PhoneHomeRunnerSettings)` and `Require(...)` (throws `ForbiddenException(..., "operator_token_required")`).
  `SessionRunnerEndpoints.RequireOperator` delegates to it with the same message and code, so the
  CARD-0653 test keeps passing unchanged. Renaming the setting is follow-up F-1.
- **D-8: Nothing logs, echoes or stores a raw credential.** The filter and the operator endpoints
  emit no log line containing header, cookie, nonce or session values; refusals are
  `ForbiddenException` with fixed messages (`ExceptionMiddleware` logs the message and trace id
  only); the login response body never contains the nonce it just consumed; the sessions response
  contains the nonce (that is its purpose) and never the token. V-4 asserts the last two.
- **D-9: Tests are proxy-shaped end to end.** Dashboard tests go through the shared
  `AntiphonWebAppFactory` (`TestServer.SendAsync`, connection addresses set explicitly) with
  `Host: localhost:17202`, `X-Forwarded-For: 100.64.0.7`, `X-Forwarded-Host:
  antiphon.desktop.codeperf.net`. The force-release test uses `PhoneHomeTestHost` (real Kestrel
  on loopback) with the same three headers. Grants are also asserted from `8.8.8.8` to pin that
  the address is not consulted in either direction.
- **D-10: `scripts/logs.ps1 -Source hangfire` sends the header.** Same file-reading helper; it
  reports "HTTP 200" (or the refusal code) and the instruction to run `hangfire-dashboard.ps1`;
  nothing else changes in that script.

## Design

### Server

- `server/Infrastructure/Security/OperatorCredential.cs` (new, static):
  `bool HeaderMatches(HttpContext http, PhoneHomeRunnerSettings settings)` reads
  `OperatorTokenFile.Header`, resolves the path, `ReadOrCreate`, `Matches`.
  `void Require(HttpContext, PhoneHomeRunnerSettings)` throws the CARD-0653 `ForbiddenException`
  (message and code unchanged). `SessionRunnerEndpoints.RequireOperator` becomes a one-line call.
- `server/Infrastructure/Security/OperatorDashboardSessions.cs` (new, singleton, `TimeProvider`
  ctor): `string IssueNonce()` (returns the raw 64-hex nonce, stores `sha256(nonce) -> expiry`);
  `string? Redeem(string? nonce)` (removes the hash atomically with `TryRemove`, refuses expired,
  creates a session and returns its raw id); `bool IsLive(string? sessionId)`; both dictionaries
  are `ConcurrentDictionary<string, DateTimeOffset>`; `Sweep()` on each call. Public constants
  `NonceLifetime`, `SessionLifetime`, `CookieName = "antiphon-operator-dashboard"`.
- `server/Infrastructure/Security/OperatorDashboardAuthorizationFilter.cs` (new,
  `IDashboardAuthorizationFilter`, ctor `(OperatorDashboardSessions, IOptions<PhoneHomeRunnerSettings>)`):
  `Authorize` = cookie live, else header matches, else false. No logging, no address access.
- `server/Api/Endpoints/OperatorEndpoints.cs` (new, `MapOperatorEndpoints`, tag `Operator`):
  `POST /api/operator/dashboard-sessions` -> `OperatorCredential.Require`, `IssueNonce`, 200
  `OperatorDashboardLoginDto(LoginPath, ExpiresAt)`. `GET /api/operator/dashboard-login` ->
  `Redeem(query "nonce")`; null -> `ForbiddenException("The dashboard login link is invalid or
  has expired; run scripts/hangfire-dashboard.ps1 again.", "operator_login_invalid")`; else
  `Response.Cookies.Append(CookieName, session, new CookieOptions { HttpOnly = true, SameSite =
  Strict, Path = "/hangfire", MaxAge = SessionLifetime, Secure = Request.IsHttps })` and
  `Results.Redirect("/hangfire")`.
- `server/Application/Dtos/OperatorDashboardLoginDto.cs` (new record).
- `server/Program.cs`: `builder.Services.AddSingleton<OperatorDashboardSessions>()` next to the
  other Security singletons; `app.MapOperatorEndpoints()` after `MapSessionRunnerEndpoints()`;
  `DashboardOptions.Authorization = [new OperatorDashboardAuthorizationFilter(app.Services.GetRequiredService<OperatorDashboardSessions>(), app.Services.GetRequiredService<IOptions<PhoneHomeRunnerSettings>>())]`
  with a comment naming CARD-0658 and why the address is not consulted.

Endpoint routing keeps `/api/operator/*` outside Hangfire's `/hangfire/{**path}` branch, so the
login route is never subject to the filter it bootstraps. Cookie `Path=/hangfire` is a prefix
match, so `/hangfire` and `/hangfire/stats` receive it and `/api/...` does not; the login
response may set a cookie whose path differs from the request path (RFC 6265 permits an explicit
`Path`). Hangfire's own antiforgery cookie for dashboard POSTs is untouched.

### Scripts and docs

- `scripts/hangfire-dashboard.ps1` (new; see D-5). Non-Windows: `Start-Process` of a URL is not
  reliable, so the script says to use the header with `curl` and exits 2.
- `scripts/logs.ps1`: the `hangfire` branch adds the header (D-10).
- `docs/agent-credentials.md:196-206`: section retitled "Operator token (CARD-0653, CARD-0658)";
  adds the dashboard, the cookie (name, lifetime, attributes), the login link, and "a loopback
  address is not a credential, and `X-Forwarded-*` is neither trusted nor required".
- `docs/ops-http.md`: new row "Operator dashboard (CARD-0658)" after the Runner seats row: the two
  routes, the codes, the script.
- `docs/antiphon-api.md`: the two routes in the route map; the "Do not expose this port" paragraph
  gains one sentence that operator-only surfaces need the token regardless of address.
- `docs/logs.md:21`, `docs/bootstrap.md:474`: "loopback only" -> "operator token; open it with
  `scripts/hangfire-dashboard.ps1`".

### Tests

- `tests/Antiphon.Tests/TestHelpers/AntiphonWebAppFactory.cs`: `["PhoneHomeRunner:OperatorTokenPath"] =
  Path.Combine(_workspacePath, "operator-token")`; `public string OperatorTokenPath { get; }`.
- `tests/Antiphon.Tests/TestHelpers/PhoneHomeTestHost.cs`: `PostOperatorAsync(..., bool proxied = false)`
  adds `Host: localhost:17202`, `X-Forwarded-For: 100.64.0.7`, `X-Forwarded-Host:
  antiphon.desktop.codeperf.net` when `proxied`.
- `tests/Antiphon.Tests/Infrastructure/HangfireStartupSafetyTests.cs`: delete
  `Loopback_request_reaches_the_Hangfire_dashboard`; rename `Non_local_request_is_rejected_by_the_built_in_local_only_filter`
  to `Request_without_a_credential_is_refused_whatever_its_address`; add the four `C658_*` methods
  of V-1..V-4. A private `SendDashboardAsync(IPAddress remote, Action<HttpRequest>? configure)`
  helper sets the three proxy headers and the connection addresses.
- `tests/Antiphon.Tests/Api/OperatorDashboardLoginTests.cs` (new, `[Category("Integration")]`,
  shared factory, `[NotInParallel]` with the Hangfire class because both read the factory's token
  file): V-6.
- `tests/Antiphon.Tests/Infrastructure/OperatorDashboardSessionsTests.cs` (new, `[Category("Unit")]`,
  `FakeTimeProvider`): V-7.
- `tests/Antiphon.Tests/Application/RunnerSlotEndpointTests.cs`: the existing loopback test sends
  `proxied: true` on every request (V-5).

## Slices and bounded Code rounds

### Round A (S1 + S2): red tests, then production (~75 min)

- **S1 (red commit).** Factory token path; `PhoneHomeTestHost.proxied`; the four `C658_*` dashboard
  tests; `OperatorDashboardLoginTests`; `OperatorDashboardSessionsTests`; the proxied force-release
  test. So the tests compile, S1 also adds the production types as inert seams: `OperatorCredential`
  (real, it is a move), `OperatorDashboardSessions` whose `Redeem` returns null and `IsLive` returns
  false, `OperatorDashboardAuthorizationFilter` (real body), `OperatorEndpoints` mapped in
  `Program.cs`; `Program.cs` still uses `LocalRequestsOnlyAuthorizationFilter`. CP-1 must show V-1,
  V-2, V-3, V-4 and V-6 red for the reasons in the table and V-5, V-7 as stated.
- **S2.** `Redeem` / `IsLive` real; `DashboardOptions.Authorization` switched to the new filter;
  Program comment. CP-2 and CP-3 green.

### Round B (S3): scripts and docs (~30 min)

- **S3.** `scripts/hangfire-dashboard.ps1`, `scripts/logs.ps1`, the five docs. CP-4.

Commit each slice with the real outcome in the message; push before CP-1 and after each green
row.

## Operator activation

None. The token file already exists on the desktop (CARD-0653 creates it at startup). After
land and restart: `pwsh -NoProfile -File scripts/hangfire-dashboard.ps1` opens the dashboard.
A manual smoke on the desktop (not a checkpoint): the browser lands on `/hangfire` showing jobs
after the redirect, and `/hangfire/stats` polling keeps working. If the browser shows 401 after
the 302, the `SameSite=Strict` assumption in D-4 failed on that browser; the fallback is
`SameSite=Lax` (Hangfire's own antiforgery still guards dashboard POSTs) and is a one-line change
plus the V-4 attribute assertion.

## Follow-ups (file as cards, do not do here)

- **F-1.** Rename `PhoneHomeRunner:OperatorTokenPath` to an `Operator:` section now that the token
  guards more than the runner routes.
- **F-2.** `tests/Antiphon.E2E` boots real `Program` without setting the token path, so it reads or
  creates the machine's real operator token; give the fixture its own path.
- **F-3.** Mutation stage candidates (method-scoped PC rows): filter returns `true` unconditionally
  (V-1 red); `Redeem` skips `TryRemove` (V-4 second-redeem assertion red); `Require` removed from
  the release route (V-5 red); `Matches` replaced by `==` is not observable by these tests and
  is covered by `OperatorTokenFileTests` only structurally.

## Verification design

Ordinary Code verification follows the `### Checkpoints` table as a closed list. Build with
`--property:OutputPath=bin-c658-<x>/` (forward slash) and `--property:UseAppHost=false` on
server2; results under `.antiphon/c658-checkpoints/`; delete every `bin-c658-*` directory before
finishing. Existing red is confirmed on the base commit with the exact failing method, never a
full suite. Assertions on the dashboard refusal use `ShouldBeOneOf(401, 403)`; assertions on our
own endpoints use exact 403 and the problem `code`.

### Coverage and falsifiable assertions

| ID | Class / executions | Behavioural oracle and expected baseline red |
|---|---|---|
| V-1 | `HangfireStartupSafetyTests.C658_Loopback_proxy_shaped_request_without_credential_is_refused` (1) | Remote and local address loopback; headers `Host: localhost:17202`, `X-Forwarded-For: 100.64.0.7`, `X-Forwarded-Host: antiphon.desktop.codeperf.net`; no token, no cookie; `GET /hangfire` -> 401/403 and the body does not contain `"Hangfire Dashboard"`. **Red at S1**: 200 (the loopback filter admits it). |
| V-2 | `HangfireStartupSafetyTests.C658_Header_token_grants_the_dashboard_from_a_non_loopback_address` (1) | Remote `8.8.8.8`, local `10.0.0.5`, proxy headers, `X-Antiphon-Operator-Token` read from `factory.OperatorTokenPath` -> 200 or 302. **Red at S1**: 401 (address refused). |
| V-3 | `HangfireStartupSafetyTests.C658_Wrong_header_token_is_refused_from_loopback` (1) | Loopback, proxy headers, header `not-the-token` -> 401/403. **Red at S1**: 200. |
| V-4 | `HangfireStartupSafetyTests.C658_Login_link_sets_a_dashboard_cookie_that_grants_once_redeemed` (1) | `POST /api/operator/dashboard-sessions` with the header from loopback -> 200; `loginPath` starts with `/api/operator/dashboard-login?nonce=` and the body does not contain the token. `GET loginPath` without header, proxy-shaped -> 302, `Location: /hangfire`, `Set-Cookie` names `antiphon-operator-dashboard`, contains `httponly`, `samesite=strict`, `path=/hangfire`, `max-age=43200`, not `secure` (http), and the body does not contain the nonce. `GET /hangfire` with that cookie from `8.8.8.8` -> 200/302. Second `GET loginPath` -> 403 code `operator_login_invalid`, no `Set-Cookie`. **Red at S1**: 302 never comes (inert `Redeem` -> 403 on the first redeem). |
| V-5 | `RunnerSlotEndpointTests.Force_release_without_the_operator_token_is_forbidden_even_from_loopback` (1, updated) | Same assertions as today with `proxied: true` on every request: no header -> 403, wrong header -> 403, orphans sweep without header -> 403, runner sees 0 `ReleaseSlot`; with the file's token the empty sweep succeeds. Green at S1 by design (the line it guards is CARD-0653's); its red is F-3's `Require` mutation. |
| V-6 | `OperatorDashboardLoginTests` (4): `Sessions_without_the_header_is_403_operator_token_required`, `Sessions_with_a_wrong_token_is_403`, `Login_with_an_unknown_nonce_is_403_and_sets_no_cookie`, `Login_over_https_marks_the_cookie_Secure` | Proxy-shaped requests through the shared factory's `HttpClient` / `SendAsync` (`Scheme = "https"` for the last). Exact 403 and `code` from the problem body; no `Set-Cookie` on refusal; `secure` present when HTTPS. **Red at S1**: the first two pass (the `Require` seam is real) and are reported so; the unknown-nonce test passes at S1 and is a regression pin; `Login_over_https` is red (inert `Redeem` -> 403 instead of 302). |
| V-7 | `OperatorDashboardSessionsTests` (4, `FakeTimeProvider`): `Nonce_redeems_exactly_once`, `Nonce_expires_after_two_minutes`, `Session_is_live_for_twelve_hours_then_not`, `Unknown_empty_or_null_values_are_never_live` | Pure store: second `Redeem` null; `Advance(121 s)` -> null; `IsLive` true at 11h59m, false at 12h01m; `IsLive("")`, `IsLive(null)`, `IsLive(64 random hex)` false. **Red at S1**: inert store returns null/false, so the first three fail. |
| R-1 | `OperatorTokenFileTests` (3) | Unchanged; pins the constant-time comparison and owner-only creation the dashboard now relies on. |
| R-2 | `HangfireStartupSafetyTests` remaining 7 methods (worker off, storage, recurring jobs, renamed refusal) | Unchanged behaviour; the renamed method still asserts 401/403 for `8.8.8.8` without a credential. |
| R-3 | `RunnerSlotEndpointTests` other 7 methods | Unchanged; prove `RequireOperator`'s move to `OperatorCredential` altered nothing. |

### Cost and execution rules

Estimated executed TUnit results: CP-1/CP-2 = 11 (Hangfire class) + 4 + 4 = 19; CP-3 = 8 + 3
= 11. Build once for S1, once for S2 (production changed), reuse for CP-3. Total ordinary floor
about 22 minutes on server2 including two builds.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c658-a/` | dashboard-red | `/*/*/(HangfireStartupSafetyTests*)\|(OperatorDashboardLoginTests*)\|(OperatorDashboardSessionsTests*)/*` | V-1..V-4, V-6, V-7 red | all 19 execute, no build/fixture errors; failed = exactly V-1, V-2, V-3, V-4, `Login_over_https_marks_the_cookie_Secure`, and the three store methods named in V-7; R-2 rows pass | 19 | 8 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c658-b/` | dashboard-green | `/*/*/(HangfireStartupSafetyTests*)\|(OperatorDashboardLoginTests*)\|(OperatorDashboardSessionsTests*)/*` | V-1..V-4, V-6, V-7, R-2 | all listed, 0 failed/skipped | 19 | 7 |
| CP-3 | S2 | CP-2 | force-release-proxied | `/*/*/(RunnerSlotEndpointTests*)\|(OperatorTokenFileTests*)/*` | V-5, R-1, R-3 | all listed, 0 failed/skipped; the proxied force-release method present | 11 | 4 |
| CP-4 | S3 | n/a | scripts-parse | `pwsh -NoProfile -Command "$t = [System.Management.Automation.Language.Parser]::ParseFile('scripts/hangfire-dashboard.ps1', [ref]$null, [ref]$e); $t2 = [System.Management.Automation.Language.Parser]::ParseFile('scripts/logs.ps1', [ref]$null, [ref]$e2); if ($e.Count -or $e2.Count) { exit 1 }"` then `grep -nP '[^\x00-\x7F]' scripts/hangfire-dashboard.ps1 scripts/logs.ps1` | D-5, D-10 | parse exit 0; grep prints nothing (ASCII-only) | n/a | 2 |

Running a row (CP-1 shown; CP-3 adds `-NoBuild` against `bin-c658-b/`):

```powershell
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.Tests -OutputPath bin-c658-a/ -Filter '/*/*/(HangfireStartupSafetyTests*)|(OperatorDashboardLoginTests*)|(OperatorDashboardSessionsTests*)/*' -MinExecuted 19 -Expect HangfireStartupSafetyTests,OperatorDashboardLoginTests,OperatorDashboardSessionsTests -ResultsRoot .antiphon/c658-checkpoints
```

On server2 pass `--property:UseAppHost=false` through `-DotnetShim` or run the two `dotnet`
commands by hand and print the identical `CHECKPOINT` line.
