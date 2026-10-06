# CARD-1087: rolling redeploy-old recycle census

Date: 2026-10-06. Plan task: `ee75c043-85be-4d3e-85a2-3bd9e29b326d` (Frontier, Linux runner mirror).
Inspected source: `d985af05b5ace2f13ec3f0d886c15992e9c13c3f` (the paused rollout's SHA).
Assigned branch: `feat/card-task-ee75c043`; fast-forward-only from that source.
Status: Plan complete with the verification design folded in. **next: land**, then Code
runs the `### Checkpoints` table as a closed list.

## Outcome and scope

The staged server2 rollout is paused at gate 6 (`redeploy-old`): old `server2` is
drained and idle, `server2-temp` serves at `d985af05`, and the phase refuses
`RecycleTaskCensusUnknown` three times over (Debug task `9501d319`, 2026-10-06).
After this card:

1. `Get-RecycleProjectId` resolves the Antiphon project from repository identity
   (canonical Git URL **or** canonical checkout path), with an explicit `-ProjectId`
   escape hatch, and never needs a project PUT.
2. The task census reads two small server-filtered closures (open statuses; pending
   lands) instead of the 12,143-row unfiltered list, with a read timeout that fits and
   a persisted, redacted census receipt.
3. Every refusal names its real cause (`cause=Timeout|Http|Transport|Empty|Malformed|
   UnfilteredRow|Unstable`, `RecycleProjectUnresolved cause=NoMatch|Ambiguous|...`)
   while keeping the stable leading reason code the docs, tests and host bridge pin.
4. Terminal `Succeeded`/`Failed`/`Canceled` rows are not bound work. `Queued`,
   `Dispatched`, `Working` and `Blocked` rows (parked Blocked tasks are still `Blocked`)
   bound to the target runner still refuse; any pending land still refuses.
5. Every refusal stays fail-closed: no host case runs, nothing is stopped or removed,
   the publication audit and volume gates are untouched.

The same census runs **twice** today, once in the PowerShell wrapper
(`scripts/deploy-server2.ps1`) and once on the host (`c1008_tasks` in
`scripts/c590-remote.sh`, also reused by `retire-temp-containers`). Both change
together, or the host still reads 12,143 rows through `curl --max-time 15`.

Out of scope: `-RecycleRunnerState`/`-RecycleCaches` (CARD-1010), a server-side
maintenance lease (CARD-1008 explicitly declined to invent one), a promotion gate
(CARD-0935), and any change to drain, stop, removal or disk-reclaim logic.

## Ground truth

Live reads were made against the desktop API on 2026-10-06 ~03:30Z with the task token;
nothing was written.

| Card / Debug assumption | What the code and the live system do | Consequence for the plan |
|---|---|---|
| The project must match `gitRepositoryUrl == https://github.com/michal-ciechan/Antiphon.git`. | `scripts/deploy-server2.ps1:384-389` requires exactly one such row among `GET /api/projects?includeArchived=true`. Live Antiphon row `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`: `gitRepositoryUrl=""`, `localRepositoryPath="C:/src/Antiphon"`, `name=Antiphon`, not archived. | Resolve by URL **or** by canonical checkout path; the live row resolves by path with no write (D-1). |
| Setting the URL needs a full project update. | `UpdateProjectRequest` requires `Name` and `GitRepositoryUrl`; `ProjectService.UpdateAsync` writes `LocalRepositoryPath` from the request, so an omitted path clears it (`ProjectService.cs:174-177`). | Confirmed; the plan does not require any PUT. The UI's full-row PUT remains an operator option, not a prerequisite. |
| The census reads everything. | Wrapper `Assert-RecycleTaskCensus` (`deploy-server2.ps1:391-478`) and host `c1008_tasks` (`c590-remote.sh:3914-3976`) both read `/api/agent-tasks?projectId=<id>&unscoped=include&includeChecks=true` with no filter and then `GET /api/agent-tasks/{id}` for **every** row. Debug: 12,143 items, 18.5 MB, 90.8 s. | Filtered closures (D-5). Live filtered reads: open statuses 4 rows, 6.8 KB, 0.13 s; pending-land rows 0. |
| `Invoke-RecycleRead` allows 15 s and hides the cause. | `deploy-server2.ps1:366-382`: `-TimeoutSec 15`; every exception rethrown as the bare `RecycleTaskCensusUnknown`; body never persisted. Host `c1008_http` uses `curl --max-time 15` and returns 2 for any failure. Debug: the projects read alone took 10.7 s. | Typed causes, 60 s per read, redacted receipt (D-3, D-4); host parity (D-7). |
| 141 `Failed` rows on server2 refuse as bound. | Bound set is `Queued,Dispatched,Working,Blocked,Failed` in both wrapper (`:450`) and host (`:3957`). Live `status=Failed,Canceled` read: 3,586 rows, 223 on `server2`, 17 on `server2-temp`. Live open rows: 3 Dispatched + 1 Blocked, all on `server2-temp`. | Drop `Failed` from the bound set (D-5). Unpublished work in a Failed task's worktree stays protected by the host publication audit (D-9). |
| A `since`/`updatedSince` window would do. | `LoadListCandidatesAsync` (`AgentTaskService.cs:2394-2402`): `since` keeps every non-settled row and trims settled rows by `CompletedAt`. A land requested on a task that succeeded days ago is a settled row outside the window, yet `LandRequestedAt`/`LandStartedAt` are non-null until `ClearPending` (`AgentTaskLandService.cs:1363-1368`). | `since` cannot see old pending lands. The plan adds a server `landPending` filter (D-6) instead. |
| A server-side count could replace the list. | `GET /api/agent-tasks/summary` (0.38 s live) hides specialists (`AgentTaskRoles.NotSpecialist`) and has no `runnerId` or land breakdown. | Not sufficient on its own; rejected as the census source (D-5). |
| The list supports paging. | No pagination; unknown query keys are `400 unknown_query_parameter` (`AgentTaskEndpoints.cs:540-552`, `AgentTaskScope.SupportedListQueryKeys`). | No invented page parameters; filtering is the only size control. |
| Filtering breaks the exclusion closure. | `ListAsync` partitions **filtered** candidates: `excluded.byProject` counts only rows matching the filter. Live: `status=Queued,...` returned `excluded.total=2` for one other project. | Walking `excluded.byProject` with the same query keeps the closure consistent; the existing count reconciliation still applies. |
| "Parked" rows must count as bound. | There is no parked task status. A CARD-1065 parked task stays `Blocked`; parked **messages** are queue rows (`SessionQueueDtos.cs:26`), not tasks. | `Blocked` covers it; the doc says so. |
| Only the wrapper needs fixing. | The host census (`c1008_tasks`) runs after the wrapper's, inside `deploy-parent` and `retire-temp-containers` (`c590-remote.sh:4049-4052`, `:4915-4918`), through `C604_SERVER_ORIGIN` (`https://antiphon.desktop.codeperf.net`). Its snapshot is journaled and compared on resume. | S5 changes the host census identically. No `-ResumeRecycle` journal exists today (Debug), so the snapshot shape change strands nothing. |
| CARD-1008 forbade any status/since window. | `2026-10-03-card-1008-...-plan.md:162-166` and the code comment at `deploy-server2.ps1:393-394`. That rule protected completeness; it predates the measured 90 s read and assumed no server-side pending-land filter. | Superseded here by D-5/D-6 with the completeness argument stated. The code comment is rewritten. |

Routing facts read for lane selection (`GET /api/runner-defaults`, `GET /api/session-runners`):
global default `server2`, which is draining with `redirectTo=server2-temp`; `server2-temp`
(linux) accepts at `d985af05`, capacity 10, occupied 4; `desktop` (windows) accepts,
capacity 2. The Code task needs native Linux for the host-lane rows (CP-5, CP-7 call
`C1008HostFixture.RequireNativeLinux()`), so dispatch with `-Platform Linux` and no
`-Runner`; the drain redirect places it on temp today and on main after the rollout.

## Decisions

- **D-1 Project identity, not a single URL.** `Get-RecycleProjectId` reads
  `GET /api/projects?includeArchived=true` once and selects rows where either
  (a) `gitRepositoryUrl`, normalized (trim; lower-case host; `git@github.com:` form and
  `.git` suffix folded), equals the canonical repository
  `https://github.com/michal-ciechan/antiphon`, or (b) `localRepositoryPath`, normalized
  (`[IO.Path]::GetFullPath`, trailing separators trimmed, `/`→`\` and case-insensitive on
  Windows, ordinal on Linux), equals the script's own `$repoRoot`. Exactly one distinct,
  non-archived project must match. The receipt records `resolvedBy=url|path|both`.
  *Why:* the docs already require running from the canonical desktop checkout, and the live
  row carries exactly that path, so the rollout resolves today with no write.
  *Rejected:* matching by `name` (not identity; names are editable labels);
  `git remote get-url origin` (nondeterministic in test checkouts and the script is
  Antiphon-specific already); requiring the URL PUT (the card's explicit non-goal).
- **D-2 Explicit `-ProjectId` as the documented escape hatch.** New optional parameter
  `[string]$ProjectId` (lower-case GUID, validated in-script like `$SavedDonor`). When
  given, the script reads `GET /api/projects/{id}` and requires an existing, non-archived
  row; it records `resolvedBy=explicit` and the row's `name`. The identity match is
  **not** required for an explicit id, because the census closure is fleet-wide
  (`unscoped=include` plus every `excluded.byProject` scope), so the entry project cannot
  hide another project's work; the id's role is the entry point and the journal field.
  *Rejected:* an environment variable (invisible in receipts); requiring identity match
  (would make the escape hatch useless exactly when identity is unresolvable).
- **D-3 Typed causes behind a stable leading code.** Refusal messages keep their first
  token (`RecycleTaskCensusUnknown`, `RecycleBoundTasks`, `RecycleLandInFlight`) so the
  CARD-1008 refusal table, `C1008_Wrapper_refusal_receipts_do_not_leak_secrets`, the host
  bridge and operators' runbooks stay valid, and append space-separated `key=value`
  tokens naming the failure (vocabulary below). A new code `RecycleProjectUnresolved`
  covers resolution, which is not a census read. Messages never include a response
  body, header, base URL or exception text; the API path (projectId plus filter) is the
  only free text.
  *Rejected:* distinct new codes per cause (breaks every existing pin for no gain);
  printing exception messages (can carry URLs with credentials).
- **D-4 Read budget 60 s per read and a redacted receipt.** `Invoke-RecycleRead` times
  each call with a stopwatch, uses `-TimeoutSec 60`, and classifies the outcome. Every
  census writes `census-<phase>-<runner>.json` under the run's evidence root (and, like
  other receipts, the copied `.antiphon/rolling-server2/<run-id>/`) containing per-read
  `path`, `elapsedMs`, `outcome` (`ok` or the cause tokens), `items`, `excludedTotal`,
  plus the reduced snapshot rows (the seven census fields and the land proof). On refusal
  the partial receipt is still written. Titles, goals, results, tokens and bodies are
  never persisted.
  *Why:* the filtered reads are sub-second; 60 s absorbs the measured 10.7 s projects
  read with margin while still bounding a hung server. *Rejected:* keeping 15 s (fails
  the live projects read); unbounded.
- **D-5 Two filtered closures; terminal rows are not bound.** For target runner `R`
  and entry project `P`, the census walks two closures, each starting at `P` and
  following `excluded.byProject` with the **same** query:
  - **Open**: `/api/agent-tasks?projectId=<scope>&unscoped=include&includeChecks=true&status=Queued,Dispatched,Working,Blocked`.
    Every row must have one of those statuses, else `cause=UnfilteredRow`. A row with
    `runnerId == R` refuses `RecycleBoundTasks <id> status=<s>`. Rows get the existing
    detail read (`GET /api/agent-tasks/{id}`), summary equality and `landRequest`
    state checks.
  - **Land**: `/api/agent-tasks?projectId=<scope>&unscoped=include&includeChecks=true&landPending=true`.
    Every row must have `landRequestedAt` or `landStartedAt` non-null, else
    `cause=UnfilteredRow`; any row refuses `RecycleLandInFlight <id>` (as today).
  Scope limit (1000), envelope shape checks, exclusion count reconciliation and the
  two-pass snapshot comparison stay, now over both closures (`cause=Unstable` on
  mismatch). `Succeeded`, `Failed` and `Canceled` rows never enter the open closure and
  are therefore never bound.
  *Why:* bound work is by definition non-terminal; a terminal task owns no live seat.
  Its worktree can still hold unpublished commits, and that is already the host
  publication audit's job (D-9), not the census's.
  *Rejected:* `since` (misses old pending lands); the summary endpoint (no runner/land
  facts, hides specialists); the unfiltered read with a longer timeout (90 s and
  growing; the card rejects it); client-side paging (none exists).
- **D-6 Server: `landPending` list filter.** Add `landPending` (bool) to
  `AgentTaskScope.SupportedListQueryKeys`, the `GET /api/agent-tasks` parameters and
  `LoadListCandidatesAsync` (`t.LandRequestedAt != null || t.LandStartedAt != null`),
  composable with `status`, `since`, `includeChecks` and scope. Document it in
  `docs/antiphon-api.md`; the client type gains an optional field and no UI change.
  An older server answers `400 unknown_query_parameter`, which the census surfaces as
  `cause=Http status=400 path=...` and refuses. The rollout pre-flight already requires
  the running server's `/api/version` SHA to equal canonical `HEAD`, so once this card
  lands and AppHost restarts, both the wrapper and the host read a server that has it.
  *Rejected:* a dedicated census endpoint (more surface for one caller); encoding the
  predicate into `status` (lands are not a status).
- **D-7 Host parity.** `c1008_tasks` implements the same two closures, filter checks,
  bound rule and snapshot (`{open:{scopes,tasks,land},land:{scopes,tasks}}`), with
  `curl --max-time 60`. `c1008_http` returns a typed cause through a variable the caller
  folds into the refusal (`RecycleTaskCensusUnknown cause=... path=...`); return codes 3
  (land) and 4 (bound) keep their meaning. Both `deploy-parent` and
  `retire-temp-containers` callers inherit it. Resume comparison of the journaled
  `.tasks` snapshot is unchanged in mechanism; a pre-change journal would mismatch and
  refuse, and none exists.
- **D-8 Read-only pre-flight phase `check-census`.** New `-Phase check-census` (no SSH,
  no host jq, no POST, no host case) resolves the project (D-1/D-2), runs both closures
  for `server2` and `server2-temp` in report mode, and prints
  `RECYCLE_PROJECT id=<guid> name=<name> resolvedBy=<how>` and one
  `RECYCLE_CENSUS runner=<id> open=<n> boundOpen=<n> landPending=<n> scopes=<n> elapsedMs=<n>`
  per runner. It refuses only on resolution or read failures; bound/land counts are
  reported, not refused, because it authorizes nothing. `docs/docker-stack.md` lists it
  under "Before starting" so the three CARD-1087 failures surface before gate 1 instead
  of after old is drained.
  *Rejected:* folding the census into `deploy-temp` (changes a gate's semantics);
  documentation-only pre-flight (the Debug found the cause only by reading code).
- **D-9 Safety boundaries unchanged.** Zero counters, drain/redirect/routing proofs,
  rollout lock, the UID-1654 publication audit (`c590-remote.sh:4064-4141`, which walks
  `git worktree list` of every repository on the work volume and `rev-list --count tip
  --not <remotes>`, independent of task status), volume reference checks, `-DryRun`
  preview and `-ResumeRecycle` all stay as CARD-1008/0994 specified. A census refusal
  still exits 2 before any host case (trace has no `case`). The stub protocol follows
  the existing `__404__`/`__503__` convention of `Invoke-RunnerRequestCore`.

### Refusal vocabulary (exact first tokens; later tokens are `key=value`)

| Message | When |
|---|---|
| `RecycleProjectUnresolved cause=NoMatch candidates=<n> urlMatches=0 pathMatches=0` | No project matches either identity rule and no `-ProjectId`. |
| `RecycleProjectUnresolved cause=Ambiguous candidates=<n> matches=<m>` | More than one distinct project matches. |
| `RecycleProjectUnresolved cause=Archived id=<guid>` | The only match, or the explicit id, is archived. |
| `RecycleProjectUnresolved cause=NotFound id=<guid>` | Explicit id answers 404. |
| `RecycleProjectUnresolved cause=InvalidId` | `-ProjectId` is not a lower-case GUID. |
| `RecycleProjectUnresolved cause=Malformed field=<name>` | A project row lacks `id`/`archivedAt`/identity fields or `id` is not a GUID. |
| `RecycleTaskCensusUnknown cause=Timeout path=<path> elapsedMs=<n>` | Read exceeded 60 s (or stub `__TIMEOUT__`). |
| `RecycleTaskCensusUnknown cause=Http status=<n> path=<path>` | Non-2xx (stub `__<code>__`). |
| `RecycleTaskCensusUnknown cause=Transport path=<path>` | Connection/DNS/TLS failure (stub nonzero exit). |
| `RecycleTaskCensusUnknown cause=Empty path=<path>` | 2xx with an empty body. |
| `RecycleTaskCensusUnknown cause=Malformed field=<field> path=<path>` | Envelope/row/detail shape or type violation; `field` names the first offending member (existing checks keep their order). |
| `RecycleTaskCensusUnknown cause=UnfilteredRow id=<guid> status=<s> path=<path>` | A row outside the requested filter (open: status; land: both land timestamps null). |
| `RecycleTaskCensusUnknown cause=Unstable` | Pass 2 snapshot differs from pass 1. |
| `RecycleTaskCensusUnknown cause=ScopeLimit scopes=<n>` | Closure exceeded 1000 scopes. |
| `RecycleBoundTasks <id> status=<s> runner=<runnerId>` | Open row bound to the target runner. |
| `RecycleLandInFlight <id>` | Any land-closure row, or an open row whose detail `landRequest.state` is non-terminal (unchanged). |
| `RecycleLandUnknown` | Detail/summary disagreement or malformed land proof (unchanged). |

`RecycleProjectUnresolved` and the `cause=` tokens join the CARD-1008 refusal table in
`docs/docker-stack.md`; `RecycleBoundTasks`/`RecycleTaskCensusUnknown` rows are reworded
to "open (`Queued/Dispatched/Working/Blocked`) target-bound task IDs" and "unavailable,
timed out, malformed or unstable response, with its cause".

### Fixture protocol (offline, no network)

- `scripts/fixtures/c727-fake-http.ps1`: `projects` (array) replaces the hard-coded
  single row when present; `projectDetail` map serves `/api/projects/{id}`; `readFaults`
  map of path-substring → `timeout|503|400|401|transport|empty|malformed` emits
  `__TIMEOUT__`, `__503__`, exit 2, blank or `{` respectively (`taskError` keeps meaning
  transport); `taskScopes.<projectId>` envelopes are filtered by `status` /
  `landPending` from the requested query before being returned, and an optional
  `taskQueries.<full-query-string>` map takes precedence for exact control. Every
  request is already traced to `C727_TEST_TRACE` with its `path`; tests assert the exact
  paths requested and that no unfiltered path is ever requested.
- Host fixture (`C1008HostFixture` injection of `c1008_http`): key `tasks.json` scopes by
  projectId and filter kind (`open`/`land`) with a jq fallback that filters the flat
  envelope; append each requested path to `$C1008_FIXTURE_ROOT/http-trace`; honour a
  `faults` map (`timeout` → sleep past a fixture-shortened `C1008_HTTP_MAX_TIME`, `http`
  → non-200, `transport` → nonzero) so the host's typed causes are provable without
  network.
- `C994TaskVectors` inputs gain `scopes.<pid>.<kind>` and the harness override of
  `Invoke-RecycleRead` keys on the query string, not only `projectId=`.
- `scripts/fixtures/c1008-recycle-real-cases.mjs` `late-land`/`c994-late-land`/
  `c994-late-task` faults insert their rows into the matching filtered scope.

## Slices (each a 30-60 minute Code task; commit and push after each)

| Slice | Files | Behaviour and tests |
|---|---|---|
| **S1 Typed reads and receipt** | `scripts/deploy-server2.ps1` (`Invoke-RecycleRead`, receipt writer), `scripts/fixtures/c727-fake-http.ps1` (`readFaults`, sentinels), `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs` | D-3/D-4 for every read site. New `C1087_Read_failures_name_their_cause`: for each fault, `retire-temp` exits 2, output contains the exact `cause=` tokens and `path=`, no `case` in trace, no `SENTINEL`/token in output or receipt, receipt file exists with the failed read's `outcome`. Existing no-leak tests stay green. |
| **S2 Project resolution** | `scripts/deploy-server2.ps1` (param block, `Get-RecycleProjectId`, normalizers), `scripts/fixtures/c727-fake-http.ps1` (`projects`, `projectDetail`), same test class | D-1/D-2. New `C1087_Project_resolves_by_repository_identity`: url-only, path-only (fixture path = `DelegateScriptRunner.RepoRoot`, mixed separators), both, URL case/`.git`/SSH variants, zero → `NoMatch` with counts, two matches → `Ambiguous`, archived-only → `Archived`, explicit id ok / 404 / archived / invalid; receipt `resolvedBy`. Default fixture row still resolves (regressions). |
| **S3 Server `landPending`** | `server/Application/Services/AgentTaskScope.cs`, `server/Application/Services/AgentTaskService.cs` (`ListAsync`, `LoadListCandidatesAsync`), `server/Api/Endpoints/AgentTaskEndpoints.cs`, `client/src/api/agentTasks.ts` (optional option only), `docs/antiphon-api.md`, `tests/Antiphon.Tests/Application/AgentTaskScopedListEndpointTests.cs` | D-6. New `Land_pending_filter_selects_pending_rows`: seeds a Succeeded row with `LandRequestedAt`, a Succeeded row with `LandStartedAt`, a Succeeded row with neither, a Working row with neither; `landPending=true` returns exactly the first two (scoped to the test's project), `excluded.byProject` counts only pending rows of another seeded project, `landPending=true&status=Working` returns none, `landPending=maybe` is 400/422 as the framework binds, and `Unknown_list_query_keys_are_refused` still lists the supported set including `landPending`. |
| **S4 Wrapper census** | `scripts/deploy-server2.ps1` (`Assert-RecycleTaskCensus`, comment rewrite), `scripts/fixtures/c727-fake-http.ps1` (query filtering, `taskQueries`), `tests/Antiphon.Tests/Scripts/C994TaskVectors.cs`, `RetiredTempContainerScriptTests.cs` (harness override keyed by query), `RollingVolumeRecycleScriptTests.cs` | D-5. New `C1087_Census_filters_open_work_and_pending_lands`: trace shows exactly the two filtered paths per scope and never an unfiltered one; Failed/Canceled/Succeeded rows bound to the target (served only when the query asks for them) accept; Queued/Dispatched/Working/Blocked bound rows refuse `RecycleBoundTasks <id> status=`; a Succeeded row with `landRequestedAt` served only under `landPending` refuses `RecycleLandInFlight`; a Succeeded row returned under the open query refuses `UnfilteredRow`; closure through an excluded scope uses the same filter; pass-2 change refuses `Unstable`. Vector changes: `bound-Failed` → accepted; add `land-pending-succeeded` (refused), `unfiltered-row` (refused), `bound-Canceled` (accepted). `C1008_Busy_routed_and_land_in_flight_refuse`: the per-status loop keeps Queued/Dispatched/Working/Blocked and the Succeeded-with-land row refusing; `Failed` moves to an accepting assertion. |
| **S5 Host census parity** | `scripts/c590-remote.sh` (`c1008_http`, `c1008_tasks`, both callers), `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs` (`C1008HostFixture` injection: kind-keyed `c1008_http`, http-trace, faults), `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`, `scripts/fixtures/c1008-recycle-real-cases.mjs` | D-7. New `C1087_Host_census_filters_and_names_cause`: http-trace holds exactly the two filtered paths per scope; terminal bound rows accept, open bound rows refuse `RecycleBoundTasks`, land rows refuse `RecycleLandInFlight`; `timeout`/`http`/`transport` faults refuse with `RecycleTaskCensusUnknown cause=...` and `f.Removed` empty. `C1008_Recycle_refuses_references_and_unknown_census`: the `terminal` and `active-land` faults move their row to `runnerId=other`, `status=Blocked` so the open closure still reads its detail; `bound-task` stays Blocked. `retire-temp-containers` path exercised through the existing `C994_*` host methods. |
| **S6 Pre-flight phase and docs** | `scripts/deploy-server2.ps1` (`check-census`, `-Report` mode), `docs/docker-stack.md`, `tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs`, `RollingVolumeRecycleScriptTests.cs` | D-8. New `C1087_Check_census_phase_is_read_only`: exits 0 with both `RECYCLE_*` lines, trace has no POST and no `case`, no SSH/verify stub call, bound rows are counted not refused, resolution/read failures still exit 2 with their cause. New `Recycle_census_preflight_is_documented` pins `check-census`, `RecycleProjectUnresolved`, `landPending` and the open-status bound set in the CARD-0934/1008 sections. |

Slices S1-S3 are independent of S4-S6 and are verified together (CP-1, CP-2); S4-S6
are verified at the final SHA (CP-3..CP-7). A Code task may take S1-S3 or S4-S6 as one
dispatch when its budget allows; the checkpoint groups do not change.

### Operator pre-flight (docs/docker-stack.md, "Before starting")

Add to the CARD-0934 preamble, before `check-host-jq`:

```powershell
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase check-census
```

Expect `RECYCLE_PROJECT ... resolvedBy=path|url|both` and two `RECYCLE_CENSUS` lines.
`RecycleProjectUnresolved cause=NoMatch` means the project row carries neither the
canonical Git URL nor this checkout's path: set either field through the project settings
UI (a full-row PUT) **or** pass `-ProjectId <guid>` to every later phase and record it in
the rollout receipt; never edit the row with a partial PUT. `cause=Http status=400`
means the running server predates `landPending`: finish landing and restart AppHost
first (the `/api/version` SHA check already requires this). Terminal `Failed`/`Canceled`
tasks bound to a runner no longer block recycling; their worktrees are still audited for
unpublished work by the host publication gate, which is why `check-census` does not
list them.

## Verification design

Coverage-to-class. All TUnit rows run in `tests/Antiphon.Tests`; script classes are
`Unit` and spawn `pwsh`/`bash` under `ParallelLimiter<ProcessSpawnLimit>`; the host and
real-Docker rows need native Linux and skip elsewhere, so the Code dispatch is
`-Platform Linux` with no `-Runner`.

| ID | Class.Method | Behaviour proven |
|---|---|---|
| V-1 | `RollingVolumeRecycleScriptTests.C1087_Read_failures_name_their_cause` | D-3/D-4: timeout, HTTP, transport, empty, malformed each produce their exact `cause=`/`path=` tokens, exit 2, no host case, redacted receipt written, no secret or body leaks. |
| V-2 | `RollingVolumeRecycleScriptTests.C1087_Project_resolves_by_repository_identity` | D-1/D-2: URL and path identity, normalization variants, NoMatch/Ambiguous/Archived/NotFound/InvalidId, explicit id, `resolvedBy` receipt. |
| V-3 | `AgentTaskScopedListEndpointTests.Land_pending_filter_selects_pending_rows` | D-6: predicate, composition with `status` and scope, exclusion counts over filtered rows, supported-key list. |
| V-4 | `RollingVolumeRecycleScriptTests.C1087_Census_filters_open_work_and_pending_lands` | D-5: exact filtered paths, terminal rows not bound, open rows bound, land rows refused, `UnfilteredRow`, closure with same filter, `Unstable`. |
| V-5 | `RetiredTempContainerScriptTests.C994_Wrapper_task_and_land_census_is_complete` (updated `C994TaskVectors`) | D-5 matrix: `bound-Failed`/`bound-Canceled` accepted; `land-pending-succeeded`, `unfiltered-row` refused; all CARD-0994 vectors keep their verdicts. |
| V-6 | `RemoteScriptContractTests.C1087_Host_census_filters_and_names_cause` | D-7: host requests the two filtered paths only, same bound/land verdicts, typed causes for timeout/http/transport, nothing removed. |
| V-7 | `RollingVolumeRecycleScriptTests.C1087_Check_census_phase_is_read_only` | D-8: report lines, no POST/case/SSH, counts instead of refusals, failures still typed. |
| R-1 | `RollingVolumeRecycleScriptTests.*` (12 existing) | Rollout wrapper regressions incl. `C1008_Wrapper_refusal_receipts_do_not_leak_secrets` (leading code survives), `C1008_Busy_routed_and_land_in_flight_refuse` (reshaped Failed assertion), `C1008_Legacy_rolling_and_jq_rosters_remain`. |
| R-2 | `AgentTaskScopedListEndpointTests.*` (10 existing) | Scope/unknown-key/summary contracts unchanged. |
| R-3 | `RetiredTempContainerScriptTests.C994_*` (13) | Retire/drain wrapper gates unchanged. |
| R-4 | `RemoteScriptContractTests.C1008_*` (11) | Host recycle gates unchanged; `C1008_Recycle_refuses_references_and_unknown_census` with reshaped land vectors. |
| R-5 | `DockerStackDocumentationTests.*` (12 existing + `Recycle_census_preflight_is_documented`) | Doc pins incl. `Main_volume_recycling_is_scripted_only`. |
| R-6 | `RollingVolumeRecycleDockerTests.C1008_Real_docker_comparison` | Real isolated Docker/Git lifecycle with the fixture's filtered task scopes; owned residue 0. |

### Positive controls (SourceLanding Mutation, method-scoped, one per behaviour)

| PC | Mutation (restore after) | Red witness |
|---|---|---|
| PC-1 | Re-add `'Failed'` to the wrapper bound set. | V-4 (`bound-Failed` accepting vector) and V-5. |
| PC-2 | Skip the land closure in the wrapper. | V-4 (`land-pending-succeeded` served only under `landPending`). |
| PC-3 | Replace the typed cause with the bare `RecycleTaskCensusUnknown`. | V-1 (exact `cause=Timeout`/`status=503` tokens absent). |
| PC-4 | Remove the path identity rule from `Get-RecycleProjectId`. | V-2 (path-only vector refuses `NoMatch`). |
| PC-5 | Drop the `landPending` predicate in `LoadListCandidatesAsync` (accept the key, ignore it). | V-3 (rows without pending land returned). |
| PC-6 | Remove `&status=...` from the host open query. | V-6 (http-trace shows an unfiltered path; fixture serves a Failed row the test expects not to be requested). |
| PC-7 | Delete the `UnfilteredRow` check in the wrapper. | V-4 (`unfiltered-row` vector accepted) and V-5. |
| PC-8 | Make `check-census` throw on `boundOpen>0`. | V-7 (counted, not refused). |

Mutation runs after land under the SourceLanding rules (external evidence, no commits).

### Cost

Ordinary Code floor is the `EstimatedMinutes` sum, 58 minutes of checkpoint time, plus
authoring: S1-S3 roughly 90-150 minutes, S4-S6 roughly 120-180 minutes. One checkpoint
run per committed group (`--rows CP-1,CP-2` after S1-S3; `--rows CP-3,CP-4,CP-5,CP-6,CP-7`
after S6), each through `scripts/build-slot.ps1`, each with the exact committed SHA:

```powershell
$planPath = 'docs/superpowers/plans/2026-10-06-card-1087-recycle-census-plan.md'
$candidateSha = git rev-parse HEAD
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1087-cp1-2 -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c1087-tool/ -- run --plan $planPath --rows CP-1,CP-2 --expected-source-sha $candidateSha --row-timeout 15m --total-timeout 40m
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1087-cp3-7 -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c1087-tool/ -- run --plan $planPath --rows CP-3,CP-4,CP-5,CP-6,CP-7 --expected-source-sha $candidateSha --row-timeout 20m --total-timeout 70m
```

Delete every `bin-c1087-*` directory before finishing. The tool bootstrap build is the
one explained unlisted build. Repeat-proof budget: at most 3 normal repetitions per
unchanged selection; none after green. No assertion or timeout is loosened to pass; a
red row is fixed and rerun as the same row.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c1087-a/` | wrapper-reads-project | `/*/*/RollingVolumeRecycleScriptTests/(C1087_Read_failures_name_their_cause*)\|(C1087_Project_resolves_by_repository_identity*)\|(C1008_Wrapper_refusal_receipts_do_not_leak_secrets*)\|(C1008_Refusal_receipts_do_not_leak_secrets*)` | V-1, V-2, R-1 | exact 4 methods, 0 failed/skipped | 4 | 10 | true |
| CP-2 | S1-S3 | CP-1 | server-land-pending | `/*/*/AgentTaskScopedListEndpointTests/*` | V-3, R-2 | exact 11 methods (10 existing + Land_pending_filter_selects_pending_rows), 0 failed/skipped | 11 | 6 | true |
| CP-3 | all | `tests/Antiphon.Tests -> bin-c1087-b/` | wrapper-census | `/*/*/RollingVolumeRecycleScriptTests/*` | V-1, V-2, V-4, V-7, R-1 | exact 16 methods (12 existing + 4 C1087_*), 0 failed/skipped | 16 | 14 | true |
| CP-4 | all | CP-3 | wrapper-vectors | `/*/*/RetiredTempContainerScriptTests/C994_*` | V-5, R-3 | exact 13 existing methods, 0 failed/skipped | 13 | 6 | true |
| CP-5 | all | CP-3 | host-census | `/*/*/RemoteScriptContractTests/(C1008_*)\|(C1087_*)` | V-6, R-4 | exact 12 methods (11 existing + C1087_Host_census_filters_and_names_cause), 0 failed/skipped, native Linux | 12 | 9 | true |
| CP-6 | all | CP-3 | docs-pins | `/*/*/DockerStackDocumentationTests/*` | R-5 | exact 13 methods (12 existing + Recycle_census_preflight_is_documented), 0 failed/skipped | 13 | 1 | true |
| CP-7 | all | CP-3 | real-docker | `/*/*/RollingVolumeRecycleDockerTests/C1008_Real_docker_comparison` | R-6 | 1 result; all RD outcomes as recorded by the fixture, 0 failed/skipped, owned residue=0 | 1 | 12 | true |

## Risks and notes for Code and Review

- The two-pass stability check now compares a handful of rows instead of 12,143, so
  `cause=Unstable` is far rarer, but a task on the counterpart runner changing status
  between passes still refuses; rerun the phase. This is the CARD-1008 design, kept.
- `C1008_Documentation_and_transport_pins_match` and the `C1008_Legacy_rolling_and_jq_rosters_remain`
  roster (`scripts/test-deploy-server2.ps1`) must stay green when the `-Phase`
  `ValidateSet` and the fixture gain entries; adding `check-census` to the host-case
  roster is not required because it runs no host case.
- The host `c1008_http` sends no auth header today and the typed cause will make a
  future `401` visible as `cause=Http status=401`; that is a report, not a reason to add
  credentials to the host script in this card.
- `landPending` is read by two consumers (wrapper via `ANTIPHON_API`, host via
  `C604_SERVER_ORIGIN`); both point at the desktop server, so a single AppHost restart
  activates it. A land that is queued after the census passes is still outside any
  script lock, exactly as CARD-1008 states.
- Do not edit the historical CARD-1008 plan; this plan records the supersession.
