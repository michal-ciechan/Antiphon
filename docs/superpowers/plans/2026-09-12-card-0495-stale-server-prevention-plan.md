# CARD-0495: prevent silent use of an obsolete server

Status: Plan and TestDesign complete; Code next. Complexity: medium; require Review after Mutation because the new boundary protects landing authority.

Baseline: `39afe4eca70741a6ff51df5981a83098f1f32bdf`. Investigation task `78d3700d` established deployment skew, not a source resolver defect. Its incident is already resolved. This plan does not reopen CARD-0488's landing algorithm.

## Outcome and scope

On this card, make an incompatible `delegate.ps1 -Land` fail before admission, make a restart prove which server build answered, and make post-land deployment verification an explicit operator step. Use one advertised land capability and one versioned route, not a general API negotiation framework.

The versioned route is necessary alongside a preflight: an old process cannot be taught to reject a new JSON property, and a process can change between GET and POST. A new URL gives the old process no matching land handler. A client-side warning or an after-the-fact response check is too late for this operation.

No periodic worker, UI, database migration, automatic restart, Git source resolver change, runner change, or global JSON-policy change belongs in this card. Follow-up proposals are at the end; this Plan creates no board cards.

Owners: [project conventions](../../project-context.md), [orchestration](../../orchestration-loop.md), [HTTP operations](../../ops-http.md), [API map](../../antiphon-api.md), [bootstrap](../../bootstrap.md), [restart runbook](../../apphost-runbook.md), [testing/build](../../testing-and-build.md).

## Ground truth

| Assumption | Evidence at the baseline | Design consequence |
|---|---|---|
| A fresh checkout means the live API implements its requests. | Investigation found the pre-September-9 process ignoring `expectedSourceSha`; the caller had newer code. During this Plan, a read-only GET `/api/version` returned the full baseline SHA, also the main worktree's HEAD. | Preserve the resolved incident; prevent a recurrence without another live restart during planning/testing. |
| The API has no running-build identity. | `VersionEndpoints.cs` returns `AntiphonVersionDto(Version, InformationalVersion)`. `AntiphonVersion.cs` reads its loaded assembly, stamped by `Directory.Build.props`. | Extend this endpoint additively. Never compute its identity from current disk HEAD at request time. |
| The CLI verifies compatibility. | `scripts/delegate.ps1`, `Land` arm, directly POSTs optional `verify`, `expectedSourceSha`, and `reviewEvidenceId` to `/{id}/land`. | Add a bounded preflight to this arm and change its POST route. Other CLI actions keep working against old servers. |
| Current landing already enforces exact approval. | `LandAgentTaskRequest` has all three fields; the endpoint forwards to `AgentTaskLandService.RequestAsync`. CARD-0488 commit is `e6826120813670cc6dd982df4f29666757d1dee0`. | Both current and new routes must share this service and its validation, resume, queue and receipt behavior. |
| Requiring unknown-field rejection now protects old binaries. | The deployed old handler predates the property and the proposed setting. Current global HTTP JSON setup only configures enum conversion; the land DTO has no unmapped-member attribute. | Rejection added today cannot fix a still-running old process. Do not make that the safety boundary. |
| Restart success proves the new build loaded. | `restart-apphost.ps1` exits 0 when dashboard URL exists and `/health` is 200. It does not probe `/api/version`. | Require health and intended build identity before success. Keep `/health` as liveness. |
| Existing staleness checks cover the API. | `check-daemon-build.ps1` covers session-runner and fake-gateway, with advisory source-closure comparisons. | Add the API restart assertion here; broader persistent staleness reporting is a separate card. |
| Build SHA is a complete content attestation. | The stamp is `git rev-parse HEAD`, with `unknown` fallback; it does not hash dirty files or runtime configuration. | State this limitation. Verify changed behavior as well when relying on it; do not claim SHA equality proves uncommitted edits loaded. |
| Every newer checkout must force a restart before land. | Worktree and documentation commits naturally differ from the deployed build. | Check supported semantics for land; reserve exact SHA equality for an explicitly requested deployment. |

## Decisions

### D-1. Advertise and address the approved-source land contract

Add `capabilities: ["land-v2"]` to GET `/api/version`, preserving both existing fields. This marker means that POST `/api/agent-tasks/{id}/land/v2` implements the existing CARD-0488 approval contract: fresh requests require exact `expectedSourceSha`, optional Review evidence is checked, and resume inherits only durable approval under the existing rules. It is an API capability, not a new named Delegation Capability or database schema version. Further compatible builds keep advertising it; a future incompatible operation needs its own marker and route.

Register `/land/v2` and existing `/land` against one shared handler that calls `AgentTaskLandService.RequestAsync`. Preserve request DTO, status codes, Problem Details, task-ID resolution, authentication/authorization context, accepted response, durable identity, and notifications. Do not introduce a second queue or service path. Preserve `/land` for existing callers on upgraded servers; direct use of an old client with an old server remains outside this new client guarantee.

An old process has no `/land/v2` handler, so it must reject that POST rather than invoke its old `/land` behavior. Do not redirect or retry `/land/v2` to `/land`, including on 404/405. Test this with an old-server stub whose `/land` would accept and record unsafe publication intent. The route, not a JSON flag or ignored request header, closes the GET-to-POST process-change race.

Rejected: a minimum commit hard-coded into `delegate.ps1` (Git availability, history/rebases/reverts/backports are not a protocol); comparing caller HEAD for every land (unrelated changes would block compatible servers); only checking the response's echoed SHA (mutation may already have begun); a new versioning framework for all APIs (unnecessary scope).

### D-2. Fail closed in `delegate.ps1 -Land`

Immediately before every land POST, including bodyless resume/cleanup, GET `/api/version` using the same resolved `ANTIPHON_API` base and existing headers. Use a bounded probe, at most 5 seconds; do not cache it between invocations. Provide a small timeout/error seam if needed without changing timeout behavior for unrelated CLI actions.

Require a well-formed response with a full hexadecimal build SHA (40 or 64 characters) and a capabilities array containing exact `land-v2`. Missing endpoint, old two-field response, absent marker, malformed JSON/shape/SHA, `unknown`, authentication failure, or timeout terminates with exit 1 and **zero land POSTs**. Compatible servers may have a different SHA from the caller checkout; unrelated capabilities are ignored.

The refusal names the safe API base, observed SHA or `unavailable`, required `land-v2`, and the recovery: update the canonical checkout if needed, perform the canonical AppHost restart, confirm `/api/version`, retry the original request. Do not print credentials, raw authorization headers, capability tokens, or credential-bearing URL components. Do not auto-pull, auto-restart, drop fields, infer an approved SHA, or provide a bypass switch.

After a successful probe, POST only `/land/v2`, retaining the exact body and existing accepted-request output. A 404/405 at this point is a loud compatibility/deployment failure with no legacy retry. Other HTTP errors retain their service diagnostics. A transport failure after sending POST is an uncertain acceptance: direct the caller to existing task/request status; do not claim zero requests or publication failure then.

### D-3. Restart success requires the intended loaded build

Add optional `-ExpectedServerSha <full-sha>` to `scripts/restart-apphost.ps1`. Resolve HEAD from the script's classified source root, never the caller's cwd. After existing ownership/worktree/lock admission and before any teardown, capture that full SHA. Default the expected SHA to this captured HEAD. An explicitly supplied malformed SHA, unreadable HEAD, or supplied SHA different from this HEAD refuses with exit 3 before any process is stopped, releasing only this invocation's lock if acquired. The explicit option lets an operator catch a stale canonical checkout before rebuilding it.

Keep the existing launch, health deadline, build/DCP failure handling and runner preservation. Once dashboard and `/health` succeed, GET `/api/version` with a bounded request within the remaining observation budget. Success requires its full SHA to equal the captured expected SHA and a fresh read of source-root HEAD still to equal that expected SHA. A changed checkout is a refusal of verification, not permission to adopt a new expectation. `-NoBuild` does not bypass this assertion.

Poll identity within the existing deadline so a transient endpoint during startup cannot cause an immediate second teardown. At deadline, health that succeeded but build identity is mismatched/unavailable/malformed, or a checkout that moved during observation, yields new exit **5**, with expected SHA, observed SHA/reason, source root, and lock/log inspection instructions. No green `backend healthy` success message. Keep the restart lock stamp like a health timeout because launch may still be in progress. Do not kill the newly launched process merely for this observation failure. Existing build failure = 1, ordinary startup/health timeout = 1, admission refusal = 3, and DCP timeout = 4 retain their behavior.

Exit 0 now means dashboard + API health + intended loaded SHA, and prints the verified full SHA. Add `5=server build unverified` to `Format-AppHostRestartExitName` and update its pinned script test. `deploy-local.ps1` already fails any nonzero restart exit; verify that propagation rather than redesigning deployment. Do not make the watchdog treat an otherwise healthy but older API as down, or change its restart cadence.

This is committed-build provenance, not dirty-file attestation. Preserve existing local dirty-build policy; print a clear limitation when the source checkout has tracked edits, and document that uncommitted behavior needs a direct feature probe. No content manifest or dirty-tree deployment policy is added here. Default HEAD comparison cannot discover a stale checkout versus an unfetched remote; the explicit expected SHA and D-4 procedure cover the intended deployment, with broader detection deferred.

### D-4. Name the standing operational habit

Add **Post-land server activation check (CARD-0495)** to `docs/orchestration-loop.md` beside landing/deploy instructions, with a short pointer in AGENTS.md's existing local-stack trigger and the executable procedure in `docs/apphost-runbook.md`:

> A land confirms publication, not server activation. Before relying on newly landed server behavior, record the landing receipt's verified commit, confirm the canonical checkout contains it, and check the running API's `/api/version`. If the build does not demonstrably include the required change, restart from the canonical checkout with the intended full HEAD as `-ExpectedServerSha`, then confirm the reported SHA and a direct capability/feature probe. Do not treat `/health`, a runner SHA, a pushed branch, or a succeeded delegate as activation evidence.

Use the landing receipt's post-rebase identity, not an assumption that the Code worktree SHA survived landing unchanged. An already-running descendant build is sufficient when local ancestry can be established and the required feature/capability is present; unrelated intervening commits do not require another restart. Unknown/unrelated build history cannot establish activation. Record desired and observed full SHAs in the deployment report. After out-of-band publication, update the canonical checkout using the existing runbook before choosing the intended deployment HEAD.

Update the API/ops route documentation and bootstrap stale-code paragraph to agree. Keep the runbook's Job Object/current-checkout caveat and its ban on a second `dev-aspire.ps1`. This card does not implement CARD-0381's Scheduled Task handoff.

## Implementation slices and test owners

| Slice | Files | Required evidence for TestDesign to make executable |
|---|---|---|
| S1: explicit API contract | `server/Application/Dtos/VersionDtos.cs`, `server/Api/Endpoints/VersionEndpoints.cs`, `server/Api/Endpoints/AgentTaskEndpoints.cs`; a small contract constants type only if needed | Extend `HealthEndpointTests`; new `AgentTaskLandContractEndpointTests` using `AntiphonWebAppFactory` and established landing setup. Prove marker, both routes' shared behavior, exact approval validation/persistence, bodyless fresh refusal and durable resume semantics. Preserve `/health` body. |
| S2: actual CLI boundary | `scripts/delegate.ps1`, `tests/Antiphon.Tests/Application/DelegateScriptLandApprovalTests.cs`, `DelegateScriptLandStatusTests.cs`; new `DelegateScriptLandCompatibilityTests.cs`; reuse `DelegateScriptRunner.cs` | Execute the real pwsh CLI against path-aware loopback stubs. Existing stubs currently return one arbitrary body for every GET and accept every POST: update them to distinguish version/status and reject incorrect routes. Capture request method/path/order/body/count. Test old server refusal and compatible probe followed by old-server POST handling, with legacy POST count zero. |
| S3: restart provenance | `scripts/restart-apphost.ps1`, `scripts/apphost-common.ps1`, `scripts/test-apphost-probe-class.ps1`; new isolated `scripts/test-apphost-server-version.ps1` and a small health/version helper if needed | Test actual observation and exit/lock wiring with injected clock/HTTP/Git/process seams or a copied fixture script tree with inert commands. A pure SHA comparator alone is insufficient. Cover defaults, explicit expected SHA, refusal-before-teardown, moved HEAD, health-only old process, missing/unknown version, delayed compatible startup, timeout, lock retention/release, and `-NoBuild`. No fixture may touch live ports/processes/tasks/locks. |
| S4: standing procedure | `docs/orchestration-loop.md`, `AGENTS.md`, `docs/apphost-runbook.md`, `docs/bootstrap.md`, `docs/ops-http.md`, `docs/antiphon-api.md` | Review concrete activation commands, route/capability semantics, exit table and first-install procedure below. Documentation must describe only the shipped behavior. |

Keep the actual production scope to these slices. Do not edit `AgentTaskLandingProtocol`, source selection, migrations, session delivery or the frontend merely to implement this compatibility boundary.

## TestDesign handoff

This dispatch does not fold TestDesign. Append the full `## Verification design` to this artifact, with executable V/R, distinct guard-to-PC mappings, exact methods, and separate Code/Mutation cost floors after reading the relevant fixtures. The following are acceptance requirements, not claimed test results:

1. A healthy old server with the original two-field version response receives zero land POSTs from the new CLI; the refusal contains useful build/upgrade diagnostics. Exercise timeout, 404, malformed/unknown identity, and missing/wrong capability independently.
2. A supported server at a different valid SHA accepts the exact caller body through `/land/v2`; request ID/publication-pending wording and resume remain unchanged. A future response with extra capabilities remains compatible. Non-Land status/create operations do not gain this probe.
3. Deterministic process-swap case: GET advertises support; subsequent POST hits an old stub exposing only `/land`. CLI fails, legacy `/land` sees zero calls, and there is no unsafe admission. A successful preflight must never authorize fallback to that route.
4. The real server's versioned route enforces existing approval/service rules and cannot create a request for a fresh missing/invalid approval. Both routes dispatch to the same durable request behavior; preserve existing receipt/outbox tests. This card introduces synchronous refusals, not a new asynchronous notification path: document that exclusion and map accepted requests to the existing delivery inventory instead of fabricating receipt from 202.
5. A dashboard and healthy API at the wrong SHA cannot produce restart exit 0. Test missing/unknown identity, changed source HEAD, and an explicit target different from source HEAD. The last case must stop before teardown; post-launch failures retain the lock and report exit 5. A later matching sample within the deadline succeeds and releases the lock. Test exit propagation through the deployment wrapper and watchdog label.
6. Safety regressions: linked-worktree refusal; fresh launch/restart locks; build/DCP timeout behavior; session-runner preservation; daemon scripts remain ASCII and parse under both pwsh and Windows PowerShell 5.1. Use existing `test-apphost-main-worktree-guard.ps1`, `test-apphost-lock-age.ps1`, and `test-apphost-probe-class.ps1` where applicable. New tests must isolate allowed restart paths completely.

Minimum affected C# selection: `HealthEndpointTests`, new contract/compatibility classes, `DelegateScriptLandApprovalTests`, `DelegateScriptLandStatusTests`, and `AgentTaskLandApprovalRequestTests`. Broaden only if a changed shared handler/seam requires it. Follow the existing Unit lane plus named integration recipe; use TUnit via `dotnet run`, isolated `OutputPath=bin-c495/`, fresh TRX, and nonzero expanded counts. Process-spawning C# tests require the assembly's `ParallelLimiter<ProcessSpawnLimit>`; do not run with Pty tests. No browser or production-runner tests are needed for these slices.

Mutation must break every independently bypassable safety guard, including preflight refusal, versioned-route/no-fallback selection, restart identity equality, expected-source HEAD admission and post-launch HEAD stability, and prove the intended assertion red before restored green. TestDesign may consolidate implementation seams but must not omit a guard by calling all of them one version check. Code reports PCs pending; Mutation performs them; Review is required before landing.

## First installation, rollback and completion

The currently live CARD-0488-capable build does **not** advertise `land-v2`. Installing only the new CLI must refuse until the server is upgraded. This is intentional, and the rollout must not depend on bypassing it.

1. Land this Plan and its TestDesign through the existing deployment. Complete Code, Mutation and Review before changing the live server.
2. Land the reviewed Code task using the canonical checkout's existing pre-CARD-0495 CLI, while it is still the installed CLI. Immediately before that one normal landing, verify `/api/version` identifies the known running CARD-0488-capable build (baseline full SHA above, or positively verified compatible successor) and pass the exact approved source SHA/Review evidence. The old script invocation is already running before the landing advances the canonical files. Do not use an unverified old script against an unknown server.
3. After publication, treat further land requests as unavailable until activation. Confirm the canonical checkout contains the receipt's verified commit, choose its intended current full HEAD, and have the operator perform `pwsh -NoProfile -File scripts/restart-apphost.ps1 -ExpectedServerSha <full-intended-head>` from the canonical checkout and an independent operator shell, per the Job Object caveat. No new CLI bypass, direct master push, or worktree restart is needed.
4. Require exit 0 and a fresh GET `/api/version` showing that exact SHA and `land-v2`. The marker is this card's direct running-capability probe; the real API tests must already have proved its advertised route and approval semantics. Do not enqueue a synthetic live landing merely to smoke-test deployment.
5. Record the desired/observed SHA and probe result in the deployment report. This activation evidence is caller-owned after landing and remains pending during Code/Mutation/Review. It is required before closing CARD-0495.

If the new CLI is already installed but the server is old, stop dependent landings and follow canonical recovery; do not disable its guard. Rollback to an old server likewise makes new CLI lands refuse. Do not restore silent fallback. If a first-install assumption fails, return the concrete version/checkout mismatch to the operator; do not improvise publication or restart mechanics.

## Follow-up cards, not prerequisites

- **Persistent API staleness visibility:** independently compare the running build with canonical checkout state, show full SHAs and relevant source changes prominently in existing attention/UI surfaces, distinguish behind/diverged/unverifiable, and account for source closure rather than merely counting days. Detection must not restart or kill a healthy process. Decide freshness of remote-reference evidence and dirty-input fingerprints in that card.
- **Strict mutation-request JSON contracts:** consider DTO-scoped unmapped-member rejection and explicit version requirements for other side-effecting endpoints, with caller inventory and compatibility tests. Do not globally reject unknown JSON here; that cannot protect already-running old binaries and could break unrelated clients.

No operator decision blocks this scoped plan. The default is S1-S4 now, these two proposals later.

## Verification design

Status: TestDesign complete at plan commit `67ee59d6`; execution is pending Code and Mutation. Scope is deliberately proportionate to the incident (a three-day-unrestarted AppHost silently ignoring a new request field): one CLI preflight, one versioned route, one restart identity assertion, one documented habit. Nothing here reopens the CARD-0488 landing algorithm; its Verification design remains the owner of admission, resolution, publication and receipt evidence.

### Inspection

Paths are repo-relative. "Read" means the whole body or the named section was read at `67ee59d6`.

| Bodies / fixture sections read | Boundaries -> coverage |
|---|---|
| `tests/Antiphon.Tests/Application/DelegateScriptRunner.cs`: real `pwsh -NoProfile -NonInteractive -File scripts/delegate.ps1`, `ANTIPHON_API` trimmed, `ANTIPHON_TASK_TOKEN` blanked unless the caller's environment map overrides it, 60 s process budget, stdout+stderr joined. | Reused unchanged for every S2 case. The 60 s budget bounds the two timeout cases (4 s and 8 s stub delays). Token-redaction case injects a nonce through the environment map. -> V-3, V-4, R-2. |
| `Application/DelegateScriptLandApprovalTests.cs` (`C488_PostsExactApprovalJson`, `C488_StatusShowsDistinctSourceFacts`, private `CapturingStub`): routes by HTTP method only, answers **every** GET with one body and **every** POST with 202, records the last POST body, no path or ordering capture. | Cannot distinguish `/api/version` from `/api/agent-tasks/{id}`, nor `/land` from `/land/v2`: today it would silently accept a legacy POST. Replace with the path-aware `LandApiStub` below; retain both assertions and add the v2 path assertion. -> V-3, R-1. |
| `Application/DelegateScriptLandStatusTests.cs` (`C467_V18_StatusAndAcceptance`, 11 rows, private `Stub`): same method-only shape; each row runs `-Status` then bodyless `-Land`. | Bodyless `-Land` must now probe first; stub must answer `/api/version` compatibly or all 11 rows go red for the wrong reason. Retain wording assertions ("Publication pending", "notification=not-required"). -> V-3 (bodyless resume), R-1, R-7. |
| `Application/HealthEndpointTests.cs`: one test; `[NotInParallel]`, `PerTestSession` `AntiphonWebAppFactory`; pins 40-hex `version`, `informationalVersion` contains it, in-process `AntiphonVersion.Sha` agrees. | Extend with `capabilities`; keep every existing assertion so the additive contract is proved (old two fields unchanged). -> V-1. |
| `TestHelpers/AntiphonWebAppFactory.cs`: real `Program` on a cloned schema; `ApplyTestOverrides(IServiceCollection)` hook; hosted services run, including `AgentTaskLandHostedService` (drains `AgentTaskLandQueue` via `RunRequestAsync`) and `AgentTaskLandSweepHostedService`. | A queued request in the shared factory would be drained and refused against the seed's fake `C:/tmp` paths, racing the bodyless-resume case. New `LandContractWebAppFactory` (PerClass) removes both hosted registrations so admitted requests stay pending and observable. Missing setup recorded: no existing HTTP-level land test; seed shape comes from `AgentTaskLandApprovalRequestTests.SeedSucceededWorktreeAsync`/`SeedReviewAsync` (copy into a shared `LandContractSeeds` helper). -> V-2. |
| `Application/AgentTaskLandApprovalRequestTests.cs`: service-level admission matrix (`expected_source_sha_required`, `expected_source_sha_invalid`, evidence mismatch, pending identity conflict, `land_running`), `CreateLand` with null protocol, isolated schema per test. | Unchanged; runs as regression proving the shared service still validates. HTTP-level V-2 rows mirror four of these codes through both routes. -> R-3. |
| `server/Api/Endpoints/AgentTaskEndpoints.cs` land route (`ResolveTaskIdAsync`, `request ?? new LandAgentTaskRequest()`, `Results.Accepted`), `server/Application/Services/AgentTaskLandService.RequestAsync` (pending inherit/conflict, fresh `NormalizeExpectedSha(required: !inherit)`), `LandApproval.NormalizeExpectedSha` (40/64 via `GitObjectId.TryNormalize`), `VersionEndpoints.cs`, `VersionDtos.cs`, `AntiphonVersion.cs`. | Both routes must reach the same handler delegate. Null body on v2 is the fresh-refusal boundary. -> V-2, G-11. |
| `TestHelpers/EphemeralHttpListener.cs`, `TestHelpers/ProcessSpawnLimit.cs`. | Loopback bind and assembly-local spawn limiter reused as-is. |
| `scripts/restart-apphost.ps1` (whole): worktree guard -> launch-lock check -> `New-AppHostLock` -> `check-daemon-build.ps1` -> kill pid-file tree -> free ports except session-runner PID -> stale dcpctrl/dashboard -> reset url/log -> `Start-Process pwsh dev-aspire.ps1` -> poll (BuildFailed exit 1 + kill child + release lock; DCP timeout exit 4 keep lock; health 200 -> exit 0 release lock; timeout exit 1 keep lock); `finally Remove-AppHostLock -KeepFile:$keepRestartLock`. | Every side effect must sit behind a seam function so the real script file runs in a disposable Git repository without touching live ports/processes. Identity observation slots in after health 200 and before the exit 0 branch. -> V-6..V-9, G-12..G-25. |
| `scripts/apphost-common.ps1` (whole): lock helpers, `Format-AppHostRestartExitName` (0/1/3/4), `Invoke-AppHostRestartCaptured`, probe classification, log verdicts. | Add exit 5 name; add seam-able primitives and the pure identity helpers. -> V-8, R-5. |
| `scripts/test-apphost-probe-class.ps1` (C5 exit names, C6 stub capture), `scripts/test-apphost-lock-age.ps1` (T-keep), `scripts/test-apphost-main-worktree-guard.ps1` (linked worktree: restart `-NoBuild -TimeoutSec 1` exits 3, no banner, no lock; deploy prints refused verdict). Pass/fail harness pattern (`Assert-True`, exit-code trailer line) reused by the new script. | Extend C5 with exit 5; C6b captures a stub `exit 5`. Worktree guard test retained unchanged as R-6. Measured: probe-class 2.7 s. -> V-8, R-5, R-6. |
| `scripts/deploy-local.ps1` (`Invoke-ChildPowerShell` throws on nonzero child exit; `DEPLOY VERDICT: failed <detail>` exit 1), `scripts/watchdog-apphost.ps1` restart capture (exit 3 not stamped; `ExitName` logged), `scripts/check-daemon-build.ps1` head (advisory runner/gateway staleness). | Propagation of 5 through the wrapper and its label are wiring cases. -> V-9, G-20. |
| `docs/apphost-runbook.md` exit table, `docs/ops-http.md` land row, `docs/antiphon-api.md` land/version rows, `docs/bootstrap.md` CARD-0358 paragraph. | S4 review checklist (V-10). |

Boundaries and where they land: SHA text 39/40/41/64/65 hex, uppercase hex, `unknown`, absent -> V-3 matrix and V-6; capabilities absent/`[]`/`["land-v2"]`/`["LAND-V2"]`/`["land-v3"]`/`["x","land-v2"]` -> V-3 (note PowerShell `-contains` is case-insensitive; the check must be `-ccontains`); version HTTP 200/401/404/500/non-JSON/timeout -> V-3; `/land/v2` 202/404/405/422/aborted -> V-3, V-4; bodyless versus full body -> V-2, V-3; probe timeout seam 1 s versus default 5 s -> V-3; restart expected = HEAD / valid-but-different / malformed / HEAD unreadable -> V-6; version sample sequences including a match on the last poll before the deadline versus first match after it -> V-7; HEAD moved with version A or B -> V-7; fresh versus stale lock -> R-6 plus existing lock-age suite; `-NoBuild` -> V-7; dirty tracked edits -> V-6. Excluded boundary combinations are listed under Out of scope.

### Delivery inventory

This card adds **no** asynchronous outcome-delivery path. Every new outcome is synchronous to its caller: CLI exit code and stdout (D-2), HTTP status and Problem Details or 202 body (D-1), script exit code and stdout (D-3). There is therefore no new producer, destination, persistence boundary, recovery or receipt to enumerate, and no UserPrompt transcript evidence is owed by this card.

An accepted `POST /api/agent-tasks/{id}/land/v2` maps to the existing CARD-0488 inventory row "Explicit CLI/API POST -> land worker" (durable identity: `AgentTaskLandRequest.Id`, returned as `requestId`). V-2 asserts admission only: the request row, its immutable approval fields and the `LandRequested` event. A 202, a `requestId`, or "Publication pending" text is admission evidence and is **not** treated as delivery or publication evidence anywhere in this design. The land worker, notification outbox and caller receipt stay covered by the CARD-0488 tests named there; this design does not re-run or restate them.

Substitutes and what each cannot prove:

| Substitute | Stands in for | Cannot prove |
|---|---|---|
| `LandApiStub` (loopback `HttpListener`, path-aware) | The server, in S2 CLI tests | Server-side admission semantics (covered by V-2 on the real host) and that a live process answers `/api/version` (covered by the first-install step recorded in the deployment report). |
| `LandContractWebAppFactory` with drain/sweep hosted services removed | The live server, in S1 tests | That a drained request runs (CARD-0488 tests) and that the loaded assembly's stamp equals disk HEAD on the canonical machine (the restart assertion, D-3, plus HealthEndpointTests' in-process agreement). |
| Seam-driven real `restart-apphost.ps1` in a disposable `git init` repository | Live teardown, `dev-aspire.ps1`, `/health`, `/api/version` | That Aspire actually launches or that the real `/api/version` is served; those remain the operator's activation check (D-4, first-install step 4). Git HEAD reads are **real** in the fixture. |

### Proves it works now

Every `C495_` method and every `T-n` case is **to implement**. Class aliases: **HE** = `Application/HealthEndpointTests.cs`; **LC** = new `Application/AgentTaskLandContractEndpointTests.cs` (`[NotInParallel]`, `[ClassDataSource<LandContractWebAppFactory>(Shared = SharedType.PerClass)]`, Integration); **CC** = new `Application/DelegateScriptLandCompatibilityTests.cs` (`[Category("Integration")]`, `[ParallelLimiter<ProcessSpawnLimit>]`); **LA** = `DelegateScriptLandApprovalTests`; **LS** = `DelegateScriptLandStatusTests`; **AR** = `AgentTaskLandApprovalRequestTests`; **SV** = new `scripts/test-apphost-server-version.ps1` (accepts `-Case <id>` to run one case, like `test-nightly-health.ps1`); **PC** = `scripts/test-apphost-probe-class.ps1`; **WG** = `scripts/test-apphost-main-worktree-guard.ps1`. CC scenarios are **individual public methods**, not `[Arguments]` rows, so each PC has an exact treenode filter.

Required new test setup:

- `tests/Antiphon.Tests/TestHelpers/LandApiStub.cs`: routes `GET /api/version` (configurable status, body, delay), `GET /api/agent-tasks/{id}` (status body), `POST /api/agent-tasks/{id}/land/v2` (configurable 202 body or 404/405/422 or connection abort after reading the body), `POST /api/agent-tasks/{id}/land` (always records `LegacyLandPosts`, returns 202 so an unsafe acceptance is observable), optional extra POST routes for `-Reply`; anything else 404 and recorded. Exposes `Requests` as an ordered list of `(Method, Path, Body)`. Presets `Old(sha)` (two-field body, no v2 route), `Compatible(sha, capabilities = ["land-v2"])`, `ProcessSwap(sha)` (compatible version, v2 404, legacy 202). Replaces both private stubs in LA and LS.
- `LandContractWebAppFactory : AntiphonWebAppFactory` overriding `ApplyTestOverrides` to remove the `AgentTaskLandHostedService` and `AgentTaskLandSweepHostedService` hosted-service registrations; `LandContractSeeds` with `SeedSucceededWorktreeAsync`/`SeedReviewAsync` copied from AR.
- `delegate.ps1` timeout seam: an environment variable (Code names it; suggested `ANTIPHON_VERSION_PROBE_TIMEOUT_SEC`, default 5) read only by the version probe. No other CLI action's timeout changes.
- `restart-apphost.ps1` seams: side effects move behind functions in `apphost-common.ps1` (`Get-AppHostPortOwners`, `Stop-AppHostProcessId`, `Stop-AppHostProcessTree`, `Get-AppHostStrayProcesses`, `Start-AppHostDevLaunch`, `Stop-AppHostLaunchChild`, `Invoke-AppHostHealthProbe`, `Invoke-AppHostVersionProbe`, `Wait-AppHostPollInterval`); teardown decision logic (which PIDs, the `-ne $srPid` filter) stays in the restart script. When an environment variable (suggested `ANTIPHON_APPHOST_TEST_SEAMS`) names an existing file, the restart script dot-sources it immediately after `apphost-common.ps1` and prints a `TEST SEAMS ACTIVE` line; otherwise nothing changes. The SV fixture copies `restart-apphost.ps1`, `apphost-common.ps1` and `deploy-local.ps1` into a temp `git init` repository with one commit (so the worktree classifier reports a main worktree and `git rev-parse HEAD` is real), writes inert `scripts/check-daemon-build.ps1` and `dev-aspire.ps1` (writes `logs/apphost-dashboard-url.txt`), and a seam file whose probes replay a scripted sample sequence and record every call. HEAD reads stay real Git. No fixture touches 172xx, live processes, scheduled tasks or `logs/` under the checkout.

| ID | Behaviour | Layer | Test / command | Expected |
|---|---|---|---|---|
| V-1 | `/api/version` advertises the land contract additively | HTTP, real host | HE.`C495_VersionAdvertisesLandV2`; retained HE.`Version_is_present_and_not_unknown_in_a_git_checkout`; retained `SmokeTests.Health_endpoint_returns_healthy` | 200; JSON properties `version` (40-hex, equals `AntiphonVersion.Sha`), `informationalVersion` (contains it), `capabilities` is a string array containing exactly one `land-v2`; `/health` body still literal `Healthy`. |
| V-2 | `/land/v2` and `/land` share admission and durable request behaviour | HTTP + DB, real host, drain disabled | LC.`C495_V2RouteQueuesExactApproval`; LC.`C495_V2RouteRejectsFreshMissingApproval`; LC.`C495_V2RouteRejectsAbbreviatedSha`; LC.`C495_V2RouteRejectsMismatchedEvidence`; LC.`C495_V2RouteBodylessResumeInheritsApproval`; LC.`C495_LegacyRouteSharesHandler`; LC.`C495_V2RouteResolvesShortIdAnd404`; LC.`C495_V2RouteRefusesUnsucceededTask` | v2 with `{expectedSourceSha: B, verify, reviewEvidenceId: null}` -> 202, Location `/api/agent-tasks/{id}`, body `{requestId, status:"queued", notification:"not-required"}`, DB row `ExpectedSourceSha=B`, `VerifyFilter` clipped filter, `LandRequested` event bound to the request. v2 `{}` -> 422 `expected_source_sha_required`, zero rows. `deadbee` -> 422 `expected_source_sha_invalid`. Evidence at B with expected C -> 409, zero rows. v2 B then bodyless v2 -> 202 `requeued`, same `requestId`, row still B; then v2 C -> 409 `land_request_identity_conflict`. Legacy `/land` B -> identical 202 shape; bodyless **v2** afterwards -> `requeued` same `requestId` (one durable path across routes); legacy `{}` fresh -> same 422 code. Short id on v2 -> 202 with full id; random id -> 404. Non-Succeeded task -> 409. |
| V-3 | New CLI refuses every incompatible server with zero land POSTs, and accepts a compatible one through `/land/v2` only | Real pwsh + `LandApiStub` | CC.`C495_OldServerTwoFieldVersionRefuses`; CC.`C495_MissingCapabilitiesRefuses` (200 with `version` but no `capabilities`); CC.`C495_EmptyCapabilitiesRefuses`; CC.`C495_WrongCapabilityRefuses` (`land-v3`); CC.`C495_UppercaseCapabilityRefuses` (`LAND-V2`); CC.`C495_UnknownShaRefuses`; CC.`C495_ShortShaRefuses` (39 hex); CC.`C495_MissingShaRefuses`; CC.`C495_Version404Refuses`; CC.`C495_Version401Refuses`; CC.`C495_Version500Refuses`; CC.`C495_VersionNonJsonRefuses`; CC.`C495_VersionProbeTimeoutRefuses` (seam 1 s, stub delay 4 s); CC.`C495_VersionProbeDefaultTimeoutIsFiveSeconds` (no seam, stub delay 8 s); CC.`C495_CompatibleServerPostsExactBodyToV2`; CC.`C495_ExtraCapabilitiesAndFieldsStillCompatible` (`["x","land-v2"]`, extra JSON fields, 64-hex sha); CC.`C495_BodylessResumeProbesThenPostsV2`; LA.`C488_PostsExactApprovalJson` (updated to `Compatible`); LS.`C467_V18_StatusAndAcceptance` (updated) | Each refusal: exit 1; `Requests` is exactly `[GET /api/version]`; `LegacyLandPosts == 0`; no `/land/v2` POST; stdout names the API base, `land-v2`, the observed SHA or `unavailable`, `/api/version` and `restart-apphost.ps1`; stdout lacks "Queued land"/"Publication pending". Timeout cases: elapsed < 4 s with the seam; between 5 s and 8 s without it. Compatible: exit 0; `Requests == [GET /api/version, POST /api/agent-tasks/{id}/land/v2]`; POST JSON has exactly `expectedSourceSha`, `reviewEvidenceId`, `verify`; stdout "Queued land request {requestId}. Publication pending..." unchanged; stub SHA differs from the caller checkout and is still accepted. Bodyless: POST body `{}`, "Requeued land" when the stub answers `requeued`. |
| V-4 | Process-swap after a successful probe fails loudly with no legacy POST; other v2 errors keep service diagnostics; post-send transport failure is reported as uncertain | Real pwsh + `LandApiStub` | CC.`C495_ProcessSwapAfterProbeFailsWithoutLegacyPost` (v2 404); CC.`C495_ProcessSwap405FailsWithoutLegacyPost`; CC.`C495_V2ServiceErrorKeepsDiagnostics` (v2 422 Problem Details); CC.`C495_TransportFailureAfterPostIsUncertain` (stub reads the body then aborts) | Swap: exit 1; `Requests == [GET version, POST v2]`; `LegacyLandPosts == 0`; stdout names `land-v2`, the status code and `restart-apphost.ps1`; no "Queued land". 422: exit 1; stdout contains the server's Problem Details message; no legacy POST. Abort: exit 1; stdout directs to `-Status <id>`; stdout does not claim zero requests or a failed publication. |
| V-5 | Refusals never print credentials; non-Land actions gain no probe | Real pwsh + stub | CC.`C495_RefusalRedactsCredentials` (token nonce via environment map, `Old` preset); CC.`C495_StatusDoesNotProbeVersion`; CC.`C495_ReplyDoesNotProbeVersion` | Nonce absent from stdout in the refusal; header name may appear, value never. `-Status` -> `Requests == [GET /api/agent-tasks/{id}]`; `-Reply` -> single POST `/reply`; no `/api/version` GET in either. |
| V-6 | Restart admission resolves the expected SHA from the script's source root before any teardown | SV with seams, real Git | SV T1 (cwd is a second temp repository with a different HEAD; default expectation); T2 (explicit `-ExpectedServerSha` = HEAD); T3 (explicit `abc`, 39-hex, 41-hex, `G`-containing, and a 64-hex value); T4 (explicit valid 40-hex != HEAD); T5 (repository with no commits: HEAD unreadable); T20 (tracked file edited before the run) | T1/T2: exit 0, printed verified SHA equals the script-root HEAD, lock file absent afterwards. T3/T4/T5: exit 3 **before** teardown: recorded teardown/kill/launch calls are all 0, `logs/apphost.restart.lock` absent after exit, stdout `REFUSED` names the supplied value, the source root and the HEAD (T4), and says to update the canonical checkout. T20: exit 0 plus the printed dirty-tree limitation line. |
| V-7 | Restart success requires health **and** the intended loaded SHA within the deadline; failure is exit 5 with the lock retained and the child alive | SV with seams, real Git | SV T6 (health 200, version always a different valid SHA); T7 (health 200, version 404 throughout); T8 (`unknown`, non-JSON, 39-hex); T9 (samples `[404, 404, old-sha, match]`); T9b (match arrives only after the deadline); T10a (seam commits in the fixture repository on its second call; version keeps returning A); T10b (same, version returns the new HEAD B); T12 (`-NoBuild` with mismatch) | T6/T7/T8/T9b/T10a/T10b/T12: exit 5; no "backend healthy" line; stdout names expected SHA, observed SHA or reason (`unavailable`/`malformed`/`checkout moved` with A and B), source root, lock path and log inspection instructions; `logs/apphost.restart.lock` present with a fresh stamp; `Stop-AppHostLaunchChild` call count 0; teardown count 1, launch count 1. T9: exit 0, lock absent, version-probe call count >= 4, printed verified SHA. T12 additionally: launch args contain `-NoBuild`. |
| V-8 | Existing exit paths and labels retained; new label added | SV, PC | SV T11 (no dashboard, no health: exit 1, lock kept, version probe count 0); T13 (`The build failed` in log: exit 1, lock removed, child stop count 1, version probe count 0); T14 (DCP text in log: exit 4, lock kept); T15 `Format-AppHostRestartExitName 5`; T18 (seams inert: without the env var `apphost-common.ps1` defines a version probe whose definition references `/api/version`, and no `TEST SEAMS ACTIVE` line is printed by the guard test's linked-worktree run); PC C5 extended and C6b (stub `exit 5`) | `5=server build unverified`; `Invoke-AppHostRestartCaptured` reports `ExitName` `5=server build unverified`. |
| V-9 | Deployment wrapper fails on exit 5 | SV T16 (copied `deploy-local.ps1`, child restart forced to exit 5 through the seam) | stdout last line `DEPLOY VERDICT: failed restart-apphost.ps1 exited 5`; exit 1; `verify-dev-stack.ps1` not invoked. |
| V-10 | Documentation matches shipped behaviour | Review checklist, not automated | Reviewer confirms: runbook exit table has 5 with operator action and the `-ExpectedServerSha` command; API map and ops-http list `/land/v2`, the `land-v2` marker and the refusal shape; orchestration-loop has the Post-land server activation check paragraph; AGENTS.md trigger points to it; bootstrap CARD-0358 paragraph references the restart assertion; the first-install sequence appears where the operator will read it. Text must not describe periodic staleness alerts or strict JSON rejection. |
| V-11 | Touched scripts stay ASCII and parse under both engines | SV T19 | Byte scan of `restart-apphost.ps1`, `apphost-common.ps1`, `deploy-local.ps1`, `delegate.ps1`, `test-apphost-server-version.ps1` finds no byte above 0x7F; `Parser::ParseFile` reports zero errors under pwsh 7 and, when `powershell.exe` exists, under 5.1. |

### Guards the regression

| ID | Regression | Test and decisive assertion |
|---|---|---|
| R-1 | A future server whose land contract changes again is silently used by the CLI (the incident shape) | CC.`C495_OldServerTwoFieldVersionRefuses`: `stub.Requests.Count.ShouldBe(1)` and `stub.LegacyLandPosts.ShouldBe(0)` with exit 1; CC.`C495_ProcessSwapAfterProbeFailsWithoutLegacyPost`: `LegacyLandPosts.ShouldBe(0)`. |
| R-2 | A refusal or retry leaks the task token or capability token | CC.`C495_RefusalRedactsCredentials`: `Output.ShouldNotContain(nonce)`. |
| R-3 | The v2 route bypasses or forks approval validation | LC.`C495_V2RouteRejectsFreshMissingApproval`: `StatusCode.ShouldBe(422)` and request row count 0; AR class unchanged and green. |
| R-4 | Restart reports healthy while serving an older build (CARD-0358 shape) | SV T6: exit code `ShouldBe 5`, output lacks `backend healthy`, lock present. |
| R-5 | The watchdog or deploy wrapper mislabels or swallows exit 5 | PC C5/C6b: exact `5=server build unverified`; SV T16: `DEPLOY VERDICT: failed` last line. |
| R-6 | Refactoring the restart script re-orders the worktree guard, lock admission or teardown | WG unchanged: `restart entry exits 3 from linked worktree`, `restart entry leaves no restart lock`, `restart entry exits before restart banner`; SV T22 (fresh restart lock pre-created: exit 3, teardown count 0, lock file untouched); existing `test-apphost-lock-age.ps1` green. |
| R-7 | Bodyless resume/cleanup lands without a probe | CC.`C495_BodylessResumeProbesThenPostsV2`: first request `ShouldBe GET /api/version`; LS all 11 rows green with the path-aware stub. |

### Guard inventory

Every safety-critical guard in D-1..D-3 maps 1:1 to a PC below; none is excluded. Guards G-21..G-25 are retained CARD-0075/0273/0310 guards that this card's restructuring of `restart-apphost.ps1` passes through and could silently reorder. Diagnostic-only properties (refusal wording, watchdog label, non-Land actions not probing, refusal reason text in exit 5) are V/R cases, not guards, because their failure cannot admit an unsafe land or a false healthy verdict.

| Guard | Plan reference and invariant | Control |
|---|---|---|
| G-1 | D-2: a version probe precedes **every** land POST, including bodyless resume | PC-1 |
| G-2 | D-2: an absent or empty capabilities array refuses | PC-2 |
| G-3 | D-2: only the exact, case-sensitive `land-v2` marker satisfies the probe | PC-3 |
| G-4 | D-2: the build SHA must be full 40/64 hex; `unknown`, short or missing refuses | PC-4 |
| G-5 | D-2: a probe transport/HTTP/parse failure refuses (fail closed) | PC-5 |
| G-6 | D-2: the probe is bounded (5 s default, seam-adjustable) | PC-6 |
| G-7 | D-1/D-2: the land POST targets `/land/v2`, never `/land` | PC-7 |
| G-8 | D-1/D-2: no retry or fallback to `/land` on 404/405 from `/land/v2` | PC-8 |
| G-9 | D-2: refusal output never contains the credential header value | PC-9 |
| G-10 | D-2: a transport failure after the POST is sent is reported as uncertain, never as "no request sent" | PC-10 |
| G-11 | D-1: `/land/v2` reaches `AgentTaskLandService.RequestAsync` with the caller's body and no defaulted approval | PC-11 |
| G-12 | D-3: exit 0 requires observed `/api/version` SHA equal to the expected SHA | PC-12 |
| G-13 | D-3: health 200 with an unavailable/404 identity is never success | PC-13 |
| G-14 | D-3: an explicit `-ExpectedServerSha` that is malformed or differs from source-root HEAD refuses (exit 3); unreadable HEAD refuses | PC-14 |
| G-15 | D-3: expected-SHA admission runs before any teardown | PC-15 |
| G-16 | D-3: source-root HEAD is re-read at verification and must still equal the captured expectation; a moved checkout is a refusal, not a new expectation | PC-16 |
| G-17 | D-3: exit 5 keeps `apphost.restart.lock` on disk | PC-17 |
| G-18 | D-3: `-NoBuild` does not bypass the identity assertion | PC-18 |
| G-19 | D-3: an identity failure never stops the launched child | PC-19 |
| G-20 | D-3: `deploy-local.ps1` fails on restart exit 5 | PC-20 |
| G-21 | Retained CARD-0273: linked-worktree refusal precedes lock acquisition and teardown | PC-21 |
| G-22 | Retained CARD-0075/0310: a fresh restart lock refuses before teardown | PC-22 |
| G-23 | Retained: the session-runner port owner is never killed during teardown | PC-23 |
| G-24 | Retained CARD-0310: BuildFailed stops the child and releases the lock | PC-24 |
| G-25 | Retained CARD-0075/0310: DCP dependency timeout exits 4 and keeps the lock | PC-25 |

Guards = 25, mapped = 25, missing = 0, duplicate PC mappings = 0. G-4 covers `unknown`/short/missing as one predicate because a defect in any of them is the same removed validation; G-14 folds malformed, different and unreadable into one admission predicate for the same reason (the exit-code oracle cannot separate them, only the message can). Two guards carry two compiling-defect variants each (PC-3, PC-16); both variants must run.

### Positive controls

Mutation applies each defect to the working tree only, runs the named method red, restores the file (refreshing its timestamp; no rebuild is needed for `.ps1` targets because the C# tests execute the script from disk), and runs the same method green. PC-11 is the only control needing a C# rebuild. Script-lane filters use `pwsh -File scripts/test-apphost-server-version.ps1 -Case <T>`; C# filters use `dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c495-pc/ -- --treenode-filter "/*/*/<Class>/<Method>"` with a fresh `--results-directory` per run. Zero executed tests, build errors or fixture errors are not red.

- PC-1: break G-1 by deleting the probe call in the `'Land'` arm of `delegate.ps1` so the POST is issued directly; expect CC.`C495_OldServerTwoFieldVersionRefuses` red at `stub.Requests.Count.ShouldBe(1)` (a `/land/v2` POST appears) and at `ExitCode.ShouldBe(1)`.
- PC-2: break G-2 by treating a null or empty capabilities array as compatible (`$hasMarker = (-not $caps) -or ($caps -ccontains 'land-v2')`); expect CC.`C495_EmptyCapabilitiesRefuses` red at the zero-POST assertion and exit 1, and CC.`C495_MissingCapabilitiesRefuses` red likewise.
- PC-3: break G-3 (a) by replacing `-ccontains 'land-v2'` with `-contains 'land-v2'`; expect CC.`C495_UppercaseCapabilityRefuses` red at exit 1 / zero-POST assertion. (b) by replacing it with `($caps -join ',') -match 'land-v'`; expect CC.`C495_WrongCapabilityRefuses` red at the same assertions. Both variants are executed.
- PC-4: break G-4 by deleting the SHA format validation in the probe; expect CC.`C495_UnknownShaRefuses` red at exit 1 / zero-POST assertion (CC.`C495_ShortShaRefuses` red as corroboration).
- PC-5: break G-5 by making the probe's `catch` block return a compatible result (`@{ Ok = $true; Sha = 'unavailable' }`); expect CC.`C495_Version404Refuses` red at the zero-POST assertion and exit 1.
- PC-6: break G-6 by removing `-TimeoutSec` (and the seam) from the probe request; expect CC.`C495_VersionProbeTimeoutRefuses` red at the zero-POST assertion (the 4 s late answer is accepted and a v2 POST follows) and at the elapsed-under-4 s assertion.
- PC-7: break G-7 by changing the POST path back to `/api/agent-tasks/$Land/land`; expect CC.`C495_CompatibleServerPostsExactBodyToV2` red at the path assertion `Requests[1].Path.ShouldEndWith("/land/v2")` and `LegacyLandPosts.ShouldBe(0)`.
- PC-8: break G-8 by adding a catch that re-POSTs to `/land` when `/land/v2` returns 404 or 405; expect CC.`C495_ProcessSwapAfterProbeFailsWithoutLegacyPost` red at `LegacyLandPosts.ShouldBe(0)` and `ExitCode.ShouldBe(1)`.
- PC-9: break G-9 by appending `$headers['X-Antiphon-Task-Token']` to the refusal message; expect CC.`C495_RefusalRedactsCredentials` red at `Output.ShouldNotContain(nonce)`.
- PC-10: break G-10 by printing `no land request was sent` in the POST failure branch; expect CC.`C495_TransportFailureAfterPostIsUncertain` red at `Output.ShouldNotContain("no land request was sent")` and at the `-Status` pointer assertion.
- PC-11: break G-11 by making the `/land/v2` handler substitute `new LandAgentTaskRequest(ExpectedSourceSha: new string('0', 40))` when the body or its SHA is null; expect LC.`C495_V2RouteRejectsFreshMissingApproval` red at `StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity)` (202 observed) and at the zero-row assertion.
- PC-12: break G-12 by replacing the SHA equality with `$true` (or length equality); expect SV T6 red at `exit code is 5` (0 observed) and at `lock file retained`.
- PC-13: break G-13 by setting verified when the version probe returns no body; expect SV T7 red at `exit code is 5`.
- PC-14: break G-14 by deleting the comparison between the explicit `-ExpectedServerSha` and source-root HEAD (accept the supplied value); expect SV T4 red at `exit code is 3` (5 observed because the fixture's version answers HEAD) and at `teardown call count is 0`.
- PC-15: break G-15 by moving the expected-SHA resolution to after the port teardown; expect SV T4 red at `teardown call count is 0` and SV T3 red at the same assertion.
- PC-16: break G-16 (a) by deleting the fresh HEAD read at verification; expect SV T10a red at `exit code is 5` (0 observed). (b) by assigning the fresh HEAD to the expected SHA before comparing; expect SV T10b red at `exit code is 5`. Both variants are executed.
- PC-17: break G-17 by setting `$keepRestartLock = $false` on the exit-5 path; expect SV T6 red at `restart lock retained after exit 5`.
- PC-18: break G-18 by adding `if ($NoBuild) { $identityVerified = $true }`; expect SV T12 red at `exit code is 5`.
- PC-19: break G-19 by calling `Stop-AppHostLaunchChild` on the exit-5 path; expect SV T6 red at `launch child stop count is 0`.
- PC-20: break G-20 by changing `Invoke-ChildPowerShell` to ignore exit 5 (`-ne 0 -and -ne 5`); expect SV T16 red at `last line is DEPLOY VERDICT: failed restart-apphost.ps1 exited 5`.
- PC-21: break G-21 by moving the worktree guard below `New-AppHostLock`; expect WG red at `restart entry exits before restart banner` (decisive) and at `restart entry leaves no restart lock` when the lock survives.
- PC-22: break G-22 by proceeding when `New-AppHostLock` reports not acquired; expect SV T22 red at `teardown call count is 0` and `exit code is 3`.
- PC-23: break G-23 by deleting the `-ne $srPid` filter in the port loop; expect SV T21 (seam reports the same fake PID on 17204 and 17202) red at `session-runner PID never passed to Stop-AppHostProcessId`.
- PC-24: break G-24 by removing the child stop and `$keepRestartLock = $false` on the BuildFailed branch; expect SV T13 red at `child stop count is 1` and `lock removed`.
- PC-25: break G-25 by changing the DCP branch to `exit 1` and `$keepRestartLock = $false`; expect SV T14 red at `exit code is 4` and `lock retained`.

Each PC report line records: mutated file and diff, expected assertion, red result with the exact failing assertion text, restore evidence (timestamp refresh; rebuilt DLL for PC-11), green result with the same filter, wall time. Never commit a mutant.

### Out of scope

- Periodic API staleness visibility and strict unmapped-JSON rejection: deferred to the two follow-up cards named in the plan; no test here asserts either.
- Live incident replay, a live land request, a canonical-stack restart, a real Aspire launch, the production runner, browser or E2E native tests: none is needed for these slices; activation evidence is caller-owned after landing (first-install steps 3-5).
- Full Cartesian product of version-response defects with every CLI parameter combination: each defect is exercised once with a full body, and the bodyless path once with a compatible server; the probe code is shared, so orthogonal combinations add no new branch.
- Combining every restart failure kind with `-NoBuild` and with an explicit `-ExpectedServerSha`: T12 covers `-NoBuild` with the mismatch branch and T2 covers explicit-equal; other combinations share the same branches.
- A SHA-256 fixture repository for the restart script: the fixture is SHA-1, so a 64-hex explicit value is exercised only as a well-formed-but-different value (T3), and 64-hex acceptance is proved in the CLI matrix.
- The `AgentTaskLandingProtocol`, source resolver, migrations, session delivery and frontend: untouched by the plan.
- Diagnosability of a pre-observation `LandRefused` whose `expected/local/remote/candidate` fields are all null and which carries no operation id (observed twice on 2026-09-11 after an AppHost restart). Reading `AgentTaskLandSourceResolver.ResolveAsync` and `AgentTaskLandService.RunRequestAsync`: every refusal raised before `git.InspectAsync` populates the request (`stale_land_request`, `landing_schema_unsupported`, `legacy_review_binding_required`, `source_coordinates_missing`, `request_coordinates_changed`, `repository_lease_required`, `landing_io_error`, `commit_lookup_failed`, and an inspection failure with a null snapshot whose Git detail collapses to `inspection.Reason ?? "source_unknown"`) prints that shape, and `landing_protocol_unavailable` prints no evidence at all. The event line carries only the reason code, not the inspected path or the failing Git command/exit/stderr summary, and `-Status` does not print `SourceRefusalReason`. Follow-up card candidate; not this card's scope.

### Cost

All figures are **estimated** floors unless marked measured. Measured calibration on this machine at `67ee59d6`: one real `delegate.ps1` invocation against a dead port 4.1 s; `test-apphost-probe-class.ps1` 2.7 s.

#### Ordinary Code V/R floor

| Group | Selection | Minutes |
|---|---|---:|
| Setup/build | `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c495/ --nologo` (cold) | 8 (estimated) |
| HTTP contract | `/*/*/(HealthEndpointTests*)\|(AgentTaskLandContractEndpointTests*)\|(SmokeTests*)/*` (factory boot + DB clone + 11 methods) | 4 (estimated) |
| CLI compatibility | `/*/*/(DelegateScriptLandCompatibilityTests*)\|(DelegateScriptLandApprovalTests*)\|(DelegateScriptLandStatusTests*)/*` (about 50 serialized pwsh runs at 4 s plus the 4 s and 8 s timeout cases) | 5 (estimated from measured 4.1 s per run) |
| Service admission regression | `/*/*/AgentTaskLandApprovalRequestTests/*` | 2 (estimated) |
| Script lane | `scripts/test-apphost-server-version.ps1` (about 24 cases, each a child pwsh of 3-5 s), `test-apphost-probe-class.ps1`, `test-apphost-lock-age.ps1`, `test-apphost-main-worktree-guard.ps1`, 5.1 parse check | 4 (estimated) |
| Evidence | fresh TRX per invocation, `scripts/test-duration-tripwire.ps1 -Trx`, executed-name check | 2 |
| **Code V/R floor** | | **25** |

Recipe (PowerShell; one fresh results directory per invocation; check `$LASTEXITCODE` after each):

~~~powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c495/ --nologo
$c495RunId = [guid]::NewGuid().ToString('N')
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c495/ -- --treenode-filter '/*/*/(HealthEndpointTests*)|(AgentTaskLandContractEndpointTests*)|(SmokeTests*)/*' --report-trx --report-trx-filename contract.trx --results-directory ".antiphon/c495/$c495RunId/contract"
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c495/ -- --treenode-filter '/*/*/(DelegateScriptLandCompatibilityTests*)|(DelegateScriptLandApprovalTests*)|(DelegateScriptLandStatusTests*)/*' --report-trx --report-trx-filename cli.trx --results-directory ".antiphon/c495/$c495RunId/cli"
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c495/ -- --treenode-filter '/*/*/AgentTaskLandApprovalRequestTests/*' --report-trx --report-trx-filename admission.trx --results-directory ".antiphon/c495/$c495RunId/admission"
pwsh -File scripts/test-apphost-server-version.ps1
pwsh -File scripts/test-apphost-probe-class.ps1
pwsh -File scripts/test-apphost-lock-age.ps1
pwsh -File scripts/test-apphost-main-worktree-guard.ps1
~~~

Do not run Pty tests alongside; do not use `--list-tests` or exit 0 with zero tests as coverage. Delete `bin-c495*` directories before finishing.

#### Mutation PC floor

| Lane | Controls (cycles) | Minutes per red/restore/green cycle | Subtotal |
|---|---:|---:|---:|
| CLI script (`delegate.ps1` mutated; C# test run method-scoped, no rebuild) | PC-1..PC-10 (11 cycles incl. PC-3a/b) | 2 (two TUnit runs of about 40 s plus edit/restore) | 22 |
| Server (rebuild required) | PC-11 (1 cycle) | 8 (two incremental builds of about 2.5 min plus two factory runs) | 8 |
| Restart scripts (`-Case` scoped) | PC-12..PC-25 (15 cycles incl. PC-16a/b) | 1 (two 10-20 s script runs plus edit/restore) | 15 |
| Setup | one `bin-c495-pc/` build, PC bookkeeping | 12 | 12 |
| **Mutation PC floor** | **25 PCs, 27 cycles** | | **57** |

**Total verification floor: 25 (Code) + 57 (Mutation) = 82 minutes, estimated.** Savings from scoping: running a whole CLI class per cycle would cost about 5 min instead of 0.7 min (about 47 min saved over 11 cycles); running the whole restart script per cycle would cost about 2 min instead of 0.3 min (about 25 min saved over 15 cycles). No PC is skipped for cost; the two-variant controls (PC-3, PC-16) are the only added cycles beyond one per guard.
