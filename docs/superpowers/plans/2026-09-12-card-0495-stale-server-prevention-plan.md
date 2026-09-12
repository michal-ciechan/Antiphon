# CARD-0495: prevent silent use of an obsolete server

Status: Plan complete; TestDesign next. Complexity: medium; require Review after Mutation because the new boundary protects landing authority.

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
