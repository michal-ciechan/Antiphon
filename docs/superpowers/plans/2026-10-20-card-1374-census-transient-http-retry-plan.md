# CARD-1374: bounded recovery for transient census reads

Date: 2026-10-20. Plan task: `cc4aebf1-a36f-435c-bddd-b75cdc8dceee`.
Inspected base: `eb7ebf5b577b0840f977a8f9f5264fc0ad4e5390` (CARD-1353/1361/1371 stack).
Read CARD-1374 on the Antiphon board and the complete investigation at
`95e2d8a15f0376c04ad1cd454f770892ad591ad1:docs/investigations/2026-10-20-card-1374-resume-audit-and-census-http.md`.
That investigation is available with `git show`; its older line numbers are superseded below.

Recover a transient task GET inside the existing census, retaining every binding,
land, shape and two-pass stability check. Verification design is folded into this
plan; next is Code. Code budget: **30-60 minutes, target 55**. No tests or builds
were run for Plan. Master publication only; RC adoption needs the user's separate
go before this can help a rollout. No live deployment is part of Code verification.

## Ground truth

All anchors below name the inspected base.

| Card assumption | What the code does | Consequence |
|---|---|---|
| One transient task GET aborts an otherwise safe resume. | Wrapper `Invoke-RecycleRead`, `scripts/deploy-server2.ps1:415`, and host `c1008_http`, `scripts/c590-remote.sh:5824`, each make one request, then refuse. | Premise confirmed; change these two transport boundaries. |
| Bounded census retry already exists. | Wrapper `:685,861` and host `:6157` restart both passes only for `cause=Drift`, three attempts with 30-second sleeps. | Reuse the bounded attempt/classification/sleep shape; retain this one drift loop unchanged. HTTP recovery retries the failed GET inside it. |
| All persistent failures already say Http. | HTTP status failures say `Http`; deadline and other request errors say `Timeout` and `Transport`. | D-3 explicitly defines the requested exhausted-retry diagnosis and required compatibility-test changes. |
| A successful audit can be reused from the journal. | Host audits at `:7886` and `:7924`; `containersRemoved` is saved at `:7923`, before audit two, and proves neither its completion nor unchanged bytes. | Audit reuse is a separate follow-up, not Code scope. |
| Existing offline tests reach the HTTP boundary. | Wrapper fake reads are in `scripts/fixtures/c727-fake-http.ps1:9`; host `scripts/fixtures/c1087-host-http.sh:33` substitutes curl while `ExerciseHttpClassifier=true` retains real `c1008_http`. | Extend these seams with per-path response sequences; do not stub out the reader being repaired. |

### Census read inventory

List paths below always include `projectId=<scope>&unscoped=include&includeChecks=true`;
the same sites follow every `excluded.byProject` scope in both census passes.

| Read | File:line | Disposition |
|---|---|---|
| Wrapper explicit project: `/api/projects/{id}` | `scripts/deploy-server2.ps1:498` | Shared reader; retain single attempt and 404-to-NotFound translation. |
| Wrapper project discovery: `/api/projects?includeArchived=true` | `scripts/deploy-server2.ps1:507` | Shared reader; retain single attempt. |
| Wrapper open list: `/api/agent-tasks?...&status=Queued,Dispatched,Working,Blocked` | `scripts/deploy-server2.ps1:715-717` | Opt into request retry. |
| Wrapper pending-land list: `/api/agent-tasks?...&landPending=true` | `scripts/deploy-server2.ps1:715-717` | Same call, other filter; opt into request retry. |
| Wrapper open-row detail: `/api/agent-tasks/{id}` | `scripts/deploy-server2.ps1:788` | Opt into request retry. Land-list rows do not fetch details. |
| Host open list, same query | `scripts/c590-remote.sh:5924-5926` | Retry in `c1008_http`. |
| Host pending-land list, same query | `scripts/c590-remote.sh:5924-5926` | Retry in `c1008_http`. |
| Host open-row detail, same route | `scripts/c590-remote.sh:6007` | Retry in `c1008_http`. |
| Adjacent wrapper runner-status admission: `/api/session-runners/{runner}/status` | `scripts/deploy-server2.ps1:332-334`, transport `:289-301` | Outside task-read scope; unchanged. |
| Adjacent host target/counterpart status | `scripts/c590-remote.sh:1577-1583`; census admission callers `:6202,6225,6262,8302,8314` | Outside task-read scope; unchanged. |

Wrapper census entry points are cleanup `:1001`, redeploy `:1252`, retirement
`:1308`, and read-only check `:1331`. Host callers are `c1008_status_proof:6266`
and `c994_status_proof:8318`, through `c1008_tasks_wait`. The fix therefore covers
initial checks, post-audit checks and removal rechecks without moving any gate.

## Decisions

| ID | Decision and reason | Rejected alternative |
|---|---|---|
| D-1 | At most **three physical attempts per task GET**, **one second** between failures, no sleep after attempt three. Keep the existing 60-second timeout per attempt: at most 182 seconds of configured network/wait budgets per logical GET, plus local processing. This is bounded recovery with short backoff. | Reuse the drift delay of 30 seconds, unbounded retries, or increasing timeout; these add avoidable delay. This is not a new total census deadline. |
| D-2 | Retry only 502/503/504 and recognized reset/timeout failures. PowerShell uses its existing timeout classification plus typed inner `SocketException` ConnectionReset and WebException ConnectionClosed/ReceiveFailure/SendFailure; curl uses 28 (timeout), 55/56 (send/receive failures, including resets). Keep other transport, DNS/TLS, all 4xx (including 408/429), other statuses, empty bodies and malformed JSON/shapes immediate. | Generic catch-and-retry, exception-message matching, curl `--retry-all-errors`, or adopting the broader server resilience policy; each widens this card's allowlist. |
| D-3 | After three retryable failures, refuse with `RecycleTaskCensusUnknown cause=Http`, exit 2, safe path, attempts=3 and last failure kind. Keep `status=<actual status>` only when known; use `failure=Timeout` or `failure=Transport` for exhausted network failures. Preserve raw per-attempt outcomes in receipts. This follows the brief's terminal Http requirement while retaining diagnosis. | Silently retaining Timeout/Transport as the exhausted public cause, fabricating an HTTP status, or losing the underlying failure kind. Nonretryable failures retain current causes. |
| D-4 | Add a default-off `-RetryTransient` switch to `Invoke-RecycleRead`, supplied only at the two task list/detail call sites. Host `c1008_http` already serves only task reads. Implement the bounded loop there, with fresh body/status/error state per attempt. | Retry project resolution, runner commands/status, the whole phase or audit; those exceed scope and can repeat expensive work. No second drift loop or retry in the land-wait wrapper. |
| D-5 | Append one existing redacted read record per physical attempt, adding an attempt number; flush wrapper receipts before sleeping. Keep raw outcome/status/timing and no body, exception prose or credentials. On host success clear stale failure/body state. | Overwrite failed observations with the successful attempt or accept a body left by a previous attempt. |
| D-6 | Native Linux offline lane with pwsh, bash and jq; omit `-Runner`, use `-Platform Linux` for Code/Review because host probes require it. Plan needs no OS pin. Both placement GETs were read on 2026-10-20: defaults revision 2 and mixed Windows/Linux eligibility; dispatch must re-read them. | Embed a fleet host/location or infer eligibility from the saved default. No Windows-specific behavior is claimed by these tests. |

These are implementation choices within the commissioned scope, with no unresolved
product decision. This change adds no session waiting for input and changes no
parking, session release or land-wait policy.

## Slices

**S1 — tests only, 15 minutes.** Extend `scripts/fixtures/c727-fake-http.ps1`
and `scripts/fixtures/c1087-host-http.sh` with sequenced faults for an exact path,
sticky final faults for exhaustion, 502/504 and typed reset support. Consume host
sequences through fixture files because curl runs in command substitution.
Add the two methods named in CP-1 to
`tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs` and
`tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`, respectively, using
the existing fixtures and `ParallelLimiter<ProcessSpawnLimit>`.
Commit/push before running CP-1 against unchanged production readers.

**S2 — implementation and documentation, 20 minutes.** Change only the readers
and wrapper opt-in calls in `scripts/deploy-server2.ps1` and
`scripts/c590-remote.sh`. Record receipts before deciding return/retry/refuse;
never pass a failed response to the JSON/shape validators. Preserve both successful
census passes and all existing drift/binding/land checks. Update timeout expectations
in `C1087_Read_failures_name_their_cause` and
`C1087_Host_census_filters_and_names_cause` (including its cleanup caller); project
reads still expect Timeout, task exhaustion expects Http with failure=Timeout,
and receipt attempt outcomes remain Timeout. Update `docs/docker-stack.md:255`
to distinguish attempt budget, retry envelope and final diagnosis, retaining the
existing CARD-1371 paragraph. Commit/push, then CP-2 once (budget 15 minutes).
Allow five minutes for evidence/report and cleanup; report actual overruns.

## Follow-up: audit reuse

Commission separately only after demonstrating durable proof that the **exact
protected state remains unchanged from the completed audit through deletion**:
work and registered runner-tmp bytes, indexes, ignore rules, refs/reflogs/pseudorefs,
repository discovery, metadata, path topology and volume generations, including
every possible writer across crashes/resumes. Remote publication must still be
freshly valid; task, runner, routing/drain, land and no-reference gates remain fresh.
Same HEAD, volume identity, timestamps or `containersRemoved` are insufficient.
Measure/prove writer exclusion and crash continuity before designing a reusable
audit receipt; no audit/journal shortcut belongs in this Code slice.

## Verification design

**V-1/V-2, wrapper/host:** one red-first witness per side. Seed one stable off-runner
Blocked task with matching detail and explicit null land proof. Stub **502 on its
first detail GET, then valid 200**; assert census success with both passes, exactly
three detail requests overall (failed attempt + recovered pass one + pass two),
unchanged list-read count, and receipt outcomes Http/ok/ok without body sentinels.
At S1 both methods must fail the expected-success assertion because production
refuses on the first 502. A fixture/compile error or zero tests is not the red.

In those same two methods, after the first witness, exercise recovery for each
other allowed failure, each list kind (including an excluded scope), third-attempt
success, and exhaustion at exactly three calls/two sleeps. Capture requested sleep
durations without wall-clock delays: use the existing wrapper fake-HTTP clock seam
only with the fake transport, and a fixture-local host sleep function. For reset,
inject the typed wrapper exception and curl 55/56; do not teach production a string
message predicate. Keep host `ExerciseHttpClassifier=true`.

Include immediate 400/401/404/408/429, nonallowlisted 500, unknown transport,
empty/malformed JSON and malformed envelope/detail controls (one call, no sleep),
plus 502 followed by malformed data (two calls, no third attempt). A recovered
response exposing bound work or invalid land proof must still refuse. Assert no
phase/destructive continuation after terminal failure, no error/body carried into
a later success, no extra outer drift/land retries, and redacted attempt receipts.
Use direct census invocation in the host fixture to avoid repeated audit setup.

**R-1:** the two existing C1087 classifier methods cover preserved error/shape,
project-resolution and cleanup behavior. **R-2:** existing
`C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts` covers
both drift sites and their original three-attempt/30-second policy. **R-3:** existing
`Recycle_census_preflight_is_documented` protects the runbook front door. Internal
vectors are assertions, not additional TUnit results.

Use `tools/Antiphon.Checkpoints` with `run --plan <this-path> --rows CP-1` at S1,
then `--rows CP-2` at S2, each with `--expected-source-sha <committed-HEAD>`.
Use the available built tool (`dotnet run --no-build --project tools/Antiphon.Checkpoints -- ...`);
if absent, report setup and build only that tool through `scripts/build-slot.ps1`
into a fresh owned alternate output. Checkpoint drivers acquire their own slots;
never hold an outer slot while waiting for them. Await every run; on 75 call the
same run's `wait --max-wait 50s` until terminal. Exit 4 is blocked, never an unleased
retry. Do not edit source during a run. Record unedited CHECKPOINT lines, exact
SHAs, roster/counts and expected red assertions; keep generated evidence ignored.
Run `scripts/check-evidence-diff.ps1` over task base..final HEAD and remove only
owned alternate outputs after all children finish. Ordinary Review uses CP-2 on
the final tip; CP-1 is historical red evidence, not a rerun against fixed code.

### Checkpoints

Closed list: **one red run containing two independent witnesses, one narrow green
filter**. Both rows use the **native Linux offline lane**; all named methods are
unparameterized. Each OR operand's trailing star is TUnit discovery compatibility;
require the exact two/six-method roster in fresh TRX, not arbitrary prefix matches.
CP-1 intentionally returns failed tests; report that outcome, never call it green.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1374-red/` | linux-census-http-red | `/*/*/(RollingVolumeRecycleScriptTests*)\|(RemoteScriptContractTests*)/(C1374_Wrapper_census_recovers_transient_reads*)\|(C1374_Host_census_recovers_transient_reads*)` | V-1, V-2 | exactly 2 executed, both fail expected recovery assertion, 0 skipped | 2 | 5 |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c1374-green/` | linux-census-http-green | `/*/*/(RollingVolumeRecycleScriptTests*)\|(RemoteScriptContractTests*)\|(DockerStackDocumentationTests*)/(C1374_Wrapper_census_recovers_transient_reads*)\|(C1374_Host_census_recovers_transient_reads*)\|(C1087_Read_failures_name_their_cause*)\|(C1087_Host_census_filters_and_names_cause*)\|(C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts*)\|(Recycle_census_preflight_is_documented*)` | V-1, V-2, R-1, R-2, R-3 | exactly 6 executed, 6 passed, 0 failed/skipped | 6 | 15 |
