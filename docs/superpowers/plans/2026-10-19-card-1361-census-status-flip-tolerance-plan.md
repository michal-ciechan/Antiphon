# CARD-1361: tolerate open-state status changes in redeploy censuses

Date: 2026-10-19. Plan task: `6af5a239-b10c-4133-a8ff-4472e20ec9de`.
Branch: `feat/card-task-6af5a239`.
Source inspected: CARD-1353 reviewed tip `cd5e2c5fb1c94307f5b226a52cb0eb86cca9f515`.
Card: `3579a748-f6a3-4d8f-8cef-4446e1ba803f`, Antiphon board, revision 0.

## Code brief override (CARD-1371, task 38f082ad)

The caller supersedes D-11's original diagnostic-only/no-retry choice below.
Cross-read count drift restarts the complete two-pass census, at most three
attempts with 30 seconds between attempts. Only persistent drift refuses with
`cause=Drift`, project, expected and observed counts. Single-envelope malformation
still refuses immediately. Binding/land guards take precedence; no partial closure
is accepted. The separate bound-land wait still retries only bound lands.

V-5's two comparison sites now cover recovery on the second attempt, refusal
after three attempts, stable nonzero counts, malformed counts/totals/projects and
bound companions, independently in both languages. Its persistent case also calls
the actual land-wait wrapper to prove that exhausted Drift receives no further
land-wait retry. PC-77/a and PC-78/a retain that outer-wait guard, with three census
attempts/two internal sleeps replacing the superseded one-read/no-sleep oracle.
All PC-1/a through PC-81/a remain pending SourceLanding Mutation; the binder must
also discover controls for the newly authorized bounded-recovery policy.

The five named red methods were committed before production changes. The exact
14-method CP-1 roster remains the ordinary scope; invoke it once with
`--row-timeout 40m --expected-source-sha <committed-HEAD>`. Native Linux, no host
pin, master-only, no live rollout actions. Historical no-retry statements and
estimates below describe the prior design, not this override.

## Outcome and scope

Permit an existing off-runner task to move among `Queued`, `Dispatched`, `Working`
and `Blocked` between list/detail reads and between the two passes of each census.
Both comparisons must preserve every other reduced task field and require explicit
null or terminal-proven land evidence. A changed runner, project, scope source,
land timestamp or land proof remains a refusal. Bound work remains a refusal.

Change only the PowerShell wrapper census, the shell host census, their offline
fixtures, and the census exception paragraph in `docs/docker-stack.md`. Preserve
CARD-1353's bound-only land snapshot, off-runner land handling, counts and bounded
land wait. This task is master-only; do not run any live `deploy-server2.ps1` phase,
restart a service, or change the rollout in progress.

TestDesign is separate: this dispatch was not commissioned to fold that stage.
The witness and checkpoint scope below are its inputs. TestDesign must add the
executable `## Verification design` and `### Checkpoints` manifest before Code.
No product-policy decision is needed to proceed under the card's stated scope.

## Ground truth

Line anchors describe the inspected source; function and method names are durable.

| What the card assumes | What the code does at the reviewed tip | Plan consequence |
|---|---|---|
| A Queued-to-Dispatched detail can be refused despite unchanged binding and land evidence. | `Assert-RecycleTaskCensus`, `scripts/deploy-server2.ps1:737-750`, skips status equality only for terminal detail with null `landRequest`; `c1008_tasks_collect`, `scripts/c590-remote.sh:5991-6007`, has the same two alternatives in jq. | The premise is confirmed. Add a third, narrowly qualified open-to-open alternative in each implementation. |
| A status-only move between passes is also refused. | `Test-RecycleForgivenDrift`, wrapper `:615-640`, only handles settled disappearance and no-land appearance. Host `:6053-6100` does likewise, then compares complete serialized snapshots. | Add same-ID status reconciliation to comparison copies, followed by the existing full comparison. |
| Pending land must not make a status flip safe. | CARD-1353 intentionally accepts and records **stable off-runner** pending lands, including detail states Queued/Held/Running/NeedsResolution. Unknown or unproven terminal states still fail. | Reject pending evidence as a qualification for the **new exception**; do not restore the old blanket pending-land refusal. |
| Every other reduced key agrees. | Both implementations retain `id`, `status`, `runnerId`, `projectId`, `scopeSource`, `landRequestedAt`, `landStartedAt`. Land evidence is separately retained as null or `{state, terminalEventId}`. | Compare the six non-status fields exactly; across passes, compare both explicit land entries exactly as well. |
| Bound work is already guarded. | The normal wrapper and host reject a bound open row before reading detail. Wrapper `-Report` deliberately bypasses that refusal to count bound work. | Keep guard order and reporting behavior. Explicitly exclude bound rows from the new tolerance, including report-mode comparisons. |
| Existing fixtures only need additions. | `C1324WrapperProbe("changed-row")` and `C1324HostCensusScript` currently use Queued-to-Working with no other change and expect Unstable (`RollingVolumeRecycleScriptTests.cs:1191-1197`, `:1322-1327`). | Replace that negative vector with a status change **plus a runner change** between two off-runner IDs. Add positive status-only witnesses separately. |
| Two-pass tolerance can be achieved by normalizing stored status. | Snapshots and receipts preserve list facts; `c1008_tasks_match_record` and `c994_status_proof` separately compare live facts with saved rollout records. | Do not normalize stored status or change journal comparisons. This card concerns one census's list/detail and two-pass boundaries. |
| Existing fixtures can exercise both scripts without a rollout. | C1317/C1319/C1324 extract shell functions and use sequenced fake HTTP; `C1008WrapperFixture` provides fake reads/details and trace/receipt inspection. `RemoteScriptContractTests.C1087_Host_census_filters_and_names_cause` exercises host census guards. | Use these seams and their process limiter. No real HTTP, Docker mutation or deployment is required for Code evidence. |
| Master publication changes the scripts used by the current rollout. | `docs/release-gates.md` distinguishes master publication from RC activation; product hosts and the rollout use the RC line. | Recommend a separately commissioned RC fix, with CARD-1353 dependency considered explicitly. Master-only completion does not fix the running rollout. |

## Decisions

| ID | Decision and reason | Rejected alternative |
|---|---|---|
| D-1 | The allowed status set is exactly `Queued/Dispatched/Working/Blocked`, case-sensitive as in the existing validators. Permit all directed moves, including Blocked-to-Dispatched and Dispatched-to-Blocked: this is equivalence for census safety, not enforcement of the task state machine. | Permit only forward progress; it would keep rejecting the decision/re-dispatch case named by the card. |
| D-2 | List/detail tolerance requires both statuses open, the row off the recycled runner, all six non-status reduced fields present and equal, and an explicit null or terminal-proven `landRequest`. Terminal proof means `Completed`, `Superseded` or `Canceled` with the existing lowercase GUID-shaped `terminalEventId`. | Ignore status before validating land evidence, or accept any non-pending string; either would admit unknown/malformed evidence. |
| D-3 | Across passes, qualify only IDs present in both open task maps, off the target runner in both observations, with different open statuses, equal non-status fields, and explicitly present, equal, safe land entries. Reconcile only that ID's status in the comparison copy. Keep full snapshot equality as the final gate. | Drop status from all snapshots, delete changed rows, or compare only selected safe keys; each would conceal other drift or rewrite observed evidence. |
| D-4 | An unchanged non-null land timestamp does not itself invalidate this exception when the required null/terminal proof is present; any timestamp addition, removal or value change fails non-status equality. Stable pending off-runner land remains governed by CARD-1353. Pending/unknown proof can never qualify a changed status. | Require every timestamp to be null, or refuse all off-runner pending lands; those would exceed the card and reverse CARD-1353 behavior. |
| D-5 | Preserve CARD-1317 terminal/null settlement, CARD-1319 settled disappearance, and CARD-1324/1326 no-land appearance restrictions. Apply status reconciliation alongside those exceptions and then compare the complete closure. Do not rewrite scope membership, excluded counts or land closure membership for a status flip. | Forgive a whole pass after finding one safe row; it could hide an unsafe companion change. |
| D-6 | Preserve refusal tokens: invalid list/detail qualification remains `RecycleLandUnknown`; residual pass differences remain `RecycleTaskCensusUnknown cause=Unstable`; earlier bound/land guards retain their current tokens and order. | Rename the misleading land token in this fix; callers and fixtures already depend on it, and diagnostic redesign is independent work. |
| D-7 | Preserve actual list status in reduced rows and receipts, and the actual second-pass snapshot returned by a successful census. Only temporary comparison data is aligned. Do not modify `c1008_tasks_match_record`, `c994_status_proof`, journal schema, retry policy, wait defaults or HTTP budgets. | Broaden tolerance between rollout phases or retry all Unstable failures; those are different observation windows and safety contracts. |
| D-8 | Four independent red-first witnesses: list/detail and pass comparison in each language. Commit and push tests before production changes, then one narrow green checkpoint after the implementation/docs group. | One aggregate assertion that can stop at the first failing branch, whole-class/Unit runs, or unmeasured broad testing; none supplies the required branch evidence within the budget. |
| D-9 | Code budget is 45-60 minutes, target 55, with offline Linux execution. Omit `-Runner`; use `-Platform Linux` for Code/Review because native host probes require it. Planning/TestDesign need no OS pin. | Embed a currently available host or force Windows plus WSL; placement can change during the rollout and Linux is the actual test requirement. |
| D-10 | Land the assigned work on master only. This is RC-worthy because it fixes routine continuous-dispatch rollout refusals without relaxing binding/land invariants. RC adoption requires a separate caller-owned task after review, checking whether CARD-1353 is present and adapting the fix to that line if necessary. | Push/cherry-pick to RC here, deploy master scripts, or promise the in-progress rollout is repaired by master publication. None is authorized. |

## Implementation design

### List/detail admission

In the wrapper, compute an `openStatusMove` qualification in
`Assert-RecycleTaskCensus` beside `settledWithoutLand`. Retain the required-member
loop. Skip only the `status` comparison when either the existing settlement rule
or the new fully qualified rule applies. All other keys still use the current
JSON equality check. The ordinary detail land validator, receipt update and
pending-land counters still execute. A missing `landRequest` is never null proof.

Use a small local PowerShell predicate for safe land evidence if shared by the
two comparison sites. It must not replace CARD-1353's ordinary validator: that
validator intentionally admits stable off-runner pending lands. Keep proof
equality separate from proof validity.

In the host's existing jq list/detail expression, add the equivalent third arm,
passing `C1008_RUNNER` as a jq argument to make the binding boundary explicit.
Keep the required summary members, exact equality arm, terminal/null arm and
subsequent land validator intact. A jq helper within the expression may define
open status and safe terminal proof; reuse the existing GUID grammar.

### Two-pass comparison

Extend `Test-RecycleForgivenDrift` with the recycled `RunnerId` supplied by
`Assert-RecycleTaskCensus`. After the existing gone/fresh handling, inspect IDs
in the intersection of the original task maps. Qualify each under D-3, and align
only `prior.open.tasks[id].status` with the live value in that parsed comparison
copy. Include a changed-status count in the existing early-return condition so
status-only drift reaches `Test-RecycleJsonEqual`. The original `$snapshot` and
receipt objects remain untouched.

In `c1008_tasks_collect`, add the equivalent jq reconciliation after the existing
gone/appeared blocks and before the final Unstable check. Require explicit
`has(id)` for both land maps; jq's missing-member-as-null behavior must not supply
evidence. Align eligible prior statuses to current statuses, preserving every
other field. The final byte comparison still rejects any remaining difference;
the success result remains the original current snapshot.

All validators remain before these exceptions. A row moving onto the recycled
runner must fail even if that requires the earlier bound guard instead of the
Unstable branch. Stable report-mode bound rows retain their count behavior;
changed bound rows do not acquire the new tolerance.

## Slices and test ownership

### S1: tests-only red witnesses and retained negative coverage

Files:

- `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs`: add
  `C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail`
  and `C1361_Wrapper_census_tolerates_open_status_changes_between_passes`, beside
  C1317/C1319/C1324. Reuse `C1319Row`, `C1319Detail`, `C1319Envelope`, sequenced
  `taskQueries`/`details`, and `C1008WrapperFixture`.
- `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`: add
  `C1361_Host_census_tolerates_open_status_changes_between_list_and_detail`
  and `C1361_Host_census_tolerates_open_status_changes_between_passes` beside
  `C1087_Host_census_filters_and_names_cause`. Extract the real census functions
  using existing `Block`/`LinuxShell` conventions and stub only HTTP/time inputs.
- In both C1324 `changed-row` fixture arms in `RollingVolumeRecycleScriptTests.cs`,
  change the pass-1 runner to a distinct off-runner ID along with the status.
  Keep their Unstable expectations. The old status-only rejection is superseded
  by CARD-1361; the non-status drift invariant remains covered.

Each new test's first positive vector must independently fail at S1 against the
unchanged scripts: list/detail returns `RecycleLandUnknown`; pass comparison
has matching list/detail within each pass but ends `cause=Unstable`. Run all four
exact methods individually against one isolated tests-only build; retain each
assertion failure and SHA. A missing helper, build failure, unavailable jq or
zero-test execution is not a red witness. Commit/push S1 before these runs.

TestDesign must bind these additional vectors to the four methods, retaining
per-vector outcomes so failures cannot be masked by a later PASS:

- All twelve unequal directed open-state pairs with null land evidence; identical
  status remains a baseline control. Use deterministic input sequences, no races.
- Completed/Superseded/Canceled with valid terminal event proof; at the pass
  boundary the proof must be identical in both observations.
- With status drift, independently change runnerId, projectId, scopeSource,
  landRequestedAt and landStartedAt. Use valid per-observation shapes where
  needed so pass cases reach the comparison instead of failing malformed input.
- Pending Queued/Held/Running/NeedsResolution proof, unknown state, missing
  `landRequest`, terminal proof missing/invalid event ID, changed terminal event
  ID and null-to-terminal proof between passes: all refuse the exception.
- Bound open rows, including wrapper report-mode drift; malformed/missing reduced
  keys; an unsafe companion change alongside a safe status move.
- Coexistence with a proven settled disappearance or safe no-land appearance;
  neither exception may erase a scope-count difference or a changed land entry.

Assert outcomes and evidence: exit/error token on refusal; latest list status,
unchanged binding and explicit land proof in the successful snapshot/receipt;
actual detail reads; no real host/deploy action. Keep subprocess tests under
`ParallelLimiter<ProcessSpawnLimit>`. Internal vector counts are separate from
the number of TUnit methods executed.

### S2: symmetric census change

Files: `scripts/deploy-server2.ps1` (`Assert-RecycleTaskCensus`,
`Test-RecycleForgivenDrift`, and an optional local safe-proof helper) and
`scripts/c590-remote.sh` (`c1008_tasks_collect` only).

Implement D-1 through D-7 together. Update nearby comments that currently say
every changed row refuses. Preserve ASCII PowerShell source, shell error handling,
full closure validation and CARD-1353's four land predicates/snapshot/wait logic.
Commit and push this meaningful production slice before the green run.

### S3: documentation and one narrow green

File: `docs/docker-stack.md`, volume recycling census exception paragraph (near
line 258 in the inspected tip). State both observation boundaries, the four-state
set, unchanged off-runner identity/land facts, explicit safe proof, and continuing
refusals. Clarify that stable off-runner pending lands still follow CARD-1353 and
cannot qualify the new status exception. Replace the blanket "changed row"
sentence with the precise remaining refusal boundary.

Review the existing pins in
`tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs`; adjust only
an assertion whose old wording conflicts with the new paragraph. No new prose-only
test is required. Commit/push S3, then execute the TestDesign manifest once against
the final committed group. Keep generated TRX, receipts and logs ignored.

## Checkpoint handoff to TestDesign

Read-only placement discovery completed on 2026-10-19 via
`GET /api/runner-defaults` (revision 2) and `GET /api/session-runners`. The catalogue
offers an eligible native Linux lane; availability is a snapshot, not a reservation.
Re-read both routes at dispatch. Do not encode a fleet location in the plan or pin
a host. Required tools are native bash, jq, pwsh and the repository .NET SDK.

| Proposed checkpoint | Lane | Build and selection | Expected evidence |
|---|---|---|---|
| CP-1, after all slices | Native Linux, offline; `-Platform Linux`, no `-Runner` | One isolated `tests/Antiphon.Tests -> bin-c1361/` build; one exact combined class/method filter selecting the four new C1361 methods and the seven regression methods listed below. | Exactly 11 named methods, nonzero execution, 0 failed/skipped, all expected vector outcomes and fresh TRX. TestDesign supplies the executable nine-column manifest and exact filter. |

Retained regression methods in that same narrow green:

1. `RollingVolumeRecycleScriptTests.C1317_Census_tolerates_a_task_that_settles_without_a_land`
2. `RollingVolumeRecycleScriptTests.C1319_Census_tolerates_a_pass0_settled_task_that_leaves_before_pass1`
3. `RollingVolumeRecycleScriptTests.C1324_Census_tolerates_a_no_land_task_that_appears_in_pass1`
4. `RollingVolumeRecycleScriptTests.C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance`
5. `RollingVolumeRecycleScriptTests.C1353_Host_census_passes_off_runner_lands_and_waits_for_bound_lands`
6. `RollingVolumeRecycleScriptTests.C1353_Wrapper_waits_for_a_bound_land_then_proceeds`
7. `RemoteScriptContractTests.C1087_Host_census_filters_and_names_cause`

The four red runs are explicitly required pre-fix witnesses outside the green
manifest. Build once into `bin-c1361-red/` at S1, then run each new method using
`/*/*/<Class>/<ExactMethod>` against that output. All build/test drivers use the
host build-slot gate. The green group uses the checkpoint tool and waits through
exit 75 to completion. Do not run an entire class, Unit lane, assembly, live census
or rollout phase. Delete all task-owned `bin-c1361*/` outputs safely before finishing.
Report retained checkpoint lines, actual method/vector counts, SHA and failures.

Budget: 8-12 minutes for S1 authoring; 8-12 for one red build plus four witnesses;
16-20 for both implementations and docs; 8-12 for the single green build/run;
5 minutes for review, cleanup and publication. Target 55 minutes, range 45-60.
If gate wait or unexpected failures exceed that budget, report the measured cause
and missing evidence; do not broaden testing or weaken assertions to compensate.

## Risks, limits and release disposition

- PowerShell and jq must agree on explicit null versus missing proof and on
  terminal event validity. Independent witnesses in both scripts guard this.
- Across-pass land equality includes terminal event identity, not just state;
  otherwise a new land could be hidden by a simultaneous status move.
- The final full-snapshot comparison is essential: nested excluded-count changes,
  additional unsafe rows and unexpected closure changes remain Unstable.
- Cross-phase saved-record comparisons still include status. A status change
  between those separate proofs may still refuse; measure phase, saved/live reduced
  fields and land proof before commissioning any extension to that boundary.
- This plan neither adds nor changes a session that waits for input. The CARD-1083
  release/deadline checklist does not apply; existing script polling is unchanged.
- **RC-worthy: yes.** The failure interrupts routine dispatch during rollout, and
  the fix preserves the safety predicates. **Assigned publication: master only.**
  Caller-owned RC adoption must account for CARD-1353 and qualify the resulting RC
  scripts. No live rollout result is claimed by this plan or its offline tests.

## Plan validation

The card, both production functions, adjacent fixtures, CARD-1353 plan and owner
documents were inspected. No tests/builds or deploy phases ran in this Plan task.
The assigned source is the required reviewed tip and the worktree was initially
clean. Before publication, validate whitespace, confirm the plan is the only
changed path, push the assigned branch, and check merge-tree against fetched master.


## CARD-1371 minimal addition (TestDesign, 2026-10-19)

Read CARD-1371 revision 0, card `df02cbd1-cff4-458b-b80b-e468e53aee80`,
board `8988ca03-7414-47ad-b0b6-51556c701703`. Its diagnosed sequence is a root
read containing six tasks, followed by a nested scope advertising five root
exclusions after one task completed. Both envelopes are individually valid.
This case is absent from the original plan. The following addition leaves
D-1 through D-10 and the implementation design above intact.

**D-11:** Take the card's explicitly permitted diagnostic alternative. A
cross-response count disagreement still refuses, now as
`RecycleTaskCensusUnknown cause=Drift field=excluded.byProject.count project=<guid> expected=<integer> observed=<integer>`.
Do not retry, accept a partial closure, normalize counts, or change land-wait
policy. `expected` is the first advertised count when two advertisements disagree;
for final reconciliation it is the advertised count and `observed` is the number
of distinct collected rows with that project. The project is the mismatching
project, not necessarily the last requested scope. Preserve a safe `path=` if
already available. Report the first mismatch in deterministic project order.

Apply this classification at **both** cross-response comparisons in each script:
repeated advertisements (`$scopeCounts.ContainsKey` / jq `has($id)`), and final
advertised-versus-collected reconciliation (wrapper near 763, host near 6008-6009
at the inspected source). Single-envelope shape, nonnegative integer totals,
strictly positive integer byProject counts, duplicate/self project IDs and the
sum-to-total check remain `cause=Malformed`. Preserve every row/detail/binding/land
check before these comparisons; `Drift` cannot reach a destructive action or
enter the bound-land retry loop. The new label describes inconsistent observations;
it does not prove which server-side event caused them.

S1 additionally adds one independently asserted C1371 method below, with both
language arms evaluated and reported even if the first fails. S2 makes the four
classification changes. S3 adds `Drift` to the documented causes and explains
single-response malformation versus disagreement between valid reads. The old
statements that counts always reconcile remain requirements for success. No live
phase, resume, retry-policy change or RC activation is authorized.

## Verification design

This is the executable verification addition to the landed fix design. Source
inspected: `c306d146d5db7ae5440c2462b146a5ff4ef0a0e7`. All new tests are proposed,
not already present. The manifest below supersedes the earlier **proposed**
11-method roster with 14 methods: four C1361, one C1371, seven retained script
regressions and two existing documentation checks. No production change or test
execution occurred during TestDesign; zero quick red proofs were used (limit one).

### Inspection

- Read `Assert-RecycleTaskCensus`, `Test-RecycleForgivenDrift`,
  `Remove-RecycleOpenIds`, `Test-RecycleJsonEqual`, `Wait-RecycleTaskCensus`
  in `scripts/deploy-server2.ps1`; host `c1008_tasks_collect`, `c1008_tasks`,
  `c1008_tasks_wait`, `c1008_tasks_match_record` and `c1008_http` in
  `scripts/c590-remote.sh` | list/detail -> V-1/V-2, pass comparison -> V-3/V-4,
  cross-read counts -> V-5, preserved binding/land/wait -> R-1 through R-7.
- Read all bodies and called census builders for C1317, C1319, C1324, C1326,
  both C1353 methods, `C1319Row/Envelope/Detail`, `C1317ShellFunction`,
  `C1008WrapperFixture` construction/Run/disposal, and the existing AST-extracted
  raw validator probe in `RollingVolumeRecycleScriptTests.cs` | R-1 through R-6,
  deterministic W-L/W-P recipes below. The two C1324 changed-row vectors currently
  contain status-only drift; S1 adds a distinct off-runner runnerId in pass 1.
- Read `RemoteScriptContractTests.C1087_Host_census_filters_and_names_cause`,
  `Block`, `Remote`, `LinuxShell`, and the C1008HostFixture constructor/Run;
  read `C994TaskVectors.Build`, `c1087-host-http.sh`, census/detail sequence
  handling in `c727-fake-http.ps1`, and `c1008-recycle-cases.json` | H-L/H-P,
  V-5/R-7. `taskQueries` is keyed by exact path; `details` by task ID; arrays
  consume entries and keep the final element. `LinuxShell` alone does not assert
  child exit, so every new shell probe must expose and assert its own exit/status.
- Read both census documentation tests in `DockerStackDocumentationTests.cs`
  and the volume-recycling paragraph in `docs/docker-stack.md` | R-8.
  Existing pins need no change for the planned prose; preserve them.
- Read testing/build checkpoint schema, CARD-0403 selectors, PC execution and
  mechanical-PC contract, orchestration stage order/D-2, and CARD-1371 full
  revision via `card.ps1 get 1371 -Board Antiphon` | execution rules below.
  No new file is required; the fixtures above are the nearest owners.

### Delivery inventory

No new or changed asynchronous delivery path. These scripts synchronously consume
filtered list/detail responses and write local census evidence. No producer,
queue, session-input handoff, recovery worker or recipient contract changes.
Task IDs join list/detail/snapshot facts; project IDs join count observations.
The local census receipt proves only observed facts and the census verdict, never
message delivery, task completion, publication or recipient receipt.

The HTTP fixtures substitute deterministic responses for the server. The fake
Docker/verify boundaries substitute for host effects. Extracted function/expression
probes below substitute inputs at the comparison boundary. They cannot prove real
HTTP scheduling, durable server state, Docker safety, RC activation, a queued
message's recovery or matching complete UserPrompt receipt. Those delivery claims
are excluded, so no busy/eligible-recipient or crash/enqueue recovery test is being
omitted from a changed delivery path. If Code introduces one, stop and extend this
design through recipient evidence before claiming completion.

### Proves it works now

Method aliases in the recipes and PC table resolve exactly as follows. Each new
method has `[Test]` and `ParallelLimiter<ProcessSpawnLimit>` and no Arguments,
Matrix or Repeat: **one TUnit execution per method**, regardless of internal cases.

| ID / alias | Behaviour and layer | Exact test method | Expected |
|---|---|---|---|
| V-1 / W-L | Wrapper list/detail admission, real function + offline wrapper | `RollingVolumeRecycleScriptTests.C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail` | Every eligible vector succeeds with unchanged list facts; each unsafe vector refuses at its named guard. |
| V-2 / H-L | Host list/detail admission, real extracted shell + jq | `RemoteScriptContractTests.C1361_Host_census_tolerates_open_status_changes_between_list_and_detail` | Same outcomes as W-L, host exit and error asserted explicitly. |
| V-3 / W-P | Wrapper two-pass comparison, real function + offline wrapper | `RollingVolumeRecycleScriptTests.C1361_Wrapper_census_tolerates_open_status_changes_between_passes` | Status-only moves pass; only comparison copies change; residual differences refuse. |
| V-4 / H-P | Host two-pass comparison, real shell + jq | `RemoteScriptContractTests.C1361_Host_census_tolerates_open_status_changes_between_passes` | Same outcomes as W-P; returned snapshot is the original pass-1 observation. |
| V-5 / C | Both validators' count classification, sequenced real census functions | `RollingVolumeRecycleScriptTests.C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts` | Both valid-response drift sites refuse as Drift with project/expected/observed; stable nonzero closure succeeds; malformed counts remain Malformed. |

**Recipes and reachability.** W-L repeats a Queued list with a Dispatched detail
in both passes; W-P serves Queued/Queued then Dispatched/Dispatched. H-L/H-P use
these exact schedules with per-path counters in `c1008_http`, extracting the real
`c1008_tasks_collect` and `c1008_tasks`; preserve real jq and receipt generation.
All rows use a fixed lowercase GUID, runner `desktop`, project null, scopeSource
None, both timestamps null and an explicitly present null landRequest unless
that case changes it. No wall-clock race is involved. Every case gets fresh
state, counters, receipts and trace. Wrapper sequences must cover both server2
and server2-temp report calls; assert the server2 receipt immediately, with the
last element stable for the second target. Do not accidentally consume a pass
from the other runner's report.

Batch the pure comparison vectors in one bounded child per method using the
existing PowerShell AST extraction pattern / shell Block extraction. Keep at
least the primary positive, pending-land negative, changed-runner negative and
bound-report negative on the full offline wrapper/census path. Assert exact
per-case results immediately or aggregate **all failures** before the final
Shouldly assertion; a later PASS cannot overwrite a prior failure. Shell output
must contain one uniquely named case result, no FAIL, and a final completion
marker; assert the census exit/error and parse snapshot/receipt JSON, never just
a PASS substring. Split shell batches below LinuxShell's existing 60-second cap;
do not widen fixture timeouts. All such batches remain inside the same method.

The following finite cases are required in **each** of the four C1361 methods
unless a boundary qualifier is stated. Case identifiers are assertion labels,
not extra TUnit executions. Code records the expanded internal case count.

| Case / variants | Recipe | Decisive oracle / classification |
|---|---|---|
| `open-pair/<from>/<to>` (12 unequal pairs) and `same/<state>` (4) | All directed pairs from Queued, Dispatched, Working, Blocked; explicit null proof; same-state baseline. | Exit 0, exact ID/runner/project/scope/timestamps; list/detail retains list status, pass comparison returns second list status. Positive acceptance. |
| `terminal/<state>` (3) | Completed, Superseded, Canceled with `44444444-4444-4444-4444-444444444444`; same proof across passes. | Exit 0; exact state/event in explicit land map and receipt. Positive acceptance. |
| `stable-stamp/<key>` (2) | Set one timestamp to `2020-01-01T00:00:00Z` in every observation, otherwise safe flip. | Exit 0; timestamp retained. D-4 acceptance. |
| `field/<key>` (6) | Independently change id, runnerId (desktop -> other), projectId, scopeSource, landRequestedAt, landStartedAt with a status flip. Use Task/Card with valid scoped envelopes for project/scope changes. | L: RecycleLandUnknown, exit 2. P: Unstable, exit 2. The comparator qualification probe below additionally proves no alignment occurred. Safety rejection. |
| `missing-summary/<key>` (7) | Remove each required reduced key from detail. | RecycleLandUnknown, exit 2; no completion or host action. Validation rejection. |
| `pending/<state>` (4) | Queued, Held, Running, NeedsResolution with null terminalEventId; L detail mismatches status; P list/detail match within each pass and both proofs are identical. | L: RecycleLandUnknown. P: Unstable (ordinary validation admits stable off-runner pending proof). Safety rejection. |
| `unsafe-proof/<variant>` (5) | Unknown state; missing landRequest; terminal state with missing, null or invalid event ID. | RecycleLandUnknown before reconciliation. Direct comparison probe also refuses unsafe qualification. Validation/safety rejection. |
| `proof-change/<variant>` (P only, 3) | Equal valid terminal state but different event GUID; null -> terminal; terminal -> null. | Unstable; both per-pass validators pass; preserve explicit maps. Safety rejection. |
| `bound/<variant>` | Ordinary bound open row; wrapper report with bound status drift; report with stable bound status; direct comparison probe with prior/current bound row. | Ordinary wrapper exit 2 / host code 4, RecycleBoundTasks before detail. Report drift refuses (L LandUnknown, P Unstable); stable report retains bound count. Direct qualification false. Safety rejection + report baseline. |
| `companion/<variant>` (P only, 5) | Safe flip plus proven settled disappearance; safe no-land appearance; changed companion runner; changed excluded-scope count with individually valid passes; changed land closure. | First two pass with real latest facts; other three Unstable. No full-pass forgiveness. Safety rejection. |
| `boundary/<variant>` (direct probe only) | Non-open prior/current statuses; bound prior/current; missing prior/current land key; unsafe equal proof; six unequal non-status fields. | Qualification must be false and prior status unchanged. Boundary defence, not HTTP reachability evidence. |

**Comparison-boundary probes (required, not optional mutation work).** Some guards
are masked by earlier validation or the final full comparison. A missing land-map
entry cannot be produced by a successful ordinary detail read, and deleting a
non-status equality condition alone can still leave the final compare red. The
same named method must therefore exercise the **actual** status-qualification /
reconciliation block separately. In PowerShell use the parsed function AST to
extract the block and its actual helper dependencies; in the host use the literal
jq reconciliation program extracted from the real function. Bind its unique
source context after Code; assert extraction count one. Supply prior/live maps,
execute the unmodified block, then assert the comparison-copy prior status is
still Queued for ineligible inputs (and is Dispatched for the safe companion).
Do not copy the predicate into test code or stub its Boolean result. Also assert
input JSON bytes unchanged. The public function tests above remain required.
For a new shared predicate, bind both call sites and test its real body. If Code
cannot isolate the real block without replacing logic, return `next: plan` for
a test seam; do not delete the obligation or declare an equivalent PC successful.

**C1371 recipe C.** A = `aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1`, B =
`bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb`, C =
`cccccccc-cccc-cccc-cccc-cccccccccccc`. Use scoped Task rows, unique IDs,
off-runner binding, explicit null land proof and internally valid envelopes.
Evaluate wrapper and host independently and collect both outcomes before failing.

| Case / variants | Exact schedule | Oracle / reachability / classification |
|---|---|---|
| `collected-count/<complete,appear>/<open,land>` | Read A with six rows, excluding B count 1; read B with one row, excluding A count 5 (completion) or 7 (appearance). Land variant uses terminal off-runner rows with valid timestamps. | At final reconciliation: exit 2, Drift, project=A, expected=5 or 7, observed=6; no Malformed. All local shape and detail checks pass. One count-drift witness family. |
| `advertised-count/<decrease,increase>/<open,land>` | A advertises B=1 then C=2 (queue B first); B later advertises C=1 or 3; totals match within each envelope. | At repeated-advertisement comparison: exit 2, Drift, project=C, expected=2, observed=1 or 3. No destructive action. Independent branch witness. |
| `stable-nonzero/<open,land>` | A has two rows and excludes B=1; B has one row and excludes A=2. | Two complete walks succeed, each nested scope read with the original filter; all three IDs and relevant land facts recorded. Count baseline. |
| `malformed-count/<zero,negative,fraction,string,null,missing>` | Put that value in one byProject entry; otherwise valid envelope. | exit 2, Malformed field=excluded.byProject.count, never Drift. For variants also violating total, field validation must remain first. |
| `malformed-total/<negative,fraction,string,null,missing,sum>` | Invalid total alone, or valid integer total differing from sum. | exit 2, Malformed field=excluded.total, never Drift. |
| `malformed-project/<self,duplicate,invalid>` | Positive counts and matching total, but self/duplicate/non-GUID excluded project. | exit 2, Malformed field=excluded.byProject.projectId. |
| `drift-bound-companion/<open,land>` | Before reaching the cross-read mismatch, include a target-bound open task or target-bound timestamped terminal task. | BoundTasks / LandInFlight wins; zero destructive actions. Count classification cannot bypass binding. |
| `drift-does-not-retry` | Invoke the actual wait wrapper with the valid-response mismatch; stub sleep only. | One failed census, no sleep, exit 2 and Drift unchanged. The ordinary wait only retries land. |

Both language arms assert the safe fields in the returned diagnostic and trace
the exact A/B/C/filter reads. The host receipt repeats its existing refusal
string. The wrapper receipt must retain the supporting read/row counts without
marking a valid response Malformed; it does not store arbitrary exception text. The fixture supplies the causal completion;
production can only classify inconsistent observations. This plan deliberately
claims diagnostic repair, not automatic progress through active churn.

**Red-first execution in Code.** Commit/push S1 with five new methods before any
production changes. One isolated gated build into `bin-c1361-red/`; run W-L, H-L,
W-P, H-P and C separately using `/*/*/<Class>/<ExactMethod>` (no wildcard,
no full-path OR). These are the only authorized pre-fix runs outside CP-1.
The first positive for each C1361 method must fail on exit/error acceptance:
L sees RecycleLandUnknown, P sees Unstable. C must retain independent wrapper and
host failures: actual Malformed versus expected Drift at the final count case.
Build/fixture errors, zero execution, skips or a failure in another guard do not
prove red. Four C1361 witnesses plus one dual-language C1371 witness, **five
TUnit executions**, are required. Reuse the red build only while source is S1;
freeze source during each run. Record exact SHA, exits, assertion labels and TRX.

### Guards the regression

- R-1: terminal/null list/detail settlement | `RollingVolumeRecycleScriptTests.C1317_Census_tolerates_a_task_that_settles_without_a_land`; both unknown/unproven cases refuse, both settled cases pass, latest list facts and explicit null proof remain.
- R-2: settled disappearance | `RollingVolumeRecycleScriptTests.C1319_Census_tolerates_a_pass0_settled_task_that_leaves_before_pass1`; open-vanish refuses Unstable; settled disappearance preserves companion and returned maps.
- R-3: safe appearance and residual binding drift | `RollingVolumeRecycleScriptTests.C1324_Census_tolerates_a_no_land_task_that_appears_in_pass1`; both changed-row cases now change runner as well as status and remain Unstable; proof-bearing/unknown appearance refuses.
- R-4: timestamp/land-closure appearance | `RollingVolumeRecycleScriptTests.C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance`; timestamp and bound closure reject, off-runner land remains accepted under CARD-1353.
- R-5: host bound-only land, proof and bounded wait | `RollingVolumeRecycleScriptTests.C1353_Host_census_passes_off_runner_lands_and_waits_for_bound_lands`; retain all seven named host outcomes, exact polls, bound refusal, receipt proof and saved-record negative.
- R-6: wrapper bounded wait | `RollingVolumeRecycleScriptTests.C1353_Wrapper_waits_for_a_bound_land_then_proceeds`; all three arms: clears once, expires without host case, zero refuses after exactly one land read.
- R-7: original host filters and causes | `RemoteScriptContractTests.C1087_Host_census_filters_and_names_cause`; retain 23 malformed-member vectors, 15 status/fault vectors, 31 C994TaskVectors cases and final timeout cleanup refusal. These are internal cases in **one** TUnit result. `withheld` still refuses; its cause may now be Drift. Do not change it to acceptance.
- R-8: census/wait documentation | `DockerStackDocumentationTests.Recycle_census_preflight_is_documented` and `DockerStackDocumentationTests.Lands_proceed_during_rollout_is_documented`; all current pins remain, including CARD-1353's no-land-freeze and bound-only snapshot language. These are **two** executions; no new prose-only test.

### Guard inventory

Every entry below names one enforcement boundary. W and H are separate guards,
even where their policy is symmetric. Variants of the same loop/predicate (each
field/state) are individually executed. Separately bypassable presence, validity,
equality, binding, count-shape and comparison guards have separate entries.
Repeated enforcement of the same ID equality within list/detail is one invariant:
the PC payload must bypass both checks or the other masks it; it may not bypass
any unrelated invariant. Unchanged HTTP transport, container ownership, volume,
Git publication and cross-phase journal guards are outside the changed boundary
and are not claimed newly qualified by these tests. None of the guards in scope
is exempted as redundant or left without a PC.

| Guard | Plan reference and safety-critical invariant | Positive control |
|---|---|---|
| G-1 | W: D-1/D-2: list/detail open-status qualification | PC-1 |
| G-2 | H: D-1/D-2: list/detail open-status qualification | PC-2 |
| G-3 | W: D-2: list/detail off-runner qualification | PC-3 |
| G-4 | H: D-2: list/detail off-runner qualification | PC-4 |
| G-5 | W: D-2: required reduced members | PC-5 |
| G-6 | H: D-2: required reduced members | PC-6 |
| G-7 | W: D-2: exact six non-status fields | PC-7 |
| G-8 | H: D-2: exact six non-status fields | PC-8 |
| G-9 | W: D-2: explicit landRequest presence | PC-9 |
| G-10 | H: D-2: explicit landRequest presence | PC-10 |
| G-11 | W: D-2/D-4: safe land state for changed status | PC-11 |
| G-12 | H: D-2/D-4: safe land state for changed status | PC-12 |
| G-13 | W: D-2: terminal event GUID proof | PC-13 |
| G-14 | H: D-2: terminal event GUID proof | PC-14 |
| G-15 | W: D-3: both pass statuses in the open set (prior) | PC-15 |
| G-16 | W: D-3: both pass statuses in the open set (current) | PC-16 |
| G-17 | H: D-3: both pass statuses in the open set (prior) | PC-17 |
| G-18 | H: D-3: both pass statuses in the open set (current) | PC-18 |
| G-19 | W: D-3: off-runner in both pass observations | PC-19 |
| G-20 | H: D-3: off-runner in both pass observations | PC-20 |
| G-21 | W: D-3: six non-status fields remain equal | PC-21 |
| G-22 | H: D-3: six non-status fields remain equal | PC-22 |
| G-23 | W: D-3: previous land-map entry explicitly present | PC-23 |
| G-24 | H: D-3: previous land-map entry explicitly present | PC-24 |
| G-25 | W: D-3: current land-map entry explicitly present | PC-25 |
| G-26 | H: D-3: current land-map entry explicitly present | PC-26 |
| G-27 | W: D-3/D-4: safe pass land state | PC-27 |
| G-28 | H: D-3/D-4: safe pass land state | PC-28 |
| G-29 | W: D-3: valid terminal event at pass boundary | PC-29 |
| G-30 | H: D-3: valid terminal event at pass boundary | PC-30 |
| G-31 | W: D-3: exact land proof equality across passes | PC-31 |
| G-32 | H: D-3: exact land proof equality across passes | PC-32 |
| G-33 | W: D-3/D-5: complete closure equality after reconciliation | PC-33 |
| G-34 | H: D-3/D-5: complete closure equality after reconciliation | PC-34 |
| G-35 | W: D-7: preserve actual observed status and receipt facts | PC-35 |
| G-36 | H: D-7: preserve actual observed status and receipt facts | PC-36 |
| G-37 | W: D-2/D-5: ordinary target-bound open row refuses before detail | PC-37 |
| G-38 | H: D-2/D-5: ordinary target-bound open row refuses before detail | PC-38 |
| G-39 | W: CARD-1353 D-1: bound timestamped land refuses | PC-39 |
| G-40 | H: CARD-1353 D-1: bound timestamped land refuses | PC-40 |
| G-41 | W: CARD-1353 D-1: bound pending detail refuses | PC-41 |
| G-42 | H: CARD-1353 D-1: bound pending detail refuses | PC-42 |
| G-43 | W: CARD-1353 D-2: land snapshot retains bound rows only | PC-43 |
| G-44 | H: CARD-1353 D-2: land snapshot retains bound rows only | PC-44 |
| G-45 | W: CARD-1353 D-5: stable off-runner pending proof is recorded and counted | PC-45 |
| G-46 | H: CARD-1353 D-5: stable off-runner pending proof is recorded and counted | PC-46 |
| G-47 | W: D-5/CARD-1317: settlement exception needs terminal status and null land | PC-47 |
| G-48 | H: D-5/CARD-1317: settlement exception needs terminal status and null land | PC-48 |
| G-49 | W: D-5/CARD-1319: disappearance requires pass-0 settlement proof | PC-49 |
| G-50 | H: D-5/CARD-1319: disappearance requires pass-0 settlement proof | PC-50 |
| G-51 | W: D-5/CARD-1324: appearance requires explicit null land proof | PC-51 |
| G-52 | H: D-5/CARD-1324: appearance requires explicit null land proof | PC-52 |
| G-53 | W: D-5/CARD-1326: appearance outside the compared land closure | PC-53 |
| G-54 | H: D-5/CARD-1326: appearance outside the compared land closure | PC-54 |
| G-55 | W: D-5/CARD-1326: appearing task has no landRequestedAt | PC-55 |
| G-56 | H: D-5/CARD-1326: appearing task has no landRequestedAt | PC-56 |
| G-57 | W: D-5/CARD-1326: appearing task has no landStartedAt | PC-57 |
| G-58 | H: D-5/CARD-1326: appearing task has no landStartedAt | PC-58 |
| G-59 | W: D-11: byProject count is a positive integer | PC-59 |
| G-60 | H: D-11: byProject count is a positive integer | PC-60 |
| G-61 | W: D-11: excluded.total is a nonnegative integer | PC-61 |
| G-62 | H: D-11: excluded.total is a nonnegative integer | PC-62 |
| G-63 | W: D-11: each envelope sum equals excluded.total | PC-63 |
| G-64 | H: D-11: each envelope sum equals excluded.total | PC-64 |
| G-65 | W: D-11: excluded project identity is valid, unique and not self (self) | PC-65 |
| G-66 | W: D-11: excluded project identity is valid, unique and not self (duplicate) | PC-66 |
| G-67 | W: D-11: excluded project identity is valid, unique and not self (invalid) | PC-67 |
| G-68 | H: D-11: excluded project identity is valid, unique and not self (self) | PC-68 |
| G-69 | H: D-11: excluded project identity is valid, unique and not self (duplicate) | PC-69 |
| G-70 | H: D-11: excluded project identity is valid, unique and not self (invalid) | PC-70 |
| G-71 | W: D-11: repeated advertised count mismatch refuses as Drift | PC-71 |
| G-72 | H: D-11: repeated advertised count mismatch refuses as Drift | PC-72 |
| G-73 | W: D-11: final advertised/collected mismatch refuses as Drift | PC-73 |
| G-74 | H: D-11: final advertised/collected mismatch refuses as Drift | PC-74 |
| G-75 | W: CARD-1353 D-3/D-4: bound land wait expires rather than proceeding | PC-75 |
| G-76 | H: CARD-1353 D-3/D-4: bound land wait expires rather than proceeding | PC-76 |
| G-77 | W: CARD-1353/D-11: only bound-land refusal is retried | PC-77 |
| G-78 | H: CARD-1353/D-11: only bound-land refusal is retried | PC-78 |
| G-79 | W: CARD-1353 D-2: land snapshot keeps every bound row | PC-79 |
| G-80 | H: CARD-1353 D-2: land snapshot keeps every bound row | PC-80 |
| G-81 | H: CARD-1353 D-4: host wait/poll bounds validated before reads | PC-81 |

Project identity clauses and prior/current status membership have distinct guard
and PC rows because they are independently bypassable. Every row is mandatory.
The non-status and required-key loops exercise every listed key;
masking duplicates are identified above, not silently counted as red.

### Positive controls

All rows are **recipes**, classification **higher-tier-required**, reason
**binding-missing / binding required after Code**. Variant `a` is the one
defect described. Prior/current and self/duplicate/invalid are separate PC IDs,
all with one payload variant a. Before Mutation the binder resolves literal payload/preimage/mutant
hashes, unique context and allowed file set at the confirmed landed O/L, the
exact assertion site, SDK/framework/arguments and fresh evidence paths under
`docs/mechanical-pc-contract.md`. No recipe is mechanically eligible yet.
Code must add the extra boundary assertions named in this table inside the
listed methods; they remain part of CP-1. No test-only copy of the policy qualifies.

Each row means: **break G-n by the compiling defect; expect the exact method
selected below red at the named assertion**. Payloads for W touch only
`scripts/deploy-server2.ps1`; H only `scripts/c590-remote.sh`. For C cases the
other language is a green companion. No mutation edits assertions or fixture
expectations. PowerShell parses and jq compiles; invalid syntax is not red.
Use the direct real-block oracle when a downstream equality/validator would mask
a defect, and retain public-path coverage separately. Code binds block extraction
and logs whether each case is public-path or injected-boundary.

| PC / variant | Compiling defect recipe | Detecting filter (exact method) | Case and decisive expected-red oracle | Reachability / masking control |
|---|---|---|---|---|
| PC-1 / a | W: replace the detail open-set membership test with true. | `/*/*/RollingVolumeRecycleScriptTests/C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail` | boundary/non-open-detail: qualification expected false, mutant true. | Use terminal detail with a valid terminal land proof (not null), so the existing terminal/null settlement arm cannot qualify. |
| PC-2 / a | H: replace the detail open-set membership test with true. | `/*/*/RemoteScriptContractTests/C1361_Host_census_tolerates_open_status_changes_between_list_and_detail` | boundary/non-open-detail: qualification expected false, mutant true. | Use terminal detail with a valid terminal land proof (not null), so the existing terminal/null settlement arm cannot qualify. |
| PC-3 / a | W: remove the new target-runner exclusion from status qualification. | `/*/*/RollingVolumeRecycleScriptTests/C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail` | bound/report-detail: expected refusal / qualification false, mutant status alignment. | W uses Report, bypassing only the ordinary bound refusal; H uses the actual qualification block with bound row input because the public bound guard is earlier. |
| PC-4 / a | H: remove the new target-runner exclusion from status qualification. | `/*/*/RemoteScriptContractTests/C1361_Host_census_tolerates_open_status_changes_between_list_and_detail` | bound/report-detail: expected refusal / qualification false, mutant status alignment. | W uses Report, bypassing only the ordinary bound refusal; H uses the actual qualification block with bound row input because the public bound guard is earlier. |
| PC-5 / a | W: remove the required-member check in the new qualification path. | `/*/*/RollingVolumeRecycleScriptTests/C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail` | boundary/missing-summary: expected qualification false, mutant true. | At the extracted boundary omit the same key from both objects, so null equality cannot mask the member check; execute all seven missing-key variants. Public missing-detail cases remain. |
| PC-6 / a | H: remove the required-member check in the new qualification path. | `/*/*/RemoteScriptContractTests/C1361_Host_census_tolerates_open_status_changes_between_list_and_detail` | boundary/missing-summary: expected qualification false, mutant true. | At the extracted boundary omit the same key from both objects, so null equality cannot mask the member check; execute all seven missing-key variants. Public missing-detail cases remain. |
| PC-7 / a | W: replace all non-status equality enforcement with true, including the redundant ID precheck. | `/*/*/RollingVolumeRecycleScriptTests/C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail` | field/<key>: expected qualification false, mutant true; public mismatch must refuse. | Use safe null proof and open statuses; test each of the six differing keys directly. Red is on the qualification oracle even if a later public equality check still refuses. |
| PC-8 / a | H: replace all non-status equality enforcement with true, including the redundant ID precheck. | `/*/*/RemoteScriptContractTests/C1361_Host_census_tolerates_open_status_changes_between_list_and_detail` | field/<key>: expected qualification false, mutant true; public mismatch must refuse. | Use safe null proof and open statuses; test each of the six differing keys directly. Red is on the qualification oracle even if a later public equality check still refuses. |
| PC-9 / a | W: remove has(landRequest) / property-presence enforcement. | `/*/*/RollingVolumeRecycleScriptTests/C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail` | unsafe-proof/missing: expected false or LandUnknown, mutant accepted. | Otherwise equal complete rows, safe status flip; missing property is deliberately distinguishable from explicit null at the extracted qualification boundary. |
| PC-10 / a | H: remove has(landRequest) / property-presence enforcement. | `/*/*/RemoteScriptContractTests/C1361_Host_census_tolerates_open_status_changes_between_list_and_detail` | unsafe-proof/missing: expected false or LandUnknown, mutant accepted. | Otherwise equal complete rows, safe status flip; missing property is deliberately distinguishable from explicit null at the extracted qualification boundary. |
| PC-11 / a | W: treat every non-null state as safe for the status exception. | `/*/*/RollingVolumeRecycleScriptTests/C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail` | pending/Running: expected qualification false, mutant true. | Pending proof is valid for ordinary off-runner CARD-1353 validation. Null and terminal positive companions still pass; unknown is checked separately in ordinary validation. |
| PC-12 / a | H: treat every non-null state as safe for the status exception. | `/*/*/RemoteScriptContractTests/C1361_Host_census_tolerates_open_status_changes_between_list_and_detail` | pending/Running: expected qualification false, mutant true. | Pending proof is valid for ordinary off-runner CARD-1353 validation. Null and terminal positive companions still pass; unknown is checked separately in ordinary validation. |
| PC-13 / a | W: replace the terminal-event validity predicate with true. | `/*/*/RollingVolumeRecycleScriptTests/C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail` | unsafe-proof/invalid-event: expected qualification false, mutant true. | Use Completed with invalid event and complete equal non-status facts. Probe the real qualification block before the ordinary land validator can mask the defect. |
| PC-14 / a | H: replace the terminal-event validity predicate with true. | `/*/*/RemoteScriptContractTests/C1361_Host_census_tolerates_open_status_changes_between_list_and_detail` | unsafe-proof/invalid-event: expected qualification false, mutant true. | Use Completed with invalid event and complete equal non-status facts. Probe the real qualification block before the ordinary land validator can mask the defect. |
| PC-15 / a | W: remove the open-set qualification of the selected side — only the prior clause. | `/*/*/RollingVolumeRecycleScriptTests/C1361_Wrapper_census_tolerates_open_status_changes_between_passes` | boundary/non-open-prior: expected unchanged prior status, mutant aligned. | Two variants (prior, current); direct maps bypass list filtering; safe equal proof and all non-status fields equal. |
| PC-16 / a | W: remove the open-set qualification of the selected side — only the current clause. | `/*/*/RollingVolumeRecycleScriptTests/C1361_Wrapper_census_tolerates_open_status_changes_between_passes` | boundary/non-open-current: expected unchanged prior status, mutant aligned. | Two variants (prior, current); direct maps bypass list filtering; safe equal proof and all non-status fields equal. |
| PC-17 / a | H: remove the open-set qualification of the selected side — only the prior clause. | `/*/*/RemoteScriptContractTests/C1361_Host_census_tolerates_open_status_changes_between_passes` | boundary/non-open-prior: expected unchanged prior status, mutant aligned. | Two variants (prior, current); direct maps bypass list filtering; safe equal proof and all non-status fields equal. |
| PC-18 / a | H: remove the open-set qualification of the selected side — only the current clause. | `/*/*/RemoteScriptContractTests/C1361_Host_census_tolerates_open_status_changes_between_passes` | boundary/non-open-current: expected unchanged prior status, mutant aligned. | Two variants (prior, current); direct maps bypass list filtering; safe equal proof and all non-status fields equal. |
| PC-19 / a | W: remove the target-runner exclusion from the pass qualification. | `/*/*/RollingVolumeRecycleScriptTests/C1361_Wrapper_census_tolerates_open_status_changes_between_passes` | boundary/bound: expected prior Queued unchanged, mutant Dispatched. | Set both row runners to target, retaining non-status equality. W additionally exercises Report. H boundary inputs avoid the earlier ordinary bound refusal. |
| PC-20 / a | H: remove the target-runner exclusion from the pass qualification. | `/*/*/RemoteScriptContractTests/C1361_Host_census_tolerates_open_status_changes_between_passes` | boundary/bound: expected prior Queued unchanged, mutant Dispatched. | Set both row runners to target, retaining non-status equality. W additionally exercises Report. H boundary inputs avoid the earlier ordinary bound refusal. |
| PC-21 / a | W: replace only non-status equality qualification with true. | `/*/*/RollingVolumeRecycleScriptTests/C1361_Wrapper_census_tolerates_open_status_changes_between_passes` | boundary/field/<key>: expected prior Queued, mutant Dispatched. | Probe reconciliation before final full equality, which would otherwise mask this defect. Six field inputs; no snapshot-output rewriting in the test. |
| PC-22 / a | H: replace only non-status equality qualification with true. | `/*/*/RemoteScriptContractTests/C1361_Host_census_tolerates_open_status_changes_between_passes` | boundary/field/<key>: expected prior Queued, mutant Dispatched. | Probe reconciliation before final full equality, which would otherwise mask this defect. Six field inputs; no snapshot-output rewriting in the test. |
| PC-23 / a | W: remove prior land-map has(id) / property check. | `/*/*/RollingVolumeRecycleScriptTests/C1361_Wrapper_census_tolerates_open_status_changes_between_passes` | boundary/missing-prior-proof: expected prior Queued, mutant Dispatched. | Delete only prior entry; live entry is explicit null; implicit missing-as-null equality and validity otherwise pass. |
| PC-24 / a | H: remove prior land-map has(id) / property check. | `/*/*/RemoteScriptContractTests/C1361_Host_census_tolerates_open_status_changes_between_passes` | boundary/missing-prior-proof: expected prior Queued, mutant Dispatched. | Delete only prior entry; live entry is explicit null; implicit missing-as-null equality and validity otherwise pass. |
| PC-25 / a | W: remove current land-map has(id) / property check. | `/*/*/RollingVolumeRecycleScriptTests/C1361_Wrapper_census_tolerates_open_status_changes_between_passes` | boundary/missing-current-proof: expected prior Queued, mutant Dispatched. | Delete only current entry; prior entry explicit null. Execute actual block before final equality can mask it. |
| PC-26 / a | H: remove current land-map has(id) / property check. | `/*/*/RemoteScriptContractTests/C1361_Host_census_tolerates_open_status_changes_between_passes` | boundary/missing-current-proof: expected prior Queued, mutant Dispatched. | Delete only current entry; prior entry explicit null. Execute actual block before final equality can mask it. |
| PC-27 / a | W: admit identical pending proof as safe status-change evidence. | `/*/*/RollingVolumeRecycleScriptTests/C1361_Wrapper_census_tolerates_open_status_changes_between_passes` | pending/Running: expected Unstable or unchanged prior, mutant success/aligned. | Both passes internally agree list/detail and use the same Running/null proof, admitted by ordinary CARD-1353 validation. |
| PC-28 / a | H: admit identical pending proof as safe status-change evidence. | `/*/*/RemoteScriptContractTests/C1361_Host_census_tolerates_open_status_changes_between_passes` | pending/Running: expected Unstable or unchanged prior, mutant success/aligned. | Both passes internally agree list/detail and use the same Running/null proof, admitted by ordinary CARD-1353 validation. |
| PC-29 / a | W: ignore terminalEventId validity when qualifying terminal proof. | `/*/*/RollingVolumeRecycleScriptTests/C1361_Wrapper_census_tolerates_open_status_changes_between_passes` | boundary/invalid-terminal: expected prior Queued, mutant Dispatched. | Equal invalid terminal proof on both sides, complete maps. Direct boundary bypasses the ordinary detail rejection; equal safe proof is a green companion. |
| PC-30 / a | H: ignore terminalEventId validity when qualifying terminal proof. | `/*/*/RemoteScriptContractTests/C1361_Host_census_tolerates_open_status_changes_between_passes` | boundary/invalid-terminal: expected prior Queued, mutant Dispatched. | Equal invalid terminal proof on both sides, complete maps. Direct boundary bypasses the ordinary detail rejection; equal safe proof is a green companion. |
| PC-31 / a | W: remove equality of prior/current land entries. | `/*/*/RollingVolumeRecycleScriptTests/C1361_Wrapper_census_tolerates_open_status_changes_between_passes` | proof-change/event: expected unchanged prior or Unstable, mutant alignment. | Both independently valid Completed proofs have distinct valid event GUIDs. Inspect normalization before final full equality masks it; also cover null/terminal directions. |
| PC-32 / a | H: remove equality of prior/current land entries. | `/*/*/RemoteScriptContractTests/C1361_Host_census_tolerates_open_status_changes_between_passes` | proof-change/event: expected unchanged prior or Unstable, mutant alignment. | Both independently valid Completed proofs have distinct valid event GUIDs. Inspect normalization before final full equality masks it; also cover null/terminal directions. |
| PC-33 / a | W: return success after one eligible status alignment, skipping residual full equality. | `/*/*/RollingVolumeRecycleScriptTests/C1361_Wrapper_census_tolerates_open_status_changes_between_passes` | companion/changed-runner: expected Unstable, mutant success. | Keep one safe flip and a second stable-status task with changed runner; each individual pass is valid and counts agree. Also assert changed scope and bound-land maps. |
| PC-34 / a | H: return success after one eligible status alignment, skipping residual full equality. | `/*/*/RemoteScriptContractTests/C1361_Host_census_tolerates_open_status_changes_between_passes` | companion/changed-runner: expected Unstable, mutant success. | Keep one safe flip and a second stable-status task with changed runner; each individual pass is valid and counts agree. Also assert changed scope and bound-land maps. |
| PC-35 / a | W: return/write the original first-pass snapshot instead of the original current snapshot. | `/*/*/RollingVolumeRecycleScriptTests/C1361_Wrapper_census_tolerates_open_status_changes_between_passes` | open-pair/Queued/Dispatched: expected latest status Dispatched, mutant Queued. | Use an untouched original previous value for this defect; the status-only case succeeds in both implementations and reaches returned snapshot and receipt assertions. |
| PC-36 / a | H: return/write the original first-pass snapshot instead of the original current snapshot. | `/*/*/RemoteScriptContractTests/C1361_Host_census_tolerates_open_status_changes_between_passes` | open-pair/Queued/Dispatched: expected latest status Dispatched, mutant Queued. | Use an untouched original previous value for this defect; the status-only case succeeds in both implementations and reaches returned snapshot and receipt assertions. |
| PC-37 / a | W: disable the ordinary bound-open refusal. | `/*/*/RollingVolumeRecycleScriptTests/C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail` | bound/ordinary: expected BoundTasks before any detail, mutant detail read or success. | Use equal open list/detail and explicit null proof so the new tolerance is unnecessary; all other validation passes. Assert exact bound token and zero detail reads. |
| PC-38 / a | H: disable the ordinary bound-open refusal. | `/*/*/RemoteScriptContractTests/C1361_Host_census_tolerates_open_status_changes_between_list_and_detail` | bound/ordinary: expected BoundTasks before any detail, mutant detail read or success. | Use equal open list/detail and explicit null proof so the new tolerance is unnecessary; all other validation passes. Assert exact bound token and zero detail reads. |
| PC-39 / a | W: disable bound timestamp-land refusal. | `/*/*/RollingVolumeRecycleScriptTests/C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts` | drift-bound-companion/land: expected LandInFlight, mutant Drift or success. | Terminal Succeeded row is only in land closure, so open-bound guard cannot mask it; a later count mismatch gives a distinct defective token. |
| PC-40 / a | H: disable bound timestamp-land refusal. | `/*/*/RollingVolumeRecycleScriptTests/C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts` | drift-bound-companion/land: expected LandInFlight, mutant Drift or success. | Terminal Succeeded row is only in land closure, so open-bound guard cannot mask it; a later count mismatch gives a distinct defective token. |
| PC-41 / a | W: disable target-bound pending-detail refusal. | `/*/*/RollingVolumeRecycleScriptTests/C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail` | boundary/bound-pending-detail: expected LandInFlight, mutant accepted. | Execute the actual ordinary detail-validation block with bound row and Running proof, after the separately tested bound-open gate. This is an explicitly injected boundary, not a reachable ordinary census claim. |
| PC-42 / a | H: disable target-bound pending-detail refusal. | `/*/*/RemoteScriptContractTests/C1361_Host_census_tolerates_open_status_changes_between_list_and_detail` | boundary/bound-pending-detail: expected LandInFlight, mutant accepted. | Execute the actual ordinary detail-validation block with bound row and Running proof, after the separately tested bound-open gate. This is an explicitly injected boundary, not a reachable ordinary census claim. |
| PC-43 / a | W: replace the bound-only land snapshot selector with all rows. | `/*/*/RollingVolumeRecycleScriptTests/C1361_Wrapper_census_tolerates_open_status_changes_between_passes` | companion/off-runner-land-appearance: expected success, mutant Unstable. | Two otherwise equal passes; an off-runner timestamped terminal land appears only in pass 1. Census receipt still records it. This extra P case is required. |
| PC-44 / a | H: replace the bound-only land snapshot selector with all rows. | `/*/*/RemoteScriptContractTests/C1361_Host_census_tolerates_open_status_changes_between_passes` | companion/off-runner-land-appearance: expected success, mutant Unstable. | Two otherwise equal passes; an off-runner timestamped terminal land appears only in pass 1. Census receipt still records it. This extra P case is required. |
| PC-45 / a | W: reject an ordinary off-runner Running proof unconditionally. | `/*/*/RollingVolumeRecycleScriptTests/C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail` | same/Blocked-pending: expected success and exact proof/counter, mutant LandUnknown. | Use unchanged status in list/detail and passes, avoiding the new exception. Add this explicit baseline to both L methods; do not weaken the changed-status pending refusal. |
| PC-46 / a | H: reject an ordinary off-runner Running proof unconditionally. | `/*/*/RemoteScriptContractTests/C1361_Host_census_tolerates_open_status_changes_between_list_and_detail` | same/Blocked-pending: expected success and exact proof/counter, mutant LandUnknown. | Use unchanged status in list/detail and passes, avoiding the new exception. Add this explicit baseline to both L methods; do not weaken the changed-status pending refusal. |
| PC-47 / a | W: treat terminal detail with non-null unproven land as settled safe and suppress its ordinary proof refusal. | `/*/*/RollingVolumeRecycleScriptTests/C1317_Census_tolerates_a_task_that_settles_without_a_land` | unproven: expected LandUnknown, mutant accepted. | One correlated defect in the settlement shortcut: bypass both of its proof enforcement sites, without changing general pending handling; host-unproven/wrapper-unproven supplies missing event proof. |
| PC-48 / a | H: treat terminal detail with non-null unproven land as settled safe and suppress its ordinary proof refusal. | `/*/*/RollingVolumeRecycleScriptTests/C1317_Census_tolerates_a_task_that_settles_without_a_land` | unproven: expected LandUnknown, mutant accepted. | One correlated defect in the settlement shortcut: bypass both of its proof enforcement sites, without changing general pending handling; host-unproven/wrapper-unproven supplies missing event proof. |
| PC-49 / a | W: treat every missing prior task as proven settled. | `/*/*/RollingVolumeRecycleScriptTests/C1319_Census_tolerates_a_pass0_settled_task_that_leaves_before_pass1` | open-vanish: expected Unstable, mutant success. | Pass-0 detail remains open with explicit null proof; pass-1 empty. Counts and root scope stay valid. |
| PC-50 / a | H: treat every missing prior task as proven settled. | `/*/*/RollingVolumeRecycleScriptTests/C1319_Census_tolerates_a_pass0_settled_task_that_leaves_before_pass1` | open-vanish: expected Unstable, mutant success. | Pass-0 detail remains open with explicit null proof; pass-1 empty. Counts and root scope stay valid. |
| PC-51 / a | W: allow a fresh task with non-null terminal proof into no-land appearance forgiveness. | `/*/*/RollingVolumeRecycleScriptTests/C1324_Census_tolerates_a_no_land_task_that_appears_in_pass1` | appears-with-landRequest: expected Unstable, mutant success. | Terminal proof is independently valid, timestamps null, outside land closure; no earlier proof rejection masks this gate. |
| PC-52 / a | H: allow a fresh task with non-null terminal proof into no-land appearance forgiveness. | `/*/*/RollingVolumeRecycleScriptTests/C1324_Census_tolerates_a_no_land_task_that_appears_in_pass1` | appears-with-landRequest: expected Unstable, mutant success. | Terminal proof is independently valid, timestamps null, outside land closure; no earlier proof rejection masks this gate. |
| PC-53 / a | W: remove fresh-ID exclusion by land-closure membership. | `/*/*/RollingVolumeRecycleScriptTests/C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance` | appears-in-land-closure-bound: expected refusal/Unstable, mutant forgiven. | W Report reaches comparison. H uses the actual appearance comparison block with bound land map to bypass earlier land refusal; add this boundary assertion inside R4 before its aggregate verdict. |
| PC-54 / a | H: remove fresh-ID exclusion by land-closure membership. | `/*/*/RollingVolumeRecycleScriptTests/C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance` | appears-in-land-closure-bound: expected refusal/Unstable, mutant forgiven. | W Report reaches comparison. H uses the actual appearance comparison block with bound land map to bypass earlier land refusal; add this boundary assertion inside R4 before its aggregate verdict. |
| PC-55 / a | W: remove only the fresh-task landRequestedAt null guard. | `/*/*/RollingVolumeRecycleScriptTests/C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance` | appears-with-land-timestamp: expected Unstable, mutant success. | Off-runner task with valid requested timestamp, explicit null detail proof and empty land closure; ordinary validation permits it. |
| PC-56 / a | H: remove only the fresh-task landRequestedAt null guard. | `/*/*/RollingVolumeRecycleScriptTests/C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance` | appears-with-land-timestamp: expected Unstable, mutant success. | Off-runner task with valid requested timestamp, explicit null detail proof and empty land closure; ordinary validation permits it. |
| PC-57 / a | W: remove only the fresh-task landStartedAt null guard. | `/*/*/RollingVolumeRecycleScriptTests/C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance` | appears-with-land-started: expected Unstable, mutant success. | Add the independent started-only variant to both R4 harnesses; off-runner, requested null, explicit null proof, empty land closure. |
| PC-58 / a | H: remove only the fresh-task landStartedAt null guard. | `/*/*/RollingVolumeRecycleScriptTests/C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance` | appears-with-land-started: expected Unstable, mutant success. | Add the independent started-only variant to both R4 harnesses; off-runner, requested null, explicit null proof, empty land closure. |
| PC-59 / a | W: bypass only the byProject count type/range predicate. | `/*/*/RollingVolumeRecycleScriptTests/C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts` | malformed-count/zero: expected Malformed count, mutant another cause or acceptance. | Set total=0 for zero; sum then agrees. Run negative/fraction/string/null/missing too; all validation assertions remain case-labelled. |
| PC-60 / a | H: bypass only the byProject count type/range predicate. | `/*/*/RollingVolumeRecycleScriptTests/C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts` | malformed-count/zero: expected Malformed count, mutant another cause or acceptance. | Set total=0 for zero; sum then agrees. Run negative/fraction/string/null/missing too; all validation assertions remain case-labelled. |
| PC-61 / a | W: bypass only total type/range validation. | `/*/*/RollingVolumeRecycleScriptTests/C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts` | malformed-total/fraction: expected Malformed total, mutant another cause or acceptance. | Direct envelope-validation block probe where subsequent sum check would mask a type defect; assert invalid total is rejected by that actual predicate. |
| PC-62 / a | H: bypass only total type/range validation. | `/*/*/RollingVolumeRecycleScriptTests/C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts` | malformed-total/fraction: expected Malformed total, mutant another cause or acceptance. | Direct envelope-validation block probe where subsequent sum check would mask a type defect; assert invalid total is rejected by that actual predicate. |
| PC-63 / a | W: remove the single-envelope sum-to-total check. | `/*/*/RollingVolumeRecycleScriptTests/C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts` | malformed-total/sum: expected Malformed total, mutant Drift or success. | Positive valid byProject count, distinct project, valid total differing by one; subsequent cross-read disagreement must not substitute for Malformed. |
| PC-64 / a | H: remove the single-envelope sum-to-total check. | `/*/*/RollingVolumeRecycleScriptTests/C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts` | malformed-total/sum: expected Malformed total, mutant Drift or success. | Positive valid byProject count, distinct project, valid total differing by one; subsequent cross-read disagreement must not substitute for Malformed. |
| PC-65 / a | W: disable the selected project validation predicate — only the self clause. | `/*/*/RollingVolumeRecycleScriptTests/C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts` | malformed-project/self: expected Malformed project, mutant another verdict. | Three variants, one per independently bypassable clause. Each uses valid positive counts and sum; execute actual envelope block if a subsequent traversal refusal would mask it. |
| PC-66 / a | W: disable the selected project validation predicate — only the duplicate clause. | `/*/*/RollingVolumeRecycleScriptTests/C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts` | malformed-project/duplicate: expected Malformed project, mutant another verdict. | Three variants, one per independently bypassable clause. Each uses valid positive counts and sum; execute actual envelope block if a subsequent traversal refusal would mask it. |
| PC-67 / a | W: disable the selected project validation predicate — only the invalid clause. | `/*/*/RollingVolumeRecycleScriptTests/C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts` | malformed-project/invalid: expected Malformed project, mutant another verdict. | Three variants, one per independently bypassable clause. Each uses valid positive counts and sum; execute actual envelope block if a subsequent traversal refusal would mask it. |
| PC-68 / a | H: disable the selected project validation predicate — only the self clause. | `/*/*/RollingVolumeRecycleScriptTests/C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts` | malformed-project/self: expected Malformed project, mutant another verdict. | Three variants, one per independently bypassable clause. Each uses valid positive counts and sum; execute actual envelope block if a subsequent traversal refusal would mask it. |
| PC-69 / a | H: disable the selected project validation predicate — only the duplicate clause. | `/*/*/RollingVolumeRecycleScriptTests/C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts` | malformed-project/duplicate: expected Malformed project, mutant another verdict. | Three variants, one per independently bypassable clause. Each uses valid positive counts and sum; execute actual envelope block if a subsequent traversal refusal would mask it. |
| PC-70 / a | H: disable the selected project validation predicate — only the invalid clause. | `/*/*/RollingVolumeRecycleScriptTests/C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts` | malformed-project/invalid: expected Malformed project, mutant another verdict. | Three variants, one per independently bypassable clause. Each uses valid positive counts and sum; execute actual envelope block if a subsequent traversal refusal would mask it. |
| PC-71 / a | W: restore the original Malformed label at repeated advertisement comparison. | `/*/*/RollingVolumeRecycleScriptTests/C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts` | advertised-count/decrease/open: expected Drift project=C expected=2 observed=1, mutant Malformed. | A queues B before C; B changes C count before C is visited. No local envelope malformation. |
| PC-72 / a | H: restore the original Malformed label at repeated advertisement comparison. | `/*/*/RollingVolumeRecycleScriptTests/C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts` | advertised-count/decrease/open: expected Drift project=C expected=2 observed=1, mutant Malformed. | A queues B before C; B changes C count before C is visited. No local envelope malformation. |
| PC-73 / a | W: restore the original Malformed label at final reconciliation. | `/*/*/RollingVolumeRecycleScriptTests/C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts` | collected-count/complete/open: expected Drift project=A expected=5 observed=6, mutant Malformed. | Both scopes read and validated; distinct rows count six. Host receipt repeats the diagnostic; wrapper read/row facts support it. |
| PC-74 / a | H: restore the original Malformed label at final reconciliation. | `/*/*/RollingVolumeRecycleScriptTests/C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts` | collected-count/complete/open: expected Drift project=A expected=5 observed=6, mutant Malformed. | Both scopes read and validated; distinct rows count six. Host receipt repeats the diagnostic; wrapper read/row facts support it. |
| PC-75 / a | W: replace expiration refusal with success. | `/*/*/RollingVolumeRecycleScriptTests/C1353_Wrapper_waits_for_a_bound_land_then_proceeds` | expires / wait-expires: expected nonzero LandInFlight with waited count, mutant success. | Persistent bound terminal land; fake clock/sleep makes expiration deterministic. Unrelated open and count guards pass. |
| PC-76 / a | H: replace expiration refusal with success. | `/*/*/RollingVolumeRecycleScriptTests/C1353_Host_census_passes_off_runner_lands_and_waits_for_bound_lands` | expires / wait-expires: expected nonzero LandInFlight with waited count, mutant success. | Persistent bound terminal land; fake clock/sleep makes expiration deterministic. Unrelated open and count guards pass. |
| PC-77 / a | W: retry a Drift refusal once as though it were bound land. | `/*/*/RollingVolumeRecycleScriptTests/C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts` | drift-does-not-retry: expected one census/no sleep, mutant second read/sleep. | Stub sleep and make second census succeed; no infinite loop. Execute actual wait function, assert unchanged Drift exit and first-read trace. |
| PC-78 / a | H: retry a Drift refusal once as though it were bound land. | `/*/*/RollingVolumeRecycleScriptTests/C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts` | drift-does-not-retry: expected one census/no sleep, mutant second read/sleep. | Stub sleep and make second census succeed; no infinite loop. Execute actual wait function, assert unchanged Drift exit and first-read trace. |
| PC-79 / a | W: replace the bound-row projection with an empty task map. | `/*/*/RollingVolumeRecycleScriptTests/C1361_Wrapper_census_tolerates_open_status_changes_between_passes` | boundary/land-snapshot-bound: expected bound task ID present, mutant absent. | Execute the real snapshot projection with a bound timestamped terminal row; ordinary census rejects before projection, so this is an injected boundary control. |
| PC-80 / a | H: replace the bound-row projection with an empty task map. | `/*/*/RemoteScriptContractTests/C1361_Host_census_tolerates_open_status_changes_between_passes` | boundary/land-snapshot-bound: expected bound task ID present, mutant absent. | Execute the real snapshot projection with a bound timestamped terminal row; ordinary census rejects before projection, so this is an injected boundary control. |
| PC-81 / a | H: replace the wait/poll positive-integer validation with true. | `/*/*/RollingVolumeRecycleScriptTests/C1353_Host_census_passes_off_runner_lands_and_waits_for_bound_lands` | wait-invalid-env: expected ContextInvalid and zero reads, mutant land read or another token. | Set wait=0 and poll=1 with persistent bound land as the existing fixture does; valid peer inputs are companions. |

Mutation runs every PC/variant after ordinary Code, Review and confirmed land:
clean baseline -> compiling defect -> intended assertion red -> exact restoration
-> fresh build -> green, using the method filter in its row for every phase.
Expected TUnit counts per phase are executed=1/skipped=0; baseline and restoration
passed=1/failed=0; mutant passed=0/failed=1, with the declared assertion/case only
(or the declared group of field-case assertions within that one method). An
additional unrelated failure is unexpected-red. Preserve each variant's evidence
and restoration hashes; no resets, commits or pushes from SourceLanding. Tests
are pending, not waived: Review judges the complete recipes before land; Mutation
completion requires **all PCs run**, including every listed variant.

Audit at this TestDesign handoff: **guards=81, mapped=81, missing=0,
duplicate PC maps=0**. Executed PCs=0 at TestDesign, as required by the stage
order; all 81 PCs are mandatory post-land obligations. No PC-green claim.

### Out of scope

- Live deploy/redeploy/check-census/resume, service restart, Docker mutation, RC
  adoption and confirmation of the running rollout. Only offline fixtures run;
  their phase-shaped inputs have fake HTTP/Docker endpoints and owned scratch.
- A new retry policy for CARD-1371, atomic server snapshots, or proof that a
  particular task caused production Drift. D-11 is the card's diagnostic option.
- Cross-phase saved-record status tolerance, publication audit, volumes/leases,
  general HTTP transport and process custody changes. Existing selected regression
  assertions remain required but this work does not requalify those entire owners.
- New async delivery, session parking/deadlines or compaction. No changed path.
- Whole classes, Unit lane, complete assembly, Windows/WSL qualification and
  benchmark claims. The native Linux selection is the requested ordinary scope.

### Checkpoints

The table is the closed ordinary scope for Code and ordinary Review. One native
Linux row, one isolated test build, one exact CARD-0403 class-and-method selector.
The method names are globally distinct within the three selected classes; the
class/method cross-product therefore selects exactly the roster above. Parentheses
and trailing stars are intentional discovery compatibility, not permission for
extra methods. In Markdown the literal OR pipes are escaped; the importer must
remove those escapes. Fresh TRX must show exactly 14 named methods/results,
14 passed, zero failed/skipped. Five methods are new; the nine existing methods
have no argument expansion. Internal vectors and subprocess calls do not raise
Min. Any added argument expansion requires an explicit roster/count amendment.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c1361-r1/` | census-status-and-count-drift | `/*/*/(RollingVolumeRecycleScriptTests*)\|(RemoteScriptContractTests*)\|(DockerStackDocumentationTests*)/(C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail*)\|(C1361_Wrapper_census_tolerates_open_status_changes_between_passes*)\|(C1361_Host_census_tolerates_open_status_changes_between_list_and_detail*)\|(C1361_Host_census_tolerates_open_status_changes_between_passes*)\|(C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts*)\|(C1317_Census_tolerates_a_task_that_settles_without_a_land*)\|(C1319_Census_tolerates_a_pass0_settled_task_that_leaves_before_pass1*)\|(C1324_Census_tolerates_a_no_land_task_that_appears_in_pass1*)\|(C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance*)\|(C1353_Host_census_passes_off_runner_lands_and_waits_for_bound_lands*)\|(C1353_Wrapper_waits_for_a_bound_land_then_proceeds*)\|(C1087_Host_census_filters_and_names_cause*)\|(Recycle_census_preflight_is_documented*)\|(Lands_proceed_during_rollout_is_documented*)` | V-1, V-2, V-3, V-4, V-5, R-1, R-2, R-3, R-4, R-5, R-6, R-7, R-8 | all 14 listed, 0 failed/skipped; exact roster in fresh TRX | 14 | 18 |

Use repository SDK 10.0.204 (global.json roll-forward policy), net9.0 and native
bash/jq/pwsh/node. Run the checkpoint tool against the committed S1-S3 group:

`dotnet run --no-build --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-19-card-1361-census-status-flip-tolerance-plan.md --rows CP-1`

This assumes the normal checkpoint-tool bootstrap is available; its own drivers
acquire build slots. If bootstrap is absent, report that setup explicitly, compile
only the checkpoint tool through `scripts/build-slot.ps1` into a fresh owned
`bin-c1361-tool/`, and invoke its net9.0 DLL directly. Never compile into a daemon's
bin directory or hold an outer build lease while waiting for the executor to
acquire its own lease. The tool bootstrap is setup, not an additional test group.
All ad hoc S1 red build/test drivers use the build-slot gate. No unleased retry
on exit 4. Await every run; on 75 use the same run's wait until terminal (short
wait intervals retain commentary). Do not finish the task with an executor alive.

Preserve original CHECKPOINT lines and exact executed roster, tested SHA, dirty=0,
sourceState=clean, buildSource=verified, counts, slot result and TRX paths in the
stored report. Code/Review also run the evidence-diff guard over base..HEAD.
No test execution happened in this TestDesign task. The single green row is a
required future Code result, not a claim inferred from source inspection. Remove
only task-owned isolated outputs under the documented custody/cleanup rules;
retain generated evidence ignored. No full-suite fallback.

### Cost

All numbers below are **estimated minutes**, not measured test durations. Queue
waits are additional and must be reported. Do not convert a vector count into time
or TUnit executions.

- Ordinary V/R floor (Code): **18** = CP-1 **18**, including one isolated test
  build (estimated 5) and the exact combined filter above (estimated 13).
- Required S1 evidence outside the green table: **10** = one shared red build
  **4** + five exact-method runs **1.2 each** (W-L, H-L, W-P, H-P, C). Each stops
  at its first decisive red unless C needs to collect both language outcomes.
- Code setup allowance: **1** (tool bootstrap/prerequisites). Code verification
  floor **29** = 1 + 10 + 18. With the original authoring allocation of **26**,
  dispatch target remains **55**; if the added boundary cases exceed it, report
  the measured cost and exact missing evidence, never weaken or omit a guard.
- PC execution floor (Mutation): **1053** = **81 exact-method PC cycles x 13**.
  Each PC/variant uses its table filter: baseline build/run **4**, defect edit
  and exact restore **1**, mutant build/run **4**, restored build/run **4**.
  There is one payload variant a per PC; field/state input variants execute
  within that method. Prior/current status and project-identity clauses have
  separate PC IDs, not hidden extra cycles. Binding/discovery/report setup
  **12** makes the Mutation floor **1065**. Final payload binding can refine
  these estimates but cannot silently remove cycles. This work is separately
  commissioned after land; it is not part of Code's 55-minute budget.
- Total verification floor across Code and Mutation: **1094** = setup **13**
  + S1 red **10** + ordinary V/R **18** + PC cycles **1053**. This excludes
  authoring and the separate ordinary Review analysis time. With Code authoring
  **26**, estimated work is **1120** before Review analysis and queue delays.
- Estimated saved builds: one CP-1 build instead of fourteen per-method builds
  saves **13 x 5 = 65**; one S1 red build instead of five saves **4 x 4 = 16**,
  total **81 minutes** under these estimates. No whole-suite timing saving is
  claimed. PC batching saving is conservatively **0**: most controls share
  source/method and cannot run in one mutated tree; binding may identify truly
  independent cross-language pairs later. All 81 controls remain required.

TestDesign handoff: the original fix design is preserved, CARD-1371 takes its
explicit Drift-diagnostic option, the ordinary roster is closed and the PC
recipes remain binding required after Code. Next stage: Code on native Linux,
master-only; ordinary Review then land, with all PCs on the post-land companion.

TestDesign validation: static checks confirmed the original plan is a byte-for-byte
prefix, the nine-column CP-1 schema, 9 existing plus 5 planned methods, and the
81 distinct G-to-PC mappings with no omissions or duplicates. `git diff --check`
passed. These checks validate the design artifact, not future test execution or
TUnit selector runtime behaviour. Tests/builds/quick-red proofs/deploy phases: 0.
